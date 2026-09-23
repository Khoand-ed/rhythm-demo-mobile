using UnityEngine;

// Everything a run accumulates: score, combo, the multiplier ladder, HP and the
// fever gauge. Split out of GameManager so the rules can be tested without a
// scene - GameManager kept the tuning fields, the TMP labels and the gauges, and
// now only pushes what changed here out to the screen.
//
// Nothing in this class knows about song time, input or UI. Where a rule needs
// one of those the method takes it as an argument or reports back what happened,
// and the caller does the scene-facing half. ActivateFever is the clearest case:
// it flips the flag and says so, while the deadline it implies stays with the
// Conductor.
[System.Serializable]
public class RunState
{
    // Handed over by the owner at startup rather than owned here, because these
    // are tuning the designer edits in the Inspector and their serialized values
    // live on GameManager. Moving them would orphan what Main.unity already holds.
    [System.NonSerialized] public HealthSettings health;
    [System.NonSerialized] public FeverSettings fever;
    [System.NonSerialized] public int[] multiplierThresholds;

    // 角色带来的两个倍率 / The operator's two multipliers, handed over the same way and for
    // the same reason: CharMeta lives in the Arknights assembly, which Rhythm.Core cannot see
    // and must not start seeing. The caller reads them off the operator and passes plain
    // floats, so the rules stay testable without any of the front-end in scope.
    //
    // 默认 1 表示没选角色 / Both default to 1, which is exactly "no operator": opening the
    // gameplay scene straight from the Editor scores the way it always did.
    [System.NonSerialized] public float scoreModifier = 1f;
    [System.NonSerialized] public float feverModifier = 1f;

    // 角色的被动, 可以为空 / The operator's passive, or null for a run without one. Null is a
    // supported state, not an error: opening the gameplay scene from the Editor has no
    // operator, and every call site here checks.
    [System.NonSerialized] public PassiveSO passive;

    // 被动的每局状态 / Scratch space the passive needs but must not keep on itself. A
    // PassiveSO is a shared asset, so a counter stored there would carry into the next run -
    // and in the Editor, across leaving Play Mode entirely. What each field means is the
    // passive's business; RunState only guarantees they are cleared for every run.
    [System.NonSerialized] public int passiveCharges;
    [System.NonSerialized] public float passiveTimer;

    public int score;
    public int combo;
    public int maxCombo;

    // Starts at 1: the ladder is 1x until the first threshold is cleared, and
    // Multiplier - 1 indexes multiplierThresholds.
    public int multiplier = 1;
    public int multiplierTracker;

    public int hp;
    public float feverGauge;
    public bool feverActive;

    public bool fullCombo = true;
    public bool failed;

    public float totalNotes;
    public float normalHits;
    public float goodHits;
    public float perfectHits;
    public float missedHits;

    /// <summary>
    /// Percentage of judgements landed. Hits over judgements rather than over
    /// chart entries, which is why totalNotes is counted by the caller.
    /// </summary>
    public float Accuracy
    {
        get
        {
            if (totalNotes <= 0f) return 0f;

            return ((normalHits + goodHits + perfectHits) / totalNotes) * 100f;
        }
    }

    /// <summary>
    /// A run that ran out of HP is never a full combo, whatever the counters say
    /// about the notes that did get played.
    /// </summary>
    public bool IsFullCombo
    {
        get { return fullCombo && !failed; }
    }

    /// <summary>
    /// Back to the top of a run. Called from Start as well as from a retry, so a
    /// value left serialized in the scene cannot leak into the first run.
    /// </summary>
    public void Reset()
    {
        score = 0;
        combo = 0;
        maxCombo = 0;
        multiplier = 1;
        multiplierTracker = 0;

        hp = health != null ? health.maxHp : 0;
        feverGauge = 0f;
        feverActive = false;

        fullCombo = true;
        failed = false;

        normalHits = 0f;
        goodHits = 0f;
        perfectHits = 0f;
        missedHits = 0f;

        passiveCharges = 0;
        passiveTimer = 0f;

        // 让被动自己填初值 / After the scratch is cleared, so a passive that seeds a charge
        // count writes into a clean slate rather than onto the last run's leftovers.
        if (passive != null) passive.BeginRun(this);
    }

    /// <summary>
    /// Score for one judgement at the current multiplier and fever state, scaled by the
    /// operator's score modifier.
    ///
    /// 角色倍率放在最后 / The operator's multiplier is applied last, over the combo ladder and
    /// the fever bonus rather than instead of them, so picking a stronger operator widens the
    /// gap the chart already earns instead of flattening it. Rounded rather than truncated:
    /// at 1.2x a 10 point hit should read as 12, and integer truncation would quietly shave
    /// a point off most awards.
    /// </summary>
    public int ScoreFor(int baseScore)
    {
        int earned = baseScore * multiplier * (feverActive && fever != null ? fever.feverScoreMultiplier : 1);

        return Mathf.RoundToInt(earned * scoreModifier);
    }

