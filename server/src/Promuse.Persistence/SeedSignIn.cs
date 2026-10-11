using System.Text.Json;
using Promuse.Contracts.Players;

namespace Promuse.Persistence;

/// <summary>
/// 默认签到表 / The sign-in squares every month uses until an administrator sets that month's own.
/// Thirty-one, so the longest month has one each; a shorter month uses the first ones.
/// Every seventh square is the week's bigger reward.
/// </summary>
public static class SeedSignIn
{
    public const string DefaultKey = "default";

    private const int Orundum = 1, Lmd = 2, Orirock = 6, Certificate = 8, Permit = 10,
                      BattleRecord = 11, Recruitment = 12, SkillSummary = 13;

    public static IReadOnlyList<ItemStack> Default()
    {
        (int Item, int Amount)[] week =
        [
            (Lmd, 2000), (BattleRecord, 5), (Orirock, 10), (Recruitment, 1), (SkillSummary, 3), (Lmd, 3000),
        ];

        var squares = new List<ItemStack>();
        for (int day = 1; day <= 31; day++)
        {
            squares.Add(day % 7 == 0
                ? new ItemStack(day == 28 ? Permit : Orundum, day == 28 ? 1 : 100 * (day / 7))
                : day == 31
                    ? new ItemStack(Certificate, 10)
                    : new ItemStack(week[(day - 1) % 7].Item, week[(day - 1) % 7].Amount));
        }

        return squares;
    }

    public static string DefaultJson => JsonSerializer.Serialize(Default(), SeedEvents.Json);
}
