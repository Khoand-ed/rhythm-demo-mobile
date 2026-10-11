using System.Collections.Generic;
using System.IO;
using System.Text;
using Data.Story;
using TMPro;
using UI.Sub;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 剧情 / The story tools:
///   - Build Story UI: the StoryUI prefab the dialogue plays on, and placeholder backgrounds,
///   - Assign Missing Line Ids: writes a #id after every line that has none, so it can be corrected
///     from the admin page once it has shipped,
///   - Story Preview: every script with what is wrong with it, and a button to play it in Play Mode.
///
/// 找到就不动 / Find-or-create: a prefab that exists is kept, a background that exists is never
/// redrawn. Rebuild Story UI deletes the prefab and builds it again.
///
/// The scripts are plain text under Resources/Story - see <see cref="StoryScript"/> for the format.
/// </summary>
public static class StorySetup
{
    public const string StoryDir = "Assets/Arknights/Resources/Story";
    public const string BackgroundDir = StoryDir + "/Backgrounds";
    private const string UiPrefabPath = "Assets/Arknights/Resources/Prefab/UI/" + StoryUI.UIName + ".prefab";

    private const int ArtWidth = 960;
    private const int ArtHeight = 540;

    // 占位背景 / Plain gradients anyone may commit. Replace the PNG with real art of the same name
    // and every script that names it picks it up.
    private static readonly (string name, Color from, Color to)[] Placeholders =
    {
        ("rhodes_deck", new Color(0.05f, 0.08f, 0.12f), new Color(0.30f, 0.42f, 0.55f)),
        ("festival_square", new Color(0.10f, 0.05f, 0.10f), new Color(0.78f, 0.45f, 0.30f)),
        ("night_rooftop", new Color(0.02f, 0.03f, 0.07f), new Color(0.20f, 0.18f, 0.40f)),
    };

