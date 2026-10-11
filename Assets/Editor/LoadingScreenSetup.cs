using System.Collections.Generic;
using System.IO;
using TMPro;
using UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 加载画面 / Builds the LoadingScreen prefab and seeds its background folder.
//
// LoadingScreen.cs binds by field, and the fields are filled here: a Screen Space - Overlay canvas
// on top of everything, a dark backdrop that also swallows clicks, the random background cropped
// to cover, a shade along the bottom so the type reads on any art, the rippling LOADING, a thin
// bar and its percentage.
//
// 找到就不动 / Find-or-create throughout: a node that already exists is left exactly where and how
// it is, and only missing pieces are added and wired. Reset Loading Screen To Default rebuilds the
// layout from scratch.
//
// 背景图不归这里管 / The backgrounds are the player-facing art, not layout, so neither command ever
// replaces or deletes one. Placeholders are generated only into an empty folder - once real art is
// in there, re-running this adds nothing beside it.
public static class LoadingScreenSetup
{
    private const string PrefabPath = "Assets/Arknights/Resources/Prefab/UI/" + LoadingScreen.PrefabName + ".prefab";
    private const string BackgroundDir = "Assets/Arknights/Resources/" + LoadingScreen.BackgroundFolder;
    private const string ShadePath = "Assets/Arknights/Resources/UI/T_LoadingShade.png";

    private const int ArtWidth = 640;
    private const int ArtHeight = 360;

    // 生成的占位图: 两色斜向渐变 + 几道柔光 / Generated stand-ins: a diagonal two-colour gradient
    // with a few soft light bands. Drawn here, so there is nobody's art to worry about.
    private static readonly (string name, Color from, Color to)[] Placeholders =
    {
        ("placeholder_01_tide", new Color(0.04f, 0.08f, 0.18f), new Color(0.10f, 0.47f, 0.60f)),
        ("placeholder_02_dusk", new Color(0.15f, 0.05f, 0.14f), new Color(0.86f, 0.38f, 0.18f)),
        ("placeholder_03_steel", new Color(0.05f, 0.06f, 0.08f), new Color(0.30f, 0.38f, 0.55f)),
        ("placeholder_04_grove", new Color(0.03f, 0.11f, 0.09f), new Color(0.45f, 0.62f, 0.30f)),
    };

    private static readonly Color Backdrop = new Color(0.045f, 0.050f, 0.060f, 1f);
    private static readonly Color Track = new Color(1f, 1f, 1f, 0.18f);
    private static readonly Color Fill = new Color(1f, 0.84f, 0.30f, 0.95f);
    private static readonly Color Caption = new Color(1f, 1f, 1f, 0.75f);

    // 盖在所有东西上面 / Above every other canvas in both scenes; 32767 is the ceiling.
    private const int SortingOrder = 30000;

    [MenuItem("Tools/Rhythm/Create Loading Screen")]
    public static void Create()
    {
        if (Application.isPlaying)
        {
            Debug.LogError("Exit Play Mode first.");
            return;
        }

        Run(false);
    }

    [MenuItem("Tools/Rhythm/Reset Loading Screen To Default")]
    public static void ResetToDefault()
    {
        if (Application.isPlaying)
        {
            Debug.LogError("Exit Play Mode first.");
            return;
        }

        Run(true);
    }

