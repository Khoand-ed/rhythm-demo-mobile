using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// The first tests in this project. Everything here is pure logic: no scene, no
// Editor, no audio, so it runs in the Test Runner and on CI in well under a second.
//
// The assertions compare against the settings fields rather than against literal
// numbers, so retuning a window or a damage value does not turn every test red.
// What is being tested is the mapping and the boundary behaviour, not the tuning.
public class JudgeSettingsTests
{
    private JudgeSettings judge;

    [SetUp]
    public void SetUp()
    {
        judge = new JudgeSettings();
    }

    // Grade compares with <=, so a delta sitting exactly on a window edge belongs
    // to the tighter tier. Getting this backwards would silently downgrade every
    // frame-perfect hit in the game.
    [Test]
    public void Grade_TapExactlyOnPerfectEdge_IsPerfect()
    {
        Assert.AreEqual(Judgement.Perfect, judge.Grade(NoteType.Tap, judge.tapPerfect));
    }

    [Test]
    public void Grade_TapJustPastPerfectEdge_IsGreat()
    {
        Assert.AreEqual(Judgement.Great, judge.Grade(NoteType.Tap, judge.tapPerfect + 0.001f));
    }

    [Test]
    public void Grade_TapExactlyOnGreatEdge_IsGreat()
    {
        Assert.AreEqual(Judgement.Great, judge.Grade(NoteType.Tap, judge.tapGreat));
    }

    [Test]
    public void Grade_TapExactlyOnHitEdge_IsHit()
    {
        Assert.AreEqual(Judgement.Hit, judge.Grade(NoteType.Tap, judge.tapHit));
    }

    [Test]
    public void Grade_TapPastHitEdge_IsMiss()
    {
        Assert.AreEqual(Judgement.Miss, judge.Grade(NoteType.Tap, judge.tapHit + 0.001f));
    }

    // Twin has no branch of its own in Grade, so it must fall through to the tap
    // path. If someone adds a Twin case later, this is what catches the drift.
    [Test]
    public void Grade_TwinMatchesTapAtEveryTier()
    {
        foreach (float delta in new[] { 0f, judge.tapPerfect, judge.tapGreat, judge.tapHit, 1f })
        {
            Assert.AreEqual(judge.Grade(NoteType.Tap, delta), judge.Grade(NoteType.Twin, delta),
                            $"Twin and Tap disagree at delta {delta}");
        }
    }

    // A hold head has no Hit tier: anything past its Great window is a miss
    // outright. This is the rule most likely to be "fixed" by mistake.
    [Test]
    public void Grade_HoldHasNoHitTier()
    {
        Assert.AreEqual(Judgement.Perfect, judge.Grade(NoteType.Hold, judge.holdPerfect));
        Assert.AreEqual(Judgement.Great, judge.Grade(NoteType.Hold, judge.holdGreat));
        Assert.AreEqual(Judgement.Miss, judge.Grade(NoteType.Hold, judge.holdGreat + 0.001f));
    }

    [Test]
    public void MaxWindow_MatchesTheWidestTierOfEachType()
    {
        Assert.AreEqual(judge.holdGreat, judge.MaxWindow(NoteType.Hold), 1e-6f);
        Assert.AreEqual(judge.tapHit, judge.MaxWindow(NoteType.Tap), 1e-6f);
        Assert.AreEqual(judge.tapHit, judge.MaxWindow(NoteType.Twin), 1e-6f);
    }

    // The spawner uses MaxWindow to decide when a note is unrecoverable, and Grade
    // to score the press. If a delta inside MaxWindow could still grade as a Miss,
    // a note would be consumed for no score.
    [Test]
    public void AnyDeltaInsideMaxWindow_Grades_AsAHit()
    {
        foreach (NoteType type in new[] { NoteType.Tap, NoteType.Hold, NoteType.Twin })
        {
            Assert.AreNotEqual(Judgement.Miss, judge.Grade(type, judge.MaxWindow(type)),
                               $"{type} grades as Miss at its own MaxWindow");
        }
    }
}

