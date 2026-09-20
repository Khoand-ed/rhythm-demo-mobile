using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// 暂停面板和开场 / Builds the pause button, the three-button pause panel and the big
// centred text the READY/GO run-in and the 3-2-1 resume countdown share.
//
// 按参考图做 / Laid out from the reference: hexagon buttons in a row on a dark plate,
// a PAUSE tab above it, pink to leave, amber to retry, green to resume with a ring
// around it. The shapes are generated into Assets/Source/Graphics/UI rather than
// borrowed from the Arknights folder - this is gameplay art and belongs on this side.
//
// 关掉点击开始 / It also switches GameManager.startOnFirstInput off, because the run-in
// now decides when the song starts. Rerunning is find-or-create, so anything moved or
// restyled by hand survives.
public class PauseMenuSetup
{
    private const string ArtDir = "Assets/Source/Graphics/UI";

    private const string PauseButtonName = "PauseButton";
    private const string PanelName = "PausePanel";
    private const string OverlayName = "IntroOverlay";

    // 参考图的配色 / Straight off the reference.
    private static readonly Color Plate = new Color(0.16f, 0.11f, 0.22f, 0.97f);
    private static readonly Color Scrim = new Color(0f, 0f, 0f, 0.55f);
    private static readonly Color Tab = new Color(0.45f, 0.25f, 0.66f, 1f);
    private static readonly Color TabInk = new Color(0.74f, 0.42f, 0.95f, 1f);
    private static readonly Color Quit = new Color(0.90f, 0.13f, 0.55f, 1f);
    private static readonly Color Retry = new Color(0.98f, 0.72f, 0.09f, 1f);
    private static readonly Color Resume = new Color(0.18f, 0.95f, 0.25f, 1f);
    private static readonly Color Ink = new Color(0.12f, 0.08f, 0.18f, 1f);

    // ---------------------------------------------------------------- entry points

    [MenuItem("Tools/Rhythm/Set Up Pause And Intro")]
    public static void SetUp()
    {
        if (Application.isPlaying)
        {
            Debug.LogError("[PauseMenuSetup] Exit Play Mode first.");
            return;
        }

        GameManager game = UnityEngine.Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        if (game == null)
        {
            Debug.LogError("[PauseMenuSetup] No GameManager in the open scene - open Main.unity first.");
            return;
        }

        Canvas canvas = game.scoreText != null ? game.scoreText.canvas : null;
        if (canvas == null) canvas = UnityEngine.Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
        if (canvas == null)
        {
            Debug.LogError("[PauseMenuSetup] No Canvas in the open scene to build on.");
            return;
        }

        canvas = canvas.rootCanvas;
        RectTransform root = (RectTransform)canvas.transform;

        Sprite hex = Shape("T_Hex", 256, p => NGon(p, 6, 0f, 0.98f));
        Sprite ring = Shape("T_HexRing", 256, p => NGon(p, 6, 0f, 0.98f) && !NGon(p, 6, 0f, 0.84f));
        Sprite play = Shape("T_IconPlay", 256, p => NGon(p - new Vector2(0.06f, 0f), 3, 0f, 0.72f));
        Sprite retry = Shape("T_IconRetry", 256, IconRetry);
        Sprite quit = Shape("T_IconQuit", 256, IconQuit);
        Sprite bars = Shape("T_IconPause", 256, IconPauseBars);

        // 九宫格: 只有中段拉伸 / Sliced, so the slanted ends hold their shape at any width.
        Sprite plate = Shape("T_Plate", 256, 128, new Vector4(56f, 0f, 56f, 0f), Slanted(0.44f));
        Sprite tab = Shape("T_Tab", 192, 64, new Vector4(34f, 0f, 34f, 0f), Slanted(0.36f));

        PauseMenu menu = game.GetComponent<PauseMenu>();
        if (menu == null) menu = Undo.AddComponent<PauseMenu>(game.gameObject);

        Undo.RecordObject(menu, "Set Up Pause And Intro");

        GameObject button = Find(root, PauseButtonName) ?? BuildPauseButton(root, bars);
        GameObject panel = Find(root, PanelName) ?? BuildPanel(root, hex, ring, play, retry, quit, plate, tab);
        GameObject overlay = Find(root, OverlayName) ?? BuildOverlay(root);

        menu.pauseButton = button;
        menu.panel = panel;
        menu.panelGroup = panel.GetComponent<CanvasGroup>();
        menu.quitButton = panel.transform.Find("Plate/Buttons/Quit").GetComponent<Button>();
        menu.retryButton = panel.transform.Find("Plate/Buttons/Retry").GetComponent<Button>();
        menu.resumeButton = panel.transform.Find("Plate/Buttons/Resume").GetComponent<Button>();
        menu.overlay = overlay;
        menu.overlayGroup = overlay.GetComponent<CanvasGroup>();
        menu.overlayText = overlay.transform.Find("Text").GetComponent<TextMeshProUGUI>();

        EditorUtility.SetDirty(menu);

        // 事件写成持久监听 / Persistent listeners, so the wiring shows up in each Button's
        // OnClick list in the Inspector instead of being invisible inside Awake.
        Wire(button.GetComponent<Button>(), menu.Pause);
        Wire(menu.quitButton, menu.Quit);
        Wire(menu.retryButton, menu.Retry);
        Wire(menu.resumeButton, menu.Resume);

        // 开场接管了开始时机 / The run-in owns the start now, so the old tap gate has to go
        // or the song would begin the moment anything is pressed during READY.
        if (game.startOnFirstInput)
        {
            Undo.RecordObject(game, "Set Up Pause And Intro");
            game.startOnFirstInput = false;
            EditorUtility.SetDirty(game);
        }

        EnsureEventSystem();

        EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
        Selection.activeObject = panel;

        Debug.Log($"[PauseMenuSetup] Pause and intro set up on '{canvas.name}'.\n" +
                  $"  {PauseButtonName} - top right, hidden until the song starts\n" +
                  $"  {PanelName} - quit / retry / resume\n" +
                  $"  {OverlayName} - READY, GO! and the 3-2-1 countdown\n" +
                  $"  Shapes generated into {ArtDir}\n" +
                  "  GameManager.startOnFirstInput is now off - the run-in starts the song.");
    }

