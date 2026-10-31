using System.Collections.Generic;
using Promuse.Contracts.Manufacture;
using Promuse.Manufacture;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 制造站界面 / Builds the Manufacture screen prefab and the room's lines, and hooks Home's
/// MANUFACTURE tile to the screen.
///
///   Arknights/Manufacture/Build Manufacture UI           - builds what is missing, keeps what exists
///   Arknights/Manufacture/Reset Manufacture UI To Default - builds the screen again from scratch
///
/// 不动手改过的东西 / Build never changes what is already there: whatever has been placed, recoloured
/// or swapped for real art by hand stays as it is. On a prefab that exists it only adds the parts a
/// later version of the screen has and this one lacks - the room's operators, the ROOM button, the
/// room picker - and saves nothing when nothing is missing. Only Reset throws the prefab away, and asks
/// first; it writes over the prefab in place, so the asset keeps its GUID. The lines asset is created
/// once and never written again by either. Hooking the tile only ever adds what is missing to it.
///
/// 布局 / The screen, bottom to top:
///   Room        (child Canvas, ManufactureRoom) wall, windows, floor, five slots the operators stand in
///   Hud         (child Canvas, CanvasGroup) the LMD and EXP station cards, left and right
///   LockOverlay below the top bar: "Clear 1-1 to unlock", "unavailable", a load error and Retry
///   TopBar      Back, the title, ROOM, the LMD the player holds, Collect all
///   UpgradePanel over everything, top bar included, until it is confirmed or cancelled
///   RoomPicker  over everything too: who stands in the room, up to five
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
            BuildOrComplete(),
            EnsureLines(),
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

        string lines = EnsureLines();
        string hooked = HookTile();
        AssetDatabase.SaveAssets();
        Debug.Log($"{Tag} Manufacture UI rebuilt from scratch at {PrefabPath}.\n  {lines}\n  {hooked}");
    }

    private static string BuildOrComplete()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            GameObject root = BuildScreen();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return $"{ManufactureUI.UIName}: built at {PrefabPath} - room and its operators, two station cards, " +
                   "upgrade panel, room picker, lock";
        }

        // 只补缺的 / An existing screen keeps everything it has; only parts it lacks are added, and it is
        // saved only when something was.
        GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            List<string> added = Complete(contents);
            if (added.Count == 0) return $"{ManufactureUI.UIName}: kept as it is";

            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            return $"{ManufactureUI.UIName}: kept, and added what it lacked - " + string.Join(", ", added);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
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

        // 后来加的部分 / The parts added since the first version, built the way Build adds them to an
        // older prefab, so a new screen and a completed one come out the same.
        Complete(root);

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

        // 小人站的位置 / Where the operators stand, on the floor's edge. Placed here so they can be moved
        // by hand, and a later build keeps them where they were put.
        RectTransform slots = NewRect("ChibiSlots", room);
        Stretch(slots);
        for (int i = 0; i < SlotXs.Length; i++)
        {
            RectTransform slot = NewRect("ChibiSlot" + i, slots);
            Place(slot, BottomCenter, BottomCenter, BottomCenter, new Vector2(SlotXs[i], 300f), SlotSize);
        }
    }

    private static readonly float[] SlotXs = { -300f, -150f, 0f, 150f, 300f };
    private static readonly Vector2 SlotSize = new Vector2(180f, 260f);

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

    // ================================================================== later parts

    private const string MaterialPath = "Assets/Plugins/Spine/Runtime/spine-unity/Materials/SkeletonGraphicDefault.mat";

    private static readonly Color BubbleInk = new Color(0.04f, 0.045f, 0.05f, 0.92f);
    private static readonly Color Picked = new Color(0.96f, 0.73f, 0.05f, 0.28f);
    private static readonly Color Refused = new Color(0.92f, 0.30f, 0.26f, 1f);

    /// <summary>
    /// 补上后来的部分 / Adds each part a later version of the screen has and this one lacks - the room's
    /// operators, the ROOM button, the room picker - leaving everything there as it is. Returns what it
    /// added: nothing, on a screen that has it all.
    /// </summary>
    private static List<string> Complete(GameObject root)
    {
        var added = new List<string>();
        ManufactureUI ui = root.GetComponent<ManufactureUI>();
        RectTransform rect = (RectTransform)root.transform;

        EnsureRoom(rect, ui, added);
        EnsureRoomButton(rect, ui, added);
        EnsureRoomPicker(rect, ui, added);
        return added;
    }

    private static void EnsureRoom(RectTransform root, ManufactureUI ui, List<string> added)
    {
        RectTransform room = root.Find("Room") as RectTransform;
        if (room == null)
        {
            Debug.LogWarning($"{Tag} {ManufactureUI.UIName} has no Room; Reset Manufacture UI To Default builds one.");
            return;
        }

        ManufactureRoom floor = room.GetComponent<ManufactureRoom>();
        if (floor == null)
        {
            floor = room.gameObject.AddComponent<ManufactureRoom>();
            added.Add("the room's operators");
        }
        if (ui.room == null) ui.room = floor;
        if (floor.graphicMaterial == null) floor.graphicMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

        RectTransform slots = room.Find("ChibiSlots") as RectTransform;
        if (slots == null)
        {
            slots = NewRect("ChibiSlots", room);
            Stretch(slots);
        }

        var chibis = new List<ManufactureChibi>();
        for (int i = 0; i < SlotXs.Length; i++)
        {
            RectTransform slot = slots.Find("ChibiSlot" + i) as RectTransform;
            if (slot == null)
            {
                slot = NewRect("ChibiSlot" + i, slots);
                Place(slot, BottomCenter, BottomCenter, BottomCenter, new Vector2(SlotXs[i], 300f), SlotSize);
            }
            chibis.Add(EnsureChibi(slot));
        }

        // 没绑才绑 / Bound only where nothing is: slots reordered or dropped by hand stay that way.
        if (floor.slots == null || floor.slots.Length == 0) floor.slots = chibis.ToArray();
    }

    /// <summary>A slot's tap area, the figure the operator stands in and the bubble over it, each only if missing.</summary>
    private static ManufactureChibi EnsureChibi(RectTransform slot)
    {
        ManufactureChibi chibi = slot.GetComponent<ManufactureChibi>();
        if (chibi == null) chibi = slot.gameObject.AddComponent<ManufactureChibi>();

        // 透明的点击区 / Transparent, and a raycast target only while someone stands here - ManufactureChibi
        // switches it - so an empty slot never takes a tap from the room behind it.
        UnityEngine.UI.Image hit = slot.GetComponent<UnityEngine.UI.Image>();
        if (hit == null)
        {
            hit = slot.gameObject.AddComponent<UnityEngine.UI.Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = false;
        }
        if (chibi.hitArea == null) chibi.hitArea = hit;

        RectTransform figure = slot.Find("Figure") as RectTransform;
        if (figure == null)
        {
            figure = NewRect("Figure", slot);
            Place(figure, BottomCenter, BottomCenter, BottomCenter, Vector2.zero, SlotSize);
        }
        if (chibi.figure == null) chibi.figure = figure;

        Transform bubble = slot.Find("Bubble");
        if (bubble == null)
        {
            UnityEngine.UI.Image box = NewImage("Bubble", slot, BubbleInk, raycast: false);
            Place(box.rectTransform, TopCenter, TopCenter, BottomCenter, new Vector2(0f, 8f), new Vector2(360f, 100f));
            TextMeshProUGUI said = NewText("Text", box.rectTransform, 22f, Color.white, TextAlignmentOptions.Center);
            Stretch(said.rectTransform);
            said.rectTransform.offsetMin = new Vector2(14f, 8f);
            said.rectTransform.offsetMax = new Vector2(-14f, -8f);

            // 长句缩小 / A long line shrinks to fit rather than spilling out of the box.
            said.enableAutoSizing = true;
            said.fontSizeMin = 16f;
            said.fontSizeMax = 22f;
            box.gameObject.SetActive(false);
            bubble = box.transform;
        }
        if (chibi.bubble == null) chibi.bubble = bubble.gameObject;
        if (chibi.bubbleText == null) chibi.bubbleText = bubble.GetComponentInChildren<TextMeshProUGUI>(true);

        return chibi;
    }

    private static void EnsureRoomButton(RectTransform root, ManufactureUI ui, List<string> added)
    {
        RectTransform bar = root.Find("TopBar") as RectTransform;
        if (bar == null) return;

        Transform button = bar.Find("RoomButton");
        if (button == null)
        {
            // 在 LMD 左边 / Left of the LMD the player holds, and clear of the title down to 4:3.
            button = SolidButton("RoomButton", bar, TopRight, new Vector2(-680f, -33f), new Vector2(150f, 55f),
                                 "ROOM", Slab, Color.white).transform;
            added.Add("the ROOM button");
        }
        if (ui.roomButton == null) ui.roomButton = button.GetComponent<UnityEngine.UI.Button>();
    }

    private static void EnsureRoomPicker(RectTransform root, ManufactureUI ui, List<string> added)
    {
        Transform picker = root.Find("RoomPicker");
        if (picker == null)
        {
            // 加在最后 / Added last, so it draws over everything, the top bar and the upgrade panel too.
            picker = BuildRoomPicker(root).transform;
            added.Add("the room picker");
        }
        if (ui.roomPicker == null) ui.roomPicker = picker.GetComponent<RoomPicker>();
    }

    private static RoomPicker BuildRoomPicker(RectTransform parent)
    {
        UnityEngine.UI.Image veil = NewImage("RoomPicker", parent, Veil, raycast: true);
        Stretch(veil.rectTransform);
        RoomPicker picker = veil.gameObject.AddComponent<RoomPicker>();

        UnityEngine.UI.Image box = NewImage("Box", veil.rectTransform, new Color(Card.r, Card.g, Card.b, 1f), raycast: true);
        Place(box.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(1100f, 780f));
        RectTransform rect = box.rectTransform;

        UnityEngine.UI.Image accent = NewImage("Accent", rect, Yellow, raycast: false);
        Place(accent.rectTransform, TopLeft, TopRight, TopCenter, Vector2.zero, new Vector2(0f, 6f));

        TextMeshProUGUI title = Label(rect, "Title", TopLeft, new Vector2(40f, -56f), new Vector2(600f, 50f), 38f, Color.white, TextAlignmentOptions.Left);
        title.fontStyle = FontStyles.Bold;
        title.text = "ROOM";
        TextMeshProUGUI about = Label(rect, "About", TopLeft, new Vector2(40f, -112f), new Vector2(1020f, 36f), 24f, Dim, TextAlignmentOptions.Left);
        about.text = $"Choose up to {RoomSelection.Capacity} operators to stand in the room. Tap again to take one out.";
        picker.count = Label(rect, "Count", TopRight, new Vector2(-40f, -56f), new Vector2(200f, 50f), 34f, Yellow, TextAlignmentOptions.Right);

        // 网格 / The owned operators, five to a row, two rows before it runs out of room.
        RectTransform grid = NewRect("Grid", rect);
        Place(grid, TopCenter, TopCenter, TopCenter, new Vector2(0f, -176f), new Vector2(1020f, 384f));
        UnityEngine.UI.GridLayoutGroup layout = grid.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
        layout.cellSize = new Vector2(180f, 180f);
        layout.spacing = new Vector2(24f, 24f);
        layout.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = RoomSelection.Capacity;
        layout.childAlignment = TextAnchor.UpperCenter;
        picker.grid = grid;
        picker.tileTemplate = BuildPickerTile(grid);

        picker.hint = Label(rect, "Hint", BottomCenter, new Vector2(0f, 140f), new Vector2(1020f, 40f), 24f, Refused, TextAlignmentOptions.Center);
        picker.hint.gameObject.SetActive(false);

        picker.confirmButton = SolidButton("ConfirmButton", rect, BottomRight, new Vector2(-40f, 40f), new Vector2(280f, 76f), "CONFIRM", Accent, Color.white);
        picker.cancelButton = SolidButton("CancelButton", rect, BottomLeft, new Vector2(40f, 40f), new Vector2(240f, 76f), "CANCEL", Slab, Color.white);

        veil.gameObject.SetActive(false);
        return picker;
    }

    /// <summary>The tile copied for each owned operator: the portrait, and when chosen a tint and its place, 1 to 5.</summary>
    private static RectTransform BuildPickerTile(RectTransform grid)
    {
        UnityEngine.UI.Image frame = NewImage("TileTemplate", grid, Slab, raycast: true);
        RectTransform tile = frame.rectTransform;
        UnityEngine.UI.Button button = frame.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = frame;

        RectTransform mask = NewRect("Mask", tile);
        Stretch(mask);
        mask.offsetMin = new Vector2(6f, 6f);
        mask.offsetMax = new Vector2(-6f, -6f);
        mask.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        UnityEngine.UI.Image portrait = NewImage("Portrait", mask, Color.white, raycast: false);
        Stretch(portrait.rectTransform);
        portrait.preserveAspect = true;

        UnityEngine.UI.Image selected = NewImage("Selected", tile, Picked, raycast: false);
        Stretch(selected.rectTransform);
        UnityEngine.UI.Image badge = NewImage("Badge", selected.rectTransform, Yellow, raycast: false);
        Place(badge.rectTransform, TopRight, TopRight, TopRight, new Vector2(-6f, -6f), new Vector2(44f, 44f));
        TextMeshProUGUI order = Label(selected.rectTransform, "Order", TopRight, new Vector2(-6f, -6f), new Vector2(44f, 44f), 28f, Bar, TextAlignmentOptions.Center);
        order.fontStyle = FontStyles.Bold;
        selected.gameObject.SetActive(false);

        return tile;
    }

    // ================================================================== lines

    private const string LinesDir = "Assets/Manufacture/Resources/" + ManufactureLines.LoadPath;
    private static string LinesPath => $"{LinesDir}/{ManufactureLines.AssetName}.asset";

    /// <summary>
    /// 只写第一次 / The room's lines, created with their first words when missing and never written again,
    /// so lines edited in the Inspector stay. English, three for each operator and three for anyone else.
    /// </summary>
    private static string EnsureLines()
    {
        if (AssetDatabase.LoadAssetAtPath<ManufactureLines>(LinesPath) != null) return "ManufactureLines: kept as they are";

        EnsureFolder(LinesDir);
        ManufactureLines lines = ScriptableObject.CreateInstance<ManufactureLines>();
        lines.entries = new List<ManufactureLines.Entry>
        {
            Lines("AMIYA",
                  "The line is running smoothly. Thank you for checking in, Doctor.",
                  "Every record we make here helps someone train. Let's keep going.",
                  "Take a break if you need one. I'll keep an eye on the stations."),
            Lines("NOVA",
                  "Another batch done! I could do this all day.",
                  "The machine keeps its own tempo. I'm learning to follow it.",
                  "Collect it before it fills up, okay? I hate seeing it stop."),
            Lines("ECHO",
                  "...The press hums in four-four. Did you notice?",
                  "I counted the units. Twice. They're all there.",
                  "It's quiet in here. I like it."),
            Lines("PULSE",
                  "Keep the beat, keep the line moving. Same thing, really.",
                  "Hey, Doctor! The press sounds great on the off-beat.",
                  "Raise the station a level and I'll make it sing."),
        };
        lines.fallback = new[]
        {
            "Everything's running on time.",
            "Need something, Doctor?",
            "The stations won't run themselves. Well, they do, actually.",
        };

        AssetDatabase.CreateAsset(lines, LinesPath);
        return $"ManufactureLines: created at {LinesPath} - {lines.entries.Count} operators with 3 lines each, " +
               $"and {lines.fallback.Length} for anyone else";
    }

    private static ManufactureLines.Entry Lines(string charId, params string[] said)
    {
        return new ManufactureLines.Entry { charId = charId, lines = said };
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
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
