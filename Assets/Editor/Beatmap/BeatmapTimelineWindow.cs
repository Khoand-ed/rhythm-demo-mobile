using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// A chart editor in the spirit of every rhythm game's built-in editor: the
// song's waveform, the beat grid with bar lines, both lanes laid out along
// time, and playback with hit sounds so a chart can be judged by ear.
//
// Mouse
//   click empty lane          add a Tap (snapped)
//   drag in empty lane        add a Hold from press to release
//   alt+click                 add a Twin
//   click / shift+click note  select / add to selection
//   drag a selected note      move the selection (snapped)
//   right-click               note menu: type, lane, delete
//   click ruler or waveform   move the playhead
//   wheel / ctrl+wheel        scroll / zoom
// Keys
//   space play/stop · delete remove · M mirror · ctrl+A select all
//   ctrl+Z / ctrl+Y undo / redo · ctrl+S save · left/right nudge playhead
public class BeatmapTimelineWindow : EditorWindow
{
    private const float ToolbarHeight = 22f;
    private const float RulerHeight = 20f;
    private const float WaveHeight = 70f;
    private const float LaneHeight = 48f;
    private const float DensityHeight = 16f;
    private const float ScrollbarHeight = 14f;
    private const float LaneLabelWidth = 44f;
    private const float NoteWidth = 10f;
    private const float WavePeaksPerSecond = 200f;
    private const int MaxUndo = 100;

    private static readonly Color Background = new Color(0.13f, 0.13f, 0.15f);
    private static readonly Color LaneBackground = new Color(0.17f, 0.17f, 0.2f);
    private static readonly Color BarLine = new Color(1f, 1f, 1f, 0.35f);
    private static readonly Color BeatLine = new Color(1f, 1f, 1f, 0.14f);
    private static readonly Color SnapLine = new Color(1f, 1f, 1f, 0.05f);
    private static readonly Color WaveColor = new Color(0.45f, 0.6f, 0.8f, 0.8f);
    private static readonly Color TapColor = new Color(0.95f, 0.95f, 1f);
    private static readonly Color HoldColor = new Color(0.55f, 0.9f, 1f);
    private static readonly Color TwinColor = new Color(1f, 0.82f, 0.3f);
    private static readonly Color SelectColor = new Color(1f, 0.5f, 0.1f);
    private static readonly Color ProblemColor = new Color(1f, 0.25f, 0.25f);
    private static readonly Color PlayheadColor = new Color(1f, 0.3f, 0.3f);

    private static readonly string[] SnapLabels = { "1/1", "1/2", "1/3", "1/4", "1/6", "1/8" };
    private static readonly int[] SnapValues = { 1, 2, 3, 4, 6, 8 };

    // Document
    private TextAsset beatmapAsset;
    private string jsonPath;
    private BeatmapJson header;
    private List<BeatmapNoteJson> notes = new List<BeatmapNoteJson>();
    private AudioClip clip;
    private bool dirty;
    private bool phaseEstimated;

    // View
    private float viewStart;
    private float pixelsPerSecond = 160f;
    private int snapIndex = 3;
    private float playhead;
    private bool hitSounds = true;
    private bool metronome;

    // Selection and dragging
    private readonly HashSet<BeatmapNoteJson> selection = new HashSet<BeatmapNoteJson>();
    private enum Drag { None, Create, Move, Seek }
    private Drag drag;
    private float dragAnchorTime;
    private int dragLane;
    private float dragCurrentTime;
    private readonly Dictionary<BeatmapNoteJson, float> dragOrigins = new Dictionary<BeatmapNoteJson, float>();
    private bool movedWhileDragging;

    // Undo
    private readonly List<string> undo = new List<string>();
    private readonly List<string> redo = new List<string>();

    // Waveform
    private float[] peaks;
    private AudioClip peaksFor;
    private string waveformMessage;

    // Playback
    private GameObject audioHost;
    private AudioSource music;
    private AudioSource sfx;
    private AudioClip tickClip;
    private AudioClip clickClip;
    private AudioClip accentClickClip;
    private bool playing;
    private List<float> tickTimes = new List<float>();
    private int nextTick;
    private int nextBeat;

    // Layout, recomputed every OnGUI
    private Rect timelineRect;
    private Rect rulerRect;
    private Rect waveRect;
    private readonly Rect[] laneRects = new Rect[2];
    private Rect densityRect;

    [MenuItem("Beatmap/Timeline Editor...", false, 1)]
    public static void Open()
    {
        GetWindow<BeatmapTimelineWindow>(false, "Beatmap Timeline", true).minSize = new Vector2(640f, 320f);
    }

    public static void OpenFile(string path, AudioClip audio)
    {
        BeatmapTimelineWindow window = GetWindow<BeatmapTimelineWindow>(false, "Beatmap Timeline", true);
        window.minSize = new Vector2(640f, 320f);
        window.Load(AssetDatabase.LoadAssetAtPath<TextAsset>(path), audio);
    }

    void OnDisable()
    {
        StopPlayback();

        if (audioHost != null) DestroyImmediate(audioHost);
        if (clickClip != null) DestroyImmediate(clickClip);
        if (accentClickClip != null) DestroyImmediate(accentClickClip);
    }