    private static readonly Color Ink = new Color(0.03f, 0.035f, 0.045f, 1f);
    private static readonly Color PanelInk = new Color(0.08f, 0.09f, 0.11f, 1f);
    private static readonly Color BoxInk = new Color(0.04f, 0.05f, 0.07f, 0.90f);
    private static readonly Color Strip = new Color(0.10f, 0.11f, 0.12f, 0.92f);
    private static readonly Color Slab = new Color(0.30f, 0.31f, 0.33f, 0.90f);
    private static readonly Color Edge = new Color(1f, 1f, 1f, 0.30f);
    private static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);
    private static readonly Color Gold = new Color(0.98f, 0.78f, 0.25f, 1f);

    // ------------------------------------------------------------------ menu items

    [MenuItem("Arknights/Story/Build Story UI", false, 1)]
    public static void Build()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        List<string> done = new List<string>();
        EnsureFolder(BackgroundDir);

        int made = 0;
        foreach ((string name, Color from, Color to) in Placeholders)
        {
            string path = $"{BackgroundDir}/{name}.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null) continue;
            ImportSprite(path, Gradient(from, to));
            made++;
        }
        done.Add($"backgrounds: {made} placeholder(s) made, {Placeholders.Length - made} already there");

        if (AssetDatabase.LoadAssetAtPath<GameObject>(UiPrefabPath) != null)
        {
            done.Add($"{StoryUI.UIName}: kept");
        }
        else
        {
            GameObject root = BuildStoryScreen();
            PrefabUtility.SaveAsPrefabAsset(root, UiPrefabPath);
            Object.DestroyImmediate(root);
            done.Add($"{StoryUI.UIName}: built at {UiPrefabPath}");
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[StorySetup] " + string.Join("\n  ", done));
    }

    [MenuItem("Arknights/Story/Rebuild Story UI", false, 20)]
    public static void Rebuild()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        AssetDatabase.DeleteAsset(UiPrefabPath);
        Build();
    }

    /// <summary>
    /// 补行号 / Writes an id after every line that has none. A line that has one keeps it - the id is
    /// what the admin page corrects the line by, so it must never change once the story has shipped.
    /// </summary>
    [MenuItem("Arknights/Story/Assign Missing Line Ids", false, 40)]
    public static void AssignMissingLineIds()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        int files = 0, lines = 0;
        foreach (string path in ScriptPaths())
        {
            int added = AssignIds(path);
            if (added == 0) continue;

            files++;
            lines += added;
            AssetDatabase.ImportAsset(path);
        }

        Debug.Log($"[StorySetup] Line ids assigned: {lines} line(s) in {files} script(s).");
    }

    [MenuItem("Arknights/Story/Story Preview...", false, 41)]
    public static void OpenPreview() => EditorWindow.GetWindow<StoryPreviewWindow>("Story Preview");

    public static List<string> ScriptPaths()
    {
        List<string> paths = new List<string>();
        if (!Directory.Exists(StoryDir)) return paths;

        foreach (string file in Directory.GetFiles(StoryDir, "*.story", SearchOption.AllDirectories))
        {
            paths.Add(file.Replace('\\', '/'));
        }
        paths.Sort();
        return paths;
    }

    private static int AssignIds(string path)
    {
        string source = File.ReadAllText(path);
        string newline = source.Contains("\r\n") ? "\r\n" : "\n";
        string[] rows = source.Replace("\r\n", "\n").Split('\n');

        HashSet<string> taken = new HashSet<string>();
        foreach (string row in rows)
        {
            string line = row.Trim();
            string id = StoryScript.SplitLineId(ref line, out _);
            if (id != null) taken.Add(id);
        }

        int added = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            string line = rows[i].Trim();
            if (line.Length == 0 || line.StartsWith("//") || line[0] == '@') continue;
            if (line[0] != '>' && line.IndexOf(':') <= 0) continue;

            string probe = line;
            if (StoryScript.SplitLineId(ref probe, out string bad) != null || bad != null) continue;

            string id;
            do { id = System.Guid.NewGuid().ToString("N").Substring(0, 4); } while (!taken.Add(id));

            rows[i] = rows[i].TrimEnd() + "   #" + id;
            added++;
        }

        if (added > 0) File.WriteAllText(path, string.Join(newline, rows), new UTF8Encoding(false));
        return added;
    }

    // ------------------------------------------------------------------ the screen

    private static GameObject BuildStoryScreen()
    {
        GameObject root = new GameObject(StoryUI.UIName, typeof(RectTransform), typeof(CanvasGroup), typeof(StoryUI));
        StoryUI ui = root.GetComponent<StoryUI>();
        RectTransform rect = (RectTransform)root.transform;
        Stretch(rect);

        Stretch(NewImage("Backdrop", rect, Ink, true).rectTransform);

        // ---- the scene: background covering the screen, the speaker in front of it
        ui.background = NewImage("Background", rect, Color.white, false);
        RectTransform bg = ui.background.rectTransform;
        bg.anchorMin = bg.anchorMax = bg.pivot = new Vector2(0.5f, 0.5f);
        ui.backgroundFit = ui.background.gameObject.AddComponent<AspectRatioFitter>();
        ui.backgroundFit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        ui.backgroundFit.aspectRatio = 16f / 9f;
        ui.background.enabled = false;

        RectTransform portraitRoot = NewRect("Portrait", rect);
        Place(portraitRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-420f, 0f), new Vector2(900f, 1000f));
        ui.portraitGroup = portraitRoot.gameObject.AddComponent<CanvasGroup>();
        ui.portraitGroup.blocksRaycasts = false;
        ui.portrait = portraitRoot.gameObject.AddComponent<UnityEngine.UI.Image>();
        ui.portrait.preserveAspect = true;
        ui.portrait.raycastTarget = false;
        ui.portrait.enabled = false;

        // ---- the dialogue box along the bottom
        UnityEngine.UI.Image box = NewImage("DialogueBox", rect, BoxInk, false);
        RectTransform boxRect = box.rectTransform;
        boxRect.anchorMin = new Vector2(0f, 0f);
        boxRect.anchorMax = new Vector2(1f, 0f);
        boxRect.pivot = new Vector2(0.5f, 0f);
        boxRect.sizeDelta = new Vector2(0f, 300f);
        boxRect.anchoredPosition = Vector2.zero;
        UnityEngine.UI.Image line = NewImage("EdgeTop", boxRect, Edge, false);
        Place(line.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 2f));

        UnityEngine.UI.Image plate = NewImage("Nameplate", boxRect, Strip, false);
        Place(plate.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(120f, -6f), new Vector2(420f, 60f));
        UnityEngine.UI.Image bar = NewImage("Bar", plate.rectTransform, Gold, false);
        Place(bar.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(8f, 0f));
        ui.nameplate = plate.gameObject;
        ui.speakerText = NewText("Speaker", plate.rectTransform, 32f, Gold, TextAlignmentOptions.Left);
        ui.speakerText.rectTransform.anchorMin = Vector2.zero;
        ui.speakerText.rectTransform.anchorMax = Vector2.one;
        ui.speakerText.rectTransform.offsetMin = new Vector2(28f, 0f);
        ui.speakerText.rectTransform.offsetMax = new Vector2(-12f, 0f);
        ui.speakerText.fontStyle = FontStyles.Bold;

        ui.bodyText = NewText("Body", boxRect, 34f, Color.white, TextAlignmentOptions.TopLeft);
        ui.bodyText.rectTransform.anchorMin = Vector2.zero;
        ui.bodyText.rectTransform.anchorMax = Vector2.one;
        ui.bodyText.rectTransform.offsetMin = new Vector2(150f, 48f);
        ui.bodyText.rectTransform.offsetMax = new Vector2(-150f, -48f);
        ui.bodyText.lineSpacing = 12f;

        UnityEngine.UI.Image mark = NewImage("NextMark", boxRect, Gold, false);
        Place(mark.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(-96f, 52f), new Vector2(20f, 20f));
        mark.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        ui.nextMark = mark;

        // ---- a tap anywhere moves on; the buttons sit above it
        UnityEngine.UI.Image tap = NewImage("TapArea", rect, new Color(0f, 0f, 0f, 0f), true);
        Stretch(tap.rectTransform);
        ui.tapArea = tap.gameObject.AddComponent<Button>();
        ui.tapArea.transition = Selectable.Transition.None;

        ui.skipButton = TextButton("SkipButton", rect, new Vector2(-48f, -40f), new Vector2(200f, 64f), "SKIP  ›");
        ui.logButton = TextButton("LogButton", rect, new Vector2(-48f - 200f - 16f, -40f), new Vector2(200f, 64f), "LOG");

        // ---- the title card, over everything but taking no taps (a tap skips it via TapArea)
        RectTransform card = NewRect("TitleCard", rect);
        Stretch(card);
        ui.titleCard = card.gameObject.AddComponent<CanvasGroup>();
        ui.titleCard.alpha = 0f;
        ui.titleCard.blocksRaycasts = false;
        ui.titleCard.interactable = false;
        UnityEngine.UI.Image band = NewImage("Band", card, new Color(0.02f, 0.02f, 0.03f, 0.92f), false);
        Place(band.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000f, 200f));
        ui.titleText = NewText("Title", card, 56f, Color.white, TextAlignmentOptions.Center);
        Place(ui.titleText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1600f, 120f));
        ui.titleText.fontStyle = FontStyles.Bold;
        ui.titleText.characterSpacing = 6f;

        BuildLog(rect, ui);
        return root;
    }

    private static void BuildLog(RectTransform rect, StoryUI ui)
    {
        UnityEngine.UI.Image panel = NewImage("LogPanel", rect, PanelInk, true);
        Stretch(panel.rectTransform);
        ui.logPanel = panel.gameObject;

        TextMeshProUGUI heading = NewText("Heading", panel.rectTransform, 30f, Color.white, TextAlignmentOptions.Left);
        Place(heading.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(150f, -40f), new Vector2(400f, 64f));
        heading.fontStyle = FontStyles.Bold;
        heading.characterSpacing = 3f;
        heading.text = "LOG";

        ui.logClose = TextButton("CloseButton", panel.rectTransform, new Vector2(-48f, -40f), new Vector2(200f, 64f), "CLOSE");

        RectTransform view = NewRect("Scroll", panel.rectTransform);
        view.anchorMin = Vector2.zero;
        view.anchorMax = Vector2.one;
        view.offsetMin = new Vector2(150f, 40f);
        view.offsetMax = new Vector2(-150f, -140f);
        view.gameObject.AddComponent<RectMask2D>();
        UnityEngine.UI.Image hit = NewImage("Hit", view, new Color(0f, 0f, 0f, 0f), true);
        Stretch(hit.rectTransform);

        RectTransform content = NewRect("Content", view);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        VerticalLayoutGroup list = content.gameObject.AddComponent<VerticalLayoutGroup>();
        list.spacing = 24f;
        list.childControlWidth = list.childControlHeight = true;
        list.childForceExpandWidth = true;
        list.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ui.logScroll = view.gameObject.AddComponent<ScrollRect>();
        ui.logScroll.viewport = view;
        ui.logScroll.content = content;
        ui.logScroll.horizontal = false;
        ui.logScroll.movementType = ScrollRect.MovementType.Clamped;
        ui.logContent = content;

        // 一条记录 / One entry: who, then what - the layout group sizes it to the text.
        RectTransform entry = NewRect("LogTemplate", content);
        VerticalLayoutGroup stack = entry.gameObject.AddComponent<VerticalLayoutGroup>();
        stack.spacing = 4f;
        stack.childControlWidth = stack.childControlHeight = true;
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = false;
        TextMeshProUGUI who = NewText("Speaker", entry, 26f, Gold, TextAlignmentOptions.Left);
        who.fontStyle = FontStyles.Bold;
        NewText("Text", entry, 30f, Color.white, TextAlignmentOptions.TopLeft);
        ui.logTemplate = entry;

        panel.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ backgrounds

    private static Color[] Gradient(Color from, Color to)
    {
        Color[] pixels = new Color[ArtWidth * ArtHeight];
        for (int y = 0; y < ArtHeight; y++)
        {
            for (int x = 0; x < ArtWidth; x++)
            {
                float u = x / (float)(ArtWidth - 1);
                float v = y / (float)(ArtHeight - 1);

                // 地平线 / A horizon a third of the way up, brightest just above it.
                float sky = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((v - 0.25f) / 0.75f));
                Color c = Color.Lerp(to, from, sky);
                float glow = Mathf.Clamp01(1f - Mathf.Abs(v - 0.34f) / 0.12f) * 0.18f;
                c += new Color(glow, glow * 0.9f, glow * 0.8f, 0f);
                if (v < 0.30f) c *= 0.55f;

                float dx = u - 0.5f;
                float dy = v - 0.5f;
                c *= 1f - Mathf.Clamp01((dx * dx + dy * dy) * 1.2f);

                c.a = 1f;
                pixels[y * ArtWidth + x] = c;
            }
        }
        return pixels;
    }

    private static void ImportSprite(string assetPath, Color[] pixels)
    {
        Texture2D texture = new Texture2D(ArtWidth, ArtHeight, TextureFormat.RGBA32, false);
        texture.SetPixels(pixels);
        texture.Apply();
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath), texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    // ------------------------------------------------------------------ helpers

    private static Button TextButton(string name, RectTransform parent, Vector2 position, Vector2 size, string label)
    {
        UnityEngine.UI.Image face = NewImage(name, parent, Slab, true);
        Place(face.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), position, size);
        UnityEngine.UI.Image edge = NewImage("Edge", face.rectTransform, Edge, false);
        Place(edge.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 2f));

        TextMeshProUGUI text = NewText("Label", face.rectTransform, 24f, Color.white, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        text.fontStyle = FontStyles.Bold;
        text.characterSpacing = 3f;
        text.text = label;

        Button button = face.gameObject.AddComponent<Button>();
        button.targetGraphic = face;
        return button;
    }

    private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static UnityEngine.UI.Image NewImage(string name, Transform parent, Color color, bool raycast)
    {
        UnityEngine.UI.Image image = NewRect(name, parent).gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = raycast;
        return image;
    }

    private static TextMeshProUGUI NewText(string name, Transform parent, float size, Color color, TextAlignmentOptions alignment)
    {
        TextMeshProUGUI text = NewRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.text = "";
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        return text;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}

