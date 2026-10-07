using Promuse.Contracts.Runs;

namespace Promuse.Persistence.Entities;

/// <summary>
/// What a run's result said, what the server made of it, and the raw input behind it.
///
/// 什么都留 / Kept whatever the verdict. A rejected result is evidence - of a cheat, or of a bug
/// in the checks that rejected it - and either way the only way to tell which is to still have
/// it. The same goes for the trace: nothing replays it yet, and the replay that will is only
/// worth building if the runs it should judge were kept.
///
/// 和 runs 一对一 / One per run, in its own table rather than as columns on runs: a trace is tens
/// of kilobytes, and every query that lists runs would otherwise drag them along.
/// </summary>
public class RunScore
{
    public Guid RunId { get; set; }

    public Run? Run { get; set; }

    /// <summary>Copied from the run, so a player's history is one index away.</summary>
    public Guid AccountId { get; set; }

    public string StageId { get; set; } = string.Empty;

    /// <summary>What the device claimed. <see cref="Run.Won"/> holds what the server counted.</summary>
    public bool ClaimedWon { get; set; }

    public int Score { get; set; }
    public int MaxCombo { get; set; }
    public int Perfect { get; set; }
    public int Great { get; set; }
    public int Hit { get; set; }
    public int Miss { get; set; }
    public bool FullCombo { get; set; }

    public ScoreVerdict Verdict { get; set; }

    /// <summary>Rejection codes, as the client was told them.</summary>
    public string[] Reasons { get; set; } = [];

    /// <summary>Statistical signals. Never sent to the client.</summary>
    public string[] Flags { get; set; } = [];

    /// <summary>The bound the score was held to. Null when the operator was unknown.</summary>
    public int? Ceiling { get; set; }

    public string RulesetFingerprint { get; set; } = string.Empty;

    /// <summary>The trace exactly as it arrived - gzipped - for the replay to read later.</summary>
    public byte[]? Trace { get; set; }

    public int TraceFrames { get; set; }
    public int Presses { get; set; }

    public int? TimingSamples { get; set; }
    public double? TimingMeanMs { get; set; }
    public double? TimingStdDevMs { get; set; }
    public double? FrameIntervalMs { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }
}
