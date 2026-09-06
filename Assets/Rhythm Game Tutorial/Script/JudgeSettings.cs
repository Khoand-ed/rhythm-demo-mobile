using UnityEngine;

// Every timing window in one place. Replaces the old per-lane hitWindow, which
// was derived from collider geometry and so differed between lanes for no
// gameplay reason - timing is a property of the note type, not of the lane.
[System.Serializable]
public class JudgeSettings
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
