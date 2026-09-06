using UnityEngine;

public enum NoteType
{
    Tap = 0,
    Hold = 1,
    Flick = 2,
}

// One entry on the song timeline. A plain serialisable struct so it works in
// the Inspector, in JsonUtility, and in a List without per-note allocation.
[System.Serializable]
public struct NoteData
{
    [Tooltip("Song time in seconds at which this note should be hit.")]
    public float hitTime;

    [Tooltip("Index into NoteSpawner.lanes.")]
    public int lane;

    [Tooltip("Sustain length in seconds. 0 for a tap note.")]
    public float duration;

    public NoteType type;
}
