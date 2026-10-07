using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Promuse.Api.Runs;
using Promuse.Contracts.Runs;
using Promuse.Persistence;
using Promuse.Scoring.Rules;

namespace Promuse.Api.Tests;

/// <summary>
/// Results that look like someone played, built from the same game data the server loaded.
///
/// 用服务端自己的数据 / The chart and rules come from the running server's GameData, so a test
/// result is checked against exactly what it was built from.
/// </summary>
internal static class Played
{
    public const string Stage = "stage_001";
    public const string Operator = "PULSE";

    public static GameData Data(PromuseApiFactory factory) => factory.Services.GetRequiredService<GameData>();

    /// <summary>
    /// A clean win: every judgement a Perfect, a full combo, the fingerprint the server expects,
    /// and a trace of presses with a person's spread. The score is whatever the test needs - the
    /// review bounds it from above, it does not recompute it.
    /// </summary>
    public static RunResult Win(PromuseApiFactory factory, int score = 1000, string stage = Stage,
                                string characterId = Operator, double jitterMs = 25)
    {
        GameData data = Data(factory);
        ScoringChart chart = data.ChartFor(stage)!;
        OperatorRules op = data.Rules.FindOperator(characterId)!;
        int total = chart.JudgementCount;

        return new RunResult(
            score, total, total, 0, 0, 0, true,
            RulesetFingerprint.Compute(data.Rules, op),
            TraceCodec.Pack(Trace(chart, jitterMs)));
    }

    /// <summary>
    /// Makes a run look opened <paramref name="by"/> ago. A won result is refused if it closes sooner than the
    /// chart takes to play, and no test should wait 36 seconds for a song.
    /// </summary>
    public static async Task AgeAsync(PromuseApiFactory factory, Guid runId, TimeSpan by)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromuseDbContext>();

        await db.Runs
            .Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.StartedAt, DateTimeOffset.UtcNow - by));
    }

    /// <summary>A chart's worth of frames at 60 fps, one press per note, normally distributed error.</summary>
    public static byte[] Trace(ScoringChart chart, double jitterMs = 25, int seed = 11)
    {
        var random = new Random(seed);
        const float dt = 1f / 60f;
        float first = Math.Max(0f, chart.FirstNoteTime - 1f);
        float last = chart.LastJudgementTime + 0.5f;

        int frames = (int)Math.Ceiling((last - first) / dt) + 1;
        var pressed = new byte[frames];

        foreach (NoteData note in chart.Notes)
        {
            double u1 = 1.0 - random.NextDouble(), u2 = random.NextDouble();
            double error = jitterMs / 1000.0 * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);

            int[] lanes = note.type == NoteType.Twin ? [0, 1] : [note.lane];
            foreach (int lane in lanes)
            {
                int frame = Math.Clamp((int)Math.Round((note.hitTime + error - first) / dt), 0, frames - 1);
                while (frame < frames - 1 && (pressed[frame] & (1 << lane)) != 0) frame++;
                pressed[frame] |= (byte)(1 << lane);
            }
        }

        var writer = new InputTraceWriter();
        writer.Begin(2);
        for (int i = 0; i < frames; i++) writer.Record(first + i * dt, pressed[i], 0);
        return writer.ToArray();
    }
}
