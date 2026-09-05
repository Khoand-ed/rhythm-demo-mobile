using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// Turns Assets/Beatmaps/<song>/<difficulty>.json into SongChart assets.
//
// A song folder holds its media once - cover.png, demo.mp3, main.mp3 - plus one
// .json per difficulty, so several difficulties share the same audio and art.
public class BeatmapImporter
{
    private const string BeatmapsRoot = "Assets/Beatmaps";
    private const string LibraryPath = "Assets/Beatmaps/BeatmapLibrary.asset";
    private const string NotePrefabPath = "Assets/Prefabs/Note.prefab";

    // Fraction of clear space to leave between two notes in the same lane.
    private const float SpacingMargin = 1.05f;

    // Used only when the note prefab cannot be measured.
    private const float FallbackNoteWidth = 1.25f;

    [MenuItem("Tools/Rhythm/Import Beatmaps")]
    static void ImportAll()
    {
        if (!AssetDatabase.IsValidFolder(BeatmapsRoot))
        {
            Debug.LogError($"No {BeatmapsRoot} folder. Create it with one folder per song, " +
                           "each holding cover.png, demo.mp3, main.mp3 and one .json per difficulty.");
            return;
        }

        float noteWidth = MeasureNoteWidth();
        Dictionary<string, int> laneIndexByName = BuildLaneMap();

        List<SongChart> imported = new List<SongChart>();
        int failed = 0;

        foreach (string songFolder in AssetDatabase.GetSubFolders(BeatmapsRoot))
        {
            AudioClip main = LoadFirst<AudioClip>(songFolder, "main");
            AudioClip demo = LoadFirst<AudioClip>(songFolder, "demo");
            Sprite cover = LoadFirst<Sprite>(songFolder, "cover");

            if (main == null) Debug.LogWarning($"{songFolder}: no main audio found (expected main.mp3).");
            if (demo == null) Debug.LogWarning($"{songFolder}: no demo audio found (expected demo.mp3).");
            if (cover == null) Debug.LogWarning($"{songFolder}: no cover found (expected cover.png).");

            ApplyAudioSettings(main, gameplay: true);
            ApplyAudioSettings(demo, gameplay: false);

            foreach (string jsonPath in JsonFilesIn(songFolder))
            {
                SongChart chart = ImportOne(jsonPath, main, demo, cover, noteWidth, laneIndexByName);

                if (chart != null) imported.Add(chart);
                else failed++;
            }
        }

        if (imported.Count > 0) UpdateLibrary(imported);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Beatmap import finished: {imported.Count} chart(s) imported, {failed} failed.");
    }

    private static SongChart ImportOne(string jsonPath, AudioClip main, AudioClip demo, Sprite cover,
                                       float noteWidth, Dictionary<string, int> laneIndexByName)
    {
        TextAsset text = AssetDatabase.LoadAssetAtPath<TextAsset>(jsonPath);
        if (text == null)
        {
            Debug.LogError($"{jsonPath}: could not be read as text.");
            return null;
        }

        BeatmapJson json;
        try
        {
            json = JsonUtility.FromJson<BeatmapJson>(text.text);
        }
        catch (System.Exception exception)
        {
            Debug.LogError($"{jsonPath}: not valid JSON - {exception.Message}");
            return null;
        }

        if (json == null || json.notes == null)
        {
            Debug.LogError($"{jsonPath}: parsed to nothing. Is it a beatmap file?");
            return null;
        }

        if (json.difficulty < 1 || json.difficulty > 10)
        {
            Debug.LogError($"{jsonPath}: difficulty {json.difficulty} is outside the 1-10 range.");
            return null;
        }

        if (json.bpm <= 0f)
        {
            Debug.LogError($"{jsonPath}: bpm must be greater than 0 (got {json.bpm}).");
            return null;
        }

        List<NoteData> notes = ConvertNotes(json, jsonPath, laneIndexByName);
        if (notes == null) return null;

        // NoteSpawner walks the list with a cursor and stops at the first note
        // that is not due, so the ordering is load-bearing rather than cosmetic.
        int outOfOrder = CountOutOfOrder(notes);
        notes.Sort((a, b) => a.hitTime.CompareTo(b.hitTime));
        if (outOfOrder > 0)
        {
            Debug.LogWarning($"{jsonPath}: {outOfOrder} note(s) were out of ascending time order and have been sorted.");
        }

        float baseSpeed = json.bpm / 60f;
        float minGap;
        float speed = ResolveSpeed(notes, baseSpeed, noteWidth, out minGap);

        string assetPath = System.IO.Path.ChangeExtension(jsonPath, ".asset");
        SongChart chart = AssetDatabase.LoadAssetAtPath<SongChart>(assetPath);
        bool isNew = chart == null;
        if (isNew) chart = ScriptableObject.CreateInstance<SongChart>();

        chart.stageId = json.stageId;
        chart.songId = json.songId;
        chart.songName = json.name;
        chart.author = json.author;
        chart.bpm = json.bpm;
        chart.difficulty = json.difficulty;
        chart.offset = json.offset;
        chart.noteSpeed = speed;
        chart.notes = notes;
        chart.clip = main;
        chart.demoClip = demo;
        chart.cover = cover;

        if (isNew) AssetDatabase.CreateAsset(chart, assetPath);
        EditorUtility.SetDirty(chart);

        Debug.Log(Summarise(assetPath, json, notes, baseSpeed, speed, minGap, noteWidth), chart);
        return chart;
    }

