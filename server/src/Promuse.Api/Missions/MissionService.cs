using Microsoft.EntityFrameworkCore;
using Promuse.Api.Economy;
using Promuse.Api.Infrastructure;
using Promuse.Api.Players;
using Promuse.Contracts.Missions;
using Promuse.Contracts.Players;
using Promuse.Persistence;
using Promuse.Persistence.Entities;

namespace Promuse.Api.Missions;

/// <summary>
/// The two mission boards.
///
/// 三步, 一步都不自动 / Three steps and only the first happens on its own: a
/// counter moves when the player actually does something, a finished mission pays
/// its points when it is claimed, and points hand over items when a reward is
/// claimed. Opening the board grants nothing. That is how the client behaved and
/// it is deliberate - the press is the confirmation.
/// </summary>
public sealed class MissionService(
    PromuseDbContext db,
    TimeProvider clock,
    MissionPeriod period,
    PlayerService players,
    Inventory inventory)
{
    private static string TitleFor(MissionTab tab) =>
        tab == MissionTab.Weekly ? "WEEKLY MISSIONS" : "DAILY MISSIONS";

    // ------------------------------------------------------------------ read

    public async Task<Outcome<MissionBoards>> GetBoardsAsync(Guid accountId, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();

        return Outcome<MissionBoards>.Ok(new MissionBoards(
            await BuildBoardAsync(accountId, MissionTab.Daily, now, ct),
            await BuildBoardAsync(accountId, MissionTab.Weekly, now, ct),
            now));
    }

    private async Task<MissionBoard> BuildBoardAsync(
        Guid accountId, MissionTab tab, DateTimeOffset now, CancellationToken ct)
    {
        string key = period.KeyFor(tab, now);

        List<MissionDefinition> definitions = await db.MissionDefinitions.AsNoTracking()
            .Where(m => m.Tab == tab && m.IsActive).OrderBy(m => m.SortOrder).ToListAsync(ct);

        List<MissionRewardDefinition> rewards = await db.MissionRewardDefinitions.AsNoTracking()
            .Where(r => r.Tab == tab && r.IsActive).OrderBy(r => r.SortOrder).ToListAsync(ct);

        // 只读当前周期 / Scoped to this period's key, which is what makes a new
        // day start empty without anything having deleted yesterday.
        Dictionary<MissionGoal, int> counters = await db.MissionCounters.AsNoTracking()
            .Where(c => c.AccountId == accountId && c.Tab == tab && c.PeriodKey == key)
            .ToDictionaryAsync(c => c.Goal, c => c.Count, ct);

        HashSet<string> claimed = [.. await db.MissionClaims.AsNoTracking()
            .Where(c => c.AccountId == accountId && c.Tab == tab && c.PeriodKey == key)
            .Select(c => c.MissionId).ToListAsync(ct)];

        HashSet<string> rewardsTaken = [.. await db.MissionRewardClaims.AsNoTracking()
            .Where(c => c.AccountId == accountId && c.Tab == tab && c.PeriodKey == key)
            .Select(c => c.RewardId).ToListAsync(ct)];

        // 只算领过的 / Claimed, not merely finished. A mission the player has not
        // pressed is worth nothing yet, which is the whole point of the step.
        int points = definitions.Where(m => claimed.Contains(m.MissionId)).Sum(m => m.Points);

        var missionStates = definitions.Select(m =>
        {
            int count = counters.GetValueOrDefault(m.Goal);

            return new MissionState(
                m.MissionId, m.Description, m.Goal, m.Target,
                // Capped so a bar never overfills.
                Math.Min(count, m.Target),
                m.Points,
                count >= m.Target,
                claimed.Contains(m.MissionId));
        }).ToList();

        var rewardStates = rewards.Select(r => new RewardState(
            r.RewardId, r.RequiredPoints, r.ItemId, r.Amount,
            rewardsTaken.Contains(r.RewardId),
            points >= r.RequiredPoints && !rewardsTaken.Contains(r.RewardId))).ToList();

        return new MissionBoard(
            tab, TitleFor(tab), points, period.ResetsAt(tab, now), missionStates, rewardStates);
    }

    // --------------------------------------------------------------- counting

    /// <summary>
    /// 两个板子一起加 / Both boards advance. One song cleared counts towards the
    /// daily play mission and the weekly one at the same time, so callers never
    /// choose a board - exactly as MissionManager.Notify behaved on the client.
    ///
    /// Called from inside whatever transaction the caller already opened: a
    /// purchase that rolls back must not leave its mission tick behind.
    /// </summary>
    public async Task NotifyAsync(Guid accountId, MissionGoal goal, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();

        foreach (MissionTab tab in new[] { MissionTab.Daily, MissionTab.Weekly })
        {
            string key = period.KeyFor(tab, now);

            // Upsert rather than read-then-write, for the reason the inventory
            // credit gives: two events arriving together would both find no row.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO mission_counters (account_id, tab, goal, period_key, count)
                 VALUES ({accountId}, {tab.ToString()}, {goal.ToString()}, {key}, 1)
                 ON CONFLICT (account_id, tab, goal, period_key)
                 DO UPDATE SET count = mission_counters.count + 1
                 """, ct);
        }
    }

    // ---------------------------------------------------------------- claims

    public async Task<Outcome<ClaimResult>> ClaimMissionAsync(
        Guid accountId, MissionTab tab, string missionId, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();
        string key = period.KeyFor(tab, now);

        MissionDefinition? mission = await db.MissionDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Tab == tab && m.MissionId == missionId && m.IsActive, ct);

        if (mission is null) return Fail(ApiProblems.MissionNotFound(missionId));

        int count = await db.MissionCounters.AsNoTracking()
            .Where(c => c.AccountId == accountId && c.Tab == tab
                        && c.Goal == mission.Goal && c.PeriodKey == key)
            .Select(c => c.Count).FirstOrDefaultAsync(ct);

        if (count < mission.Target) return Fail(ApiProblems.MissionNotComplete(count, mission.Target));

        db.MissionClaims.Add(new MissionClaim
        {
            AccountId = accountId, Tab = tab, MissionId = missionId,
            PeriodKey = key, ClaimedAt = now,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // 主键就是防重 / The row existing IS the claim, so claiming twice is a
            // primary key violation rather than a check that could race.
            db.ChangeTracker.Clear();

            return Fail(ApiProblems.AlreadyClaimed(missionId));
        }

        // 不给物品, 只给分 / Points only. Nothing in PlayerState changed, so its
        // version is deliberately left alone.
        return await ResultAsync(accountId, [], ct);
    }

    public async Task<Outcome<ClaimResult>> ClaimRewardAsync(
        Guid accountId, MissionTab tab, string rewardId, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();
        string key = period.KeyFor(tab, now);

        MissionRewardDefinition? reward = await db.MissionRewardDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Tab == tab && r.RewardId == rewardId && r.IsActive, ct);

        if (reward is null) return Fail(ApiProblems.MissionNotFound(rewardId));

        int points = await ClaimedPointsAsync(accountId, tab, key, ct);

        if (points < reward.RequiredPoints)
        {
            return Fail(ApiProblems.NotEnoughPoints(points, reward.RequiredPoints));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        db.MissionRewardClaims.Add(new MissionRewardClaim
        {
            AccountId = accountId, Tab = tab, RewardId = rewardId,
            PeriodKey = key, ClaimedAt = now,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();

            return Fail(ApiProblems.AlreadyClaimed(rewardId));
        }

        // 记录和发放一起 / The claim row and the items move together. A crash
        // between them would either pay twice or pay nothing.
        await inventory.CreditAsync(accountId, reward.ItemId, reward.Amount, ct);
        await inventory.BumpStateVersionAsync(accountId, now, ct);

        await transaction.CommitAsync(ct);

        return await ResultAsync(accountId, [new ItemStack(reward.ItemId, reward.Amount)], ct);
    }

    // ------------------------------------------------------------ internals

    private async Task<int> ClaimedPointsAsync(
        Guid accountId, MissionTab tab, string key, CancellationToken ct)
    {
        List<string> claimed = await db.MissionClaims.AsNoTracking()
            .Where(c => c.AccountId == accountId && c.Tab == tab && c.PeriodKey == key)
            .Select(c => c.MissionId).ToListAsync(ct);

        if (claimed.Count == 0) return 0;

        return await db.MissionDefinitions.AsNoTracking()
            .Where(m => m.Tab == tab && claimed.Contains(m.MissionId))
            .SumAsync(m => m.Points, ct);
    }

    private async Task<Outcome<ClaimResult>> ResultAsync(
        Guid accountId, IReadOnlyList<ItemStack> granted, CancellationToken ct)
    {
        Outcome<MissionBoards> boards = await GetBoardsAsync(accountId, ct);
        Outcome<PlayerState> state = await players.GetStateAsync(accountId, ct);

        if (!state.IsSuccess) return Fail(state.Problem!);

        return Outcome<ClaimResult>.Ok(new ClaimResult(granted, boards.Value!, state.Value!));
    }

    private static Outcome<ClaimResult> Fail(Promuse.Contracts.ApiProblem problem) =>
        Outcome<ClaimResult>.Fail(problem);
}
