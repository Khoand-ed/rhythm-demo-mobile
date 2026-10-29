// Compiled by both sides - see the note at the top of ApiProblem.cs. C# 9 syntax
// is deliberate: Unity 6 compiles at that level.

#nullable enable

using System;
using System.Collections.Generic;
using Promuse.Contracts.Players;

namespace Promuse.Contracts.Manufacture
{
    /// <summary>The two stations. Fixed: a station is never added or renamed by a player.</summary>
    public static class ManufactureStations
    {
        public const string Lmd = "lmd";
        public const string Exp = "exp";

        /// <summary>Both, in the order they are locked when one request touches both.</summary>
        public static readonly IReadOnlyList<string> All = new[] { Exp, Lmd };

        public static bool IsStation(string? stationId) => stationId == Lmd || stationId == Exp;
    }

    /// <summary>
    /// 一个等级的产线 / What a station makes at one level: <paramref name="UnitAmount"/> of
    /// <paramref name="ItemId"/> every <paramref name="SecondsPerUnit"/>, holding at most
    /// <paramref name="CapacityUnits"/> before it stops.
    /// </summary>
    /// <param name="SecondsPerUnit">
    /// As the server runs it - already divided by its time scale, so a Local server that runs an
    /// hour a minute sends a number the client's countdown can use as it is.
    /// </param>
    public sealed record StationLevel(int Level, int ItemId, int UnitAmount, int SecondsPerUnit, int CapacityUnits);

    /// <summary>The next level of a station and what it takes to get there.</summary>
    /// <param name="Cost">Spent on the upgrade. The blueprint is not in it: it is held, never spent.</param>
    /// <param name="BlueprintItemId">The blueprint the player must hold - see <c>KeyItems</c>.</param>
    /// <param name="BlueprintStageCode">The stage whose first clear pays it, as the player knows it ("1-3").</param>
    public sealed record StationUpgrade(
        StationLevel To,
        IReadOnlyList<ItemStack> Cost,
        int BlueprintItemId,
        string BlueprintStageCode);

    /// <summary>One station as it reads at <see cref="ManufactureView.ServerTime"/>.</summary>
    /// <param name="AnchorAt">
    /// The moment production is counted from. With <see cref="StationLevel.SecondsPerUnit"/> it lets
    /// the client run its own clock through <see cref="ManufactureClock.Read"/> between two loads.
    /// </param>
    /// <param name="NextUnitAt">When the next unit lands; null when the store is full.</param>
    /// <param name="FullAt">When the store fills; null when it already has.</param>
    /// <param name="Next">The next level; null at the top.</param>
    public sealed record StationState(
        string StationId,
        int Level,
        int MaxLevel,
        StationLevel Current,
        int StoredUnits,
        DateTimeOffset AnchorAt,
        DateTimeOffset? NextUnitAt,
        DateTimeOffset? FullAt,
        StationUpgrade? Next);

    /// <param name="IsUnlocked">The player holds Manufacture Blueprint I.</param>
    /// <param name="UnlockStageCode">The stage that unlocks it ("1-1"); null once unlocked.</param>
    /// <param name="Stations">Empty while locked.</param>
    /// <param name="Player">
    /// 只在这次读取发了东西时才有 / Present only when this read handed something out - a blueprint
    /// owed for a stage cleared before Manufacture existed. The bag changed, so the client applies it.
    /// </param>
    public sealed record ManufactureView(
        bool IsUnlocked,
        string? UnlockStageCode,
        IReadOnlyList<StationState> Stations,
        DateTimeOffset ServerTime,
        PlayerState? Player = null);

    /// <param name="StationId">One station, or null to collect from every station that has something.</param>
    public sealed record CollectRequest(string? StationId);

    /// <param name="FromLevel">
    /// 防止旧画面多升一级 / The level the player saw. A screen that has fallen behind - the station
    /// was raised on another device - is refused rather than raising it a second time.
    /// </param>
    public sealed record UpgradeRequest(string StationId, int FromLevel);

    /// <param name="Granted">What went into the bag: collected units, including those an upgrade collects first.</param>
    public sealed record ManufactureActionResult(
        ManufactureView View,
        IReadOnlyList<ItemStack> Granted,
        PlayerState Player);

