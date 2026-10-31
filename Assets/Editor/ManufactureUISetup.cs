using System.Collections.Generic;
using Promuse.Contracts.Manufacture;
using Promuse.Manufacture;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 制造站界面 / Builds the Manufacture screen prefab and hooks Home's MANUFACTURE tile to it.
///
///   Arknights/Manufacture/Build Manufacture UI           - builds what is missing, keeps what exists
///   Arknights/Manufacture/Reset Manufacture UI To Default - builds the screen again from scratch
///
/// 不动手改过的东西 / Build never touches a prefab that already exists: whatever has been placed,
/// recoloured or swapped for real art by hand stays as it is. Only Reset throws that away, and asks
/// first. It writes over the prefab in place rather than deleting it, so the asset keeps its GUID.
/// Hooking the tile only ever adds what is missing to it.
///
/// 布局 / The screen, bottom to top:
///   Room        (child Canvas) wall, windows, floor, five empty chibi slots on the floor
///   Hud         (child Canvas, CanvasGroup) the LMD and EXP station cards, left and right
///   LockOverlay below the top bar: "Clear 1-1 to unlock", "unavailable", a load error and Retry
///   TopBar      Back, the title, the LMD the player holds, Collect all
///   UpgradePanel over everything, top bar included, until it is confirmed or cancelled
/// </summary>
public static class ManufactureUISetup
{
    private const string Tag = "[ManufactureUISetup]";
    private const string UiPrefabDir = "Assets/Arknights/Resources/Prefab/UI";
    private const string TileName = "ManufactureTile";

    private const float TopBarHeight = 120f;

