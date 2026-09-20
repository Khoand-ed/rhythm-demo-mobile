using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 选人界面 / Builds the character select screen that sits between the song list and gameplay.
//
// 三栏, 用锚点不用绝对坐标 / Three columns, positioned by anchors rather than absolute offsets from
// the middle. The canvas pins its HEIGHT to 1080 and lets the width float with the aspect ratio
// (1920 at 16:9, 2160 at 18:9, 2400 at 20:9), so the two side columns are anchored to their edges
// and the portrait stretches into whatever is left.
//
// 用 TMP / TextMeshProUGUI throughout, matching SongSelectUI next door. The legacy-Text constraint
// that forced MissionUI's hand comes from ItemIconComponent, which this screen does not use.
//
// 节点名字是绑定的一部分 / The node names here are load-bearing. CharSelectUI.CharCell looks its
// children up by path, so renaming one breaks the binding with nothing but a null reference.
public static class CharSelectUISetup
{
    private const string UiPrefabDir = "Assets/Arknights/Resources/Prefab/UI";

    private const float TopBarHeight = 140f;
    private const float Pad = 40f;
    private const float Gap = 24f;
    private const float HeaderHeight = 72f;
    private const float GridWidth = 560f;
    private const float StatWidth = 620f;
    private const float PlayHeight = 96f;

    // 格子宽度算出来的 / 560 − 8 − 8 padding − 16 spacing = 528, halved is 264; 260 leaves eight
    // pixels of slack so no cell touches the mask edge.
    private static readonly Vector2 CellSize = new Vector2(260f, 300f);

