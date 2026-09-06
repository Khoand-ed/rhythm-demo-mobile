using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// Everything the generator needs, so the window is just a form over this.
public class ChartGeneratorSettings
{
    public AudioClip clip;
    public string outputFolder = "Assets/Beatmaps/song_001";
    public string fileName = "generated";

    public string stageId = "stage_001";
    public string songId = "song_001";
    public string songName = "";
    public string author = "";

    [Range(1, 10)]
    public int difficulty = 3;

    // 0 means "detect it"; anything else overrides the estimate.
    public float bpmOverride = 0f;

    // Higher rejects more onsets. 1.4-1.6 suits most music.
    public float sensitivity = 1.5f;

    public int windowSize = 1024;
    public int hopSize = 512;

    // How many notes may share a lane before alternation is forced.
    public int maxSameLaneRun = 3;

    public int seed = 12345;
}

public class ChartGeneratorResult
{
    public bool ok;
    public string message;
    public string jsonPath;

    public TempoEstimate tempo;
    public int onsetsFound;
    public int notesKept;
    public float notesPerSecond;
    public float impliedNoteSpeed;
    public int leftLaneCount;
    public int rightLaneCount;
}

// Turns an audio clip into a beatmap.json. The existing Tools/Rhythm/Import
// Beatmaps then converts that into a SongChart, so generated and hand-written
// charts travel exactly the same path.
public static class ChartGenerator
{
    // Grid subdivisions per beat, and density targets, indexed by difficulty.
    // Index 0 is unused so the tables read as difficulty 1..10.
    private static readonly int[] SubdivisionByDifficulty = { 0, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4 };
    private static readonly float[] NotesPerSecondByDifficulty = { 0f, 0.8f, 1.1f, 1.5f, 1.9f, 2.3f, 2.8f, 3.3f, 3.9f, 4.5f, 5.2f };
    private static readonly float[] MinSameLaneGapByDifficulty = { 0f, 0.6f, 0.6f, 0.5f, 0.5f, 0.4f, 0.4f, 0.28f, 0.28f, 0.2f, 0.2f };

    // Matches BeatmapImporter's own margin, so the speed reported here is the
    // speed the importer will actually derive.
    private const float SpacingMargin = 1.05f;
    private const float FallbackNoteWidth = 1.25f;

