using System.Security.Cryptography;

namespace Promuse.Api.Gacha;

/// <summary>A source of uniform integers in [0, max). Swapped out by tests that need to steer a roll.</summary>
public interface IGachaDice
{
    int Next(int max);
}

/// <summary>
/// 用密码学随机数 / The operating system's cryptographic generator rather than System.Random.
/// System.Random is predictable from its seed and its output; a pull is worth real money to the
/// player, and its result should not be something an observer of earlier pulls can forecast.
/// </summary>
public sealed class SecureGachaDice : IGachaDice
{
    public int Next(int max) => RandomNumberGenerator.GetInt32(max);
}

/// <summary>The odds of one banner, in basis points.</summary>
public sealed record GachaOdds(int FiveStar, int FourStar, int ThreeStar, int PityThreshold)
{
    public const int Whole = 10000;
}

public readonly record struct PoolMember(string CharacterId, int Rarity);

/// <param name="Guaranteed">Given by the pity counter rather than by the odds.</param>
public readonly record struct GachaRoll(PoolMember Member, bool Guaranteed);

/// <summary>
/// 抽一次的规则 / What a single pull gives - nothing else. No database, no money, no clock, so
/// every rule the Details screen promises can be pinned by a test that runs in microseconds.
///
/// 两步 / Two draws: a tier from the odds, then an operator uniformly within that tier - which is
/// exactly the "tier rate split evenly across the tier" the Details screen prints.
///
/// 保底 / A counter of pulls since the last 5-star. When it has reached the threshold the pull is
/// a 5-star without consulting the odds; any 5-star, guaranteed or lucky, sets it back to zero.
/// Inside a ten-pull every pull counts on its own, so a guarantee can land on the third card.
/// </summary>
public static class GachaRoller
{
    public static GachaRoll Next(GachaOdds odds, IReadOnlyList<PoolMember> pool, ref int pity, IGachaDice dice)
    {
        bool guaranteed = pity >= odds.PityThreshold;
        int rarity = guaranteed ? 5 : Tier(odds, dice.Next(GachaOdds.Whole));

        List<PoolMember> tier = pool.Where(m => m.Rarity == rarity).ToList();

        // Validate() rules this out for a banner that is served; reaching it means a banner was
        // changed underneath a running server, and guessing another tier would be inventing odds.
        if (tier.Count == 0)
        {
            throw new InvalidOperationException($"The pool has no {rarity}-star operator to give.");
        }

        PoolMember member = tier[dice.Next(tier.Count)];
        pity = rarity == 5 ? 0 : pity + 1;

        return new GachaRoll(member, guaranteed);
    }

    /// <summary>
    /// A banner that cannot keep its promises: odds that do not add up, a threshold below one, or
    /// a tier with a chance of being drawn and nobody in it. Null when it is sound.
    /// </summary>
    public static string? Validate(GachaOdds odds, IReadOnlyList<PoolMember> pool)
    {
        if (odds.FiveStar < 0 || odds.FourStar < 0 || odds.ThreeStar < 0)
        {
            return "A tier rate is negative.";
        }

        if (odds.FiveStar + odds.FourStar + odds.ThreeStar != GachaOdds.Whole)
        {
            return $"The tier rates add up to {odds.FiveStar + odds.FourStar + odds.ThreeStar}, not {GachaOdds.Whole}.";
        }

        if (odds.PityThreshold < 1) return "The pity threshold must be at least 1.";

        // 保底一定会给五星 / The guarantee always gives a 5-star, so one has to exist even when
        // the 5-star rate itself is zero.
        if (pool.All(m => m.Rarity != 5)) return "The pool has no 5-star for the guarantee to give.";

        foreach ((int rarity, int rate) in new[] { (4, odds.FourStar), (3, odds.ThreeStar) })
        {
            if (rate > 0 && pool.All(m => m.Rarity != rarity))
            {
                return $"{rarity}-star has a {rate} in {GachaOdds.Whole} chance but nobody in the pool.";
            }
        }

        return null;
    }

    private static int Tier(GachaOdds odds, int roll)
    {
        if (roll < odds.FiveStar) return 5;
        if (roll < odds.FiveStar + odds.FourStar) return 4;
        return 3;
    }
}
