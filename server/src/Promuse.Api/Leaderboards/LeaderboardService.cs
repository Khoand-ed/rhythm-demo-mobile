using Microsoft.EntityFrameworkCore;
using Promuse.Api.Infrastructure;
using Promuse.Api.Missions;
using Promuse.Contracts.Leaderboards;
using Promuse.Contracts.Missions;
using Promuse.Persistence;
using Promuse.Persistence.Entities;

namespace Promuse.Api.Leaderboards;

/// <summary>
/// Two boards per stage - all-time and this week - holding each player's best.
///
/// 周榜的边界 / The week is the weekly missions' week: Monday 04:00 in the players' timezone,
/// keyed the same way. One calendar for the whole game, so "this week" never means two
/// different things on two screens.
/// </summary>
public sealed class LeaderboardService(PromuseDbContext db, MissionPeriod period, TimeProvider clock)
{
    public const string AllTimeKey = "all";

    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    public string KeyFor(LeaderboardPeriod board, DateTimeOffset instant) =>
        board == LeaderboardPeriod.AllTime ? AllTimeKey : period.WeeklyKey(instant);

    /// <summary>
    /// Records an accepted, won run on both boards. Returns true when it beat the player's
    /// all-time best on this stage.
    ///
    /// 一条语句 / One statement per board, upsert guarded by "only if higher". Two results for the
    /// same player landing together are serialised by the row lock the upsert takes, and the lower
    /// of the two can never overwrite the higher. Must run inside the caller's transaction, so a
    /// run is ranked if and only if it was also closed.
    /// </summary>
    public async Task<bool> SubmitAsync(Run run, int score, int maxCombo, DateTimeOffset at, CancellationToken ct)
    {
        int allTime = await UpsertAsync(run, AllTimeKey, score, maxCombo, at, ct);
        await UpsertAsync(run, period.WeeklyKey(at), score, maxCombo, at, ct);

        return allTime == 1;
    }

    private Task<int> UpsertAsync(Run run, string key, int score, int maxCombo, DateTimeOffset at, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO leaderboard_scores
                (stage_id, period_key, account_id, score, max_combo, character_id, run_id, achieved_at)
            VALUES
                ({run.StageId}, {key}, {run.AccountId}, {score}, {maxCombo}, {run.CharacterId}, {run.Id}, {at})
            ON CONFLICT (stage_id, period_key, account_id) DO UPDATE
            SET score = EXCLUDED.score,
                max_combo = EXCLUDED.max_combo,
                character_id = EXCLUDED.character_id,
                run_id = EXCLUDED.run_id,
                achieved_at = EXCLUDED.achieved_at
            WHERE EXCLUDED.score > leaderboard_scores.score
            """, ct);

    /// <summary>
    /// 1-based, or null when the player has nothing on this board. Ties go to whoever set the
    /// score first.
    /// </summary>
    public async Task<int?> RankOfAsync(Guid accountId, string stageId, string key, CancellationToken ct)
    {
        LeaderboardScore? mine = await db.LeaderboardScores
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.StageId == stageId && e.PeriodKey == key && e.AccountId == accountId, ct);

        if (mine is null) return null;

        return await AheadOfAsync(mine, ct) + 1;
    }

    private Task<int> AheadOfAsync(LeaderboardScore mine, CancellationToken ct) =>
        db.LeaderboardScores.CountAsync(e =>
            e.StageId == mine.StageId && e.PeriodKey == mine.PeriodKey
            && (e.Score > mine.Score || (e.Score == mine.Score && e.AchievedAt < mine.AchievedAt)), ct);

    public async Task<Outcome<LeaderboardPage>> GetPageAsync(
        Guid accountId, string stageId, LeaderboardPeriod board, int limit, CancellationToken ct)
    {
        if (limit < 1 || limit > MaxLimit)
        {
            return Outcome<LeaderboardPage>.Fail(ApiProblems.ValidationFailed(
                new Dictionary<string, string[]> { ["limit"] = [$"Must be between 1 and {MaxLimit}."] }));
        }

        bool stageExists = await db.Stages.AnyAsync(s => s.StageId == stageId && s.IsActive, ct);
        if (!stageExists) return Outcome<LeaderboardPage>.Fail(ApiProblems.StageNotFound(stageId));

        DateTimeOffset now = clock.GetUtcNow();
        string key = KeyFor(board, now);

        var rows = await db.LeaderboardScores
            .AsNoTracking()
            .Where(e => e.StageId == stageId && e.PeriodKey == key)
            .OrderByDescending(e => e.Score)
            .ThenBy(e => e.AchievedAt)
            .ThenBy(e => e.AccountId)
            .Take(limit)
            .Select(e => new
            {
                e.AccountId,
                e.Score,
                e.MaxCombo,
                e.CharacterId,
                e.AchievedAt,
                e.Player!.DisplayName,
            })
            .ToListAsync(ct);

        List<LeaderboardEntry> entries = rows
            .Select((r, i) => new LeaderboardEntry(
                i + 1, r.AccountId, r.DisplayName, r.Score, r.MaxCombo, r.CharacterId, r.AchievedAt))
            .ToList();

        // 我自己 / The caller's own line, from the page when they made it, otherwise looked up
        // with the same ordering the page uses.
        LeaderboardEntry? me = entries.FirstOrDefault(e => e.PlayerId == accountId);

        if (me is null)
        {
            var mine = await db.LeaderboardScores
                .AsNoTracking()
                .Where(e => e.StageId == stageId && e.PeriodKey == key && e.AccountId == accountId)
                .Select(e => new { Row = e, e.Player!.DisplayName })
                .FirstOrDefaultAsync(ct);

            if (mine is not null)
            {
                int rank = await AheadOfAsync(mine.Row, ct) + 1;
                me = new LeaderboardEntry(rank, accountId, mine.DisplayName, mine.Row.Score,
                                          mine.Row.MaxCombo, mine.Row.CharacterId, mine.Row.AchievedAt);
            }
        }

        DateTimeOffset? resetsAt = board == LeaderboardPeriod.Weekly
            ? period.ResetsAt(MissionTab.Weekly, now)
            : null;

        return Outcome<LeaderboardPage>.Ok(new LeaderboardPage(
            stageId, board, key, resetsAt, entries, me, now));
    }
}
