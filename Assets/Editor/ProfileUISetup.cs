using System.Collections.Generic;
using TMPro;
using UI;
using UI.Sub;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 个人名片 / Builds the profile card screen after the reference's "My Info", and puts the button
/// that opens it on Home, beside the news panel:
///   - ProfileUI: the blurred Home behind, a Profile tab on the left, and the card - the assistant
///     standing on its left half with the registration day, campaign progress and operator count;
///     level, name#tag, the signature and the rhythm record on its right half,
///   - ProfileButton: inside HomeUI's LeftPanel, so it leans with the news panel's hand-set tilt.
///
/// 找到就不动 / Find-or-create: a prefab or a button that exists is kept, so a button nudged by hand
/// stays where it was put. Reset Profile UI To Default deletes both and builds them again.
///
/// The glyphs are drawn from rectangles, like Home's own: the default TMP font has no card, diamond
/// or pen glyph, and a missing glyph renders as a box.
/// </summary>
public static class ProfileUISetup
{
    private const string UiPrefabDir = "Assets/Arknights/Resources/Prefab/UI";
    private const string HomePrefabPath = UiPrefabDir + "/HomeUI.prefab";
    private const string BlurMaterialPath = "Assets/Arknights/Shaders/UIBlur.mat";

    public const string ButtonName = "ProfileButton";
    private const string LeftPanel = "LeftPanel";
    private const string NewsPanel = "NewsPanel";

