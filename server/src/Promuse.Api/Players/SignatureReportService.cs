using Microsoft.EntityFrameworkCore;
using Npgsql;
using Promuse.Api.Infrastructure;
using Promuse.Contracts.Moderation;
using Promuse.Persistence;
using Promuse.Persistence.Entities;

namespace Promuse.Api.Players;

/// <summary>
/// 举报签名 / Players reporting the line on someone else's profile card, and the moderators' queue
/// of what was reported.
///
/// 一句话只报一次 / A player reports a line once: while their report waits, and after a moderator
/// has seen it, the same line cannot be reported by them again - only a changed one. With a daily
/// limit on top, one player cannot bury a line, or the queue, in reports.
///
/// 结案就致谢 / Closing a line's reports - by taking it down here or on the Signatures list, or by
/// dismissing them - thanks every reporter by mail, in the same transaction.
/// </summary>
public sealed class SignatureReportService(PromuseDbContext db, TimeProvider clock)
{
    public const int QueueLimit = 100;

    /// <summary>How long a thank-you mail stays in the box.</summary>
    public const int MailDays = 14;

    // ------------------------------------------------------------------ the player

    public async Task<Outcome<bool>> ReportAsync(
        Guid reporterId, Guid targetId, SignatureReportRequest request, CancellationToken ct)
    {
        string? note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var errors = new Dictionary<string, string[]>();

        if (!Enum.IsDefined(request.Reason)) errors["reason"] = ["Spam, Offensive or Other."];
        if (note is { Length: > SignatureReportLimits.NoteMaxLength })
        {
            errors["note"] = [$"At most {SignatureReportLimits.NoteMaxLength} characters."];
        }
        if (reporterId == targetId) errors["playerId"] = ["You cannot report your own signature."];
        if (errors.Count > 0) return Outcome<bool>.Fail(ApiProblems.ValidationFailed(errors));

        // 举报人的名字给管理员看 / The moderators' queue names who reported, so a reporter needs a name.
        if (!await db.Players.AnyAsync(p => p.AccountId == reporterId && p.DisplayName != null, ct))
        {
            return Outcome<bool>.Fail(ApiProblems.NameRequired());
        }

        // 封号的人不给看, 也就不给报 / A banned player's card is not shown to others, so it cannot be reported either.
        var target = await db.Players.AsNoTracking()
            .Where(p => p.AccountId == targetId && p.Account!.BannedAt == null)
            .Select(p => new { p.Signature })
            .FirstOrDefaultAsync(ct);

        if (target is null) return Outcome<bool>.Fail(ApiProblems.PlayerNotFound());

        if (target.Signature is null)
        {
            return Outcome<bool>.Fail(ApiProblems.ValidationFailed(new Dictionary<string, string[]>
            {
                ["playerId"] = ["This Doctor has no signature to report."],
            }));
        }

        DateTimeOffset now = clock.GetUtcNow();

        int today = await db.SignatureReports.CountAsync(r => r.ReporterId == reporterId && r.CreatedAt > now.AddDays(-1), ct);
        if (today >= SignatureReportLimits.PerDay)
        {
            return Outcome<bool>.Fail(ApiProblems.ReportLimitReached(SignatureReportLimits.PerDay));
        }

        bool seen = await db.SignatureReports.AnyAsync(r =>
            r.ReporterId == reporterId && r.TargetId == targetId
            && (r.ResolvedAt == null || r.Signature == target.Signature), ct);

        if (seen) return Outcome<bool>.Fail(ApiProblems.AlreadyReported());

        db.SignatureReports.Add(new SignatureReport
        {
            ReporterId = reporterId,
            TargetId = targetId,
            Signature = target.Signature,
            Reason = request.Reason,
            Note = note,
            CreatedAt = now,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // 两个同时到 / Two of the same report at once: the open-report index let one in.
            return Outcome<bool>.Fail(ApiProblems.AlreadyReported());
        }

        return Outcome<bool>.Ok(true);
    }

    // ------------------------------------------------------------------ the moderators

    /// <summary>Every player with open reports, the most reported first, each with their reports oldest first.</summary>
    public async Task<AdminReportQueue> QueueAsync(CancellationToken ct)
    {
        var open = await db.SignatureReports.AsNoTracking()
            .Where(r => r.ResolvedAt == null)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new
            {
                Report = r,
                Reporter = db.Players.Where(p => p.AccountId == r.ReporterId)
                    .Select(p => new { p.DisplayName, p.Tag }).FirstOrDefault(),
            })
            .ToListAsync(ct);

        List<Guid> targets = open
            .GroupBy(r => r.Report.TargetId)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Min(r => r.Report.CreatedAt))
            .Take(QueueLimit)
            .Select(g => g.Key)
            .ToList();

