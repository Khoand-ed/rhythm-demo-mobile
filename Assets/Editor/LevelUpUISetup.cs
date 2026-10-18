using Promuse.Contracts.Characters;
using TMPro;
using UI;
using UI.Sub;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 干员升级 / Builds the level-up screen after the reference: the operator standing on the left over
/// a shade, with the UPDATE panel (max HP, score, fever before and after), the target level and its
/// EXP, and the four battle records with the LMD they cost along the bottom; on the right a light
/// panel carrying the dark level dial - the levels above fading in, the target in a ring that fills
/// with its EXP - and Level Up.
///
/// 找到就不动 / Find-or-create: a prefab that exists is kept. Reset Level Up UI To Default deletes it
/// and builds it again. CharInfoUI opens it from its level panel in code; no prefab of its is touched.
///
/// The glyphs are drawn from rectangles: the default TMP font has no chevrons or arrows worth trusting.
/// </summary>
public static class LevelUpUISetup
{
    private const string UiPrefabDir = "Assets/Arknights/Resources/Prefab/UI";

    private static readonly Color Ink = new Color(0.07f, 0.075f, 0.085f, 1f);
    private static readonly Color Panel = new Color(0.74f, 0.75f, 0.77f, 1f);
    private static readonly Color Strip = new Color(0.20f, 0.205f, 0.22f, 1f);
    private static readonly Color Glass = new Color(0f, 0f, 0f, 0.55f);
    private static readonly Color Well = new Color(0f, 0f, 0f, 0.6f);
    private static readonly Color Light = new Color(0.93f, 0.93f, 0.94f, 1f);
    private static readonly Color PaperInk = new Color(0.14f, 0.14f, 0.16f, 1f);
    private static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);
    private static readonly Color Yellow = new Color(0.96f, 0.73f, 0.05f, 1f);
    private static readonly Color ButtonFace = new Color(0.16f, 0.45f, 0.60f, 1f);

    // ------------------------------------------------------------------ menu items

    [MenuItem("Arknights/Character/Build Level Up UI", false, 2)]
    public static void Build()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        string path = $"{UiPrefabDir}/{LevelUpUI.UIName}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
        {
            Debug.Log($"[LevelUpUISetup] {LevelUpUI.UIName}: kept. Reset Level Up UI To Default rebuilds it.");
            return;
        }

        GameObject root = BuildScreen();
        OperatorSplashFrames.FitInto(root, LevelUpUI.UIName);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();

        Debug.Log($"[LevelUpUISetup] {LevelUpUI.UIName}: built at {path}.\n" +
                  "  Opened from CharInfoUI's level panel. Device builds read the UI bundle: rebuild the " +
                  "AssetBundles before testing on a phone.");
    }

    [MenuItem("Arknights/Character/Reset Level Up UI To Default", false, 21)]
    public static void ResetToDefault()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        AssetDatabase.DeleteAsset($"{UiPrefabDir}/{LevelUpUI.UIName}.prefab");
        Build();
    }

    // ================================================================== the screen

    private static GameObject BuildScreen()
    {
        GameObject root = new GameObject(LevelUpUI.UIName, typeof(RectTransform), typeof(CanvasGroup), typeof(LevelUpUI));
        LevelUpUI ui = root.GetComponent<LevelUpUI>();
        RectTransform rect = (RectTransform)root.transform;
        Stretch(rect);

        Stretch(NewImage("Backdrop", rect, Ink, true).rectTransform);

        // ---- the operator, clipped to the left; OperatorSplashFrames frames it and shades it
        RectTransform clip = NewRect("ArtClip", rect);
        Place(clip, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(1180f, 0f));
        UnityEngine.UI.Image art = NewImage("Art", clip, Color.white, false);
        Place(art.rectTransform, Center, Center, Center, new Vector2(40f, -20f), new Vector2(900f, 1080f));
        ui.art = art;

        BuildDial(rect, ui);
        BuildLeft(rect, ui);

        // ---- top: back and the operator's name
        UnityEngine.UI.Image back = NewImage("BackButton", rect, new Color(0.08f, 0.085f, 0.095f, 0.92f), true);
        Place(back.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(40f, -30f), new Vector2(150f, 64f));
        Chevron(back.rectTransform, "Glyph", Vector2.zero, 30f, Color.white, 0f);
        ui.backButton = back.gameObject.AddComponent<UnityEngine.UI.Button>();
        ui.backButton.targetGraphic = back;

        ui.operatorName = Text(rect, "OperatorName", TopLeft, new Vector2(214f, -30f), new Vector2(600f, 64f), 44f, Color.white,
                               TextAlignmentOptions.MidlineLeft);
        ui.operatorName.fontStyle = FontStyles.Bold;

        ui.flash = Text(rect, "Flash", Center, new Vector2(-380f, 260f), new Vector2(900f, 90f), 60f, Yellow,
                        TextAlignmentOptions.Center);
        ui.flash.fontStyle = FontStyles.Bold;
        return root;
    }

    /// <summary>
    /// 右边 / The light panel on the right, the dark dial strip down it, the target ring and the
    /// levels either side of it, and Level Up in the corner. The strip is the drag area.
    /// </summary>
    private static void BuildDial(RectTransform rect, LevelUpUI ui)
    {
        UnityEngine.UI.Image panel = NewImage("Panel", rect, Panel, true);
        Place(panel.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(780f, 0f));
        RectTransform p = panel.rectTransform;

        UnityEngine.UI.Image strip = NewImage("Wheel", p, Strip, true);
        Place(strip.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(90f, 0f), new Vector2(300f, 0f));
        RectTransform s = strip.rectTransform;
        ui.wheel = strip.gameObject.AddComponent<LevelWheel>();

        TextMeshProUGUI above2 = Text(s, "Above2", Center, new Vector2(0f, 410f), new Vector2(280f, 90f), 64f, new Color(1f, 1f, 1f, 0.22f), TextAlignmentOptions.Center);
        TextMeshProUGUI above1 = Text(s, "Above1", Center, new Vector2(0f, 280f), new Vector2(280f, 110f), 84f, new Color(1f, 1f, 1f, 0.45f), TextAlignmentOptions.Center);
        above1.fontStyle = above2.fontStyle = FontStyles.Bold;
        ui.wheelAbove = new[] { above1, above2 };

        // 圈 / The ring: a light disc, the EXP filling over it in yellow, the dark face on top.
        RectTransform circle = NewRect("Circle", s);
        Place(circle, Center, Center, Center, new Vector2(0f, 40f), new Vector2(260f, 260f));
        UnityEngine.UI.Image outer = NewImage("Outer", circle, Light, false);
        outer.sprite = Knob();
        Stretch(outer.rectTransform);
        UnityEngine.UI.Image ring = NewImage("Ring", circle, Yellow, false);
        ring.sprite = Knob();
        ring.type = UnityEngine.UI.Image.Type.Filled;
        ring.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
        ring.fillOrigin = (int)UnityEngine.UI.Image.Origin360.Top;
        ring.fillClockwise = true;
        Stretch(ring.rectTransform);
        ui.ring = ring;
        UnityEngine.UI.Image face = NewImage("Face", circle, new Color(0.13f, 0.135f, 0.15f, 1f), false);
        face.sprite = Knob();
        Place(face.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(232f, 232f));
        Text(circle, "Caption", Center, new Vector2(0f, 66f), new Vector2(120f, 36f), 30f, Dim, TextAlignmentOptions.Center).text = "LV";
        ui.wheelLevel = Text(circle, "Level", Center, new Vector2(0f, -14f), new Vector2(220f, 130f), 110f, Color.white, TextAlignmentOptions.Center);
        ui.wheelLevel.fontStyle = FontStyles.Bold;

        ui.wheelBelow = Text(s, "Below", Center, new Vector2(0f, -190f), new Vector2(280f, 80f), 56f, new Color(1f, 1f, 1f, 0.28f), TextAlignmentOptions.Center);

        Chevron(s, "Down1", new Vector2(0f, -300f), 46f, Light, 90f);
        Chevron(s, "Down2", new Vector2(0f, -330f), 46f, Light, 90f);
        ui.hint = Text(s, "Hint", Center, new Vector2(0f, -410f), new Vector2(280f, 80f), 24f, new Color(1f, 1f, 1f, 0.75f), TextAlignmentOptions.Center);

        // 指向圈的标签 / The tag pointing at the ring, on the light panel beside the strip.
        RectTransform tag = NewRect("TargetTag", p);
        Place(tag, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(400f, 40f), new Vector2(150f, 40f));
        Chevron(tag, "Point", new Vector2(-60f, 0f), 26f, PaperInk, 0f);
        Text(tag, "Label", Center, new Vector2(14f, 0f), new Vector2(120f, 40f), 22f, PaperInk, TextAlignmentOptions.MidlineLeft).text = "TARGET";

        // ---- Level Up
        UnityEngine.UI.Image go = NewImage("LevelUpButton", p, ButtonFace, true);
        Place(go.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 48f), new Vector2(320f, 96f));
        ui.levelUpButton = go.gameObject.AddComponent<UnityEngine.UI.Button>();
        ui.levelUpButton.targetGraphic = go;
        ui.levelUpGroup = go.gameObject.AddComponent<CanvasGroup>();
        Chevron(go.rectTransform, "Up1", new Vector2(-104f, -6f), 30f, Color.white, -90f);
        Chevron(go.rectTransform, "Up2", new Vector2(-104f, 10f), 30f, Color.white, -90f);
        Text(go.rectTransform, "Label", Center, new Vector2(26f, 0f), new Vector2(220f, 60f), 34f, Color.white, TextAlignmentOptions.Center).text = "Level Up";
    }

    /// <summary>
    /// 左下 / Over the operator's shade: what the level changes, the target and its EXP, and the
    /// records and LMD along the bottom.
    /// </summary>
    private static void BuildLeft(RectTransform rect, LevelUpUI ui)
    {
        // ---- UPDATE
        UnityEngine.UI.Image update = NewImage("UpdatePanel", rect, Glass, false);
        Place(update.rectTransform, BottomLeft, BottomLeft, BottomLeft, new Vector2(56f, 300f), new Vector2(560f, 230f));
        RectTransform u = update.rectTransform;
        UnityEngine.UI.Image title = NewImage("Title", u, Light, false);
        Place(title.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(0f, 0f), new Vector2(150f, 34f));
        Text(title.rectTransform, "Label", Center, Vector2.zero, new Vector2(150f, 34f), 20f, PaperInk, TextAlignmentOptions.Center).text = "UPDATE";

        string[] captions = { "MAX HP", "SCORE", "FEVER" };
        TextMeshProUGUI[] from = new TextMeshProUGUI[3];
        TextMeshProUGUI[] to = new TextMeshProUGUI[3];
        for (int i = 0; i < 3; i++)
        {
            float y = -78f - i * 50f;
            Text(u, captions[i] + "Caption", TopLeft, new Vector2(28f, y), new Vector2(170f, 40f), 26f, Dim, TextAlignmentOptions.MidlineLeft).text = captions[i];
            from[i] = Text(u, captions[i] + "From", TopLeft, new Vector2(196f, y), new Vector2(140f, 40f), 28f, Color.white, TextAlignmentOptions.MidlineRight);
            RectTransform arrow = Chevron(u, captions[i] + "Arrow", Vector2.zero, 20f, Dim, 180f);
            Place(arrow, TopLeft, TopLeft, Center, new Vector2(370f, y - 20f), new Vector2(20f, 20f));
            to[i] = Text(u, captions[i] + "To", TopLeft, new Vector2(396f, y), new Vector2(140f, 40f), 28f, Color.white, TextAlignmentOptions.MidlineLeft);
            from[i].fontStyle = to[i].fontStyle = FontStyles.Bold;
        }
        ui.hpFrom = from[0]; ui.hpTo = to[0];
        ui.scoreFrom = from[1]; ui.scoreTo = to[1];
        ui.feverFrom = from[2]; ui.feverTo = to[2];

        // ---- the target
        UnityEngine.UI.Image target = NewImage("TargetPanel", rect, Glass, false);
        Place(target.rectTransform, BottomLeft, BottomLeft, BottomLeft, new Vector2(636f, 380f), new Vector2(480f, 150f));
        RectTransform t = target.rectTransform;
        Text(t, "Caption", TopLeft, new Vector2(24f, -18f), new Vector2(240f, 40f), 26f, Dim, TextAlignmentOptions.MidlineLeft).text = "Target Level";
        ui.targetLevel = Text(t, "Level", TopLeft, new Vector2(240f, -14f), new Vector2(216f, 48f), 38f, Color.white, TextAlignmentOptions.MidlineRight);
        ui.targetLevel.fontStyle = FontStyles.Bold;
        ui.expText = Text(t, "Exp", TopLeft, new Vector2(24f, -72f), new Vector2(432f, 40f), 26f, Color.white, TextAlignmentOptions.MidlineLeft);
        UnityEngine.UI.Image barGround = NewImage("ExpBar", t, new Color(1f, 1f, 1f, 0.18f), false);
        Place(barGround.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(24f, -124f), new Vector2(432f, 10f));
        UnityEngine.UI.Image bar = NewImage("Fill", barGround.rectTransform, Yellow, false);
        bar.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        bar.type = UnityEngine.UI.Image.Type.Filled;
        bar.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
        Stretch(bar.rectTransform);
        ui.expBar = bar;

        // ---- the records, then the LMD
        int count = OperatorLevels.Records.Count;
        ui.recordIcons = new UnityEngine.UI.Image[count];
        ui.recordOwned = new TextMeshProUGUI[count];
        ui.recordChosen = new TextMeshProUGUI[count];
        ui.recordAdd = new UnityEngine.UI.Button[count];
        ui.recordRemove = new UnityEngine.UI.Button[count];

        for (int i = 0; i < count; i++)
        {
            RectTransform slot = NewRect("Record" + i, rect);
            Place(slot, BottomLeft, BottomLeft, Center, new Vector2(140f + i * 190f, 170f), new Vector2(150f, 150f));

            UnityEngine.UI.Image disc = NewImage("Disc", slot, Well, true);
            disc.sprite = Knob();
            Place(disc.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(140f, 140f));
            UnityEngine.UI.Button add = disc.gameObject.AddComponent<UnityEngine.UI.Button>();
            add.targetGraphic = disc;
            ui.recordAdd[i] = add;

            UnityEngine.UI.Image icon = NewImage("Icon", disc.rectTransform, Color.white, false);
            icon.preserveAspect = true;
            Place(icon.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(108f, 108f));
            ui.recordIcons[i] = icon;

            ui.recordOwned[i] = Text(slot, "Owned", Center, new Vector2(0f, -100f), new Vector2(150f, 36f), 26f, Color.white, TextAlignmentOptions.Center);

            UnityEngine.UI.Image badge = NewImage("Badge", slot, Yellow, false);
            badge.sprite = Knob();
            Place(badge.rectTransform, Center, Center, Center, new Vector2(52f, 52f), new Vector2(52f, 52f));
            ui.recordChosen[i] = Text(badge.rectTransform, "Chosen", Center, Vector2.zero, new Vector2(52f, 40f), 26f, PaperInk, TextAlignmentOptions.Center);
            ui.recordChosen[i].fontStyle = FontStyles.Bold;

            UnityEngine.UI.Image minus = NewImage("Remove", slot, new Color(0.25f, 0.26f, 0.28f, 1f), true);
            minus.sprite = Knob();
            Place(minus.rectTransform, Center, Center, Center, new Vector2(-54f, 52f), new Vector2(46f, 46f));
            Bar(minus.rectTransform, "Glyph", Vector2.zero, new Vector2(22f, 5f), 0f, Color.white);
            ui.recordRemove[i] = minus.gameObject.AddComponent<UnityEngine.UI.Button>();
            ui.recordRemove[i].targetGraphic = minus;
        }

        RectTransform lmd = NewRect("Lmd", rect);
        Place(lmd, BottomLeft, BottomLeft, Center, new Vector2(140f + count * 190f + 40f, 170f), new Vector2(150f, 150f));
        UnityEngine.UI.Image card = NewImage("Card", lmd, Well, false);
        Place(card.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(140f, 140f));
        UnityEngine.UI.Image lmdIcon = NewImage("Icon", card.rectTransform, Color.white, false);
        lmdIcon.preserveAspect = true;
        Place(lmdIcon.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(100f, 100f));
        ui.lmdIcon = lmdIcon;
        ui.lmdCost = Text(lmd, "Cost", Center, new Vector2(0f, -100f), new Vector2(190f, 36f), 28f, Color.white, TextAlignmentOptions.Center);
        ui.lmdCost.fontStyle = FontStyles.Bold;
    }

    // ================================================================== glyphs

    /// <summary>A chevron pointing left at 0 degrees, turned by <paramref name="degrees"/>.</summary>
    private static RectTransform Chevron(RectTransform parent, string name, Vector2 position, float size, Color color, float degrees)
    {
        RectTransform spot = NewRect(name, parent);
        Place(spot, Center, Center, Center, position, new Vector2(size, size));
        spot.localRotation = Quaternion.Euler(0f, 0f, degrees);
        float arm = size * 0.62f, thick = Mathf.Max(3f, size * 0.13f);
        Bar(spot, "Upper", new Vector2(0f, size * 0.2f), new Vector2(arm, thick), 45f, color);
        Bar(spot, "Lower", new Vector2(0f, -size * 0.2f), new Vector2(arm, thick), -45f, color);
        return spot;
    }

    // ================================================================== helpers

    private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
    private static readonly Vector2 TopLeft = new Vector2(0f, 1f);
    private static readonly Vector2 BottomLeft = new Vector2(0f, 0f);

    private static Sprite Knob() => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

    /// <summary>A text placed by its pivot at <paramref name="corner"/>'s anchor - top-left pivot for a top-left corner.</summary>
    private static TextMeshProUGUI Text(RectTransform parent, string name, Vector2 corner, Vector2 position, Vector2 size,
                                       float fontSize, Color color, TextAlignmentOptions alignment)
    {
        TextMeshProUGUI text = NewRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.text = "";
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        Place(text.rectTransform, corner, corner, corner, position, size);
        return text;
    }

    private static void Bar(RectTransform parent, string name, Vector2 position, Vector2 size, float degrees, Color color)
    {
        UnityEngine.UI.Image bar = NewImage(name, parent, color, false);
        Place(bar.rectTransform, Center, Center, Center, position, size);
        bar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, degrees);
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
        go.layer = parent.gameObject.layer;
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

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
