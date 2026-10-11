namespace Promuse.Persistence.Entities;

/// <summary>
/// 签到奖励表 / The squares of the monthly sign-in: "default", or one month ("2026-10") that
/// overrides it. A month with no table of its own uses the default.
/// </summary>
public class SignInTable
{
    public string Key { get; set; } = string.Empty;

    /// <summary>A list of ItemStack, square 1 first (jsonb).</summary>
    public string Rewards { get; set; } = "[]";

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

/// <summary>
/// One square claimed. Keyed by (player, month, square), so a square pays once; the server day it
/// was claimed on is unique per player, so at most one square a day - both enforced by the database.
/// </summary>
public class SignInClaim
{
    public Guid AccountId { get; set; }

    public string MonthKey { get; set; } = string.Empty;

    public int Day { get; set; }

    public string PeriodKey { get; set; } = string.Empty;

    public DateTimeOffset ClaimedAt { get; set; }
}
