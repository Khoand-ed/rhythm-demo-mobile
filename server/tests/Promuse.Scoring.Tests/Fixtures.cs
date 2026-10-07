using Promuse.Scoring.Rules;

namespace Promuse.Scoring.Tests;

/// <summary>
/// The shipped data, and two ways of producing a run without a device.
///
/// 读仓库里的真文件 / The ruleset and charts are read from where they live in the repository,
/// not copied into the tests: a test that passed against a stale copy would prove nothing
/// about what the server actually loads.
/// </summary>
internal static class Fixtures
{
    public static readonly string[] Stages = ["stage_001", "stage_AIW", "stage_AIW_hard"];

    public static readonly string[] Operators = ["AMIYA", "NOVA", "ECHO", "PULSE"];

    private static readonly Dictionary<string, string> ChartFiles = new()
    {
        ["stage_001"] = Path.Combine("Assets", "Beatmaps", "song_001", "generated.json"),
        ["stage_AIW"] = Path.Combine("Assets", "Beatmaps", "song_002", "easy.json"),
        ["stage_AIW_hard"] = Path.Combine("Assets", "Beatmaps", "song_002", "hard.json"),
    };

    public static string RepoPath(string relative)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Assets"))
                && Directory.Exists(Path.Combine(dir.FullName, "server")))
            {
                return Path.Combine(dir.FullName, relative);
            }
        }

        throw new DirectoryNotFoundException("Could not find the repository root above " + AppContext.BaseDirectory);
    }

    public static Ruleset Ruleset() => RulesetLoader.Load(RepoPath(Path.Combine("server", "data", "ruleset.json")));

    public static ScoringChart Chart(string stageId) => ScoringChart.Load(RepoPath(ChartFiles[stageId]));

    public static OperatorRules Operator(Ruleset rules, string id) =>
        rules.FindOperator(id) ?? throw new InvalidOperationException("No operator " + id);

    /// <summary>
    /// Plays a chart the way GameManager would for a player who hits every note dead on time,
    /// except the judgements listed in <paramref name="missed"/>, which run out unplayed.
    ///
    /// 只用游戏自己的规则 / Every score, combo, fever and passive effect goes through RunState
    /// and the operator's PassiveSO - the device's code - so the totals are the device's. What
    /// this adds is only the order of events in song time, mirroring GameManager: a press is
    /// paid, then its fever gain lands, and a window that opens runs for its duration plus
    /// whatever the passive adds as it opens.
    /// </summary>
    public static PlayedRun Play(ScoringChart chart, Ruleset rules, OperatorRules op, ISet<int>? missed = null)
    {
        missed ??= new HashSet<int>();
        RunState run = rules.CreateRun(op);
        float feverEnds = float.PositiveInfinity;

        // (time, order within the instant, kind, judgement index or -1, note type, hold id)
        var events = new List<(float Time, int Order, int Kind, int Judgement, NoteType Type, int Hold)>();
        int judgement = 0;
        int holdId = 0;

        foreach (NoteData note in chart.Notes)
        {
            if (note.type == NoteType.Twin)
            {
                events.Add((note.hitTime, 0, 0, judgement++, NoteType.Twin, -1));
                events.Add((note.hitTime, 0, 0, judgement++, NoteType.Twin, -1));
            }
            else if (note.type == NoteType.Hold && note.duration > 0f)
            {
                int id = holdId++;
                events.Add((note.hitTime, 0, 0, judgement++, NoteType.Hold, id));

                int ticks = (int)Math.Floor(note.duration / rules.score.holdTickInterval);
                for (int k = 1; k <= ticks; k++)
                {
                    events.Add((note.hitTime + k * rules.score.holdTickInterval, 1, 1, -1, NoteType.Hold, id));
                }

                events.Add((note.hitTime + note.duration, 2, 2, judgement++, NoteType.Hold, id));
            }
            else
            {
                events.Add((note.hitTime, 0, 0, judgement++, note.type, -1));
            }
        }

        var deadHolds = new HashSet<int>();
        int perfect = 0, miss = 0;

        foreach (var e in events.OrderBy(e => e.Time).ThenBy(e => e.Order))
        {
            if (run.feverActive && e.Time >= feverEnds) run.TickFever(feverEnds - e.Time);

            if (e.Kind == 1)
            {
                if (!deadHolds.Contains(e.Hold)) run.HoldTick(rules.score.holdTick);
                continue;
            }

            if (e.Kind == 2 && deadHolds.Contains(e.Hold)) continue;

            if (missed.Contains(e.Judgement))
            {
                // A hold missed at its head is two misses and never gets its body or tail.
                run.NoteMissed(e.Kind == 0 ? e.Type : NoteType.Tap);
                miss += e.Kind == 0 && e.Type == NoteType.Hold ? 2 : 1;
                if (e.Hold >= 0) deadHolds.Add(e.Hold);
                continue;
            }

            run.NoteHit(rules.score.perfect);
            perfect++;

            if (run.AddFever(rules.fever.perfectGain))
            {
                float extra = run.passive != null ? run.passive.ExtraFeverSeconds(run) : 0f;
                feverEnds = e.Time + rules.fever.feverDuration + extra;
            }
        }

        return new PlayedRun(run.score, run.maxCombo, perfect, miss, run.IsFullCombo, run.failed);
    }

    /// <summary>
    /// A trace of someone playing the chart: frames at <paramref name="fps"/> covering the whole
    /// chart, and one press per note on the frame nearest its time plus a normally-distributed
    /// error of <paramref name="jitterMs"/>.
    ///
    /// 0 毫秒就是脚本 / Zero jitter is a script: every press on the frame nearest its note.
    /// </summary>
    public static byte[] Trace(ScoringChart chart, float fps = 60f, double jitterMs = 25, int seed = 7,
                               float start = -1f, float end = -1f, int extraPressesPerNote = 0)
    {
        var random = new Random(seed);
        float dt = 1f / fps;
        float first = start >= 0 ? start : Math.Max(0f, chart.FirstNoteTime - 1f);
        float last = end >= 0 ? end : chart.LastJudgementTime + 0.5f;

        int frames = (int)Math.Ceiling((last - first) / dt) + 1;
        var times = new float[frames];
        var pressed = new byte[frames];
        for (int i = 0; i < frames; i++) times[i] = first + i * dt;

        void Press(int lane, float at)
        {
            int frame = (int)Math.Round((at - first) / dt);
            frame = Math.Clamp(frame, 0, frames - 1);

            // One press per lane per frame is all a frame can carry; slide to a free one.
            while (frame < frames - 1 && (pressed[frame] & (1 << lane)) != 0) frame++;
            pressed[frame] |= (byte)(1 << lane);
        }

        foreach (NoteData note in chart.Notes)
        {
            double error = jitterMs * Gaussian(random) / 1000.0;
            int[] lanes = note.type == NoteType.Twin ? [0, 1] : [note.lane];

            foreach (int lane in lanes)
            {
                Press(lane, note.hitTime + (float)error);
                for (int k = 0; k < extraPressesPerNote; k++) Press(lane, note.hitTime + (k + 1) * 0.03f);
            }
        }

        var writer = new InputTraceWriter();
        writer.Begin(2);
        for (int i = 0; i < frames; i++) writer.Record(times[i], pressed[i], 0);
        return writer.ToArray();
    }

    private static double Gaussian(Random random)
    {
        // Box-Muller; deterministic for a seeded Random.
        double u1 = 1.0 - random.NextDouble();
        double u2 = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}

internal sealed record PlayedRun(int Score, int MaxCombo, int Perfect, int Miss, bool FullCombo, bool Failed);
