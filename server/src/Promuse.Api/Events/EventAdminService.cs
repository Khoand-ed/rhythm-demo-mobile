using Microsoft.EntityFrameworkCore;
using Promuse.Api.Infrastructure;
using Promuse.Api.Players;
using Promuse.Contracts;
using Promuse.Contracts.Events;
using Promuse.Persistence;
using Promuse.Persistence.Entities;

namespace Promuse.Api.Events;

/// <summary>
/// Creating and editing events from the admin page.
///
/// 和远程配置一样的规矩 / The same rules as remote config: an edit names the version it was made
/// from and is refused with 412 if someone saved since. An event is never deleted - switching it
/// off hides it and keeps everyone's progress in it.
/// </summary>
public sealed class EventAdminService(PromuseDbContext db, TimeProvider clock)
{
    public async Task<AdminEventList> ListAsync(CancellationToken ct)
    {
        var rows = await db.GameEvents
            .AsNoTracking()
            .OrderByDescending(e => e.StartsAt)
            .Select(e => new
            {
                Row = e,
                Author = db.Accounts.Where(a => a.Id == e.UpdatedBy).Select(a => a.Username).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return new AdminEventList([.. rows.Select(r => ToAdmin(r.Row, r.Author))]);
    }

    public async Task<EventCatalog> CatalogAsync(CancellationToken ct) =>
        new(await db.Stages.AsNoTracking().OrderBy(s => s.StageId).Select(s => s.StageId).ToListAsync(ct),
            await db.GachaBanners.AsNoTracking().OrderBy(b => b.SortOrder).Select(b => b.BannerId).ToListAsync(ct));

    /// <summary>
    /// Creates the event when it does not exist yet (no If-Match), or saves a new version of it
    /// (If-Match with the version it was read at).
    /// </summary>
    public async Task<Outcome<AdminEvent>> UpsertAsync(
        Guid adminId, string eventId, string? ifMatch, EventUpsertRequest request, CancellationToken ct)
    {
        if (!EventRules.IsValidId(eventId))
        {
            return Outcome<AdminEvent>.Fail(ApiProblems.ValidationFailed(new Dictionary<string, string[]>
            {
                ["eventId"] = ["Lower-case letters, digits and underscores, up to 64."],
            }));
        }

        EventCatalog catalog = await CatalogAsync(ct);
        Dictionary<string, string[]> errors = EventRules.Validate(
            request.Definition, catalog.StageIds.ToHashSet(), catalog.BannerIds.ToHashSet());

        if (errors.Count > 0) return Outcome<AdminEvent>.Fail(ApiProblems.ValidationFailed(errors));

        EventDefinition d = EventRules.Normalize(request.Definition);
        DateTimeOffset now = clock.GetUtcNow();

        GameEvent? row = await db.GameEvents.FirstOrDefaultAsync(e => e.EventId == eventId, ct);
        bool created = row is null;

        if (row is null)
        {
            if (!string.IsNullOrEmpty(ifMatch)) return Outcome<AdminEvent>.Fail(Conflict(0));

            row = new GameEvent { EventId = eventId, Version = 0, CreatedAt = now };
            db.GameEvents.Add(row);
        }
        else
        {
            if (!PlayerService.TryParseETag(ifMatch, out int expected)) return Outcome<AdminEvent>.Fail(ApiProblems.PreconditionRequired());
            if (expected != row.Version) return Outcome<AdminEvent>.Fail(Conflict(row.Version));
        }

        row.Version += 1;
        row.Definition = EventRules.Serialize(d);
        row.StartsAt = d.StartsAt;
        row.EndsAt = d.EndsAt;
        row.IsActive = d.IsActive;
        row.UpdatedAt = now;
        row.UpdatedBy = adminId;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // 同时创建或保存 / Two admins creating the same id, or saving the same version, at once.
            db.ChangeTracker.Clear();
            int latest = await db.GameEvents.Where(e => e.EventId == eventId).Select(e => e.Version).FirstOrDefaultAsync(ct);
            return Outcome<AdminEvent>.Fail(Conflict(latest));
        }

        string? author = await db.Accounts.Where(a => a.Id == adminId).Select(a => a.Username).FirstOrDefaultAsync(ct);
        return Outcome<AdminEvent>.Ok(ToAdmin(row, author), created);
    }

    private static ApiProblem Conflict(int version) =>
        ApiProblems.Create(412, ErrorCodes.StateConflict, "Event has moved on",
            version == 0
                ? "An event with this id was created by someone else meanwhile, or no longer exists. Reload the list."
                : $"Someone saved this event first; it is now at version {version}. Reload it and make the change again.");

    private static AdminEvent ToAdmin(GameEvent row, string? author) =>
        new(row.EventId, row.Version, EventRules.Parse(row.Definition), row.UpdatedAt, author);
}
