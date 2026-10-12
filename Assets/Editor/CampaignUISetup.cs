using System.IO;
using TMPro;
using UI.Sub;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 主线 / Builds CampaignUI: chapters on the left, the chosen chapter's stages on the right, each
/// with its first-clear Orundum, its drops, its Sanity, a STORY and a PLAY button. ModeSelectUI's
/// STORY card opens it (ModeSelectUI.StoryUIName).
///
/// 找到就不动 / Find-or-create: a prefab that exists is kept. Rebuild Campaign UI deletes it and
/// builds it again. Same palette and header as the event and song screens.
/// </summary>
public static class CampaignUISetup
{
    private const string UiPrefabDir = "Assets/Arknights/Resources/Prefab/UI";
    private const string PrefabPath = UiPrefabDir + "/" + CampaignUI.UIName + ".prefab";
    private const string LibraryPath = "Assets/Beatmaps/BeatmapLibrary.asset";

    private static readonly Color Ink = new Color(0.05f, 0.06f, 0.08f, 1f);
    private static readonly Color PanelInk = new Color(0.08f, 0.09f, 0.11f, 1f);
    private static readonly Color RowTint = new Color(0.13f, 0.14f, 0.17f, 1f);
    private static readonly Color Strip = new Color(0.10f, 0.11f, 0.12f, 0.92f);
    private static readonly Color Slab = new Color(0.30f, 0.31f, 0.33f, 0.90f);
    private static readonly Color EdgeInk = new Color(1f, 1f, 1f, 0.30f);
    private static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);
    private static readonly Color Accent = new Color(0.17f, 0.56f, 0.90f, 1f);
    private static readonly Color Gold = new Color(0.98f, 0.78f, 0.25f, 1f);

    [MenuItem("Arknights/Campaign/Build Campaign UI", false, 1)]
    public static void Build()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        Directory.CreateDirectory(UiPrefabDir);
        string done;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            done = $"{CampaignUI.UIName}: kept";
        }
        else
        {
            GameObject root = BuildScreen();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            done = $"{CampaignUI.UIName}: built at {PrefabPath}";
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[CampaignUISetup] {done}\n  ModeSelectUI STORY opens {ModeSelectUI.StoryUIName}");
    }

    [MenuItem("Arknights/Campaign/Rebuild Campaign UI", false, 20)]
    public static void Rebuild()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        AssetDatabase.DeleteAsset(PrefabPath);
        Build();
    }

    // ------------------------------------------------------------------ the screen

    private static GameObject BuildScreen()
    {
        GameObject root = new GameObject(CampaignUI.UIName, typeof(RectTransform), typeof(CanvasGroup), typeof(CampaignUI));
        CampaignUI ui = root.GetComponent<CampaignUI>();
        RectTransform rect = (RectTransform)root.transform;
        Stretch(rect);

        ui.library = AssetDatabase.LoadAssetAtPath<BeatmapLibrary>(LibraryPath);
        if (ui.library == null) Debug.LogWarning($"[CampaignUISetup] No BeatmapLibrary at {LibraryPath}; stages will list their charts as missing.");

        Stretch(NewImage("Backdrop", rect, Ink, true).rectTransform);
        UnityEngine.UI.Image band = NewImage("Band", rect, new Color(Gold.r, Gold.g, Gold.b, 0.025f), false);
        Place(band.rectTransform, Center, Center, Center, new Vector2(0f, -40f), new Vector2(3000f, 380f));
        band.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -10f);

        ui.backButton = Header(rect, "MAIN CAMPAIGN");
        ui.songListButton = TextButton("SongListButton", rect, TopRight, new Vector2(-48f, -40f), new Vector2(260f, 68f), "SONG LIST  ›", Slab, 24f);

        // ---- chapters
        ui.chapterList = NewRect("ChapterList", rect);
        Place(ui.chapterList, TopLeft, TopLeft, TopLeft, new Vector2(48f, -140f), new Vector2(380f, 860f));
        Vertical(ui.chapterList, 12f);
        ui.chapterTemplate = ChapterButton(ui.chapterList);

        // ---- stages
        RectTransform area = NewRect("Area", rect);
        area.anchorMin = Vector2.zero;
        area.anchorMax = Vector2.one;
        area.offsetMin = new Vector2(48f + 380f + 40f, 40f);
        area.offsetMax = new Vector2(-48f, -140f);

        ui.chapterTitle = NewText("ChapterTitle", area, 44f, Color.white, TextAlignmentOptions.TopLeft);
        Place(ui.chapterTitle.rectTransform, TopLeft, TopRight, TopLeft, Vector2.zero, new Vector2(0f, 60f));
        ui.chapterTitle.fontStyle = FontStyles.Bold;

        ui.note = NewText("Note", area, 26f, Dim, TextAlignmentOptions.Center);
        Place(ui.note.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(800f, 50f));

        ui.stageList = ScrollList(area, Vector2.zero, new Vector2(0f, -80f));
        ui.stageTemplate = StageRow(ui.stageList);
        return root;
    }

    private static RectTransform ChapterButton(RectTransform list)
    {
        RectTransform b = NewRect("ChapterTemplate", list);
        FixedSize(b, 380f, 110f);
        b.gameObject.AddComponent<CanvasGroup>();

        UnityEngine.UI.Image face = NewImage("Face", b, PanelInk, true);
        Stretch(face.rectTransform);
        UnityEngine.UI.Image frame = NewImage("Frame", b, new Color(1f, 1f, 1f, 0.12f), false);
        Place(frame.rectTransform, BottomLeft, TopLeft, MiddleLeft, Vector2.zero, new Vector2(8f, 0f));

        TextMeshProUGUI title = NewText("Title", b, 26f, Color.white, TextAlignmentOptions.Left);
        Place(title.rectTransform, TopLeft, TopRight, TopLeft, new Vector2(24f, -16f), new Vector2(-40f, 40f));
        title.fontStyle = FontStyles.Bold;
        title.enableAutoSizing = true;
        title.fontSizeMin = 18f;
        title.fontSizeMax = 26f;
        TextMeshProUGUI progress = NewText("Progress", b, 20f, Dim, TextAlignmentOptions.Left);
        Place(progress.rectTransform, BottomLeft, BottomRight, BottomLeft, new Vector2(24f, 16f), new Vector2(-40f, 30f));

        b.gameObject.AddComponent<Button>().targetGraphic = face;
        return b;
    }

    /// <summary>One stage: its code, song and status on the left; drops, Sanity, STORY and PLAY on the right.</summary>
    private static RectTransform StageRow(RectTransform parent)
    {
        UnityEngine.UI.Image back = NewImage("StageTemplate", parent, RowTint, false);
        RectTransform row = back.rectTransform;
        FixedSize(row, 1200f, 120f);
        row.gameObject.AddComponent<CanvasGroup>();

        TextMeshProUGUI code = NewText("Code", row, 40f, Gold, TextAlignmentOptions.Center);
        Place(code.rectTransform, MiddleLeft, MiddleLeft, MiddleLeft, new Vector2(8f, 0f), new Vector2(110f, 60f));
        code.fontStyle = FontStyles.Bold;

        TextMeshProUGUI title = NewText("Title", row, 26f, Color.white, TextAlignmentOptions.Left);
        title.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        title.rectTransform.anchorMax = new Vector2(1f, 1f);
        title.rectTransform.offsetMin = new Vector2(132f, 0f);
        title.rectTransform.offsetMax = new Vector2(-880f, -10f);
        title.fontStyle = FontStyles.Bold;
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.overflowMode = TextOverflowModes.Ellipsis;

        TextMeshProUGUI detail = NewText("Detail", row, 20f, Gold, TextAlignmentOptions.Left);
        detail.rectTransform.anchorMin = new Vector2(0f, 0f);
        detail.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        detail.rectTransform.offsetMin = new Vector2(132f, 10f);
        detail.rectTransform.offsetMax = new Vector2(-880f, 0f);
        detail.textWrappingMode = TextWrappingModes.NoWrap;
        detail.overflowMode = TextOverflowModes.Ellipsis;

        // ---- right to left: PLAY, STORY, Sanity, drops
        TextButton("Action", row, MiddleRight, new Vector2(-16f, 0f), new Vector2(200f, 64f), "PLAY", Accent, 24f);
        TextButton("Story", row, MiddleRight, new Vector2(-228f, 0f), new Vector2(140f, 64f), "STORY", Slab, 22f).gameObject.SetActive(false);

        TextMeshProUGUI cost = NewText("Cost", row, 20f, Dim, TextAlignmentOptions.Right);
        Place(cost.rectTransform, MiddleRight, MiddleRight, MiddleRight, new Vector2(-384f, 0f), new Vector2(130f, 40f));
        cost.fontStyle = FontStyles.Bold;

        RectTransform drops = NewRect("Drops", row);
        Place(drops, MiddleRight, MiddleRight, MiddleRight, new Vector2(-530f, 0f), new Vector2(340f, 100f));
        HorizontalLayoutGroup h = drops.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8f;
        h.childAlignment = TextAnchor.MiddleRight;
        h.childControlWidth = h.childControlHeight = false;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        h.reverseArrangement = false;

        RectTransform drop = NewRect("DropTemplate", drops);
        drop.sizeDelta = new Vector2(76f, 96f);
        UnityEngine.UI.Image slot = NewImage("Slot", drop, new Color(1f, 1f, 1f, 0.06f), false);
        Place(slot.rectTransform, TopCenter, TopCenter, TopCenter, Vector2.zero, new Vector2(68f, 68f));
        UnityEngine.UI.Image icon = NewImage("Icon", drop, Color.white, false);
        Place(icon.rectTransform, TopCenter, TopCenter, TopCenter, new Vector2(0f, -4f), new Vector2(60f, 60f));
        icon.preserveAspect = true;
        TextMeshProUGUI rarity = NewText("Rarity", drop, 14f, Dim, TextAlignmentOptions.Center);
        Place(rarity.rectTransform, BottomCenter, BottomCenter, BottomCenter, Vector2.zero, new Vector2(84f, 24f));
        rarity.fontStyle = FontStyles.Bold;
        rarity.enableAutoSizing = true;
        rarity.fontSizeMin = 10f;
        rarity.fontSizeMax = 14f;
        drop.gameObject.SetActive(false);

        row.gameObject.SetActive(false);
        return row;
    }

    // ------------------------------------------------------------------ helpers

    private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
    private static readonly Vector2 TopLeft = new Vector2(0f, 1f);
    private static readonly Vector2 TopCenter = new Vector2(0.5f, 1f);
    private static readonly Vector2 TopRight = new Vector2(1f, 1f);
    private static readonly Vector2 MiddleLeft = new Vector2(0f, 0.5f);
    private static readonly Vector2 MiddleRight = new Vector2(1f, 0.5f);
    private static readonly Vector2 BottomLeft = new Vector2(0f, 0f);
    private static readonly Vector2 BottomCenter = new Vector2(0.5f, 0f);
    private static readonly Vector2 BottomRight = new Vector2(1f, 0f);

    private static Button Header(RectTransform root, string title)
    {
        UnityEngine.UI.Image back = NewImage("BackButton", root, Slab, true);
        Place(back.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(48f, -40f), new Vector2(170f, 68f));
        AddEdges(back.rectTransform);
        TextMeshProUGUI glyph = NewText("Text", back.rectTransform, 46f, Color.white, TextAlignmentOptions.Center);
        Stretch(glyph.rectTransform);
        glyph.text = "‹";
        Button button = back.gameObject.AddComponent<Button>();
        button.targetGraphic = back;

        UnityEngine.UI.Image plate = NewImage("Title", root, Strip, false);
        Place(plate.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(48f + 170f + 8f, -40f), new Vector2(380f, 68f));
        TextMeshProUGUI text = NewText("Text", plate.rectTransform, 28f, Color.white, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        text.fontStyle = FontStyles.Bold;
        text.characterSpacing = 3f;
        text.text = title;
        return button;
    }

    private static Button TextButton(string name, RectTransform parent, Vector2 anchor, Vector2 position, Vector2 size,
                                     string label, Color color, float fontSize)
    {
        UnityEngine.UI.Image face = NewImage(name, parent, color, true);
        Place(face.rectTransform, anchor, anchor, anchor, position, size);
        face.gameObject.AddComponent<CanvasGroup>();
        AddEdges(face.rectTransform);
        TextMeshProUGUI text = NewText("Label", face.rectTransform, fontSize, Color.white, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        text.fontStyle = FontStyles.Bold;
        text.characterSpacing = 2f;
        text.text = label;
        Button button = face.gameObject.AddComponent<Button>();
        button.targetGraphic = face;
        return button;
    }

    private static RectTransform ScrollList(RectTransform parent, Vector2 offsetMin, Vector2 offsetMax)
    {
        RectTransform view = NewRect("Scroll", parent);
        view.anchorMin = Vector2.zero;
        view.anchorMax = Vector2.one;
        view.offsetMin = offsetMin;
        view.offsetMax = offsetMax;
        view.gameObject.AddComponent<RectMask2D>();
        UnityEngine.UI.Image hit = NewImage("Hit", view, new Color(0f, 0f, 0f, 0f), true);
        Stretch(hit.rectTransform);

        RectTransform content = NewRect("Content", view);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        Vertical(content, 12f);
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = view.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = view;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        return content;
    }

    private static void Vertical(RectTransform rect, float spacing)
    {
        VerticalLayoutGroup layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }

    private static void FixedSize(RectTransform rect, float width, float height)
    {
        rect.sizeDelta = new Vector2(width, height);
        LayoutElement element = rect.gameObject.AddComponent<LayoutElement>();
        element.minHeight = element.preferredHeight = height;
        element.preferredWidth = width;
        element.flexibleHeight = 0f;
    }

    private static void AddEdges(RectTransform parent)
    {
        Edge(parent, "EdgeTop", TopLeft, TopRight, new Vector2(0f, -1f), new Vector2(0f, 2f));
        Edge(parent, "EdgeBottom", BottomLeft, new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(0f, 2f));
        Edge(parent, "EdgeLeft", BottomLeft, TopLeft, new Vector2(1f, 0f), new Vector2(2f, 0f));
        Edge(parent, "EdgeRight", new Vector2(1f, 0f), TopRight, new Vector2(-1f, 0f), new Vector2(2f, 0f));
    }

    private static void Edge(RectTransform parent, string name, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
    {
        UnityEngine.UI.Image edge = NewImage(name, parent, EdgeInk, false);
        edge.rectTransform.anchorMin = min;
        edge.rectTransform.anchorMax = max;
        edge.rectTransform.pivot = Center;
        edge.rectTransform.anchoredPosition = position;
        edge.rectTransform.sizeDelta = size;
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
}
