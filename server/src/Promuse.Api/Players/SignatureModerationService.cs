using Microsoft.EntityFrameworkCore;
using Promuse.Api.Infrastructure;
using Promuse.Contracts.Moderation;
using Promuse.Contracts.Players;
using Promuse.Persistence;
using Promuse.Persistence.Entities;

namespace Promuse.Api.Players;

/// <summary>
/// 签名审核 / What moderators do about the line on a profile card: read the lines written most
/// recently, find one player, take a line down, lock a player out of writing another, lift a lock,
/// and read back everything that was done.
///
/// 一个事务 / Taking a line down is one transaction: the line goes, the lock is set, the mail is
/// sent and the history is written together, or none of them is. The player's row is locked for
/// it, so a line the player saves at the same moment either lands before - and is taken down - or
/// after, and is refused by the lock.
///
/// 版本要动 / Every change moves the player's state version, as any write to the save does: a
/// device holding the old one is told its copy is stale instead of writing over the moderator.
/// </summary>
public sealed class SignatureModerationService(PromuseDbContext db, TimeProvider clock)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;
    public const int MaxReasonLength = 200;
    public const int MaxLockDays = 365;

    /// <summary>How long the mail about a removal stays in the box.</summary>
    public const int MailDays = 30;

    // ------------------------------------------------------------------ reading

    /// <summary>
    /// With no query, the lines written most recently, newest first. With one, the players it names:
    /// "Name#0420" is that one player, anything else matches names containing it, whether or not
    /// they have a line - a moderator looking for someone to unlock needs to find them too.
    /// </summary>
    public async Task<Outcome<AdminSignatureList>> ListAsync(string? query, int limit, CancellationToken ct)
    {
        if (limit is < 1 or > MaxLimit)
        {
            return Outcome<AdminSignatureList>.Fail(ApiProblems.ValidationFailed(
                new Dictionary<string, string[]> { ["limit"] = [$"Must be between 1 and {MaxLimit}."] }));
        }

        IQueryable<Player> players = db.Players.AsNoTracking();
        string text = query?.Trim() ?? "";

        if (text.Length == 0)
        {
            players = players
                .Where(p => p.Signature != null)
                .OrderByDescending(p => p.SignatureUpdatedAt)
                .ThenBy(p => p.AccountId);
        }
        else if (TryParseNameAndTag(text, out string name, out int tag))
        {
            players = players.Where(p => p.DisplayName.ToLower() == name.ToLower() && p.Tag == tag);
        }
        else
        {
            // 字面匹配 / Matched literally: a % or _ typed into the box means itself.
            string pattern = "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            players = players
                .Where(p => EF.Functions.ILike(p.DisplayName, pattern, "\\"))
                .OrderBy(p => p.DisplayName)
                .ThenBy(p => p.Tag);
        }

        DateTimeOffset now = clock.GetUtcNow();

        var rows = await players
            .Take(limit)
            .Select(p => new
            {
                p.AccountId,
                p.DisplayName,
                p.Tag,
                p.Signature,
                p.SignatureUpdatedAt,
                p.SignatureLockedUntil,
                Removals = db.SignatureModerations.Count(m =>
                    m.AccountId == p.AccountId && m.Action == SignatureModerationAction.Removed),
            })
            .ToListAsync(ct);

        return Outcome<AdminSignatureList>.Ok(new AdminSignatureList([.. rows.Select(r => new AdminSignature(
            r.AccountId, r.DisplayName, r.Tag, r.Signature, r.SignatureUpdatedAt,
            LockOf(r.SignatureLockedUntil, now), r.Removals))]));
    }

    /// <summary>Everything moderators have done, newest first - for one player, or for everyone.</summary>
    public async Task<Outcome<SignatureModerationHistory>> HistoryAsync(Guid? playerId, int limit, CancellationToken ct)
    {
        if (limit is < 1 or > MaxLimit)
        {
            return Outcome<SignatureModerationHistory>.Fail(ApiProblems.ValidationFailed(
                new Dictionary<string, string[]> { ["limit"] = [$"Must be between 1 and {MaxLimit}."] }));
        }

        IQueryable<SignatureModeration> entries = db.SignatureModerations.AsNoTracking();
        if (playerId is { } id) entries = entries.Where(m => m.AccountId == id);

        var rows = await entries
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Take(limit)
            .Select(m => new
            {
                Entry = m,
                Name = db.Players.Where(p => p.AccountId == m.AccountId).Select(p => p.DisplayName).FirstOrDefault(),
                Tag = db.Players.Where(p => p.AccountId == m.AccountId).Select(p => p.Tag).FirstOrDefault(),
                By = db.Accounts.Where(a => a.Id == m.CreatedBy).Select(a => a.Username).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return Outcome<SignatureModerationHistory>.Ok(new SignatureModerationHistory([.. rows.Select(r =>
            new SignatureModerationEntry(
                r.Entry.Id, r.Entry.AccountId, r.Name ?? "", r.Tag, r.Entry.Action,
                r.Entry.RemovedSignature, r.Entry.Reason, r.Entry.LockedUntil, r.Entry.MailId is not null,
                r.By, r.Entry.CreatedAt))]));
    }

    // ------------------------------------------------------------------ acting

    /// <summary>
    /// 撤下 / Takes the player's line down, locks them out of writing another for as long as asked,
    /// tells them by mail if asked, and records it. A lock on a player with no line is allowed - a
    /// player who keeps writing the same thing may have just cleared it - but doing nothing is not.
    /// </summary>
    public async Task<Outcome<AdminSignature>> RemoveAsync(
        Guid moderatorId, Guid playerId, SignatureRemoveRequest request, CancellationToken ct)
    {
        string reason = request.Reason?.Trim() ?? "";
        var errors = new Dictionary<string, string[]>();

        if (reason.Length is 0 or > MaxReasonLength) errors["reason"] = [$"1-{MaxReasonLength} characters."];
        if (request.LockDays is < 0 or > MaxLockDays) errors["lockDays"] = [$"Between 0 and {MaxLockDays}."];
        if (errors.Count > 0) return Outcome<AdminSignature>.Fail(ApiProblems.ValidationFailed(errors));

        DateTimeOffset now = clock.GetUtcNow();
        DateTimeOffset? lockUntil = request.LockPermanently ? SignatureLock.Permanent
                                  : request.LockDays > 0 ? now.AddDays(request.LockDays)
                                  : null;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        Player? player = await LockAsync(playerId, ct);
        if (player is null) return Outcome<AdminSignature>.Fail(ApiProblems.PlayerNotFound());

        string? removed = player.Signature;

        if (removed is null && lockUntil is null)
        {
            return Outcome<AdminSignature>.Fail(ApiProblems.ValidationFailed(new Dictionary<string, string[]>
            {
                ["lockDays"] = ["This player has no signature to remove; choose a lock to lock them anyway."],
            }));
        }

        player.Signature = null;

        // 只会更长不会更短 / A new lock never shortens one already running: taking down a second
        // line for a day must not lift a month that is still to run. Lifting is what unlock is for.
        if (lockUntil is { } until && (player.SignatureLockedUntil is not { } held || held < until))
        {
            player.SignatureLockedUntil = until;
        }

        player.StateVersion++;
        player.UpdatedAt = now;

        DateTimeOffset? lockedAfter = LockOf(player.SignatureLockedUntil, now);

        MailMessage? mail = null;
        if (request.NotifyByMail)
        {
            mail = new MailMessage
            {
                Id = Guid.NewGuid(),
                ToAccountId = playerId,
                Title = "About your profile signature",
                Body = MailBody(removed is not null, reason, lockedAfter),
                Sender = "Rhodes Island",
                Attachments = "[]",
                SentAt = now,
                ExpiresAt = now.AddDays(MailDays),
                SentBy = moderatorId,
            };
            db.Mails.Add(mail);
        }

        db.SignatureModerations.Add(new SignatureModeration
        {
            AccountId = playerId,
            Action = SignatureModerationAction.Removed,
            RemovedSignature = removed,
            Reason = reason,
            LockedUntil = lockedAfter,
            MailId = mail?.Id,
            CreatedBy = moderatorId,
            CreatedAt = now,
        });

        // 举报一并结案 / Whatever was reported about this line is answered by its removal: the
        // open reports close, and each reporter is thanked.
        await SignatureReportService.CloseOpenAsync(db, playerId, SignatureReportResolution.Removed, reason,
                                                    moderatorId, now, ct);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return Outcome<AdminSignature>.Ok(await RowAsync(playerId, now, ct));
    }

    /// <summary>Lifts a lock before its time, and records it. A player who is not locked is refused.</summary>
    public async Task<Outcome<AdminSignature>> UnlockAsync(
        Guid moderatorId, Guid playerId, SignatureUnlockRequest? request, CancellationToken ct)
    {
        string? reason = string.IsNullOrWhiteSpace(request?.Reason) ? null : request!.Reason!.Trim();
        if (reason is { Length: > MaxReasonLength })
        {
            return Outcome<AdminSignature>.Fail(ApiProblems.ValidationFailed(
                new Dictionary<string, string[]> { ["reason"] = [$"At most {MaxReasonLength} characters."] }));
        }

        DateTimeOffset now = clock.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        Player? player = await LockAsync(playerId, ct);
        if (player is null) return Outcome<AdminSignature>.Fail(ApiProblems.PlayerNotFound());

        if (LockOf(player.SignatureLockedUntil, now) is null)
        {
            return Outcome<AdminSignature>.Fail(ApiProblems.ValidationFailed(
                new Dictionary<string, string[]> { ["playerId"] = ["This player's signature is not locked."] }));
        }

        player.SignatureLockedUntil = null;
        player.StateVersion++;
        player.UpdatedAt = now;

        db.SignatureModerations.Add(new SignatureModeration
        {
            AccountId = playerId,
            Action = SignatureModerationAction.Unlocked,
            Reason = reason,
            CreatedBy = moderatorId,
            CreatedAt = now,
        });

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return Outcome<AdminSignature>.Ok(await RowAsync(playerId, now, ct));
    }

    // ------------------------------------------------------------------ internals

    /// <summary>"Name#0420", as PlayerTag.Format writes it. The name may itself hold a '#': the last one splits.</summary>
    internal static bool TryParseNameAndTag(string text, out string name, out int tag)
    {
        name = "";
        tag = 0;

        int hash = text.LastIndexOf('#');
        if (hash <= 0 || hash == text.Length - 1) return false;

        string digits = text[(hash + 1)..];
        if (digits.Length > 4 || !digits.All(char.IsAsciiDigit)) return false;

        name = text[..hash].Trim();
        tag = int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
        return name.Length > 0;
    }

    /// <summary>The lock as it stands at <paramref name="now"/>: null once its day has passed.</summary>
    private static DateTimeOffset? LockOf(DateTimeOffset? until, DateTimeOffset now) =>
        until is { } value && value > now ? value : null;

    private Task<Player?> LockAsync(Guid playerId, CancellationToken ct) =>
        db.Players
            .FromSqlInterpolated($"""
                SELECT * FROM players
                WHERE account_id = {playerId}
                FOR UPDATE
                """)
            .FirstOrDefaultAsync(ct);

    private async Task<AdminSignature> RowAsync(Guid playerId, DateTimeOffset now, CancellationToken ct)
    {
        var row = await db.Players.AsNoTracking()
            .Where(p => p.AccountId == playerId)
            .Select(p => new
            {
                p.DisplayName,
                p.Tag,
                p.Signature,
                p.SignatureUpdatedAt,
                p.SignatureLockedUntil,
                Removals = db.SignatureModerations.Count(m =>
                    m.AccountId == p.AccountId && m.Action == SignatureModerationAction.Removed),
            })
            .FirstAsync(ct);

        return new AdminSignature(playerId, row.DisplayName, row.Tag, row.Signature, row.SignatureUpdatedAt,
            LockOf(row.SignatureLockedUntil, now), row.Removals);
    }

    /// <summary>What the player is told: what happened, why, and for how long they cannot write another.</summary>
    internal static string MailBody(bool removed, string reason, DateTimeOffset? lockedUntil)
    {
        string what = removed
            ? "Doctor,\n\nthe signature on your profile card has been removed by a moderator."
            : "Doctor,\n\na moderator has locked the signature on your profile card.";

        string lockLine = lockedUntil switch
        {
            null => "You can write a new one at any time.",
            { } until when SignatureLock.IsPermanent(until) => "You cannot write a new one until a moderator lifts the lock.",
            { } until => $"You can write a new one after {until.UtcDateTime:yyyy-MM-dd HH:mm} UTC.",
        };

        return $"{what}\n\nReason: {reason}\n\n{lockLine}";
    }
}
