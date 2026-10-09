using System.Collections.Generic;
using System.IO;
using Data.Gacha;
using TMPro;
using UI.Sub;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 寻访界面 / Builds the headhunting screen, its banner data, and the HomeUI hook that opens it.
///
/// Layout follows the reference wish screen - tabs across the top, wallet top right, one large
/// banner card in the middle with the rules on the left and the operator art on the right, shop
/// and details bottom left, the two pull buttons bottom right - in the project's dark palette so
/// it sits with SongSelect and CharSelect rather than standing apart from them.
///
/// The canvas matches HEIGHT, so width floats 1920-2400. Everything here is anchored to the edge
/// it belongs to; nothing is placed by an absolute x from the centre except the card itself,
/// which is narrower than the narrowest screen.
/// </summary>
public static class GachaUISetup
{
    private const string UiPrefabDir = "Assets/Arknights/Resources/Prefab/UI";
    private const string DataDir = "Assets/Arknights/Resources/Data/Gacha";
    private const string LibraryPath = DataDir + "/GachaBannerLibrary.asset";
    private const string EventPath = DataDir + "/Banner_Event_Amiya.asset";
    private const string StandardPath = DataDir + "/Banner_Standard.asset";
    private const string HeadhuntTileName = "HeadhuntTile";

    private static readonly string[] Roster = { "AMIYA", "NOVA", "ECHO", "PULSE" };

    private static readonly Vector2 CardSize = new Vector2(1280f, 660f);
    private const float InfoWidth = 520f;

