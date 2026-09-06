using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Turns the hand-placed NoteHolder notes into a SongChart asset, using the
// same formula NoteObject.Start() used at runtime so the generated chart
// reproduces the old timing exactly.
public class ChartExtractor
{
    private const string ChartPath = "Assets/Rhythm Game Tutorial/Charts/RhythmTutorial.asset";

    [MenuItem("Tools/Rhythm/Extract Chart From Open Scene")]
    static void Extract()
    {
        // Must run in edit mode: BeatScroller.Awake() divides beatTempo by 60
        // at runtime, so in play mode the speed below would be 60x too small.
        if (Application.isPlaying)
        {
            Debug.LogError("Exit Play Mode before extracting - beatTempo is mutated at runtime.");
            return;
        }

        NoteSpawner spawner = Object.FindAnyObjectByType<NoteSpawner>(FindObjectsInactive.Include);
        if (spawner == null || spawner.lanes == null || spawner.lanes.Length == 0)
        {
            Debug.LogError("Run Tools/Rhythm/Set Up Note System first - the extractor needs the lane list to map keys to lane indices.");
            return;
        }

        Dictionary<KeyCode, float> hitZoneX = new Dictionary<KeyCode, float>();
        foreach (ButtonController button in Object.FindObjectsByType<ButtonController>(FindObjectsInactive.Include))
        {
            hitZoneX[button.keyToPress] = button.transform.position.x;
        }

        List<NoteData> notes = new List<NoteData>();
        float speed = 0f;

        foreach (BeatScroller holder in Object.FindObjectsByType<BeatScroller>(FindObjectsInactive.Include))
        {
            speed = holder.beatTempo / 60f;

            foreach (NoteObject note in holder.GetComponentsInChildren<NoteObject>(true))
            {
                int laneIndex = System.Array.FindIndex(spawner.lanes, lane => lane.key == note.keyToPress);

                if (laneIndex < 0 || !hitZoneX.ContainsKey(note.keyToPress))
                {
                    Debug.LogWarning($"Skipping {note.name}: no lane or button for {note.keyToPress}.", note);
                    continue;
                }

                notes.Add(new NoteData
                {
                    hitTime = Mathf.Abs(note.transform.position.x - hitZoneX[note.keyToPress]) / speed,
                    lane = laneIndex,
                    duration = 0f,
                    type = NoteType.Tap,
                });
            }
        }

        SongChart chart = AssetDatabase.LoadAssetAtPath<SongChart>(ChartPath);
        bool isNew = chart == null;
        if (isNew) chart = ScriptableObject.CreateInstance<SongChart>();

        chart.notes = notes;
        chart.noteSpeed = speed;
        chart.SortNotes();

        GameManager gameManager = Object.FindAnyObjectByType<GameManager>();
        if (gameManager != null && gameManager.theMusic != null)
        {
            chart.clip = gameManager.theMusic.clip;
        }

        if (isNew)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ChartPath));
            AssetDatabase.CreateAsset(chart, ChartPath);
        }

        EditorUtility.SetDirty(chart);
        AssetDatabase.SaveAssets();

        Debug.Log($"Extracted {notes.Count} notes at {speed} units/s into {ChartPath}.");
        foreach (NoteData note in chart.notes)
        {
            Debug.Log($"  lane {note.lane} ({spawner.lanes[note.lane].name})  hitTime {note.hitTime:F4}");
        }
    }

}
