using System.Collections.Generic;
using TMPro;
using UI.Sub;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 举报签名 / Builds the report form and puts its button on the profile card:
///   - SignatureReportUI: a dark veil, a panel with the line being reported, three reasons (spam,
///     offensive, other), an optional note and SEND REPORT,
///   - ReportButton: a flag at the right end of ProfileUI's signature box - where the pen sits on
///     the player's own card - shown only on someone else's card with a line.
///
/// 找到就不动 / Find-or-create: a prefab or a button that exists is kept. Reset Signature Report UI To
/// Default deletes both and builds them again.
///
/// The glyphs are drawn from rectangles: the default TMP font has no flag or cross to trust.
/// </summary>
public static class SignatureReportUISetup
{
    private const string UiPrefabDir = "Assets/Arknights/Resources/Prefab/UI";
    private const string ProfilePrefabPath = UiPrefabDir + "/" + ProfileUI.UIName + ".prefab";

    public const string ButtonName = "ReportButton";
    private const string SignatureBox = "Signature";

    private static readonly Color PanelFace = new Color(0.12f, 0.125f, 0.14f, 0.98f);
    private static readonly Color Field = new Color(0.09f, 0.095f, 0.105f, 1f);
    private static readonly Color Tile = new Color(0.2f, 0.21f, 0.23f, 1f);
    private static readonly Color Accent = new Color(0.10f, 0.55f, 0.80f, 1f);
    private static readonly Color Danger = new Color(0.80f, 0.27f, 0.24f, 1f);
    private static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);

    private static readonly string[] Reasons = { "SPAM / ADS", "OFFENSIVE", "OTHER" };

    // ------------------------------------------------------------------ menu items

    [MenuItem("Arknights/Home/Build Signature Report UI", false, 36)]
    public static void Build()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        List<string> done = new List<string> { BuildIfMissing(), HookProfile() };

        AssetDatabase.SaveAssets();
        Debug.Log("[SignatureReportUISetup] " + string.Join("\n  ", done) +
                  "\n  Device builds read the UI bundle: rebuild the AssetBundles before testing on a phone.");
    }

    [MenuItem("Arknights/Home/Reset Signature Report UI To Default", false, 37)]
    public static void ResetToDefault()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        AssetDatabase.DeleteAsset($"{UiPrefabDir}/{SignatureReportUI.UIName}.prefab");
        RemoveProfileButton();
        Build();
    }

    private static string BuildIfMissing()
    {
        string path = $"{UiPrefabDir}/{SignatureReportUI.UIName}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return $"{SignatureReportUI.UIName}: kept";

        GameObject root = BuildScreen();
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return $"{SignatureReportUI.UIName}: built at {path}";
    }

    // ================================================================== the form

    private static GameObject BuildScreen()
    {
        GameObject root = new GameObject(SignatureReportUI.UIName, typeof(RectTransform), typeof(CanvasGroup), typeof(SignatureReportUI));
        SignatureReportUI ui = root.GetComponent<SignatureReportUI>();
        RectTransform rect = (RectTransform)root.transform;
        Stretch(rect);

        UnityEngine.UI.Image veil = NewImage("Veil", rect, new Color(0f, 0f, 0f, 0.72f), true);
        Stretch(veil.rectTransform);
        ui.veilButton = veil.gameObject.AddComponent<UnityEngine.UI.Button>();
        ui.veilButton.targetGraphic = veil;
        ui.veilButton.transition = Selectable.Transition.None;

        UnityEngine.UI.Image panel = NewImage("Panel", rect, PanelFace, true);
        Place(panel.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(940f, 600f));
        RectTransform p = panel.rectTransform;
        UnityEngine.UI.Image strip = NewImage("Strip", p, Danger, false);
        Place(strip.rectTransform, TopLeft, new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 6f));

        TextMeshProUGUI title = Text(p, "Title", new Vector2(44f, -34f), new Vector2(700f, 52f), 40f, Color.white);
        title.text = "REPORT SIGNATURE";
        title.fontStyle = FontStyles.Bold;
        ui.whoText = Text(p, "Who", new Vector2(44f, -88f), new Vector2(760f, 34f), 24f, Dim);

        UnityEngine.UI.Image close = NewImage("CloseButton", p, new Color(0.30f, 0.31f, 0.33f, 1f), true);
        close.sprite = Knob();
        Place(close.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), Center, new Vector2(-50f, -50f), new Vector2(60f, 60f));
        Bar(close.rectTransform, "A", Vector2.zero, new Vector2(28f, 6f), 45f, Color.white);
        Bar(close.rectTransform, "B", Vector2.zero, new Vector2(28f, 6f), -45f, Color.white);
        ui.closeButton = close.gameObject.AddComponent<UnityEngine.UI.Button>();
        ui.closeButton.targetGraphic = close;

        // 被举报的那句话 / The line, as it was read, in its own well - never as rich text.
        UnityEngine.UI.Image well = NewImage("Line", p, Field, false);
        Place(well.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(44f, -136f), new Vector2(852f, 84f));
        ui.lineText = NewText("Text", well.rectTransform, 26f, Color.white, TextAlignmentOptions.MidlineLeft);
        Stretch(ui.lineText.rectTransform);
        ui.lineText.rectTransform.offsetMin = new Vector2(20f, 6f);
        ui.lineText.rectTransform.offsetMax = new Vector2(-20f, -6f);
        ui.lineText.richText = false;
        ui.lineText.fontStyle = FontStyles.Italic;
        ui.lineText.textWrappingMode = TextWrappingModes.Normal;
        ui.lineText.overflowMode = TextOverflowModes.Ellipsis;

        Text(p, "ReasonCaption", new Vector2(44f, -244f), new Vector2(400f, 30f), 22f, Dim).text = "REASON";

        ui.reasonButtons = new UnityEngine.UI.Button[Reasons.Length];
        for (int i = 0; i < Reasons.Length; i++)
        {
            UnityEngine.UI.Image face = NewImage("Reason" + i, p, Tile, true);
            Place(face.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(44f + i * 290f, -282f), new Vector2(272f, 66f));
            TextMeshProUGUI label = NewText("Label", face.rectTransform, 24f, Color.white, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            label.text = Reasons[i];
            label.fontStyle = FontStyles.Bold;
            UnityEngine.UI.Button button = face.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = face;
            button.transition = Selectable.Transition.None;
            ui.reasonButtons[i] = button;
        }

        Text(p, "NoteCaption", new Vector2(44f, -374f), new Vector2(400f, 30f), 22f, Dim).text = "NOTE (OPTIONAL)";
        ui.noteField = NoteField(p);

        UnityEngine.UI.Image send = NewImage("SendButton", p, Danger, true);
        Place(send.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-44f, 36f), new Vector2(300f, 70f));
        ui.sendLabel = NewText("Label", send.rectTransform, 26f, Color.white, TextAlignmentOptions.Center);
        Stretch(ui.sendLabel.rectTransform);
        ui.sendLabel.text = "SEND REPORT";
        ui.sendLabel.fontStyle = FontStyles.Bold;
        ui.sendButton = send.gameObject.AddComponent<UnityEngine.UI.Button>();
        ui.sendButton.targetGraphic = send;

        return root;
    }

    private static TMP_InputField NoteField(RectTransform panel)
    {
        GameObject go = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
        go.name = "Note";
        RectTransform box = (RectTransform)go.transform;
        box.SetParent(panel, false);
        Place(box, TopLeft, TopLeft, TopLeft, new Vector2(44f, -412f), new Vector2(852f, 66f));

        UnityEngine.UI.Image face = go.GetComponent<UnityEngine.UI.Image>();
        face.sprite = null;
        face.type = UnityEngine.UI.Image.Type.Simple;
        face.color = Field;

        TMP_InputField input = go.GetComponent<TMP_InputField>();
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.richText = false;
        input.characterLimit = Promuse.Contracts.Moderation.SignatureReportLimits.NoteMaxLength;
        input.customCaretColor = true;
        input.caretColor = Color.white;
        input.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.5f);

        ColorBlock colors = input.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.disabledColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        input.colors = colors;

        input.textViewport.offsetMin = new Vector2(20f, 8f);
        input.textViewport.offsetMax = new Vector2(-20f, -8f);

        TextMeshProUGUI text = (TextMeshProUGUI)input.textComponent;
        Restyle(text, 24f, Color.white);
        text.richText = false;

        TextMeshProUGUI placeholder = (TextMeshProUGUI)input.placeholder;
        Restyle(placeholder, 24f, Dim);
        placeholder.fontStyle = FontStyles.Italic;
        placeholder.text = "What should the moderators know?";

        return input;
    }

    // ================================================================== the card

    /// <summary>
    /// 签名框右端 / At the right end of the card's signature box, where the pen is on the player's own
    /// card - the two never show together. One that exists anywhere on the card is left where it is.
    /// </summary>
    private static string HookProfile()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ProfilePrefabPath) == null) return "ProfileUI: no prefab, no button";

        GameObject contents = PrefabUtility.LoadPrefabContents(ProfilePrefabPath);
        try
        {
            ProfileUI profile = contents.GetComponent<ProfileUI>();
            Transform existing = FindDeep(contents.transform, ButtonName);
            string what;

            if (existing != null)
            {
                what = $"ProfileUI {ButtonName}: kept";
            }
            else
            {
                RectTransform box = FindDeep(contents.transform, SignatureBox) as RectTransform;
                if (box == null) return $"ProfileUI: no {SignatureBox} box to put the button in";

                existing = BuildProfileButton(box);
                what = $"ProfileUI {ButtonName}: placed in {box.name} -> {SignatureReportUI.UIName}";
            }

            // 绑定字段 / Bound whether built now or found, so a card built before the field existed
            // still gets it.
            if (profile != null && profile.reportButton == null)
            {
                profile.reportButton = existing.GetComponent<UnityEngine.UI.Button>();
                what += ", bound";
            }

            PrefabUtility.SaveAsPrefabAsset(contents, ProfilePrefabPath);
            return what;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static RectTransform BuildProfileButton(RectTransform box)
    {
        UnityEngine.UI.Image face = NewImage(ButtonName, box, new Color(1f, 1f, 1f, 0.001f), true);
        RectTransform rect = face.rectTransform;
        Place(rect, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Center, new Vector2(-34f, 0f), new Vector2(56f, 56f));

        // 小旗 / A pole and its flag.
        UnityEngine.UI.Image pole = NewImage("Pole", rect, Dim, false);
        Place(pole.rectTransform, Center, Center, Center, new Vector2(-9f, -2f), new Vector2(4f, 34f));
        UnityEngine.UI.Image flag = NewImage("Flag", rect, Danger, false);
        Place(flag.rectTransform, Center, Center, Center, new Vector2(3f, 7f), new Vector2(22f, 15f));

        UnityEngine.UI.Button button = face.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = face;
        button.transition = Selectable.Transition.None;

        // 默认藏着 / Hidden until ProfileUI shows it on a card that can be reported.
        face.gameObject.SetActive(false);
        return rect;
    }

    private static void RemoveProfileButton()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ProfilePrefabPath) == null) return;

        GameObject contents = PrefabUtility.LoadPrefabContents(ProfilePrefabPath);
        try
        {
            Transform button = FindDeep(contents.transform, ButtonName);
            if (button == null) return;

            Object.DestroyImmediate(button.gameObject);
            ProfileUI profile = contents.GetComponent<ProfileUI>();
            if (profile != null) profile.reportButton = null;
            PrefabUtility.SaveAsPrefabAsset(contents, ProfilePrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // ================================================================== helpers

    private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
    private static readonly Vector2 TopLeft = new Vector2(0f, 1f);

    private static Sprite Knob() => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

    private static TextMeshProUGUI Text(RectTransform parent, string name, Vector2 topLeft, Vector2 size, float fontSize, Color color)
    {
        TextMeshProUGUI text = NewText(name, parent, fontSize, color, TextAlignmentOptions.MidlineLeft);
        Place(text.rectTransform, TopLeft, TopLeft, TopLeft, topLeft, size);
        return text;
    }

    private static void Restyle(TextMeshProUGUI text, float size, Color color)
    {
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
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

    private static TextMeshProUGUI NewText(string name, Transform parent, float size, Color color, TextAlignmentOptions alignment)
    {
        TextMeshProUGUI text = NewRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
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