    private static readonly Color Wall = new Color(0.12f, 0.13f, 0.15f, 1f);
    private static readonly Color WindowGlow = new Color(0.30f, 0.42f, 0.52f, 0.35f);
    private static readonly Color Floor = new Color(0.19f, 0.19f, 0.21f, 1f);
    private static readonly Color FloorEdge = new Color(0.36f, 0.37f, 0.40f, 1f);
    private static readonly Color Bar = new Color(0.05f, 0.055f, 0.06f, 0.96f);
    private static readonly Color Card = new Color(0.07f, 0.075f, 0.085f, 0.92f);
    private static readonly Color Track = new Color(1f, 1f, 1f, 0.12f);
    private static readonly Color Accent = new Color(0.10f, 0.55f, 0.80f, 1f);
    private static readonly Color Slab = new Color(0.30f, 0.31f, 0.33f, 1f);
    private static readonly Color Yellow = new Color(0.96f, 0.73f, 0.05f, 1f);
    private static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);
    private static readonly Color Veil = new Color(0f, 0f, 0f, 0.78f);

    private static string PrefabPath => $"{UiPrefabDir}/{ManufactureUI.UIName}.prefab";

    // ================================================================== menus

    [MenuItem("Arknights/Manufacture/Build Manufacture UI", false, 0)]
    public static void Build()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        var done = new List<string>
        {
            BuildIfMissing(),
            HookTile(),
        };

        AssetDatabase.SaveAssets();
        Debug.Log($"{Tag} Manufacture UI:\n  " + string.Join("\n  ", done));
    }

    [MenuItem("Arknights/Manufacture/Reset Manufacture UI To Default", false, 1)]
    public static void ResetToDefault()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        bool sure = Application.isBatchMode || EditorUtility.DisplayDialog(
            "Reset Manufacture UI",
            $"This builds {PrefabPath} again from scratch, over what is there. Anything changed on it by hand - " +
            "positions, colours, art - is lost.",
            "Reset", "Cancel");
        if (!sure)
        {
            Debug.Log($"{Tag} Reset cancelled; nothing changed.");
            return;
        }

        Rebuild();
    }

    /// <summary>
    /// 重建 / Builds the screen again over the existing prefab, keeping its GUID, then hooks the tile.
    /// What Reset does once it has asked; called directly only by automation that has decided to.
    /// </summary>
    public static void Rebuild()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        GameObject root = BuildScreen();
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        string hooked = HookTile();
        AssetDatabase.SaveAssets();
        Debug.Log($"{Tag} Manufacture UI rebuilt from scratch at {PrefabPath}.\n  {hooked}");
    }

    private static string BuildIfMissing()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return $"{ManufactureUI.UIName}: kept as it is";

        GameObject root = BuildScreen();
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return $"{ManufactureUI.UIName}: built at {PrefabPath} - room, two station cards, upgrade panel, lock";
    }

    // ================================================================== screen

    private static GameObject BuildScreen()
    {
        GameObject root = new GameObject(ManufactureUI.UIName, typeof(RectTransform), typeof(CanvasGroup), typeof(ManufactureUI));
        RectTransform rect = (RectTransform)root.transform;
        Stretch(rect);
        ManufactureUI ui = root.GetComponent<ManufactureUI>();

        BuildRoom(rect);

        // 子画布 / The stations tick four times a second; on a child canvas only it rebuilds.
        RectTransform hud = ChildCanvas("Hud", rect);
        ui.stationLayer = hud.gameObject.AddComponent<CanvasGroup>();
        ui.lmdCard = BuildCard(hud, ManufactureStations.Lmd, left: true);
        ui.expCard = BuildCard(hud, ManufactureStations.Exp, left: false);

        BuildLock(rect, ui);
        BuildTopBar(rect, ui);
        ui.upgradePanel = BuildUpgradePanel(rect);

        return root;
    }

    private static void BuildRoom(RectTransform parent)
    {
        // 子画布 / The chibis on the floor animate every frame (M6); they rebuild only this canvas.
        RectTransform room = ChildCanvas("Room", parent);

        // 挡住主页 / Home stays open under every screen: the wall takes a tap that lands on nothing,
        // so it never presses a Home tile behind it - DepotUI's backdrop does the same.
        Stretch(NewImage("Wall", room, Wall, raycast: true).rectTransform);

        Image left = NewImage("WindowLeft", room, WindowGlow, raycast: false);
        Place(left.rectTransform, Center, Center, Center, new Vector2(-170f, 170f), new Vector2(280f, 220f));
        Image right = NewImage("WindowRight", room, WindowGlow, raycast: false);
        Place(right.rectTransform, Center, Center, Center, new Vector2(170f, 170f), new Vector2(280f, 220f));

        Image floor = NewImage("Floor", room, Floor, raycast: false);
        Place(floor.rectTransform, BottomLeft, BottomRight, BottomCenter, Vector2.zero, new Vector2(0f, 300f));
        Image edge = NewImage("FloorEdge", floor.rectTransform, FloorEdge, raycast: false);
        Place(edge.rectTransform, TopLeft, TopRight, TopCenter, Vector2.zero, new Vector2(0f, 6f));

        // 小人站的位置 / Where the chibis will stand, on the floor's edge. Empty until M6 fills them;
        // placed here so they can be moved by hand and a later build keeps them where they were put.
        RectTransform slots = NewRect("ChibiSlots", room);
        Stretch(slots);
        float[] xs = { -300f, -150f, 0f, 150f, 300f };
        for (int i = 0; i < xs.Length; i++)
        {
            RectTransform slot = NewRect("ChibiSlot" + i, slots);
            Place(slot, BottomCenter, BottomCenter, BottomCenter, new Vector2(xs[i], 300f), new Vector2(180f, 260f));
        }
    }

    private static StationCard BuildCard(RectTransform parent, string stationId, bool left)
    {
        Image face = NewImage(stationId == ManufactureStations.Lmd ? "LmdCard" : "ExpCard", parent, Card, raycast: true);
        Vector2 anchor = left ? new Vector2(0f, 0.5f) : new Vector2(1f, 0.5f);
        Place(face.rectTransform, anchor, anchor, anchor, new Vector2(left ? 64f : -64f, -40f), new Vector2(480f, 470f));
        RectTransform card = face.rectTransform;

        StationCard view = face.gameObject.AddComponent<StationCard>();
        view.stationId = stationId;

        Image accent = NewImage("Accent", card, Yellow, raycast: false);
        Place(accent.rectTransform, TopLeft, TopRight, TopCenter, Vector2.zero, new Vector2(0f, 6f));

        view.title = Label(card, "Title", TopLeft, new Vector2(28f, -40f), new Vector2(300f, 44f), 32f, Color.white, TextAlignmentOptions.Left);
        view.title.fontStyle = FontStyles.Bold;
        view.title.text = StationCard.TitleOf(stationId);
        view.level = Label(card, "Level", TopRight, new Vector2(-28f, -40f), new Vector2(180f, 44f), 30f, Yellow, TextAlignmentOptions.Right);

        view.productIcon = NewImage("ProductIcon", card, Color.white, raycast: false);
        view.productIcon.preserveAspect = true;
        Place(view.productIcon.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(28f, -100f), new Vector2(104f, 104f));
        view.productName = Label(card, "ProductName", TopLeft, new Vector2(152f, -108f), new Vector2(300f, 36f), 24f, Dim, TextAlignmentOptions.Left);
        view.stored = Label(card, "Stored", TopLeft, new Vector2(152f, -150f), new Vector2(300f, 64f), 46f, Color.white, TextAlignmentOptions.Left);
        view.stored.fontStyle = FontStyles.Bold;

        Image track = NewImage("Progress", card, Track, raycast: false);
        Place(track.rectTransform, TopLeft, TopRight, TopCenter, new Vector2(0f, -236f), new Vector2(-56f, 16f));
        view.progressFill = NewFill("Fill", track.rectTransform, Accent).rectTransform;

        view.next = Label(card, "Next", TopLeft, new Vector2(28f, -270f), new Vector2(424f, 36f), 26f, Color.white, TextAlignmentOptions.Left);
        view.full = Label(card, "Full", TopLeft, new Vector2(28f, -310f), new Vector2(424f, 36f), 24f, Dim, TextAlignmentOptions.Left);

        view.collectButton = SolidButton("CollectButton", card, BottomLeft, new Vector2(28f, 28f), new Vector2(200f, 72f), "COLLECT", Accent, Color.white);
        view.upgradeButton = SolidButton("UpgradeButton", card, BottomRight, new Vector2(-28f, 28f), new Vector2(200f, 72f), "UPGRADE", Slab, Color.white);

        return view;
    }

    private static void BuildLock(RectTransform parent, ManufactureUI ui)
    {
        // 顶栏露在外面 / Below the top bar, so Back still works while the screen is locked.
        Image veil = NewImage("LockOverlay", parent, Veil, raycast: true);
        RectTransform rect = veil.rectTransform;
        Stretch(rect);
        rect.offsetMax = new Vector2(0f, -TopBarHeight);
        ui.lockOverlay = veil.gameObject;

        ui.lockText = NewText("Message", rect, 40f, Color.white, TextAlignmentOptions.Center);
        Place(ui.lockText.rectTransform, Center, Center, Center, new Vector2(0f, 40f), new Vector2(1200f, 160f));
        ui.lockText.text = "Loading…";

        ui.retryButton = SolidButton("RetryButton", rect, Center, new Vector2(0f, -90f), new Vector2(260f, 72f), "RETRY", Slab, Color.white);
        ui.retryButton.gameObject.SetActive(false);

        veil.gameObject.SetActive(false);
    }

    private static void BuildTopBar(RectTransform parent, ManufactureUI ui)
    {
        Image bar = NewImage("TopBar", parent, Bar, raycast: true);
        Place(bar.rectTransform, TopLeft, TopRight, TopCenter, Vector2.zero, new Vector2(0f, TopBarHeight));
        RectTransform rect = bar.rectTransform;

        // 出口 / The way out, wired in the prefab so Android Back finds it (UIBase.FindExitButton).
        Button back = SolidButton("BackButton", rect, TopLeft, new Vector2(42f, -33f), new Vector2(156f, 55f), "BACK", Color.white, Bar);
        UnityEditor.Events.UnityEventTools.AddBoolPersistentListener(back.onClick, ui.Hide, true);

        TextMeshProUGUI title = Label(rect, "Title", TopLeft, new Vector2(230f, -30f), new Vector2(600f, 60f), 44f, Color.white, TextAlignmentOptions.Left);
        title.fontStyle = FontStyles.Bold;
        title.text = "MANUFACTURE";

        ui.collectAllButton = SolidButton("CollectAllButton", rect, TopRight, new Vector2(-42f, -27f), new Vector2(300f, 66f), "COLLECT ALL", Accent, Color.white);

        Image pill = NewImage("Lmd", rect, new Color(1f, 1f, 1f, 0.08f), raycast: false);
        Place(pill.rectTransform, TopRight, TopRight, TopRight, new Vector2(-370f, -33f), new Vector2(300f, 55f));
        ui.lmdIcon = NewImage("Icon", pill.rectTransform, Color.white, raycast: false);
        ui.lmdIcon.preserveAspect = true;
        Place(ui.lmdIcon.rectTransform, MiddleLeft, MiddleLeft, MiddleLeft, new Vector2(10f, 0f), new Vector2(44f, 44f));
        ui.lmdText = Label(pill.rectTransform, "Amount", MiddleRight, new Vector2(-16f, 0f), new Vector2(220f, 44f), 30f, Color.white, TextAlignmentOptions.Right);
    }

    private static UpgradePanel BuildUpgradePanel(RectTransform parent)
    {
        // 盖住一切 / Over everything, the top bar too: it is confirmed or cancelled - or Android
        // Back closes it - before anything else on the screen can be pressed.
        Image veil = NewImage("UpgradePanel", parent, Veil, raycast: true);
        Stretch(veil.rectTransform);
        UpgradePanel panel = veil.gameObject.AddComponent<UpgradePanel>();

        Image box = NewImage("Box", veil.rectTransform, new Color(Card.r, Card.g, Card.b, 1f), raycast: true);
        Place(box.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(1000f, 660f));
        RectTransform rect = box.rectTransform;

        Image accent = NewImage("Accent", rect, Yellow, raycast: false);
        Place(accent.rectTransform, TopLeft, TopRight, TopCenter, Vector2.zero, new Vector2(0f, 6f));

        panel.title = Label(rect, "Title", TopLeft, new Vector2(40f, -56f), new Vector2(920f, 50f), 38f, Color.white, TextAlignmentOptions.Left);
        panel.title.fontStyle = FontStyles.Bold;
        panel.product = Label(rect, "Product", TopLeft, new Vector2(40f, -124f), new Vector2(920f, 40f), 28f, Dim, TextAlignmentOptions.Left);
        panel.pace = Label(rect, "Pace", TopLeft, new Vector2(40f, -184f), new Vector2(920f, 40f), 30f, Color.white, TextAlignmentOptions.Left);
        panel.holds = Label(rect, "Holds", TopLeft, new Vector2(40f, -234f), new Vector2(920f, 40f), 30f, Color.white, TextAlignmentOptions.Left);
        panel.blueprint = Label(rect, "Blueprint", TopLeft, new Vector2(40f, -304f), new Vector2(920f, 40f), 28f, Color.white, TextAlignmentOptions.Left);
        panel.cost = Label(rect, "Cost", TopLeft, new Vector2(40f, -364f), new Vector2(920f, 70f), 28f, Color.white, TextAlignmentOptions.TopLeft);
        panel.note = Label(rect, "Note", TopLeft, new Vector2(40f, -450f), new Vector2(920f, 36f), 22f, Dim, TextAlignmentOptions.Left);

        panel.confirmButton = SolidButton("ConfirmButton", rect, BottomRight, new Vector2(-40f, 40f), new Vector2(280f, 76f), "UPGRADE", Accent, Color.white);
        panel.cancelButton = SolidButton("CancelButton", rect, BottomLeft, new Vector2(40f, 40f), new Vector2(240f, 76f), "CANCEL", Slab, Color.white);

        veil.gameObject.SetActive(false);
        return panel;
    }

    // ================================================================== Home tile

    /// <summary>
    /// 只加缺的 / Points Home's MANUFACTURE tile at the screen, adding only what the tile lacks: a
    /// hit area if it draws nothing, a Button, an OpenUIButton naming the screen.
    /// </summary>
    private static string HookTile()
    {
        string homePath = UiPrefabDir + "/HomeUI.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(homePath) == null)
        {
            Debug.LogWarning($"{Tag} No HomeUI prefab at {homePath}, so nothing opens Manufacture yet.");
            return $"HomeUI {TileName}: NOT hooked - no HomeUI prefab";
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(homePath);
        try
        {
            Transform tile = FindDeep(contents.transform, TileName);
            if (tile == null)
            {
                Debug.LogWarning($"{Tag} HomeUI has no '{TileName}' child.");
                return $"HomeUI {TileName}: NOT hooked - no such tile";
            }

            // 透明图也能接收点击 / An alpha-zero Image is still a raycast target: a tile that draws
            // nothing of its own gets one, so it can be pressed without covering the art behind it.
            if (tile.GetComponentInChildren<Graphic>() == null) NewImage("Hit", tile, new Color(0f, 0f, 0f, 0f), raycast: true);

            Button button = tile.GetComponent<Button>();
            if (button == null) button = tile.gameObject.AddComponent<Button>();
            if (button.targetGraphic == null) button.targetGraphic = tile.GetComponentInChildren<Graphic>();

            OpenUIButton open = tile.GetComponent<OpenUIButton>();
            bool added = open == null;
            if (added) open = tile.gameObject.AddComponent<OpenUIButton>();
            bool changed = added || open.uiName != ManufactureUI.UIName;
            open.uiName = ManufactureUI.UIName;

            PrefabUtility.SaveAsPrefabAsset(contents, homePath);
            return $"HomeUI {TileName}: {(changed ? "now opens" : "already opened")} {ManufactureUI.UIName}";
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // ================================================================== helpers

    private static readonly Vector2 TopLeft = new Vector2(0f, 1f);
    private static readonly Vector2 TopCenter = new Vector2(0.5f, 1f);
    private static readonly Vector2 TopRight = new Vector2(1f, 1f);
    private static readonly Vector2 MiddleLeft = new Vector2(0f, 0.5f);
    private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
    private static readonly Vector2 MiddleRight = new Vector2(1f, 0.5f);
    private static readonly Vector2 BottomLeft = new Vector2(0f, 0f);
    private static readonly Vector2 BottomCenter = new Vector2(0.5f, 0f);
    private static readonly Vector2 BottomRight = new Vector2(1f, 0f);

    /// <summary>A full-screen child with a Canvas and a GraphicRaycaster of its own - a nested canvas needs both to be pressed.</summary>
    private static RectTransform ChildCanvas(string name, Transform parent)
    {
        RectTransform rect = NewRect(name, parent);
        Stretch(rect);
        rect.gameObject.AddComponent<Canvas>();
        rect.gameObject.AddComponent<GraphicRaycaster>();
        return rect;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static Image NewImage(string name, Transform parent, Color color, bool raycast)
    {
        Image image = NewRect(name, parent).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycast;
        return image;
    }

    /// <summary>A progress fill driven by its right anchor - StationCard.SetFill - so it needs no sprite.</summary>
    private static Image NewFill(string name, RectTransform parent, Color color)
    {
        Image fill = NewImage(name, parent, color, raycast: false);
        RectTransform rect = fill.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(0f, 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0f, 0.5f);
        return fill;
    }

    private static TextMeshProUGUI NewText(string name, Transform parent, float size, Color color, TextAlignmentOptions alignment)
    {
        TextMeshProUGUI text = NewRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.richText = false;
        text.text = "";
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        return text;
    }

    private static TextMeshProUGUI Label(RectTransform parent, string name, Vector2 corner, Vector2 position, Vector2 size,
                                         float fontSize, Color color, TextAlignmentOptions alignment)
    {
        TextMeshProUGUI text = NewText(name, parent, fontSize, color, alignment);
        Place(text.rectTransform, corner, corner, corner, position, size);
        return text;
    }

    private static Button SolidButton(string name, RectTransform parent, Vector2 corner, Vector2 position, Vector2 size,
                                      string label, Color face, Color ink)
    {
        Image image = NewImage(name, parent, face, raycast: true);
        Place(image.rectTransform, corner, corner, corner, position, size);

        TextMeshProUGUI text = NewText("Label", image.rectTransform, 28f, ink, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        text.fontStyle = FontStyles.Bold;
        text.text = label;

        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        return button;
    }

    private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
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
