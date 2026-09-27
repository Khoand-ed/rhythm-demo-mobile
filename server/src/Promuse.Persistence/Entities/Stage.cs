namespace Promuse.Persistence.Entities;

/// <summary>
/// What a chart costs to attempt.
///
/// 这个数字以前不存在 / The cost had no home before this. DungeonMeta declares a
/// `reason` field, but no DungeonMeta asset exists in the project at all - the
/// dungeon path is unfinished placeholder content, so SelectDungeonUI was
/// spending a number that came from nowhere.
///
/// 客户端不能报价 / It lives here because a run's price cannot be something the
/// client names, for the same reason a shop price cannot. The client asks to play
/// a stage; the server decides what that costs.
///
/// Phase 5 (content delivery) is where chart metadata gets a real pipeline. Until
/// then this table is the source of truth and a cost change is an UPDATE.
/// </summary>
public class Stage
{
    /// <summary>From <c>SongChart.stageId</c>. The natural key, so it is the key.</summary>
    public string StageId { get; set; } = string.Empty;

    public int StaminaCost { get; set; }

    /// <summary>
    /// A stage can be taken out of rotation without deleting it, so the runs
    /// that reference it keep something to point at.
    /// </summary>
    public bool IsActive { get; set; } = true;
}
