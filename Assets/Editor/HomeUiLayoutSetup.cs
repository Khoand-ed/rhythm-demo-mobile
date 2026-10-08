using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Two edits to the hand-authored HomeUI prefab, kept as tools rather than done
// by hand so they are reviewable and can be re-run after the prefab is touched:
// which widgets the parallax moves, and whether the menu tiles are
// parallelograms or rectangles.
//
// The motion itself lives in HomeUI.lua.txt; this only decides what sits under
// the two transforms that script drives.
//
// 近景再分两块 / Inside move2 the near widgets are split into RightPanel (the
// menu tiles, the currency row and the clock) and LeftPanel (the news panel,
// and whatever is added beside it later). Each panel is a plain container you
// tilt by hand in the Inspector, so a whole side leans as one rigid plane about
// one pivot. Tilting the tiles one at a time could never look right: each would
// turn about its own centre and they would stop lining up as a surface.
//
// 面板的旋转不会被覆盖 / HomeUI.lua.txt writes move2.localRotation every frame,
// so a tilt set on move2 itself would be erased at runtime. The panels sit one
// level below and the Lua never touches them, so the hand-set tilt composes with
// the pointer-driven one instead of being replaced by it.
public static class HomeUiLayoutSetup
{
    private const string HomePrefabPath = "Assets/Arknights/Resources/Prefab/UI/HomeUI.prefab";
    private const string SkewSpriteName = "T_SkewTile";

    private const string Move1 = "move1";
    private const string Move2 = "move2";
    private const string RightPanel = "RightPanel";
    private const string LeftPanel = "LeftPanel";
    private const string Background = "BackGround";

    // 不动的 / Stays put: the top-left utility icons and everything belonging to
    // the player plate. These are the screen's fixed reference points, so the
    // motion around them has something to read against.
    private static readonly string[] StaticPlate =
        { "PlayerPlate", "expTrack", "expPercentage", "playerLevel", "levelCaption", "playerName" };

    private static readonly string[] StaticIcons =
        { "SettingsIcon", "AlertIcon", "MailIcon", "CalendarIcon" };

    // move1: 背景装饰, 反向小幅 / Background decor, drifting slightly against the
    // near layer. Small amounts - this is the far plane.
    private static readonly string[] FarGroup =
        { "Beam1", "Beam2", "Beam3", "Strut1", "Strut2" };

    // move2: 其余全部 / Everything else - the menu tiles the player actually aims
    // at, plus the clock, the currency row and the news panel. This is the near
    // plane and carries the bulk of the movement.
    //
    // 右边一块 / RightPanel: everything on the right half. The currency row and the
    // clock lean with the tiles rather than staying flat above them, by choice -
    // the whole corner reads as one surface. Listed in stacking order.
    private static readonly string[] RightGroup =
    {
        "time", "dragonCoinSlot", "syntheticJadeSlot", "sourceStoneSlot",
        "OperationTile", "SquadTile", "OperatorsTile", "StoreTile", "RecruitTile",
        "PublicRecruitTile", "HeadhuntTile", "MissionsTile", "ManufactureTile", "DepotTile"
    };

    // 左边一块 / LeftPanel: the news panel, with room for buttons added beside it.
    // Anything put in here later inherits the panel's tilt rather than needing its own.
    private static readonly string[] LeftGroup = { "NewsPanel" };

    private static readonly string[] NearGroup = Concat(RightGroup, LeftGroup);

