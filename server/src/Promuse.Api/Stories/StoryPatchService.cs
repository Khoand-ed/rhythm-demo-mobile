using Microsoft.EntityFrameworkCore;
using Promuse.Api.Infrastructure;
using Promuse.Contracts.Stories;
using Promuse.Persistence;
using Promuse.Persistence.Entities;
using StoryPatch = Promuse.Contracts.Stories.StoryPatch;

namespace Promuse.Api.Stories;

/// <summary>
/// Corrections to story lines, for the typo found after a build has shipped.
///
/// 剧情不在服务端 / The stories themselves are not here: they ship inside the game, and the server
/// never sees them. An admin opens the script file on the admin page, picks the line, and what is
/// saved is the line's id, the text it had, and the text it should have.
///
/// 一行一行存 / One row per line, saved on its own: two admins fixing two lines never collide, and
/// two fixing the same line both land, the later one winning - a typo fix has nothing to merge.
/// </summary>
public sealed class StoryPatchService(PromuseDbContext db, TimeProvider clock)
{
    /// <summary>What the game applies: every correction in force, and the revision as the ETag.</summary>
    public async Task<StoryPatchList> ListAsync(CancellationToken ct)
    {
        int revision = await RevisionAsync(ct);

        List<StoryLinePatch> rows = await db.StoryPatches.AsNoTracking()
            .Where(p => p.Text != null)
            .OrderBy(p => p.StoryId).ThenBy(p => p.LineId)
            .ToListAsync(ct);

        return new StoryPatchList(revision, [.. rows.Select(p => new StoryPatch(
            p.StoryId, p.LineId, p.Original, p.Text!))]);
    }

    /// <summary>
    /// The highest revision ever handed out, taken-back corrections included - so taking one back
    /// changes it too, and a client holding the old list is told to fetch again.
    /// </summary>
    public async Task<int> RevisionAsync(CancellationToken ct) =>
        await db.StoryPatches.AsNoTracking().MaxAsync(p => (int?)p.Revision, ct) ?? 0;

    public async Task<AdminStoryPatchList> AdminListAsync(CancellationToken ct)
    {
        var rows = await db.StoryPatches.AsNoTracking()
            .Where(p => p.Text != null)
            .OrderByDescending(p => p.UpdatedAt)
            .Select(p => new
            {
                Row = p,
                Author = db.Accounts.Where(a => a.Id == p.UpdatedBy).Select(a => a.Username).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return new AdminStoryPatchList([.. rows.Select(r => ToAdmin(r.Row, r.Author))]);
    }

    public async Task<Outcome<AdminStoryPatch>> SaveAsync(
        Guid adminId, string storyId, string lineId, StoryPatchRequest request, CancellationToken ct)
    {
        Dictionary<string, string[]> errors = Validate(storyId, lineId);

        string original = request.Original?.Trim() ?? "";
        string text = request.Text?.Trim() ?? "";

        if (original.Length is 0 or > StoryIds.MaxLineText) errors["original"] = [$"1-{StoryIds.MaxLineText} characters."];
        if (text.Length is 0 or > StoryIds.MaxLineText) errors["text"] = [$"1-{StoryIds.MaxLineText} characters."];
        else if (text == original) errors["text"] = ["Same as the original - there is nothing to correct."];

        if (errors.Count > 0) return Outcome<AdminStoryPatch>.Fail(ApiProblems.ValidationFailed(errors));

        DateTimeOffset now = clock.GetUtcNow();

        // 一条语句 / One statement, so two saves of the same line cannot both insert: the second
        // becomes an update, and each takes its own revision from the sequence.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO story_patches (story_id, line_id, original, text, revision, updated_at, updated_by)
             VALUES ({storyId}, {lineId}, {original}, {text}, nextval('story_patch_revisions'), {now}, {adminId})
             ON CONFLICT (story_id, line_id) DO UPDATE SET
                 original = EXCLUDED.original,
                 text = EXCLUDED.text,
                 revision = EXCLUDED.revision,
                 updated_at = EXCLUDED.updated_at,
                 updated_by = EXCLUDED.updated_by
             """, ct);

        StoryLinePatch row = await db.StoryPatches.AsNoTracking()
            .FirstAsync(p => p.StoryId == storyId && p.LineId == lineId, ct);

        string? author = await db.Accounts.Where(a => a.Id == adminId).Select(a => a.Username).FirstOrDefaultAsync(ct);
        return Outcome<AdminStoryPatch>.Ok(ToAdmin(row, author));
    }

    /// <summary>Takes a correction back: the line reads as the build has it again.</summary>
    public async Task<Outcome<bool>> RevertAsync(Guid adminId, string storyId, string lineId, CancellationToken ct)
    {
        Dictionary<string, string[]> errors = Validate(storyId, lineId);
        if (errors.Count > 0) return Outcome<bool>.Fail(ApiProblems.ValidationFailed(errors));

        DateTimeOffset now = clock.GetUtcNow();

        int changed = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE story_patches
             SET text = NULL, revision = nextval('story_patch_revisions'), updated_at = {now}, updated_by = {adminId}
             WHERE story_id = {storyId} AND line_id = {lineId} AND text IS NOT NULL
             """, ct);

        return changed == 0
            ? Outcome<bool>.Fail(ApiProblems.StoryPatchNotFound(storyId, lineId))
            : Outcome<bool>.Ok(true);
    }

    private static Dictionary<string, string[]> Validate(string storyId, string lineId)
    {
        var errors = new Dictionary<string, string[]>();
        if (!StoryIds.IsStoryId(storyId)) errors["storyId"] = ["The script's file name: lower-case letters, digits and underscores."];
        if (!StoryIds.IsLineId(lineId)) errors["lineId"] = ["The id after the line: 4-8 lower-case letters and digits."];
        return errors;
    }

    private static AdminStoryPatch ToAdmin(StoryLinePatch row, string? author) =>
        new(row.StoryId, row.LineId, row.Original, row.Text ?? "", row.Revision, row.UpdatedAt, author);
}