/// <summary>
/// Every story script, what is wrong with each, and its lines as the game will read them. In Play
/// Mode, PLAY runs the selected script on the real StoryUI.
/// </summary>
public class StoryPreviewWindow : EditorWindow
{
    private List<string> paths = new List<string>();
    private int selected = -1;
    private StoryScript script;
    private Vector2 listScroll, lineScroll;

    private void OnEnable() => Refresh();

    private void OnFocus() => Refresh();

    private void Refresh()
    {
        string current = selected >= 0 && selected < paths.Count ? paths[selected] : null;
        paths = StorySetup.ScriptPaths();
        selected = current != null ? paths.IndexOf(current) : (paths.Count > 0 ? 0 : -1);
        Load();
    }

    private void Load()
    {
        script = selected >= 0 && selected < paths.Count
            ? StoryScript.Parse(Path.GetFileNameWithoutExtension(paths[selected]), File.ReadAllText(paths[selected]))
            : null;
    }

    private void OnGUI()
    {
        EditorGUILayout.BeginHorizontal();

        EditorGUILayout.BeginVertical(GUILayout.Width(260f));
        EditorGUILayout.LabelField($"{paths.Count} script(s) in {StorySetup.StoryDir}", EditorStyles.miniLabel);
        listScroll = EditorGUILayout.BeginScrollView(listScroll);
        for (int i = 0; i < paths.Count; i++)
        {
            string id = Path.GetFileNameWithoutExtension(paths[i]);
            if (GUILayout.Toggle(i == selected, id, EditorStyles.toolbarButton) && i != selected)
            {
                selected = i;
                Load();
            }
        }
        EditorGUILayout.EndScrollView();
        if (GUILayout.Button("Assign Missing Line Ids")) { StorySetup.AssignMissingLineIds(); Load(); }
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical();
        if (script == null)
        {
            EditorGUILayout.HelpBox("No script selected. Add a .story file under " + StorySetup.StoryDir + ".", MessageType.Info);
        }
        else
        {
            DrawScript();
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }

    private void DrawScript()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(script.Id, EditorStyles.boldLabel);
        if (GUILayout.Button("Open", GUILayout.Width(70f)))
        {
            AssetDatabase.OpenAsset(AssetDatabase.LoadMainAssetAtPath(paths[selected]));
        }
        using (new EditorGUI.DisabledScope(!Application.isPlaying || script.Errors.Count > 0))
        {
            if (GUILayout.Button(new GUIContent("Play", "Plays it on the real StoryUI - Play Mode only."), GUILayout.Width(70f)))
            {
                StoryUI.Play(script.Id, () => Debug.Log($"[StoryPreviewWindow] '{script.Id}' finished."));
            }
        }
        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(script.Title)) EditorGUILayout.LabelField("Title card", script.Title);

        foreach (string error in script.Errors) EditorGUILayout.HelpBox("Line " + error, MessageType.Error);
        foreach (string warning in script.Warnings) EditorGUILayout.HelpBox("Line " + warning, MessageType.Warning);

        lineScroll = EditorGUILayout.BeginScrollView(lineScroll);
        string background = null;
        foreach (StoryLine line in script.Lines)
        {
            if (line.Background != background)
            {
                background = line.Background;
                bool found = StoryManagerProbe(background);
                EditorGUILayout.LabelField("background: " + (background ?? "none") + (found || background == null ? "" : "   (MISSING)"),
                    EditorStyles.miniBoldLabel);
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(line.LineId != null ? "#" + line.LineId : "(no id)", EditorStyles.miniLabel, GUILayout.Width(60f));
            EditorGUILayout.LabelField(line.Kind == StoryLineKind.Narration ? "-" : line.Speaker, EditorStyles.boldLabel, GUILayout.Width(110f));
            EditorGUILayout.LabelField(line.Text, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }

    private static bool StoryManagerProbe(string background) =>
        background != null && AssetDatabase.LoadAssetAtPath<Sprite>($"{StorySetup.BackgroundDir}/{background}.png") != null;
}
