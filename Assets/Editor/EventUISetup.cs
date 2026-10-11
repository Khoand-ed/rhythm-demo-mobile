using System.Collections.Generic;
using System.IO;
using TMPro;
using UI;
using UI.Sub;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 活动界面 / Builds the event screens and the way into them:
///   - ModeSelectUI: EVENT and STORY (coming soon), opened from Home's news screen,
///   - EventUI: the event list, a tab per module, and the templates the screen clones,
///   - HomeEventTile on HomeUI's NewsPanel, so the little TV shows the running event,
///   - the Event Token item the seeded event pays in.
///
/// 找到就不动 / Find-or-create: a prefab that exists is kept as it is, and the Home hook is only
/// added where it is missing. Rebuild Event UI deletes the two prefabs and builds them again.
///
/// Same palette and header as the headhunting and song screens, so leaving any of them is the same
/// gesture in the same corner.
/// </summary>
public static class EventUISetup
{
    private const string UiPrefabDir = "Assets/Arknights/Resources/Prefab/UI";
    private const string LibraryPath = "Assets/Beatmaps/BeatmapLibrary.asset";

    private static readonly Color Ink = new Color(0.05f, 0.06f, 0.08f, 1f);
    private static readonly Color PanelInk = new Color(0.08f, 0.09f, 0.11f, 1f);
    private static readonly Color InfoInk = new Color(0.11f, 0.12f, 0.15f, 1f);
    private static readonly Color Strip = new Color(0.10f, 0.11f, 0.12f, 0.92f);
    private static readonly Color Slab = new Color(0.30f, 0.31f, 0.33f, 0.90f);
    private static readonly Color Edge = new Color(1f, 1f, 1f, 0.30f);
    private static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);
    private static readonly Color Accent = new Color(0.17f, 0.56f, 0.90f, 1f);
    private static readonly Color Gold = new Color(0.98f, 0.78f, 0.25f, 1f);
    // 完全不透明 / Fully opaque, like every panel in this project: the screen renders in linear
    // space, and even 5% lets the diagonal band behind the list show through as grey patches.
    private static readonly Color RowTint = new Color(0.13f, 0.14f, 0.17f, 1f);

    // ------------------------------------------------------------------ menu items

    [MenuItem("Arknights/Events/Build Event UI", false, 1)]
    public static void Build()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        List<string> done = new List<string>();

        DepotUISetup.CreateMissingItemMetas();
        done.Add("item metas checked (20 Event Token)");

        Directory.CreateDirectory(UiPrefabDir);
        done.Add(BuildIfMissing(ModeSelectUI.UIName, BuildModeSelect));
        done.Add(BuildIfMissing(EventUI.UIName, BuildEventScreen));
        done.Add(HookHome());

        AssetDatabase.SaveAssets();
        Debug.Log("[EventUISetup] " + string.Join("\n  ", done));
    }

    /// <summary>Deletes both event prefabs and builds them again. Home's hook is left alone.</summary>
    [MenuItem("Arknights/Events/Rebuild Event UI", false, 20)]
    public static void Rebuild()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        AssetDatabase.DeleteAsset($"{UiPrefabDir}/{ModeSelectUI.UIName}.prefab");
        AssetDatabase.DeleteAsset($"{UiPrefabDir}/{EventUI.UIName}.prefab");
        Build();
    }

    private static string BuildIfMissing(string name, System.Func<GameObject> build)
    {
        string path = $"{UiPrefabDir}/{name}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return $"{name}: kept";

        GameObject root = build();
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return $"{name}: built at {path}";
    }

    // ------------------------------------------------------------------ mode select

    private static GameObject BuildModeSelect()
    {
        GameObject root = NewRoot(ModeSelectUI.UIName, typeof(ModeSelectUI));
        ModeSelectUI ui = root.GetComponent<ModeSelectUI>();
        RectTransform rect = (RectTransform)root.transform;

        Stretch((RectTransform)NewImage("Backdrop", rect, Ink, true).transform);
        ui.backButton = Header(rect, "SELECT MODE");

        // 活动卡 / The event card: what is on, and for how long.
        RectTransform eventCard = Card(rect, "EventCard", new Vector2(-350f, -30f), Accent, "EVENT", out ui.eventCard);
        ui.eventGroup = eventCard.gameObject.AddComponent<CanvasGroup>();
        ui.eventTitle = NewText("EventTitle", eventCard, 36f, Color.white, TextAlignmentOptions.Center);
        Place(ui.eventTitle.rectTransform, Center, Center, Center, new Vector2(0f, -40f), new Vector2(580f, 100f));
        ui.eventTitle.fontStyle = FontStyles.Bold;
        ui.eventDetail = NewText("EventDetail", eventCard, 24f, Dim, TextAlignmentOptions.Center);
        Place(ui.eventDetail.rectTransform, Center, Center, Center, new Vector2(0f, -120f), new Vector2(580f, 40f));

        // 剧情卡 / The story card - a later update; it says so rather than being missing.
        RectTransform storyCard = Card(rect, "StoryCard", new Vector2(350f, -30f), Slab, "STORY", out ui.storyCard);
        storyCard.gameObject.AddComponent<CanvasGroup>().alpha = 0.7f;
        TextMeshProUGUI soon = NewText("ComingSoon", storyCard, 30f, Gold, TextAlignmentOptions.Center);
        Place(soon.rectTransform, Center, Center, Center, new Vector2(0f, -40f), new Vector2(580f, 60f));
        soon.fontStyle = FontStyles.Bold;
        soon.characterSpacing = 4f;
        soon.text = "COMING SOON";
        TextMeshProUGUI pitch = NewText("Pitch", storyCard, 24f, Dim, TextAlignmentOptions.Center);
        Place(pitch.rectTransform, Center, Center, Center, new Vector2(0f, -120f), new Vector2(560f, 80f));
        pitch.text = "Chapters, dialogue, stages and a shop of its own.";

        return root;
    }

    private static RectTransform Card(RectTransform parent, string name, Vector2 position, Color bar, string label, out Button button)
    {
        UnityEngine.UI.Image face = NewImage(name, parent, PanelInk, true);
        RectTransform card = (RectTransform)face.transform;
        Place(card, Center, Center, Center, position, new Vector2(640f, 520f));
        AddEdges(card, Edge, 2f);

        UnityEngine.UI.Image top = NewImage("Bar", card, bar, false);
        Place((RectTransform)top.transform, TopLeft, TopRight, TopCenter, Vector2.zero, new Vector2(0f, 10f));

        TextMeshProUGUI title = NewText("Label", card, 72f, Color.white, TextAlignmentOptions.Center);
        Place(title.rectTransform, Center, Center, Center, new Vector2(0f, 110f), new Vector2(580f, 110f));
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 12f;
        title.text = label;

        button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = face;
        return card;
    }

    // ------------------------------------------------------------------ event screen

    private static GameObject BuildEventScreen()
    {
        GameObject root = NewRoot(EventUI.UIName, typeof(EventUI));
        EventUI ui = root.GetComponent<EventUI>();
        RectTransform rect = (RectTransform)root.transform;

        ui.library = AssetDatabase.LoadAssetAtPath<BeatmapLibrary>(LibraryPath);
        if (ui.library == null) Debug.LogWarning($"[EventUISetup] No BeatmapLibrary at {LibraryPath}; the stages tab will list charts as missing.");

        Stretch((RectTransform)NewImage("Backdrop", rect, Ink, true).transform);
        UnityEngine.UI.Image band = NewImage("Band", rect, new Color(Accent.r, Accent.g, Accent.b, 0.06f), false);
        Place((RectTransform)band.transform, Center, Center, Center, new Vector2(0f, -40f), new Vector2(3000f, 380f));
        band.transform.localRotation = Quaternion.Euler(0f, 0f, 10f);

        ui.backButton = Header(rect, "EVENT");

        // 钱包 / The event currency, top right.
        UnityEngine.UI.Image pill = NewImage("CurrencyPill", rect, Strip, false);
        RectTransform pillRect = (RectTransform)pill.transform;
        Place(pillRect, TopRight, TopRight, TopRight, new Vector2(-48f, -42f), new Vector2(260f, 64f));
        AddEdges(pillRect, Edge, 2f);
        ui.currencyPill = pill.gameObject;
        ui.currencyIcon = NewImage("Icon", pillRect, Color.white, false);
        Place(ui.currencyIcon.rectTransform, MiddleLeft, MiddleLeft, MiddleLeft, new Vector2(12f, 0f), new Vector2(46f, 46f));
        ui.currencyIcon.preserveAspect = true;
        ui.currencyAmount = NewText("Amount", pillRect, 28f, Color.white, TextAlignmentOptions.Right);
        Place(ui.currencyAmount.rectTransform, MiddleRight, MiddleRight, MiddleRight, new Vector2(-18f, 0f), new Vector2(180f, 50f));

        // ---- left: the events
        ui.eventList = NewRect("EventList", rect);
        Place(ui.eventList, TopLeft, TopLeft, TopLeft, new Vector2(48f, -140f), new Vector2(380f, 860f));
        Vertical(ui.eventList, 12f);
        ui.eventTemplate = EventButton(ui.eventList);

        // ---- right: heading, tabs, pages
        RectTransform area = NewRect("Area", rect);
        area.anchorMin = Vector2.zero;
        area.anchorMax = Vector2.one;
        area.offsetMin = new Vector2(48f + 380f + 40f, 40f);
        area.offsetMax = new Vector2(-48f, -140f);

        ui.titleText = NewText("Title", area, 52f, Color.white, TextAlignmentOptions.TopLeft);
        Place(ui.titleText.rectTransform, TopLeft, TopRight, TopLeft, Vector2.zero, new Vector2(-420f, 64f));
        ui.titleText.fontStyle = FontStyles.Bold;
        ui.subtitleText = NewText("Subtitle", area, 24f, Dim, TextAlignmentOptions.TopLeft);
        Place(ui.subtitleText.rectTransform, TopLeft, TopRight, TopLeft, new Vector2(0f, -66f), new Vector2(-420f, 32f));
        ui.timeText = NewText("Time", area, 24f, Gold, TextAlignmentOptions.TopRight);
        Place(ui.timeText.rectTransform, TopRight, TopRight, TopRight, new Vector2(0f, -12f), new Vector2(400f, 36f));
        ui.timeText.fontStyle = FontStyles.Bold;

        ui.tabRow = NewRect("Tabs", area);
        Place(ui.tabRow, TopLeft, TopRight, TopLeft, new Vector2(0f, -112f), new Vector2(0f, 56f));
        Horizontal(ui.tabRow, 12f);
        ui.tabTemplate = TabButton(ui.tabRow);

        RectTransform pages = NewRect("Pages", area);
        pages.anchorMin = Vector2.zero;
        pages.anchorMax = Vector2.one;
        pages.offsetMin = Vector2.zero;
        pages.offsetMax = new Vector2(0f, -190f);

        ui.note = NewText("Note", pages, 26f, Dim, TextAlignmentOptions.Center);
        Place(ui.note.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(800f, 50f));

        BuildOverview(pages, ui);
        BuildCheckIn(pages, ui);
        BuildMissions(pages, ui);
        ui.shopPage = Page(pages, "ShopPage");
        ui.shopList = ScrollList(ui.shopPage.transform, Vector2.zero, Vector2.zero);
        ui.stagesPage = Page(pages, "StagesPage");
        ui.stageList = ScrollList(ui.stagesPage.transform, Vector2.zero, Vector2.zero);

        // ---- the templates the pages clone
        RectTransform templates = NewRect("Templates", rect);
        Place(templates, Center, Center, Center, Vector2.zero, new Vector2(1200f, 400f));
        ui.rowTemplate = Row(templates);
        ui.cellTemplate = RewardCell(templates);

        return root;
    }

    private static void BuildOverview(RectTransform pages, EventUI ui)
    {
        ui.overviewPage = Page(pages, "OverviewPage");
        RectTransform page = (RectTransform)ui.overviewPage.transform;

        UnityEngine.UI.Image panel = NewImage("Panel", page, InfoInk, false);
        RectTransform panelRect = (RectTransform)panel.transform;
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = new Vector2(0f, 110f);
        panelRect.offsetMax = Vector2.zero;

        ui.descriptionText = NewText("Description", panelRect, 28f, Color.white, TextAlignmentOptions.TopLeft);
        Inset(ui.descriptionText.rectTransform, 32f);

        ui.bannerButton = TextButton("BannerButton", page, BottomLeft, new Vector2(0f, 10f), new Vector2(380f, 80f), "HEADHUNTING  ›", Accent, out _);
    }

    private static void BuildCheckIn(RectTransform pages, EventUI ui)
    {
        ui.checkInPage = Page(pages, "CheckInPage");
        RectTransform page = (RectTransform)ui.checkInPage.transform;

        ui.dayGrid = NewRect("Days", page);
        Place(ui.dayGrid, TopLeft, TopRight, TopLeft, Vector2.zero, new Vector2(0f, 500f));
        GridLayoutGroup grid = ui.dayGrid.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(180f, 230f);
        grid.spacing = new Vector2(16f, 16f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 7;

        ui.checkInButton = TextButton("CheckInButton", page, BottomLeft, new Vector2(0f, 10f), new Vector2(320f, 80f), "CHECK IN", Accent, out ui.checkInLabel);
        ui.checkInNote = NewText("Note", page, 24f, Dim, TextAlignmentOptions.Left);
        Place(ui.checkInNote.rectTransform, BottomLeft, BottomLeft, BottomLeft, new Vector2(350f, 28f), new Vector2(700f, 44f));
    }

    private static void BuildMissions(RectTransform pages, EventUI ui)
    {
        ui.missionsPage = Page(pages, "MissionsPage");
        RectTransform page = (RectTransform)ui.missionsPage.transform;

        ui.pointsText = NewText("Points", page, 30f, Gold, TextAlignmentOptions.Left);
        Place(ui.pointsText.rectTransform, TopLeft, TopLeft, TopLeft, Vector2.zero, new Vector2(400f, 40f));
        ui.pointsText.fontStyle = FontStyles.Bold;

        ui.milestoneRow = NewRect("Milestones", page);
        Place(ui.milestoneRow, TopLeft, TopRight, TopLeft, new Vector2(0f, -50f), new Vector2(0f, 230f));
        Horizontal(ui.milestoneRow, 16f);

        ui.missionList = ScrollList(page, Vector2.zero, new Vector2(0f, -300f));
    }

    // ------------------------------------------------------------------ templates

    private static RectTransform EventButton(RectTransform list)
    {
        RectTransform b = NewRect("EventTemplate", list);
        b.sizeDelta = new Vector2(380f, 100f);
        FixedSize(b, 380f, 100f);

        UnityEngine.UI.Image face = NewImage("Face", b, PanelInk, true);
        Stretch((RectTransform)face.transform);
        UnityEngine.UI.Image frame = NewImage("Frame", b, new Color(1f, 1f, 1f, 0.12f), false);
        Place((RectTransform)frame.transform, BottomLeft, TopLeft, MiddleLeft, Vector2.zero, new Vector2(8f, 0f));

        TextMeshProUGUI title = NewText("Title", b, 28f, Color.white, TextAlignmentOptions.Left);
        Place(title.rectTransform, TopLeft, TopRight, TopLeft, new Vector2(24f, -14f), new Vector2(-40f, 40f));
        title.fontStyle = FontStyles.Bold;
        TextMeshProUGUI detail = NewText("Detail", b, 20f, Dim, TextAlignmentOptions.Left);
        Place(detail.rectTransform, BottomLeft, BottomRight, BottomLeft, new Vector2(24f, 14f), new Vector2(-40f, 30f));

        Button button = b.gameObject.AddComponent<Button>();
        button.targetGraphic = face;
        return b;
    }

    private static RectTransform TabButton(RectTransform row)
    {
        RectTransform t = NewRect("TabTemplate", row);
        FixedSize(t, 200f, 56f);

        UnityEngine.UI.Image face = NewImage("Frame", t, new Color(1f, 1f, 1f, 0.12f), true);
        Stretch((RectTransform)face.transform);
        TextMeshProUGUI label = NewText("Label", t, 22f, Color.white, TextAlignmentOptions.Center);
        Stretch(label.rectTransform);
        label.fontStyle = FontStyles.Bold;
        label.characterSpacing = 3f;

        t.gameObject.AddComponent<Button>().targetGraphic = face;
        return t;
    }

    /// <summary>One list row: icon, title over detail, a right-hand figure, and an action button.</summary>
    private static RectTransform Row(RectTransform parent)
    {
        UnityEngine.UI.Image back = NewImage("RowTemplate", parent, RowTint, false);
        RectTransform row = (RectTransform)back.transform;
        FixedSize(row, 1200f, 88f);

        UnityEngine.UI.Image icon = NewImage("Icon", row, Color.white, false);
        Place(icon.rectTransform, MiddleLeft, MiddleLeft, MiddleLeft, new Vector2(16f, 0f), new Vector2(60f, 60f));
        icon.preserveAspect = true;

        TextMeshProUGUI title = NewText("Title", row, 26f, Color.white, TextAlignmentOptions.Left);
        title.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        title.rectTransform.anchorMax = new Vector2(1f, 1f);
        title.rectTransform.offsetMin = new Vector2(92f, 0f);
        title.rectTransform.offsetMax = new Vector2(-540f, -6f);
        title.fontStyle = FontStyles.Bold;
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.overflowMode = TextOverflowModes.Ellipsis;

        TextMeshProUGUI detail = NewText("Detail", row, 20f, Dim, TextAlignmentOptions.Left);
        detail.rectTransform.anchorMin = new Vector2(0f, 0f);
        detail.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        detail.rectTransform.offsetMin = new Vector2(92f, 6f);
        detail.rectTransform.offsetMax = new Vector2(-540f, 0f);

        TextMeshProUGUI right = NewText("Right", row, 22f, Gold, TextAlignmentOptions.Right);
        Place(right.rectTransform, MiddleRight, MiddleRight, MiddleRight, new Vector2(-270f, 0f), new Vector2(250f, 44f));
        right.fontStyle = FontStyles.Bold;

        TextButton("Action", row, MiddleRight, new Vector2(-16f, 0f), new Vector2(236f, 60f), "CLAIM", Accent, out _);
        return row;
    }

    /// <summary>One reward cell: a header, the item, its amount, and a state or a button.</summary>
    private static RectTransform RewardCell(RectTransform parent)
    {
        UnityEngine.UI.Image back = NewImage("CellTemplate", parent, InfoInk, false);
        RectTransform cell = (RectTransform)back.transform;
        FixedSize(cell, 180f, 230f);

        UnityEngine.UI.Image frame = NewImage("Frame", cell, Dim, false);
        Place((RectTransform)frame.transform, TopLeft, TopRight, TopCenter, Vector2.zero, new Vector2(0f, 6f));

        TextMeshProUGUI header = NewText("Header", cell, 22f, Color.white, TextAlignmentOptions.Center);
        Place(header.rectTransform, TopCenter, TopCenter, TopCenter, new Vector2(0f, -14f), new Vector2(170f, 32f));
        header.fontStyle = FontStyles.Bold;

        UnityEngine.UI.Image icon = NewImage("Icon", cell, Color.white, false);
        Place(icon.rectTransform, Center, Center, Center, new Vector2(0f, 16f), new Vector2(84f, 84f));
        icon.preserveAspect = true;

        TextMeshProUGUI amount = NewText("Amount", cell, 22f, Color.white, TextAlignmentOptions.Center);
        Place(amount.rectTransform, Center, Center, Center, new Vector2(0f, -46f), new Vector2(170f, 32f));

        TextMeshProUGUI state = NewText("State", cell, 18f, Gold, TextAlignmentOptions.Center);
        Place(state.rectTransform, BottomCenter, BottomCenter, BottomCenter, new Vector2(0f, 14f), new Vector2(170f, 28f));
        state.fontStyle = FontStyles.Bold;

        TextButton("Action", cell, BottomCenter, new Vector2(0f, 10f), new Vector2(150f, 44f), "CLAIM", Accent, out _);
        return cell;
    }

    // ------------------------------------------------------------------ Home hook

    /// <summary>
    /// 主页的新闻屏 / Turns HomeUI's NewsPanel into the event entry: two text lines, a Button on the
    /// panel, and the HomeEventTile that fills them. Only added where missing.
    /// </summary>
    private static string HookHome()
    {
        string homePath = UiPrefabDir + "/HomeUI.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(homePath) == null) return "HomeUI: no prefab, not hooked";

        GameObject contents = PrefabUtility.LoadPrefabContents(homePath);
        try
        {
            Transform panel = FindDeep(contents.transform, "NewsPanel");
            if (panel == null) return "HomeUI: no NewsPanel, not hooked";
            if (panel.GetComponent<HomeEventTile>() != null) return "HomeUI NewsPanel: already hooked";

            HomeEventTile tile = panel.gameObject.AddComponent<HomeEventTile>();

            List<GameObject> idle = new List<GameObject>();
            for (int i = 0; i < panel.childCount; i++)
            {
                Transform child = panel.GetChild(i);
                if (child.name.StartsWith("Bar") || child.name == "StandBy") idle.Add(child.gameObject);
            }
            tile.idleOnly = idle.ToArray();

            tile.title = NewText("EventTitle", panel, 26f, Color.white, TextAlignmentOptions.Center);
            Place(tile.title.rectTransform, Center, Center, Center, new Vector2(0f, 2f), new Vector2(236f, 66f));
            tile.title.fontStyle = FontStyles.Bold;
            tile.title.enableAutoSizing = true;
            tile.title.fontSizeMin = 16f;
            tile.title.fontSizeMax = 26f;

            tile.detail = NewText("EventTime", panel, 16f, Gold, TextAlignmentOptions.Center);
            Place(tile.detail.rectTransform, Center, Center, Center, new Vector2(0f, -56f), new Vector2(236f, 24f));
            tile.detail.fontStyle = FontStyles.Bold;

            Graphic graphic = panel.GetComponent<Graphic>();
            Button button = panel.GetComponent<Button>();
            if (button == null) button = panel.gameObject.AddComponent<Button>();
            if (button.targetGraphic == null) button.targetGraphic = graphic;
            tile.button = button;

            PrefabUtility.SaveAsPrefabAsset(contents, homePath);
            return $"HomeUI NewsPanel: hooked ({idle.Count} idle-only children)";
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
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

    private static GameObject NewRoot(string name, System.Type component)
    {
        GameObject root = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup), component);
        Stretch((RectTransform)root.transform);
        return root;
    }

    /// <summary>The back slab and title plate every screen shares, top left.</summary>
    private static Button Header(RectTransform root, string title)
    {
        Button back = SquareButton("BackButton", root, TopLeft, new Vector2(48f, -40f), new Vector2(170f, 68f), "‹", 46f);

        UnityEngine.UI.Image plate = NewImage("Title", root, Strip, false);
        Place((RectTransform)plate.transform, TopLeft, TopLeft, TopLeft, new Vector2(48f + 170f + 8f, -40f), new Vector2(340f, 68f));
        TextMeshProUGUI text = NewText("Text", plate.transform, 28f, Color.white, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        text.fontStyle = FontStyles.Bold;
        text.characterSpacing = 3f;
        text.text = title;
        return back;
    }

    private static GameObject Page(RectTransform pages, string name)
    {
        RectTransform page = NewRect(name, pages);
        Stretch(page);
        page.gameObject.SetActive(false);
        return page.gameObject;
    }

    /// <summary>A vertical list that scrolls: viewport clipped by RectMask2D, content sized to its rows.</summary>
    private static RectTransform ScrollList(Transform parent, Vector2 offsetMin, Vector2 offsetMax)
    {
        RectTransform view = NewRect("Scroll", parent);
        view.anchorMin = Vector2.zero;
        view.anchorMax = Vector2.one;
        view.offsetMin = offsetMin;
        view.offsetMax = offsetMax;
        view.gameObject.AddComponent<RectMask2D>();

        // 透明但能拖 / A transparent graphic so the empty space between rows still takes the drag.
        NewImage("Hit", view, new Color(0f, 0f, 0f, 0f), true).rectTransform.SetAsFirstSibling();
        Stretch((RectTransform)view.Find("Hit"));

        RectTransform content = NewRect("Content", view);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        Vertical(content, 10f);
        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

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
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }

    private static void Horizontal(RectTransform rect, float spacing)
    {
        HorizontalLayoutGroup layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
    }

    /// <summary>
    /// 固定尺寸 / A size layout groups keep: preferred = min = the size, nothing flexible. Without
    /// it a controlling layout group stretches a row's height to fill the list.
    /// </summary>
    private static void FixedSize(RectTransform rect, float width, float height)
    {
        rect.sizeDelta = new Vector2(width, height);
        LayoutElement element = rect.gameObject.AddComponent<LayoutElement>();
        element.minHeight = element.preferredHeight = height;
        element.preferredWidth = width;
        element.flexibleHeight = 0f;
        element.flexibleWidth = 0f;
    }

    private static Button TextButton(string name, RectTransform parent, Vector2 anchor, Vector2 position, Vector2 size,
                                     string label, Color color, out TextMeshProUGUI text)
    {
        UnityEngine.UI.Image face = NewImage(name, parent, color, true);
        RectTransform rect = (RectTransform)face.transform;
        Place(rect, anchor, anchor, anchor, position, size);
        rect.gameObject.AddComponent<CanvasGroup>();
        AddEdges(rect, Edge, 2f);

        text = NewText("Label", rect, size.y >= 70f ? 28f : 22f, Color.white, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        text.fontStyle = FontStyles.Bold;
        text.characterSpacing = 2f;
        text.text = label;

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = face;
        return button;
    }

    private static Button SquareButton(string name, RectTransform parent, Vector2 anchor, Vector2 position,
                                       Vector2 size, string glyph, float glyphSize)
    {
        UnityEngine.UI.Image face = NewImage(name, parent, Slab, true);
        RectTransform rect = (RectTransform)face.transform;
        Place(rect, anchor, anchor, anchor, position, size);
        AddEdges(rect, Edge, 2f);

        TextMeshProUGUI text = NewText("Text", rect, glyphSize, Color.white, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        text.text = glyph;

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = face;
        return button;
    }

    private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
                              Vector2 position, Vector2 size)
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

    private static void Inset(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static void AddEdges(RectTransform parent, Color color, float thickness)
    {
        EdgeLine(parent, "EdgeTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -thickness / 2f), new Vector2(0f, thickness), color);
        EdgeLine(parent, "EdgeBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, thickness / 2f), new Vector2(0f, thickness), color);
        EdgeLine(parent, "EdgeLeft", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(thickness / 2f, 0f), new Vector2(thickness, 0f), color);
        EdgeLine(parent, "EdgeRight", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-thickness / 2f, 0f), new Vector2(thickness, 0f), color);
    }

    private static void EdgeLine(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                             Vector2 position, Vector2 size, Color color)
    {
        UnityEngine.UI.Image edge = NewImage(name, parent, color, false);
        RectTransform rect = (RectTransform)edge.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
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
