using NUnit.Framework;

// The scoring rules, pinned. Everything here used to live inside GameManager and
// could only be checked by playing the game; the point of moving it into
// Rhythm.Core was to be able to write these.
//
// Where a rule is a mapping, the assertions compare against the settings fields
// rather than literals, so retuning a value does not turn the file red. Where the
// rule IS the number - the multiplier ladder, a Hold costing two misses - the
// number is written out, because that is the thing under test.
public class RunStateTests
{
    private static RunState NewRun(params int[] thresholds)
    {
        RunState run = new RunState
        {
            health = new HealthSettings(),
            fever = new FeverSettings(),
            multiplierThresholds = thresholds
        };

        run.Reset();
        return run;
    }

    private static void Hit(RunState run, int times, int baseScore = 100)
    {
        for (int i = 0; i < times; i++) run.NoteHit(baseScore);
    }

    // --- the multiplier ladder -------------------------------------------------

    [Test]
    public void NoteHit_ClimbsOneRungPerThresholdCleared()
    {
        RunState run = NewRun(2, 3);

        Assert.AreEqual(1, run.multiplier, "the ladder starts at 1x, not 0x");

        Hit(run, 2);
        Assert.AreEqual(2, run.multiplier);

        Hit(run, 3);
        Assert.AreEqual(3, run.multiplier);
    }

    // multiplier - 1 indexes the array, so a ladder that kept climbing past the
    // last rung would read off the end. This is the test that catches that.
    [Test]
    public void NoteHit_PastTheLastRung_StopsClimbingInsteadOfOverrunning()
    {
        RunState run = NewRun(1);

        Hit(run, 1);
        Assert.AreEqual(2, run.multiplier);

        Assert.DoesNotThrow(() => Hit(run, 50));
        Assert.AreEqual(2, run.multiplier, "there is no rung above the last threshold");
    }

    // The rung is climbed before the score is added, so the hit that clears a
    // threshold already pays at the new rate. Easy to get backwards when reading
    // the method, and worth a full point of score on every ladder step.
    [Test]
    public void NoteHit_ClearingARung_AlreadyScoresAtTheNewRate()
    {
        RunState run = NewRun(2);

        run.NoteHit(100);
        Assert.AreEqual(100, run.score, "still 1x - this hit does not clear the rung");

        run.NoteHit(100);
        Assert.AreEqual(100 + 200, run.score, "this one clears it, and is paid at 2x rather than 1x");

        run.NoteHit(100);
        Assert.AreEqual(100 + 200 + 200, run.score);
    }

    [Test]
    public void MaxCombo_KeepsThePeakAfterTheComboBreaks()
    {
        RunState run = NewRun(1000);

        Hit(run, 5);
        run.NoteMissed(NoteType.Tap);

        Assert.AreEqual(0, run.combo);
        Assert.AreEqual(5, run.maxCombo);
    }

    // --- fever -----------------------------------------------------------------

    [Test]
    public void Fever_MultipliesScoreOnlyWhileItBurns()
    {
        RunState run = NewRun(1000);

        run.NoteHit(100);
        int plain = run.score;

        Assert.IsTrue(run.AddFever(run.fever.maxFever), "a full gauge with autoActivate on should start fever");

        run.NoteHit(100);

        Assert.AreEqual(plain * run.fever.feverScoreMultiplier, run.score - plain);
    }

    [Test]
    public void AddFever_WhileBurning_LeavesTheGaugeToTheDrain()
    {
        RunState run = NewRun(1000);
        run.AddFever(run.fever.maxFever);

        float burning = run.feverGauge;
        run.AddFever(-run.fever.missLoss);

        Assert.AreEqual(burning, run.feverGauge, 1e-4f, "the drain owns the gauge once fever is lit");
    }

    [Test]
    public void AddFever_WithAutoActivateOff_FillsButDoesNotLight()
    {
        RunState run = NewRun(1000);
        run.fever.autoActivate = false;

        Assert.IsFalse(run.AddFever(run.fever.maxFever));
        Assert.IsFalse(run.feverActive);
        Assert.AreEqual(run.fever.maxFever, run.feverGauge, 1e-4f, "the gauge still fills, it just waits for the key");

        Assert.IsTrue(run.ActivateFever(), "and the key press is what lights it");
    }

    [Test]
    public void ActivateFever_OnAPartialGauge_DoesNothing()
    {
        RunState run = NewRun(1000);
        run.AddFever(run.fever.maxFever - 1f);

        Assert.IsFalse(run.ActivateFever());
        Assert.IsFalse(run.feverActive);
    }

