using System;
using System.Globalization;
using Promuse.Contracts.Manufacture;

namespace Promuse.Manufacture {
    /// <summary>
    /// 怎么写给玩家看 / How Manufacture's numbers read on screen: countdowns, a level's pace and its
    /// store. Pure, so the screen's wording is tested without a screen.
    ///
    /// 都从服务端的数来 / Every number here comes from what the server sent - the level's
    /// SecondsPerUnit is already the server's own pace, so a Local server running an hour a minute
    /// reads as "every 0:30" here, and production reads as "every 30:00".
    /// </summary>
    public static class ManufactureFormat {
        /// <summary>"1:12:04" past an hour, "12:31" under one, "0:05" under a minute. Never negative.</summary>
        public static string Countdown(TimeSpan left) {
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            long seconds = (long)Math.Ceiling(left.TotalSeconds);
            long hours = seconds / 3600;
            long minutes = seconds % 3600 / 60;
            long rest = seconds % 60;

            return hours > 0
                ? hours.ToString(CultureInfo.InvariantCulture) + ":" + minutes.ToString("00", CultureInfo.InvariantCulture) + ":" + rest.ToString("00", CultureInfo.InvariantCulture)
                : minutes.ToString(CultureInfo.InvariantCulture) + ":" + rest.ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>A whole amount with thousands separators: "12,340".</summary>
        public static string Amount(long amount) => amount.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>What <paramref name="units"/> of this level come to, in items: 16 units of 50 LMD is 800.</summary>
        public static long Items(StationLevel level, int units) => (long)units * level.UnitAmount;

        /// <summary>"450 / 800": what the store holds against what it can.</summary>
        public static string Stored(StationLevel level, int units) =>
            Amount(Items(level, units)) + " / " + Amount(Items(level, level.CapacityUnits));

        /// <summary>"+50 every 30:00" - a level's pace, as the item count of one unit and how long it takes.</summary>
        public static string Pace(StationLevel level) =>
            "+" + Amount(level.UnitAmount) + " every " + Countdown(TimeSpan.FromSeconds(level.SecondsPerUnit));

        /// <summary>"Holds 800 (8:00:00)": a full store, and how long it takes to fill from empty.</summary>
        public static string Holds(StationLevel level) =>
            "Holds " + Amount(Items(level, level.CapacityUnits)) + " (" +
            Countdown(TimeSpan.FromSeconds((double)level.SecondsPerUnit * level.CapacityUnits)) + ")";

        /// <summary>
        /// How far the unit being made has come, 0 to 1. A full store reads 1 - nothing is being made,
        /// and the bar should say the store is full rather than empty.
        /// </summary>
        public static float Progress(StationLevel level, StationReading reading, DateTimeOffset serverNow) {
            if (reading.NextUnitAt == null) return 1f;
            double left = (reading.NextUnitAt.Value - serverNow).TotalSeconds;
            double done = 1.0 - left / level.SecondsPerUnit;
            return (float)Math.Max(0.0, Math.Min(1.0, done));
        }
    }
}
