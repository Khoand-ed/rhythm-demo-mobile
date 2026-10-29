using Microsoft.EntityFrameworkCore;
using Promuse.Api.Economy;
using Promuse.Api.Gacha;
using Promuse.Api.Infrastructure;
using Promuse.Api.Players;
using Promuse.Contracts;
using Promuse.Contracts.Campaign;
using Promuse.Contracts.Players;
using Promuse.Persistence;
using CampaignRow = Promuse.Persistence.Entities.Campaign;

namespace Promuse.Api.Campaign;

/// <summary>
/// The main campaign: what a player sees of it, whether a run may open as one of its stages, and
/// what a cleared stage pays. Plus the admin side that edits it.
///
/// 首通的听众 / What else a first clear pays - Manufacture's blueprints - comes from the
/// <see cref="ICampaignClearListener"/>s, so the campaign pays it and guards the stages it is paid
/// on without knowing what any of it is.
/// </summary>
public sealed class CampaignService(
    PromuseDbContext db,
    TimeProvider clock,
    Inventory inventory,
    IGachaDice dice,
    IEnumerable<ICampaignClearListener> listeners)
{
    // ------------------------------------------------------------------ player

    public async Task<CampaignView> ViewAsync(Guid accountId, CancellationToken ct)
    {
        CampaignDefinition d = await DefinitionAsync(ct);
        var order = CampaignRules.Flatten(d);
        HashSet<string> cleared = await ClearedAsync(accountId, ct);

        Dictionary<string, int> costs = await db.Stages.AsNoTracking()
            .ToDictionaryAsync(s => s.StageId, s => s.StaminaCost, ct);

        var chapters = new List<CampaignChapterState>();
        int index = 0;
        foreach (CampaignChapter chapter in d.Chapters)
        {
            var stages = new List<CampaignStageState>();
            foreach (CampaignStage stage in chapter.Stages)
            {
                stages.Add(new CampaignStageState(stage, CampaignRules.IsUnlocked(order, index, cleared),
                    cleared.Contains(stage.StageId), costs.GetValueOrDefault(stage.ChartId)));
                index++;
            }

            chapters.Add(new CampaignChapterState(chapter.ChapterId, chapter.Title,
                stages.Count > 0 && stages[0].IsUnlocked, stages));
        }

        return new CampaignView(chapters, clock.GetUtcNow());
    }

    /// <summary>
    /// 开局前查 / Whether a run of <paramref name="chartId"/> may open as campaign stage
    /// <paramref name="stageId"/>: the stage exists, plays that chart, and the one before it is
    /// cleared. Null when it may - checked before any Sanity is charged.
    /// </summary>
    public async Task<ApiProblem?> CanStartAsync(Guid accountId, string stageId, string chartId, CancellationToken ct)
    {
        var order = CampaignRules.Flatten(await DefinitionAsync(ct));
        int index = order.FindIndex(o => o.Stage.StageId == stageId);

        if (index < 0) return Invalid("campaignStageId", $"'{stageId}' is not a stage of the campaign.");
        if (order[index].Stage.ChartId != chartId) return Invalid("stageId", $"Campaign stage '{stageId}' plays '{order[index].Stage.ChartId}'.");

        HashSet<string> cleared = await ClearedAsync(accountId, ct);
        return CampaignRules.IsUnlocked(order, index, cleared) ? null : ApiProblems.StageLocked(order[index - 1].Stage.Code);
    }

    /// <summary>
    /// A run opened as a campaign stage was cleared: record the clear, pay the first-clear Orundum -
    /// and whatever the listeners pay - the first time, roll the drops every time. Inside the caller's
    /// transaction, which opens none; the caller runs <see cref="CampaignPayout.AfterCommit"/> once
    /// it has committed.
    /// </summary>
    public async Task<CampaignPayout> OnClearedAsync(
        Guid accountId, string stageId, DateTimeOffset now, CancellationToken ct)
    {
        CampaignStage? stage = CampaignRules.Flatten(await DefinitionAsync(ct))
            .Select(o => o.Stage).FirstOrDefault(s => s.StageId == stageId);

        // 关卡被删了 / Removed from the campaign while the song was played: nothing to pay.
        if (stage is null) return CampaignPayout.None;

        // 先插入 / Insert-if-absent first: of two clears racing for a first clear, exactly one inserts,
        // and only that one pays the Orundum. The other counts as a repeat clear.
        int first = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO campaign_clears (account_id, stage_id, clears, first_cleared_at)
             VALUES ({accountId}, {stageId}, 1, {now})
             ON CONFLICT (account_id, stage_id) DO NOTHING
             """, ct);

        if (first == 0)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE campaign_clears SET clears = clears + 1 WHERE account_id = {accountId} AND stage_id = {stageId}", ct);
        }

        int orundum = first == 1 ? stage.FirstClearOrundum : 0;
        if (orundum > 0) await inventory.CreditAsync(accountId, SeedCampaign.OrundumItemId, orundum, ct);

        var firstClearItems = new List<ItemStack>();
        var afterCommit = new List<Action>();
        if (first == 1)
        {
            foreach (ICampaignClearListener listener in listeners)
            {
                FirstClearPayout payout = await listener.OnFirstClearAsync(accountId, stageId, now, ct);
                firstClearItems.AddRange(payout.Items);
                if (payout.Committed is not null) afterCommit.Add(payout.Committed);
            }
        }

        List<ItemStack> drops = CampaignRules.Roll(stage.Drops, dice);
        foreach (ItemStack drop in drops) await inventory.CreditAsync(accountId, drop.ItemId, drop.Amount, ct);

        return new CampaignPayout(orundum, drops, firstClearItems, afterCommit);
    }

    /// <summary>The code of the furthest stage <paramref name="accountId"/> has cleared, for a profile.</summary>
    public async Task<string?> FurthestClearedAsync(Guid accountId, CancellationToken ct)
    {
        HashSet<string> cleared = await ClearedAsync(accountId, ct);
        if (cleared.Count == 0) return null;

        return CampaignRules.Furthest(CampaignRules.Flatten(await DefinitionAsync(ct)), cleared)?.Code;
    }

    /// <summary>
    /// Every stage's code by its id - "main_1_3" is "1-3" - for a screen that names a stage the way
    /// the player knows it. From the live definition, so a stage an admin renamed shows its new code.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> StageCodesAsync(CancellationToken ct)
    {
        var codes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (_, stage) in CampaignRules.Flatten(await DefinitionAsync(ct))) codes.TryAdd(stage.StageId, stage.Code);
        return codes;
    }

    private async Task<HashSet<string>> ClearedAsync(Guid accountId, CancellationToken ct) =>
        [.. await db.CampaignClears.AsNoTracking()
            .Where(c => c.AccountId == accountId).Select(c => c.StageId).ToListAsync(ct)];

    private async Task<CampaignDefinition> DefinitionAsync(CancellationToken ct)
    {
        string? json = await db.Campaigns.AsNoTracking()
            .Where(c => c.Key == SeedCampaign.Key).Select(c => c.Definition).FirstOrDefaultAsync(ct);
        return json is null ? new CampaignDefinition([]) : CampaignRules.Parse(json);
    }

    private static ApiProblem Invalid(string field, string message) =>
        ApiProblems.ValidationFailed(new Dictionary<string, string[]> { [field] = [message] });

    // ------------------------------------------------------------------ admin

    public async Task<AdminCampaign> AdminGetAsync(CancellationToken ct)
    {
        var row = await db.Campaigns.AsNoTracking()
            .Where(c => c.Key == SeedCampaign.Key)
            .Select(c => new { Row = c, By = db.Accounts.Where(a => a.Id == c.UpdatedBy).Select(a => a.Username).FirstOrDefault() })
            .FirstOrDefaultAsync(ct);

        return row is null
            ? new AdminCampaign(0, new CampaignDefinition([]), clock.GetUtcNow(), null)
            : new AdminCampaign(row.Row.Version, CampaignRules.Parse(row.Row.Definition), row.Row.UpdatedAt, row.By);
    }

    /// <summary>Saves a new version of the whole campaign; If-Match names the version it was read at.</summary>
    public async Task<Outcome<AdminCampaign>> SaveAsync(
        Guid adminId, string? ifMatch, CampaignUpsertRequest? request, CancellationToken ct)
    {
        HashSet<string> charts = [.. await db.Stages.AsNoTracking().Select(s => s.StageId).ToListAsync(ct)];
        Dictionary<string, string[]> errors = CampaignRules.Validate(request?.Definition, charts);

        // 不能删掉付东西的关 / A stage a listener pays on stays: without it, what it paid - a
        // Manufacture blueprint - could never be had again, and everything behind it stays shut.
        if (errors.Count == 0)
        {
            HashSet<string> stages =
                [.. CampaignRules.Flatten(CampaignRules.Normalize(request!.Definition)).Select(o => o.Stage.StageId)];
            string[] lost =
            [
                .. listeners.SelectMany(l => l.StagesPaidOn)
                    .Where(paid => !stages.Contains(paid.Key))
                    .Select(paid => $"Stage '{paid.Key}' cannot be removed: {paid.Value}."),
            ];
            if (lost.Length > 0) errors["chapters"] = lost;
        }

        if (errors.Count > 0) return Outcome<AdminCampaign>.Fail(ApiProblems.ValidationFailed(errors));

        CampaignRow? row = await db.Campaigns.FirstOrDefaultAsync(c => c.Key == SeedCampaign.Key, ct);
        if (row is null)
        {
            row = new CampaignRow { Key = SeedCampaign.Key };
            db.Campaigns.Add(row);
        }
        else
        {
            if (!PlayerService.TryParseETag(ifMatch, out int expected)) return Outcome<AdminCampaign>.Fail(ApiProblems.PreconditionRequired());
            if (expected != row.Version) return Outcome<AdminCampaign>.Fail(Conflict(row.Version));
        }

        DateTimeOffset now = clock.GetUtcNow();
        row.Version += 1;
        row.Definition = CampaignRules.Serialize(request!.Definition);
        row.UpdatedAt = now;
        row.UpdatedBy = adminId;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            int latest = await db.Campaigns.Where(c => c.Key == SeedCampaign.Key).Select(c => c.Version).FirstOrDefaultAsync(ct);
            return Outcome<AdminCampaign>.Fail(Conflict(latest));
        }

        return Outcome<AdminCampaign>.Ok(await AdminGetAsync(ct));
    }

    private static ApiProblem Conflict(int version) =>
        ApiProblems.Create(412, ErrorCodes.StateConflict, "Campaign has moved on",
            $"Someone saved the campaign first; it is now at version {version}. Reload it and make the change again.");
}
