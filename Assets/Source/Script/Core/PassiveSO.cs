using UnityEngine;

/// <summary>
/// 一个角色的被动 / One operator's passive: what it is called, and what it does to a run.
///
/// 用子类而不是枚举 / Deliberately an abstract asset with a subclass per effect, rather than
/// the enum pair the GDD drafts (triggerType + effectType). The GDD's own stated benefit for
/// the data-driven framework is "no code changes when adding a character", and enums do not
/// deliver that: every new effect means a new enum value AND a new branch in whichever switch
/// reads it. With a subclass, a new passive is a new file and nothing existing is touched.
/// The cost is one asset type per effect, which is the right trade when the roster is meant
/// to grow.
///
/// 每局的状态不要放在这里 / Per-run state must NOT live on these fields. A ScriptableObject is
/// a shared asset: a flag set during a run persists into the next one, and in the Editor it
/// survives leaving Play Mode entirely - so the second run would start with the shield already
/// spent. Anything that resets per run belongs on RunState, which is why the hooks take it.
/// </summary>
public abstract class PassiveSO : ScriptableObject
{
    [Header("展示 / Shown on the character select screen")]
    public string id;
    public string passiveName;

    [TextArea(2, 4)]
    public string description;

    public Sprite icon;

    /// <summary>Called once as a run begins, before the first note.</summary>
    public virtual void BeginRun(RunState run) { }

    /// <summary>
    /// A chance to rewrite a judgement before it is applied. Called only for judgements that
    /// actually landed - a Miss never reaches here, so a passive cannot rescue a press that
    /// was out of range.
    /// </summary>
    public virtual Judgement Regrade(Judgement judged, RunState run)
    {
        return judged;
    }

    /// <summary>
    /// Return true to keep the combo alive through a miss. The miss still counts and still
    /// costs HP; only the combo is spared.
    /// </summary>
    public virtual bool AbsorbComboBreak(RunState run)
    {
        return false;
    }

    /// <summary>Extra seconds added to a fever window as it opens.</summary>
    public virtual float ExtraFeverSeconds(RunState run)
    {
        return 0f;
    }

    /// <summary>
    /// Called every frame while the song is playing, with song-time delta.
    ///
    /// 用歌曲时间 / Song time rather than Time.deltaTime, so a passive on a timer cannot drift
    /// against the chart and keeps counting correctly across a pause.
    /// </summary>
    public virtual void Tick(float deltaSeconds, RunState run) { }
}
