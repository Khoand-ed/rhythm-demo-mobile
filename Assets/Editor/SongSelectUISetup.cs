using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Builds the SongSelectUI prefab, points it at the beatmap library, hooks the
// home screen's Operation tile to it, and puts a Back button on the results
// screen - everything the front-end needs to reach the rhythm scene and to come
// back out of it.
//
// The prefab is generated art: it is only built when it is missing, so a layout
// you have adjusted by hand survives a re-run. Rebuild Song Select UI is the
// one that starts over.
public class SongSelectUISetup
{
    private const string UiFolder = "Assets/Arknights/Resources/Prefab/UI/";
    private const string PrefabPath = UiFolder + "SongSelectUI.prefab";
    private const string HomePrefabPath = UiFolder + "HomeUI.prefab";
    private const string OperationTileName = "OperationTile";

    // 取自 CharUI / ShopUI / CharInfoUI 的 back 按钮 / Lifted straight off the back button that
    // CharUI, ShopUI and CharInfoUI already share: a flat slab with a 2px white outline and a
    // '‹' glyph, no sprite. Matching the numbers is what makes a new screen read as one of the set.
    private const float TopBarHeight = 140f;
    private const float PanelWidth = 420f;
    private const float BackWidth = 170f;
    private const float SlabHeight = 68f;

    private static readonly Color Slab = new Color(0.30f, 0.31f, 0.33f, 0.90f);
    private static readonly Color Strip = new Color(0.10f, 0.11f, 0.12f, 0.92f);
    private static readonly Color Edge = new Color(1f, 1f, 1f, 0.30f);