    public void NoteHit(int baseScore)
    {
        // Past the last threshold the ladder stops climbing, and the tracker stops
        // counting with it - otherwise the index below would run off the array.
        if (multiplierThresholds != null && multiplier - 1 < multiplierThresholds.Length)
        {
            multiplierTracker++;

            if (multiplierThresholds[multiplier - 1] <= multiplierTracker)
            {
                multiplierTracker = 0;
                multiplier++;
            }
        }

        score += ScoreFor(baseScore);
        combo++;

        if (combo > maxCombo) maxCombo = combo;
    }

    /// <summary>
    /// Awarded per tick of a held note's body. Deliberately does not touch combo
    /// or the multiplier - the spec says only the head and tail do.
    ///
    /// Takes the rate rather than holding it, so retuning it in the Inspector
    /// mid-run still lands the way it did before this state moved out here.
    /// </summary>
    public void HoldTick(int scorePerTick)
    {
        score += ScoreFor(scorePerTick);
    }

    /// <summary>
    /// Releasing a hold early kills the rest of the note: the spec drops the combo
    /// and the Full Combo, but charges no HP for it. The unplayed tail still counts
    /// as a miss on the results screen, or accuracy would ignore it.
    /// </summary>
    public void HoldDropped()
    {
        missedHits++;
        BreakCombo();
    }

    /// <summary>
    /// Returns true if this miss is what emptied the HP bar, so the caller can run
    /// the one-time failure sequence.
    /// </summary>
    public bool NoteMissed(NoteType type)
    {
        // 被动只挡断连 / A passive can spare the combo, nothing else. The miss is still
        // counted, still costs HP and still ends the Full Combo - the player did miss, and
        // hiding that would make the results screen lie.
        if (passive != null && passive.AbsorbComboBreak(this)) fullCombo = false;
        else BreakCombo();

        // A hold missed at its head never gets to its tail either, and both count
        // towards totalNotes.
        missedHits += type == NoteType.Hold ? 2 : 1;

        if (fever != null) AddFever(-fever.missLoss);

        return Damage(health != null ? health.DamageFor(type) : 0);
    }

    private void BreakCombo()
    {
        multiplier = 1;
        multiplierTracker = 0;
        combo = 0;
        fullCombo = false;
    }

    /// <summary>Returns true if this call is what took HP to zero.</summary>
    public bool Damage(int amount)
    {
        if (failed) return false;

        hp = Mathf.Max(0, hp - amount);

        if (hp != 0) return false;

        failed = true;
        return true;
    }

    public void Heal(int amount)
    {
        if (failed) return;

        hp = Mathf.Min(health != null ? health.maxHp : hp, hp + amount);
    }

    /// <summary>
    /// Returns true if this call filled the gauge and auto-activation turned fever
    /// on, so the caller can set the deadline.
    /// </summary>
    public bool AddFever(float amount)
    {
        // While fever is burning, its own drain owns the gauge.
        if (feverActive || fever == null || Mathf.Approximately(amount, 0f)) return false;

        // 只加成涨的那一边 / Gains only. The GDD defines this modifier as how fast the bar
        // builds, so scaling the miss penalty by it too would punish the operator who is
        // meant to be better at fever - the higher the modifier, the more a miss would cost.
        if (amount > 0f) amount *= feverModifier;

        feverGauge = Mathf.Clamp(feverGauge + amount, 0f, fever.maxFever);

        if (feverGauge >= fever.maxFever && fever.autoActivate) return ActivateFever();

        return false;
    }

    /// <summary>
    /// Returns true if fever just turned on. The deadline it implies runs on song
    /// time, so the caller owns it.
    /// </summary>
    public bool ActivateFever()
    {
        if (feverActive || fever == null || feverGauge < fever.maxFever) return false;

        feverActive = true;
        return true;
    }

    /// <summary>
    /// Advances anything the passive runs on a timer.
    ///
    /// 只在歌曲进行时调用 / Called only while the song is actually playing, with song-time
    /// delta, so a regen passive cannot tick through the pause menu or the READY/GO run-in.
    /// </summary>
    public void TickPassive(float deltaSeconds)
    {
        if (passive == null || deltaSeconds <= 0f) return;

        passive.Tick(deltaSeconds, this);
    }

    /// <summary>
    /// Drains the gauge over the remaining fever time, and returns true on the tick
    /// that ends it. Takes the remaining seconds rather than reading a clock, so the
    /// caller can keep fever on song time.
    /// </summary>
    public bool TickFever(float remaining)
    {
        if (!feverActive || fever == null) return false;

        if (remaining <= 0f)
        {
            feverActive = false;
            feverGauge = 0f;
            return true;
        }

        feverGauge = fever.maxFever * Mathf.Clamp01(remaining / Mathf.Max(0.0001f, fever.feverDuration));
        return false;
    }
}
