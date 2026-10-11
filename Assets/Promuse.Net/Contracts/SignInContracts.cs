// Compiled by both sides - see the note at the top of ApiProblem.cs. C# 9 syntax
// is deliberate: Unity 6 compiles at that level.

#nullable enable

using System;
using System.Collections.Generic;
using Promuse.Contracts.Players;

namespace Promuse.Contracts.SignIn
{
    /// <summary>
    /// 每月签到 / The month's daily sign-in, as one player sees it.
    ///
    /// 按次数 / Counted in days signed, not calendar dates: each server day the next square pays,
    /// a missed day loses nothing but a square at the end of the month. A new month starts again
    /// from square one. Months and days turn over at the server's daily reset.
    /// </summary>
    /// <param name="MonthKey">"2026-10".</param>
    /// <param name="Rewards">One per day of this month, square 1 first.</param>
    /// <param name="Signed">Squares claimed so far this month.</param>
    /// <param name="CanSignToday">Not yet signed today, and a square left.</param>
    public sealed record SignInMonth(
        string MonthKey,
        int Year,
        int Month,
        IReadOnlyList<ItemStack> Rewards,
        int Signed,
        bool CanSignToday,
        DateTimeOffset NextDayAt,
        DateTimeOffset ServerTime);

    /// <param name="Day">The square just claimed, from 1.</param>
    public sealed record SignInResult(
        int Day,
        ItemStack Granted,
        SignInMonth Month,
        PlayerState Player);

    // ------------------------------------------------------------------ admin

    /// <param name="Key">"default", or a month "2026-10" that overrides it.</param>
    public sealed record SignInTable(
        string Key,
        IReadOnlyList<ItemStack> Rewards,
        DateTimeOffset UpdatedAt,
        string? UpdatedBy);

    public sealed record SignInTables(IReadOnlyList<SignInTable> Tables);

    public sealed record SignInTableRequest(IReadOnlyList<ItemStack> Rewards);
}
