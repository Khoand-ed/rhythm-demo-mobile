using UnityEngine;

// Every timing window in one place. Replaces the old per-lane hitWindow, which
// was derived from collider geometry and so differed between lanes for no
// gameplay reason - timing is a property of the note type, not of the lane.
// 做成资产 / An asset. Timing windows are the tuning most worth reviewing as a diff and the
// most likely to want a per-difficulty variant, so they have the least business being buried
// inside a scene file.
[CreateAssetMenu(menuName = "Rhythm/Judge Settings", fileName = "JudgeSettings")]
public class JudgeSettings : ScriptableObject
{
    [Tooltip("Tap and Twin, seconds. Perfect 50ms / Great 100ms / Hit 200ms.")]
    public float tapPerfect = 0.05f;
    public float tapGreat = 0.1f;
    public float tapHit = 0.2f;

    [Tooltip("Hold head, seconds. Perfect 60ms / Great 160ms - no Hit tier, " +
             "anything past Great is an outright miss.")]
    public float holdPerfect = 0.06f;
    public float holdGreat = 0.16f;

    // Widest window for this type: past it the note is missed, and a press can
    // no longer resolve it.
    public float MaxWindow(NoteType type)
    {
        return type == NoteType.Hold ? holdGreat : tapHit;
    }

    public Judgement Grade(NoteType type, float delta)
    {
        if (type == NoteType.Hold)
        {
            if (delta <= holdPerfect) return Judgement.Perfect;
            if (delta <= holdGreat) return Judgement.Great;
            return Judgement.Miss;
        }

        if (delta <= tapPerfect) return Judgement.Perfect;
        if (delta <= tapGreat) return Judgement.Great;
        if (delta <= tapHit) return Judgement.Hit;
        return Judgement.Miss;
    }
}