    // 和 SongSelect / CharSelect 同一套 / The same palette as SongSelect and CharSelect - except
    // that the panels here are fully opaque, where theirs are 0.97.
    //
    // 线性空间里 3% 也看得见 / The project renders in linear colour space, so blending happens
    // there. This dark panel is about 0.007 in linear terms; 3% of white text behind it adds 0.03,
    // several times the panel's own value, and converted back to gamma that reads as clearly
    // ghosted text. Measured on the details panel over the banner card: brightest pixel 0.142
    // against 0.092 for the bare panel at alpha 0.97, identical at 1.0. A panel meant to hide what
    // is behind it has to be exactly 1.
    private static readonly Color Ink = new Color(0.05f, 0.06f, 0.08f, 1f);
    private static readonly Color PanelInk = new Color(0.08f, 0.09f, 0.11f, 1f);
    private static readonly Color InfoInk = new Color(0.11f, 0.12f, 0.15f, 1f);
    private static readonly Color Strip = new Color(0.10f, 0.11f, 0.12f, 0.92f);
    private static readonly Color Slab = new Color(0.30f, 0.31f, 0.33f, 0.90f);
    private static readonly Color Edge = new Color(1f, 1f, 1f, 0.30f);
    private static readonly Color Faint = new Color(1f, 1f, 1f, 0.10f);
    private static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);
    private static readonly Color Accent = new Color(0.17f, 0.56f, 0.90f, 1f);
    private static readonly Color Orundum = new Color(0.86f, 0.30f, 0.45f, 1f);
    private static readonly Color Originite = new Color(0.90f, 0.72f, 0.28f, 1f);

    // ------------------------------------------------------------------ menu items

    [MenuItem("Arknights/Gacha/Build Gacha UI", false, 1)]
    public static void BuildGachaUI()
    {
        if (!GuardEditMode()) return;

        GachaBannerLibrary library = EnsureData(false, out string dataSummary);

        string path = $"{UiPrefabDir}/{GachaUI.UIName}.prefab";
        bool built = false;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            Build(library);
            built = true;
        }

        bool hooked = HookHeadhuntTile();

        Debug.Log($"[GachaUISetup] {(built ? "Built" : "Kept")} {path}\n" +
                  $"  data: {dataSummary}\n" +
                  $"  HomeUI {HeadhuntTileName}: {(hooked ? "opens " + GachaUI.UIName : "NOT hooked - see warning above")}\n" +
                  (built ? "" : "  Use Arknights/Gacha/Rebuild Gacha UI to regenerate the prefab."));
    }

    /// <summary>
    /// 推倒重建界面 / Deletes the prefab and builds it again. Banner data is left alone - it is
    /// content, and has its own reset below.
    /// </summary>
    [MenuItem("Arknights/Gacha/Rebuild Gacha UI", false, 20)]
    public static void RebuildGachaUI()
    {
        if (!GuardEditMode()) return;

        AssetDatabase.DeleteAsset($"{UiPrefabDir}/{GachaUI.UIName}.prefab");
        BuildGachaUI();
    }

    /// <summary>
    /// 卡池数据回到默认 / Overwrites the two seeded banners with their defaults, in place, so the
    /// library and the prefab keep pointing at the same assets.
    /// </summary>
    [MenuItem("Arknights/Gacha/Reset Banner Data To Default", false, 21)]
    public static void ResetBannerData()
    {
        if (!GuardEditMode()) return;

        EnsureData(true, out string summary);
        Debug.Log($"[GachaUISetup] Banner data reset to default: {summary}");
    }

    // ------------------------------------------------------------------------ data

    private static GachaBannerLibrary EnsureData(bool reset, out string summary)
    {
        Directory.CreateDirectory(DataDir);

        GachaBanner eventBanner = FindOrCreate<GachaBanner>(EventPath, out bool eventMade);
        if (eventMade || reset) SeedEvent(eventBanner);

        GachaBanner standard = FindOrCreate<GachaBanner>(StandardPath, out bool standardMade);
        if (standardMade || reset) SeedStandard(standard);

        GachaBannerLibrary library = FindOrCreate<GachaBannerLibrary>(LibraryPath, out bool libraryMade);

        // 只补缺的 / Adds what is missing and leaves the existing order alone, so a banner added
        // or reordered by hand survives a re-run.
        if (!library.banners.Contains(eventBanner)) library.banners.Add(eventBanner);
        if (!library.banners.Contains(standard)) library.banners.Add(standard);
        library.banners.RemoveAll(b => b == null);

        EditorUtility.SetDirty(eventBanner);
        EditorUtility.SetDirty(standard);
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();

        summary = $"event {(eventMade ? "created" : reset ? "reset" : "kept")}, " +
                  $"standard {(standardMade ? "created" : reset ? "reset" : "kept")}, " +
                  $"library {(libraryMade ? "created" : "kept")} ({library.banners.Count} banners)";
        return library;
    }

    /// <summary>
    /// 只有一个五星, 所以不写"概率提升" / The roster has exactly one 5-star, so an event banner
    /// cannot honestly claim raised odds - both banners roll the same pool. The copy says what is
    /// true: AMIYA is featured, the odds match the standard pool.
    /// </summary>
    private static void SeedEvent(GachaBanner banner)
    {
        banner.bannerId = "event_amiya";
        banner.kind = GachaBannerKind.Event;
        banner.title = "Resonant\nDawn";
        banner.headline = "Featured operator";
        banner.description = "AMIYA leads this event. Every operator in the standard pool can appear " +
                             "as well, at the same odds - see Details.";
        banner.featuredCharId = "AMIYA";
        banner.poolCharIds = (string[])Roster.Clone();
        banner.startsAtUtc = "2026-10-01T00:00:00Z";
        banner.endsAtUtc = "2026-12-31T23:59:59Z";
        SeedRules(banner);
    }

    private static void SeedStandard(GachaBanner banner)
    {
        banner.bannerId = "standard";
        banner.kind = GachaBannerKind.Standard;
        banner.title = "Standard\nHeadhunting";
        banner.headline = "Always open";
        banner.description = "The permanent pool. Every operator can appear, and its pity is counted " +
                             "separately from event banners.";
        banner.featuredCharId = "";
        banner.poolCharIds = (string[])Roster.Clone();
        banner.startsAtUtc = "";
        banner.endsAtUtc = "";
        SeedRules(banner);
    }

    // GDD 的数字 / The GDD's numbers: 2 / 10 / 88, pity at 30, 180 Orundum a pull.
    private static void SeedRules(GachaBanner banner)
    {
        banner.rateFiveStar = 0.02f;
        banner.rateFourStar = 0.10f;
        banner.rateThreeStar = 0.88f;
        banner.pityThreshold = 30;
        banner.currencyItemId = 1;
        banner.costSingle = 180;
        banner.costMulti = 1800;
    }

    private static T FindOrCreate<T>(string path, out bool created) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        created = asset == null;
        if (!created) return asset;

        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    // ---------------------------------------------------------------------- build

    private static void Build(GachaBannerLibrary library)
    {
        GameObject root = new GameObject(GachaUI.UIName,
            typeof(RectTransform), typeof(CanvasGroup), typeof(GachaUI));
        RectTransform rootRect = (RectTransform)root.transform;
        Stretch(rootRect);

        GachaUI ui = root.GetComponent<GachaUI>();
        ui.library = library;

        UnityEngine.UI.Image backdrop = NewImage("Backdrop", rootRect, Ink, true);
        Stretch((RectTransform)backdrop.transform);

        // 背景一道斜光 / One faint diagonal band behind the card, for depth without art.
        UnityEngine.UI.Image band = NewImage("Band", rootRect, new Color(Accent.r, Accent.g, Accent.b, 0.07f), false);
        Place((RectTransform)band.transform, Center, Center, Center, new Vector2(0f, -20f), new Vector2(3000f, 420f));
        band.transform.localRotation = Quaternion.Euler(0f, 0f, 12f);

        BuildHeader(rootRect, ui);
        BuildArrows(rootRect, ui);
        BuildCard(rootRect, ui);
        BuildBottomLeft(rootRect, ui);
        BuildPullButtons(rootRect, ui);
        BuildDetails(rootRect, ui);

        Directory.CreateDirectory(UiPrefabDir);
        PrefabUtility.SaveAsPrefabAsset(root, $"{UiPrefabDir}/{GachaUI.UIName}.prefab");
        Object.DestroyImmediate(root);
    }

    // --------------------------------------------------------------------- header

    private static void BuildHeader(RectTransform root, GachaUI ui)
    {
        // 左上返回键 + 标题牌 / Back slab top left with the title plate beside it - the same
        // 170x68 slab at (48, -40) and the same plate SongSelect, CharSelect and Mission use, so
        // leaving every screen is the same gesture in the same corner.
        ui.backButton = SquareButton("BackButton", root, TopLeft, new Vector2(48f, -40f), new Vector2(170f, 68f), "‹", 46f);

        UnityEngine.UI.Image plate = NewImage("Title", root, Strip, false);
        Place((RectTransform)plate.transform, TopLeft, TopLeft, TopLeft, new Vector2(48f + 170f + 8f, -40f), new Vector2(340f, 68f));
        TextMeshProUGUI title = NewText("Text", plate.transform, 28f, Color.white, TextAlignmentOptions.Center);
        Stretch(title.rectTransform);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 3f;
        title.text = "HEADHUNTING";

        // 页签 / Banner tabs, top centre. Laid out by the row; the screen clones the template.
        RectTransform row = NewRect("TabRow", root);
        // 和返回键同一条中线 / Centred on the back slab's line at -74 (-32 - 84/2), like the title
        // plate and the wallet, so the whole top row reads as one bar.
        Place(row, TopCenter, TopCenter, TopCenter, new Vector2(0f, -32f), new Vector2(900f, 84f));
        HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 28f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        ui.tabRow = row;
        ui.tabTemplate = BuildTabTemplate(row);

        // 钱包 / Wallet, top right: Originite with its +, then Orundum. Centred on the back slab's
        // row - a 64px pill at -42 shares the slab's centre line at -74.
        RectTransform originite = Pill("OriginitePill", root, new Vector2(-48f, -42f), Originite, out ui.originiteAmount);
        ui.originitePlus = SquareButton("Plus", originite, MiddleRight, new Vector2(-10f, 0f), new Vector2(44f, 44f), "+", 34f);

        Pill("OrundumPill", root, new Vector2(-48f - 250f - 20f, -42f), Orundum, out ui.orundumAmount);
    }

    /// <summary>
    /// 页签模板 / One tab. The node names Frame, Crop/Thumb and Label are the contract with
    /// GachaUI's TabView - rename one here and the screen throws on open.
    /// </summary>
    private static RectTransform BuildTabTemplate(RectTransform row)
    {
        RectTransform tab = NewRect("TabTemplate", row);
        tab.sizeDelta = new Vector2(200f, 80f);

        UnityEngine.UI.Image frame = NewImage("Frame", tab, new Color(1f, 1f, 1f, 0.25f), true);
        Stretch((RectTransform)frame.transform);

        UnityEngine.UI.Image fill = NewImage("Fill", tab, PanelInk, false);
        Inset((RectTransform)fill.transform, 4f);

        // 头像铺满再裁 / The avatar covers the tab and is cropped to it, the way the reference
        // crops its art, instead of shrinking a square into the strip's 68px height. GachaUI sets
        // the fitter's ratio from the sprite, so a non-square avatar still crops correctly.
        RectTransform crop = NewRect("Crop", tab);
        Inset(crop, 6f);
        crop.gameObject.AddComponent<RectMask2D>();

        UnityEngine.UI.Image thumb = NewImage("Thumb", crop, Color.white, false);
        AspectRatioFitter cover = thumb.gameObject.AddComponent<AspectRatioFitter>();
        cover.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        cover.aspectRatio = 1f;

        TextMeshProUGUI label = NewText("Label", tab, 22f, Color.white, TextAlignmentOptions.Center);
        Stretch(label.rectTransform);
        label.fontStyle = FontStyles.Bold;
        label.characterSpacing = 3f;

        Button button = tab.gameObject.AddComponent<Button>();
        button.targetGraphic = frame;

        tab.gameObject.SetActive(false);
        return tab;
    }

    private static RectTransform Pill(string name, RectTransform root, Vector2 position, Color gem,
                                      out TextMeshProUGUI amount)
    {
        UnityEngine.UI.Image pill = NewImage(name, root, Strip, false);
        RectTransform rect = (RectTransform)pill.transform;
        Place(rect, TopRight, TopRight, TopRight, position, new Vector2(250f, 64f));
        AddEdges(rect, Faint, 2f);

        Diamond("Gem", rect, new Vector2(34f, 0f), gem, 26f);

        amount = NewText("Amount", rect, 30f, Color.white, TextAlignmentOptions.Left);
        Place(amount.rectTransform, MiddleLeft, MiddleLeft, MiddleLeft, new Vector2(62f, 0f), new Vector2(130f, 48f));
        amount.text = "0";
        return rect;
    }

    // --------------------------------------------------------------------- arrows

    private static void BuildArrows(RectTransform root, GachaUI ui)
    {
        ui.prevButton = SquareButton("PrevButton", root, MiddleLeft, new Vector2(48f, 0f), new Vector2(72f, 128f), "‹", 64f);
        ui.nextButton = SquareButton("NextButton", root, MiddleRight, new Vector2(-48f, 0f), new Vector2(72f, 128f), "›", 64f);
    }

    // ----------------------------------------------------------------------- card

    private static void BuildCard(RectTransform root, GachaUI ui)
    {
        // 外框不裁切, 让种类标签压在卡片上沿 / The frame does not clip, so the kind tag can sit
        // across the card's top edge the way the reference does. Only the inner card masks.
        RectTransform frame = NewRect("CardFrame", root);
        Place(frame, Center, Center, Center, new Vector2(0f, -10f), CardSize);

        // 整张卡可以左右滑 / The whole card - kind tag and edges included - moves with a swipe, and
        // fades across a page change through this group.
        CanvasGroup cardFade = frame.gameObject.AddComponent<CanvasGroup>();
        UI.SwipePager pager = frame.gameObject.AddComponent<UI.SwipePager>();
        pager.target = frame;
        pager.fade = cardFade;
        ui.swipe = pager;

        RectTransform card = NewRect("Card", frame);
        Stretch(card);
        card.gameObject.AddComponent<RectMask2D>();

        // 卡面接收拖动 / The face is the card's one raycast target: drag events start here and
        // bubble up to the SwipePager on the frame. Nothing on the card is a button, so taking
        // the touch costs nothing.
        UnityEngine.UI.Image face = NewImage("Face", card, PanelInk, true);
        Stretch((RectTransform)face.transform);

        BuildArt(card, ui);
        BuildInfo(card, ui);

        AddEdges(frame, Edge, 2f);

        // 标签宽度随文字 / The tag sizes itself to its text. A fixed width wrapped
        // "STANDARD HEADHUNTING" onto two lines and out of the tag; a fitted one also survives
        // whatever banner kinds or translations come later.
        UnityEngine.UI.Image tag = NewImage("KindTag", frame, Accent, false);
        Place((RectTransform)tag.transform, TopLeft, TopLeft, new Vector2(0f, 0.5f), new Vector2(-6f, 0f), new Vector2(340f, 46f));
        HorizontalLayoutGroup tagLayout = tag.gameObject.AddComponent<HorizontalLayoutGroup>();
        tagLayout.padding = new RectOffset(22, 22, 0, 0);
        tagLayout.childAlignment = TextAnchor.MiddleCenter;
        tagLayout.childControlWidth = true;
        tagLayout.childControlHeight = true;
        tagLayout.childForceExpandWidth = false;
        tagLayout.childForceExpandHeight = true;
        ContentSizeFitter tagFit = tag.gameObject.AddComponent<ContentSizeFitter>();
        tagFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        tagFit.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        ui.kindLabel = NewText("Text", tag.transform, 24f, Color.white, TextAlignmentOptions.Center);
        ui.kindLabel.fontStyle = FontStyles.Bold;
        ui.kindLabel.characterSpacing = 2f;
        ui.kindLabel.textWrappingMode = TextWrappingModes.NoWrap;
    }

    private static void BuildArt(RectTransform card, GachaUI ui)
    {
        RectTransform art = NewRect("Art", card);
        art.anchorMin = Vector2.zero;
        art.anchorMax = Vector2.one;
        art.offsetMin = new Vector2(InfoWidth, 0f);
        art.offsetMax = Vector2.zero;

        UnityEngine.UI.Image glow = NewImage("Glow", art, new Color(Accent.r, Accent.g, Accent.b, 0.16f), false);
        Stretch((RectTransform)glow.transform);

        // 主推身后的阵容 / The rest of the pool, smaller, behind the featured operator.
        ui.lineupBehind = Lineup("LineupBehind", art, BottomRight, new Vector2(1f, 0f), new Vector2(-10f, -40f),
            new Vector2(520f, 520f), -150f, TextAnchor.LowerRight, new Vector2(300f, 450f), out ui.lineupBehindTemplate);

        // 没有主推时整排展示 / With no featured operator, the whole pool fanned like a hand of
        // cards. Sized so four fit inside the art area: 4 x 300 - 3 x 160 = 720 of the 760 there
        // is. Wider cards or less overlap push the outer two under the info column and past the
        // card's clip.
        ui.lineupFull = Lineup("LineupFull", art, BottomCenter, new Vector2(0.5f, 0f), new Vector2(0f, 60f),
            new Vector2(740f, 480f), -160f, TextAnchor.LowerCenter, new Vector2(300f, 450f), out ui.lineupFullTemplate);

        ui.featuredImage = NewImage("Featured", art, Color.white, false);
        Place(ui.featuredImage.rectTransform, BottomLeft, BottomLeft, BottomLeft, new Vector2(40f, -70f), new Vector2(533f, 800f));
        ui.featuredImage.preserveAspect = true;

        BuildPlate(art, ui);
    }

    private static RectTransform Lineup(string name, RectTransform art, Vector2 anchor, Vector2 pivot,
                                        Vector2 position, Vector2 size, float spacing, TextAnchor align,
                                        Vector2 cell, out UnityEngine.UI.Image template)
    {
        RectTransform row = NewRect(name, art);
        Place(row, anchor, anchor, pivot, position, size);

        HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = align;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        template = NewImage("Template", row, new Color(1f, 1f, 1f, 0.85f), false);
        template.rectTransform.sizeDelta = cell;
        template.preserveAspect = true;
        template.gameObject.SetActive(false);
        return row;
    }

    private static void BuildPlate(RectTransform art, GachaUI ui)
    {
        RectTransform plate = NewRect("Plate", art);
        Place(plate, BottomLeft, BottomLeft, BottomLeft, new Vector2(330f, 60f), new Vector2(420f, 170f));
        ui.featuredPlate = plate.gameObject;

        UnityEngine.UI.Image back = NewImage("Back", plate, Strip, false);
        Place((RectTransform)back.transform, new Vector2(0f, 0.32f), Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        ui.featuredName = NewText("Name", plate, 46f, Color.white, TextAlignmentOptions.Left);
        Place(ui.featuredName.rectTransform, TopLeft, TopRight, TopLeft, new Vector2(20f, -6f), new Vector2(-40f, 60f));
        ui.featuredName.fontStyle = FontStyles.Bold;

        ui.featuredStars = NewImage("Stars", plate, Color.white, false);
        Place(ui.featuredStars.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(20f, -70f), new Vector2(170f, 34f));
        ui.featuredStars.preserveAspect = true;

        UnityEngine.UI.Image epithetBack = NewImage("EpithetBack", plate, new Color(Accent.r, Accent.g, Accent.b, 0.85f), false);
        Place((RectTransform)epithetBack.transform, BottomLeft, BottomLeft, BottomLeft, new Vector2(20f, 0f), new Vector2(300f, 44f));
        ui.featuredEpithet = NewText("Epithet", epithetBack.transform, 24f, Color.white, TextAlignmentOptions.Left);
        Inset(ui.featuredEpithet.rectTransform, 12f);

        UnityEngine.UI.Image badge = NewImage("UpBadge", plate, new Color(0.95f, 0.78f, 0.25f, 1f), false);
        Place((RectTransform)badge.transform, TopRight, TopRight, new Vector2(0.5f, 0.5f), new Vector2(-30f, 0f), new Vector2(84f, 40f));
        badge.transform.localRotation = Quaternion.Euler(0f, 0f, 8f);
        TextMeshProUGUI up = NewText("Text", badge.transform, 26f, Ink, TextAlignmentOptions.Center);
        Stretch(up.rectTransform);
        up.fontStyle = FontStyles.Bold;
        up.text = "UP!";
        ui.upBadge = badge.gameObject;
    }

    private static void BuildInfo(RectTransform card, GachaUI ui)
    {
        UnityEngine.UI.Image info = NewImage("Info", card, InfoInk, false);
        RectTransform rect = (RectTransform)info.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.sizeDelta = new Vector2(InfoWidth, 0f);
        rect.anchoredPosition = Vector2.zero;

        const float pad = 44f;
        const float width = InfoWidth - pad * 2f;

        ui.titleText = NewText("Title", rect, 58f, Color.white, TextAlignmentOptions.TopLeft);
        Place(ui.titleText.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(pad, -46f), new Vector2(width, 150f));
        ui.titleText.fontStyle = FontStyles.Bold;
        ui.titleText.lineSpacing = -12f;

        UnityEngine.UI.Image rule = NewImage("Rule", rect, Faint, false);
        Place((RectTransform)rule.transform, TopLeft, TopLeft, TopLeft, new Vector2(pad, -206f), new Vector2(width, 2f));

        ui.headlineText = NewText("Headline", rect, 32f, Color.white, TextAlignmentOptions.TopLeft);
        Place(ui.headlineText.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(pad, -224f), new Vector2(width, 44f));
        ui.headlineText.fontStyle = FontStyles.Bold;

        UnityEngine.UI.Image box = NewImage("GuaranteeBox", rect, new Color(Accent.r, Accent.g, Accent.b, 0.85f), false);
        Place((RectTransform)box.transform, TopLeft, TopLeft, TopLeft, new Vector2(pad, -278f), new Vector2(width, 86f));
        UnityEngine.UI.Image boxMark = NewImage("Mark", box.transform, Color.white, false);
        Place((RectTransform)boxMark.transform, MiddleLeft, MiddleLeft, new Vector2(0.5f, 0.5f), new Vector2(20f, 0f), new Vector2(10f, 10f));
        boxMark.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        ui.guaranteeText = NewText("Text", box.transform, 24f, Color.white, TextAlignmentOptions.Left);
        Place(ui.guaranteeText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(18f, 0f), new Vector2(-60f, -12f));

        ui.descriptionText = NewText("Description", rect, 22f, Dim, TextAlignmentOptions.TopLeft);
        Place(ui.descriptionText.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(pad, -384f), new Vector2(width, 120f));

        ui.timeCaption = NewText("TimeCaption", rect, 20f, Dim, TextAlignmentOptions.BottomLeft);
        Place(ui.timeCaption.rectTransform, BottomLeft, BottomLeft, BottomLeft, new Vector2(pad, 84f), new Vector2(width, 30f));
        ui.timeCaption.characterSpacing = 3f;

        ui.timeValue = NewText("TimeValue", rect, 32f, Color.white, TextAlignmentOptions.BottomLeft);
        Place(ui.timeValue.rectTransform, BottomLeft, BottomLeft, BottomLeft, new Vector2(pad, 40f), new Vector2(width, 44f));
    }

    // ---------------------------------------------------------------- bottom left

    private static void BuildBottomLeft(RectTransform root, GachaUI ui)
    {
        RectTransform row = NewRect("BottomLeft", root);
        Place(row, BottomLeft, BottomLeft, BottomLeft, new Vector2(48f, 40f), new Vector2(660f, 68f));
        HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 20f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        ui.shopButton = TextButton("ShopButton", row, "SHOP");
        ui.detailsButton = TextButton("DetailsButton", row, "DETAILS");
        ui.historyButton = TextButton("HistoryButton", row, "HISTORY");
    }

    private static Button TextButton(string name, RectTransform parent, string label)
    {
        UnityEngine.UI.Image face = NewImage(name, parent, Slab, true);
        RectTransform rect = (RectTransform)face.transform;
        rect.sizeDelta = new Vector2(200f, 68f);
        AddEdges(rect, Edge, 2f);

        TextMeshProUGUI text = NewText("Text", rect, 26f, Color.white, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        text.fontStyle = FontStyles.Bold;
        text.characterSpacing = 3f;
        text.text = label;

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = face;
        return button;
    }

    // --------------------------------------------------------------- pull buttons

    private static void BuildPullButtons(RectTransform root, GachaUI ui)
    {
        Vector2 size = new Vector2(380f, 104f);

        ui.multiButton = PullButton("MultiButton", root, new Vector2(-48f, 36f), size, "HEADHUNT ×10", out ui.multiCost);
        ui.singleButton = PullButton("SingleButton", root, new Vector2(-48f - size.x - 28f, 36f), size, "HEADHUNT ×1", out ui.singleCost);
    }

    /// <summary>
    /// 抽卡按钮 / A pull button. It carries a CanvasGroup because GachaUI dims an ended banner's
    /// buttons through it - the Button's colour tint owns the graphic's own alpha.
    /// </summary>
    private static Button PullButton(string name, RectTransform root, Vector2 position, Vector2 size,
                                     string label, out TextMeshProUGUI cost)
    {
        UnityEngine.UI.Image face = NewImage(name, root, Accent, true);
        RectTransform rect = (RectTransform)face.transform;
        Place(rect, BottomRight, BottomRight, BottomRight, position, size);
        rect.gameObject.AddComponent<CanvasGroup>();
        AddEdges(rect, Edge, 2f);

        TextMeshProUGUI text = NewText("Label", rect, 30f, Color.white, TextAlignmentOptions.Center);
        Place(text.rectTransform, new Vector2(0f, 0.5f), Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, -4f), Vector2.zero);
        text.fontStyle = FontStyles.Bold;
        text.characterSpacing = 2f;
        text.text = label;

        RectTransform costRow = NewRect("Cost", rect);
        Place(costRow, BottomCenter, BottomCenter, BottomCenter, new Vector2(0f, 12f), new Vector2(200f, 38f));
        Diamond("Gem", costRow, new Vector2(46f, 0f), Orundum, 22f);
        cost = NewText("Amount", costRow, 26f, Color.white, TextAlignmentOptions.Left);
        Place(cost.rectTransform, MiddleLeft, MiddleLeft, MiddleLeft, new Vector2(70f, 0f), new Vector2(130f, 38f));
        cost.text = "0";

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = face;
        return button;
    }

    // -------------------------------------------------------------------- details

    private static void BuildDetails(RectTransform root, GachaUI ui)
    {
        // 全屏遮罩吃掉背后的点击 / The full-screen dim is a raycast target on purpose: while the
        // details are open, nothing behind them should be pressable.
        UnityEngine.UI.Image dim = NewImage("DetailsOverlay", root, new Color(0f, 0f, 0f, 0.72f), true);
        RectTransform overlay = (RectTransform)dim.transform;
        Stretch(overlay);
        ui.detailsOverlay = overlay.gameObject;

        UnityEngine.UI.Image panel = NewImage("Panel", overlay, PanelInk, true);
        RectTransform panelRect = (RectTransform)panel.transform;
        Place(panelRect, Center, Center, Center, Vector2.zero, new Vector2(980f, 720f));
        AddEdges(panelRect, Edge, 2f);

        UnityEngine.UI.Image mark = NewImage("Mark", panelRect, Accent, false);
        Place((RectTransform)mark.transform, TopLeft, TopLeft, TopLeft, new Vector2(44f, -40f), new Vector2(8f, 36f));

        TextMeshProUGUI header = NewText("Header", panelRect, 32f, Color.white, TextAlignmentOptions.Left);
        Place(header.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(66f, -36f), new Vector2(600f, 44f));
        header.fontStyle = FontStyles.Bold;
        header.characterSpacing = 3f;
        header.text = "DETAILS";

        ui.detailsBody = NewText("Body", panelRect, 24f, Color.white, TextAlignmentOptions.TopLeft);
        RectTransform body = ui.detailsBody.rectTransform;
        body.anchorMin = Vector2.zero;
        body.anchorMax = Vector2.one;
        body.offsetMin = new Vector2(44f, 40f);
        body.offsetMax = new Vector2(-44f, -100f);
        ui.detailsBody.richText = true;

        ui.detailsClose = SquareButton("Close", panelRect, TopRight, new Vector2(-24f, -24f), new Vector2(64f, 64f), "×", 46f);

        overlay.gameObject.SetActive(false);
    }

    // --------------------------------------------------------------------- wiring

    /// <summary>
    /// 主页的寻访格子 / Hooks HomeUI's HEADHUNTING tile. It already carries an Image and a
    /// Button, so only the OpenUIButton is added - the same way MissionUISetup hooks MISSIONS.
    /// </summary>
    private static bool HookHeadhuntTile()
    {
        string homePath = UiPrefabDir + "/HomeUI.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(homePath) == null)
        {
            Debug.LogWarning($"[GachaUISetup] No HomeUI prefab at {homePath}, so nothing opens headhunting yet.");
            return false;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(homePath);

        try
        {
            Transform tile = FindDeep(contents.transform, HeadhuntTileName);
            if (tile == null)
            {
                Debug.LogWarning($"[GachaUISetup] HomeUI has no '{HeadhuntTileName}' child.");
                return false;
            }

            OpenUIButton existing = tile.GetComponent<OpenUIButton>();
            if (existing != null && existing.uiName == GachaUI.UIName) return true;

            if (tile.GetComponent<Graphic>() == null)
            {
                NewImage("Hit", tile, new Color(0f, 0f, 0f, 0f), true);
            }

            // 显式判空, 不用 ?? / Explicit null checks rather than ??: in the Editor a missing
            // component can come back as Unity's fake-null object, which compares equal to null
            // but is not a null reference, so ?? would keep it instead of adding the component.
            Button button = tile.GetComponent<Button>();
            if (button == null) button = tile.gameObject.AddComponent<Button>();
            if (button.targetGraphic == null) button.targetGraphic = tile.GetComponent<Graphic>();

            OpenUIButton open = existing;
            if (open == null) open = tile.gameObject.AddComponent<OpenUIButton>();
            open.uiName = GachaUI.UIName;

            PrefabUtility.SaveAsPrefabAsset(contents, homePath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // -------------------------------------------------------------------- helpers

    private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
    private static readonly Vector2 TopLeft = new Vector2(0f, 1f);
    private static readonly Vector2 TopCenter = new Vector2(0.5f, 1f);
    private static readonly Vector2 TopRight = new Vector2(1f, 1f);
    private static readonly Vector2 MiddleLeft = new Vector2(0f, 0.5f);
    private static readonly Vector2 MiddleRight = new Vector2(1f, 0.5f);
    private static readonly Vector2 BottomLeft = new Vector2(0f, 0f);
    private static readonly Vector2 BottomCenter = new Vector2(0.5f, 0f);
    private static readonly Vector2 BottomRight = new Vector2(1f, 0f);

    private static bool GuardEditMode()
    {
        if (!Application.isPlaying) return true;
        Debug.LogError("[GachaUISetup] Exit Play Mode first.");
        return false;
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

    // 菱形宝石 / A gem drawn as a square turned 45 degrees - no sprite, and no glyph the default
    // font might not carry.
    private static void Diamond(string name, RectTransform parent, Vector2 position, Color color, float size)
    {
        UnityEngine.UI.Image gem = NewImage(name, parent, color, false);
        Place((RectTransform)gem.transform, MiddleLeft, MiddleLeft, Center, position, new Vector2(size, size));
        gem.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
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

    private static void Inset(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
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
