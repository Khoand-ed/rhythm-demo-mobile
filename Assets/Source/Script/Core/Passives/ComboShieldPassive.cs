using UnityEngine;

/// <summary>
/// 一局挡一次断连 / Absorbs a limited number of combo breaks per run.
///
/// AMIYA's "Field Medic". The most legible passive of the four: the player sees the combo
/// survive a miss they know they made, so the operator's presence is obvious without reading
/// a number.
///
/// 用掉的次数存在 RunState / The spend counter lives on RunState, not here. This asset is
/// shared and would otherwise carry a spent shield into the next run.
/// </summary>
[CreateAssetMenu(menuName = "Rhythm/Passive/Combo Shield", fileName = "PassiveComboShield")]
public class ComboShieldPassive : PassiveSO
{
    [Tooltip("How many misses a run may absorb before the combo starts breaking again.")]
    public int charges = 1;

    public override void BeginRun(RunState run)
    {
        run.passiveCharges = charges;
    }

    public override bool AbsorbComboBreak(RunState run)
    {
        if (run.passiveCharges <= 0) return false;

        run.passiveCharges--;
        return true;
    }
}
