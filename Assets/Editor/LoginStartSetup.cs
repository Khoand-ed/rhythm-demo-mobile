using UI.Sub;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 开始键和登出键 / Builds the two buttons the login screen's title shows while the device holds a
/// session: START, one wide button where ACCOUNT LOGIN and REGISTER stand side by side, and LOG OUT in
/// the top-left corner just under the black bar, where the sign-in form keeps its back arrow. LoginUI
/// shows one pair or the other, and wires both clicks in code.
///
/// 照着登录键做 / Both are copies of home_login, so they keep whatever look the title's buttons were
/// given. The copy's persistent clicks are cleared: a Hide on LOG OUT would make it the screen's exit
/// button, and LoginUI must have none (BackKeyTests).
///
/// 找到就不动 / Find-or-create: a home_start or home_logout already in HomePanel is kept, and only its
/// link to LoginUI is filled in. Reset Start And Log Out Buttons To Default deletes both and builds
/// them again.
/// </summary>
public static class LoginStartSetup
{
    private const string LoginPrefabPath = "Assets/Arknights/Resources/Prefab/UI/LoginUI.prefab";

    public const string StartName = "home_start";
    public const string LogOutName = "home_logout";
    private const string PanelName = "HomePanel";

    private static readonly Vector2 StartSize = new Vector2(400f, 74f);
    private static readonly Vector2 LogOutSize = new Vector2(200f, 66f);

    // 表单返回键的位置 / Where the sign-in form's back arrow sits, if the prefab has none to read it from.
    private static readonly Vector2 CornerPosition = new Vector2(48f, -128f);

    // ------------------------------------------------------------------ menu items

    [MenuItem("Arknights/Login/Build Start And Log Out Buttons", false, 44)]
    public static void Build()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        GameObject root = PrefabUtility.LoadPrefabContents(LoginPrefabPath);
        try
        {
            LoginUI ui = root.GetComponent<LoginUI>();
            if (ui == null)
            {
                Debug.LogError($"[LoginStartSetup] {LoginPrefabPath} has no LoginUI.");
                return;
            }

            string done = BuildInto(ui);
            PrefabUtility.SaveAsPrefabAsset(root, LoginPrefabPath);
            Debug.Log("[LoginStartSetup] " + done +
                      "\n  Device builds read the UI bundle: rebuild the AssetBundles before testing on a phone.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [MenuItem("Arknights/Login/Reset Start And Log Out Buttons To Default", false, 45)]
    public static void ResetToDefault()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        GameObject root = PrefabUtility.LoadPrefabContents(LoginPrefabPath);
        try
        {
            Transform panel = root.transform.Find(PanelName);
            if (panel != null)
            {
                foreach (string name in new[] { StartName, LogOutName })
                {
                    Transform old = panel.Find(name);
                    if (old != null) Object.DestroyImmediate(old.gameObject);
                }
            }
            PrefabUtility.SaveAsPrefabAsset(root, LoginPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        Build();
    }

    /// <summary>
    /// Finds or builds both buttons in LoginUI's HomePanel and links them. Also called by Generate
    /// Bootstrap Assets, which builds the whole LoginUI from nothing.
    /// </summary>
    public static string BuildInto(LoginUI ui)
    {
        Transform panel = ui.homePanel != null ? ui.homePanel.transform : Root(ui).Find(PanelName);
        if (panel == null) return $"{StartName}, {LogOutName}: not built - LoginUI has no {PanelName}";
        if (ui.home_login == null || ui.home_register == null)
            return $"{StartName}, {LogOutName}: not built - LoginUI has no home_login and home_register to copy";

        string start = FindOrBuild(panel, StartName, () => BuildStart(ui, panel), out UnityEngine.UI.Button startButton);
        string logOut = FindOrBuild(panel, LogOutName, () => BuildLogOut(ui, panel), out UnityEngine.UI.Button logOutButton);

        ui.home_start = startButton;
        ui.home_logout = logOutButton;
        return $"{start}, {logOut}, linked to LoginUI (home_start, home_logout)";
    }

    private static string FindOrBuild(Transform panel, string name, System.Func<UnityEngine.UI.Button> build,
                                      out UnityEngine.UI.Button button)
    {
        Transform found = panel.Find(name);
        if (found != null)
        {
            button = found.GetComponent<UnityEngine.UI.Button>();
            return $"{name}: kept";
        }

        button = build();
        return $"{name}: built";
    }

    // ================================================================== the buttons

    /// <summary>
    /// 居中, 占两个键的位置 / Centred under the title, at the height of the two sign-in buttons and
    /// halfway between them, wider than either.
    /// </summary>
    private static UnityEngine.UI.Button BuildStart(LoginUI ui, Transform panel)
    {
        RectTransform login = (RectTransform)ui.home_login.transform;
        RectTransform register = (RectTransform)ui.home_register.transform;
        Vector2 position = new Vector2(
            (login.anchoredPosition.x + register.anchoredPosition.x) / 2f, login.anchoredPosition.y);

        UnityEngine.UI.Button button = Copy(ui.home_login, panel, StartName, "START", 28, StartSize);
        RectTransform rect = (RectTransform)button.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;

        // 紧跟在注册键后面 / Right after REGISTER, so it draws where the pair draws.
        rect.SetSiblingIndex(register.GetSiblingIndex() + 1);
        return button;
    }

    /// <summary>
    /// 左上角, 黑条下面 / Top-left, where the sign-in form's back arrow sits: below the black top bar,
    /// whose image catches taps, never under it.
    /// </summary>
    private static UnityEngine.UI.Button BuildLogOut(LoginUI ui, Transform panel)
    {
        Vector2 position = ui.login_back != null
            ? ((RectTransform)ui.login_back.transform).anchoredPosition
            : CornerPosition;

        UnityEngine.UI.Button button = Copy(ui.home_login, panel, LogOutName, "LOG OUT", 22, LogOutSize);
        RectTransform rect = (RectTransform)button.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.SetAsLastSibling();
        return button;
    }

    /// <summary>
    /// A copy of <paramref name="source"/> with its own caption and size, no clicks of its own, and
    /// switched off: LoginUI decides on Show which of the title's buttons are up.
    /// </summary>
    private static UnityEngine.UI.Button Copy(UnityEngine.UI.Button source, Transform panel, string name,
                                              string caption, int fontSize, Vector2 size)
    {
        GameObject go = Object.Instantiate(source.gameObject, panel);
        go.name = name;
        ((RectTransform)go.transform).sizeDelta = size;

        UnityEngine.UI.Button button = go.GetComponent<UnityEngine.UI.Button>();
        button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        button.interactable = true;

        Transform captionTransform = go.transform.Find("Text");
        Text text = captionTransform != null
            ? captionTransform.GetComponent<Text>()
            : go.GetComponentInChildren<Text>(true);
        if (text != null)
        {
            text.text = caption;
            text.fontSize = fontSize;
            RectTransform captionRect = (RectTransform)text.transform;
            captionRect.anchoredPosition = Vector2.zero;
            captionRect.sizeDelta = size;
        }

        go.SetActive(false);
        return button;
    }

    /// <summary>
    /// UIBase hides <c>transform</c> behind a field it fills in Awake, which never runs on a prefab
    /// being edited - so the component's own transform, asked for as a Component.
    /// </summary>
    private static Transform Root(LoginUI ui) => ((Component)ui).transform;
}
