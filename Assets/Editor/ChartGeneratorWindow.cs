using UnityEditor;
using UnityEngine;

// The project's only EditorWindow. Every other tool is a bare [MenuItem], but
// generating a chart takes about eight inputs and the real workflow is
// iterative - tweak the sensitivity, regenerate, listen - which a menu item
// cannot support.
public class ChartGeneratorWindow : EditorWindow
{
    private readonly ChartGeneratorSettings settings = new ChartGeneratorSettings();

    private ChartGeneratorResult lastResult;
    private bool showAdvanced;
    private Vector2 scroll;

    [MenuItem("Tools/Rhythm/Generate Chart From Audio")]
    static void Open()
    {
        GetWindow<ChartGeneratorWindow>(false, "Chart Generator", true).minSize = new Vector2(380f, 520f);
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
        {
            if (GUILayout.Button("Generate", GUILayout.Height(30f))) Generate();
        }

        DrawReport();

        EditorGUILayout.EndScrollView();
    }

    private void Generate()
    {
        if (settings.hopSize > settings.windowSize)
        {
            settings.hopSize = settings.windowSize / 2;
        }

        lastResult = ChartGenerator.Generate(settings);

        if (lastResult.ok) Debug.Log(lastResult.message);
        else if (!string.IsNullOrEmpty(lastResult.message)) Debug.LogWarning(lastResult.message);
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
        EditorGUILayout.LabelField("Beat phase", $"{tempo.phase:0.###}s");
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
        EditorGUILayout.LabelField("Density", $"{lastResult.notesPerSecond:0.##} notes/sec");
        EditorGUILayout.LabelField("Lane split", $"{lastResult.leftLaneCount} left / {lastResult.rightLaneCount} right");
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
        EditorGUILayout.HelpBox(
            $"Wrote {lastResult.jsonPath}.\nRun Tools/Rhythm/Import Beatmaps to turn it into a chart asset.",
            MessageType.Info);

        if (GUILayout.Button("Select JSON"))
        {
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<Object>(lastResult.jsonPath);
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
            if (folder.StartsWith("Assets/Beatmaps/"))
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
            case 2: return "quarter notes, sparse";
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