    [Test]
    public void TickFever_DrainsTowardsEmptyAndReportsTheEndOnce()
    {
        RunState run = NewRun(1000);
        run.AddFever(run.fever.maxFever);

        Assert.IsFalse(run.TickFever(run.fever.feverDuration * 0.5f));
        Assert.AreEqual(run.fever.maxFever * 0.5f, run.feverGauge, 1e-3f);

        Assert.IsTrue(run.TickFever(0f), "the tick that runs out of time is the one that ends fever");
        Assert.IsFalse(run.feverActive);
        Assert.AreEqual(0f, run.feverGauge, 1e-4f);

        Assert.IsFalse(run.TickFever(0f), "and it only says so once");
    }

    // --- holds -----------------------------------------------------------------

    // The spec says only the head and tail of a hold move the combo, so the body
    // pays score and nothing else.
    [Test]
    public void HoldTick_PaysScoreWithoutTouchingCombo()
    {
        RunState run = NewRun(1000);
        Hit(run, 3);

        int combo = run.combo;
        int before = run.score;

        run.HoldTick(10);

        Assert.AreEqual(combo, run.combo);
        Assert.AreEqual(before + 10, run.score);
    }

    // The body pays at the ladder's current rate, the same as a head or tail does.
    [Test]
    public void HoldTick_PaysAtTheCurrentMultiplier()
    {
        RunState run = NewRun(1);

        Hit(run, 1);
        Assert.AreEqual(2, run.multiplier);

        int before = run.score;
        run.HoldTick(10);

        Assert.AreEqual(before + 20, run.score);
    }

    // Letting go early drops the combo and the Full Combo, but charges no HP. The
    // unplayed tail still counts as a miss, or accuracy would quietly ignore it.
    [Test]
    public void HoldDropped_BreaksTheComboAndCountsAMiss_ButCostsNoHp()
    {
        RunState run = NewRun(2);
        Hit(run, 4);

        int hp = run.hp;
        run.HoldDropped();

        Assert.AreEqual(0, run.combo);
        Assert.AreEqual(1, run.multiplier, "the ladder goes back to the bottom too");
        Assert.IsFalse(run.fullCombo);
        Assert.AreEqual(1f, run.missedHits);
        Assert.AreEqual(hp, run.hp, "a dropped hold is not a miss for HP purposes");
    }

    // --- misses, HP and failure ------------------------------------------------

    // A hold missed at its head never reaches its tail either, and both halves
    // count towards totalNotes.
    [Test]
    public void NoteMissed_Hold_CountsTwo_WhileTapAndTwinCountOne()
    {
        Assert.AreEqual(2f, MissesFor(NoteType.Hold));
        Assert.AreEqual(1f, MissesFor(NoteType.Tap));
        Assert.AreEqual(1f, MissesFor(NoteType.Twin));
    }

    private static float MissesFor(NoteType type)
    {
        RunState run = NewRun(1000);
        run.NoteMissed(type);
        return run.missedHits;
    }

    [Test]
    public void NoteMissed_ChargesTheDamageForThatNoteType()
    {
        RunState run = NewRun(1000);
        int maxHp = run.health.maxHp;

        run.NoteMissed(NoteType.Hold);

        Assert.AreEqual(maxHp - run.health.holdMissDamage, run.hp);
    }

    [Test]
    public void Damage_ClampsAtZeroAndAnnouncesTheFailureExactlyOnce()
    {
        RunState run = NewRun(1000);

        Assert.IsFalse(run.Damage(run.health.maxHp - 1), "still standing");
        Assert.IsTrue(run.Damage(1), "this is the hit that empties the bar");
        Assert.AreEqual(0, run.hp);
        Assert.IsTrue(run.failed);

        Assert.IsFalse(run.Damage(10), "a failed run cannot fail again");
        Assert.AreEqual(0, run.hp, "and cannot go below zero");
    }

    [Test]
    public void Heal_ClampsAtMaxHp_AndIsIgnoredAfterAFailure()
    {
        RunState run = NewRun(1000);

        run.Damage(20);
        run.Heal(1000);
        Assert.AreEqual(run.health.maxHp, run.hp);

        run.Damage(run.health.maxHp);
        run.Heal(50);
        Assert.AreEqual(0, run.hp, "healing out of a failed run would undo the failure");
    }

    [Test]
    public void IsFullCombo_IsFalseAfterAFailedRun_EvenWithEveryNoteHit()
    {
        RunState run = NewRun(1000);
        Hit(run, 10);

        Assert.IsTrue(run.IsFullCombo);

        run.Damage(run.health.maxHp);

        Assert.IsTrue(run.fullCombo, "no note was missed...");
        Assert.IsFalse(run.IsFullCombo, "...but a run that ran out of HP is never a full combo");
    }

