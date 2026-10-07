using System.IO;
using NUnit.Framework;
using UnityEngine;

// The formats both runtimes produce, pinned to the values the server's own tests pin.
//
// 两边钉同一个数 / SharedDataTests in server/tests/Promuse.Scoring.Tests asserts these exact
// values on .NET. Each side passing on its own proves little; both passing with the SAME numbers
// is the property that matters, because an honest phone whose fingerprint or rolls differ from
// the server's in one bit would have every run it plays refused.
//
// 为什么钉位 / The rolls are compared as floats, i.e. bit for bit, never as printed text: Unity's
// runtime prints 0.27860111f as "0.2786011" where .NET prints "0.27860111". Same value, different
// text - which is exactly why RulesetFingerprint hashes IEEE bits instead of formatted numbers.
public class RhythmSharedFormatTests
{
    private static Ruleset ShippedRuleset()
    {
        string path = Path.Combine(Application.dataPath, "..", "server", "data", "ruleset.json");
        return JsonUtility.FromJson<Ruleset>(File.ReadAllText(path));
    }

    [Test]
    public void Fingerprint_OfTheShippedRules_IsTheOneTheServerComputes()
    {
        Ruleset rules = ShippedRuleset();

        Assert.AreEqual("856c377e36e8ba6f", RulesetFingerprint.Compute(rules, rules.FindOperator("AMIYA")),
                        "server/tests SharedDataTests.ShippedAmiyaFingerprint pins the same value");
    }

    [Test]
    public void Rolls_FromASeed_AreTheServersBitForBit()
    {
        RunState run = new RunState { seed = 42 };
        run.Reset();

        Assert.AreEqual(0.74156487f, run.NextRoll());
        Assert.AreEqual(0.159910381f, run.NextRoll());
        Assert.AreEqual(0.27860111f, run.NextRoll());
    }

    [Test]
    public void Reset_RewindsTheRolls()
    {
        RunState run = new RunState { seed = 7 };
        run.Reset();
        float first = run.NextRoll();
        run.NextRoll();

        run.Reset();

        Assert.AreEqual(first, run.NextRoll(), "a retry must draw what the first attempt drew");
    }

    [Test]
    public void Trace_ReadsBackWhatWasWritten()
    {
        InputTraceWriter writer = new InputTraceWriter();
        writer.Begin(2);
        writer.Record(0.016666668f, 1, 0);
        writer.Record(0.033333335f, 2, 1);

        InputTrace trace;
        string error;
        Assert.IsTrue(InputTrace.TryRead(writer.ToArray(), 100, out trace, out error), error);

        Assert.AreEqual(2, trace.LaneCount);
        Assert.AreEqual(new[] { 0.016666668f, 0.033333335f }, trace.Times);
        Assert.AreEqual(new byte[] { 1, 2 }, trace.Pressed);
        Assert.AreEqual(new byte[] { 0, 1 }, trace.Held);
    }

    [Test]
    public void Trace_ThatGoesBackInTime_IsRefused()
    {
        InputTraceWriter writer = new InputTraceWriter();
        writer.Begin(2);
        writer.Record(2f, 1, 0);
        writer.Record(1f, 1, 0);

        InputTrace trace;
        string error;
        Assert.IsFalse(InputTrace.TryRead(writer.ToArray(), 100, out trace, out error));
    }
}
