using UnityEngine;

// Fever is not defined in the design doc, so every rule here is a dial rather
// than a hardcoded choice. The defaults are playable, not authoritative.
[System.Serializable]
public class FeverSettings
{
    public float maxFever = 100f;

    [Header("Gain per judgement")]
    public float perfectGain = 3f;
    public float greatGain = 2f;
    public float hitGain = 1f;

    [Tooltip("Fever lost on a miss. Set to 0 to keep the gauge through misses.")]
    public float missLoss = 10f;

    [Tooltip("Start fever the moment the gauge fills. Untick to require a key press instead.")]
    public bool autoActivate = true;

    [Tooltip("Only used when autoActivate is off.")]
    public KeyCode manualActivateKey = KeyCode.Space;

    [Tooltip("Seconds of fever, over which the gauge drains back to empty.")]
    public float feverDuration = 8f;

    [Tooltip("Multiplies score on top of the combo multiplier while fever is active.")]
    public int feverScoreMultiplier = 2;

    public float GainFor(Judgement judgement)
    {
        switch (judgement)
        {
            case Judgement.Perfect: return perfectGain;
            case Judgement.Great: return greatGain;
            case Judgement.Hit: return hitGain;
            default: return 0f;
        }
    }
}