    [MenuItem("Arknights/Home/Apply Parallax Groups")]
    public static void ApplyParallaxGroups()
    {
        if (Application.isPlaying)
        {
            Debug.LogError("Exit Play Mode first.");
            return;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(HomePrefabPath);

        try
        {
            RectTransform root = (RectTransform)contents.transform;
            RectTransform move1 = Find(root, Move1);
            RectTransform move2 = Find(root, Move2);

            if (move1 == null || move2 == null)
            {
                Debug.LogError($"HomeUI has no '{Move1}'/'{Move2}' transform, so there is nothing for " +
                               "HomeUI.lua.txt to drive.");
                return;
            }

            // 容器不能吃点击 / Both containers carry a full-bleed Image with no sprite.
            // At alpha 0 it is invisible but still a raycast target, so once the
            // container is drawn above the tiles it swallows every click aimed at
            // them. A container is never the thing being clicked.
            StopEatingClicks(move1);
            StopEatingClicks(move2);

            // Everything that should not move goes back to the root first, so a
            // re-run can undo an earlier grouping as well as build a new one.
            foreach (string name in StaticPlate) Reparent(Find(root, name), root, root);
            foreach (string name in StaticIcons) Reparent(Find(root, name), root, root);

            move1.anchoredPosition = Vector2.zero;
            move1.localRotation = Quaternion.identity;
            foreach (string name in FarGroup) Reparent(Find(root, name), move1, root);

            // move2 tilts, so its rect has to sit in the middle of what it holds -
            // a rotation about the centre of the screen would swing the far tiles
            // through an arc instead of leaning them.
            Vector2 centre = GroupCentre(root, NearGroup, out Vector2 size);
            Recentre(move2, centre, root);
            move2.sizeDelta = size;
            move2.localRotation = Quaternion.identity;

            // 两块面板 / The two hand-tilted panels. Found if they exist and left exactly
            // as they are - their rotation is the one thing in here that is set by hand.
            RectTransform right = FindOrCreatePanel(move2, RightPanel, RightGroup, root, out bool rightMade);
            if (right != null) foreach (string name in RightGroup) Reparent(Find(root, name), right, root);

            RectTransform left = FindOrCreateLeftPanel(move2, root, out bool leftMade);
            if (left != null) foreach (string name in LeftGroup) Reparent(Find(root, name), left, root);

            // 新闻面板原本在最上层 / The news panel was authored on top, so its panel stays last.
            if (right != null) right.SetSiblingIndex(0);
            if (left != null) left.SetAsLastSibling();

            // 还原原来的层次 / Restore the stacking the prefab was authored with:
            // decor behind, then the plate, then the icons, then everything else.
            int order = Find(root, Background) != null ? 1 : 0;
            move1.SetSiblingIndex(order++);
            foreach (string name in StaticPlate) SetOrder(Find(root, name), order++);
            foreach (string name in StaticIcons) SetOrder(Find(root, name), order++);
            move2.SetSiblingIndex(order);

            PrefabUtility.SaveAsPrefabAsset(contents, HomePrefabPath);

            Debug.Log($"[HomeUiLayoutSetup] HomeUI parallax groups applied.\n" +
                      $"  still: {string.Join(", ", StaticIcons)} + {string.Join(", ", StaticPlate)}\n" +
                      $"  {Move1} (far, drifts against): {string.Join(", ", FarGroup)}\n" +
                      $"  {Move2} (near, pivot {centre} size {size}):\n" +
                      $"    {RightPanel} ({(rightMade ? "created" : "kept")}): {string.Join(", ", RightGroup)}\n" +
                      $"    {LeftPanel} ({(leftMade ? "created" : "kept")}): {string.Join(", ", LeftGroup)}\n" +
                      "  Tilt a side by rotating its panel. Pointer amounts and the idle return live in HomeUI.lua.txt.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    /// <summary>
    /// 把平行四边形按钮改成矩形 / Drops the skewed sprite off the menu tiles so they
    /// render as plain rectangles. One-way: re-assign T_SkewTile by hand to undo it.
    /// </summary>
    [MenuItem("Arknights/Home/Square Off Tiles")]
    public static void SquareOffTiles()
    {
        if (Application.isPlaying)
        {
            Debug.LogError("Exit Play Mode first.");
            return;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(HomePrefabPath);

        try
        {
            List<string> squared = new List<string>();

            foreach (Image image in contents.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite == null || image.sprite.name != SkewSpriteName) continue;

                image.sprite = null;
                image.type = Image.Type.Simple;
                squared.Add(image.name);
            }

            if (squared.Count == 0)
            {
                Debug.Log($"No '{SkewSpriteName}' images left on HomeUI - the tiles are already rectangles.");
                return;
            }

            PrefabUtility.SaveAsPrefabAsset(contents, HomePrefabPath);
            Debug.Log($"Squared off {squared.Count} tile(s): {string.Join(", ", squared)}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static void StopEatingClicks(RectTransform container)
    {
        Graphic graphic = container.GetComponent<Graphic>();
        if (graphic == null || !graphic.raycastTarget) return;

        graphic.raycastTarget = false;
        Debug.Log($"{container.name} had a {graphic.GetType().Name} of " +
                  $"{container.sizeDelta} still taking raycasts; turned that off.");
    }

    private static void SetOrder(RectTransform node, int index)
    {
        if (node != null) node.SetSiblingIndex(index);
    }

    // Bounding box of a set of widgets, measured from the screen centre.
    private static Vector2 GroupCentre(RectTransform root, string[] names, out Vector2 size)
    {
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        bool any = false;

        foreach (string name in names)
        {
            RectTransform node = Find(root, name);
            if (node == null) continue;

            Vector2 position = PositionUnderRoot(node, root);
            Vector2 half = node.sizeDelta * 0.5f;

            min = Vector2.Min(min, position - half);
            max = Vector2.Max(max, position + half);
            any = true;
        }

        if (!any)
        {
            size = Vector2.zero;
            return Vector2.zero;
        }

        size = max - min;
        return (min + max) * 0.5f;
    }

    /// <summary>
    /// Where a node sits in the layout, relative to the screen centre. Every rect in HomeUI is
    /// centre-anchored, so the offsets simply add up the chain - which makes moving a widget
    /// between parents a matter of subtracting the new parent's own offset.
    ///
    /// 这是布局坐标, 不是世界坐标 / Layout position, deliberately not world position: rotation
    /// is ignored. The panels are tilted by hand, and that tilt is presentation layered on top
    /// of the layout. Placing a widget into a tilted panel therefore puts it at its designed
    /// spot within the tilt, so it leans with its neighbours - which is the whole point of the
    /// panel. A world-space reparent would instead counter-rotate it and leave it standing flat.
    /// </summary>
    private static Vector2 PositionUnderRoot(RectTransform node, Transform root)
    {
        Vector2 total = Vector2.zero;

        for (Transform t = node; t != null && t != root; t = t.parent)
        {
            total += ((RectTransform)t).anchoredPosition;
        }

        return total;
    }

    private static void Reparent(RectTransform node, RectTransform target, RectTransform root)
    {
        // 已经在位就不动 / Already where it belongs: touch nothing, not even its sibling index,
        // so a re-run never restacks or nudges something adjusted by hand.
        if (node == null || node == target || node.parent == target) return;

        Vector2 position = PositionUnderRoot(node, root);
        Vector2 targetPosition = target == root ? Vector2.zero : PositionUnderRoot(target, root);

        node.SetParent(target, false);
        node.anchoredPosition = position - targetPosition;
    }

    /// <summary>
    /// 挪容器但不挪内容 / Moves a container's centre without moving anything it holds: the
    /// children are shifted back by the same amount. Without this, re-centring move2 after a
    /// widget is added to a group would drag every panel and tile along with it.
    /// </summary>
    private static void Recentre(RectTransform container, Vector2 centre, RectTransform root)
    {
        Vector2 delta = centre - PositionUnderRoot(container, root);
        if (delta.sqrMagnitude < 0.0001f) return;

        container.anchoredPosition += delta;

        for (int i = 0; i < container.childCount; i++)
        {
            if (container.GetChild(i) is RectTransform child) child.anchoredPosition -= delta;
        }
    }

    /// <summary>
    /// A bare container: a RectTransform and nothing else. Deliberately no Graphic, so it can
    /// never become the invisible raycast target StopEatingClicks has to clean up after.
    /// </summary>
    private static RectTransform NewContainer(string name, RectTransform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;

        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        return rect;
    }

    /// <summary>
    /// Finds a panel or builds it around the bounding box of its members, pivot in the middle,
    /// so a tilt turns the group about its own centre rather than the screen's.
    ///
    /// 已存在就原样保留 / An existing panel is returned untouched - position, size and above
    /// all rotation. Its tilt is set by hand and a re-run must never undo it.
    /// </summary>
    private static RectTransform FindOrCreatePanel(RectTransform move2, string name, string[] members,
                                                   RectTransform root, out bool created)
    {
        created = false;

        RectTransform panel = Find(root, name);
        if (panel != null)
        {
            if (panel.parent != move2) Reparent(panel, move2, root);
            return panel;
        }

        Vector2 centre = GroupCentre(root, members, out Vector2 size);
        if (size == Vector2.zero)
        {
            Debug.LogWarning($"[HomeUiLayoutSetup] None of {name}'s widgets exist; not creating it.");
            return null;
        }

        panel = NewContainer(name, move2);
        panel.sizeDelta = size;
        panel.anchoredPosition = centre - PositionUnderRoot(move2, root);

        created = true;
        return panel;
    }

    /// <summary>
    /// 左边的面板接管新闻面板的整个姿态 / LeftPanel is built by taking over the news panel's
    /// whole pose - position, depth, size, pivot and its hand-set tilt - and then resetting the
    /// news panel to sit flat at the panel's origin. On screen nothing moves; the difference is
    /// that the tilt now belongs to the panel, so a button added beside the news later leans
    /// with it instead of needing its own angle.
    ///
    /// Only done when the panel is first created. On a re-run the panel already owns the tilt,
    /// and copying the news panel's (now flat) rotation up again would wipe it.
    /// </summary>
    private static RectTransform FindOrCreateLeftPanel(RectTransform move2, RectTransform root, out bool created)
    {
        created = false;

        RectTransform panel = Find(root, LeftPanel);
        if (panel != null)
        {
            if (panel.parent != move2) Reparent(panel, move2, root);
            return panel;
        }

        RectTransform news = Find(root, LeftGroup[0]);
        if (news == null)
        {
            Debug.LogWarning($"[HomeUiLayoutSetup] No {LeftGroup[0]} to build {LeftPanel} around; not creating it.");
            return null;
        }

        Vector2 layout = PositionUnderRoot(news, root) - PositionUnderRoot(move2, root);

        panel = NewContainer(LeftPanel, move2);
        panel.anchorMin = news.anchorMin;
        panel.anchorMax = news.anchorMax;
        panel.pivot = news.pivot;
        panel.sizeDelta = news.sizeDelta;
        panel.anchoredPosition3D = new Vector3(layout.x, layout.y, news.localPosition.z);
        panel.localRotation = news.localRotation;
        panel.localScale = news.localScale;

        // 新闻面板回到原点, 平放 / Pivot-for-pivot on the panel's origin, flat and at depth 0.
        // With a centred pivot that is (0, 0); the general form keeps it right if the pivot moves.
        news.SetParent(panel, false);
        news.anchoredPosition3D = new Vector3((news.pivot.x - 0.5f) * news.sizeDelta.x,
                                              (news.pivot.y - 0.5f) * news.sizeDelta.y, 0f);
        news.localRotation = Quaternion.identity;
        news.localScale = Vector3.one;

        created = true;
        return panel;
    }

    private static string[] Concat(string[] a, string[] b)
    {
        string[] all = new string[a.Length + b.Length];
        a.CopyTo(all, 0);
        b.CopyTo(all, a.Length);
        return all;
    }

    private static RectTransform Find(Transform root, string name)
    {
        if (root.name == name) return root as RectTransform;

        for (int i = 0; i < root.childCount; i++)
        {
            RectTransform found = Find(root.GetChild(i), name);
            if (found != null) return found;
        }

        return null;
    }
}
