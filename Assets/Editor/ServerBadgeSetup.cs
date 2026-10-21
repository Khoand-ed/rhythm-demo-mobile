using UI.Sub;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 服务器标签 / Builds the small label on the login screen that names the server a build talks to -
/// "STAGING · v1.1" - in amber, at the right of the black bar along the top of LoginUI.prefab. LoginUI
/// fills it from ServerTarget and hides it in a production build.
///
/// 找到就不动 / Find-or-create: a ServerBadge already in the prefab is kept, and only its link to
/// LoginUI is filled in. Reset Server Badge To Default deletes it and builds it again.
/// </summary>
public static class ServerBadgeSetup
{
    private const string LoginPrefabPath = "Assets/Arknights/Resources/Prefab/UI/LoginUI.prefab";

    public const string BadgeName = "ServerBadge";
    private const string BarName = "TopBar";

    private static readonly Color Amber = new Color(0.96f, 0.76f, 0.23f, 1f);

    // ------------------------------------------------------------------ menu items

    [MenuItem("Arknights/Login/Build Server Badge", false, 42)]
    public static void Build()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        GameObject root = PrefabUtility.LoadPrefabContents(LoginPrefabPath);
        try
        {
            LoginUI ui = root.GetComponent<LoginUI>();
            if (ui == null)
            {
                Debug.LogError($"[ServerBadgeSetup] {LoginPrefabPath} has no LoginUI.");
                return;
            }

            string done = BuildInto(ui);
            PrefabUtility.SaveAsPrefabAsset(root, LoginPrefabPath);
            Debug.Log("[ServerBadgeSetup] " + done +
                      "\n  Device builds read the UI bundle: rebuild the AssetBundles before testing on a phone.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [MenuItem("Arknights/Login/Reset Server Badge To Default", false, 43)]
    public static void ResetToDefault()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        GameObject root = PrefabUtility.LoadPrefabContents(LoginPrefabPath);
        try
        {
            Transform bar = root.transform.Find(BarName);
            Transform badge = bar != null ? bar.Find(BadgeName) : null;
            if (badge != null) Object.DestroyImmediate(badge.gameObject);
            PrefabUtility.SaveAsPrefabAsset(root, LoginPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        Build();
    }

    /// <summary>
    /// Finds or builds the badge in LoginUI's top bar and links it. Also called by Generate Bootstrap
    /// Assets, which builds the whole LoginUI from nothing.
    /// </summary>
    public static string BuildInto(LoginUI ui)
    {
        // UIBase hides transform behind a field it fills in Awake, which never runs on a prefab being
        // edited - so the component's own transform, asked for as a Component.
        Transform root = ((Component)ui).transform;
        Transform bar = root.Find(BarName);
        if (bar == null) return $"{BadgeName}: not built - LoginUI has no {BarName}";

        Transform badge = bar.Find(BadgeName);
        string what = $"{BadgeName}: kept";

        if (badge == null)
        {
            badge = BuildBadge((RectTransform)bar, ui.login_userName.textComponent.font).transform;
            what = $"{BadgeName}: built";
        }

        ui.serverBadge = badge.GetComponent<Text>();
        return what + ", linked to LoginUI.serverBadge";
    }

    private static Text BuildBadge(RectTransform bar, Font font)
    {
        GameObject go = new GameObject(BadgeName, typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(bar, false);

        // Right edge of the bar, vertically centred, clear of anything the bar may gain on the left.
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-40f, 0f);
        rect.sizeDelta = new Vector2(420f, 40f);

        Text text = go.AddComponent<Text>();
        text.font = font;
        text.fontSize = 22;
        text.color = Amber;
        text.alignment = TextAnchor.MiddleRight;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.text = "LOCAL · v0.0";
        return text;
    }
}