    /// <summary>A station's store at one moment.</summary>
    public sealed record StationReading(int StoredUnits, DateTimeOffset? NextUnitAt, DateTimeOffset? FullAt);

    /// <summary>
    /// 生产时钟 / How much a station has made: one definition, run by the server to decide and by the
    /// client to count down between loads.
    ///
    /// 纯函数 / Pure, like StaminaCalculator: a station keeps only the moment it counts from, never a
    /// number that ticks, and reading it never moves it - so two reads a second apart agree on when
    /// the next unit lands.
    ///
    /// 整秒 / Whole seconds throughout. Postgres keeps a timestamp to the microsecond and .NET to the
    /// tenth of one, so a moment that went through the database and one that did not could disagree
    /// by a fraction - enough to land on either side of a unit boundary. Every anchor this returns is
    /// a whole second, and elapsed time is counted in whole seconds.
    /// </summary>
    public static class ManufactureClock
    {
        /// <summary>
        /// What the store holds at <paramref name="now"/>. A store at its capacity has stopped:
        /// nothing more accrues, however long it waits.
        ///
        /// 锚点在未来 / An anchor after <paramref name="now"/> means a clock moved, not that the
        /// player owes time: it reads as empty, and nothing already made is taken back.
        /// </summary>
        public static StationReading Read(
            DateTimeOffset anchorAt, int secondsPerUnit, int capacityUnits, DateTimeOffset now)
        {
            Check(secondsPerUnit, capacityUnits);

            long anchor = anchorAt.ToUnixTimeSeconds();
            long elapsed = Math.Max(0, now.ToUnixTimeSeconds() - anchor);
            int units = (int)Math.Min(capacityUnits, elapsed / secondsPerUnit);

            if (units >= capacityUnits) return new StationReading(capacityUnits, null, null);

            return new StationReading(
                units,
                At(anchor + (units + 1L) * secondsPerUnit),
                At(anchor + (long)capacityUnits * secondsPerUnit));
        }

        /// <summary>
        /// Where the anchor goes once everything stored at <paramref name="now"/> has been collected.
        ///
        /// 满了从现在算 / A full store restarts from now: it stopped when it filled, and the time it
        /// sat full is not owed to anyone. Otherwise the anchor moves past the units taken and keeps
        /// the part of a unit already made, so collecting early never costs progress.
        /// </summary>
        public static DateTimeOffset AnchorAfterCollect(
            DateTimeOffset anchorAt, int secondsPerUnit, int capacityUnits, DateTimeOffset now)
        {
            int units = Read(anchorAt, secondsPerUnit, capacityUnits, now).StoredUnits;

            return units >= capacityUnits
                ? WholeSeconds(now)
                : At(anchorAt.ToUnixTimeSeconds() + (long)units * secondsPerUnit);
        }

        /// <summary>
        /// 升级后 / Where the anchor goes when a station changes level, after its store was collected
        /// (<paramref name="collectedAnchor"/> is what <see cref="AnchorAfterCollect"/> returned).
        /// The part of a unit already made carries over, up to one second short of a unit at the new
        /// level - so an upgrade never hands out a unit on the spot.
        /// </summary>
        public static DateTimeOffset AnchorAtNewLevel(
            DateTimeOffset collectedAnchor, int newSecondsPerUnit, DateTimeOffset now)
        {
            Check(newSecondsPerUnit, 1);

            long current = now.ToUnixTimeSeconds();
            long made = Math.Max(0, current - collectedAnchor.ToUnixTimeSeconds());
            return At(current - Math.Min(made, newSecondsPerUnit - 1L));
        }

        /// <summary><paramref name="time"/> without its fraction of a second, in UTC.</summary>
        public static DateTimeOffset WholeSeconds(DateTimeOffset time) => At(time.ToUnixTimeSeconds());

        private static DateTimeOffset At(long unixSeconds) => DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

        private static void Check(int secondsPerUnit, int capacityUnits)
        {
            if (secondsPerUnit < 1) throw new ArgumentOutOfRangeException(nameof(secondsPerUnit), secondsPerUnit, "At least one second.");
            if (capacityUnits < 1) throw new ArgumentOutOfRangeException(nameof(capacityUnits), capacityUnits, "At least one unit.");
        }
    }
}