    /// <summary>
    /// 删掉重建 / Deletes the three objects and the component, then builds them again.
    ///
    /// 和 Set Up 配对 / The explicit reset that pairs with the find-or-create above. Anything
    /// moved or restyled by hand is lost, which is the point.
    /// </summary>
    [MenuItem("Tools/Rhythm/Reset Pause And Intro To Default")]
    public static void Reset()
    {
        if (Application.isPlaying)
        {
            Debug.LogError("[PauseMenuSetup] Exit Play Mode first.");
            return;
        }

        GameManager game = UnityEngine.Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        if (game == null)
        {
            Debug.LogError("[PauseMenuSetup] No GameManager in the open scene.");
            return;
        }

        Canvas canvas = UnityEngine.Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
        if (canvas != null)
        {
            foreach (string name in new[] { PauseButtonName, PanelName, OverlayName })
            {
                GameObject existing = Find((RectTransform)canvas.rootCanvas.transform, name);
                if (existing != null) Undo.DestroyObjectImmediate(existing);
            }
        }

        PauseMenu menu = game.GetComponent<PauseMenu>();
        if (menu != null) Undo.DestroyObjectImmediate(menu);

        SetUp();
    }

    // -------------------------------------------------------------- pause button

    private static GameObject BuildPauseButton(RectTransform parent, Sprite bars)
    {
        Image face = NewImage(PauseButtonName, parent, new Color(1f, 1f, 1f, 0.001f));
        RectTransform rect = (RectTransform)face.transform;
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-36f, -28f);
        rect.sizeDelta = new Vector2(110f, 110f);

        // 透明也能接收点击 / A nearly-transparent Image is still a raycast target, which is what
        // gives the icon a comfortable tap area without drawing a box around it.
        face.raycastTarget = true;

        Button button = face.gameObject.AddComponent<Button>();
        button.targetGraphic = face;