    public static ChartGeneratorResult Generate(ChartGeneratorSettings settings)
    {
        ChartGeneratorResult result = new ChartGeneratorResult();

        if (settings.clip == null)
        {
            result.message = "No audio clip selected.";
            return result;
        }

        float[] mono;
        string loadError;
        if (!TryLoadMono(settings.clip, out mono, out loadError))
        {
            result.message = loadError;
            return result;
        }

        int sampleRate = settings.clip.frequency;
        float duration = mono.Length / (float)sampleRate;

        OnsetDetector detector = new OnsetDetector(settings.windowSize, settings.hopSize);
        float frameRate = detector.FrameRate(sampleRate);

        float[] flux;
        float[] lowRatio;
        bool cancelled = false;

        try
        {
            detector.Analyse(mono, sampleRate, out flux, out lowRatio, progress =>
            {
                cancelled = EditorUtility.DisplayCancelableProgressBar(
                    "Generating chart", $"Analysing {settings.clip.name}...", progress * 0.8f);
                return !cancelled;
            });
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (cancelled)
        {
            result.message = "Cancelled.";
            return result;
        }

        List<Onset> onsets = detector.PickPeaks(flux, lowRatio, frameRate, settings.sensitivity, 0.05f);
        result.onsetsFound = onsets.Count;

        if (onsets.Count == 0)
        {
            result.message = "No onsets detected. Try lowering the sensitivity.";
            return result;
        }

        TempoEstimate tempo = new TempoEstimator().Estimate(flux, frameRate);

        if (settings.bpmOverride > 0f)
        {
            // Keep the detected phase: the override is about tempo, and the
            // phase estimate is independent and usually the reliable half.
            tempo.bpm = settings.bpmOverride;
        }

        result.tempo = tempo;

        int difficulty = Mathf.Clamp(settings.difficulty, 1, 10);
        float beatPeriod = 60f / Mathf.Max(1f, tempo.bpm);
        float gridStep = beatPeriod / SubdivisionByDifficulty[difficulty];

        List<Onset> quantised = Quantise(onsets, tempo.phase, gridStep);
        List<Onset> kept = ThinToDensity(quantised, NotesPerSecondByDifficulty[difficulty], duration);

        List<BeatmapNoteJson> notes = AssignLanes(kept, settings, MinSameLaneGapByDifficulty[difficulty],
                                                  out result.leftLaneCount, out result.rightLaneCount);

        result.notesKept = notes.Count;
        result.notesPerSecond = duration > 0f ? notes.Count / duration : 0f;
        result.impliedNoteSpeed = ImpliedNoteSpeed(notes, tempo.bpm);

        string json = BuildJson(settings, tempo, notes);
        string path = $"{settings.outputFolder}/{settings.fileName}.json";

        System.IO.Directory.CreateDirectory(settings.outputFolder);
        System.IO.File.WriteAllText(path, json);
        AssetDatabase.ImportAsset(path);

        result.ok = true;
        result.jsonPath = path;
        result.message = $"Wrote {notes.Count} notes to {path}.";
        return result;
    }

    // GetData fails on Streaming clips, which is exactly what BeatmapImporter
    // sets demo clips to - so this fixes the setting rather than failing with
    // an opaque error, and only ever on the clip the user picked.
    private static bool TryLoadMono(AudioClip clip, out float[] mono, out string error)
    {
        mono = null;
        error = null;

        string path = AssetDatabase.GetAssetPath(clip);
        AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;

        if (importer != null)
        {
            AudioImporterSampleSettings sample = importer.defaultSampleSettings;

            if (sample.loadType != AudioClipLoadType.DecompressOnLoad || !sample.preloadAudioData)
            {
                if (System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant() == "demo")
                {
                    error = $"{path} is a demo clip, which the beatmap importer keeps streaming for the " +
                            "select screen. Point the generator at the song's main clip instead.";
                    return false;
                }

                sample.loadType = AudioClipLoadType.DecompressOnLoad;
                sample.preloadAudioData = true;
                importer.defaultSampleSettings = sample;
                importer.SaveAndReimport();

                Debug.Log($"{path}: set to Decompress On Load so its samples can be read.");
                clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }
        }

        int channels = Mathf.Max(1, clip.channels);
        float[] interleaved = new float[clip.samples * channels];

        if (!clip.GetData(interleaved, 0))
        {
            error = $"Could not read samples from {clip.name}.";
            return false;
        }

        mono = new float[clip.samples];

        for (int i = 0; i < mono.Length; i++)
        {
            float sum = 0f;
            for (int c = 0; c < channels; c++) sum += interleaved[i * channels + c];
            mono[i] = sum / channels;
        }

        return true;
    }

    // Snaps onsets to the grid and collapses each slot to its strongest hit.
    // Anything landing more than half a step away was never on the grid.
    private static List<Onset> Quantise(List<Onset> onsets, float phase, float gridStep)
    {
        Dictionary<int, Onset> bySlot = new Dictionary<int, Onset>();

        for (int i = 0; i < onsets.Count; i++)
        {
            Onset onset = onsets[i];

            int slot = Mathf.RoundToInt((onset.time - phase) / gridStep);
            float snapped = phase + slot * gridStep;

            if (snapped < 0f) continue;
            if (Mathf.Abs(snapped - onset.time) > gridStep * 0.5f) continue;

            onset.time = snapped;

            Onset existing;
            if (bySlot.TryGetValue(slot, out existing) && existing.strength >= onset.strength) continue;

            bySlot[slot] = onset;
        }

        List<Onset> result = new List<Onset>(bySlot.Values);
        result.Sort((a, b) => a.time.CompareTo(b.time));
        return result;
    }

    // Keeps the strongest onsets up to the difficulty's density target.
    private static List<Onset> ThinToDensity(List<Onset> onsets, float notesPerSecond, float duration)
    {
        int target = Mathf.Max(1, Mathf.RoundToInt(notesPerSecond * duration));
        if (onsets.Count <= target) return onsets;

        List<Onset> byStrength = new List<Onset>(onsets);
        byStrength.Sort((a, b) => b.strength.CompareTo(a.strength));
        byStrength.RemoveRange(target, byStrength.Count - target);
        byStrength.Sort((a, b) => a.time.CompareTo(b.time));

        return byStrength;
    }

    // Low-frequency hits (kicks) go left, brighter hits (snares, hats) right,
    // so the pattern tracks the music instead of looking arbitrary. A run guard
    // breaks up long stretches in one lane, and dropping notes that crowd their
    // own lane keeps the importer from deriving a punishing scroll speed.
    private static List<BeatmapNoteJson> AssignLanes(List<Onset> onsets, ChartGeneratorSettings settings,
                                                     float minSameLaneGap, out int leftCount, out int rightCount)
    {
        List<BeatmapNoteJson> notes = new List<BeatmapNoteJson>();
        System.Random random = new System.Random(settings.seed);

        leftCount = 0;
        rightCount = 0;

        float lastLeft = float.NegativeInfinity;
        float lastRight = float.NegativeInfinity;
        int lastLane = -1;
        int sameLaneRun = 0;

        for (int i = 0; i < onsets.Count; i++)
        {
            Onset onset = onsets[i];

            int lane = onset.lowRatio >= 0.5f ? 0 : 1;

            // Nudge ambiguous hits rather than letting them clump in one lane.
            if (Mathf.Abs(onset.lowRatio - 0.5f) < 0.05f) lane = random.Next(2);

            if (lane == lastLane && sameLaneRun >= settings.maxSameLaneRun) lane = 1 - lane;

            float lastInLane = lane == 0 ? lastLeft : lastRight;

            // Too close in this lane: try the other one, and drop the note if
            // that is crowded too.
            if (onset.time - lastInLane < minSameLaneGap)
            {
                int other = 1 - lane;
                float lastInOther = other == 0 ? lastLeft : lastRight;

                if (onset.time - lastInOther < minSameLaneGap) continue;
                lane = other;
            }

            if (lane == 0) { lastLeft = onset.time; leftCount++; }
            else { lastRight = onset.time; rightCount++; }

            sameLaneRun = lane == lastLane ? sameLaneRun + 1 : 1;
            lastLane = lane;

            notes.Add(new BeatmapNoteJson
            {
                id = notes.Count + 1,
                type = "Tap",
                lane = lane == 0 ? "Left" : "Right",
                time = onset.time,
            });
        }

        return notes;
    }

    // Mirrors BeatmapImporter's rule, so the window can show the scroll speed
    // this chart will actually produce before it is imported.
    private static float ImpliedNoteSpeed(List<BeatmapNoteJson> notes, float bpm)
    {
        float baseSpeed = bpm / 60f;
        float minGap = float.MaxValue;

        float lastLeft = float.NegativeInfinity;
        float lastRight = float.NegativeInfinity;

        for (int i = 0; i < notes.Count; i++)
        {
            bool left = notes[i].lane == "Left";
            float previous = left ? lastLeft : lastRight;

            if (!float.IsNegativeInfinity(previous)) minGap = Mathf.Min(minGap, notes[i].time - previous);

            if (left) lastLeft = notes[i].time;
            else lastRight = notes[i].time;
        }

        if (minGap == float.MaxValue || minGap <= 0f) return baseSpeed;

        return Mathf.Max(baseSpeed, NoteWidth() * SpacingMargin / minGap);
    }

    private static float NoteWidth()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Note.prefab");
        if (prefab == null) return FallbackNoteWidth;

        SpriteRenderer renderer = prefab.GetComponentInChildren<SpriteRenderer>(true);
        if (renderer == null || renderer.sprite == null) return FallbackNoteWidth;

        return renderer.sprite.bounds.size.x * Mathf.Abs(prefab.transform.localScale.x);
    }

