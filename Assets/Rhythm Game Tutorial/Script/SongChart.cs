using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SongChart", menuName = "Rhythm/Song Chart")]
public class SongChart : ScriptableObject
{
    public AudioClip clip;

    public float bpm = 120f;

    // World units per second every note travels. The legacy NoteHolders used
    // beatTempo / 60, so 120 BPM meant 2 units per second.
    public float noteSpeed = 2f;

    // Must stay sorted ascending by hitTime - NoteSpawner relies on that to
    // stop scanning as soon as it finds a note that is not due yet.
    public List<NoteData> notes = new List<NoteData>();

    public void SortNotes()
    {
        notes.Sort((a, b) => a.hitTime.CompareTo(b.hitTime));
    }

    void OnValidate()
    {
        for (int i = 1; i < notes.Count; i++)
        {
            if (notes[i].hitTime < notes[i - 1].hitTime)
            {
                Debug.LogWarning($"{name}: notes are not sorted by hitTime; call SortNotes().", this);
                return;
            }
        }
    }
}

// Wrapper so JsonUtility can round-trip a chart: it cannot serialise a
// top-level array, so the note list has to hang off an object.
[System.Serializable]
public class SongChartJson
{
    public float bpm;
    public float noteSpeed;
    public List<NoteData> notes = new List<NoteData>();
}