    // SongSelectUI 的配色 / SongSelectUI's palette, not MissionUI's paper one - this screen's
    // immediate neighbour is the song map.
    private static readonly Color Ink = new Color(0.05f, 0.06f, 0.08f, 1f);
    private static readonly Color PanelInk = new Color(0.08f, 0.09f, 0.11f, 0.97f);
    private static readonly Color Slab = new Color(0.30f, 0.31f, 0.33f, 0.90f);
    private static readonly Color Edge = new Color(1f, 1f, 1f, 0.30f);
    private static readonly Color Faint = new Color(1f, 1f, 1f, 0.10f);
    private static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);
    private static readonly Color Accent = new Color(0.17f, 0.56f, 0.90f, 1f);
    private static readonly Color PlayDormant = new Color(0.22f, 0.24f, 0.27f, 0.95f);

    // --------------------------------------------------------------- entry points

    [MenuItem("Arknights/Character/Build Char Select UI", false, 1)]
    public static void BuildCharSelectUI()
    {
        if (!GuardEditMode()) return;

        string path = $"{UiPrefabDir}/{CharSelectUI.UIName}.prefab";

        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
        {
            Debug.Log($"[CharSelectUISetup] {path} already exists; left alone. " +
                      "Use Arknights/Character/Rebuild Char Select UI to regenerate it.");
            return;
        }

        Build();
    }

    /// <summary>
    /// 推倒重建 / Deletes the prefab and builds it from scratch.
    ///
    /// 和 Build 配对 / The explicit reset that pairs with the find-or-create above, the way every
    /// other tool in this project pairs the two. Anything adjusted by hand on the prefab is lost.
    /// </summary>
    [MenuItem("Arknights/Character/Rebuild Char Select UI", false, 20)]
    public static void RebuildCharSelectUI()
    {
        if (!GuardEditMode()) return;

        AssetDatabase.DeleteAsset($"{UiPrefabDir}/{CharSelectUI.UIName}.prefab");
        Build();
    }

    private static void Build()
    {
        GameObject root = new GameObject(CharSelectUI.UIName,
            typeof(RectTransform), typeof(CanvasGroup), typeof(CharSelectUI));
        RectTransform rootRect = (RectTransform)root.transform;
        Stretch(rootRect);

        CharSelectUI ui = root.GetComponent<CharSelectUI>();

        UnityEngine.UI.Image backdrop = NewImage("Backdrop", rootRect, Ink);
        Stretch((RectTransform)backdrop.transform);
        backdrop.raycastTarget = true;

        BuildTopBar(rootRect, ui);
        BuildRosterColumn(rootRect, ui);
        BuildPortrait(rootRect, ui);
        BuildStatPanel(rootRect, ui);

        Save(root, CharSelectUI.UIName);

        Debug.Log($"[CharSelectUISetup] Built {UiPrefabDir}/{CharSelectUI.UIName}.prefab\n" +
                  "  left: 2-column roster grid, scrolls vertically\n" +
                  "  centre: portrait + name plate\n" +
                  "  right: max HP / score / fever, one passive, PLAY pinned to the bottom\n" +
                  "  Opened by SongSelectUI's START button - no HomeUI tile hooks this one.");
    }

    // -------------------------------------------------------------------- top bar

    private static void BuildTopBar(RectTransform parent, CharSelectUI ui)
    {
        RectTransform bar = NewRect("TopBar", parent);
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(0f, TopBarHeight);
        bar.anchoredPosition = Vector2.zero;

        UnityEngine.UI.Image back = NewImage("BackButton", bar, Slab);
        RectTransform backRect = (RectTransform)back.transform;
        backRect.anchorMin = new Vector2(0f, 1f);
        backRect.anchorMax = new Vector2(0f, 1f);
        backRect.pivot = new Vector2(0f, 1f);
        backRect.anchoredPosition = new Vector2(48f, -40f);
        backRect.sizeDelta = new Vector2(170f, 68f);
        AddEdges(backRect);

        ui.backButton = back.gameObject.AddComponent<Button>();
        ui.backButton.targetGraphic = back;

        TextMeshProUGUI backLabel = NewText("Text", backRect, 46f, Color.white, TextAlignmentOptions.Center);
        Stretch((RectTransform)backLabel.transform);
        backLabel.text = "‹";

        // 提示刚才选了什么 / What the player just queued up, so the pick they are about to commit
        // to is still on screen.
        ui.queueLabel = NewText("QueueLabel", bar, 26f, Dim, TextAlignmentOptions.Right);
        RectTransform queueRect = (RectTransform)ui.queueLabel.transform;
        queueRect.anchorMin = new Vector2(1f, 1f);
        queueRect.anchorMax = new Vector2(1f, 1f);
        queueRect.pivot = new Vector2(1f, 1f);
        queueRect.anchoredPosition = new Vector2(-48f, -44f);
        queueRect.sizeDelta = new Vector2(900f, 60f);
    }

    // ------------------------------------------------------------- roster column

    private static void BuildRosterColumn(RectTransform parent, CharSelectUI ui)
    {
        RectTransform column = NewRect("Roster", parent);
        column.anchorMin = new Vector2(0f, 0f);
        column.anchorMax = new Vector2(0f, 1f);
        column.pivot = new Vector2(0f, 0.5f);
        column.sizeDelta = new Vector2(GridWidth, -(TopBarHeight + Pad));
        column.anchoredPosition = new Vector2(Pad, (Pad - TopBarHeight) / 2f);

        RectTransform header = NewRect("Header", column);
        header.anchorMin = new Vector2(0f, 1f);
        header.anchorMax = new Vector2(1f, 1f);
        header.pivot = new Vector2(0.5f, 1f);
        header.sizeDelta = new Vector2(0f, HeaderHeight);
        header.anchoredPosition = Vector2.zero;

        TextMeshProUGUI title = NewText("Title", header, 30f, Dim, TextAlignmentOptions.Left);
        Stretch((RectTransform)title.transform);
        title.fontStyle = FontStyles.Bold;
        title.text = "OPERATORS";

        ui.rosterCount = NewText("Count", header, 30f, Color.white, TextAlignmentOptions.Right);
        Stretch((RectTransform)ui.rosterCount.transform);
        ui.rosterCount.fontStyle = FontStyles.Bold;
        ui.rosterCount.text = "0";

        ui.cellTemplate = BuildGrid(column);
    }

    /// <summary>
    /// 两列网格 / The 2-column grid, copied from MissionUISetup.BuildRewardCells.
    ///
    /// 四个干员时其实不会溢出 / Be honest about this: with the four seeded operators the content is
    /// two rows and does not overflow, so the scroll is real but idle. It starts mattering at seven.
    /// </summary>
    private static RectTransform BuildGrid(RectTransform column)
    {
        RectTransform scrollRoot = NewRect("Scroll", column);
        scrollRoot.anchorMin = Vector2.zero;
        scrollRoot.anchorMax = Vector2.one;
        scrollRoot.offsetMin = Vector2.zero;
        scrollRoot.offsetMax = new Vector2(0f, -(HeaderHeight + 8f));

        RectTransform viewport = NewViewport(scrollRoot);

        RectTransform content = NewRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;

        GridLayoutGroup grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = CellSize;
        grid.spacing = new Vector2(16f, 16f);
        grid.padding = new RectOffset(8, 8, 8, 8);
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;

        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 40f;

        return BuildCellTemplate(content);
    }

    /// <summary>
    /// 一个格子 / One roster cell, kept INACTIVE: CharSelectUI clones it into its own parent and
    /// the clones inherit that, so an active template would sit in the grid forever as an extra,
    /// blank operator.
    /// </summary>
    private static RectTransform BuildCellTemplate(RectTransform parent)
    {
        UnityEngine.UI.Image face = NewImage("CharCellTemplate", parent, PanelInk);
        RectTransform rect = (RectTransform)face.transform;
        rect.sizeDelta = CellSize;

        Button button = face.gameObject.AddComponent<Button>();
        button.targetGraphic = face;

        UnityEngine.UI.Image ground = NewImage("Ground", rect, Color.white);
        RectTransform groundRect = (RectTransform)ground.transform;
        Stretch(groundRect);
        groundRect.offsetMin = new Vector2(6f, 58f);
        groundRect.offsetMax = new Vector2(-6f, -6f);
        ground.raycastTarget = false;

        // 比 Ground 缩进一圈 / Inset further than Ground so the rarity-tinted card shows as a
        // frame around it; sharing Ground's exact rect would cover it completely and leave that
        // node drawing nothing.
        UnityEngine.UI.Image avatar = NewImage("Avatar", rect, Color.white);
        RectTransform avatarRect = (RectTransform)avatar.transform;
        Stretch(avatarRect);
        avatarRect.offsetMin = new Vector2(16f, 68f);
        avatarRect.offsetMax = new Vector2(-16f, -16f);
        avatar.raycastTarget = false;
        avatar.preserveAspect = true;

        UnityEngine.UI.Image stars = NewImage("Stars", rect, Color.white);
        RectTransform starsRect = (RectTransform)stars.transform;
        starsRect.anchorMin = new Vector2(0f, 1f);
        starsRect.anchorMax = new Vector2(0f, 1f);
        starsRect.pivot = new Vector2(0f, 1f);
        starsRect.anchoredPosition = new Vector2(12f, -12f);
        starsRect.sizeDelta = new Vector2(120f, 24f);
        stars.raycastTarget = false;
        stars.preserveAspect = true;

        UnityEngine.UI.Image profession = NewImage("Profession", rect, Dim);
        RectTransform professionRect = (RectTransform)profession.transform;
        professionRect.anchorMin = new Vector2(1f, 1f);
        professionRect.anchorMax = new Vector2(1f, 1f);
        professionRect.pivot = new Vector2(1f, 1f);
        professionRect.anchoredPosition = new Vector2(-12f, -12f);
        professionRect.sizeDelta = new Vector2(40f, 40f);
        profession.raycastTarget = false;
        profession.preserveAspect = true;

        UnityEngine.UI.Image nameGround = NewImage("NameGround", rect, Ink);
        RectTransform nameGroundRect = (RectTransform)nameGround.transform;
        nameGroundRect.anchorMin = Vector2.zero;
        nameGroundRect.anchorMax = new Vector2(1f, 0f);
        nameGroundRect.pivot = new Vector2(0.5f, 0f);
        nameGroundRect.anchoredPosition = new Vector2(0f, 6f);
        nameGroundRect.sizeDelta = new Vector2(-12f, 46f);
        nameGround.raycastTarget = false;

        TextMeshProUGUI label = NewText("Name", nameGroundRect, 24f, Color.white, TextAlignmentOptions.Center);
        Stretch((RectTransform)label.transform);
        label.fontStyle = FontStyles.Bold;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;

        // 选中框 / The selection outline, four edges so the cell art stays visible under it.
        RectTransform selected = NewRect("Selected", rect);
        Stretch(selected);
        AddEdges(selected, Accent, 4f);
        selected.gameObject.SetActive(false);

        rect.gameObject.SetActive(false);
        return rect;
    }

    // ---------------------------------------------------------------- portrait

    private static void BuildPortrait(RectTransform parent, CharSelectUI ui)
    {
        RectTransform centre = NewRect("Portrait", parent);
        centre.anchorMin = Vector2.zero;
        centre.anchorMax = Vector2.one;
        centre.offsetMin = new Vector2(Pad + GridWidth + Gap, Pad);
        centre.offsetMax = new Vector2(-(Pad + StatWidth + Gap), -TopBarHeight);

        ui.portrait = NewImage("Image", centre, Color.white);
        RectTransform portraitRect = (RectTransform)ui.portrait.transform;
        Stretch(portraitRect);
        portraitRect.offsetMin = new Vector2(24f, 120f);
        portraitRect.offsetMax = new Vector2(-24f, -24f);
        ui.portrait.preserveAspect = true;
        ui.portrait.raycastTarget = false;

        // 没选人时的提示 / Shown until something is picked. The whole "dim PLAY plus a popup"
        // requirement rests on this state existing, so nothing is pre-selected.
        TextMeshProUGUI empty = NewText("EmptyState", centre, 34f, Dim, TextAlignmentOptions.Center);
        Stretch((RectTransform)empty.transform);
        empty.text = "SELECT AN OPERATOR";
        ui.emptyState = empty.gameObject;

        // 名牌整块开关 / Toggled as a whole by CharSelectUI, because its star and profession Images
        // would otherwise draw as solid quads whenever their sprite is null.
        UnityEngine.UI.Image plate = NewImage("NamePlate", centre, PanelInk);
        ui.namePlate = plate.gameObject;
        RectTransform plateRect = (RectTransform)plate.transform;
        plateRect.anchorMin = Vector2.zero;
        plateRect.anchorMax = new Vector2(1f, 0f);
        plateRect.pivot = new Vector2(0.5f, 0f);
        plateRect.anchoredPosition = Vector2.zero;
        plateRect.sizeDelta = new Vector2(0f, 96f);
        plate.raycastTarget = false;

        ui.rarityRule = NewImage("RarityRule", plateRect, Dim);
        RectTransform ruleRect = (RectTransform)ui.rarityRule.transform;
        ruleRect.anchorMin = new Vector2(0f, 1f);
        ruleRect.anchorMax = new Vector2(1f, 1f);
        ruleRect.pivot = new Vector2(0.5f, 1f);
        ruleRect.anchoredPosition = Vector2.zero;
        ruleRect.sizeDelta = new Vector2(0f, 4f);
        ui.rarityRule.raycastTarget = false;

        ui.portraitName = NewText("Name", plateRect, 44f, Color.white, TextAlignmentOptions.Left);
        RectTransform nameRect = (RectTransform)ui.portraitName.transform;
        Stretch(nameRect);
        nameRect.offsetMin = new Vector2(24f, 0f);
        nameRect.offsetMax = new Vector2(-180f, -8f);
        ui.portraitName.fontStyle = FontStyles.Bold;

        ui.portraitStars = NewImage("Stars", plateRect, Color.white);
        RectTransform starsRect = (RectTransform)ui.portraitStars.transform;
        starsRect.anchorMin = new Vector2(1f, 0.5f);
        starsRect.anchorMax = new Vector2(1f, 0.5f);
        starsRect.pivot = new Vector2(1f, 0.5f);
        starsRect.anchoredPosition = new Vector2(-84f, 0f);
        starsRect.sizeDelta = new Vector2(140f, 28f);
        ui.portraitStars.preserveAspect = true;
        ui.portraitStars.raycastTarget = false;

        ui.portraitProfession = NewImage("Profession", plateRect, Dim);
        RectTransform professionRect = (RectTransform)ui.portraitProfession.transform;
        professionRect.anchorMin = new Vector2(1f, 0.5f);
        professionRect.anchorMax = new Vector2(1f, 0.5f);
        professionRect.pivot = new Vector2(1f, 0.5f);
        professionRect.anchoredPosition = new Vector2(-24f, 0f);
        professionRect.sizeDelta = new Vector2(48f, 48f);
        ui.portraitProfession.preserveAspect = true;
        ui.portraitProfession.raycastTarget = false;
    }

    // -------------------------------------------------------------- stat panel

    private static void BuildStatPanel(RectTransform parent, CharSelectUI ui)
    {
        RectTransform panel = NewRect("Status", parent);
        panel.anchorMin = new Vector2(1f, 0f);
        panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot = new Vector2(1f, 0.5f);
        panel.sizeDelta = new Vector2(StatWidth, -(TopBarHeight + Pad));
        panel.anchoredPosition = new Vector2(-Pad, (Pad - TopBarHeight) / 2f);

        TextMeshProUGUI header = NewText("Header", panel, 30f, Dim, TextAlignmentOptions.Left);
        RectTransform headerRect = (RectTransform)header.transform;
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.sizeDelta = new Vector2(0f, HeaderHeight);
        headerRect.anchoredPosition = Vector2.zero;
        header.fontStyle = FontStyles.Bold;
        header.text = "STATUS";

        // 三行音游数值 / The three rhythm numbers. Deliberately no ATK/DEF/RES/block/DP cost:
        // CharMeta still carries them for the inherited tower-defense screens, but none of them
        // mean anything in a rhythm run.
        ui.hpValue = StatRow(panel, "MaxHp", "MAX HP", -HeaderHeight);
        ui.scoreValue = StatRow(panel, "Score", "SCORE", -HeaderHeight - 76f);
        ui.feverValue = StatRow(panel, "Fever", "FEVER", -HeaderHeight - 152f);

        UnityEngine.UI.Image divider = NewImage("Divider", panel, Faint);
        RectTransform dividerRect = (RectTransform)divider.transform;
        dividerRect.anchorMin = new Vector2(0f, 1f);
        dividerRect.anchorMax = new Vector2(1f, 1f);
        dividerRect.pivot = new Vector2(0.5f, 1f);
        dividerRect.anchoredPosition = new Vector2(0f, -HeaderHeight - 236f);
        dividerRect.sizeDelta = new Vector2(0f, 2f);
        divider.raycastTarget = false;

        BuildSkillBlock(panel, ui);
        BuildPlayButton(panel, ui);
    }

    private static TextMeshProUGUI StatRow(RectTransform panel, string name, string caption, float y)
    {
        RectTransform row = NewRect(name, panel);
        row.anchorMin = new Vector2(0f, 1f);
        row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.anchoredPosition = new Vector2(0f, y);
        row.sizeDelta = new Vector2(0f, 64f);

        TextMeshProUGUI label = NewText("Label", row, 26f, Dim, TextAlignmentOptions.Left);
        Stretch((RectTransform)label.transform);
        label.text = caption;

        TextMeshProUGUI value = NewText("Value", row, 34f, Color.white, TextAlignmentOptions.Right);
        Stretch((RectTransform)value.transform);
        value.fontStyle = FontStyles.Bold;
        value.text = "--";

        return value;
    }

    /// <summary>
    /// 被动 / The one passive.
    ///
    /// 只有一个, 没有占位槽 / Exactly one block and no empty slots, which is what the GDD asks for.
    /// CharSelectUI hides the whole thing when the passive is empty rather than drawing a
    /// placeholder that suggests content nobody has authored.
    /// </summary>
    private static void BuildSkillBlock(RectTransform panel, CharSelectUI ui)
    {
        UnityEngine.UI.Image block = NewImage("Skill", panel, PanelInk);
        RectTransform blockRect = (RectTransform)block.transform;
        blockRect.anchorMin = Vector2.zero;
        blockRect.anchorMax = Vector2.one;
        blockRect.offsetMin = new Vector2(0f, PlayHeight + Gap + 24f);
        blockRect.offsetMax = new Vector2(0f, -(HeaderHeight + 260f));
        block.raycastTarget = false;
        ui.skillBlock = block.gameObject;

        ui.skillIcon = NewImage("Icon", blockRect, Color.white);
        RectTransform iconRect = (RectTransform)ui.skillIcon.transform;
        iconRect.anchorMin = new Vector2(0f, 1f);
        iconRect.anchorMax = new Vector2(0f, 1f);
        iconRect.pivot = new Vector2(0f, 1f);
        iconRect.anchoredPosition = new Vector2(20f, -20f);
        iconRect.sizeDelta = new Vector2(96f, 96f);
        ui.skillIcon.preserveAspect = true;
        ui.skillIcon.raycastTarget = false;

        TextMeshProUGUI caption = NewText("Caption", blockRect, 20f, Dim, TextAlignmentOptions.TopLeft);
        RectTransform captionRect = (RectTransform)caption.transform;
        captionRect.anchorMin = new Vector2(0f, 1f);
        captionRect.anchorMax = new Vector2(1f, 1f);
        captionRect.pivot = new Vector2(0.5f, 1f);
        captionRect.anchoredPosition = new Vector2(68f, -22f);
        captionRect.sizeDelta = new Vector2(-156f, 26f);
        caption.text = "PASSIVE";

        ui.skillName = NewText("Name", blockRect, 28f, Color.white, TextAlignmentOptions.TopLeft);
        RectTransform skillNameRect = (RectTransform)ui.skillName.transform;
        skillNameRect.anchorMin = new Vector2(0f, 1f);
        skillNameRect.anchorMax = new Vector2(1f, 1f);
        skillNameRect.pivot = new Vector2(0.5f, 1f);
        skillNameRect.anchoredPosition = new Vector2(68f, -54f);
        skillNameRect.sizeDelta = new Vector2(-156f, 40f);
        ui.skillName.fontStyle = FontStyles.Bold;

        ui.skillDescription = NewText("Description", blockRect, 22f, Dim, TextAlignmentOptions.TopLeft);
        RectTransform descRect = (RectTransform)ui.skillDescription.transform;
        descRect.anchorMin = Vector2.zero;
        descRect.anchorMax = Vector2.one;
        descRect.offsetMin = new Vector2(20f, 20f);
        descRect.offsetMax = new Vector2(-20f, -136f);

        block.gameObject.SetActive(false);
    }

    /// <summary>
    /// 开始键 / PLAY, pinned to the bottom of the panel.
    ///
    /// 永远 interactable / The Button stays interactable at all times and CharSelectUI paints the
    /// dormant look on by hand. That is deliberate: Button.OnPointerClick returns before firing
    /// onClick when IsInteractable() is false, so a genuinely disabled button cannot show the
    /// popup that explains why it did nothing. Do not set interactable = false here.
    /// </summary>
    private static void BuildPlayButton(RectTransform panel, CharSelectUI ui)
    {
        ui.playFace = NewImage("PlayButton", panel, PlayDormant);
        RectTransform playRect = (RectTransform)ui.playFace.transform;
        playRect.anchorMin = new Vector2(0f, 0f);
        playRect.anchorMax = new Vector2(1f, 0f);
        playRect.pivot = new Vector2(0.5f, 0f);
        playRect.anchoredPosition = new Vector2(0f, 24f);
        playRect.sizeDelta = new Vector2(0f, PlayHeight);
        AddEdges(playRect);

        ui.playButton = ui.playFace.gameObject.AddComponent<Button>();
        ui.playButton.targetGraphic = ui.playFace;

        ui.playLabel = NewText("Text", playRect, 34f, Dim, TextAlignmentOptions.Center);
        Stretch((RectTransform)ui.playLabel.transform);
        ui.playLabel.fontStyle = FontStyles.Bold;
        ui.playLabel.text = "SELECT AN OPERATOR";
    }

    // --------------------------------------------------------------------- helpers

    private static bool GuardEditMode()
    {
        if (!Application.isPlaying) return true;
        Debug.LogError("[CharSelectUISetup] Exit Play Mode first.");
        return false;
    }

    private static void Save(GameObject root, string name)
    {
        Directory.CreateDirectory(UiPrefabDir);
        PrefabUtility.SaveAsPrefabAsset(root, $"{UiPrefabDir}/{name}.prefab");
        Object.DestroyImmediate(root);
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static UnityEngine.UI.Image NewImage(string name, Transform parent, Color color)
    {
        UnityEngine.UI.Image image = NewRect(name, parent).gameObject.AddComponent<UnityEngine.UI.Image>();
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

    /// <summary>
    /// 用 RectMask2D 不用 Mask / A ScrollRect viewport clipped by RectMask2D rather than Mask.
    ///
    /// 为什么换 / DepotUI and MissionUI both use Mask + an opaque Image, and both work in game.
    /// RectMask2D is the better fit here for two reasons. It clips to the RectTransform rect
    /// instead of writing a stencil from a graphic's alpha, so it needs no Image at all - and the
    /// Image that Mask required was a standing trap, because dropping its alpha below 1 silently
    /// clips away every child with no error. It also clips correctly in an offscreen capture with
    /// no stencil buffer, which is what makes the grid's overflow provable without entering Play
    /// Mode rather than something to take on faith.
    ///
    /// 只能切矩形 / The trade is that RectMask2D only does axis-aligned rectangles. That is exactly
    /// what a scroll viewport is.
    /// </summary>
    private static RectTransform NewViewport(RectTransform parent)
    {
        RectTransform viewport = NewRect("Viewport", parent);
        Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        return viewport;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void AddEdges(RectTransform parent)
    {
        AddEdges(parent, Edge, 2f);
    }

    private static void AddEdges(RectTransform parent, Color color, float thickness)
    {
        NewEdge(parent, "EdgeTop", new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -thickness / 2f), new Vector2(0f, thickness), color);
        NewEdge(parent, "EdgeBottom", new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(0f, thickness / 2f), new Vector2(0f, thickness), color);
        NewEdge(parent, "EdgeLeft", new Vector2(0f, 0f), new Vector2(0f, 1f),
            new Vector2(thickness / 2f, 0f), new Vector2(thickness, 0f), color);
        NewEdge(parent, "EdgeRight", new Vector2(1f, 0f), new Vector2(1f, 1f),
            new Vector2(-thickness / 2f, 0f), new Vector2(thickness, 0f), color);
    }

    private static void NewEdge(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                                Vector2 position, Vector2 size, Color color)
    {
        UnityEngine.UI.Image edge = NewImage(name, parent, color);
        RectTransform rect = (RectTransform)edge.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        edge.raycastTarget = false;
    }
}
