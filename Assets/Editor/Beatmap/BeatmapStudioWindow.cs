using UnityEditor;
using UnityEngine;

// The one place for everything that turns audio into playable charts:
// generate a beatmap from a clip, import it into a SongChart, and rebuild the
// library. Scene and setup tools stay under Tools/Rhythm; this has its own
// Beatmap menu so the two never get mixed up.
//
// A window rather than bare menu items because generating takes a dozen inputs
// and the real workflow is iterative - tweak, regenerate, play, listen.
public class BeatmapStudioWindow : EditorWindow
{
    private const string BeatmapsRoot = "Assets/Beatmaps";

    private readonly ChartGeneratorSettings settings = new ChartGeneratorSettings();

    private ChartGeneratorResult lastResult;
    private SongChart lastChart;
    private bool showAdvanced;
    private Vector2 scroll;

    [MenuItem("Beatmap/Beatmap Studio...", false, 0)]
    public static void Open()
    {
        GetWindow<BeatmapStudioWindow>(false, "Beatmap Studio", true).minSize = new Vector2(400f, 560f);
    }

    [MenuItem("Beatmap/Open Beatmaps Folder", false, 40)]
    static void PingBeatmapsFolder()
    {
        Object folder = AssetDatabase.LoadAssetAtPath<Object>(BeatmapsRoot);

        if (folder == null)
        {
            Debug.LogWarning($"{BeatmapsRoot} does not exist yet - generate a chart to create it.");
            return;
        }

        Selection.activeObject = folder;
        EditorGUIUtility.PingObject(folder);
    }

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);

        AudioClip previousClip = settings.clip;
        settings.clip = (AudioClip)EditorGUILayout.ObjectField("Main clip", settings.clip, typeof(AudioClip), false);

        // Fill in the obvious fields the first time a clip is chosen, without
        // stamping over anything already typed.
        if (settings.clip != null && settings.clip != previousClip) AutoFillFromClip();

        EditorGUILayout.HelpBox(
            "Point this at the song's main clip. Demo clips are kept streaming for the select " +
            "screen and their samples cannot be read.", MessageType.None);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        settings.outputFolder = EditorGUILayout.TextField("Folder", settings.outputFolder);
        settings.fileName = EditorGUILayout.TextField("File name", settings.fileName);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Metadata", EditorStyles.boldLabel);
        settings.stageId = EditorGUILayout.TextField("Stage id", settings.stageId);
        settings.songId = EditorGUILayout.TextField("Song id", settings.songId);
        settings.songName = EditorGUILayout.TextField("Name", settings.songName);
        settings.author = EditorGUILayout.TextField("Author", settings.author);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Chart", EditorStyles.boldLabel);
        settings.difficulty = EditorGUILayout.IntSlider("Difficulty", settings.difficulty, 1, 10);
        EditorGUILayout.LabelField(" ", $"{(settings.difficulty <= 5 ? "Dễ" : "Khó")}  ({DescribeDifficulty()})");

        settings.sensitivity = EditorGUILayout.Slider("Onset sensitivity", settings.sensitivity, 1f, 3f);
        settings.bpmOverride = EditorGUILayout.FloatField("BPM override (0 = auto)", settings.bpmOverride);

        EditorGUILayout.Space();
        DrawNoteTypes();

        EditorGUILayout.Space();
        DrawChartingStyle();

        EditorGUILayout.Space();
        showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced", true);

        if (showAdvanced)
        {
            EditorGUI.indentLevel++;
            settings.windowSize = EditorGUILayout.IntPopup("FFT window", settings.windowSize,
                new[] { "512", "1024", "2048" }, new[] { 512, 1024, 2048 });
            settings.hopSize = EditorGUILayout.IntPopup("Hop size", settings.hopSize,
                new[] { "256", "512", "1024" }, new[] { 256, 512, 1024 });
            settings.maxSameLaneRun = EditorGUILayout.IntSlider("Max same-lane run", settings.maxSameLaneRun, 1, 8);
            settings.seed = EditorGUILayout.IntField("Seed", settings.seed);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(settings.clip == null))
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate", GUILayout.Height(30f))) Generate(import: false);
            if (GUILayout.Button("Generate + Import", GUILayout.Height(30f))) Generate(import: true);
        }

        DrawReport();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Library", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Timeline Editor")) BeatmapTimelineWindow.Open();
            if (GUILayout.Button("Import All Beatmaps")) BeatmapImporter.ImportAll();
            if (GUILayout.Button("Open Beatmaps Folder")) PingBeatmapsFolder();
        }

        EditorGUILayout.EndScrollView();
    }

    // The hand-charting habits the generator follows, as dials.
    private void DrawChartingStyle()
    {
        EditorGUILayout.LabelField("Charting style", EditorStyles.boldLabel);

        settings.energyFollow = EditorGUILayout.Slider(
            new GUIContent("Follow song energy", "0 spreads notes evenly. Higher makes loud sections dense and " +
                           "quiet ones sparse, the way hand-made charts build and release."),
            settings.energyFollow, 0f, 1f);

        settings.repeatPatterns = EditorGUILayout.Toggle(
            new GUIContent("Repeat patterns", "A bar with the same rhythm as an earlier one plays the same way."),
            settings.repeatPatterns);

        using (new EditorGUI.DisabledScope(!settings.repeatPatterns))
        {
            EditorGUI.indentLevel++;
            settings.mirrorRepeats = EditorGUILayout.Toggle(
                new GUIContent("Mirror every other repeat", "Call and response. Difficulty 5 and up."),
                settings.mirrorRepeats);
            EditorGUI.indentLevel--;
        }

        settings.handFlow = EditorGUILayout.Toggle(
            new GUIContent("Alternate hands on fast runs", "Notes half a beat apart or closer alternate lanes, " +
                           "so streams are played with two thumbs."),
            settings.handFlow);

        settings.leaveQuietIntro = EditorGUILayout.Toggle(
            new GUIContent("Leave quiet intro empty", "Start charting where the music comes in, like osu!. " +
                           "The game shows a Skip button over a long empty opening."),
            settings.leaveQuietIntro);
    }

    private void DrawNoteTypes()
    {
        EditorGUILayout.LabelField("Note types", EditorStyles.boldLabel);

        settings.enableHolds = EditorGUILayout.Toggle("Hold notes", settings.enableHolds);

        using (new EditorGUI.DisabledScope(!settings.enableHolds))
        {
            EditorGUI.indentLevel++;
            settings.holdAmount = EditorGUILayout.Slider(
                new GUIContent("Hold amount", "How much a sound may fade and still count as held. " +
                               "Higher turns more sustained sounds into holds."),
                settings.holdAmount, 0f, 1f);
            settings.minHoldBeats = EditorGUILayout.Slider(
                new GUIContent("Min hold (beats)", "Shortest ringing sound worth a hold. Shorter ones stay Taps."),
                settings.minHoldBeats, 0.5f, 4f);
            EditorGUI.indentLevel--;
        }

        settings.enableTwins = EditorGUILayout.Toggle("Twin notes (both lanes)", settings.enableTwins);

        using (new EditorGUI.DisabledScope(!settings.enableTwins))
        {
            EditorGUI.indentLevel++;
            settings.twinAmount = EditorGUILayout.Slider(
                new GUIContent("Twin amount", "Scales how many big accents become Twins. " +
                               "Difficulty 1-2 never has any."),
                settings.twinAmount, 0f, 2f);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.HelpBox(
            "Holds go on sounds that keep ringing: sustained vocals, pads, strings, synth leads and " +
            "ringing cymbals. Short hits and plucks stay Taps. Twins go on the strongest on-beat accents.",
            MessageType.None);
    }

    private void Generate(bool import)
    {
        if (settings.hopSize > settings.windowSize)
        {
            settings.hopSize = settings.windowSize / 2;
        }

        lastChart = null;
        lastResult = ChartGenerator.Generate(settings);

        if (!lastResult.ok)
        {
            if (!string.IsNullOrEmpty(lastResult.message)) Debug.LogWarning(lastResult.message);
            return;
        }

        Debug.Log(lastResult.message);

        if (import)
        {
            lastChart = BeatmapImporter.ImportFile(lastResult.jsonPath);
            if (lastChart == null) return;

            // The output folder need not hold the song - the clip can come from
            // anywhere in the project - so fall back to the clip the chart was
            // generated from rather than leaving the stage silent.
            if (lastChart.clip == null)
            {
                lastChart.clip = settings.clip;
                EditorUtility.SetDirty(lastChart);
                AssetDatabase.SaveAssets();
            }

            Selection.activeObject = lastChart;
        }
    }

    private void DrawReport()
    {
        if (lastResult == null) return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);

        if (!lastResult.ok)
        {
            EditorGUILayout.HelpBox(lastResult.message, MessageType.Warning);
            return;
        }

        TempoEstimate tempo = lastResult.tempo;

        EditorGUILayout.LabelField("Detected BPM", $"{tempo.bpm:0.##}");
        EditorGUILayout.LabelField("Beat phase", $"{tempo.phase:0.###}s   (bar starts on beat {lastResult.downbeatOffset + 1})");
        EditorGUILayout.LabelField("Confidence", $"{tempo.confidence:0.##}  (rival {tempo.rivalRatio:0.##})");

        // Confidence is the whole point of auto-detection being honest about
        // itself: a wrong tempo makes every note wrong, so say when to distrust it.
        if (tempo.confidence < 2.5f || tempo.rivalRatio > 0.85f)
        {
            EditorGUILayout.HelpBox(
                "Low confidence - another tempo fit almost as well. Check the chart against the music, " +
                "and set a BPM override if it feels wrong.", MessageType.Warning);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Onsets found", lastResult.onsetsFound.ToString());
        EditorGUILayout.LabelField("Notes kept", lastResult.notesKept.ToString());
        EditorGUILayout.LabelField("Note types",
            $"{lastResult.tapCount} Tap / {lastResult.holdCount} Hold / {lastResult.twinCount} Twin");
        EditorGUILayout.LabelField("Density", $"{lastResult.notesPerSecond:0.##} notes/sec");
        EditorGUILayout.LabelField("Lane split", $"{lastResult.leftLaneCount} left / {lastResult.rightLaneCount} right");
        EditorGUILayout.LabelField("Structure",
            $"{lastResult.barCount} bars, {lastResult.reusedPatternBars} reuse a pattern, {lastResult.restsInserted} rests added");

        if (lastResult.introSeconds > 0f)
        {
            EditorGUILayout.LabelField("Quiet intro",
                $"{lastResult.introSeconds:0.#}s left empty - players get a Skip button");
        }
        EditorGUILayout.LabelField("Implied note speed", $"{lastResult.impliedNoteSpeed:0.##} units/sec");

        // Density and readability are the same dial seen from two ends: the
        // importer raises scroll speed until notes stop overlapping.
        if (lastResult.impliedNoteSpeed > 6f)
        {
            EditorGUILayout.HelpBox(
                $"At {lastResult.impliedNoteSpeed:0.##} units/sec notes will cross the screen quickly, because " +
                "the importer raises speed until same-lane notes stop overlapping. Lower the difficulty if it " +
                "reads badly.", MessageType.Info);
        }

        EditorGUILayout.Space();

        if (lastChart != null)
        {
            EditorGUILayout.HelpBox($"Imported into {AssetDatabase.GetAssetPath(lastChart)} and added to the library.",
                                    MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox($"Wrote {lastResult.jsonPath}.\nClick Generate + Import, or Import All " +
                                    "Beatmaps below, to turn it into a playable chart.", MessageType.Info);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Open in Timeline"))
            {
                BeatmapTimelineWindow.OpenFile(lastResult.jsonPath, settings.clip);
            }

            if (GUILayout.Button("Select JSON"))
            {
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<Object>(lastResult.jsonPath);
            }

            using (new EditorGUI.DisabledScope(lastChart == null))
            {
                if (GUILayout.Button("Select Chart")) Selection.activeObject = lastChart;
            }
        }
    }

    private void AutoFillFromClip()
    {
        string path = AssetDatabase.GetAssetPath(settings.clip);
        string folder = System.IO.Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(folder))
        {
            folder = folder.Replace('\\', '/');

            // Only adopt the clip's folder when it looks like a beatmap folder,
            // so picking a clip from elsewhere does not scatter output.
            if (folder.StartsWith(BeatmapsRoot + "/"))
            {
                settings.outputFolder = folder;
                settings.songId = System.IO.Path.GetFileName(folder);
            }
        }

        if (string.IsNullOrEmpty(settings.songName)) settings.songName = settings.clip.name;
        if (string.IsNullOrEmpty(settings.fileName)) settings.fileName = "generated";
    }

    private string DescribeDifficulty()
    {
        switch (settings.difficulty)
        {
            case 1:
            case 2: return "quarter notes, sparse, no twins";
            case 3:
            case 4: return "eighth notes, light";
            case 5:
            case 6: return "eighth notes, busy";
            case 7:
            case 8: return "sixteenths, dense";
            default: return "sixteenths, very dense";
        }
    }
}
