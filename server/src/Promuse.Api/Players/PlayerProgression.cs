using System.ComponentModel.DataAnnotations;
using Promuse.Contracts.Players;

namespace Promuse.Api.Players;

/// <summary>
/// The two curves the client already has, ported.
///
/// ⚠ 这是重复的 / These duplicate <c>PlayerData.GetMaxExp</c> and
/// <c>PlayerData.GetMaxReason</c>, and that duplication is a known cost rather
/// than an oversight. Those two are <c>public static</c> pure functions, so they
/// could be linked the way Promuse.Scoring links RunState - but they live on a
/// ScriptableObject inside the Arknights assembly, tangled with CharData,
/// ItemStack and Unity types, and untangling them is a change to game code that
/// this server-side change has no business making.
///
/// 怎么处理 / Until they are extracted: the tests pin both against values taken
/// from the client's formulas, so a divergence fails here rather than showing up
/// as a level bar that disagrees with the server. If the curves are retuned,
/// retune them here too and the tests will say whether you got it right.
/// </summary>
public static class PlayerProgression
{
    /// <summary>Exp needed to leave <paramref name="level"/>. -1 past the cap.</summary>
    public static int MaxExp(int level)
    {
        if (level < 1) return 500;
        if (level < 2) return 800;
        if (level < 28) return 80 * level + 1000;
        if (level < 34) return 100 * level + 160;
        if (level < 50) return 300 * level - 6300;
        if (level < 65) return 500 * level - 16500;
        if (level < 101) return 1000 * level - 49000;
        if (level < 110) return 2000 * level - 150000;
        if (level < 120) return 3000 * level - 260000;

        return -1;
    }

    /// <summary>
    /// Stamina ceiling for a level. Note the integer division at the third branch
    /// and the double at the fifth - both are the client's, and changing either
    /// to "tidy" it moves the curve.
    /// </summary>
    public static int MaxStamina(int level)
    {
        if (level < 5) return 80 + 2 * level;
        if (level < 35) return 85 + level;
        if (level < 85) return 113 + level / 5;
        if (level < 100) return 130;
        if (level < 120) return (int)(105.75 + level / 4D);

        return -1;
    }
}

public sealed class StaminaOptions
{
    public const string SectionName = "Stamina";

    /// <summary>
    /// 客户端从来没有这个数 / A number that did not exist anywhere in the project:
    /// the client spends stamina in SelectDungeonUI and never gives any back, so
    /// the refill rate is decided here for the first time.
    ///
    /// At three minutes a point, a level-1 bar of 82 refills from empty in about
    /// four hours.
    /// </summary>
    [Range(1, 60)]
    public int MinutesPerPoint { get; set; } = 3;
}

/// <summary>
/// 纯函数 / Deliberately pure: takes the stored value, the moment it was true and
/// the current time, and returns what the bar reads now. No clock of its own and
/// no database, so every branch below is testable without either.
/// </summary>
public static class StaminaCalculator
{
    public static Stamina Compute(
        int stored, DateTimeOffset storedAt, int max, TimeSpan perPoint, DateTimeOffset now)
    {
        // 超上限不回复 / At or over the ceiling nothing accrues. Over is a real
        // state, not a bug: an item can push the bar past its cap, and that
        // surplus must not be silently trimmed on the next read.
        if (stored >= max) return new Stamina(stored, max, null, null);

        // A stored timestamp in the future means a clock moved, not that the
        // player owes time. Clamp rather than subtract points.
        TimeSpan elapsed = now > storedAt ? now - storedAt : TimeSpan.Zero;

        long gained = perPoint > TimeSpan.Zero ? elapsed.Ticks / perPoint.Ticks : 0;
        int current = (int)Math.Min(max, stored + gained);

        if (current >= max) return new Stamina(max, max, null, null);

        // 从锚点算, 不是从现在 / Both deadlines are measured from the anchor, not
        // from now. Reading the value must not move it, or two reads a second
        // apart would report two different times for the same next point.
        return new Stamina(
            current,
            max,
            NextPointAt: storedAt + perPoint * (gained + 1),
            FullAt: storedAt + perPoint * (max - stored));
    }
}
