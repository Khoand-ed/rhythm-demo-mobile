using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SongChart", menuName = "Rhythm/Song Chart")]
public class SongChart : ScriptableObject
{
    [Header("Identity")]
    public string stageId;
    public string songId;

    // ScriptableObject already has `name`, so the song's own title needs a
    // different field name.
    public string songName;
    public string author;

    [Header("Presentation")]
    public Sprite cover;

    [Tooltip("Short preview clip for the song select screen.")]
    public AudioClip demoClip;

    [Header("Audio")]
    [Tooltip("The gameplay track. Applied to the Conductor's AudioSource at start.")]
    public AudioClip clip;

    [Tooltip("Per-song sync offset in seconds, added to the player's device latency.")]
    public float offset;

    [Header("Timing")]
    [Range(1, 10)]
    public int difficulty = 1;

    public float bpm = 120f;

    // World units per second every note travels. Derived at import from bpm,
    // then raised if that would let same-lane notes overlap - see BeatmapImporter.
    public float noteSpeed = 2f;

    // Must stay sorted ascending by hitTime - NoteSpawner relies on that to
    // stop scanning as soon as it finds a note that is not due yet.
    public List<NoteData> notes = new List<NoteData>();

    // 1-5 is the easy band, 6-10 the hard band; the select screen shows the
    // number next to this label.
    public string DifficultyLabel
    {
        get { return difficulty <= 5 ? "Dễ" : "Khó"; }
    }

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
