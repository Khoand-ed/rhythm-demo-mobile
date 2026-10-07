using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Great 有几率算成 Perfect / A Great has a chance to be counted as a Perfect.
///
/// NOVA's "Overdrive". The GDD calls this tier "Good"; the code's Judgement enum has no Good,
/// so it reads Great - the same tier under a different name, not a different rule.
///
/// 无状态 / Stateless by design: it rolls per judgement rather than counting up to a tenth
/// one, so nothing has to be stored or reset between runs.
///
/// 用这一局的骰子 / It rolls on the run's own sequence, not UnityEngine.Random. That one is
/// shared with everything else in the scene, so the server could never draw the same numbers;
/// this one starts from the seed the server issued and is drawn only here.
/// </summary>
[CreateAssetMenu(menuName = "Rhythm/Passive/Judge Upgrade", fileName = "PassiveJudgeUpgrade")]
public class JudgeUpgradePassive : PassiveSO
{
    [Range(0f, 1f)]
    [Tooltip("Probability that a Great is promoted to a Perfect.")]
    public float chance = 0.25f;

    public override Judgement Regrade(Judgement judged, RunState run)
    {
        if (judged != Judgement.Great) return judged;

        return run.NextRoll() < chance ? Judgement.Perfect : judged;
    }

    public override void WriteRules(List<RuleParam> into)
    {
        into.Add(new RuleParam("chance", chance));
    }

    public override void ReadRules(IReadOnlyList<RuleParam> from)
    {
        chance = RuleParam.Find(from, "chance", chance);
    }
}
