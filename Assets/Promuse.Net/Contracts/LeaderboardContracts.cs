// Compiled by both sides - see the note at the top of ApiProblem.cs. C# 9 syntax
// is deliberate: Unity 6 compiles at that level.

#nullable enable

using System;
using System.Collections.Generic;

namespace Promuse.Contracts.Leaderboards
{
    /// <summary>
    /// 两张榜 / Every stage has two boards: the best ever, and the best this week.
    ///
    /// 周榜不清零 / The weekly board is never emptied by a job. Its entries are keyed by the
    /// week they were set in - the same Monday 04:00 boundary the weekly missions use - so a
    /// new week simply reads a key nobody has written yet.
    /// </summary>
    public enum LeaderboardPeriod
    {
        AllTime,
        Weekly
    }

    /// <param name="Rank">
    /// 1-based. Ties go to whoever set the score first: matching a score is not the same as
    /// having held it.
    /// </param>
    /// <param name="CharacterId">The operator the score was set with.</param>
    public sealed record LeaderboardEntry(
        int Rank,
        Guid PlayerId,
        string DisplayName,
        int Score,
        int MaxCombo,
        string? CharacterId,
        DateTimeOffset AchievedAt);

    /// <param name="PeriodKey">"all", or the ISO week such as "2026-W41".</param>
    /// <param name="ResetsAt">When the weekly board moves to a new key. Null for all-time.</param>
    /// <param name="Me">
    /// The caller's own entry with its rank, whether or not it made the page. Null when they
    /// have no ranked run on this board.
    /// </param>
    public sealed record LeaderboardPage(
        string StageId,
        LeaderboardPeriod Period,
        string PeriodKey,
        DateTimeOffset? ResetsAt,
        IReadOnlyList<LeaderboardEntry> Entries,
        LeaderboardEntry? Me,
        DateTimeOffset ServerTime);
}
