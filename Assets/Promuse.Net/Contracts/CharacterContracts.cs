// Compiled by both sides - see the note at the top of ApiProblem.cs. C# 9 syntax
// is deliberate: Unity 6 compiles at that level.

#nullable enable

using System.Collections.Generic;
using Promuse.Contracts.Players;

namespace Promuse.Contracts.Characters
{
    /// <summary>
    /// 干员升级的规则 / How an operator levels: the cap, the EXP each level takes, what a battle
    /// record is worth and what the LMD costs. One definition, compiled into the game and the
    /// server, so the level-up screen previews exactly what the server will do.
    ///
    /// 只到当前上限 / Levelling stops at the current elite phase's cap. Promotion, which would raise
    /// it, is a separate feature and not built yet.
    /// </summary>
    public static class OperatorLevels
    {
        public const int LmdItemId = 2;

        /// <summary>The battle records, smallest first. Each is spent whole.</summary>
        public static readonly IReadOnlyList<ExpRecord> Records = new[]
        {
            new ExpRecord(11, 200),
            new ExpRecord(14, 400),
            new ExpRecord(15, 1000),
            new ExpRecord(16, 2000),
        };

        /// <summary>The EXP one of <paramref name="itemId"/> gives, or 0 for an item that is not a record.</summary>
        public static int ExpOf(int itemId)
        {
            foreach (ExpRecord record in Records)
            {
                if (record.ItemId == itemId) return record.Exp;
            }

            return 0;
        }

        /// <summary>The highest elite phase a rarity reaches. -1 for a rarity that does not exist.</summary>
        public static int MaxElite(int rarity)
        {
            switch (rarity)
            {
                case 1:
                case 2:
                    return 0;
                case 3:
                    return 1;
                case 4:
                case 5:
                case 6:
                    return 2;
                default:
                    return -1;
            }
        }

        /// <summary>The level cap at an elite phase. -1 for a combination that does not exist.</summary>
        public static int MaxLevel(int rarity, int elite)
        {
            switch (rarity)
            {
                case 1:
                case 2:
                    return elite == 0 ? 30 : -1;
                case 3:
                    return elite == 0 ? 40 : elite == 1 ? 55 : -1;
                case 4:
                    return elite == 0 ? 45 : elite == 1 ? 60 : elite == 2 ? 70 : -1;
                case 5:
                    return elite == 0 ? 50 : elite == 1 ? 70 : elite == 2 ? 80 : -1;
                case 6:
                    return elite == 0 ? 50 : elite == 1 ? 80 : elite == 2 ? 90 : -1;
                default:
                    return -1;
            }
        }

        /// <summary>The EXP that takes <paramref name="level"/> to the next one.</summary>
        public static int ExpToNext(int elite, int level)
        {
            switch (elite)
            {
                case 0:
                    return 100 + level * 20;
                case 1:
                    return 120 + level * 60;
                case 2:
                    return 120 + level * 120;
                default:
                    return -1;
            }
        }

        /// <summary>
        /// 龙门币按经验收 / The LMD a level-up costs: a share of the EXP it actually puts into the
        /// operator, rounded up - 3 in 10 at elite 0, 5 at elite 1, 8 at elite 2. EXP lost past the
        /// cap costs nothing.
        /// </summary>
        public static int LmdFor(int elite, int appliedExp)
        {
            int tenths = elite <= 0 ? 3 : elite == 1 ? 5 : 8;
            return (appliedExp * tenths + 9) / 10;
        }

        /// <summary>The EXP still needed to reach <paramref name="maxLevel"/> from where the operator is.</summary>
        public static int ExpToCap(int elite, int level, int exp, int maxLevel)
        {
            int total = 0;
            for (int l = level; l < maxLevel; l++) total += ExpToNext(elite, l);
            return total > exp ? total - exp : 0;
        }

        /// <summary>
        /// 加经验 / Adds <paramref name="addedExp"/> to an operator at (<paramref name="level"/>,
        /// <paramref name="exp"/>): levels up while the EXP covers the next level, stops at the cap.
        /// At the cap the EXP bar is empty and whatever is left over is wasted.
        /// </summary>
        public static LevelGain Gain(int elite, int level, int exp, int maxLevel, int addedExp)
        {
            int applied = 0;
            int left = addedExp;

            while (level < maxLevel && left > 0)
            {
                int need = ExpToNext(elite, level) - exp;
                if (left < need)
                {
                    exp += left;
                    applied += left;
                    left = 0;
                    break;
                }

                left -= need;
                applied += need;
                level++;
                exp = 0;
            }

            if (level >= maxLevel) exp = 0;

            return new LevelGain(level, exp, applied, left);
        }
    }

    /// <param name="ItemId">The record's item id.</param>
    /// <param name="Exp">The EXP one of it gives.</param>
    public sealed record ExpRecord(int ItemId, int Exp);

    /// <param name="Level">The level reached.</param>
    /// <param name="Exp">The EXP into that level; 0 at the cap.</param>
    /// <param name="Applied">The EXP that went into the operator.</param>
    /// <param name="Wasted">The EXP past the cap, which goes nowhere.</param>
    public sealed record LevelGain(int Level, int Exp, int Applied, int Wasted);

    /// <summary>
    /// 用哪些记录 / The battle records to spend, by item id and count. The level reached is not
    /// stated - it follows from the EXP, which the server adds up itself.
    /// </summary>
    public sealed record LevelUpRequest(IReadOnlyList<ItemStack> Records);

    /// <param name="CharacterId">The operator levelled.</param>
    /// <param name="FromLevel">Its level before.</param>
    /// <param name="ToLevel">Its level now; also in <paramref name="Player"/>.</param>
    /// <param name="ExpApplied">The EXP that went into the operator.</param>
    /// <param name="LmdSpent">The LMD it cost.</param>
    /// <param name="Player">The whole save after: the operator, the records and the LMD.</param>
    public sealed record LevelUpResult(
        string CharacterId,
        int FromLevel,
        int ToLevel,
        int ExpApplied,
        int LmdSpent,
        PlayerState Player);
}