    // ---------------------------------------------------------------- loading

    private void Load(TextAsset asset, AudioClip audio)
    {
        if (dirty && beatmapAsset != null && asset != beatmapAsset &&
            !EditorUtility.DisplayDialog("Unsaved changes", $"Discard the changes to {beatmapAsset.name}?", "Discard", "Keep editing"))
        {
            return;
        }

        StopPlayback();
        selection.Clear();
        undo.Clear();
        redo.Clear();
        dirty = false;

        beatmapAsset = asset;
        jsonPath = asset != null ? AssetDatabase.GetAssetPath(asset) : null;
        header = null;
        notes = new List<BeatmapNoteJson>();

        if (asset != null)
        {
            try
            {
                header = JsonUtility.FromJson<BeatmapJson>(asset.text);
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"{jsonPath}: not valid JSON - {exception.Message}");
            }
        }

        if (header != null)
        {
            notes = header.notes ?? new List<BeatmapNoteJson>();
            header.notes = null;
            SortNotes();

            phaseEstimated = false;
            if (Mathf.Approximately(header.beatPhase, 0f) && notes.Count > 0)
            {
                header.beatPhase = EstimatePhase();
                phaseEstimated = true;
            }
        }

        clip = audio != null ? audio : FindClipFor(jsonPath);
        viewStart = 0f;
        playhead = 0f;
        Repaint();
    }

    // The chart asset next to the json knows its clip; failing that, a song
    // folder with exactly one clip in it.
    private static AudioClip FindClipFor(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        SongChart chart = AssetDatabase.LoadAssetAtPath<SongChart>(System.IO.Path.ChangeExtension(path, ".asset"));
        if (chart != null && chart.clip != null) return chart.clip;

        string folder = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folder });
        return guids.Length == 1 ? AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
    }

    // For hand-written charts with no beatPhase: the phase that puts the most
    // notes on the beat - a circular mean of note times modulo one beat.
    private float EstimatePhase()
    {
        float period = BeatPeriod;
        float sx = 0f;
        float sy = 0f;

        foreach (BeatmapNoteJson note in notes)
        {
            float angle = Start(note) / period * 2f * Mathf.PI;
            sx += Mathf.Cos(angle);
            sy += Mathf.Sin(angle);
        }

        float phase = Mathf.Atan2(sy, sx) / (2f * Mathf.PI) * period;
        return phase < 0f ? phase + period : phase;
    }

    // ---------------------------------------------------------------- GUI

    void OnGUI()
    {
        DrawToolbar();

        if (header == null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Pick a beatmap .json above, or open one from Beatmap Studio after generating.",
                                    MessageType.Info);
            return;
        }

        DrawInfoBar();
        LayoutTimeline();
        HandleInput();

        if (Event.current.type == EventType.Repaint) DrawTimeline();

        DrawScrollbar();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            TextAsset picked = (TextAsset)EditorGUILayout.ObjectField(beatmapAsset, typeof(TextAsset), false,
                                                                     GUILayout.Width(180f));
            if (picked != beatmapAsset) Load(picked, null);

            AudioClip pickedClip = (AudioClip)EditorGUILayout.ObjectField(clip, typeof(AudioClip), false,
                                                                         GUILayout.Width(160f));
            if (pickedClip != clip)
            {
                StopPlayback();
                clip = pickedClip;
            }

            using (new EditorGUI.DisabledScope(header == null))
            {
                if (GUILayout.Button(playing ? "■ Stop" : "► Play", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                {
                    TogglePlayback();
                }

                GUILayout.Label("Snap", GUILayout.Width(32f));
                snapIndex = EditorGUILayout.Popup(snapIndex, SnapLabels, EditorStyles.toolbarPopup, GUILayout.Width(48f));

                hitSounds = GUILayout.Toggle(hitSounds, "Hit sounds", EditorStyles.toolbarButton);
                metronome = GUILayout.Toggle(metronome, "Metronome", EditorStyles.toolbarButton);

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(undo.Count == 0))
                {
                    if (GUILayout.Button("Undo", EditorStyles.toolbarButton)) Undo();
                }

                using (new EditorGUI.DisabledScope(redo.Count == 0))
                {
                    if (GUILayout.Button("Redo", EditorStyles.toolbarButton)) Redo();
                }

                if (GUILayout.Button(dirty ? "Save*" : "Save", EditorStyles.toolbarButton)) Save(false);
                if (GUILayout.Button("Save + Import", EditorStyles.toolbarButton)) Save(true);
            }
        }
    }

    private void DrawInfoBar()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUIUtility.labelWidth = 34f;

            EditorGUI.BeginChangeCheck();
            float bpm = EditorGUILayout.FloatField("BPM", header.bpm, GUILayout.Width(100f));
            EditorGUIUtility.labelWidth = 72f;
            float phase = EditorGUILayout.FloatField(phaseEstimated ? "Bar 1 (est.)" : "Bar 1 at", header.beatPhase,
                                                     GUILayout.Width(140f));
            EditorGUIUtility.labelWidth = 58f;
            int difficulty = EditorGUILayout.IntSlider("Difficulty", header.difficulty, 1, 10, GUILayout.Width(220f));

            if (EditorGUI.EndChangeCheck())
            {
                PushUndo();
                header.bpm = Mathf.Max(1f, bpm);
                header.beatPhase = phase;
                header.difficulty = difficulty;
                phaseEstimated = false;
                MarkDirty();
            }

            EditorGUIUtility.labelWidth = 0f;
            GUILayout.FlexibleSpace();

            int taps = 0, holds = 0, twins = 0;
            foreach (BeatmapNoteJson note in notes)
            {
                if (note.type == "Hold") holds++;
                else if (note.type == "Twin") twins++;
                else taps++;
            }

            float length = clip != null ? clip.length : (notes.Count > 0 ? End(notes[notes.Count - 1]) : 0f);
            float nps = length > 0f ? notes.Count / length : 0f;

            GUILayout.Label($"{taps} Tap · {holds} Hold · {twins} Twin   {nps:0.0} notes/s   " +
                            $"speed {ChartSpacing.ImpliedSpeed(notes, header.bpm):0.0} u/s   " +
                            $"{ProblemCount()} problems", EditorStyles.miniLabel);
        }
    }

    private void LayoutTimeline()
    {
        Rect last = GUILayoutUtility.GetLastRect();
        float top = last.yMax + 4f;

        timelineRect = new Rect(LaneLabelWidth, top, position.width - LaneLabelWidth,
                                RulerHeight + WaveHeight + LaneHeight * 2f + DensityHeight);

        float y = top;
        rulerRect = new Rect(timelineRect.x, y, timelineRect.width, RulerHeight); y += RulerHeight;
        waveRect = new Rect(timelineRect.x, y, timelineRect.width, WaveHeight); y += WaveHeight;
        laneRects[0] = new Rect(timelineRect.x, y, timelineRect.width, LaneHeight); y += LaneHeight;
        laneRects[1] = new Rect(timelineRect.x, y, timelineRect.width, LaneHeight); y += LaneHeight;
        densityRect = new Rect(timelineRect.x, y, timelineRect.width, DensityHeight);

        GUILayoutUtility.GetRect(position.width, timelineRect.height + 4f);
    }

    private void DrawScrollbar()
    {
        Rect bar = new Rect(timelineRect.x, timelineRect.yMax + 2f, timelineRect.width, ScrollbarHeight);
        float visible = timelineRect.width / pixelsPerSecond;
        float total = Mathf.Max(SongLength, visible);

        float scrolled = GUI.HorizontalScrollbar(bar, viewStart, visible, 0f, total);
        if (!Mathf.Approximately(scrolled, viewStart))
        {
            viewStart = scrolled;
            Repaint();
        }

        GUILayout.Space(ScrollbarHeight + 6f);

        EditorGUILayout.HelpBox(
            "Click lane: Tap · drag: Hold · alt+click: Twin · right-click note: change type · space: play · " +
            "M: mirror · ctrl+wheel: zoom", MessageType.None);
    }

    // ---------------------------------------------------------------- drawing

    private void DrawTimeline()
    {
        EditorGUI.DrawRect(new Rect(0f, timelineRect.y, position.width, timelineRect.height), Background);
        EditorGUI.DrawRect(laneRects[0], LaneBackground);
        EditorGUI.DrawRect(laneRects[1], new Color(LaneBackground.r + 0.02f, LaneBackground.g + 0.02f, LaneBackground.b + 0.02f));

        GUI.Label(new Rect(4f, laneRects[0].y + LaneHeight * 0.5f - 8f, LaneLabelWidth, 16f), "Left", EditorStyles.miniLabel);
        GUI.Label(new Rect(4f, laneRects[1].y + LaneHeight * 0.5f - 8f, LaneLabelWidth, 16f), "Right", EditorStyles.miniLabel);
        GUI.Label(new Rect(4f, waveRect.y + WaveHeight * 0.5f - 8f, LaneLabelWidth, 16f), "Audio", EditorStyles.miniLabel);

        DrawWaveform();
        DrawGrid();
        DrawDensity();
        DrawNotes();
        DrawDragPreview();

        float x = TimeToX(playhead);
        if (x >= timelineRect.x && x <= timelineRect.xMax)
        {
            EditorGUI.DrawRect(new Rect(x - 1f, timelineRect.y, 2f, timelineRect.height), PlayheadColor);
        }
    }

    private void DrawWaveform()
    {
        EnsurePeaks();

        if (peaks == null)
        {
            if (!string.IsNullOrEmpty(waveformMessage))
            {
                GUI.Label(new Rect(waveRect.x + 6f, waveRect.y + 4f, waveRect.width, 16f), waveformMessage, EditorStyles.miniLabel);
            }

            return;
        }

        float mid = waveRect.center.y;
        float half = WaveHeight * 0.45f;

        for (float x = waveRect.x; x < waveRect.xMax; x += 1f)
        {
            int from = Mathf.FloorToInt(XToTime(x) * WavePeaksPerSecond);
            int to = Mathf.FloorToInt(XToTime(x + 1f) * WavePeaksPerSecond);
            if (to < 0 || from >= peaks.Length) continue;

            float peak = 0f;
            for (int i = Mathf.Max(0, from); i <= Mathf.Min(peaks.Length - 1, to); i++) peak = Mathf.Max(peak, peaks[i]);

            float h = Mathf.Max(1f, peak * half);
            EditorGUI.DrawRect(new Rect(x, mid - h, 1f, h * 2f), WaveColor);
        }
    }

    private void DrawGrid()
    {
        float period = BeatPeriod;
        float step = period / SnapValues[snapIndex];
        float visibleEnd = XToTime(timelineRect.xMax);

        // Skip snap lines when zoomed out far enough that they would smear.
        bool drawSnap = step * pixelsPerSecond >= 6f;
        bool drawBeats = period * pixelsPerSecond >= 4f;

        int first = Mathf.FloorToInt((viewStart - header.beatPhase) / step);
        int subdivisions = SnapValues[snapIndex];

        for (int i = first; ; i++)
        {
            float t = header.beatPhase + i * step;
            if (t > visibleEnd) break;
            if (t < viewStart) continue;

            float x = TimeToX(t);
            bool onBeat = ((i % subdivisions) + subdivisions) % subdivisions == 0;
            int beat = Mathf.FloorToInt((float)i / subdivisions);
            bool onBar = onBeat && ((beat % BeatGrid.BeatsPerBar) + BeatGrid.BeatsPerBar) % BeatGrid.BeatsPerBar == 0;

            Color color = onBar ? BarLine : onBeat ? BeatLine : SnapLine;
            if (!onBeat && !drawSnap) continue;
            if (onBeat && !onBar && !drawBeats) continue;

            float top = onBar ? rulerRect.y : waveRect.y;
            EditorGUI.DrawRect(new Rect(x, top, 1f, densityRect.yMax - top), color);

            if (onBar)
            {
                int barNumber = Mathf.FloorToInt((float)beat / BeatGrid.BeatsPerBar) + 1;
                GUI.Label(new Rect(x + 3f, rulerRect.y + 2f, 40f, 16f), barNumber.ToString(), EditorStyles.miniLabel);
            }
        }
    }

    // Notes per bar under the lanes: where the chart is dense, and whether
    // that follows the waveform above it.
    private void DrawDensity()
    {
        float barPeriod = BeatPeriod * BeatGrid.BeatsPerBar;
        Dictionary<int, int> perBar = new Dictionary<int, int>();
        int max = 1;

        foreach (BeatmapNoteJson note in notes)
        {
            int bar = Mathf.FloorToInt((Start(note) - header.beatPhase) / barPeriod);
            int count;
            perBar.TryGetValue(bar, out count);
            perBar[bar] = ++count;
            max = Mathf.Max(max, count);
        }

        foreach (KeyValuePair<int, int> entry in perBar)
        {
            float x0 = TimeToX(header.beatPhase + entry.Key * barPeriod);
            float x1 = TimeToX(header.beatPhase + (entry.Key + 1) * barPeriod);
            if (x1 < timelineRect.x || x0 > timelineRect.xMax) continue;

            float share = entry.Value / (float)max;
            float h = share * (DensityHeight - 2f);
            Color color = Color.Lerp(new Color(0.3f, 0.8f, 0.4f, 0.8f), new Color(1f, 0.35f, 0.3f, 0.9f), share);
            EditorGUI.DrawRect(new Rect(x0 + 1f, densityRect.yMax - h - 1f, Mathf.Max(1f, x1 - x0 - 2f), h), color);
        }
    }

    private void DrawNotes()
    {
        HashSet<BeatmapNoteJson> problems = FindProblems();

        foreach (BeatmapNoteJson note in notes)
        {
            float x0 = TimeToX(Start(note));
            float x1 = TimeToX(End(note));
            if (x1 + NoteWidth < timelineRect.x || x0 - NoteWidth > timelineRect.xMax) continue;

            bool selected = selection.Contains(note);
            bool problem = problems.Contains(note);

            if (note.type == "Twin")
            {
                Rect rect = new Rect(x0 - NoteWidth * 0.5f, laneRects[0].y + 6f, NoteWidth, LaneHeight * 2f - 12f);
                DrawNoteRect(rect, TwinColor, selected, problem);
                continue;
            }

            Rect lane = laneRects[LaneIndex(note)];

            if (note.type == "Hold")
            {
                Rect body = new Rect(x0, lane.y + LaneHeight * 0.3f, Mathf.Max(1f, x1 - x0), LaneHeight * 0.4f);
                EditorGUI.DrawRect(body, new Color(HoldColor.r, HoldColor.g, HoldColor.b, 0.45f));
                DrawNoteRect(new Rect(x1 - 3f, lane.y + 10f, 6f, LaneHeight - 20f), HoldColor, selected, problem);
                DrawNoteRect(new Rect(x0 - NoteWidth * 0.5f, lane.y + 6f, NoteWidth, LaneHeight - 12f), HoldColor, selected, problem);
            }
            else
            {
                DrawNoteRect(new Rect(x0 - NoteWidth * 0.5f, lane.y + 6f, NoteWidth, LaneHeight - 12f), TapColor, selected, problem);
            }
        }
    }

    private static void DrawNoteRect(Rect rect, Color color, bool selected, bool problem)
    {
        if (selected || problem)
        {
            Rect outline = new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f);
            EditorGUI.DrawRect(outline, selected ? SelectColor : ProblemColor);
        }

        EditorGUI.DrawRect(rect, color);
    }

    private void DrawDragPreview()
    {
        if (drag != Drag.Create) return;

        float a = Mathf.Min(dragAnchorTime, dragCurrentTime);
        float b = Mathf.Max(dragAnchorTime, dragCurrentTime);
        Rect lane = laneRects[dragLane];

        EditorGUI.DrawRect(new Rect(TimeToX(a), lane.y + LaneHeight * 0.3f, Mathf.Max(2f, TimeToX(b) - TimeToX(a)), LaneHeight * 0.4f),
                           new Color(HoldColor.r, HoldColor.g, HoldColor.b, 0.3f));
    }

    // Notes too close to the note before them in the same lane - they would
    // overlap on screen or be unplayable - and anything outside the song.
    private HashSet<BeatmapNoteJson> FindProblems()
    {
        HashSet<BeatmapNoteJson> problems = new HashSet<BeatmapNoteJson>();
        float[] laneFree = { float.NegativeInfinity, float.NegativeInfinity };
        const float minGap = 0.06f;

        foreach (BeatmapNoteJson note in notes)
        {
            float start = Start(note);

            if (start < 0f || (clip != null && End(note) > clip.length)) problems.Add(note);

            if (note.type == "Twin")
            {
                if (start - laneFree[0] < minGap || start - laneFree[1] < minGap) problems.Add(note);
                laneFree[0] = laneFree[1] = start;
                continue;
            }

            int lane = LaneIndex(note);
            if (start - laneFree[lane] < minGap) problems.Add(note);
            laneFree[lane] = End(note);
        }

        return problems;
    }

    private int ProblemCount()
    {
        return FindProblems().Count;
    }

    // ---------------------------------------------------------------- input

    private void HandleInput()
    {
        Event e = Event.current;

        switch (e.type)
        {
            case EventType.ScrollWheel:
                if (!timelineRect.Contains(e.mousePosition)) return;

                if (e.control || e.command)
                {
                    float anchor = XToTime(e.mousePosition.x);
                    pixelsPerSecond = Mathf.Clamp(pixelsPerSecond * (e.delta.y > 0f ? 0.85f : 1.18f), 20f, 1200f);
                    viewStart = Mathf.Max(0f, anchor - (e.mousePosition.x - timelineRect.x) / pixelsPerSecond);
                }
                else
                {
                    viewStart = Mathf.Max(0f, viewStart + e.delta.y * 30f / pixelsPerSecond);
                }

                e.Use();
                Repaint();
                break;

            case EventType.MouseDown:
                if (!timelineRect.Contains(e.mousePosition)) return;
                GUIUtility.keyboardControl = 0;
                OnMouseDown(e);
                break;

            case EventType.MouseDrag:
                if (drag == Drag.None) return;
                OnMouseDrag(e);
                break;

            case EventType.MouseUp:
                if (drag == Drag.None) return;
                OnMouseUp(e);
                break;

            case EventType.KeyDown:
                if (GUIUtility.keyboardControl != 0) return;
                OnKeyDown(e);
                break;
        }
    }

    private void OnMouseDown(Event e)
    {
        float time = XToTime(e.mousePosition.x);

        if (rulerRect.Contains(e.mousePosition) || waveRect.Contains(e.mousePosition))
        {
            drag = Drag.Seek;
            Seek(time);
            e.Use();
            return;
        }

        int lane = LaneAt(e.mousePosition);
        if (lane < 0) return;

        BeatmapNoteJson hit = NoteAt(e.mousePosition);

        if (e.button == 1)
        {
            ShowContextMenu(hit, lane, time);
            e.Use();
            return;
        }

        if (e.button != 0) return;

        if (hit != null)
        {
            if (e.shift)
            {
                if (!selection.Remove(hit)) selection.Add(hit);
            }
            else if (!selection.Contains(hit))
            {
                selection.Clear();
                selection.Add(hit);
            }

            drag = Drag.Move;
            dragAnchorTime = Snap(time);
            movedWhileDragging = false;
            dragOrigins.Clear();
            foreach (BeatmapNoteJson note in selection) dragOrigins[note] = Start(note);
        }
        else if (e.alt)
        {
            AddNote(new BeatmapNoteJson { type = "Twin", time = Snap(time) });
        }
        else
        {
            selection.Clear();
            drag = Drag.Create;
            dragLane = lane;
            dragAnchorTime = Snap(time);
            dragCurrentTime = dragAnchorTime;
        }

        e.Use();
        Repaint();
    }

    private void OnMouseDrag(Event e)
    {
        float time = XToTime(e.mousePosition.x);

        switch (drag)
        {
            case Drag.Seek:
                Seek(time);
                break;

            case Drag.Create:
                dragCurrentTime = Snap(time);
                break;

            case Drag.Move:
                float delta = Snap(time) - dragAnchorTime;
                if (Mathf.Approximately(delta, 0f) && !movedWhileDragging) break;

                if (!movedWhileDragging)
                {
                    PushUndo();
                    movedWhileDragging = true;
                }

                foreach (KeyValuePair<BeatmapNoteJson, float> origin in dragOrigins)
                {
                    SetStart(origin.Key, Mathf.Max(0f, origin.Value + delta));
                }

                MarkDirty();
                break;
        }

        e.Use();
        Repaint();
    }

    private void OnMouseUp(Event e)
    {
        if (drag == Drag.Create)
        {
            float a = Mathf.Min(dragAnchorTime, dragCurrentTime);
            float b = Mathf.Max(dragAnchorTime, dragCurrentTime);
            string lane = dragLane == 0 ? "Left" : "Right";

            if (b - a >= SnapStep * 0.99f)
            {
                AddNote(new BeatmapNoteJson { type = "Hold", lane = lane, startTime = a, endTime = b });
            }
            else
            {
                AddNote(new BeatmapNoteJson { type = "Tap", lane = lane, time = dragAnchorTime });
            }
        }
        else if (drag == Drag.Move && movedWhileDragging)
        {
            SortNotes();
        }

        drag = Drag.None;
        e.Use();
        Repaint();
    }

    private void OnKeyDown(Event e)
    {
        bool ctrl = e.control || e.command;

        switch (e.keyCode)
        {
            case KeyCode.Space:
                TogglePlayback();
                break;

            case KeyCode.Delete:
            case KeyCode.Backspace:
                DeleteSelection();
                break;

            case KeyCode.M:
                MirrorSelection();
                break;

            case KeyCode.A:
                if (!ctrl) return;
                selection.Clear();
                foreach (BeatmapNoteJson note in notes) selection.Add(note);
                break;

            case KeyCode.Z:
                if (!ctrl) return;
                if (e.shift) Redo();
                else Undo();
                break;

            case KeyCode.Y:
                if (!ctrl) return;
                Redo();
                break;

            case KeyCode.S:
                if (!ctrl) return;
                Save(false);
                break;

            case KeyCode.LeftArrow:
                Seek(Mathf.Max(0f, Snap(playhead) - SnapStep));
                break;

            case KeyCode.RightArrow:
                Seek(Snap(playhead) + SnapStep);
                break;

            default:
                return;
        }

        e.Use();
        Repaint();
    }

    private void ShowContextMenu(BeatmapNoteJson hit, int lane, float time)
    {
        GenericMenu menu = new GenericMenu();
        float snapped = Snap(time);
        string laneName = lane == 0 ? "Left" : "Right";

        if (hit == null)
        {
            menu.AddItem(new GUIContent("Add Tap"), false, () => AddNote(new BeatmapNoteJson { type = "Tap", lane = laneName, time = snapped }));
            menu.AddItem(new GUIContent("Add Hold (1 beat)"), false, () => AddNote(new BeatmapNoteJson
            {
                type = "Hold", lane = laneName, startTime = snapped, endTime = snapped + BeatPeriod,
            }));
            menu.AddItem(new GUIContent("Add Twin"), false, () => AddNote(new BeatmapNoteJson { type = "Twin", time = snapped }));
        }
        else
        {
            if (!selection.Contains(hit))
            {
                selection.Clear();
                selection.Add(hit);
            }

            menu.AddItem(new GUIContent("Make Tap"), hit.type == "Tap", () => ConvertSelection("Tap", laneName));
            menu.AddItem(new GUIContent("Make Hold (1 beat)"), hit.type == "Hold", () => ConvertSelection("Hold", laneName));
            menu.AddItem(new GUIContent("Make Twin"), hit.type == "Twin", () => ConvertSelection("Twin", laneName));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Swap Lane (M)"), false, MirrorSelection);
            menu.AddItem(new GUIContent("Delete"), false, DeleteSelection);
        }

        menu.ShowAsContext();
    }

    // ---------------------------------------------------------------- edits

    private void AddNote(BeatmapNoteJson note)
    {
        PushUndo();
        notes.Add(note);
        SortNotes();
        selection.Clear();
        selection.Add(note);
        MarkDirty();
    }

    private void DeleteSelection()
    {
        if (selection.Count == 0) return;

        PushUndo();
        notes.RemoveAll(selection.Contains);
        selection.Clear();
        MarkDirty();
    }

    private void MirrorSelection()
    {
        if (selection.Count == 0) return;

        PushUndo();
        foreach (BeatmapNoteJson note in selection)
        {
            if (note.type != "Twin") note.lane = note.lane == "Left" ? "Right" : "Left";
        }

        MarkDirty();
    }

    private void ConvertSelection(string type, string fallbackLane)
    {
        PushUndo();

        foreach (BeatmapNoteJson note in selection)
        {
            float start = Start(note);
            if (string.IsNullOrEmpty(note.lane)) note.lane = fallbackLane;

            note.type = type;
            note.time = start;
            note.startTime = start;

            // A hold made from a tap starts at one beat; one that already
            // was a hold keeps its length.
            if (type == "Hold" && note.endTime <= start) note.endTime = start + BeatPeriod;
            if (type != "Hold") note.endTime = 0f;
        }

        SortNotes();
        MarkDirty();
    }

    private void PushUndo()
    {
        undo.Add(Serialize());
        if (undo.Count > MaxUndo) undo.RemoveAt(0);
        redo.Clear();
    }

    private void Undo()
    {
        if (undo.Count == 0) return;

        redo.Add(Serialize());
        Restore(undo[undo.Count - 1]);
        undo.RemoveAt(undo.Count - 1);
    }

    private void Redo()
    {
        if (redo.Count == 0) return;

        undo.Add(Serialize());
        Restore(redo[redo.Count - 1]);
        redo.RemoveAt(redo.Count - 1);
    }

    private string Serialize()
    {
        return BeatmapJsonWriter.Write(header, notes);
    }

    private void Restore(string json)
    {
        BeatmapJson restored = JsonUtility.FromJson<BeatmapJson>(json);
        notes = restored.notes ?? new List<BeatmapNoteJson>();
        restored.notes = null;
        header = restored;
        selection.Clear();
        SortNotes();
        MarkDirty();
    }

    private void MarkDirty()
    {
        dirty = true;
        if (playing) RebuildTicks();
    }

    private void Save(bool import)
    {
        if (string.IsNullOrEmpty(jsonPath)) return;

        SortNotes();
        for (int i = 0; i < notes.Count; i++) notes[i].id = i + 1;

        System.IO.File.WriteAllText(jsonPath, BeatmapJsonWriter.Write(header, notes));
        AssetDatabase.ImportAsset(jsonPath);
        dirty = false;

        if (!import)
        {
            Debug.Log($"Saved {notes.Count} notes to {jsonPath}.");
            return;
        }

        SongChart chart = BeatmapImporter.ImportFile(jsonPath);
        if (chart == null) return;

        if (chart.clip == null && clip != null)
        {
            chart.clip = clip;
            EditorUtility.SetDirty(chart);
            AssetDatabase.SaveAssets();
        }

        Debug.Log($"Saved and imported {jsonPath} into {AssetDatabase.GetAssetPath(chart)}.", chart);
    }

    // ---------------------------------------------------------------- playback

    private void TogglePlayback()
    {
        if (playing) StopPlayback();
        else StartPlayback();
    }

    private void StartPlayback()
    {
        if (clip == null)
        {
            ShowNotification(new GUIContent("No audio clip to play."));
            return;
        }

        EnsureAudio();

        music.clip = clip;
        music.time = Mathf.Clamp(playhead, 0f, clip.length - 0.01f);
        music.Play();

        RebuildTicks();

        playing = true;
        EditorApplication.update -= UpdatePlayback;
        EditorApplication.update += UpdatePlayback;
    }

    private void StopPlayback()
    {
        EditorApplication.update -= UpdatePlayback;

        if (music != null) music.Stop();
        playing = false;
        Repaint();
    }

    private void Seek(float time)
    {
        playhead = Mathf.Max(0f, time);

        if (playing)
        {
            music.time = Mathf.Clamp(playhead, 0f, clip.length - 0.01f);
            RebuildTicks();
        }

        Repaint();
    }

    private void RebuildTicks()
    {
        tickTimes.Clear();
        foreach (BeatmapNoteJson note in notes)
        {
            tickTimes.Add(Start(note));
            if (note.type == "Hold") tickTimes.Add(note.endTime);
        }

        tickTimes.Sort();

        nextTick = tickTimes.BinarySearch(playhead);
        if (nextTick < 0) nextTick = ~nextTick;

        nextBeat = Mathf.CeilToInt((playhead - header.beatPhase) / BeatPeriod);
    }

    private void UpdatePlayback()
    {
        if (music == null || !music.isPlaying)
        {
            StopPlayback();
            return;
        }

        // The audio clock is the truth: the playhead is wherever the music is.
        playhead = music.timeSamples / (float)clip.frequency;

        while (nextTick < tickTimes.Count && tickTimes[nextTick] <= playhead)
        {
            if (hitSounds && tickClip != null) sfx.PlayOneShot(tickClip, 0.8f);
            nextTick++;
        }

        while (header.beatPhase + nextBeat * BeatPeriod <= playhead)
        {
            if (metronome)
            {
                bool bar = ((nextBeat % BeatGrid.BeatsPerBar) + BeatGrid.BeatsPerBar) % BeatGrid.BeatsPerBar == 0;
                sfx.PlayOneShot(bar ? accentClickClip : clickClip, 0.6f);
            }

            nextBeat++;
        }

        // Follow the playhead, paging rather than scrolling every frame, so
        // the notes stay still enough to read.
        float visible = timelineRect.width / pixelsPerSecond;
        if (playhead > viewStart + visible * 0.85f || playhead < viewStart)
        {
            viewStart = Mathf.Max(0f, playhead - visible * 0.15f);
        }

        Repaint();
    }

    private void EnsureAudio()
    {
        if (audioHost == null)
        {
            audioHost = EditorUtility.CreateGameObjectWithHideFlags("BeatmapTimelineAudio", HideFlags.HideAndDontSave,
                                                                   typeof(AudioSource), typeof(AudioSource));
            AudioSource[] sources = audioHost.GetComponents<AudioSource>();
            music = sources[0];
            sfx = sources[1];
            music.playOnAwake = false;
            sfx.playOnAwake = false;
        }

        if (tickClip == null)
        {
            // The game's own hit sound when it exists, so the preview sounds
            // like playing the chart.
            tickClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Source/Audio/hit_tap.wav");
            if (tickClip == null) tickClip = MakeClick("BeatmapTick", 2000f, 0.02f);
        }

        if (clickClip == null) clickClip = MakeClick("BeatmapClick", 1000f, 0.03f);
        if (accentClickClip == null) accentClickClip = MakeClick("BeatmapAccent", 1600f, 0.04f);
    }

    private static AudioClip MakeClick(string name, float frequency, float length)
    {
        const int rate = 44100;
        int count = Mathf.CeilToInt(length * rate);
        float[] data = new float[count];

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)rate;
            data[i] = 0.7f * Mathf.Sin(2f * Mathf.PI * frequency * t) * Mathf.Exp(-t / (length * 0.3f));
        }

        AudioClip made = AudioClip.Create(name, count, 1, rate, false);
        made.SetData(data, 0);
        made.hideFlags = HideFlags.HideAndDontSave;
        return made;
    }

    private void EnsurePeaks()
    {
        if (clip == peaksFor) return;

        peaksFor = clip;
        peaks = null;
        waveformMessage = null;

        if (clip == null) return;

        if (clip.loadType != AudioClipLoadType.DecompressOnLoad)
        {
            waveformMessage = "No waveform: the clip streams. Set its Load Type to Decompress On Load to see it.";
            return;
        }

        float[] samples = new float[clip.samples * clip.channels];
        if (!clip.GetData(samples, 0))
        {
            waveformMessage = "No waveform: could not read the clip's samples.";
            return;
        }

        int blocks = Mathf.CeilToInt(clip.length * WavePeaksPerSecond);
        int framesPerBlock = Mathf.Max(1, Mathf.CeilToInt(clip.frequency / WavePeaksPerSecond));
        peaks = new float[blocks];
        float max = 0.0001f;

        for (int b = 0; b < blocks; b++)
        {
            int from = b * framesPerBlock * clip.channels;
            int to = Mathf.Min(samples.Length, from + framesPerBlock * clip.channels);

            float peak = 0f;
            for (int i = from; i < to; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));

            peaks[b] = peak;
            max = Mathf.Max(max, peak);
        }

        for (int b = 0; b < blocks; b++) peaks[b] /= max;
    }

    // ---------------------------------------------------------------- helpers

    private float BeatPeriod
    {
        get { return 60f / Mathf.Max(1f, header != null ? header.bpm : 120f); }
    }

    private float SnapStep
    {
        get { return BeatPeriod / SnapValues[snapIndex]; }
    }

    private float SongLength
    {
        get
        {
            if (clip != null) return clip.length;
            return notes.Count > 0 ? End(notes[notes.Count - 1]) + 4f : 60f;
        }
    }

    private float Snap(float time)
    {
        float step = SnapStep;
        return header.beatPhase + Mathf.Round((time - header.beatPhase) / step) * step;
    }

    private float TimeToX(float time)
    {
        return timelineRect.x + (time - viewStart) * pixelsPerSecond;
    }

    private float XToTime(float x)
    {
        return viewStart + (x - timelineRect.x) / pixelsPerSecond;
    }

    private int LaneAt(Vector2 mouse)
    {
        if (laneRects[0].Contains(mouse)) return 0;
        if (laneRects[1].Contains(mouse)) return 1;
        return -1;
    }

    private BeatmapNoteJson NoteAt(Vector2 mouse)
    {
        int lane = LaneAt(mouse);
        if (lane < 0) return null;

        BeatmapNoteJson best = null;
        float bestDistance = NoteWidth;

        foreach (BeatmapNoteJson note in notes)
        {
            if (note.type != "Twin" && LaneIndex(note) != lane) continue;

            float x0 = TimeToX(Start(note));
            float x1 = TimeToX(End(note));

            float distance = mouse.x < x0 ? x0 - mouse.x : mouse.x > x1 ? mouse.x - x1 : 0f;
            if (distance <= bestDistance)
            {
                best = note;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static int LaneIndex(BeatmapNoteJson note)
    {
        return note.lane == "Right" ? 1 : 0;
    }

    private static float Start(BeatmapNoteJson note)
    {
        return note.type == "Hold" ? note.startTime : note.time;
    }

    private static float End(BeatmapNoteJson note)
    {
        return note.type == "Hold" ? note.endTime : note.time;
    }

    private static void SetStart(BeatmapNoteJson note, float start)
    {
        if (note.type == "Hold")
        {
            float length = note.endTime - note.startTime;
            note.startTime = start;
            note.endTime = start + length;
        }
        else
        {
            note.time = start;
        }
    }

    private void SortNotes()
    {
        notes.Sort((a, b) => Start(a).CompareTo(Start(b)));
    }
}
