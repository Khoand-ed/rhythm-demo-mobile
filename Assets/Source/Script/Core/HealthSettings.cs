using UnityEngine;

// 做成资产而不是内嵌字段 / An asset rather than an inline field on GameManager. Serialized
// inline, these numbers lived inside Main.unity: a reviewer could not see a tuning change in
// a diff, two scenes could not share one balance pass, and a per-difficulty variant was
// impossible without duplicating the component.
[CreateAssetMenu(menuName = "Rhythm/Health Settings", fileName = "HealthSettings")]
public class HealthSettings : ScriptableObject
{
    public int maxHp = 100;

    public int tapMissDamage = 10;

    public int holdMissDamage = 15;

    [Tooltip("Per missed half of a Twin, so a fully missed pair costs twice this.")]
    public int twinMissDamage = 10;

    public int DamageFor(NoteType type)
    {
        switch (type)
        {
            case NoteType.Hold: return holdMissDamage;
            case NoteType.Twin: return twinMissDamage;
            default: return tapMissDamage;
        }
    }
}
