using UnityEngine;

/// <summary>
/// Great 有几率算成 Perfect / A Great has a chance to be counted as a Perfect.
///
/// NOVA's "Overdrive". The GDD calls this tier "Good"; the code's Judgement enum has no Good,
/// so it reads Great - the same tier under a different name, not a different rule.
///
/// 无状态 / Stateless by design: it rolls per judgement rather than counting up to a tenth
/// one, so nothing has to be stored or reset between runs.
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

        return Random.value < chance ? Judgement.Perfect : judged;
    }
}