    // --- accuracy --------------------------------------------------------------

    [Test]
    public void Accuracy_IsHitsOverTotalJudgements()
    {
        RunState run = NewRun(1000);
        run.totalNotes = 10f;
        run.normalHits = 2f;
        run.goodHits = 3f;
        run.perfectHits = 1f;

        Assert.AreEqual(60f, run.Accuracy, 1e-3f);
    }

    // Opening the scene with no chart leaves totalNotes at zero, and the results
    // screen would otherwise print NaN%.
    [Test]
    public void Accuracy_WithNothingToHit_IsZeroRatherThanNaN()
    {
        RunState run = NewRun(1000);

        Assert.AreEqual(0f, run.Accuracy, 1e-6f);
    }

    // --- reset -----------------------------------------------------------------

    [Test]
    public void Reset_PutsEveryCounterBackToTheTopOfARun()
    {
        RunState run = NewRun(2);

        Hit(run, 6);
        run.HoldDropped();
        run.Damage(30);
        run.AddFever(run.fever.maxFever);

        run.Reset();

        Assert.AreEqual(0, run.score);
        Assert.AreEqual(0, run.combo);
        Assert.AreEqual(0, run.maxCombo);
        Assert.AreEqual(1, run.multiplier);
        Assert.AreEqual(0, run.multiplierTracker);
        Assert.AreEqual(run.health.maxHp, run.hp);
        Assert.AreEqual(0f, run.feverGauge, 1e-6f);
        Assert.IsFalse(run.feverActive);
        Assert.IsTrue(run.fullCombo);
        Assert.IsFalse(run.failed);
        Assert.AreEqual(0f, run.missedHits);
    }

    // --- the operator's modifiers ---------------------------------------------

    [Test]
    public void ScoreModifier_DefaultsToNeutral()
    {
        // 没选角色也要能跑 / A run with no operator must score exactly as it did before
        // operators existed, or opening the gameplay scene from the Editor changes meaning.
        RunState run = NewRun(4);

        Assert.AreEqual(1f, run.scoreModifier, "a fresh run must not scale score");
        Assert.AreEqual(1f, run.feverModifier, "a fresh run must not scale fever");
        Assert.AreEqual(100, run.ScoreFor(100));
    }

    [Test]
    public void ScoreModifier_AppliesOverTheComboLadder()
    {
        // 倍率叠在阶梯之上, 不是取代它 / The operator widens what the chart already earns.
        RunState plain = NewRun(2);
        RunState boosted = NewRun(2);
        boosted.scoreModifier = 1.2f;

        Hit(plain, 2);
        Hit(boosted, 2);

        Assert.AreEqual(2, plain.multiplier, "precondition: the ladder climbed");
        Assert.AreEqual(2, boosted.multiplier, "the modifier must not disturb the ladder");
        Assert.AreEqual(plain.ScoreFor(100) * 1.2f, boosted.ScoreFor(100), 0.5f);
    }

    [Test]
    public void ScoreModifier_RoundsRatherThanTruncates()
    {
        // 截断会悄悄吃掉一分 / 10 * 1.2 is 12, and integer truncation would make it 11 for
        // most awards - a bias that is invisible per note and large over a chart.
        RunState run = NewRun(4);
        run.scoreModifier = 1.2f;

        Assert.AreEqual(12, run.ScoreFor(10));
    }

    [Test]
    public void FeverModifier_ScalesGainsOnly()
    {
        // 加成只作用于涨 / The GDD defines this as how fast the bar builds. Scaling the miss
        // penalty too would make the fever specialist punished hardest for a miss.
        RunState plain = NewRun(4);
        RunState boosted = NewRun(4);
        boosted.feverModifier = 1.5f;

        plain.AddFever(10f);
        boosted.AddFever(10f);

        Assert.AreEqual(10f, plain.feverGauge, 0.001f);
        Assert.AreEqual(15f, boosted.feverGauge, 0.001f, "a gain must scale with the modifier");

        float plainBefore = plain.feverGauge;
        float boostedBefore = boosted.feverGauge;

        plain.AddFever(-4f);
        boosted.AddFever(-4f);

        Assert.AreEqual(4f, plainBefore - plain.feverGauge, 0.001f);
        Assert.AreEqual(4f, boostedBefore - boosted.feverGauge, 0.001f,
                        "a loss must cost the same whatever the modifier");
    }
}
