using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Promuse.Api.Infrastructure;
using Promuse.Contracts.Players;
using Promuse.Persistence;
using Promuse.Persistence.Entities;

namespace Promuse.Api.Players;

/// <summary>
/// Reads and writes the save.
///
/// 只有两个写 / Only two writes, and both are preferences the client legitimately
/// owns: the squad and the desktop character. Anything that grants or spends
/// belongs to Phase 3 and gets its own endpoint, because the client must not be
/// able to state what it now holds.
/// </summary>
public sealed class PlayerService(
    PromuseDbContext db,
    TimeProvider clock,
    IOptions<StaminaOptions> stamina)
{
    public const int SquadSize = 4;

    private TimeSpan PerPoint => TimeSpan.FromMinutes(stamina.Value.MinutesPerPoint);

    /// <summary>
    /// 弱 ETag / Weak, and deliberately so. The body is not byte-identical between
    /// two reads at the same version - serverTime moves and the stamina bar fills -
    /// but it is semantically the same resource. A strong ETag would be a lie.
    ///
    /// That the bar keeps filling under a 304 is fine: the response carries
    /// nextPointAt and fullAt precisely so the client can count down locally
    /// instead of polling for a number it can work out itself.
    /// </summary>
    public static string ETagFor(int stateVersion) => $"W/\"{stateVersion}\"";

    /// <summary>Accepts `W/"3"`, `"3"` and `3`, because clients get this wrong.</summary>
    public static bool TryParseETag(string? value, out int stateVersion)
    {
        stateVersion = 0;

        if (string.IsNullOrWhiteSpace(value)) return false;

        return int.TryParse(value.Trim().TrimStart('W', '/').Trim('"'), out stateVersion);
    }

    // ----------------------------------------------------------------- read

    public async Task<Outcome<PlayerState>> GetStateAsync(Guid accountId, CancellationToken ct)
    {
        Player? player = await LoadAsync(accountId, tracking: false, ct);

        if (player is null) return Outcome<PlayerState>.Fail(ApiProblems.Unauthorized());

        return Outcome<PlayerState>.Ok(ToDto(player, clock.GetUtcNow()));
    }

    // --------------------------------------------------------------- writes

    public async Task<Outcome<PlayerState>> SetSquadAsync(
        Guid accountId, IReadOnlyList<string?>? squad, string? ifMatch, CancellationToken ct)
    {
        Player? player = await LoadAsync(accountId, tracking: true, ct);

        if (player is null) return Outcome<PlayerState>.Fail(ApiProblems.Unauthorized());

        if (Guard(player, ifMatch) is { } refused) return refused;

        if (ValidateSquad(squad, player) is { Count: > 0 } errors)
            return Outcome<PlayerState>.Fail(ApiProblems.ValidationFailed(errors));

        foreach (SquadSlot slot in player.Squad)
        {
            slot.CharacterId = squad![slot.Slot];
        }

        return await CommitAsync(player, ct);
    }

    public async Task<Outcome<PlayerState>> SetDesktopCharacterAsync(
        Guid accountId, string? characterId, string? ifMatch, CancellationToken ct)
    {
        Player? player = await LoadAsync(accountId, tracking: true, ct);

        if (player is null) return Outcome<PlayerState>.Fail(ApiProblems.Unauthorized());

        if (Guard(player, ifMatch) is { } refused) return refused;

        if (characterId is not null && !player.Characters.Any(c => c.CharacterId == characterId))
        {
            return Outcome<PlayerState>.Fail(ApiProblems.ValidationFailed(
                new Dictionary<string, string[]>
                {
                    ["characterId"] = [$"'{characterId}' is not a character this player owns."],
                }));
        }

        player.DesktopCharacterId = characterId;

        return await CommitAsync(player, ct);
    }

    // ------------------------------------------------------------ internals

    /// <summary>
    /// The precondition, checked before any work. Returns null to proceed.
    /// </summary>
    private static Outcome<PlayerState>? Guard(Player player, string? ifMatch)
    {
        if (!TryParseETag(ifMatch, out int expected))
            return Outcome<PlayerState>.Fail(ApiProblems.PreconditionRequired());

        if (expected != player.StateVersion)
            return Outcome<PlayerState>.Fail(ApiProblems.StateConflict(player.StateVersion));

        return null;
    }

    /// <summary>
    /// 两道防线 / Two layers on purpose. The version check above refuses the
    /// common case cheaply; this catch covers the one it cannot - two writers
    /// that both read the same version and both passed the check. StateVersion is
    /// an EF concurrency token, so the UPDATE carries the original value in its
    /// WHERE and the loser changes no rows.
    /// </summary>
    private async Task<Outcome<PlayerState>> CommitAsync(Player player, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();

        player.StateVersion++;
        player.UpdatedAt = now;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();

            Player? current = await LoadAsync(player.AccountId, tracking: false, ct);

            return Outcome<PlayerState>.Fail(ApiProblems.StateConflict(current?.StateVersion ?? 0));
        }

        return Outcome<PlayerState>.Ok(ToDto(player, now));
    }

    private Task<Player?> LoadAsync(Guid accountId, bool tracking, CancellationToken ct)
    {
        IQueryable<Player> query = db.Players
            .Include(p => p.Characters)
            .Include(p => p.Items)
            .Include(p => p.Squad);

        if (!tracking) query = query.AsNoTracking();

        return query.FirstOrDefaultAsync(p => p.AccountId == accountId, ct);
    }

    private Dictionary<string, string[]> ValidateSquad(IReadOnlyList<string?>? squad, Player player)
    {
        var errors = new Dictionary<string, string[]>();

        if (squad is null || squad.Count != SquadSize)
        {
            errors["squad"] = [$"Must contain exactly {SquadSize} entries, using null for an empty slot."];

            return errors;
        }

        var problems = new List<string>();

        // 同一个人不能占两格 / One operator cannot hold two slots. Without this the
        // client can field the same character four times.
        var duplicates = squad
            .Where(id => id is not null)
            .GroupBy(id => id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key!);

        problems.AddRange(duplicates.Select(id => $"'{id}' appears in more than one slot."));

        problems.AddRange(squad
            .Where(id => id is not null && player.Characters.All(c => c.CharacterId != id))
            .Select(id => $"'{id}' is not a character this player owns."));

        if (problems.Count > 0) errors["squad"] = [.. problems];

        return errors;
    }

    private PlayerState ToDto(Player player, DateTimeOffset now)
    {
        var squad = new string?[SquadSize];

        foreach (SquadSlot slot in player.Squad)
        {
            if (slot.Slot >= 0 && slot.Slot < SquadSize) squad[slot.Slot] = slot.CharacterId;
        }

        return new PlayerState(
            PlayerId: player.AccountId,
            DisplayName: player.DisplayName,
            Level: player.Level,
            Exp: player.Exp,
            ExpToNextLevel: PlayerProgression.MaxExp(player.Level),
            Stamina: StaminaCalculator.Compute(
                player.Stamina,
                player.StaminaUpdatedAt,
                PlayerProgression.MaxStamina(player.Level),
                PerPoint,
                now),
            // Ordered so two reads of an unchanged player produce the same
            // document. Without it the row order is whatever the query planner
            // felt like, and a client diffing responses sees phantom changes.
            Characters: [.. player.Characters
                .OrderBy(c => c.CharacterId, StringComparer.Ordinal)
                .Select(c => new CharacterState(c.CharacterId, c.Elite, c.Level, c.Exp, c.Trust))],
            Squad: squad,
            DesktopCharacterId: player.DesktopCharacterId,
            Inventory: [.. player.Items
                .OrderBy(i => i.ItemId)
                .Select(i => new ItemStack(i.ItemId, i.Amount))],
            StateVersion: player.StateVersion,
            ServerTime: now);
    }
}
