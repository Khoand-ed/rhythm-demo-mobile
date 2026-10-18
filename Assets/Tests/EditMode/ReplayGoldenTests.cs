using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// 两边同一个结果 / The golden input trace in server/tests/golden, judged here by RunJudge on Unity's
// runtime with the chart asset the device plays, and pinned to the numbers the server's own test
// (RunJudgeTests.The_golden_trace_replays_to_its_pinned_result) pins on .NET.
//
// 为什么要两边都钉 / The server flags any result its replay does not reproduce. If the two runtimes
// ever judged the same frames differently - a float rounded another way, an order that depends on
// the platform - every honest device would be flagged, and one of these two tests fails first.
public class ReplayGoldenTests
{
    private const string ChartPath = "Assets/Beatmaps/song_002/hard.asset";

    [Test]
    public void TheGoldenTrace_ReplaysToTheServersPinnedResult()
    {
        string root = Path.Combine(Application.dataPath, "..");
        Ruleset rules = JsonUtility.FromJson<Ruleset>(File.ReadAllText(Path.Combine(root, "server", "data", "ruleset.json")));
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, "server", "tests", "golden", "replay_stage_AIW_hard.trace"));

        Assert.IsTrue(InputTrace.TryRead(bytes, InputTraceWriter.MaxFrames, out InputTrace trace, out string error), error);

        SongChart chart = AssetDatabase.LoadAssetAtPath<SongChart>(ChartPath);
        Assert.IsNotNull(chart, ChartPath + " is missing - run Beatmap/Import All Beatmaps");

        OperatorRules op = rules.FindOperator("NOVA").AtLevel(10);
        RunState state = CreateRun(rules, op, 12345);
        JudgeSettings judge = ScriptableObject.CreateInstance<JudgeSettings>();
        rules.judge.ApplyTo(judge);

        RunJudge run = new RunJudge(chart.notes, trace.LaneCount, judge, rules.score, state);
        run.Reset();

        for (int i = 0; i < trace.FrameCount && !run.Failed; i++)
        {
            run.Step(trace.Times[i], trace.Pressed[i], trace.Held[i], null);
        }

        Assert.AreEqual(370949, state.score, "score");
        Assert.AreEqual(479, state.maxCombo, "max combo");
        Assert.AreEqual(212, Mathf.RoundToInt(state.perfectHits), "perfect");
        Assert.AreEqual(109, Mathf.RoundToInt(state.goodHits), "great");
        Assert.AreEqual(158, Mathf.RoundToInt(state.normalHits), "hit");
        Assert.AreEqual(0, Mathf.RoundToInt(state.missedHits), "miss");
        Assert.IsTrue(state.IsFullCombo, "full combo");
        Assert.IsFalse(state.failed, "failed");
    }

    /// <summary>
    /// A RunState set up the way GameManager.SyncTuning sets one up - the server's
    /// RulesetLoader.CreateRun, written again here because that one is server code.
    /// </summary>
    private static RunState CreateRun(Ruleset rules, OperatorRules op, long seed)
    {
        HealthSettings health = ScriptableObject.CreateInstance<HealthSettings>();
        rules.health.ApplyTo(health);
        FeverSettings fever = ScriptableObject.CreateInstance<FeverSettings>();
        rules.fever.ApplyTo(fever);

        PassiveSO passive = null;
        if (op.passive != null && !op.passive.IsNone)
        {
            passive = (PassiveSO)ScriptableObject.CreateInstance(typeof(PassiveSO).Assembly.GetType(op.passive.type));
            passive.ReadRules(op.passive.parameters);
        }

        RunState run = new RunState
        {
            health = health,
            fever = fever,
            multiplierThresholds = (int[])rules.score.multiplierThresholds.Clone(),
            scoreModifier = op.scoreModifier,
            feverModifier = op.feverModifier,
            operatorMaxHp = op.maxHp,
            passive = passive,
            seed = seed,
        };

        run.Reset();
        return run;
    }
}