    // Converts the on-disk note shape into the runtime one. Tap and Twin carry
    // `time`; Hold carries `startTime`/`endTime`, which become hitTime+duration.
    private static List<NoteData> ConvertNotes(BeatmapJson json, string jsonPath,
                                               Dictionary<string, int> laneIndexByName)
    {
        List<NoteData> notes = new List<NoteData>(json.notes.Count);
        HashSet<int> seenIds = new HashSet<int>();
        bool failed = false;

        for (int i = 0; i < json.notes.Count; i++)
        {
            BeatmapNoteJson source = json.notes[i];
            string where = $"{jsonPath}: note id {source.id} (index {i})";

            if (!seenIds.Add(source.id))
            {
                Debug.LogWarning($"{where} reuses an id already seen in this beatmap.");
            }

            NoteType type;
            if (!TryParseType(source.type, out type))
            {
                Debug.LogError($"{where} has unknown type \"{source.type}\". Supported types are Tap, Hold and Twin.");
                failed = true;
                continue;
            }

            int lane = 0;
            if (type != NoteType.Twin && !TryParseLane(source.lane, laneIndexByName, out lane))
            {
                Debug.LogError($"{where} has unknown lane \"{source.lane}\". Supported lanes are Left and Right.");
                failed = true;
                continue;
            }

            float hitTime;
            float duration = 0f;

            if (type == NoteType.Hold)
            {
                if (source.endTime <= source.startTime)
                {
                    Debug.LogError($"{where} is a Hold whose endTime ({source.endTime}) is not after its " +
                                   $"startTime ({source.startTime}).");
                    failed = true;
                    continue;
                }

                hitTime = source.startTime;
                duration = source.endTime - source.startTime;
            }
            else
            {
                hitTime = source.time;

                // Almost certainly an authoring slip rather than a note at t=0.
                if (source.time == 0f && source.startTime != 0f)
                {
                    Debug.LogWarning($"{where} is a {type} with no `time` but a `startTime` of " +
                                     $"{source.startTime}; using that.");
                    hitTime = source.startTime;
                }
            }

            if (hitTime < 0f)
            {
                Debug.LogError($"{where} has a negative time ({hitTime}).");
                failed = true;
                continue;
            }

            notes.Add(new NoteData
            {
                hitTime = hitTime,
                lane = lane,
                duration = duration,
                type = type,
            });
        }

        return failed ? null : notes;
    }

    // bpm sets the baseline scroll speed. If that would let two notes in the
    // same lane touch, the speed is raised just enough to keep them apart -
    // which never changes when a note lands, only how early it appears.
    private static float ResolveSpeed(List<NoteData> notes, float baseSpeed, float noteWidth, out float minGap)
    {
        minGap = float.MaxValue;

        // Last time each lane is still occupied: a Hold blocks its lane until
        // its tail, so the gap is measured from the end of the hold.
        Dictionary<int, float> laneFreeAt = new Dictionary<int, float>();

        for (int i = 0; i < notes.Count; i++)
        {
            NoteData note = notes[i];

            foreach (int lane in LanesOccupiedBy(note))
            {
                float previousEnd;
                if (laneFreeAt.TryGetValue(lane, out previousEnd))
                {
                    minGap = Mathf.Min(minGap, note.hitTime - previousEnd);
                }

                laneFreeAt[lane] = note.hitTime + note.duration;
            }
        }

        if (minGap == float.MaxValue || minGap <= 0f) return baseSpeed;

        float required = noteWidth * SpacingMargin / minGap;
        return Mathf.Max(baseSpeed, required);
    }

    // A Twin occupies both lanes at once, so it counts against each of them.
    private static IEnumerable<int> LanesOccupiedBy(NoteData note)
    {
        if (note.type == NoteType.Twin)
        {
            yield return 0;
            yield return 1;
        }
        else
        {
            yield return note.lane;
        }
    }

    private static int CountOutOfOrder(List<NoteData> notes)
    {
        int count = 0;

        for (int i = 1; i < notes.Count; i++)
        {
            if (notes[i].hitTime < notes[i - 1].hitTime) count++;
        }

        return count;
    }

    private static bool TryParseType(string value, out NoteType type)
    {
        type = NoteType.Tap;
        if (string.IsNullOrEmpty(value)) return false;

        switch (value.Trim().ToLowerInvariant())
        {
            case "tap": type = NoteType.Tap; return true;
            case "hold": type = NoteType.Hold; return true;
            case "twin": type = NoteType.Twin; return true;
            default: return false;
        }
    }

    private static bool TryParseLane(string value, Dictionary<string, int> laneIndexByName, out int lane)
    {
        lane = 0;
        if (string.IsNullOrEmpty(value)) return false;

        return laneIndexByName.TryGetValue(value.Trim().ToLowerInvariant(), out lane);
    }

