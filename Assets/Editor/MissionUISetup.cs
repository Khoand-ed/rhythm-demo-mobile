using System.IO;
using Data.Item;
using Data.Mission;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 任务界面 / Builds the mission board and its reward popup.
//
// 布局参考截图 / Laid out from the reference screens: rewards down the left in a narrow column,
// missions down the right in a wide one, both scrolling vertically on their own.
//
// 用旧版 Text 而不是 TMP / Legacy Text throughout, like DepotUISetup and for the same reason:
// ItemIconComponent declares a UnityEngine.UI.Text field, so a TextMeshProUGUI would not bind, and
// mixing the two inside one screen is exactly what CLAUDE.md warns about.
//
// 节点名字是绑定的一部分 / The node names here are load-bearing. MissionUI and MissionRewardUI look
// their row parts up by path, so renaming a node here breaks the binding with nothing but a null
// reference to explain it.
public static class MissionUISetup
{
    private const string UiPrefabDir = "Assets/Arknights/Resources/Prefab/UI";
    private const string MaterialDir = "Assets/Arknights/Shaders";
    private const string BlurShader = "Arknights/UI/Blur";
    private const string MissionsTileName = "MissionsTile";

    private const float TopBarHeight = 140f;
    private const float Pad = 40f;
    private const float Gap = 24f;
    private const float RewardWidth = 620f;
    private const float HeaderHeight = 72f;
    private const float RewardRowHeight = 168f;
    private const float MissionRowHeight = 150f;
    private const float ClaimAllWidth = 300f;
    private const float PointsWidth = 440f;
    private const float AwardWidth = 288f;
    private const float AwardHeight = 118f;

    // Same palette as the depot, so the two screens read as one front-end.
    private static readonly Color Slab = new Color(0.30f, 0.31f, 0.33f, 0.90f);
    private static readonly Color Edge = new Color(1f, 1f, 1f, 0.30f);
    private static readonly Color Paper = new Color(0.93f, 0.94f, 0.95f, 1f);
    private static readonly Color PaperDim = new Color(0.86f, 0.87f, 0.89f, 1f);
    private static readonly Color Ink = new Color(0.10f, 0.11f, 0.12f, 1f);
    private static readonly Color InkDim = new Color(0.42f, 0.44f, 0.47f, 1f);
    private static readonly Color TabOn = new Color(0.32f, 0.33f, 0.35f, 1f);
    private static readonly Color TabOff = new Color(0.78f, 0.79f, 0.81f, 1f);
    private static readonly Color Accent = new Color(0.17f, 0.56f, 0.90f, 1f);
    private static readonly Color Track = new Color(0f, 0f, 0f, 0.35f);

    private static Font font;

    // --------------------------------------------------------------- entry points

    [MenuItem("Arknights/Mission/Build Mission UI", false, 1)]
    public static void BuildMissionUI()
    {
        if (!GuardEditMode()) return;

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) Debug.LogWarning("[MissionUISetup] No builtin font found; Text components will render blank.");

        BuildMissionScreen();
        BuildRewardPopup();
        bool hooked = HookMissionsTile();

        AssetDatabase.SaveAssets();