    // Hand-rolled rather than JsonUtility so the note list stays one object per
    // line, which is what makes a generated chart reviewable and hand-editable.
    private static string BuildJson(ChartGeneratorSettings settings, TempoEstimate tempo,
                                    List<BeatmapNoteJson> notes)
    {
        StringBuilder json = new StringBuilder();

        json.AppendLine("{");
        json.AppendLine($"  \"stageId\": \"{Escape(settings.stageId)}\",");
        json.AppendLine($"  \"songId\": \"{Escape(settings.songId)}\",");
        json.AppendLine($"  \"name\": \"{Escape(settings.songName)}\",");
        json.AppendLine($"  \"author\": \"{Escape(settings.author)}\",");
        json.AppendLine($"  \"bpm\": {tempo.bpm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)},");
        json.AppendLine($"  \"difficulty\": {Mathf.Clamp(settings.difficulty, 1, 10)},");

        // The detected beat phase lives in the note times, not here: offset is
        // an audio-sync correction, which generation knows nothing about.
        json.AppendLine("  \"offset\": 0,");
        json.AppendLine("  \"notes\": [");

        for (int i = 0; i < notes.Count; i++)
        {
            BeatmapNoteJson note = notes[i];
            string time = note.time.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            string comma = i < notes.Count - 1 ? "," : "";

            json.AppendLine($"    {{ \"id\": {note.id}, \"type\": \"Tap\", \"lane\": \"{note.lane}\", \"time\": {time} }}{comma}");
        }

        json.AppendLine("  ]");
        json.AppendLine("}");

        return json.ToString();
    }

    private static string Escape(string value)
    {
        return string.IsNullOrEmpty(value) ? "" : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