    private static void Run(bool reset)
    {
        EnsureFolder(BackgroundDir);
        int seeded = SeedBackgrounds();
        Sprite shade = ShadeSprite();

        if (reset && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            AssetDatabase.DeleteAsset(PrefabPath);
        }

        bool fresh = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null;
        GameObject root = fresh ? new GameObject(LoadingScreen.PrefabName, typeof(RectTransform))
                                : PrefabUtility.LoadPrefabContents(PrefabPath);

        List<string> made = new List<string>();
        int wired;

        try
        {
            Build(root, shade, made);
            wired = Wire(root);

            if (fresh || made.Count > 0 || wired > 0) PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            if (fresh) Object.DestroyImmediate(root);
            else PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();

        int art = AssetDatabase.FindAssets("t:Sprite", new[] { BackgroundDir }).Length;

        Debug.Log($"[LoadingScreenSetup] {(reset ? "Reset" : fresh ? "Created" : "Checked")} {PrefabPath}: " +
                  (made.Count > 0 ? $"added {string.Join(", ", made)}" : "every node already there") +
                  $", {wired} field(s) wired. Backgrounds: {art} in {BackgroundDir}" +
                  (seeded > 0 ? $" ({seeded} placeholder(s) generated into the empty folder)." : ".") +
                  "\n  Drop your own images into that folder (Texture Type: Sprite) and they join the rotation. " +
                  "Only art you have the right to use - the repository is public.");
    }

    // ------------------------------------------------------------------ prefab

    private static void Build(GameObject root, Sprite shade, List<string> made)
    {
        Canvas canvas = root.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            made.Add("Canvas");
        }

        if (root.GetComponent<CanvasScaler>() == null)
        {
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

            // 按高适配, 和前端其他界面一致 / Height-matched like the rest of the front-end, so
            // the type is the same size on a 20:9 phone as on a 16:9 monitor.
            scaler.matchWidthOrHeight = 1f;
            made.Add("CanvasScaler");
        }

        // 没有它挡不住点击 / Without a raycaster the backdrop would not block a single tap.
        if (root.GetComponent<GraphicRaycaster>() == null)
        {
            root.AddComponent<GraphicRaycaster>();
            made.Add("GraphicRaycaster");
        }

        if (root.GetComponent<CanvasGroup>() == null)
        {
            root.AddComponent<CanvasGroup>().alpha = 0f;
            made.Add("CanvasGroup");
        }

        if (root.GetComponent<LoadingScreen>() == null)
        {
            root.AddComponent<LoadingScreen>();
            made.Add("LoadingScreen");
        }

        Transform t = root.transform;

        // 底板也负责挡点击 / The backdrop is the one raycast target: it is what makes the screen
        // swallow taps, and what shows when the background folder is empty.
        RectTransform backdrop = Child(t, "Backdrop", 0, made, rect =>
        {
            Stretch(rect);
            UnityEngine.UI.Image image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = Backdrop;
            image.raycastTarget = true;
        });

        Child(t, "Background", backdrop.GetSiblingIndex() + 1, made, rect =>
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1920, 1080);

            UnityEngine.UI.Image image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.raycastTarget = false;

            // 铺满裁边 / Envelope the parent: the art covers the screen at any aspect and the
            // overflow simply falls off the edges. LoadingScreen sets the ratio per sprite.
            AspectRatioFitter fit = rect.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = 16f / 9f;
        });

        Child(t, "Shade", backdrop.GetSiblingIndex() + 2, made, rect =>
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            UnityEngine.UI.Image image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.sprite = shade;
            image.color = Color.black;
            image.raycastTarget = false;
        });

        Child(t, "Word", -1, made, rect =>
        {
            BottomRight(rect, new Vector2(-80f, 112f), new Vector2(900f, 140f));
            TextMeshProUGUI text = NewText(rect, "LOADING", 104f, Color.white);
            text.fontStyle = FontStyles.Bold;
            text.characterSpacing = 18f;
        });

        RectTransform bar = Child(t, "Bar", -1, made, rect =>
        {
            BottomRight(rect, new Vector2(-88f, 96f), new Vector2(560f, 6f));
            UnityEngine.UI.Image image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = Track;
            image.raycastTarget = false;
        });

        Child(bar, "Fill", -1, made, rect =>
        {
            // 锚点在运行时驱动 / The anchors are driven at runtime: x 0 - progress when the
            // progress is known, a sliding segment when it is not.
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            UnityEngine.UI.Image image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = Fill;
            image.raycastTarget = false;
        });

        Child(t, "Percent", -1, made, rect =>
        {
            BottomRight(rect, new Vector2(-88f, 44f), new Vector2(200f, 40f));
            TextMeshProUGUI text = NewText(rect, "0%", 28f, Caption);
            text.fontStyle = FontStyles.Bold;
        });
    }

    /// <summary>Fills only the fields still empty, so a hand-rewired field is never overwritten.</summary>
    private static int Wire(GameObject root)
    {
        LoadingScreen screen = root.GetComponent<LoadingScreen>();
        Transform t = root.transform;
        int wired = 0;

        if (screen.group == null) { screen.group = root.GetComponent<CanvasGroup>(); wired++; }

        Transform background = t.Find("Background");
        if (screen.background == null && background != null)
        {
            screen.background = background.GetComponent<UnityEngine.UI.Image>();
            wired++;
        }

        if (screen.backgroundFit == null && background != null)
        {
            screen.backgroundFit = background.GetComponent<AspectRatioFitter>();
            wired++;
        }

        Transform word = t.Find("Word");
        if (screen.word == null && word != null) { screen.word = word.GetComponent<TextMeshProUGUI>(); wired++; }

        Transform fill = t.Find("Bar/Fill");
        if (screen.barFill == null && fill != null) { screen.barFill = (RectTransform)fill; wired++; }

        Transform percent = t.Find("Percent");
        if (screen.percent == null && percent != null) { screen.percent = percent.GetComponent<TextMeshProUGUI>(); wired++; }

        if (wired > 0) EditorUtility.SetDirty(screen);
        return wired;
    }

    /// <summary>
    /// The named child, created and styled only if it is missing. <paramref name="index"/> puts a
    /// new layer back in its place in the stack (-1 = on top), so one deleted by hand comes back
    /// behind the type rather than over it.
    /// </summary>
    private static RectTransform Child(Transform parent, string name, int index, List<string> made,
                                       System.Action<RectTransform> style)
    {
        Transform found = parent.Find(name);
        if (found != null) return (RectTransform)found;

        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        if (index >= 0) rect.SetSiblingIndex(Mathf.Min(index, parent.childCount - 1));

        style(rect);
        made.Add(name);
        return rect;
    }

    private static TextMeshProUGUI NewText(RectTransform rect, string value, float size, Color color)
    {
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.BottomRight;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void BottomRight(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    // ------------------------------------------------------------------ art

    /// <summary>Generates the placeholders, but only into a folder that has no art at all.</summary>
    private static int SeedBackgrounds()
    {
        if (AssetDatabase.FindAssets("t:Texture2D", new[] { BackgroundDir }).Length > 0) return 0;

        foreach ((string name, Color from, Color to) in Placeholders)
        {
            ImportSprite($"{BackgroundDir}/{name}.png", Gradient(from, to), ArtWidth, ArtHeight, false);
        }

        return Placeholders.Length;
    }

    private static Color[] Gradient(Color from, Color to)
    {
        Color[] pixels = new Color[ArtWidth * ArtHeight];

        for (int y = 0; y < ArtHeight; y++)
        {
            for (int x = 0; x < ArtWidth; x++)
            {
                float u = x / (float)(ArtWidth - 1);
                float v = y / (float)(ArtHeight - 1);

                // 左下暗, 右上亮 / Dark bottom-left to bright top-right.
                Color c = Color.Lerp(from, to, Mathf.SmoothStep(0f, 1f, u * 0.65f + v * 0.35f));

                // 三道斜向柔光 / Three soft diagonal light bands.
                float d = u * 0.8f - v * 0.45f;
                float glow = Band(d, 0.18f, 0.05f) * 0.10f + Band(d, 0.42f, 0.11f) * 0.07f + Band(d, 0.63f, 0.03f) * 0.12f;
                c += new Color(glow, glow, glow, 0f);

                // 四角压暗 / Vignette.
                float dx = u - 0.5f;
                float dy = v - 0.5f;
                c *= 1f - Mathf.Clamp01((dx * dx + dy * dy) * 1.1f);

                c.a = 1f;
                pixels[y * ArtWidth + x] = c;
            }
        }

        return pixels;
    }

    private static float Band(float d, float centre, float width)
    {
        float k = Mathf.Clamp01(1f - Mathf.Abs(d - centre) / width);
        return k * k;
    }

    /// <summary>
    /// Black fading to clear upwards, laid along the bottom half so white type stays readable on
    /// the brightest background anyone drops in.
    /// </summary>
    private static Sprite ShadeSprite()
    {
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(ShadePath);
        if (existing != null) return existing;

        const int width = 4;
        const int height = 256;
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            // SetPixels 从下往上 / SetPixels rows run bottom-up, so y = 0 is the darkest row.
            float a = Mathf.Pow(1f - y / (float)(height - 1), 1.6f) * 0.8f;
            for (int x = 0; x < width; x++) pixels[y * width + x] = new Color(1f, 1f, 1f, a);
        }

        return ImportSprite(ShadePath, pixels, width, height, true);
    }

    private static Sprite ImportSprite(string assetPath, Color[] pixels, int width, int height, bool alpha)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.SetPixels(pixels);
        texture.Apply();

        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath),
            texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = alpha;
        importer.wrapMode = TextureWrapMode.Clamp;

        // 渐变压缩会出色带 / Compression bands a smooth gradient visibly.
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