    // Fully opaque: the screen it opens over (HomeUI, or the login screen when
    // it is opened straight from a tool) must not read through the map.
    private static readonly Color Ink = new Color(0.05f, 0.06f, 0.08f, 1f);
    private static readonly Color PanelInk = new Color(0.08f, 0.09f, 0.11f, 0.97f);
    private static readonly Color Accent = new Color(0.17f, 0.56f, 0.90f);
    private static readonly Color Faint = new Color(1f, 1f, 1f, 0.10f);
    private static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);

    [MenuItem("Tools/Rhythm/Wire Song Flow")]
    static void WireSongFlow()
    {
        if (Application.isPlaying)
        {
            Debug.LogError("Exit Play Mode first.");
            return;
        }

        bool built = false;

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            BuildPrefab();
            built = true;
        }

        bool hooked = HookHomeTile();
        bool wired = WireResultsBackButton();

        AssetDatabase.SaveAssets();

        Debug.Log($"Song flow wired.\n" +
                  $"  SongSelectUI prefab: {(built ? "built at " + PrefabPath : "already existed, left alone")}\n" +
                  $"  HomeUI {OperationTileName}: {(hooked ? "opens SongSelectUI" : "NOT hooked - see the warning above")}\n" +
                  $"  Results Back button: {(wired ? "added and wired to GameManager.ReturnToSongSelect" : "skipped - open Assets/Scenes/Main.unity and re-run")}");
    }

    [MenuItem("Tools/Rhythm/Rebuild Song Select UI")]
    static void Rebuild()
    {
        if (Application.isPlaying)
        {
            Debug.LogError("Exit Play Mode first.");
            return;
        }

        AssetDatabase.DeleteAsset(PrefabPath);
        BuildPrefab();
        AssetDatabase.SaveAssets();

        Debug.Log($"{PrefabPath} rebuilt from scratch. Any hand edits to it are gone.");
    }

    // ---------------------------------------------------------------- prefab

    private static void BuildPrefab()
    {
        GameObject root = new GameObject("SongSelectUI",
            typeof(RectTransform), typeof(CanvasGroup), typeof(SongSelectUI));

        RectTransform rootRect = (RectTransform)root.transform;
        Stretch(rootRect);

        Image backdrop = NewImage("Backdrop", rootRect, Ink, "T_SkewTile");
        Stretch((RectTransform)backdrop.transform);
        backdrop.raycastTarget = false;

        BuildMap(rootRect);
        BuildTopBar(rootRect);
        BuildInfoPanel(rootRect);

        // Assigned before saving, so the prefab ships already pointing at the
        // library rather than needing a second pass over the asset.
        root.GetComponent<SongSelectUI>().library = FindLibrary();

        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
    }

    // The scrollable node map. Nodes and links are created at runtime from the
    // library; all that lives here is the frame they go in and one template.
    private static void BuildMap(RectTransform parent)
    {
        RectTransform map = NewRect("Map", parent);
        map.anchorMin = Vector2.zero;
        map.anchorMax = Vector2.one;
        map.offsetMin = Vector2.zero;

        // Full height: the top bar is now floating slabs, not a band that eats
        // into the map, so the map runs behind them the way the reference does.
        map.offsetMax = new Vector2(-PanelWidth, 0f);

        Image viewportImage = NewImage("Viewport", map, new Color(0f, 0f, 0f, 0f), null);
        RectTransform viewport = (RectTransform)viewportImage.transform;
        Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();

        // Anchored to the left edge and vertically centred, because every node
        // and link is placed by an anchoredPosition measured from exactly there.
        RectTransform content = NewRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 0f);
        content.anchorMax = new Vector2(0f, 1f);
        content.pivot = new Vector2(0f, 0.5f);
        content.sizeDelta = new Vector2(1200f, 0f);
        content.anchoredPosition = Vector2.zero;

        // Links first so they draw behind the nodes they join.
        Stretch(NewRect("Links", content));
        RectTransform nodes = NewRect("Nodes", content);
        Stretch(nodes);

        BuildNodeTemplate(nodes);

        ScrollRect scroll = map.gameObject.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = true;
        scroll.vertical = false;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 30f;
    }

    private static void BuildNodeTemplate(RectTransform parent)
    {
        Image frame = NewImage("NodeTemplate", parent, new Color(0.10f, 0.11f, 0.13f, 0.95f), "T_ChamferSmall");
        RectTransform rect = (RectTransform)frame.transform;
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(200f, 66f);

        Button button = frame.gameObject.AddComponent<Button>();
        button.targetGraphic = frame;

        // The hex badge that marks a stop on the Arknights stage map.
        Image badge = NewImage("Badge", rect, Accent, "T_HexBadge");
        RectTransform badgeRect = (RectTransform)badge.transform;
        badgeRect.anchorMin = new Vector2(0f, 0.5f);
        badgeRect.anchorMax = new Vector2(0f, 0.5f);
        badgeRect.pivot = new Vector2(0.5f, 0.5f);
        badgeRect.anchoredPosition = new Vector2(32f, 0f);
        badgeRect.sizeDelta = new Vector2(34f, 34f);
        badge.raycastTarget = false;

        TextMeshProUGUI tag = NewText("Tag", rect, 15f, Dim, TextAlignmentOptions.BottomLeft);
        RectTransform tagRect = (RectTransform)tag.transform;
        tagRect.anchorMin = new Vector2(0f, 0.5f);
        tagRect.anchorMax = Vector2.one;
        tagRect.offsetMin = new Vector2(58f, 0f);
        tagRect.offsetMax = new Vector2(-10f, -8f);

        TextMeshProUGUI label = NewText("Label", rect, 22f, Color.white, TextAlignmentOptions.TopLeft);
        label.fontStyle = FontStyles.Bold;
        RectTransform labelRect = (RectTransform)label.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = new Vector2(1f, 0.5f);
        labelRect.offsetMin = new Vector2(58f, 8f);
        labelRect.offsetMax = new Vector2(-10f, 0f);
    }

    // Floating slabs over the map rather than a solid band: CharUI, ShopUI and
    // CharInfoUI all sit their back button straight on the background, and the
    // stage map in the reference screens does the same. The bar stops short of
    // the detail panel so the sanity strip lands over the map, not over it.
    private static void BuildTopBar(RectTransform parent)
    {
        RectTransform bar = NewRect("TopBar", parent);
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(-PanelWidth, TopBarHeight);
        bar.anchoredPosition = new Vector2(-PanelWidth / 2f, 0f);

        Image back = NewImage("BackButton", bar, Slab, null);
        RectTransform backRect = (RectTransform)back.transform;
        TopLeftSlab(backRect, 48f, BackWidth);

        // Only buttons get the outline; the info strips below go without it,
        // which is exactly how HomeUI's SanityStrip differs from CharUI's back.
        AddEdges(backRect);

        Button backButton = back.gameObject.AddComponent<Button>();
        backButton.targetGraphic = back;

        // '‹' (U+2039), not '<' - the glyph every other back button uses.
        TextMeshProUGUI backLabel = NewText("Text", backRect, 46f, Color.white, TextAlignmentOptions.Center);
        Stretch((RectTransform)backLabel.transform);
        backLabel.text = "‹";

        Image titlePlate = NewImage("Title", bar, Strip, null);
        RectTransform titleRect = (RectTransform)titlePlate.transform;
        TopLeftSlab(titleRect, 48f + BackWidth + 8f, 320f);
        titlePlate.raycastTarget = false;

        TextMeshProUGUI title = NewText("Text", titleRect, 28f, Color.white, TextAlignmentOptions.Center);
        title.fontStyle = FontStyles.Bold;
        Stretch((RectTransform)title.transform);
        title.text = "OPERATION";

        BuildSanityStrip(bar);
    }

    // The same 286x52 readout HomeUI carries, so the number reads identically on
    // both screens: dark strip, dim uppercase caption, bright value.
    private static void BuildSanityStrip(RectTransform parent)
    {
        Image strip = NewImage("Sanity", parent, Strip, null);
        RectTransform rect = (RectTransform)strip.transform;
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-48f, -48f);
        rect.sizeDelta = new Vector2(286f, 52f);
        strip.raycastTarget = false;

        TextMeshProUGUI caption = NewText("Caption", rect, 22f, new Color(1f, 1f, 1f, 0.75f),
                                          TextAlignmentOptions.Left);
        Inset((RectTransform)caption.transform, 18f);
        caption.text = "SANITY";

        TextMeshProUGUI value = NewText("Value", rect, 26f, Color.white, TextAlignmentOptions.Right);
        Inset((RectTransform)value.transform, 18f);
        value.text = "0/0";
    }

    private static void TopLeftSlab(RectTransform rect, float x, float width)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -40f);
        rect.sizeDelta = new Vector2(width, SlabHeight);
    }

    // Laid out by a VerticalLayoutGroup rather than by hand, so adding a row
    // later does not mean re-deriving every y offset below it.
    private static void BuildInfoPanel(RectTransform parent)
    {
        Image panel = NewImage("InfoPanel", parent, PanelInk, "T_ChamferBig");
        RectTransform rect = (RectTransform)panel.transform;
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(PanelWidth, 0f);
        rect.anchoredPosition = Vector2.zero;

        panel.gameObject.AddComponent<CanvasGroup>();

        VerticalLayoutGroup layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 20, 20);
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        BuildHeader(rect);

        Image cover = NewImage("Cover", rect, new Color(1f, 1f, 1f, 1f), null);
        cover.preserveAspect = true;
        cover.raycastTarget = false;
        Preferred((RectTransform)cover.transform, 150f);

        BuildRating(rect);

        TextMeshProUGUI stats = NewText("Stats", rect, 18f, Dim, TextAlignmentOptions.Left);
        Preferred((RectTransform)stats.transform, 26f);

        BuildDifficulties(rect);
        BuildStartButton(rect);
    }

    private static void BuildHeader(RectTransform parent)
    {
        RectTransform header = NewRect("Header", parent);
        Preferred(header, 118f);

        VerticalLayoutGroup layout = header.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 2f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI songId = NewText("SongId", header, 16f, Accent, TextAlignmentOptions.Left);
        songId.fontStyle = FontStyles.Bold;
        Preferred((RectTransform)songId.transform, 22f);

        TextMeshProUGUI name = NewText("Name", header, 34f, Color.white, TextAlignmentOptions.Left);
        name.fontStyle = FontStyles.Bold;
        Preferred((RectTransform)name.transform, 46f);

        TextMeshProUGUI author = NewText("Author", header, 17f, Dim, TextAlignmentOptions.Left);
        Preferred((RectTransform)author.transform, 24f);
    }

    // Five hexes, filled up to the chart's difficulty - the operation rating
    // from the reference screens, drawn with the placeholder badge.
    private static void BuildRating(RectTransform parent)
    {
        RectTransform rating = NewRect("Rating", parent);
        Preferred(rating, 34f);

        HorizontalLayoutGroup layout = rating.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 6f;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.MiddleLeft;

        for (int i = 0; i < 5; i++)
        {
            Image hex = NewImage("Hex" + i, rating, new Color(1f, 1f, 1f, 0.15f), "T_HexBadge");
            ((RectTransform)hex.transform).sizeDelta = new Vector2(30f, 30f);
            hex.raycastTarget = false;
        }
    }

    private static void BuildDifficulties(RectTransform parent)
    {
        RectTransform list = NewRect("Difficulties", parent);

        // The only row that grows, so the Start button stays pinned at the
        // bottom however many difficulties a song turns out to have.
        LayoutElement element = list.gameObject.AddComponent<LayoutElement>();
        element.flexibleHeight = 1f;
        element.minHeight = 60f;

        VerticalLayoutGroup layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.UpperCenter;

        Image row = NewImage("DifficultyTemplate", list, new Color(1f, 1f, 1f, 0.08f), "T_ChamferSmall");
        RectTransform rowRect = (RectTransform)row.transform;
        rowRect.sizeDelta = new Vector2(0f, 52f);
        Preferred(rowRect, 52f);

        Button button = row.gameObject.AddComponent<Button>();
        button.targetGraphic = row;

        TextMeshProUGUI label = NewText("Label", rowRect, 20f, Color.white, TextAlignmentOptions.Left);
        RectTransform labelRect = (RectTransform)label.transform;
        Stretch(labelRect);
        labelRect.offsetMin = new Vector2(16f, 0f);
        labelRect.offsetMax = new Vector2(-16f, 0f);
    }

    private static void BuildStartButton(RectTransform parent)
    {
        Image start = NewImage("StartButton", parent, Accent, "T_ChamferBig");
        RectTransform rect = (RectTransform)start.transform;
        Preferred(rect, 76f);

        Button button = start.gameObject.AddComponent<Button>();
        button.targetGraphic = start;

        TextMeshProUGUI label = NewText("Label", rect, 28f, Color.white, TextAlignmentOptions.Center);
        label.fontStyle = FontStyles.Bold;
        Stretch((RectTransform)label.transform);
        label.text = "START";
    }

    // ------------------------------------------------------------- wiring

    // HomeUI is driven by Lua, so the tile gets a small C# component rather than
    // a new injection and a new line in HomeUI.lua.txt.
    private static bool HookHomeTile()
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(HomePrefabPath);
        if (asset == null)
        {
            Debug.LogWarning($"No HomeUI prefab at {HomePrefabPath}, so nothing opens SongSelectUI yet.");
            return false;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(HomePrefabPath);

        try
        {
            Transform tile = FindDeep(contents.transform, OperationTileName);
            if (tile == null)
            {
                Debug.LogWarning($"HomeUI has no '{OperationTileName}' child. Add an OpenUIButton " +
                                 $"with uiName '{SongSelectUI.UIName}' to whichever tile should open the song list.");
                return false;
            }

            // A Button needs something raycastable under it; the tile art may be
            // a child rather than the tile itself.
            if (tile.GetComponent<Graphic>() == null)
            {
                Image hit = tile.gameObject.AddComponent<Image>();
                hit.color = new Color(0f, 0f, 0f, 0f);
            }

            Button button = tile.GetComponent<Button>();
            if (button == null) button = tile.gameObject.AddComponent<Button>();
            button.targetGraphic = tile.GetComponent<Graphic>();

            OpenUIButton open = tile.GetComponent<OpenUIButton>();
            if (open == null) open = tile.gameObject.AddComponent<OpenUIButton>();
            open.uiName = SongSelectUI.UIName;

            PrefabUtility.SaveAsPrefabAsset(contents, HomePrefabPath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // Only does anything when the gameplay scene is the open one, the same way
    // HudSetup works on whatever scene you have in front of you.
    private static bool WireResultsBackButton()
    {
        GameManager gameManager = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        if (gameManager == null) return false;

        if (gameManager.resultsScreen == null)
        {
            Debug.LogWarning("GameManager.resultsScreen is not assigned, so the Back button has nowhere to go.");
            return false;
        }

        Transform results = gameManager.resultsScreen.transform;
        Transform existing = results.Find("BackButton");
        if (existing != null) return true;

        Image back = NewImage("BackButton", (RectTransform)results, Faint, "T_ChamferSmall");
        RectTransform rect = (RectTransform)back.transform;
        Undo.RegisterCreatedObjectUndo(back.gameObject, "Wire Song Flow");

        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 40f);
        rect.sizeDelta = new Vector2(320f, 72f);

        Button button = back.gameObject.AddComponent<Button>();
        button.targetGraphic = back;
        UnityEventTools.AddPersistentListener(button.onClick,
            new UnityAction(gameManager.ReturnToSongSelect));

        TextMeshProUGUI label = NewText("Label", rect, 26f, Color.white, TextAlignmentOptions.Center);
        label.fontStyle = FontStyles.Bold;
        Stretch((RectTransform)label.transform);
        label.text = "SONG SELECT";

        EditorUtility.SetDirty(gameManager.resultsScreen);
        EditorSceneManager.MarkSceneDirty(gameManager.gameObject.scene);
        return true;
    }

    private static BeatmapLibrary FindLibrary()
    {
        string[] guids = AssetDatabase.FindAssets("t:BeatmapLibrary");

        if (guids.Length == 0)
        {
            Debug.LogWarning("No BeatmapLibrary asset in the project, so the map will be empty. " +
                             "Run Tools/Rhythm/Import Beatmaps first.");
            return null;
        }

        if (guids.Length > 1)
        {
            Debug.LogWarning($"{guids.Length} BeatmapLibrary assets found; using the first. " +
                             "Assign the right one on the SongSelectUI prefab if that is wrong.");
        }

        return AssetDatabase.LoadAssetAtPath<BeatmapLibrary>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    // ------------------------------------------------------------- helpers

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static Image NewImage(string name, Transform parent, Color color, string spriteName)
    {
        Image image = NewRect(name, parent).gameObject.AddComponent<Image>();
        image.color = color;

        if (!string.IsNullOrEmpty(spriteName))
        {
            image.sprite = Art(spriteName);
            // The chamfer and tile sprites carry 9-slice borders, so they scale
            // to any panel without the corners stretching.
            if (image.sprite != null) image.type = Image.Type.Sliced;
        }

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

    private static void Inset(RectTransform rect, float horizontal)
    {
        Stretch(rect);
        rect.offsetMin = new Vector2(horizontal, 0f);
        rect.offsetMax = new Vector2(-horizontal, 0f);
    }

    /// <summary>
    /// 四条2px描边 / The four 2px white hairlines that mark something as pressable across the
    /// front-end. CharUI, ShopUI and their cards all carry the same four children under the
    /// same names, so a reader moving between screens sees one widget, not two.
    /// </summary>
    private static void AddEdges(RectTransform parent)
    {
        NewEdge(parent, "EdgeTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(0f, 2f));
        NewEdge(parent, "EdgeBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(0f, 2f));
        NewEdge(parent, "EdgeLeft", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(2f, 0f));
        NewEdge(parent, "EdgeRight", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-1f, 0f), new Vector2(2f, 0f));
    }

    private static void NewEdge(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                                Vector2 position, Vector2 size)
    {
        Image edge = NewImage(name, parent, Edge, null);
        RectTransform rect = (RectTransform)edge.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        edge.raycastTarget = false;
    }

    private static void Preferred(RectTransform rect, float height)
    {
        LayoutElement element = rect.GetComponent<LayoutElement>();
        if (element == null) element = rect.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = height;
    }

    private static Sprite Art(string name)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UiFolder + name + ".png");

        if (sprite == null)
        {
            Debug.LogWarning($"Placeholder sprite '{name}' is missing from {UiFolder}. " +
                             "Run Arknights/Placeholders/Generate Bootstrap Assets.");
        }

        return sprite;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), name);
            if (found != null) return found;
        }

        return null;
    }
}
