using UnityEngine;

/// <summary>
/// 连击高则 Fever 更久 / A high combo lengthens the fever window.
///
/// ECHO's "Resonance". Reads the combo it is handed rather than tracking one, so it is
/// stateless: the bonus is decided when the window opens and does not change mid-fever, which
/// keeps the deadline GameManager computed honest.
/// </summary>
[CreateAssetMenu(menuName = "Rhythm/Passive/Fever Extend", fileName = "PassiveFeverExtend")]
public class FeverExtendPassive : PassiveSO
{
    [Tooltip("Combo needed before the extension applies at all.")]
    public int comboThreshold = 50;

    [Tooltip("Extra seconds of fever once the threshold is met.")]
    public float extraSeconds = 3f;

    public override float ExtraFeverSeconds(RunState run)
    {
        return run.combo >= comboThreshold ? extraSeconds : 0f;
    }
}