    private static readonly Color Ink = new Color(0.07f, 0.075f, 0.085f, 1f);
    private static readonly Color CardFace = new Color(0.15f, 0.155f, 0.17f, 0.97f);
    private static readonly Color Well = new Color(0.11f, 0.115f, 0.13f, 1f);
    private static readonly Color Field = new Color(0.09f, 0.095f, 0.105f, 1f);
    private static readonly Color Tile = new Color(0.09f, 0.10f, 0.11f, 0.92f);
    private static readonly Color Light = new Color(0.86f, 0.86f, 0.87f, 1f);
    private static readonly Color PaperInk = new Color(0.14f, 0.14f, 0.16f, 1f);
    private static readonly Color Accent = new Color(0.10f, 0.55f, 0.80f, 1f);
    private static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);
    private static readonly Color Rule = new Color(1f, 1f, 1f, 0.22f);
    private static readonly Color Yellow = new Color(0.96f, 0.73f, 0.05f, 1f);

    // ------------------------------------------------------------------ menu items

    [MenuItem("Arknights/Home/Build Profile UI", false, 32)]
    public static void Build()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        List<string> done = new List<string> { BuildIfMissing(), HookHome() };

        AssetDatabase.SaveAssets();
        Debug.Log("[ProfileUISetup] " + string.Join("\n  ", done) +
                  "\n  Device builds read the UI bundle: rebuild the AssetBundles before testing on a phone.");
    }

    [MenuItem("Arknights/Home/Reset Profile UI To Default", false, 33)]
    public static void ResetToDefault()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        AssetDatabase.DeleteAsset($"{UiPrefabDir}/{ProfileUI.UIName}.prefab");
        RemoveHomeButton();
        Build();
    }

    private static string BuildIfMissing()
    {
        string path = $"{UiPrefabDir}/{ProfileUI.UIName}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return $"{ProfileUI.UIName}: kept";

        GameObject root = BuildProfile();
        OperatorSplashFrames.FitInto(root, ProfileUI.UIName);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return $"{ProfileUI.UIName}: built at {path}";
    }

    // ================================================================== the screen

    private static GameObject BuildProfile()
    {
        GameObject root = new GameObject(ProfileUI.UIName, typeof(RectTransform), typeof(CanvasGroup), typeof(ProfileUI));
        ProfileUI ui = root.GetComponent<ProfileUI>();
        RectTransform rect = (RectTransform)root.transform;
        Stretch(rect);

        // ---- behind: Home, blurred and darkened. It takes every click, so nothing on Home is
        // pressed through the card.
        UnityEngine.UI.Image blur = NewImage("Blur", rect, new Color(0.42f, 0.43f, 0.45f, 1f), true);
        Stretch(blur.rectTransform);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(BlurMaterialPath);
        if (material != null)
        {
            blur.material = material;
            blur.gameObject.AddComponent<UiBlurCapture>();
        }
        else
        {
            blur.color = Ink;
        }
        ui.blur = blur;

        Stretch(NewImage("Veil", rect, new Color(0f, 0f, 0f, 0.35f), false).rectTransform);

        // ---- top left: back, then the one tab
        UnityEngine.UI.Image back = NewImage("BackButton", rect, new Color(0.08f, 0.085f, 0.095f, 0.92f), true);
        Place(back.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(40f, -30f), new Vector2(150f, 64f));
        Chevron(back.rectTransform, "Glyph", Vector2.zero, 30f, Color.white);
        ui.backButton = back.gameObject.AddComponent<Button>();
        ui.backButton.targetGraphic = back;

        UnityEngine.UI.Image tab = NewImage("ProfileTab", rect, Light, false);
        Place(tab.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(40f, -130f), new Vector2(250f, 88f));
        IdCard(tab.rectTransform, "Icon", new Vector2(-78f, 0f), 58f, PaperInk, Light);
        TextMeshProUGUI tabLabel = NewText("Label", tab.rectTransform, 34f, PaperInk, TextAlignmentOptions.Left);
        Place(tabLabel.rectTransform, Center, Center, Center, new Vector2(36f, 0f), new Vector2(150f, 50f));
        tabLabel.text = "Profile";

        // ---- the card
        UnityEngine.UI.Image face = NewImage("Card", rect, CardFace, true);
        Place(face.rectTransform, Center, Center, Center, new Vector2(110f, -20f), new Vector2(1460f, 760f));
        RectTransform card = face.rectTransform;
        AddEdges(card, new Color(1f, 1f, 1f, 0.6f), 2f);
        ui.card = card.gameObject.AddComponent<CanvasGroup>();

        BuildAssistantSide(card, ui);
        BuildCardSide(card, ui);
        return root;
    }

    /// <summary>
    /// 左半边 / The assistant standing on the left half, and over its left edge - darkened by the
    /// frame's shade - when the account was made, how far through the campaign, how many operators.
    /// </summary>
    private static void BuildAssistantSide(RectTransform card, ProfileUI ui)
    {
        UnityEngine.UI.Image well = NewImage("Assistant", card, Well, false);
        Place(well.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(640f, 0f));
        RectTransform side = well.rectTransform;

        // OperatorSplashFrames makes this a frame, clipped to the side, with a shade along its left.
        UnityEngine.UI.Image art = NewImage("Art", side, Color.white, false);
        Place(art.rectTransform, Center, Center, Center, new Vector2(110f, -10f), new Vector2(460f, 740f));
        ui.assistantArt = art;

        UnityEngine.UI.Image since = NewImage("SinceChip", side, Accent, false);
        Place(since.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(28f, -30f), new Vector2(78f, 30f));
        TextMeshProUGUI sinceLabel = NewText("Label", since.rectTransform, 17f, Color.white, TextAlignmentOptions.Center);
        Stretch(sinceLabel.rectTransform);
        sinceLabel.text = "SINCE";
        sinceLabel.fontStyle = FontStyles.Bold;
        ui.registeredText = TopLeftText(side, "Registered", new Vector2(116f, -30f), new Vector2(220f, 30f), 24f, Light);

        UnityEngine.UI.Image caption = NewImage("AssistantChip", side, Light, false);
        Place(caption.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(28f, -92f), new Vector2(150f, 30f));
        TextMeshProUGUI captionLabel = NewText("Label", caption.rectTransform, 18f, PaperInk, TextAlignmentOptions.Center);
        Stretch(captionLabel.rectTransform);
        captionLabel.text = "ASSISTANT";
        captionLabel.fontStyle = FontStyles.Bold;
        ui.assistantName = TopLeftText(side, "AssistantName", new Vector2(28f, -128f), new Vector2(320f, 50f), 38f, Color.white);
        ui.assistantName.fontStyle = FontStyles.Bold;

        TopLeftText(side, "ProgressCaption", new Vector2(28f, -250f), new Vector2(240f, 30f), 22f, Dim).text = "Progress";
        Diamond(side, "ProgressGlyph", new Vector2(50f, -326f), 30f);
        ui.progressText = TopLeftText(side, "Progress", new Vector2(84f, -290f), new Vector2(260f, 72f), 64f, Color.white);
        ui.progressText.fontStyle = FontStyles.Bold;

        TopLeftText(side, "OperatorsCaption", new Vector2(28f, -410f), new Vector2(240f, 30f), 22f, Dim).text = "Operators";
        ui.operatorsText = TopLeftText(side, "Operators", new Vector2(28f, -450f), new Vector2(260f, 72f), 64f, Color.white);
        ui.operatorsText.fontStyle = FontStyles.Bold;
    }

    /// <summary>
    /// 右半边 / The level disc and the name with its tag, the signature under them, and the rhythm
    /// record in four tiles where the reference has its support units.
    /// </summary>
    private static void BuildCardSide(RectTransform card, ProfileUI ui)
    {
        UnityEngine.UI.Image disc = NewImage("LevelDisc", card, Yellow, false);
        disc.sprite = Knob();
        Place(disc.rectTransform, TopLeft, TopLeft, Center, new Vector2(738f, -80f), new Vector2(100f, 100f));
        UnityEngine.UI.Image inner = NewImage("Inner", disc.rectTransform, Ink, false);
        inner.sprite = Knob();
        Place(inner.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(86f, 86f));
        TextMeshProUGUI lv = NewText("Caption", disc.rectTransform, 16f, Dim, TextAlignmentOptions.Center);
        Place(lv.rectTransform, Center, Center, Center, new Vector2(0f, 24f), new Vector2(80f, 20f));
        lv.text = "LV";
        ui.levelText = NewText("Level", disc.rectTransform, 38f, Color.white, TextAlignmentOptions.Center);
        Place(ui.levelText.rectTransform, Center, Center, Center, new Vector2(0f, -8f), new Vector2(84f, 46f));
        ui.levelText.fontStyle = FontStyles.Bold;

        ui.nameText = NewText("Name", card, 46f, Color.white, TextAlignmentOptions.Left);
        Place(ui.nameText.rectTransform, TopLeft, TopLeft, new Vector2(0f, 0.5f), new Vector2(810f, -80f), new Vector2(610f, 64f));
        ui.nameText.fontStyle = FontStyles.Bold;
        ui.nameText.enableAutoSizing = true;
        ui.nameText.fontSizeMin = 28f;
        ui.nameText.fontSizeMax = 46f;
        ui.nameText.textWrappingMode = TextWrappingModes.NoWrap;
        ui.nameText.richText = true;

        BuildSignature(card, ui);

        UnityEngine.UI.Image rule = NewImage("Divider", card, Rule, false);
        Place(rule.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(690f, -238f), new Vector2(730f, 2f));

        UnityEngine.UI.Image bar = NewImage("RecordBar", card, Accent, false);
        Place(bar.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(690f, -262f), new Vector2(6f, 28f));
        TextMeshProUGUI header = TopLeftText(card, "RecordHeader", new Vector2(708f, -262f), new Vector2(400f, 28f), 24f, Light);
        header.text = "RHYTHM RECORD";
        header.fontStyle = FontStyles.Bold;

        ui.playsText = StatTile(card, "PlaysTile", new Vector2(690f, -310f), "PLAYS");
        ui.clearedText = StatTile(card, "ClearedTile", new Vector2(1070f, -310f), "SONGS CLEARED");
        ui.fullCombosText = StatTile(card, "FullComboTile", new Vector2(690f, -520f), "FULL COMBO");
        ui.bestScoreText = StatTile(card, "BestScoreTile", new Vector2(1070f, -520f), "BEST SCORE");
    }

    /// <summary>
    /// The line the player wrote, as an input that is only interactable on their own card. Rich text
    /// is off on both the field and its text: it is somebody's typing, shown on other people's
    /// screens, and a typed tag must read as the characters it is.
    /// </summary>
    private static void BuildSignature(RectTransform card, ProfileUI ui)
    {
        GameObject go = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
        go.name = "Signature";
        RectTransform box = (RectTransform)go.transform;
        box.SetParent(card, false);
        Place(box, TopLeft, TopLeft, TopLeft, new Vector2(690f, -136f), new Vector2(730f, 78f));

        UnityEngine.UI.Image boxImage = go.GetComponent<UnityEngine.UI.Image>();
        boxImage.sprite = null;
        boxImage.type = UnityEngine.UI.Image.Type.Simple;
        boxImage.color = Field;

        TMP_InputField input = go.GetComponent<TMP_InputField>();
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.richText = false;
        input.characterLimit = Promuse.Contracts.Players.PlayerProfileLimits.SignatureMaxLength;
        input.customCaretColor = true;
        input.caretColor = Color.white;
        input.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.5f);

        ColorBlock colors = input.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.disabledColor = Color.white;
        input.colors = colors;

        RectTransform area = input.textViewport;
        area.offsetMin = new Vector2(22f, 8f);
        area.offsetMax = new Vector2(-64f, -8f);

        TextMeshProUGUI text = (TextMeshProUGUI)input.textComponent;
        Restyle(text, 26f, Color.white);
        text.richText = false;

        TextMeshProUGUI placeholder = (TextMeshProUGUI)input.placeholder;
        Restyle(placeholder, 26f, Dim);
        placeholder.fontStyle = FontStyles.Italic;
        placeholder.text = "";

        RectTransform pen = NewRect("EditHint", box);
        Place(pen, MiddleRight, MiddleRight, Center, new Vector2(-34f, 0f), new Vector2(32f, 32f));
        Pen(pen, Dim);

        ui.signatureField = input;
        ui.signaturePlaceholder = placeholder;
        ui.editHint = pen.gameObject;
    }

    private static void Restyle(TextMeshProUGUI text, float size, Color color)
    {
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
    }

    /// <summary>One tile of the rhythm record: a caption over a big number, an accent down its left.</summary>
    private static TextMeshProUGUI StatTile(RectTransform card, string name, Vector2 topLeft, string caption)
    {
        UnityEngine.UI.Image tile = NewImage(name, card, Well, false);
        Place(tile.rectTransform, TopLeft, TopLeft, TopLeft, topLeft, new Vector2(350f, 190f));

        UnityEngine.UI.Image strip = NewImage("Strip", tile.rectTransform, Accent, false);
        Place(strip.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(6f, 0f));

        TopLeftText(tile.rectTransform, "Caption", new Vector2(26f, -22f), new Vector2(310f, 30f), 22f, Dim).text = caption;

        TextMeshProUGUI value = TopLeftText(tile.rectTransform, "Value", new Vector2(26f, -70f), new Vector2(310f, 96f), 64f, Color.white);
        value.fontStyle = FontStyles.Bold;
        value.enableAutoSizing = true;
        value.fontSizeMin = 32f;
        value.fontSizeMax = 64f;
        value.textWrappingMode = TextWrappingModes.NoWrap;
        return value;
    }

    // ================================================================== Home

    /// <summary>
    /// 新闻屏旁边 / The button beside the news panel, inside LeftPanel so it takes the panel's tilt.
    /// One that exists anywhere on Home is left exactly where it is.
    /// </summary>
    private static string HookHome()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(HomePrefabPath) == null) return "HomeUI: no prefab, no button";

        GameObject contents = PrefabUtility.LoadPrefabContents(HomePrefabPath);
        try
        {
            if (FindDeep(contents.transform, ButtonName) != null) return $"HomeUI {ButtonName}: kept";

            RectTransform news = FindDeep(contents.transform, NewsPanel) as RectTransform;
            if (news == null) return $"HomeUI: no {NewsPanel} to sit beside, no button";

            // LeftPanel when Apply Parallax Groups has made it; otherwise beside the news wherever it is.
            RectTransform panel = FindDeep(contents.transform, LeftPanel) as RectTransform;
            RectTransform parent = panel != null && news.parent == panel ? panel : (RectTransform)news.parent;

            RectTransform button = BuildHomeButton(parent);
            button.anchoredPosition = news.anchoredPosition + new Vector2(news.sizeDelta.x * 0.5f + 14f + button.sizeDelta.x * 0.5f, 0f);
            button.SetSiblingIndex(news.GetSiblingIndex() + 1);

            PrefabUtility.SaveAsPrefabAsset(contents, HomePrefabPath);
            return $"HomeUI {ButtonName}: placed in {parent.name} beside {NewsPanel} -> {ProfileUI.UIName}";
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static RectTransform BuildHomeButton(RectTransform parent)
    {
        UnityEngine.UI.Image face = NewImage(ButtonName, parent, Tile, true);
        face.gameObject.layer = parent.gameObject.layer;
        RectTransform rect = face.rectTransform;
        Place(rect, Center, Center, Center, Vector2.zero, new Vector2(150f, 164f));

        UnityEngine.UI.Image strip = NewImage("Strip", rect, Accent, false);
        Place(strip.rectTransform, TopLeft, TopRight, TopCenter, Vector2.zero, new Vector2(0f, 6f));

        IdCard(rect, "Icon", new Vector2(0f, 18f), 72f, Color.white, Tile);

        TextMeshProUGUI label = NewText("Label", rect, 22f, Color.white, TextAlignmentOptions.Center);
        Place(label.rectTransform, Center, Center, Center, new Vector2(0f, -50f), new Vector2(150f, 30f));
        label.text = "PROFILE";
        label.fontStyle = FontStyles.Bold;

        Button button = face.gameObject.AddComponent<Button>();
        button.targetGraphic = face;
        face.gameObject.AddComponent<OpenUIButton>().uiName = ProfileUI.UIName;

        foreach (Transform child in rect.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = parent.gameObject.layer;
        return rect;
    }

    private static void RemoveHomeButton()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(HomePrefabPath) == null) return;

        GameObject contents = PrefabUtility.LoadPrefabContents(HomePrefabPath);
        try
        {
            Transform button = FindDeep(contents.transform, ButtonName);
            if (button == null) return;

            Object.DestroyImmediate(button.gameObject);
            PrefabUtility.SaveAsPrefabAsset(contents, HomePrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // ================================================================== glyphs

    /// <summary>An ID card: an outline, a head and shoulders on the left, two lines of text on the right.</summary>
    private static void IdCard(RectTransform parent, string name, Vector2 position, float width, Color ink, Color paper)
    {
        RectTransform icon = NewRect(name, parent);
        Place(icon, Center, Center, Center, position, new Vector2(width, width * 0.7f));
        float t = Mathf.Max(2f, width * 0.05f);
        AddEdges(icon, ink, t);

        UnityEngine.UI.Image head = NewImage("Head", icon, ink, false);
        head.sprite = Knob();
        Place(head.rectTransform, Center, Center, Center, new Vector2(-width * 0.2f, width * 0.07f), new Vector2(width * 0.2f, width * 0.2f));
        UnityEngine.UI.Image body = NewImage("Body", icon, ink, false);
        Place(body.rectTransform, Center, Center, Center, new Vector2(-width * 0.2f, -width * 0.14f), new Vector2(width * 0.3f, width * 0.12f));

        Bar(icon, "Line1", new Vector2(width * 0.17f, width * 0.07f), new Vector2(width * 0.3f, t), 0f, ink);
        Bar(icon, "Line2", new Vector2(width * 0.17f, -width * 0.06f), new Vector2(width * 0.3f, t), 0f, ink);
    }

    /// <summary>The diamond in front of the reference's progress, a square on its point.</summary>
    private static void Diamond(RectTransform parent, string name, Vector2 topLeft, float size)
    {
        RectTransform spot = NewRect(name, parent);
        Place(spot, TopLeft, TopLeft, Center, topLeft, Vector2.zero);
        Bar(spot, "Outer", Vector2.zero, new Vector2(size, size), 45f, Light);
        Bar(spot, "Inner", Vector2.zero, new Vector2(size * 0.45f, size * 0.45f), 45f, Accent);
    }

    /// <summary>A pencil laid at 45 degrees over a short rule.</summary>
    private static void Pen(RectTransform parent, Color color)
    {
        Bar(parent, "Shaft", new Vector2(2f, 3f), new Vector2(6f, 26f), -45f, color);
        Bar(parent, "Rule", new Vector2(0f, -13f), new Vector2(26f, 3f), 0f, color);
    }

    private static void Chevron(RectTransform parent, string name, Vector2 position, float size, Color color)
    {
        RectTransform spot = NewRect(name, parent);
        Place(spot, Center, Center, Center, position, new Vector2(size, size));
        float arm = size * 0.62f, t = Mathf.Max(3f, size * 0.13f);
        Bar(spot, "Upper", new Vector2(0f, size * 0.2f), new Vector2(arm, t), 45f, color);
        Bar(spot, "Lower", new Vector2(0f, -size * 0.2f), new Vector2(arm, t), -45f, color);
    }

    // ================================================================== helpers

    private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
    private static readonly Vector2 TopLeft = new Vector2(0f, 1f);
    private static readonly Vector2 TopCenter = new Vector2(0.5f, 1f);
    private static readonly Vector2 TopRight = new Vector2(1f, 1f);
    private static readonly Vector2 MiddleRight = new Vector2(1f, 0.5f);

    private static Sprite Knob() => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

    /// <summary>A text placed by its top-left corner, measured from the parent's top-left.</summary>
    private static TextMeshProUGUI TopLeftText(RectTransform parent, string name, Vector2 position, Vector2 size,
                                               float fontSize, Color color)
    {
        TextMeshProUGUI text = NewText(name, parent, fontSize, color, TextAlignmentOptions.TopLeft);
        Place(text.rectTransform, TopLeft, TopLeft, TopLeft, position, size);
        return text;
    }

    private static void Bar(RectTransform parent, string name, Vector2 position, Vector2 size, float degrees, Color color)
    {
        UnityEngine.UI.Image bar = NewImage(name, parent, color, false);
        Place(bar.rectTransform, Center, Center, Center, position, size);
        bar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, degrees);
    }

    private static void AddEdges(RectTransform parent, Color color, float thickness)
    {
        Edge(parent, "EdgeTop", TopLeft, TopRight, new Vector2(0f, -thickness / 2f), new Vector2(0f, thickness), color);
        Edge(parent, "EdgeBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, thickness / 2f), new Vector2(0f, thickness), color);
        Edge(parent, "EdgeLeft", new Vector2(0f, 0f), TopLeft, new Vector2(thickness / 2f, 0f), new Vector2(thickness, 0f), color);
        Edge(parent, "EdgeRight", new Vector2(1f, 0f), TopRight, new Vector2(-thickness / 2f, 0f), new Vector2(thickness, 0f), color);
    }

    private static void Edge(RectTransform parent, string name, Vector2 min, Vector2 max, Vector2 position, Vector2 size, Color color)
    {
        UnityEngine.UI.Image edge = NewImage(name, parent, color, false);
        edge.rectTransform.anchorMin = min;
        edge.rectTransform.anchorMax = max;
        edge.rectTransform.pivot = Center;
        edge.rectTransform.anchoredPosition = position;
        edge.rectTransform.sizeDelta = size;
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