public class HealthSettingsTests
{
    [Test]
    public void DamageFor_MapsEveryNoteType()
    {
        HealthSettings health = new HealthSettings();

        Assert.AreEqual(health.holdMissDamage, health.DamageFor(NoteType.Hold));
        Assert.AreEqual(health.twinMissDamage, health.DamageFor(NoteType.Twin));

        // Tap reaches the switch's default branch rather than a case of its own.
        Assert.AreEqual(health.tapMissDamage, health.DamageFor(NoteType.Tap));
    }
}

public class FeverSettingsTests
{
    [Test]
    public void GainFor_MapsEveryJudgement()
    {
        FeverSettings fever = new FeverSettings();

        Assert.AreEqual(fever.perfectGain, fever.GainFor(Judgement.Perfect), 1e-6f);
        Assert.AreEqual(fever.greatGain, fever.GainFor(Judgement.Great), 1e-6f);
        Assert.AreEqual(fever.hitGain, fever.GainFor(Judgement.Hit), 1e-6f);
    }

    // A miss must never feed the gauge, however the tiers above are retuned.
    [Test]
    public void GainFor_Miss_IsZero()
    {
        Assert.AreEqual(0f, new FeverSettings().GainFor(Judgement.Miss), 1e-6f);
    }
}

public class SongChartTests
{
    // CreateInstance leaves the object alive until something destroys it, and the
    // Test Runner reports anything still around when the run ends.
    private readonly List<SongChart> created = new List<SongChart>();

    [TearDown]
    public void TearDown()
    {
        foreach (SongChart chart in created)
        {
            if (chart != null) Object.DestroyImmediate(chart);
        }

        created.Clear();
    }

    private SongChart ChartWith(params float[] hitTimes)
    {
        SongChart chart = ScriptableObject.CreateInstance<SongChart>();
        created.Add(chart);
        chart.notes = new List<NoteData>();

        foreach (float time in hitTimes)
        {
            chart.notes.Add(new NoteData { hitTime = time, lane = 0, type = NoteType.Tap });
        }

        return chart;
    }

    private static void AssertAscending(SongChart chart)
    {
        for (int i = 1; i < chart.notes.Count; i++)
        {
            Assert.LessOrEqual(chart.notes[i - 1].hitTime, chart.notes[i].hitTime,
                               $"notes[{i - 1}] comes after notes[{i}]");
        }
    }

    // NoteSpawner walks the chart in order and stops at the first note that is not
    // due yet, so an unsorted chart silently drops every note after the first
    // inversion. Sorting is the precondition the whole spawn loop rests on.
    [Test]
    public void SortNotes_PutsNotesInAscendingHitTime()
    {
        SongChart chart = ChartWith(3f, 1f, 2f);
        chart.SortNotes();

        AssertAscending(chart);
        Assert.AreEqual(1f, chart.notes[0].hitTime, 1e-6f);
        Assert.AreEqual(3f, chart.notes[2].hitTime, 1e-6f);
    }

    [Test]
    public void SortNotes_OnAlreadySortedChart_ChangesNothing()
    {
        SongChart chart = ChartWith(1f, 2f, 3f);
        chart.SortNotes();

        AssertAscending(chart);
        Assert.AreEqual(3, chart.notes.Count);
    }

    [Test]
    public void SortNotes_OnEmptyChart_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => ChartWith().SortNotes());
    }

    [Test]
    public void DifficultyLabel_SwitchesAtTheFiveSixBoundary()
    {
        SongChart chart = ChartWith();

        chart.difficulty = 5;
        Assert.AreEqual("Dễ", chart.DifficultyLabel);

        chart.difficulty = 6;
        Assert.AreEqual("Khó", chart.DifficultyLabel);
    }
}