    // Lane names come from the open scene when there is one, so a renamed or
    // reordered lane keeps working; otherwise the conventional order is used.
    private static Dictionary<string, int> BuildLaneMap()
    {
        Dictionary<string, int> map = new Dictionary<string, int>();
        NoteSpawner spawner = Object.FindAnyObjectByType<NoteSpawner>(FindObjectsInactive.Include);

        if (spawner != null && spawner.lanes != null && spawner.lanes.Length > 0)
        {
            for (int i = 0; i < spawner.lanes.Length; i++)
            {
                string laneName = spawner.lanes[i].name;
                if (!string.IsNullOrEmpty(laneName)) map[laneName.Trim().ToLowerInvariant()] = i;
            }
        }

        if (!map.ContainsKey("left")) map["left"] = 0;
        if (!map.ContainsKey("right")) map["right"] = 1;

        return map;
    }

    // The note's on-screen width is what two notes have to clear to not overlap.
    private static float MeasureNoteWidth()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NotePrefabPath);
        if (prefab == null) return FallbackNoteWidth;

        SpriteRenderer renderer = prefab.GetComponentInChildren<SpriteRenderer>(true);
        if (renderer == null || renderer.sprite == null) return FallbackNoteWidth;

        return renderer.sprite.bounds.size.x * Mathf.Abs(prefab.transform.localScale.x);
    }

    // Gameplay audio is decompressed up front so PlayScheduled is not racing a
    // decoder; the preview streams, so a select screen full of songs does not
    // pull every clip into memory.
    private static void ApplyAudioSettings(AudioClip clip, bool gameplay)
    {
        if (clip == null) return;

        string path = AssetDatabase.GetAssetPath(clip);
        AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null) return;

        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        AudioClipLoadType wanted = gameplay ? AudioClipLoadType.DecompressOnLoad : AudioClipLoadType.Streaming;

        // preloadAudioData lives on the per-platform sample settings in Unity 6,
        // not on the importer itself.
        bool changed = settings.loadType != wanted
                       || settings.preloadAudioData != gameplay
                       || importer.loadInBackground == gameplay;

        if (!changed) return;

        settings.loadType = wanted;
        settings.preloadAudioData = gameplay;
        importer.defaultSampleSettings = settings;
        importer.loadInBackground = !gameplay;
        importer.SaveAndReimport();
    }

    private static void UpdateLibrary(List<SongChart> charts)
    {
        BeatmapLibrary library = AssetDatabase.LoadAssetAtPath<BeatmapLibrary>(LibraryPath);
        bool isNew = library == null;
        if (isNew) library = ScriptableObject.CreateInstance<BeatmapLibrary>();

        library.charts = new List<SongChart>(charts);
        library.charts.Sort((a, b) =>
        {
            int bySong = string.CompareOrdinal(a.songId, b.songId);
            return bySong != 0 ? bySong : a.difficulty.CompareTo(b.difficulty);
        });

        if (isNew) AssetDatabase.CreateAsset(library, LibraryPath);
        EditorUtility.SetDirty(library);
    }

    private static string Summarise(string assetPath, BeatmapJson json, List<NoteData> notes,
                                    float baseSpeed, float speed, float minGap, float noteWidth)
    {
        int taps = 0, holds = 0, twins = 0;

        for (int i = 0; i < notes.Count; i++)
        {
            if (notes[i].type == NoteType.Hold) holds++;
            else if (notes[i].type == NoteType.Twin) twins++;
            else taps++;
        }

        StringBuilder report = new StringBuilder();
        report.AppendLine($"Imported {assetPath}");
        report.AppendLine($"  \"{json.name}\" by {json.author} - difficulty {json.difficulty}, bpm {json.bpm}, offset {json.offset}s");
        report.AppendLine($"  {notes.Count} notes: {taps} Tap, {holds} Hold, {twins} Twin");

        if (speed > baseSpeed + 0.0001f)
        {
            report.Append($"  noteSpeed raised {baseSpeed:F2} -> {speed:F2} u/s: the tightest same-lane gap is " +
                          $"{minGap:F3}s and a note is {noteWidth:F2}u wide, so at {baseSpeed:F2} u/s they would overlap.");
        }
        else
        {
            report.Append($"  noteSpeed {speed:F2} u/s from bpm; tightest same-lane gap {minGap:F3}s clears a {noteWidth:F2}u note.");
        }

        return report.ToString();
    }

    private static IEnumerable<string> JsonFilesIn(string folder)
    {
        foreach (string path in System.IO.Directory.GetFiles(folder, "*.json", System.IO.SearchOption.TopDirectoryOnly))
        {
            yield return path.Replace('\\', '/');
        }
    }

    // Matches by file name so cover/demo/main can carry any supported extension.
    private static T LoadFirst<T>(string folder, string baseName) where T : Object
    {
        foreach (string guid in AssetDatabase.FindAssets($"{baseName} t:{typeof(T).Name}", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (System.IO.Path.GetDirectoryName(path).Replace('\\', '/') != folder) continue;
            if (System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant() != baseName) continue;

            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
        }

        return null;
    }
}