        Debug.Log("[MissionUISetup] Mission UI built.\n" +
                  $"  {UiPrefabDir}/MissionUI.prefab - Daily/Weekly tabs, rewards left, missions right, both scroll\n" +
                  $"  {UiPrefabDir}/MissionRewardUI.prefab - claim popup, blurred backdrop\n" +
                  $"  HomeUI {MissionsTileName}: {(hooked ? "opens MissionUI" : "NOT hooked, see warning above")}\n" +
                  "  Progress lives in PlayerPrefs; Arknights/Mission/Reset Mission Progress clears it.");
    }

    /// <summary>
    /// 清空进度 / Wipes every counter and claimed flag on both boards.
    ///
    /// 这是唯一的重置 / This is the only reset there is: nothing reads the calendar, so "daily"
    /// and "weekly" name the two boards rather than a schedule. That keeps a test run repeatable,
    /// and a real clock reset is a change to MissionManager when it is wanted.
    /// </summary>
    [MenuItem("Arknights/Mission/Reset Mission Progress", false, 20)]
    public static void ResetMissionProgress()
    {
        if (!GuardEditMode()) return;

        MissionManager.Inst().ResetAll();
        Debug.Log("[MissionUISetup] Mission progress reset: every counter and claimed flag cleared on both boards.");
    }

    // -------------------------------------------------------------- mission screen

    private static void BuildMissionScreen()
    {
        GameObject root = new GameObject("MissionUI",
            typeof(RectTransform), typeof(CanvasGroup), typeof(UI.Sub.MissionUI));
        RectTransform rootRect = (RectTransform)root.transform;
        Stretch(rootRect);

        UI.Sub.MissionUI ui = root.GetComponent<UI.Sub.MissionUI>();

        UnityEngine.UI.Image backdrop = NewImage("Backdrop", rootRect, Paper);
        Stretch((RectTransform)backdrop.transform);

        BuildTopBar(rootRect, ui);
        BuildRewardColumn(rootRect, ui);
        BuildMissionColumn(rootRect, ui);

        Save(root, "MissionUI");
    }

    private static void BuildTopBar(RectTransform parent, UI.Sub.MissionUI ui)
    {
        RectTransform bar = NewRect("TopBar", parent);
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(0f, TopBarHeight);
        bar.anchoredPosition = Vector2.zero;

        // 返回键和其他界面一致 / The same back slab the depot, shop and character screens use.
        UnityEngine.UI.Image back = NewImage("BackButton", bar, Slab);
        RectTransform backRect = (RectTransform)back.transform;
        backRect.anchorMin = new Vector2(0f, 1f);
        backRect.anchorMax = new Vector2(0f, 1f);
        backRect.pivot = new Vector2(0f, 1f);
        backRect.anchoredPosition = new Vector2(48f, -40f);
        backRect.sizeDelta = new Vector2(170f, 68f);
        AddEdges(backRect);

        Button backButton = back.gameObject.AddComponent<Button>();
        backButton.targetGraphic = back;
        UnityEditor.Events.UnityEventTools.AddBoolPersistentListener(backButton.onClick, ui.Hide, true);

        Text backLabel = NewText("Text", backRect, 46, Color.white, TextAnchor.MiddleCenter);
        Stretch((RectTransform)backLabel.transform);
        backLabel.text = "‹";

        // 两个页签互斥 / One group so the two tabs behave as radio buttons - MissionUI only ever
        // redraws on the toggle going on, and relies on exactly one being on at a time.
        RectTransform tabs = NewRect("Tabs", bar);
        tabs.anchorMin = new Vector2(1f, 1f);
        tabs.anchorMax = new Vector2(1f, 1f);
        tabs.pivot = new Vector2(1f, 1f);
        tabs.anchoredPosition = new Vector2(-48f, -40f);
        tabs.sizeDelta = new Vector2(660f, 68f);

        HorizontalLayoutGroup layout = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        ToggleGroup group = tabs.gameObject.AddComponent<ToggleGroup>();

        ui.dailyTab = NewTab("Daily", tabs, group, "DAILY", true);
        ui.weeklyTab = NewTab("Weekly", tabs, group, "WEEKLY", false);
    }

    private static Toggle NewTab(string name, RectTransform parent, ToggleGroup group, string label, bool on)
    {
        UnityEngine.UI.Image face = NewImage(name, parent, on ? TabOn : TabOff);

        Toggle toggle = face.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = face;
        toggle.group = group;
        toggle.isOn = on;

        toggle.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = toggle.colors;
        colors.normalColor = Color.white;
        toggle.colors = colors;

        // MissionUI.BindTab recolours this with GetComponentInChildren<Text>(), so it has to be
        // the only Text under the tab.
        Text text = NewText("Label", (RectTransform)face.transform, 28, on ? Color.white : Ink,
                            TextAnchor.MiddleCenter);
        Stretch((RectTransform)text.transform);
        text.text = label;
        text.fontStyle = FontStyle.Bold;

        return toggle;
    }

    // -------------------------------------------------------------- reward column

    private static void BuildRewardColumn(RectTransform parent, UI.Sub.MissionUI ui)
    {
        RectTransform column = NewRect("Rewards", parent);
        column.anchorMin = new Vector2(0f, 0f);
        column.anchorMax = new Vector2(0f, 1f);
        column.pivot = new Vector2(0f, 0.5f);
        column.sizeDelta = new Vector2(RewardWidth, -(TopBarHeight + Pad));
        column.anchoredPosition = new Vector2(Pad, (Pad - TopBarHeight) / 2f);

        Text header = NewText("Header", column, 30, InkDim, TextAnchor.MiddleLeft);
        RectTransform headerRect = (RectTransform)header.transform;
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.sizeDelta = new Vector2(0f, HeaderHeight);
        headerRect.anchoredPosition = Vector2.zero;
        header.text = "REWARDS";
        header.fontStyle = FontStyle.Bold;

        RectTransform content = NewScroll(column, 14f);
        ui.rewardTemplate = BuildRewardRowTemplate(content);
    }

    /// <summary>
    /// 一行奖励 / One reward row, kept INACTIVE: MissionUI clones it into its own parent and the
    /// clones inherit that, so an active template would sit in the list forever as a fourth,
    /// blank reward.
    /// </summary>
    private static RectTransform BuildRewardRowTemplate(RectTransform parent)
    {
        UnityEngine.UI.Image row = NewImage("RewardRowTemplate", parent, PaperDim);
        RectTransform rect = (RectTransform)row.transform;
        SetRowHeight(rect, RewardRowHeight);

        // 门槛分数 / The threshold, as a plate on the left the way the reference stamps its
        // reward sets.
        UnityEngine.UI.Image badge = NewImage("PointBadge", rect, TabOn);
        RectTransform badgeRect = (RectTransform)badge.transform;
        badgeRect.anchorMin = new Vector2(0f, 0.5f);
        badgeRect.anchorMax = new Vector2(0f, 0.5f);
        badgeRect.pivot = new Vector2(0f, 0.5f);
        badgeRect.anchoredPosition = new Vector2(18f, 0f);
        badgeRect.sizeDelta = new Vector2(112f, 124f);

        Text value = NewText("Value", badgeRect, 52, Color.white, TextAnchor.MiddleCenter);
        RectTransform valueRect = (RectTransform)value.transform;
        valueRect.anchorMin = new Vector2(0f, 0.32f);
        valueRect.anchorMax = Vector2.one;
        valueRect.offsetMin = Vector2.zero;
        valueRect.offsetMax = Vector2.zero;
        value.fontStyle = FontStyle.Bold;
        value.text = "0";

        Text caption = NewText("Caption", badgeRect, 20, new Color(1f, 1f, 1f, 0.70f), TextAnchor.UpperCenter);
        RectTransform captionRect = (RectTransform)caption.transform;
        captionRect.anchorMin = Vector2.zero;
        captionRect.anchorMax = new Vector2(1f, 0.32f);
        captionRect.offsetMin = Vector2.zero;
        captionRect.offsetMax = Vector2.zero;
        caption.text = "PTS";

        // 物品图标 / The item itself. ItemIconComponent wants ground/icon/amount and a Button,
        // because MissionUI wires a tap through to ItemInfoUI.
        UnityEngine.UI.Image ground = NewImage("Icon", rect, Color.white);
        RectTransform groundRect = (RectTransform)ground.transform;
        groundRect.anchorMin = new Vector2(0f, 0.5f);
        groundRect.anchorMax = new Vector2(0f, 0.5f);
        groundRect.pivot = new Vector2(0f, 0.5f);
        groundRect.anchoredPosition = new Vector2(148f, 0f);
        groundRect.sizeDelta = new Vector2(124f, 124f);

        Button iconButton = ground.gameObject.AddComponent<Button>();
        iconButton.targetGraphic = ground;

        UnityEngine.UI.Image icon = NewImage("Icon", groundRect, Color.white);
        RectTransform iconRect = (RectTransform)icon.transform;
        Stretch(iconRect);
        iconRect.offsetMin = new Vector2(18f, 18f);
        iconRect.offsetMax = new Vector2(-18f, -18f);
        icon.raycastTarget = false;
        icon.preserveAspect = true;

        Text amount = NewText("Amount", groundRect, 26, Color.white, TextAnchor.LowerRight);
        RectTransform amountRect = (RectTransform)amount.transform;
        amountRect.anchorMin = Vector2.zero;
        amountRect.anchorMax = new Vector2(1f, 0f);
        amountRect.pivot = new Vector2(0.5f, 0f);
        amountRect.sizeDelta = new Vector2(-10f, 36f);
        amountRect.anchoredPosition = new Vector2(0f, 6f);
        amount.fontStyle = FontStyle.Bold;

        ItemIconComponent component = ground.gameObject.AddComponent<ItemIconComponent>();
        component.ground = ground;
        component.icon = icon;
        component.amount = amount;

        // 名字放上半, 按钮放下半 / The name takes the top half and the claim button the bottom, so
        // the two never fight for the same strip. Side by side, a 200px button would leave barely
        // a hundred for the name, which is not enough for "Sugar Substitute" at this size.
        Text itemName = NewText("Name", rect, 26, Ink, TextAnchor.LowerLeft);
        RectTransform nameRect = (RectTransform)itemName.transform;
        nameRect.anchorMin = new Vector2(0f, 0.5f);
        nameRect.anchorMax = Vector2.one;
        nameRect.offsetMin = new Vector2(288f, 0f);
        nameRect.offsetMax = new Vector2(-16f, -18f);
        itemName.fontStyle = FontStyle.Bold;

        UnityEngine.UI.Image claim = NewImage("Claim", rect, Accent);
        RectTransform claimRect = (RectTransform)claim.transform;
        claimRect.anchorMin = new Vector2(1f, 0f);
        claimRect.anchorMax = new Vector2(1f, 0f);
        claimRect.pivot = new Vector2(1f, 0f);
        claimRect.anchoredPosition = new Vector2(-16f, 24f);
        claimRect.sizeDelta = new Vector2(200f, 56f);

        Button claimButton = claim.gameObject.AddComponent<Button>();
        claimButton.targetGraphic = claim;

        Text claimLabel = NewText("Text", claimRect, 26, Color.white, TextAnchor.MiddleCenter);
        Stretch((RectTransform)claimLabel.transform);
        claimLabel.fontStyle = FontStyle.Bold;
        claimLabel.text = "CLAIM";

        claim.gameObject.SetActive(false);

        // 领过的整行压暗 / A claimed reward is finished business: scrim the row and lay a banner
        // clean across it. The banner stretches instead of taking a fixed width because a plate
        // narrower than the row leaves the icon and the item name poking out at both ends, which
        // reads as two things colliding rather than as one stamp.
        BuildDoneStamp(rect, "COMPLETED",
                       new Color(PaperDim.r, PaperDim.g, PaperDim.b, 0.72f), InkDim,
                       new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f),
                       Vector2.zero, new Vector2(-36f, 48f));

        rect.gameObject.SetActive(false);
        return rect;
    }

    // ------------------------------------------------------------- mission column

    private static void BuildMissionColumn(RectTransform parent, UI.Sub.MissionUI ui)
    {
        RectTransform column = NewRect("Missions", parent);
        column.anchorMin = Vector2.zero;
        column.anchorMax = Vector2.one;
        column.offsetMin = new Vector2(Pad + RewardWidth + Gap, Pad);
        column.offsetMax = new Vector2(-Pad, -TopBarHeight);

        RectTransform header = NewRect("Header", column);
        header.anchorMin = new Vector2(0f, 1f);
        header.anchorMax = new Vector2(1f, 1f);
        header.pivot = new Vector2(0.5f, 1f);
        header.sizeDelta = new Vector2(0f, HeaderHeight);
        header.anchoredPosition = Vector2.zero;

        ui.boardTitle = NewText("BoardTitle", header, 32, Ink, TextAnchor.MiddleLeft);
        RectTransform titleRect = (RectTransform)ui.boardTitle.transform;
        Stretch(titleRect);
        titleRect.offsetMax = new Vector2(-(ClaimAllWidth + Gap + PointsWidth + Gap), 0f);
        ui.boardTitle.fontStyle = FontStyle.Bold;
        ui.boardTitle.text = "DAILY MISSIONS";

        // 一键全领 / Claim All, at the far right where the reference puts its collect-all banner.
        // MissionUI greys it out when there is nothing waiting, so the button reports whether a
        // press is worth making rather than only what it would do.
        UnityEngine.UI.Image claimAll = NewImage("ClaimAll", header, Accent);
        RectTransform claimAllRect = (RectTransform)claimAll.transform;
        claimAllRect.anchorMin = new Vector2(1f, 0.5f);
        claimAllRect.anchorMax = new Vector2(1f, 0.5f);
        claimAllRect.pivot = new Vector2(1f, 0.5f);
        claimAllRect.anchoredPosition = Vector2.zero;
        claimAllRect.sizeDelta = new Vector2(ClaimAllWidth, 56f);

        ui.claimAllButton = claimAll.gameObject.AddComponent<Button>();
        ui.claimAllButton.targetGraphic = claimAll;

        // 灰掉时要看得出来 / The disabled tint has to be visible: this button spends most of its
        // life with nothing to claim, and the default near-white disabled colour would leave it
        // looking enabled against the accent fill.
        ColorBlock colors = ui.claimAllButton.colors;
        colors.disabledColor = new Color(0.62f, 0.64f, 0.67f, 1f);
        ui.claimAllButton.colors = colors;

        ui.claimAllLabel = NewText("Text", claimAllRect, 24, Color.white, TextAnchor.MiddleCenter);
        Stretch((RectTransform)ui.claimAllLabel.transform);
        ui.claimAllLabel.fontStyle = FontStyle.Bold;
        ui.claimAllLabel.text = "CLAIM ALL";

        // 累计分数条 / The board's running point total, counting only missions whose points have
        // been claimed. Cumulative, never deducted: a reward opens when this passes its threshold
        // and every cheaper reward stays open behind it.
        UnityEngine.UI.Image pointsGround = NewImage("PointsGround", header, Track);
        RectTransform pointsRect = (RectTransform)pointsGround.transform;
        pointsRect.anchorMin = new Vector2(1f, 0.5f);
        pointsRect.anchorMax = new Vector2(1f, 0.5f);
        pointsRect.pivot = new Vector2(1f, 0.5f);
        pointsRect.anchoredPosition = new Vector2(-(ClaimAllWidth + Gap), 0f);
        pointsRect.sizeDelta = new Vector2(PointsWidth, 46f);

        ui.pointsFill = NewFill("Fill", pointsRect, Accent);

        ui.pointsLabel = NewText("Label", pointsRect, 24, Color.white, TextAnchor.MiddleCenter);
        Stretch((RectTransform)ui.pointsLabel.transform);
        ui.pointsLabel.fontStyle = FontStyle.Bold;
        ui.pointsLabel.text = "0 / 0 PTS";

        RectTransform content = NewScroll(column, 12f);
        ui.missionTemplate = BuildMissionRowTemplate(content);
    }

    /// <summary>一行任务 / One mission row, inactive for the same reason the reward row is.</summary>
    private static RectTransform BuildMissionRowTemplate(RectTransform parent)
    {
        UnityEngine.UI.Image row = NewImage("MissionRowTemplate", parent, PaperDim);
        RectTransform rect = (RectTransform)row.transform;
        SetRowHeight(rect, MissionRowHeight);

        UnityEngine.UI.Image chip = NewImage("Chip", rect, TabOn);
        RectTransform chipRect = (RectTransform)chip.transform;
        chipRect.anchorMin = new Vector2(0f, 0.5f);
        chipRect.anchorMax = new Vector2(0f, 0.5f);
        chipRect.pivot = new Vector2(0f, 0.5f);
        chipRect.anchoredPosition = new Vector2(20f, 0f);
        chipRect.sizeDelta = new Vector2(156f, 36f);

        Text chipText = NewText("Text", chipRect, 18, Color.white, TextAnchor.MiddleCenter);
        Stretch((RectTransform)chipText.transform);
        chipText.text = "requirement";

        Text description = NewText("Description", rect, 26, Ink, TextAnchor.MiddleLeft);
        RectTransform descRect = (RectTransform)description.transform;
        Stretch(descRect);
        descRect.offsetMin = new Vector2(192f, 14f);
        descRect.offsetMax = new Vector2(-320f, -14f);
        description.text = "";

        // 报酬 / What the mission pays, and how far along it is - the reference puts both in one
        // dark plate on the right.
        UnityEngine.UI.Image award = NewImage("Award", rect, Slab);
        RectTransform awardRect = (RectTransform)award.transform;
        awardRect.anchorMin = new Vector2(1f, 0.5f);
        awardRect.anchorMax = new Vector2(1f, 0.5f);
        awardRect.pivot = new Vector2(1f, 0.5f);
        awardRect.anchoredPosition = new Vector2(-16f, 0f);
        awardRect.sizeDelta = new Vector2(AwardWidth, AwardHeight);

        Text value = NewText("Value", awardRect, 40, Accent, TextAnchor.MiddleRight);
        RectTransform valueRect = (RectTransform)value.transform;
        valueRect.anchorMin = new Vector2(0f, 0.44f);
        valueRect.anchorMax = Vector2.one;
        valueRect.offsetMin = new Vector2(14f, 0f);
        valueRect.offsetMax = new Vector2(-16f, -6f);
        value.fontStyle = FontStyle.Bold;
        value.text = "x0";

        UnityEngine.UI.Image progress = NewImage("Progress", awardRect, Track);
        RectTransform progressRect = (RectTransform)progress.transform;
        progressRect.anchorMin = Vector2.zero;
        progressRect.anchorMax = new Vector2(1f, 0.44f);
        progressRect.offsetMin = new Vector2(14f, 14f);
        progressRect.offsetMax = new Vector2(-14f, -4f);

        NewFill("Fill", progressRect, Accent);

        Text label = NewText("Label", progressRect, 20, Color.white, TextAnchor.MiddleCenter);
        Stretch((RectTransform)label.transform);
        label.fontStyle = FontStyle.Bold;
        label.text = "0/0";

        // 领取键占掉进度块的位置 / The claim button takes the Award block's exact rect rather than
        // sitting beside it. MissionUI shows one or the other, never both: a finished bar reading
        // 2/2 next to a button offering the same thing is one element too many, and there is no
        // room to the left of it without eating into the description.
        UnityEngine.UI.Image claim = NewImage("Claim", rect, Accent);
        RectTransform claimRect = (RectTransform)claim.transform;
        claimRect.anchorMin = new Vector2(1f, 0.5f);
        claimRect.anchorMax = new Vector2(1f, 0.5f);
        claimRect.pivot = new Vector2(1f, 0.5f);
        claimRect.anchoredPosition = new Vector2(-16f, 0f);
        claimRect.sizeDelta = new Vector2(AwardWidth, AwardHeight);

        Button claimButton = claim.gameObject.AddComponent<Button>();
        claimButton.targetGraphic = claim;

        Text claimLabel = NewText("Text", claimRect, 34, Color.white, TextAnchor.MiddleCenter);
        RectTransform claimLabelRect = (RectTransform)claimLabel.transform;
        claimLabelRect.anchorMin = new Vector2(0f, 0.38f);
        claimLabelRect.anchorMax = Vector2.one;
        claimLabelRect.offsetMin = Vector2.zero;
        claimLabelRect.offsetMax = Vector2.zero;
        claimLabel.fontStyle = FontStyle.Bold;
        claimLabel.text = "CLAIM";

        // MissionUI writes the points here, so the button says what the press is actually worth.
        Text claimPoints = NewText("Points", claimRect, 22, new Color(1f, 1f, 1f, 0.85f), TextAnchor.UpperCenter);
        RectTransform claimPointsRect = (RectTransform)claimPoints.transform;
        claimPointsRect.anchorMin = Vector2.zero;
        claimPointsRect.anchorMax = new Vector2(1f, 0.38f);
        claimPointsRect.offsetMin = Vector2.zero;
        claimPointsRect.offsetMax = Vector2.zero;
        claimPoints.text = "+0 PTS";

        claim.gameObject.SetActive(false);

        // 任务行完成后仍要读得清 / A mission still has to be readable when done, so only a faint
        // wash and a tag sitting where the "requirement" chip is.
        BuildDoneStamp(rect, "✓ DONE",
                       new Color(Accent.r, Accent.g, Accent.b, 0.14f), Accent,
                       new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                       new Vector2(20f, 0f), new Vector2(156f, 36f));

        rect.gameObject.SetActive(false);
        return rect;
    }

    /// <summary>
    /// 完成标记 / The "done" state: a wash over the whole row plus a plate carrying the label.
    ///
    /// 两种行摆法不同 / The two row kinds place it differently, which is why the anchor is a
    /// parameter. A mission keeps its text readable and only wants a tag where its "requirement"
    /// chip sits, so its wash is faint and its plate is on the left. A claimed reward is finished
    /// business, so it gets a real scrim and the plate sits across the middle - the same way the
    /// reference greys a spent reward set out entirely rather than annotating it.
    ///
    /// 别把牌子叠在徽章上 / What it must not do is straddle the row's own left badge: a plate wider
    /// and shorter than the badge leaves the threshold number poking out above and below it, which
    /// reads as a rendering fault rather than as a stamp.
    /// </summary>
    private static void BuildDoneStamp(RectTransform row, string label, Color wash, Color plateColor,
                                       Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
                                       Vector2 position, Vector2 size)
    {
        // 压暗要靠这层, 不能靠 CanvasGroup / The fading is this layer's job, not a CanvasGroup on
        // the row. A group fades the plate along with everything else, and a half-transparent
        // plate lets the item name underneath read straight through the word on it.
        UnityEngine.UI.Image done = NewImage("Done", row, wash);
        RectTransform doneRect = (RectTransform)done.transform;
        Stretch(doneRect);
        done.raycastTarget = false;

        UnityEngine.UI.Image plate = NewImage("Plate", doneRect, plateColor);
        RectTransform plateRect = (RectTransform)plate.transform;
        plateRect.anchorMin = anchorMin;
        plateRect.anchorMax = anchorMax;
        plateRect.pivot = pivot;
        plateRect.anchoredPosition = position;
        plateRect.sizeDelta = size;
        plate.raycastTarget = false;

        Text text = NewText("Text", plateRect, 22, Color.white, TextAnchor.MiddleCenter);
        Stretch((RectTransform)text.transform);
        text.fontStyle = FontStyle.Bold;
        text.text = label;

        done.gameObject.SetActive(false);
    }

    // --------------------------------------------------------------- reward popup

    private static void BuildRewardPopup()
    {
        GameObject root = new GameObject("MissionRewardUI",
            typeof(RectTransform), typeof(CanvasGroup), typeof(UI.Sub.MissionRewardUI));
        RectTransform rootRect = (RectTransform)root.transform;
        Stretch(rootRect);

        UI.Sub.MissionRewardUI ui = root.GetComponent<UI.Sub.MissionRewardUI>();

        // 点背景关闭 / Tapping the backdrop dismisses, the way it does on the item popup. The
        // reward is already in the bag by the time this is up, so dismissing costs nothing.
        UnityEngine.UI.Image blur = NewImage("Blur", rootRect, new Color(0.70f, 0.71f, 0.73f, 1f));
        Stretch((RectTransform)blur.transform);
        blur.material = BlurMaterial();
        blur.gameObject.AddComponent<UI.UiBlurCapture>();

        Button dismiss = blur.gameObject.AddComponent<Button>();
        dismiss.targetGraphic = blur;
        UnityEditor.Events.UnityEventTools.AddBoolPersistentListener(dismiss.onClick, ui.Hide, true);

        ui.blur = blur;

        UnityEngine.UI.Image panelImage = NewImage("Panel", rootRect, Paper);
        RectTransform panel = (RectTransform)panelImage.transform;
        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(940f, 620f);
        panel.anchoredPosition = Vector2.zero;

        UnityEngine.UI.Image titleGround = NewImage("TitleGround", panel, TabOn);
        RectTransform titleRect = (RectTransform)titleGround.transform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -32f);
        titleRect.sizeDelta = new Vector2(-72f, 64f);

        ui.title = NewText("Title", titleRect, 32, Color.white, TextAnchor.MiddleCenter);
        Stretch((RectTransform)ui.title.transform);
        ui.title.fontStyle = FontStyle.Bold;
        ui.title.text = "MISSION REWARD";

        Text caption = NewText("Caption", panel, 24, InkDim, TextAnchor.MiddleCenter);
        RectTransform captionRect = (RectTransform)caption.transform;
        captionRect.anchorMin = new Vector2(0f, 1f);
        captionRect.anchorMax = new Vector2(1f, 1f);
        captionRect.pivot = new Vector2(0.5f, 1f);
        captionRect.anchoredPosition = new Vector2(0f, -104f);
        captionRect.sizeDelta = new Vector2(-72f, 36f);
        caption.text = "Added to your Depot.";

        ui.cellTemplate = BuildRewardCells(panel);

        UnityEngine.UI.Image close = NewImage("CloseButton", panel, Slab);
        RectTransform closeRect = (RectTransform)close.transform;
        closeRect.anchorMin = new Vector2(0.5f, 0f);
        closeRect.anchorMax = new Vector2(0.5f, 0f);
        closeRect.pivot = new Vector2(0.5f, 0f);
        closeRect.anchoredPosition = new Vector2(0f, 36f);
        closeRect.sizeDelta = new Vector2(340f, 72f);
        AddEdges(closeRect);

        Button closeButton = close.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = close;
        UnityEditor.Events.UnityEventTools.AddBoolPersistentListener(closeButton.onClick, ui.Hide, true);

        Text closeLabel = NewText("Text", closeRect, 30, Color.white, TextAnchor.MiddleCenter);
        Stretch((RectTransform)closeLabel.transform);
        closeLabel.fontStyle = FontStyle.Bold;
        closeLabel.text = "OK";

        Save(root, "MissionRewardUI");
    }

    /// <summary>
    /// 奖励格子 / The grid the popup lists its items in. It scrolls, because a run that finished
    /// several missions at once can hand over more rewards than fit across the card.
    /// </summary>
    private static RectTransform BuildRewardCells(RectTransform panel)
    {
        RectTransform scrollRoot = NewRect("Body", panel);
        scrollRoot.anchorMin = Vector2.zero;
        scrollRoot.anchorMax = Vector2.one;
        scrollRoot.offsetMin = new Vector2(36f, 128f);
        scrollRoot.offsetMax = new Vector2(-36f, -156f);

        RectTransform viewport = NewViewport(scrollRoot);

        RectTransform content = NewRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;

        GridLayoutGroup grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(200f, 220f);
        grid.spacing = new Vector2(20f, 20f);
        grid.padding = new RectOffset(8, 8, 8, 8);
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;

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

        return BuildRewardCellTemplate(content);
    }

    private static RectTransform BuildRewardCellTemplate(RectTransform parent)
    {
        RectTransform cell = NewRect("RewardCellTemplate", parent);
        cell.sizeDelta = new Vector2(200f, 220f);

        // 弹窗里的图标不可点 / No Button here, deliberately. UiBlurCapture shares one material and
        // one render texture for the session, so opening ItemInfoUI on top of this popup would
        // have the two fighting over that single capture.
        UnityEngine.UI.Image ground = NewImage("Icon", cell, Color.white);
        RectTransform groundRect = (RectTransform)ground.transform;
        groundRect.anchorMin = new Vector2(0.5f, 1f);
        groundRect.anchorMax = new Vector2(0.5f, 1f);
        groundRect.pivot = new Vector2(0.5f, 1f);
        groundRect.anchoredPosition = new Vector2(0f, -8f);
        groundRect.sizeDelta = new Vector2(148f, 148f);

        UnityEngine.UI.Image icon = NewImage("Icon", groundRect, Color.white);
        RectTransform iconRect = (RectTransform)icon.transform;
        Stretch(iconRect);
        iconRect.offsetMin = new Vector2(20f, 20f);
        iconRect.offsetMax = new Vector2(-20f, -20f);
        icon.raycastTarget = false;
        icon.preserveAspect = true;

        Text amount = NewText("Amount", groundRect, 28, Color.white, TextAnchor.LowerRight);
        RectTransform amountRect = (RectTransform)amount.transform;
        amountRect.anchorMin = Vector2.zero;
        amountRect.anchorMax = new Vector2(1f, 0f);
        amountRect.pivot = new Vector2(0.5f, 0f);
        amountRect.sizeDelta = new Vector2(-10f, 38f);
        amountRect.anchoredPosition = new Vector2(0f, 6f);
        amount.fontStyle = FontStyle.Bold;

        ItemIconComponent component = ground.gameObject.AddComponent<ItemIconComponent>();
        component.ground = ground;
        component.icon = icon;
        component.amount = amount;

        Text label = NewText("Name", cell, 22, Ink, TextAnchor.UpperCenter);
        RectTransform labelRect = (RectTransform)label.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = new Vector2(1f, 0f);
        labelRect.pivot = new Vector2(0.5f, 0f);
        labelRect.anchoredPosition = new Vector2(0f, 4f);
        labelRect.sizeDelta = new Vector2(-4f, 56f);
        label.text = "";

        cell.gameObject.SetActive(false);
        return cell;
    }

    // ---------------------------------------------------------------------- wiring

    private static bool HookMissionsTile()
    {
        string homePath = UiPrefabDir + "/HomeUI.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(homePath) == null)
        {
            Debug.LogWarning($"[MissionUISetup] No HomeUI prefab at {homePath}, so nothing opens the mission board yet.");
            return false;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(homePath);

        try
        {
            Transform tile = FindDeep(contents.transform, MissionsTileName);
            if (tile == null)
            {
                Debug.LogWarning($"[MissionUISetup] HomeUI has no '{MissionsTileName}' child.");
                return false;
            }

            // 透明图也能接收点击 / An alpha-zero Image is still a raycast target, which is exactly
            // what makes a tile clickable without drawing anything over the art behind it.
            if (tile.GetComponent<Graphic>() == null)
            {
                NewImage("Hit", (RectTransform)tile, new Color(0f, 0f, 0f, 0f));
            }

            Button button = tile.GetComponent<Button>() ?? tile.gameObject.AddComponent<Button>();
            button.targetGraphic = tile.GetComponent<Graphic>();

            OpenUIButton open = tile.GetComponent<OpenUIButton>() ?? tile.gameObject.AddComponent<OpenUIButton>();
            open.uiName = UI.Sub.MissionUI.UIName;

            PrefabUtility.SaveAsPrefabAsset(contents, homePath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // --------------------------------------------------------------------- helpers

    private static bool GuardEditMode()
    {
        if (!Application.isPlaying) return true;
        Debug.LogError("[MissionUISetup] Exit Play Mode first.");
        return false;
    }

    private static void Save(GameObject root, string name)
    {
        Directory.CreateDirectory(UiPrefabDir);
        PrefabUtility.SaveAsPrefabAsset(root, $"{UiPrefabDir}/{name}.prefab");
        Object.DestroyImmediate(root);
    }

    /// <summary>
    /// 行高 / Pins a row's height for the layout group above it.
    ///
    /// 必须走 LayoutElement / The number has to reach the group through a LayoutElement: the group
    /// controls height, so it rewrites the rect and a height set only on sizeDelta is lost on the
    /// first rebuild.
    /// </summary>
    private static void SetRowHeight(RectTransform rect, float height)
    {
        rect.sizeDelta = new Vector2(0f, height);

        LayoutElement element = rect.gameObject.AddComponent<LayoutElement>();
        element.minHeight = height;
        element.preferredHeight = height;
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

    /// <summary>
    /// 进度条的填充 / A progress fill, driven by its right anchor rather than by fillAmount.
    ///
    /// 不用 Image.Filled / Deliberately not Image.Type.Filled. Filled needs a sprite - with none,
    /// Image ignores `type` and draws the whole rect - and the only sprite always on hand is the
    /// builtin UISprite, which is a 9-sliced rounded rect. Filled does not slice: it stretches the
    /// whole sprite, so the rounded caps smear into a blurry pill instead of a bar.
    ///
    /// 锚点缩放没有这个问题 / Moving anchorMax.x has none of that. No sprite, no asset, and the bar
    /// is a crisp rectangle at every width. MissionUI.SetFill is the other half of this.
    /// </summary>
    private static UnityEngine.UI.Image NewFill(string name, RectTransform parent, Color color)
    {
        UnityEngine.UI.Image fill = NewImage(name, parent, color);
        RectTransform rect = (RectTransform)fill.transform;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(0f, 1f);   // empty until something draws it
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0f, 0.5f);

        fill.raycastTarget = false;
        return fill;
    }

    private static Text NewText(string name, Transform parent, int size, Color color, TextAnchor anchor)
    {
        Text text = NewRect(name, parent).gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = anchor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.text = "";
        return text;
    }

    /// <summary>
    /// 一个竖向滚动列表 / A vertical scroll list: ScrollRect on the given root, below the header,
    /// with the content sized by its own fitter.
    ///
    /// 行高来自 LayoutElement / Row height comes from the LayoutElement on each row template, which
    /// is why childControlHeight is on: with it off the group reads sizeDelta directly and the
    /// height depends on anchors the group itself is busy rewriting.
    /// </summary>
    private static RectTransform NewScroll(RectTransform column, float spacing)
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

        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.padding = new RectOffset(0, 0, 0, 8);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

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

        return content;
    }

    /// <summary>
    /// 遮罩图必须不透明 / A ScrollRect viewport: an Image that is never drawn, only used as the
    /// mask shape.
    ///
    /// 透明度必须是1 / The alpha has to be opaque even though nothing renders. Mask writes the
    /// stencil from the graphic through an alpha clip, so a nearly-transparent image writes
    /// nothing and clips away every child - an empty list with no error to explain it.
    /// </summary>
    private static RectTransform NewViewport(RectTransform parent)
    {
        UnityEngine.UI.Image image = NewImage("Viewport", parent, Color.white);
        RectTransform viewport = (RectTransform)image.transform;
        Stretch(viewport);

        Mask mask = viewport.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

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
        NewEdge(parent, "EdgeTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(0f, 2f));
        NewEdge(parent, "EdgeBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(0f, 2f));
        NewEdge(parent, "EdgeLeft", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(2f, 0f));
        NewEdge(parent, "EdgeRight", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-1f, 0f), new Vector2(2f, 0f));
    }

    private static void NewEdge(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                                Vector2 position, Vector2 size)
    {
        UnityEngine.UI.Image edge = NewImage(name, parent, Edge);
        RectTransform rect = (RectTransform)edge.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        edge.raycastTarget = false;
    }

    private static Material BlurMaterial()
    {
        string path = MaterialDir + "/UIBlur.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find(BlurShader);
        if (shader == null)
        {
            Debug.LogWarning($"[MissionUISetup] Shader '{BlurShader}' not found; the popup backdrop will not blur.");
            return null;
        }

        Directory.CreateDirectory(MaterialDir);
        Material material = new Material(shader);
        AssetDatabase.CreateAsset(material, path);
        return material;
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
