using NUnit.Framework;
using UnityEngine;

// The four passives, pinned. These are the rules that make one operator play
// differently from another, so they are the last place a silent regression should
// be allowed to hide.
//
// 概率用 0 和 1 / Where a passive rolls a die, the tests set the chance to 0 or 1
// rather than seeding Random: Random.value is [0,1), so 1 always fires and 0 never
// does. That keeps these deterministic without pinning the test to an engine detail.
public class PassiveTests
{
    private static RunState NewRun(PassiveSO passive)
    {
        RunState run = new RunState
        {
            health = ScriptableObject.CreateInstance<HealthSettings>(),
            fever = ScriptableObject.CreateInstance<FeverSettings>(),
            multiplierThresholds = new[] { 4 },
            passive = passive
        };

        run.Reset();
        return run;
    }

    // --- combo shield ---------------------------------------------------------

    [Test]
    public void ComboShield_SparesTheComboOnceThenStopsSparing()
    {
        ComboShieldPassive shield = ScriptableObject.CreateInstance<ComboShieldPassive>();
        shield.charges = 1;

        RunState run = NewRun(shield);
        run.NoteHit(100);
        run.NoteHit(100);

        Assert.AreEqual(2, run.combo, "precondition: a combo to protect");

        run.NoteMissed(NoteType.Tap);
        Assert.AreEqual(2, run.combo, "the first miss must not break the combo");

        run.NoteMissed(NoteType.Tap);
        Assert.AreEqual(0, run.combo, "the second miss has no charge left and must break it");
    }

    [Test]
    public void ComboShield_StillCostsHpAndTheFullCombo()
    {
        // 挡的是连击, 不是那一次失误 / The shield spares the combo and nothing else. Hiding the
        // miss from HP or from the results screen would make both of them lie.
        ComboShieldPassive shield = ScriptableObject.CreateInstance<ComboShieldPassive>();
        shield.charges = 1;

        RunState run = NewRun(shield);
        int hpBefore = run.hp;

        run.NoteMissed(NoteType.Tap);

        Assert.Less(run.hp, hpBefore, "a spared miss must still cost HP");
        Assert.IsFalse(run.IsFullCombo, "a spared miss must still end the Full Combo");
        Assert.AreEqual(1f, run.missedHits, "a spared miss must still be counted");
    }

    [Test]
    public void ComboShield_RechargesOnReset()
    {
        // 资产是共享的 / The charge counter lives on RunState precisely so it cannot survive
        // into the next run. If it ever moves onto the asset, this test is what catches it.
        ComboShieldPassive shield = ScriptableObject.CreateInstance<ComboShieldPassive>();
        shield.charges = 1;

        RunState run = NewRun(shield);
        run.NoteHit(100);
        run.NoteMissed(NoteType.Tap);
        run.NoteMissed(NoteType.Tap);

        Assert.AreEqual(0, run.combo, "precondition: the charge was spent");

        run.Reset();
        run.NoteHit(100);
        run.NoteMissed(NoteType.Tap);

        Assert.AreEqual(1, run.combo, "a new run must start with the shield back");
    }

    // --- judge upgrade --------------------------------------------------------

    [Test]
    public void JudgeUpgrade_PromotesGreatOnly()
    {
        JudgeUpgradePassive up = ScriptableObject.CreateInstance<JudgeUpgradePassive>();
        up.chance = 1f;

        RunState run = NewRun(up);

        Assert.AreEqual(Judgement.Perfect, up.Regrade(Judgement.Great, run),
                        "a Great must be promotable");
        Assert.AreEqual(Judgement.Hit, up.Regrade(Judgement.Hit, run),
                        "a Hit is not a Great and must be left alone");
        Assert.AreEqual(Judgement.Perfect, up.Regrade(Judgement.Perfect, run),
                        "a Perfect must survive unchanged");
        Assert.AreEqual(Judgement.Miss, up.Regrade(Judgement.Miss, run),
                        "a Miss must never be rescued here");
    }

    [Test]
    public void JudgeUpgrade_AtZeroChanceChangesNothing()
    {
        JudgeUpgradePassive up = ScriptableObject.CreateInstance<JudgeUpgradePassive>();
        up.chance = 0f;

        RunState run = NewRun(up);

        Assert.AreEqual(Judgement.Great, up.Regrade(Judgement.Great, run));
    }

    // --- fever extend ---------------------------------------------------------

    [Test]
    public void FeverExtend_OnlyPaysAboveTheComboThreshold()
    {
        FeverExtendPassive extend = ScriptableObject.CreateInstance<FeverExtendPassive>();
        extend.comboThreshold = 10;
        extend.extraSeconds = 3f;

        RunState run = NewRun(extend);

        Assert.AreEqual(0f, extend.ExtraFeverSeconds(run), 1e-6f,
                        "at zero combo there is no bonus");

        for (int i = 0; i < 10; i++) run.NoteHit(10);

        Assert.AreEqual(10, run.combo, "precondition: the threshold is met exactly");
        Assert.AreEqual(3f, extend.ExtraFeverSeconds(run), 1e-6f,
                        "the threshold is inclusive");
    }

    // --- regen ----------------------------------------------------------------

    [Test]
    public void Regen_HealsOncePerInterval()
    {
        RegenPassive regen = ScriptableObject.CreateInstance<RegenPassive>();
        regen.interval = 10f;
        regen.amount = 5;

        RunState run = NewRun(regen);
        run.Damage(50);

        int hurt = run.hp;

        run.TickPassive(9f);
        Assert.AreEqual(hurt, run.hp, "nothing is owed before the interval elapses");

        run.TickPassive(1.5f);
        Assert.AreEqual(hurt + 5, run.hp, "one interval crossed pays exactly once");
    }

    [Test]
    public void Regen_PaysEveryIntervalALongFrameCrossed()
    {
        // 卡一帧不该吞掉回血 / A long frame - or a seek - crosses several intervals at once.
        // Paying only the last one would quietly swallow the rest.
        RegenPassive regen = ScriptableObject.CreateInstance<RegenPassive>();
        regen.interval = 10f;
        regen.amount = 5;

        RunState run = NewRun(regen);
        run.Damage(80);

        int hurt = run.hp;

        run.TickPassive(35f);

        Assert.AreEqual(hurt + 15, run.hp, "three whole intervals must pay three times");
    }

    [Test]
    public void Regen_CannotHealAboveMaxOrAfterFailing()
    {
        RegenPassive regen = ScriptableObject.CreateInstance<RegenPassive>();
        regen.interval = 1f;
        regen.amount = 5;

        RunState full = NewRun(regen);
        full.TickPassive(10f);
        Assert.AreEqual(full.health.maxHp, full.hp, "regen must not push HP past the cap");

        RunState dead = NewRun(regen);
        dead.Damage(dead.health.maxHp);
        Assert.IsTrue(dead.failed, "precondition: the run is over");

        dead.TickPassive(10f);
        Assert.AreEqual(0, dead.hp, "a failed run must not be healed back to life");
    }

    // --- no passive -----------------------------------------------------------

    [Test]
    public void NoPassive_BehavesExactlyAsBefore()
    {
        // 没选角色是合法状态 / A run without an operator is a supported state, not an error:
        // opening the gameplay scene straight from the Editor has none.
        RunState run = NewRun(null);
        run.NoteHit(100);

        run.NoteMissed(NoteType.Tap);
        Assert.AreEqual(0, run.combo, "with no passive a miss breaks the combo");

        Assert.DoesNotThrow(() => run.TickPassive(5f));
    }
}