        DateTimeOffset now = clock.GetUtcNow();

        var players = await db.Players.AsNoTracking()
            .Where(p => targets.Contains(p.AccountId))
            .Select(p => new
            {
                p.AccountId,
                p.DisplayName,
                p.Tag,
                p.Signature,
                p.SignatureLockedUntil,
                Removals = db.SignatureModerations.Count(m =>
                    m.AccountId == p.AccountId && m.Action == SignatureModerationAction.Removed),
            })
            .ToDictionaryAsync(p => p.AccountId, ct);

        var queue = new List<AdminReportedPlayer>();
        foreach (Guid target in targets)
        {
            if (!players.TryGetValue(target, out var p)) continue;

            queue.Add(new AdminReportedPlayer(
                p.AccountId, p.DisplayName!, p.Tag!.Value, p.Signature,
                p.SignatureLockedUntil > now ? p.SignatureLockedUntil : null,
                p.Removals,
                [.. open.Where(r => r.Report.TargetId == target).Select(r => new AdminSignatureReport(
                    r.Report.Id, r.Report.ReporterId, r.Reporter?.DisplayName ?? "", r.Reporter?.Tag ?? 0,
                    r.Report.Reason, r.Report.Note, r.Report.Signature, r.Report.CreatedAt))]));
        }

        return new AdminReportQueue(queue);
    }

    /// <summary>The line is within the rules: closes its open reports, and thanks the reporters.</summary>
    public async Task<Outcome<int>> DismissAsync(
        Guid moderatorId, Guid targetId, SignatureReportDismissRequest? request, CancellationToken ct)
    {
        string? note = string.IsNullOrWhiteSpace(request?.Note) ? null : request!.Note!.Trim();
        if (note is { Length: > SignatureReportLimits.NoteMaxLength })
        {
            return Outcome<int>.Fail(ApiProblems.ValidationFailed(new Dictionary<string, string[]>
            {
                ["note"] = [$"At most {SignatureReportLimits.NoteMaxLength} characters."],
            }));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        int closed = await CloseOpenAsync(db, targetId, SignatureReportResolution.Dismissed, note, moderatorId,
                                          clock.GetUtcNow(), ct);

        if (closed == 0)
        {
            return Outcome<int>.Fail(ApiProblems.ValidationFailed(new Dictionary<string, string[]>
            {
                ["playerId"] = ["This player has no open reports."],
            }));
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Outcome<int>.Ok(closed);
    }

    /// <summary>
    /// Closes every open report on <paramref name="targetId"/>'s line as <paramref name="resolution"/>
    /// and queues a thank-you mail to each reporter. Locks the reports it closes; the caller saves
    /// and commits - the Signatures list calls this inside its own removal.
    /// </summary>
    internal static async Task<int> CloseOpenAsync(
        PromuseDbContext db, Guid targetId, SignatureReportResolution resolution, string? note,
        Guid moderatorId, DateTimeOffset now, CancellationToken ct)
    {
        List<SignatureReport> open = await db.SignatureReports
            .FromSqlInterpolated($"""
                SELECT * FROM signature_reports
                WHERE target_id = {targetId} AND resolved_at IS NULL
                FOR UPDATE
                """)
            .ToListAsync(ct);

        foreach (SignatureReport report in open)
        {
            var mail = new MailMessage
            {
                Id = Guid.NewGuid(),
                ToAccountId = report.ReporterId,
                Title = "Thank you for your report",
                Body = ThankYou(resolution),
                Sender = "Rhodes Island",
                Attachments = "[]",
                SentAt = now,
                ExpiresAt = now.AddDays(MailDays),
                SentBy = moderatorId,
            };
            db.Mails.Add(mail);

            report.ResolvedAt = now;
            report.Resolution = resolution;
            report.ResolutionNote = note is { Length: > SignatureReportLimits.NoteMaxLength }
                ? note[..SignatureReportLimits.NoteMaxLength]
                : note;
            report.ResolvedBy = moderatorId;
            report.MailId = mail.Id;
        }

        return open.Count;
    }

    /// <summary>What a reporter is told. Never who was reported or why they were judged as they were.</summary>
    internal static string ThankYou(SignatureReportResolution resolution) => resolution == SignatureReportResolution.Removed
        ? "Doctor,\n\nthank you for your report. A moderator has looked at the profile signature you reported " +
          "and taken it down.\n\nReports like yours keep Rhodes Island a pleasant place for everyone."
        : "Doctor,\n\nthank you for your report. A moderator has looked at the profile signature you reported " +
          "and found it within the rules, so it stays up.\n\nIf it changes into something that is not, you can report it again.";
}
