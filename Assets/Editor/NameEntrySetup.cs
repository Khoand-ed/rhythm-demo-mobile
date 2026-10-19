using Promuse.Contracts.Players;
using UI.Sub;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 起名界面 / Builds the name screen inside LoginUI.prefab - "Do you remember your name?", one field,
/// the rule in yellow under it and a Confirm button - the step between signing up and Home. It sits
/// on the black backdrop with the wire ball sunk to the bottom edge, as the loading screen keeps it.
///
/// 照着登录页做 / The field and the button are copies of the login page's own, so the screen keeps
/// whatever look those were given; the two lines of text use their font.
///
/// 找到就不动 / Find-or-create: a NamePanel already in the prefab is kept, and only its links to
/// LoginUI are filled in. Reset Name Entry To Default deletes it and builds it again.
/// </summary>
public static class NameEntrySetup
{
    private const string LoginPrefabPath = "Assets/Arknights/Resources/Prefab/UI/LoginUI.prefab";

    public const string PanelName = "NamePanel";

    private static readonly Color Rule = new Color(0.96f, 0.76f, 0.23f, 1f);
    // 线性空间里 6% 的白已经像一行标题 / In Linear space even 6% white reads as a second heading;
    // this stays a shape behind the question.
    private static readonly Color Watermark = new Color(1f, 1f, 1f, 0.012f);

    // ------------------------------------------------------------------ menu items

    [MenuItem("Arknights/Login/Build Name Entry", false, 40)]
    public static void Build()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        GameObject root = PrefabUtility.LoadPrefabContents(LoginPrefabPath);
        try
        {
            LoginUI ui = root.GetComponent<LoginUI>();
            if (ui == null)
            {
                Debug.LogError($"[NameEntrySetup] {LoginPrefabPath} has no LoginUI.");
                return;
            }

            string done = BuildInto(ui);
            PrefabUtility.SaveAsPrefabAsset(root, LoginPrefabPath);
            Debug.Log("[NameEntrySetup] " + done +
                      "\n  Device builds read the UI bundle: rebuild the AssetBundles before testing on a phone.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [MenuItem("Arknights/Login/Reset Name Entry To Default", false, 41)]
    public static void ResetToDefault()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        GameObject root = PrefabUtility.LoadPrefabContents(LoginPrefabPath);
        try
        {
            Transform panel = root.transform.Find(PanelName);
            if (panel != null) Object.DestroyImmediate(panel.gameObject);
            PrefabUtility.SaveAsPrefabAsset(root, LoginPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        Build();
    }

    /// <summary>
    /// Finds or builds the panel under <paramref name="ui"/>'s root and links it. Also called by
    /// Generate Bootstrap Assets, which builds the whole LoginUI from nothing.
    /// </summary>
    public static string BuildInto(LoginUI ui)
    {
        Transform root = Root(ui);
        string what;

        if (root.Find(PanelName) == null)
        {
            BuildPanel(ui);
            what = $"{PanelName}: built";
        }
        else
        {
            what = $"{PanelName}: kept";
        }

        Transform panel = root.Find(PanelName);
        ui.namePanel = panel.GetComponent<CanvasGroup>();
        ui.name_input = panel.Find("name_input").GetComponent<InputField>();
        ui.name_hint = panel.Find("name_hint").GetComponent<Text>();
        ui.name_confirm = panel.Find("name_confirm").GetComponent<Button>();

        return what + ", linked to LoginUI (namePanel, name_input, name_hint, name_confirm)";
    }

    // ================================================================== the panel

    private static void BuildPanel(LoginUI ui)
    {
        GameObject panel = new GameObject(PanelName, typeof(RectTransform), typeof(CanvasGroup));
        RectTransform rect = (RectTransform)panel.transform;
        rect.SetParent(Root(ui), false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        panel.GetComponent<CanvasGroup>().alpha = 0f;

        // 在加载页之后, 上下黑条之前 / Over the loading page, under the black bars that frame every step.
        rect.SetSiblingIndex(ui.loadingPanel.transform.GetSiblingIndex() + 1);

        Font font = ui.login_userName.textComponent.font;

        Text mark = Label(rect, "Watermark", "RHODES ISLAND", font, 128, Watermark, new Vector2(0f, 70f));
        mark.fontStyle = FontStyle.Bold;

        Label(rect, "Title", "Do you remember your name?", font, 34, Color.white, new Vector2(0f, 120f));

        InputField field = Object.Instantiate(ui.login_userName.gameObject, rect).GetComponent<InputField>();
        field.name = "name_input";
        Place((RectTransform)field.transform, new Vector2(0f, -70f), new Vector2(450f, 66f));
        field.text = "";
        field.contentType = InputField.ContentType.Standard;
        field.characterLimit = PlayerNameRules.MaxLength;
        field.characterValidation = InputField.CharacterValidation.Alphanumeric;
        ((Text)field.placeholder).text = "Your name";

        Text hint = Label(rect, "name_hint", PlayerNameRules.Description, font, 22, Rule, new Vector2(0f, -140f));
        ((RectTransform)hint.transform).sizeDelta = new Vector2(1100f, 40f);

        Button confirm = Object.Instantiate(ui.login_enter.gameObject, rect).GetComponent<Button>();
        confirm.name = "name_confirm";
        Place((RectTransform)confirm.transform, new Vector2(0f, -218f), new Vector2(210f, 58f));
        Text caption = confirm.transform.Find("Text").GetComponent<Text>();
        caption.text = "Confirm";
        caption.fontSize = 24;
        ((RectTransform)caption.transform).sizeDelta = new Vector2(210f, 58f);

        panel.SetActive(false);
    }

    /// <summary>
    /// UIBase hides <c>transform</c> behind a field it fills in Awake, which never runs on a prefab
    /// being edited - so the component's own transform, asked for as a Component.
    /// </summary>
    private static Transform Root(LoginUI ui) => ((Component)ui).transform;

    private static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static Text Label(RectTransform parent, string name, string text, Font font, int size, Color color, Vector2 position)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        Place(rect, position, new Vector2(900f, 60f));

        Text t = go.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        t.text = text;
        return t;
    }
}
