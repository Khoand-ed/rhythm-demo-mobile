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
public static class HomeUiLayoutSetup
{
    private const string HomePrefabPath = "Assets/Arknights/Resources/Prefab/UI/HomeUI.prefab";
    private const string SkewSpriteName = "T_SkewTile";

    private const string Move1 = "move1";
    private const string Move2 = "move2";
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
    private static readonly string[] NearGroup =
    {
        "time", "dragonCoinSlot", "syntheticJadeSlot", "sourceStoneSlot",
        "OperationTile", "SquadTile", "OperatorsTile", "StoreTile", "RecruitTile",
        "PublicRecruitTile", "HeadhuntTile", "MissionsTile", "ManufactureTile", "DepotTile",
        "NewsPanel"
    };

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
            move2.anchoredPosition = centre;
            move2.sizeDelta = size;
            move2.localRotation = Quaternion.identity;
            foreach (string name in NearGroup) Reparent(Find(root, name), move2, root);

            // 还原原来的层次 / Restore the stacking the prefab was authored with:
            // decor behind, then the plate, then the icons, then everything else.
            int order = Find(root, Background) != null ? 1 : 0;
            move1.SetSiblingIndex(order++);
            foreach (string name in StaticPlate) SetOrder(Find(root, name), order++);
            foreach (string name in StaticIcons) SetOrder(Find(root, name), order++);
            move2.SetSiblingIndex(order);

            PrefabUtility.SaveAsPrefabAsset(contents, HomePrefabPath);

            Debug.Log($"HomeUI parallax groups applied.\n" +
                      $"  still: {string.Join(", ", StaticIcons)} + {string.Join(", ", StaticPlate)}\n" +
                      $"  {Move1} (far, drifts against): {string.Join(", ", FarGroup)}\n" +
                      $"  {Move2} (near, pivot {centre} size {size}): {string.Join(", ", NearGroup)}\n" +
                      "  Amounts and the idle return live in HomeUI.lua.txt.");
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
    /// Where a node sits relative to the screen centre. Every rect in HomeUI is centre-anchored
    /// with no rotation or scale, so the offsets simply add up the chain - which makes moving a
    /// widget between parents a matter of subtracting the new parent's own offset.
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
        if (node == null || node == target) return;

        Vector2 position = PositionUnderRoot(node, root);
        Vector2 targetPosition = target == root ? Vector2.zero : PositionUnderRoot(target, root);

        node.SetParent(target, false);
        node.anchoredPosition = position - targetPosition;
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