        Image icon = NewImage("Icon", rect, new Color(1f, 1f, 1f, 0.92f));
        RectTransform iconRect = (RectTransform)icon.transform;
        iconRect.anchorMin = new Vector2(0.5f, 0.5f);
        iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = Vector2.zero;
        iconRect.sizeDelta = new Vector2(56f, 56f);
        icon.sprite = bars;
        icon.raycastTarget = false;

        return face.gameObject;
    }

    // ---------------------------------------------------------------------- panel

    private static GameObject BuildPanel(RectTransform parent, Sprite hex, Sprite ring,
                                         Sprite play, Sprite retry, Sprite quit,
                                         Sprite plateShape, Sprite tabShape)
    {
        Image scrim = NewImage(PanelName, parent, Scrim);
        RectTransform root = (RectTransform)scrim.transform;
        Stretch(root);

        // 压住底下的输入 / The scrim is a raycast target on purpose: it swallows taps aimed at
        // the note zones underneath while the panel is up.
        scrim.raycastTarget = true;
        root.gameObject.AddComponent<CanvasGroup>();

        Image plate = NewImage("Plate", root, Plate);
        RectTransform plateRect = (RectTransform)plate.transform;
        plateRect.anchorMin = new Vector2(0.5f, 0.5f);
        plateRect.anchorMax = new Vector2(0.5f, 0.5f);
        plateRect.pivot = new Vector2(0.5f, 0.5f);
        plateRect.anchoredPosition = Vector2.zero;
        plateRect.sizeDelta = new Vector2(880f, 330f);
        plate.sprite = plateShape;
        plate.type = Image.Type.Sliced;

        // PAUSE 的小标签 / The tab sits astride the plate's top edge, as in the reference.
        Image tab = NewImage("Tab", plateRect, Tab);
        RectTransform tabRect = (RectTransform)tab.transform;
        tabRect.anchorMin = new Vector2(0.5f, 1f);
        tabRect.anchorMax = new Vector2(0.5f, 1f);
        tabRect.pivot = new Vector2(0.5f, 0.5f);
        tabRect.anchoredPosition = Vector2.zero;
        tabRect.sizeDelta = new Vector2(300f, 64f);
        tab.sprite = tabShape;
        tab.type = Image.Type.Sliced;
        tab.raycastTarget = false;

        TextMeshProUGUI tabText = NewText("Text", tabRect, 38f, TabInk, TextAlignmentOptions.Center);
        Stretch((RectTransform)tabText.transform);
        tabText.fontStyle = FontStyles.Bold;
        tabText.text = "PAUSE";

        RectTransform row = NewRect("Buttons", plateRect);
        row.anchorMin = new Vector2(0.5f, 0.5f);
        row.anchorMax = new Vector2(0.5f, 0.5f);
        row.pivot = new Vector2(0.5f, 0.5f);
        row.anchoredPosition = new Vector2(0f, -12f);
        row.sizeDelta = new Vector2(680f, 210f);

        HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 36f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        // 顺序按参考图 / Left to right as in the reference: leave, retry, resume.
        HexButton(row, "Quit", hex, quit, Quit, null);
        HexButton(row, "Retry", hex, retry, Retry, null);
        HexButton(row, "Resume", hex, play, Resume, ring);

        root.gameObject.SetActive(false);
        return root.gameObject;
    }

    /// <summary>
    /// 一个六边形按钮 / One hexagon button: the tinted hexagon, a dark glyph on it, and for the
    /// resume button an outline ring, which is how the reference marks the default action.
    /// </summary>
    private static void HexButton(RectTransform parent, string name, Sprite hex, Sprite icon,
                                  Color tint, Sprite ringSprite)
    {
        Image face = NewImage(name, parent, tint);
        RectTransform rect = (RectTransform)face.transform;
        rect.sizeDelta = new Vector2(200f, 200f);
        face.sprite = hex;
        face.preserveAspect = true;

        Button button = face.gameObject.AddComponent<Button>();
        button.targetGraphic = face;

        if (ringSprite != null)
        {
            Image outline = NewImage("Ring", rect, new Color(1f, 1f, 1f, 0.95f));
            RectTransform outlineRect = (RectTransform)outline.transform;
            Stretch(outlineRect);
            outlineRect.offsetMin = new Vector2(-10f, -10f);
            outlineRect.offsetMax = new Vector2(10f, 10f);
            outline.sprite = ringSprite;
            outline.preserveAspect = true;
            outline.raycastTarget = false;
        }

        Image glyph = NewImage("Icon", rect, Ink);
        RectTransform glyphRect = (RectTransform)glyph.transform;
        Stretch(glyphRect);
        glyphRect.offsetMin = new Vector2(52f, 52f);
        glyphRect.offsetMax = new Vector2(-52f, -52f);
        glyph.sprite = icon;
        glyph.preserveAspect = true;
        glyph.raycastTarget = false;
    }

    // -------------------------------------------------------------------- overlay

    private static GameObject BuildOverlay(RectTransform parent)
    {
        RectTransform root = NewRect(OverlayName, parent);
        Stretch(root);
        root.gameObject.AddComponent<CanvasGroup>();

        TextMeshProUGUI text = NewText("Text", root, 220f, Color.white, TextAlignmentOptions.Center);
        RectTransform textRect = (RectTransform)text.transform;
        textRect.anchorMin = new Vector2(0.5f, 0.5f);
        textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = new Vector2(1200f, 400f);
        text.fontStyle = FontStyles.Bold;

        // 描边和阴影 / An outline and a drop shadow, because this sits straight on top of the
        // playfield with nothing behind it - white on a bright chart would vanish otherwise.
        text.fontMaterial.EnableKeyword("OUTLINE_ON");
        text.outlineWidth = 0.25f;
        text.outlineColor = new Color32(30, 16, 46, 255);

        root.gameObject.SetActive(false);
        return root.gameObject;
    }

    // ------------------------------------------------------------- shape drawing

    /// <summary>
    /// 生成一张白色形状图 / Writes a white alpha-shaped sprite, supersampled for smooth edges.
    ///
    /// 全部涂白, 颜色交给 Image.color / Everything is drawn white and tinted by Image.color at
    /// use, so one hexagon serves all three buttons.
    /// </summary>
    private static Sprite Shape(string name, int size, Func<Vector2, bool> inside)
    {
        return Shape(name, size, size, Vector4.zero, inside);
    }

    /// <summary>
    /// 生成一张白色形状图 / Writes a white alpha-shaped sprite, supersampled for smooth edges.
    ///
    /// 全部涂白, 颜色交给 Image.color / Everything is drawn white and tinted by Image.color at
    /// use, so one hexagon serves all three buttons.
    ///
    /// border 非零就是九宫格 / A non-zero border makes it a sliced sprite: the plate and the tab
    /// keep their slanted ends at any width while only the middle stretches.
    /// </summary>
    private static Sprite Shape(string name, int width, int height, Vector4 border,
                                Func<Vector2, bool> inside)
    {
        string path = $"{ArtDir}/{name}.png";
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null) return existing;

        const int ss = 3;
        float halfW = width * 0.5f;
        float halfH = height * 0.5f;
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int hits = 0;

                for (int sy = 0; sy < ss; sy++)
                {
                    for (int sx = 0; sx < ss; sx++)
                    {
                        Vector2 p = new Vector2((x + (sx + 0.5f) / ss - halfW) / halfW,
                                                (y + (sy + 0.5f) / ss - halfH) / halfH);
                        if (inside(p)) hits++;
                    }
                }

                pixels[y * width + x] = new Color(1f, 1f, 1f, hits / (float)(ss * ss));
            }
        }

        return Import(path, pixels, width, height, border);
    }

    /// <summary>
    /// 两端斜切的长条 / A bar with both ends cut back at an angle, as the reference's plate and
    /// tab both are. The cut is widest at the top and bottom edges and closes to nothing at
    /// mid-height, which is what gives the flattened-hexagon silhouette.
    /// </summary>
    private static Func<Vector2, bool> Slanted(float slant)
    {
        return p => (p.x + 1f) > slant * Mathf.Abs(p.y)
                 && (1f - p.x) > slant * Mathf.Abs(p.y);
    }

    /// <summary>正n边形 / Point-in-regular-polygon, folded into one sector.</summary>
    private static bool NGon(Vector2 p, int sides, float rotation, float radius)
    {
        float sector = Mathf.PI * 2f / sides;
        float angle = Mathf.Atan2(p.y, p.x) - rotation;
        float folded = Mathf.Repeat(angle, sector) - sector * 0.5f;
        float edge = radius * Mathf.Cos(sector * 0.5f) / Mathf.Cos(folded);

        return p.magnitude <= edge;
    }

    /// <summary>回旋箭头 / A ring with a bite out of it and an arrowhead at the bite.</summary>
    private static bool IconRetry(Vector2 p)
    {
        float r = p.magnitude;
        float a = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;

        bool ring = r >= 0.46f && r <= 0.76f;
        bool gap = a > 24f && a < 104f;

        // Sitting on the ring where the gap opens, turned to follow it round.
        Vector2 head = new Vector2(Mathf.Cos(24f * Mathf.Deg2Rad), Mathf.Sin(24f * Mathf.Deg2Rad)) * 0.61f;
        bool arrow = NGon(p - head, 3, -50f * Mathf.Deg2Rad, 0.30f);

        return (ring && !gap) || arrow;
    }

    /// <summary>出口箭头 / An arrow leaving a bracket - the reference's exit glyph.</summary>
    private static bool IconQuit(Vector2 p)
    {
        bool spine = p.x > 0.40f && p.x < 0.62f && Mathf.Abs(p.y) < 0.74f;
        bool arms = Mathf.Abs(Mathf.Abs(p.y) - 0.64f) < 0.10f && p.x > 0.04f && p.x < 0.62f;

        bool shaft = Mathf.Abs(p.y) < 0.12f && p.x > -0.34f && p.x < 0.34f;
        bool head = NGon(p - new Vector2(-0.48f, 0f), 3, Mathf.PI, 0.32f);

        return spine || arms || shaft || head;
    }

    /// <summary>两条竖条 / The two bars on the pause button.</summary>
    private static bool IconPauseBars(Vector2 p)
    {
        if (Mathf.Abs(p.y) > 0.62f) return false;

        return (p.x > -0.50f && p.x < -0.14f) || (p.x > 0.14f && p.x < 0.50f);
    }

    private static Sprite Import(string path, Color[] pixels, int width, int height, Vector4 border)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.SetPixels(pixels);
        tex.Apply();

        Directory.CreateDirectory(ArtDir);
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), path),
                           tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.spriteBorder = border;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // -------------------------------------------------------------------- helpers

    private static void Wire(Button button, UnityEngine.Events.UnityAction call)
    {
        if (button == null) return;

        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
        {
            if (button.onClick.GetPersistentTarget(i) == call.Target as UnityEngine.Object &&
                button.onClick.GetPersistentMethodName(i) == call.Method.Name)
            {
                return;
            }
        }

        Undo.RecordObject(button, "Set Up Pause And Intro");
        UnityEventTools.AddPersistentListener(button.onClick, call);
        EditorUtility.SetDirty(button);
    }

    /// <summary>
    /// 没有 EventSystem 按钮就是死的 / Without one, every Button in the scene exists visually
    /// and silently does nothing when pressed.
    /// </summary>
    private static void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>(
                FindObjectsInactive.Include) != null)
        {
            return;
        }

        GameObject go = new GameObject("EventSystem",
            typeof(UnityEngine.EventSystems.EventSystem),
            typeof(UnityEngine.EventSystems.StandaloneInputModule));

        Undo.RegisterCreatedObjectUndo(go, "Set Up Pause And Intro");
        Debug.LogWarning("[PauseMenuSetup] The scene had no EventSystem; one was added, or no " +
                         "button in this scene would respond.");
    }

    private static GameObject Find(RectTransform root, string name)
    {
        Transform found = root.Find(name);
        return found != null ? found.gameObject : null;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static Image NewImage(string name, Transform parent, Color color)
    {
        Image image = NewRect(name, parent).gameObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static TextMeshProUGUI NewText(string name, Transform parent, float size,
                                           Color color, TextAlignmentOptions alignment)
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
}
