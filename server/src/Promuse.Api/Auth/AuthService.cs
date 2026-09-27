using Microsoft.EntityFrameworkCore;
using Promuse.Api.Infrastructure;
using Promuse.Api.Players;
using Promuse.Contracts;
using Promuse.Contracts.Auth;
using Promuse.Persistence;
using Promuse.Persistence.Entities;

namespace Promuse.Api.Auth;

/// <summary>
/// Everything a sign-in means. The endpoints above this are thin on purpose -
/// they translate HTTP to a call and an outcome back to a status code, and none
/// of the rules live there.
/// </summary>
public sealed class AuthService(
    PromuseDbContext db,
    TokenService tokens,
    PasswordHasher passwords,
    TimeProvider clock)
{
    /// <summary>
    /// Verified against when no account matches, so that a missing username and
    /// a wrong password take the same time. Without this, "which usernames
    /// exist" is answerable with a stopwatch: Argon2 at 19 MiB is tens of
    /// milliseconds and a skipped lookup is not.
    /// </summary>
    private static readonly string DummyHash =
        new PasswordHasher().Hash("this value is never a real password");

    // --------------------------------------------------------------- guest

    /// <summary>
    /// Idempotent by device as well as by Idempotency-Key: a reinstall sends a
    /// new key for the same device, and it must not produce a second player.
    /// <c>Created</c> distinguishes 201 from 200 for the caller.
    /// </summary>
    public async Task<Outcome<AuthSession>> GuestSignInAsync(string deviceId, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();

        Account? account = await db.Accounts
            .FirstOrDefaultAsync(a => a.DeviceId == deviceId, ct);

        if (account is not null)
        {
            if (account.BannedAt is not null) return Outcome<AuthSession>.Fail(ApiProblems.AccountBanned());

            return Outcome<AuthSession>.Ok(await IssueSessionAsync(account, now, ct));
        }

        account = NewAccount(now);
        account.DeviceId = deviceId;

        db.Accounts.Add(account);
        db.Players.Add(NewPlayer(account, $"Doctor{now.ToUnixTimeMilliseconds() % 100000}", now));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();

            // Lost a race with another sign-in from the same device: the unique
            // index refused the second insert, which is exactly the outcome
            // wanted. Re-read and hand back the account that did win.
            Account? winner = await db.Accounts.FirstOrDefaultAsync(a => a.DeviceId == deviceId, ct);

            // 不是那个竞态就别吞 / Some other write failure. Rethrow rather than
            // reporting a successful sign-in that did not happen - a swallowed
            // DbUpdateException here would hand the caller tokens for an account
            // that was never stored.
            if (winner is null) throw;

            return Outcome<AuthSession>.Ok(await IssueSessionAsync(winner, now, ct));
        }

        return Outcome<AuthSession>.Ok(await IssueSessionAsync(account, now, ct), created: true);
    }

    // ------------------------------------------------------------ register

    public async Task<Outcome<AuthSession>> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();

        Account account = NewAccount(now);
        account.Username = request.Username;
        account.PasswordHash = passwords.Hash(request.Password);
        account.DeviceId = string.IsNullOrWhiteSpace(request.DeviceId) ? null : request.DeviceId;

        db.Accounts.Add(account);
        db.Players.Add(NewPlayer(account, request.Username, now));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // 唯一索引才是裁判 / The unique index is the arbiter, not a prior
            // SELECT. Checking first and inserting second leaves a window two
            // concurrent sign-ups both pass through; this cannot.
            db.ChangeTracker.Clear();

            return Outcome<AuthSession>.Fail(ApiProblems.UsernameTaken());
        }

        return Outcome<AuthSession>.Ok(await IssueSessionAsync(account, now, ct), created: true);
    }

    // --------------------------------------------------------------- login

    public async Task<Outcome<AuthSession>> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();

        Account? account = await db.Accounts
            .FirstOrDefaultAsync(a => a.Username == request.Username, ct);

        // Burn the same work on a miss as on a hit before answering.
        if (account?.PasswordHash is null)
        {
            passwords.Verify(request.Password, DummyHash);

            return Outcome<AuthSession>.Fail(ApiProblems.InvalidCredentials());
        }

        if (!passwords.Verify(request.Password, account.PasswordHash))
        {
            return Outcome<AuthSession>.Fail(ApiProblems.InvalidCredentials());
        }

        // 封号检查在验证之后 / Checked after the password, not before: answering
        // "banned" to an unverified guess tells a stranger that the account
        // exists and is worth attention.
        if (account.BannedAt is not null) return Outcome<AuthSession>.Fail(ApiProblems.AccountBanned());

        // The only moment the plaintext is in hand, so the only moment a
        // strengthened cost parameter can be applied.
        if (passwords.NeedsRehash(account.PasswordHash))
        {
            account.PasswordHash = passwords.Hash(request.Password);
            await db.SaveChangesAsync(ct);
        }

        return Outcome<AuthSession>.Ok(await IssueSessionAsync(account, now, ct));
    }

    // ---------------------------------------------------------------- link

    public async Task<Outcome<AuthSession>> LinkAsync(
        Guid accountId, RegisterRequest request, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();

        Account? account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);

        if (account is null) return Outcome<AuthSession>.Fail(ApiProblems.Unauthorized());

        if (account.Username is not null) return Outcome<AuthSession>.Fail(ApiProblems.NotAGuest());

        account.Username = request.Username;
        account.PasswordHash = passwords.Hash(request.Password);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();

            return Outcome<AuthSession>.Fail(ApiProblems.UsernameTaken());
        }

        // 不撤销旧令牌 / Existing tokens deliberately stay valid. The player id
        // has not changed and neither has the session - this is an upgrade of
        // the account, not a new sign-in, and signing them out mid-upgrade is
        // how progress feels lost even when it is not.
        return Outcome<AuthSession>.Ok(await IssueSessionAsync(account, now, ct));
    }

    // ------------------------------------------------------------- refresh

    /// <summary>
    /// Rotation with reuse detection.
    ///
    /// 竞态靠条件更新 / The race is handled by a conditional UPDATE rather than a
    /// read-then-write. Two requests arriving with the same token both see it
    /// active; the UPDATE that filters on <c>RevokedAt == null</c> succeeds for
    /// exactly one of them, and the other is told - correctly - that its token
    /// had already been rotated away. Without that filter both would mint a new
    /// token and the family would silently fork.
    /// </summary>
    public async Task<Outcome<TokenPair>> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();
        string hash = TokenService.HashRefreshToken(refreshToken);

        RefreshToken? existing = await db.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (existing is null) return Outcome<TokenPair>.Fail(ApiProblems.RefreshTokenInvalid());

        // Expiry first, so an expired token is simply dead rather than being
        // reported as a theft.
        if (existing.ExpiresAt <= now) return Outcome<TokenPair>.Fail(ApiProblems.RefreshTokenInvalid());

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        int claimed = await db.RefreshTokens
            .Where(t => t.Id == existing.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

        if (claimed == 0)
        {
            // Already rotated away, so whoever still holds this copy should not.
            // The server cannot tell the thief from the victim, so both go.
            await db.RefreshTokens
                .Where(t => t.FamilyId == existing.FamilyId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, (DateTimeOffset?)now), ct);

            await transaction.CommitAsync(ct);

            return Outcome<TokenPair>.Fail(ApiProblems.RefreshTokenReused());
        }

        Account? account = await db.Accounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == existing.AccountId, ct);

        if (account is null || account.BannedAt is not null)
        {
            await transaction.CommitAsync(ct);

            return Outcome<TokenPair>.Fail(
                account is null ? ApiProblems.RefreshTokenInvalid() : ApiProblems.AccountBanned());
        }

        (TokenPair pair, Guid mintedId) = await MintAsync(account.Id, existing.FamilyId, now, ct);

        // Completes the chain old -> new. Not required to detect reuse, which
        // the RevokedAt filter above already does, but it is what makes a
        // compromised family readable afterwards: the rotations are in order and
        // the fork is visible.
        await db.RefreshTokens
            .Where(t => t.Id == existing.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReplacedByTokenId, mintedId), ct);

        await transaction.CommitAsync(ct);

        return Outcome<TokenPair>.Ok(pair);
    }

    // -------------------------------------------------------------- logout

    /// <summary>
    /// Ends the session the token belongs to - the whole family, because the
    /// family IS the session on that device.
    ///
    /// 永远 204 / Always succeeds, even for a token that never existed. Reporting
    /// "no such token" would make this endpoint a way to test whether a stolen
    /// string is live, and there is nothing a caller could usefully do with the
    /// distinction anyway.
    /// </summary>
    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();
        string hash = TokenService.HashRefreshToken(refreshToken);

        RefreshToken? existing = await db.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (existing is null) return;

        await db.RefreshTokens
            .Where(t => t.FamilyId == existing.FamilyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, (DateTimeOffset?)now), ct);
    }

    // ------------------------------------------------------------ internals

    private async Task<AuthSession> IssueSessionAsync(Account account, DateTimeOffset now, CancellationToken ct)
    {
        // A fresh family per sign-in, so revoking one device leaves the others
        // signed in.
        (TokenPair pair, _) = await MintAsync(account.Id, Guid.NewGuid(), now, ct);

        return new AuthSession(account.Id, account.Username is null, pair, now);
    }

    private async Task<(TokenPair Pair, Guid TokenId)> MintAsync(
        Guid accountId, Guid familyId, DateTimeOffset now, CancellationToken ct)
    {
        (string access, int accessSeconds) = tokens.CreateAccessToken(accountId);
        (string refresh, string refreshHash) = TokenService.CreateRefreshToken();

        var row = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            TokenHash = refreshHash,
            FamilyId = familyId,
            CreatedAt = now,
            ExpiresAt = tokens.RefreshTokenExpiry(now),
        };

        db.RefreshTokens.Add(row);
        await db.SaveChangesAsync(ct);

        return (new TokenPair(access, TokenPair.Bearer, accessSeconds, refresh, tokens.RefreshTokenSeconds),
                row.Id);
    }

    private static Account NewAccount(DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = now,
    };

    /// <summary>
    /// 和客户端的初始化保持一致 / The starting roster and bag, matching
    /// <c>PlayerData.Initialization</c>: the four playable operators from the
    /// GDD's MVP roster, and the same three stacks. Kept in step with that method
    /// - a new player who does not own what the character select screen expects
    /// draws a cell with no data behind it.
    /// </summary>
    private static readonly string[] StartingRoster = ["AMIYA", "NOVA", "ECHO", "PULSE"];

    private static readonly (int Id, int Amount)[] StartingItems = [(0, 5), (1, 500), (2, 1000)];

    private static Player NewPlayer(Account account, string displayName, DateTimeOffset now)
    {
        var player = new Player
        {
            AccountId = account.Id,
            DisplayName = displayName,
            Level = 1,
            Exp = 0,

            // Starts full. PlayerProgression.MaxStamina(1) is 82, the same value
            // PlayerData.GetMaxReason(1) gives.
            Stamina = 82,
            StaminaUpdatedAt = now,
            StateVersion = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };

        foreach (string id in StartingRoster)
        {
            player.Characters.Add(new PlayerCharacter { AccountId = account.Id, CharacterId = id, Level = 1 });
        }

        foreach ((int id, int amount) in StartingItems)
        {
            player.Items.Add(new PlayerItem { AccountId = account.Id, ItemId = id, Amount = amount });
        }

        // 四行都要有, 即使全空 / All four rows exist from the start, even empty.
        // Creating them lazily on the first write would mean the squad endpoint
        // has to handle "no rows yet" as a separate case forever.
        for (int slot = 0; slot < PlayerService.SquadSize; slot++)
        {
            player.Squad.Add(new SquadSlot { AccountId = account.Id, Slot = slot, CharacterId = null });
        }

        return player;
    }
}
