using UnityEngine;

[System.Serializable]
public class HealthSettings
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
