namespace Promuse.Persistence.Entities;

/// <summary>
/// One attempt at a song: opened when the player enters, closed when a result
/// arrives.
///
/// 为什么不是"扣理智"接口 / Deliberately a lifecycle rather than a bare
/// "spend stamina" call. A run has two ends, and the far end is Phase 4 - a
/// result can only be trusted if the server already knows a run was started, how
/// much it cost, and on which chart. An endpoint that merely debited stamina
/// would have to be redesigned into this the moment scores arrive.
///
/// 种子是这里的关键 / <see cref="Seed"/> is why this exists now rather than
/// later. JudgeUpgradePassive rolls Random.value per judgement, so the scoring
/// rules are not deterministic and the server cannot re-simulate a run without
/// knowing the sequence. The server issues the seed here and NEVER accepts one
/// from the client - otherwise a player re-rolls until the draw is favourable.
/// </summary>
public class Run
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Player? Player { get; set; }

    /// <summary>The chart, from SongChart.stageId.</summary>
    public string StageId { get; set; } = string.Empty;

    /// <summary>What entering cost, recorded so a refund knows the amount.</summary>
    public int StaminaSpent { get; set; }

    /// <summary>
    /// Server-generated. Sent to the client once, at start, and kept here so a
    /// submitted result can be replayed against the same sequence.
    /// </summary>
    public long Seed { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// Null while the run is open. A run that is never completed simply stays
    /// open - the stamina is spent either way, exactly as it is when a player
    /// closes the app mid-song.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }
}
