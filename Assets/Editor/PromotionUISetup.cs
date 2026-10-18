using TMPro;
using UI;
using UI.Sub;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 精英化 / Builds the promotion screen after the reference: the operator standing over a dark band
/// that reads "1 Elite" to "2 Elite" - the emblems drawn, the next one large in the middle - with what
/// the phase unlocks beside it; along the bottom the level the promotion needs and the LMD, Orirock
/// and Sugar Substitute it costs; and "Promote ...?" with cancel and confirm.
///
/// 找到就不动 / Find-or-create: a prefab that exists is kept. Reset Promotion UI To Default deletes it
/// and builds it again. CharInfoUI opens it from its PROMOTION panel in code.
///
/// The emblems, the cross and the tick are drawn from rectangles: the elite sprites in the project are
/// placeholder bars, and the default TMP font has no tick or cross glyph to trust.
/// </summary>
public static class PromotionUISetup
{
    private const string UiPrefabDir = "Assets/Arknights/Resources/Prefab/UI";

    private static readonly Color Ink = new Color(0.07f, 0.075f, 0.085f, 1f);
    private static readonly Color Band = new Color(0.04f, 0.045f, 0.05f, 0.86f);
    private static readonly Color Well = new Color(0f, 0f, 0f, 0.6f);
    private static readonly Color Light = new Color(0.93f, 0.93f, 0.94f, 1f);
    private static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);
    private static readonly Color Yellow = new Color(0.96f, 0.73f, 0.05f, 1f);
    private static readonly Color ConfirmFace = new Color(0.16f, 0.45f, 0.60f, 1f);
    private static readonly Color CancelFace = new Color(0.22f, 0.23f, 0.25f, 1f);

    // ------------------------------------------------------------------ menu items

    [MenuItem("Arknights/Character/Build Promotion UI", false, 3)]
    public static void Build()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        string path = $"{UiPrefabDir}/{PromotionUI.UIName}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
        {
            Debug.Log($"[PromotionUISetup] {PromotionUI.UIName}: kept. Reset Promotion UI To Default rebuilds it.");
            return;
        }

        GameObject root = BuildScreen();
        OperatorSplashFrames.FitInto(root, PromotionUI.UIName);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();

        Debug.Log($"[PromotionUISetup] {PromotionUI.UIName}: built at {path}.\n" +
                  "  Opened from CharInfoUI's PROMOTION panel. Device builds read the UI bundle: rebuild the " +
                  "AssetBundles before testing on a phone.");
    }

    [MenuItem("Arknights/Character/Reset Promotion UI To Default", false, 22)]
    public static void ResetToDefault()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        AssetDatabase.DeleteAsset($"{UiPrefabDir}/{PromotionUI.UIName}.prefab");
        Build();
    }

    // ================================================================== the screen

    private static GameObject BuildScreen()
    {
        GameObject root = new GameObject(PromotionUI.UIName, typeof(RectTransform), typeof(CanvasGroup), typeof(PromotionUI));
        PromotionUI ui = root.GetComponent<PromotionUI>();
        RectTransform rect = (RectTransform)root.transform;
        Stretch(rect);

        Stretch(NewImage("Backdrop", rect, Ink, true).rectTransform);

        // ---- the operator; OperatorSplashFrames frames it and shades the bottom
        RectTransform clip = NewRect("ArtClip", rect);
        Stretch(clip);
        UnityEngine.UI.Image art = NewImage("Art", clip, Color.white, false);
        Place(art.rectTransform, Center, Center, Center, new Vector2(-260f, 40f), new Vector2(820f, 1080f));
        ui.art = art;

        BuildBand(rect, ui);
        BuildNeeds(rect, ui);
        BuildConfirm(rect, ui);

        UnityEngine.UI.Image back = NewImage("BackButton", rect, new Color(0.08f, 0.085f, 0.095f, 0.92f), true);
        Place(back.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(40f, -30f), new Vector2(150f, 64f));
        Chevron(back.rectTransform, "Glyph", Vector2.zero, 30f, Color.white);
        ui.backButton = back.gameObject.AddComponent<UnityEngine.UI.Button>();
        ui.backButton.targetGraphic = back;

        ui.flash = Text(rect, "Flash", Center, new Vector2(0f, 300f), new Vector2(1200f, 90f), 60f, Yellow, TextAlignmentOptions.Center);
        ui.flash.fontStyle = FontStyles.Bold;
        return root;
    }

    /// <summary>
    /// The dark band across the screen: this phase and its emblem, the next phase's emblem large in the
    /// middle, the next phase's number and what it unlocks. The next side hides at the last phase.
    /// </summary>
    private static void BuildBand(RectTransform rect, PromotionUI ui)
    {
        UnityEngine.UI.Image band = NewImage("Band", rect, Band, false);
        Place(band.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), Center, new Vector2(0f, -80f), new Vector2(0f, 250f));
        RectTransform b = band.rectTransform;

        UnityEngine.UI.Image edge = NewImage("Edge", b, new Color(1f, 1f, 1f, 0.12f), false);
        Place(edge.rectTransform, TopLeft, new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 2f));

        RectTransform from = NewRect("FromSide", b);
        Stretch(from);
        ui.fromSide = from.gameObject;
        ui.fromNumber = Text(from, "FromNumber", Center, new Vector2(-780f, 28f), new Vector2(180f, 150f), 130f, Color.white, TextAlignmentOptions.Center);
        Text(from, "FromCaption", Center, new Vector2(-780f, -80f), new Vector2(180f, 50f), 40f, Color.white, TextAlignmentOptions.Center).text = "Elite";
        ui.fromEmblem = Emblem(from, "FromEmblem", new Vector2(-570f, 0f), 190f, Color.white);

        ui.bigEmblem = Emblem(b, "BigEmblem", new Vector2(-200f, 0f), 320f, new Color(1f, 1f, 1f, 0.92f));

        RectTransform to = NewRect("ToSide", b);
        Stretch(to);
        ui.toSide = to.gameObject;
        ui.toNumber = Text(to, "ToNumber", Center, new Vector2(40f, 28f), new Vector2(180f, 150f), 130f, Color.white, TextAlignmentOptions.Center);
        Text(to, "ToCaption", Center, new Vector2(40f, -80f), new Vector2(180f, 50f), 40f, Color.white, TextAlignmentOptions.Center).text = "Elite";

        ui.details = Text(b, "Details", Center, new Vector2(530f, 0f), new Vector2(720f, 220f), 30f, Color.white, TextAlignmentOptions.MidlineLeft);
        ui.details.textWrappingMode = TextWrappingModes.Normal;
        ui.details.lineSpacing = 12f;
    }

    /// <summary>Along the bottom: the level the promotion needs, then the LMD and the two materials.</summary>
    private static void BuildNeeds(RectTransform rect, PromotionUI ui)
    {
        RectTransform level = NewRect("Level", rect);
        Place(level, Center, Center, Center, new Vector2(-820f, -390f), new Vector2(170f, 170f));
        ui.levelCell = level.gameObject;
        UnityEngine.UI.Image outer = NewImage("Outer", level, new Color(1f, 1f, 1f, 0.25f), false);
        outer.sprite = Knob();
        Stretch(outer.rectTransform);
        UnityEngine.UI.Image ring = NewImage("Ring", level, Yellow, false);
        ring.sprite = Knob();
        ring.type = UnityEngine.UI.Image.Type.Filled;
        ring.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
        ring.fillOrigin = (int)UnityEngine.UI.Image.Origin360.Top;
        Stretch(ring.rectTransform);
        ui.levelRing = ring;
        UnityEngine.UI.Image face = NewImage("Face", level, new Color(0.10f, 0.105f, 0.12f, 1f), false);
        face.sprite = Knob();
        Place(face.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(148f, 148f));
        ui.levelValue = Text(level, "Value", Center, new Vector2(0f, 10f), new Vector2(150f, 80f), 68f, Color.white, TextAlignmentOptions.Center);
        ui.levelValue.fontStyle = FontStyles.Bold;
        Text(level, "Caption", Center, new Vector2(0f, -42f), new Vector2(150f, 36f), 26f, Dim, TextAlignmentOptions.Center).text = "Level";
        ui.levelText = Text(level, "Progress", Center, new Vector2(0f, -122f), new Vector2(220f, 44f), 36f, Color.white, TextAlignmentOptions.Center);

        RectTransform row = NewRect("Costs", rect);
        Stretch(row);
        ui.costRow = row.gameObject;
        ui.costIcons = new UnityEngine.UI.Image[3];
        ui.costTexts = new TextMeshProUGUI[3];

        for (int i = 0; i < 3; i++)
        {
            RectTransform cell = NewRect("Cost" + i, row);
            Place(cell, Center, Center, Center, new Vector2(-620f + i * 200f, -390f), new Vector2(170f, 170f));
            UnityEngine.UI.Image disc = NewImage("Disc", cell, Well, false);
            disc.sprite = Knob();
            Place(disc.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(160f, 160f));
            UnityEngine.UI.Image icon = NewImage("Icon", cell, Color.white, false);
            icon.preserveAspect = true;
            Place(icon.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(120f, 120f));
            ui.costIcons[i] = icon;
            ui.costTexts[i] = Text(cell, "Amount", Center, new Vector2(0f, -122f), new Vector2(220f, 44f), 36f, Color.white, TextAlignmentOptions.Center);
        }
    }

    /// <summary>"Promote ...?" and the two buttons, bottom right.</summary>
    private static void BuildConfirm(RectTransform rect, PromotionUI ui)
    {
        ui.prompt = Text(rect, "Prompt", Center, new Vector2(560f, -300f), new Vector2(760f, 64f), 44f, new Color(0.85f, 0.85f, 0.87f, 1f),
                         TextAlignmentOptions.Center);

        UnityEngine.UI.Image cancel = NewImage("CancelButton", rect, CancelFace, true);
        Place(cancel.rectTransform, Center, Center, Center, new Vector2(430f, -410f), new Vector2(250f, 96f));
        UnityEngine.UI.Image cancelDisc = NewImage("Disc", cancel.rectTransform, Color.white, false);
        cancelDisc.sprite = Knob();
        Place(cancelDisc.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(58f, 58f));
        Bar(cancelDisc.rectTransform, "A", Vector2.zero, new Vector2(30f, 7f), 45f, CancelFace);
        Bar(cancelDisc.rectTransform, "B", Vector2.zero, new Vector2(30f, 7f), -45f, CancelFace);
        ui.cancelButton = cancel.gameObject.AddComponent<UnityEngine.UI.Button>();
        ui.cancelButton.targetGraphic = cancel;

        UnityEngine.UI.Image confirm = NewImage("ConfirmButton", rect, ConfirmFace, true);
        Place(confirm.rectTransform, Center, Center, Center, new Vector2(690f, -410f), new Vector2(250f, 96f));
        UnityEngine.UI.Image confirmDisc = NewImage("Disc", confirm.rectTransform, Color.white, false);
        confirmDisc.sprite = Knob();
        Place(confirmDisc.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(58f, 58f));
        Bar(confirmDisc.rectTransform, "Short", new Vector2(-7f, -3f), new Vector2(16f, 7f), -45f, ConfirmFace);
        Bar(confirmDisc.rectTransform, "Long", new Vector2(5f, 2f), new Vector2(28f, 7f), 45f, ConfirmFace);
        ui.confirmButton = confirm.gameObject.AddComponent<UnityEngine.UI.Button>();
        ui.confirmButton.targetGraphic = confirm;
        ui.confirmGroup = confirm.gameObject.AddComponent<CanvasGroup>();
    }

    // ================================================================== glyphs

    /// <summary>
    /// 三种标志 / The three phase emblems under one EliteEmblem: Elite 0 a short bar, Elite 1 crossed
    /// blades, Elite 2 two pairs of them side by side.
    /// </summary>
    private static EliteEmblem Emblem(RectTransform parent, string name, Vector2 position, float size, Color color)
    {
        RectTransform root = NewRect(name, parent);
        Place(root, Center, Center, Center, position, new Vector2(size, size));
        EliteEmblem emblem = root.gameObject.AddComponent<EliteEmblem>();

        RectTransform zero = NewRect("Elite0", root);
        Stretch(zero);
        Bar(zero, "Bar", Vector2.zero, new Vector2(size * 0.5f, size * 0.07f), 0f, color);

        RectTransform one = NewRect("Elite1", root);
        Stretch(one);
        Blades(one, Vector2.zero, size, color);

        RectTransform two = NewRect("Elite2", root);
        Stretch(two);
        Blades(two, new Vector2(-size * 0.2f, 0f), size * 0.75f, color);
        Blades(two, new Vector2(size * 0.2f, 0f), size * 0.75f, color);

        emblem.phases = new[] { zero.gameObject, one.gameObject, two.gameObject };
        return emblem;
    }

    private static void Blades(RectTransform parent, Vector2 centre, float size, Color color)
    {
        Bar(parent, "BladeA", centre, new Vector2(size * 0.95f, size * 0.09f), 38f, color);
        Bar(parent, "BladeB", centre, new Vector2(size * 0.95f, size * 0.09f), -38f, color);
    }

    private static void Chevron(RectTransform parent, string name, Vector2 position, float size, Color color)
    {
        RectTransform spot = NewRect(name, parent);
        Place(spot, Center, Center, Center, position, new Vector2(size, size));
        float arm = size * 0.62f, thick = Mathf.Max(3f, size * 0.13f);
        Bar(spot, "Upper", new Vector2(0f, size * 0.2f), new Vector2(arm, thick), 45f, color);
        Bar(spot, "Lower", new Vector2(0f, -size * 0.2f), new Vector2(arm, thick), -45f, color);
    }

    // ================================================================== helpers

    private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
    private static readonly Vector2 TopLeft = new Vector2(0f, 1f);

    private static Sprite Knob() => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

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
