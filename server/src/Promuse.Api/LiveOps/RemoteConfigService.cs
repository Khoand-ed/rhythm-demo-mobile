using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Promuse.Api.Infrastructure;
using Promuse.Api.Players;
using Promuse.Contracts.Config;
using Promuse.Persistence;
using Promuse.Persistence.Entities;

namespace Promuse.Api.LiveOps;

/// <summary>
/// Changing the remote config: a new version on every save, never an edit in place.
///
/// 和存档一样的并发规则 / The same concurrency rule as the player save: a write names the version
/// it was made from (If-Match), and is refused with 412 if someone saved since. Two admins with
/// the page open cannot silently overwrite each other's change.
/// </summary>
public sealed class RemoteConfigService(PromuseDbContext db, TimeProvider clock, RemoteConfigStore store)
{
    public const int MaxText = 300;
    public const int MaxNote = 200;
    public const int MaxHistory = 100;

    public async Task<Outcome<RemoteConfig>> UpdateAsync(
        Guid adminId, string? ifMatch, ConfigUpdateRequest request, CancellationToken ct)
    {
        Dictionary<string, string[]> errors = Validate(request.Document, request.Note);
        if (errors.Count > 0) return Outcome<RemoteConfig>.Fail(ApiProblems.ValidationFailed(errors));

        return await AppendAsync(adminId, ifMatch, request.Document, request.Note, rolledBackFrom: null, ct);
    }

    public async Task<Outcome<RemoteConfig>> RollbackAsync(
        Guid adminId, string? ifMatch, ConfigRollbackRequest request, CancellationToken ct)
    {
        if (request.Note is { Length: > MaxNote })
        {
            return Outcome<RemoteConfig>.Fail(ApiProblems.ValidationFailed(
                new Dictionary<string, string[]> { ["note"] = [$"At most {MaxNote} characters."] }));
        }

        RemoteConfigVersion? target = await db.RemoteConfigVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Version == request.Version, ct);

        if (target is null)
        {
            return Outcome<RemoteConfig>.Fail(ApiProblems.ValidationFailed(
                new Dictionary<string, string[]> { ["version"] = [$"There is no version {request.Version}."] }));
        }

        string note = string.IsNullOrWhiteSpace(request.Note) ? $"Rollback to version {target.Version}" : request.Note!;

        return await AppendAsync(adminId, ifMatch, store.Parse(target.Document), note, target.Version, ct);
    }

    public async Task<Outcome<ConfigHistory>> GetHistoryAsync(int limit, CancellationToken ct)
    {
        if (limit < 1 || limit > MaxHistory)
        {
            return Outcome<ConfigHistory>.Fail(ApiProblems.ValidationFailed(
                new Dictionary<string, string[]> { ["limit"] = [$"Must be between 1 and {MaxHistory}."] }));
        }

        var rows = await db.RemoteConfigVersions
            .AsNoTracking()
            .OrderByDescending(v => v.Version)
            .Take(limit)
            .Select(v => new
            {
                Row = v,
                Author = db.Accounts.Where(a => a.Id == v.CreatedBy).Select(a => a.Username).FirstOrDefault(),
            })
            .ToListAsync(ct);

        List<ConfigRevision> revisions = [.. rows.Select(r => new ConfigRevision(
            r.Row.Version, store.Parse(r.Row.Document), r.Row.CreatedAt, r.Author, r.Row.Note, r.Row.RolledBackFrom))];

        return Outcome<ConfigHistory>.Ok(new ConfigHistory(revisions));
    }

    /// <summary>
    /// The caller is an administrator right now - read from the account, not from the token, so a
    /// revoked admin loses access on their next request rather than when the token runs out.
    /// </summary>
    public static async Task<bool> IsAdminAsync(PromuseDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return false;

        return await db.Accounts.AnyAsync(a => a.Id == accountId && a.IsAdmin && a.BannedAt == null, ct);
    }

    // ------------------------------------------------------------------ steps

    private async Task<Outcome<RemoteConfig>> AppendAsync(
        Guid adminId, string? ifMatch, ConfigDocument document, string? note, int? rolledBackFrom, CancellationToken ct)
    {
        if (!PlayerService.TryParseETag(ifMatch, out int expected))
        {
            return Outcome<RemoteConfig>.Fail(ApiProblems.PreconditionRequired());
        }

        int current = await db.RemoteConfigVersions.MaxAsync(v => v.Version, ct);
        if (expected != current) return Outcome<RemoteConfig>.Fail(ApiProblems.ConfigConflict(current));

        DateTimeOffset now = clock.GetUtcNow();

        db.RemoteConfigVersions.Add(new RemoteConfigVersion
        {
            Version = current + 1,
            Document = store.Serialize(document),
            CreatedAt = now,
            CreatedBy = adminId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            RolledBackFrom = rolledBackFrom,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // 主键拒绝了第二个人 / Another admin saved the same next version between the read
            // above and this insert, and the primary key refused this one.
            db.ChangeTracker.Clear();
            int latest = await db.RemoteConfigVersions.MaxAsync(v => v.Version, ct);
            return Outcome<RemoteConfig>.Fail(ApiProblems.ConfigConflict(latest));
        }

        store.Invalidate();

        return Outcome<RemoteConfig>.Ok(new RemoteConfig(current + 1, document, now, now), created: true);
    }

    private static Dictionary<string, string[]> Validate(ConfigDocument? document, string? note)
    {
        var errors = new Dictionary<string, string[]>();

        if (document is null)
        {
            errors["document"] = ["Required."];
            return errors;
        }

        if (document.Maintenance is null) errors["document.maintenance"] = ["Required."];
        if (document.Features is null) errors["document.features"] = ["Required."];

        if (!ClientVersion.IsValid(document.MinClientVersion))
        {
            errors["document.minClientVersion"] = ["Must look like 1, 1.2 or 1.2.3."];
        }

        if (document.Maintenance?.Message is { Length: > MaxText })
        {
            errors["document.maintenance.message"] = [$"At most {MaxText} characters."];
        }

        if (document.Announcement is { Length: > MaxText })
        {
            errors["document.announcement"] = [$"At most {MaxText} characters."];
        }

        if (note is { Length: > MaxNote }) errors["note"] = [$"At most {MaxNote} characters."];

        return errors;
    }
}
