using System.Collections.Generic;
using TMPro;
using UI;
using UI.Sub;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 选助理 / Builds the assistant screen - a popup with a grid of square operator portraits - and puts
/// the button that opens it on Home, beside the PROFILE button:
///   - AssistantUI: a dark veil over whatever is open, a panel with the grid, the current one marked,
///   - AssistantButton: inside HomeUI's LeftPanel, right of the PROFILE button, leaning with the panel.
///
/// 找到就不动 / Find-or-create: a prefab or a button that exists is kept. Reset Assistant UI To Default
/// deletes both and builds them again. The profile card opens the screen too, in code.
///
/// The glyphs are drawn from rectangles: the default TMP font has no person, tick or cross to trust.
/// </summary>
public static class AssistantUISetup
{
    private const string UiPrefabDir = "Assets/Arknights/Resources/Prefab/UI";
    private const string HomePrefabPath = UiPrefabDir + "/HomeUI.prefab";

    public const string ButtonName = "AssistantButton";
    private const string LeftPanel = "LeftPanel";

    private static readonly Color PanelFace = new Color(0.12f, 0.125f, 0.14f, 0.98f);
    private static readonly Color TileFace = new Color(0.2f, 0.21f, 0.23f, 1f);
    private static readonly Color Tile = new Color(0.09f, 0.10f, 0.11f, 0.92f);
    private static readonly Color Accent = new Color(0.10f, 0.55f, 0.80f, 1f);
    private static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);
    private static readonly Color Yellow = new Color(0.96f, 0.73f, 0.05f, 1f);

    // ------------------------------------------------------------------ menu items

    [MenuItem("Arknights/Home/Build Assistant UI", false, 34)]
    public static void Build()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        List<string> done = new List<string> { BuildIfMissing(), HookHome() };

        AssetDatabase.SaveAssets();
        Debug.Log("[AssistantUISetup] " + string.Join("\n  ", done) +
                  "\n  Device builds read the UI bundle: rebuild the AssetBundles before testing on a phone.");
    }

    [MenuItem("Arknights/Home/Reset Assistant UI To Default", false, 35)]
    public static void ResetToDefault()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        AssetDatabase.DeleteAsset($"{UiPrefabDir}/{AssistantUI.UIName}.prefab");
        RemoveHomeButton();
        Build();
    }

    private static string BuildIfMissing()
    {
        string path = $"{UiPrefabDir}/{AssistantUI.UIName}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return $"{AssistantUI.UIName}: kept";

        GameObject root = BuildScreen();
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return $"{AssistantUI.UIName}: built at {path}";
    }

    // ================================================================== the screen

    private static GameObject BuildScreen()
    {
        GameObject root = new GameObject(AssistantUI.UIName, typeof(RectTransform), typeof(CanvasGroup), typeof(AssistantUI));
        AssistantUI ui = root.GetComponent<AssistantUI>();
        RectTransform rect = (RectTransform)root.transform;
        Stretch(rect);

        // 点外面关闭 / The veil closes the popup, the way tapping outside a card does elsewhere.
        UnityEngine.UI.Image veil = NewImage("Veil", rect, new Color(0f, 0f, 0f, 0.72f), true);
        Stretch(veil.rectTransform);
        ui.veilButton = veil.gameObject.AddComponent<UnityEngine.UI.Button>();
        ui.veilButton.targetGraphic = veil;
        ui.veilButton.transition = Selectable.Transition.None;

        UnityEngine.UI.Image panel = NewImage("Panel", rect, PanelFace, true);
        Place(panel.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(1240f, 640f));
        RectTransform p = panel.rectTransform;
        UnityEngine.UI.Image strip = NewImage("Strip", p, Accent, false);
        Place(strip.rectTransform, TopLeft, new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 6f));

        TextMeshProUGUI title = Text(p, "Title", new Vector2(48f, -36f), new Vector2(700f, 56f), 44f, Color.white);
        title.text = "ASSISTANT";
        title.fontStyle = FontStyles.Bold;
        Text(p, "Subtitle", new Vector2(48f, -96f), new Vector2(900f, 36f), 24f, Dim).text =
            "Stands on Home and on your profile card.";

        UnityEngine.UI.Image close = NewImage("CloseButton", p, new Color(0.30f, 0.31f, 0.33f, 1f), true);
        close.sprite = Knob();
        Place(close.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), Center, new Vector2(-56f, -56f), new Vector2(64f, 64f));
        Bar(close.rectTransform, "A", Vector2.zero, new Vector2(30f, 6f), 45f, Color.white);
        Bar(close.rectTransform, "B", Vector2.zero, new Vector2(30f, 6f), -45f, Color.white);
        ui.closeButton = close.gameObject.AddComponent<UnityEngine.UI.Button>();
        ui.closeButton.targetGraphic = close;

        RectTransform grid = NewRect("Grid", p);
        grid.anchorMin = new Vector2(0f, 0f);
        grid.anchorMax = new Vector2(1f, 1f);
        grid.offsetMin = new Vector2(48f, 48f);
        grid.offsetMax = new Vector2(-48f, -156f);
        GridLayoutGroup layout = grid.gameObject.AddComponent<GridLayoutGroup>();
        layout.cellSize = new Vector2(200f, 200f);
        layout.spacing = new Vector2(28f, 28f);
        layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
        layout.childAlignment = TextAnchor.UpperLeft;
        ui.grid = grid;

        ui.tileTemplate = BuildTile(grid);
        return root;
    }

    /// <summary>
    /// 一格 / One tile: the portrait filling a square, a yellow frame and tick when it is the one standing,
    /// and a DEFAULT strip on the tile that clears the choice.
    /// </summary>
    private static RectTransform BuildTile(RectTransform grid)
    {
        UnityEngine.UI.Image face = NewImage("TileTemplate", grid, TileFace, true);
        RectTransform tile = face.rectTransform;
        UnityEngine.UI.Button button = face.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = face;

        RectTransform mask = NewRect("Mask", tile);
        Stretch(mask);
        mask.offsetMin = new Vector2(6f, 6f);
        mask.offsetMax = new Vector2(-6f, -6f);
        mask.gameObject.AddComponent<RectMask2D>();

        UnityEngine.UI.Image portrait = NewImage("Portrait", mask, Color.white, false);
        portrait.preserveAspect = true;
        Stretch(portrait.rectTransform);

        RectTransform selected = NewRect("Selected", tile);
        Stretch(selected);
        Edge(selected, "Top", TopLeft, new Vector2(1f, 1f), new Vector2(0f, -3f), new Vector2(0f, 6f));
        Edge(selected, "Bottom", Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, 3f), new Vector2(0f, 6f));
        Edge(selected, "Left", Vector2.zero, TopLeft, new Vector2(3f, 0f), new Vector2(6f, 0f));
        Edge(selected, "Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-3f, 0f), new Vector2(6f, 0f));
        UnityEngine.UI.Image badge = NewImage("Badge", selected, Yellow, false);
        badge.sprite = Knob();
        Place(badge.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), Center, new Vector2(-18f, -18f), new Vector2(44f, 44f));
        Bar(badge.rectTransform, "Short", new Vector2(-6f, -3f), new Vector2(12f, 5f), -45f, new Color(0.14f, 0.14f, 0.16f, 1f));
        Bar(badge.rectTransform, "Long", new Vector2(4f, 1f), new Vector2(22f, 5f), 45f, new Color(0.14f, 0.14f, 0.16f, 1f));

        UnityEngine.UI.Image tag = NewImage("DefaultTag", tile, new Color(0f, 0f, 0f, 0.7f), false);
        Place(tag.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(-12f, 36f));
        TextMeshProUGUI label = NewText("Label", tag.rectTransform, 22f, Color.white, TextAlignmentOptions.Center);
        Stretch(label.rectTransform);
        label.text = "DEFAULT";
        label.fontStyle = FontStyles.Bold;

        return tile;
    }

    // ================================================================== Home

    /// <summary>
    /// PROFILE 旁边 / Right of the PROFILE button inside LeftPanel - or right of the news panel, should
    /// there be no PROFILE button. One that exists anywhere on Home is left where it is.
    /// </summary>
    private static string HookHome()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(HomePrefabPath) == null) return "HomeUI: no prefab, no button";

        GameObject contents = PrefabUtility.LoadPrefabContents(HomePrefabPath);
        try
        {
            if (FindDeep(contents.transform, ButtonName) != null) return $"HomeUI {ButtonName}: kept";

            RectTransform beside = (FindDeep(contents.transform, ProfileUISetup.ButtonName)
                                    ?? FindDeep(contents.transform, "NewsPanel")) as RectTransform;
            if (beside == null) return $"HomeUI: nothing to sit beside, no button";

            RectTransform parent = (RectTransform)beside.parent;
            RectTransform button = BuildHomeButton(parent);
            button.anchoredPosition = beside.anchoredPosition +
                                      new Vector2(beside.sizeDelta.x * 0.5f + 14f + button.sizeDelta.x * 0.5f, 0f);
            button.SetSiblingIndex(beside.GetSiblingIndex() + 1);

            PrefabUtility.SaveAsPrefabAsset(contents, HomePrefabPath);
            return $"HomeUI {ButtonName}: placed in {parent.name} beside {beside.name} -> {AssistantUI.UIName}";
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static RectTransform BuildHomeButton(RectTransform parent)
    {
        UnityEngine.UI.Image face = NewImage(ButtonName, parent, Tile, true);
        RectTransform rect = face.rectTransform;
        Place(rect, Center, Center, Center, Vector2.zero, new Vector2(150f, 164f));

        UnityEngine.UI.Image strip = NewImage("Strip", rect, Accent, false);
        Place(strip.rectTransform, TopLeft, new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 6f));

        // 半身像 / A head over shoulders.
        UnityEngine.UI.Image head = NewImage("Head", rect, Color.white, false);
        head.sprite = Knob();
        Place(head.rectTransform, Center, Center, Center, new Vector2(0f, 38f), new Vector2(34f, 34f));
        UnityEngine.UI.Image shoulders = NewImage("Shoulders", rect, Color.white, false);
        shoulders.sprite = Knob();
        shoulders.type = UnityEngine.UI.Image.Type.Filled;
        shoulders.fillMethod = UnityEngine.UI.Image.FillMethod.Vertical;
        shoulders.fillOrigin = (int)UnityEngine.UI.Image.OriginVertical.Top;
        shoulders.fillAmount = 0.5f;
        Place(shoulders.rectTransform, Center, Center, Center, new Vector2(0f, -4f), new Vector2(66f, 52f));

        TextMeshProUGUI label = NewText("Label", rect, 20f, Color.white, TextAlignmentOptions.Center);
        Place(label.rectTransform, Center, Center, Center, new Vector2(0f, -50f), new Vector2(150f, 30f));
        label.text = "ASSISTANT";
        label.fontStyle = FontStyles.Bold;

        UnityEngine.UI.Button button = face.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = face;
        face.gameObject.AddComponent<OpenUIButton>().uiName = AssistantUI.UIName;

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

    private static void Edge(RectTransform parent, string name, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
    {
        UnityEngine.UI.Image edge = NewImage(name, parent, Yellow, false);
        edge.rectTransform.anchorMin = min;
        edge.rectTransform.anchorMax = max;
        edge.rectTransform.pivot = Center;
        edge.rectTransform.anchoredPosition = position;
        edge.rectTransform.sizeDelta = size;
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
