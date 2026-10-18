using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Promuse.Api.Runs;
using Promuse.Contracts.Runs;
using Promuse.Persistence;
using Promuse.Scoring.Review;
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
    public const string Operator = "AMIYA";

    public static GameData Data(PromuseApiFactory factory) => factory.Services.GetRequiredService<GameData>();

    /// <summary>
    /// A win as an honest device sends it: a trace of presses with a person's spread, the
    /// fingerprint the server expects, and the numbers that trace scores - judged by the server's
    /// own replay, so the review finds nothing to flag. <paramref name="misses"/> leaves the
    /// first notes unpressed, for a lower score than a clean run; a test that needs a score the
    /// input did not make says so with <c>with { Score = ... }</c>. Played at
    /// <paramref name="level"/>, which is part of the rules the fingerprint names, and rolled from
    /// <paramref name="seed"/> - the run's - for an operator whose passive rolls.
    /// </summary>
    public static RunResult Win(PromuseApiFactory factory, int misses = 0, string stage = Stage,
                                string characterId = Operator, double jitterMs = 25, int level = 1, long seed = 0)
    {
        GameData data = Data(factory);
        ScoringChart chart = data.ChartFor(stage)!;
        OperatorRules op = data.Rules.FindOperator(characterId)!.AtLevel(level);

        byte[] trace = Trace(chart, jitterMs, misses: misses);
        Assert.True(InputTrace.TryRead(trace, InputTraceWriter.MaxFrames, out InputTrace? parsed, out string? error), error);
        ReplayResult played = RunReplay.Play(chart, data.Rules, op, seed, parsed!);
        Assert.False(played.Failed, $"{misses} misses empty {characterId}'s HP; a win cannot have that many");

        return new RunResult(
            played.Score, played.MaxCombo, played.Perfect, played.Great, played.Hit, played.Miss, played.FullCombo,
            RulesetFingerprint.Compute(data.Rules, op),
            TraceCodec.Pack(trace));
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

    /// <summary>
    /// A chart's worth of frames at 60 fps, one press per note with normally distributed error,
    /// a finger kept down through a hold, and the first <paramref name="misses"/> notes left alone.
    /// </summary>
    public static byte[] Trace(ScoringChart chart, double jitterMs = 25, int seed = 11, int misses = 0)
    {
        var random = new Random(seed);
        const float dt = 1f / 60f;
        float first = Math.Max(0f, chart.FirstNoteTime - 1f);
        float last = chart.LastJudgementTime + 0.5f;

        int frames = (int)Math.Ceiling((last - first) / dt) + 1;
        var pressed = new byte[frames];
        var held = new byte[frames];
        int n = 0;

        foreach (NoteData note in chart.Notes)
        {
            double u1 = 1.0 - random.NextDouble(), u2 = random.NextDouble();
            double error = jitterMs / 1000.0 * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);

            if (n++ < misses) continue;

            int[] lanes = note.type == NoteType.Twin ? [0, 1] : [note.lane];
            foreach (int lane in lanes)
            {
                int frame = Math.Clamp((int)Math.Round((note.hitTime + error - first) / dt), 0, frames - 1);
                while (frame < frames - 1 && (pressed[frame] & (1 << lane)) != 0) frame++;
                pressed[frame] |= (byte)(1 << lane);
                held[frame] |= (byte)(1 << lane);

                if (note.type == NoteType.Hold && note.duration > 0f)
                {
                    float release = note.hitTime + note.duration;
                    for (int f = frame; f < frames && first + (f - 1) * dt < release; f++) held[f] |= (byte)(1 << lane);
                }
            }
        }

        var writer = new InputTraceWriter();
        writer.Begin(2);
        for (int i = 0; i < frames; i++) writer.Record(first + i * dt, pressed[i], held[i]);
        return writer.ToArray();
    }
}
