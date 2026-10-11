namespace Promuse.Persistence.Entities;

/// <summary>
/// 主线 / The main campaign: chapters, their stages in order, what each pays. One document, edited
/// whole on the admin page - the same shape as a live event. There is one row, keyed "main".
/// </summary>
public class Campaign
{
    public string Key { get; set; } = string.Empty;

    /// <summary>Bumped on every save; the ETag an admin edit must match.</summary>
    public int Version { get; set; }

    /// <summary>The CampaignDefinition as JSON (jsonb).</summary>
    public string Definition { get; set; } = "{}";

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

/// <summary>
/// 打过的主线关卡 / A campaign stage a player has cleared from the campaign screen. The row existing
/// is what unlocks the next stage and what makes the first-clear Orundum pay once: of two clears
/// racing, only one inserts it.
/// </summary>
public class CampaignClear
{
    public Guid AccountId { get; set; }

    public string StageId { get; set; } = string.Empty;

    public int Clears { get; set; }

    public DateTimeOffset FirstClearedAt { get; set; }
}
