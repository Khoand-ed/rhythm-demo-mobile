using System.Collections.Generic;
using System.IO;
using Data.Item;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 仓库界面 / Builds the depot screen and the item detail popup.
//
// DepotUI.cs, ItemInfoUI.cs and ItemIconComponent.cs were already in the project; only their
// prefabs were missing, which is why the Depot tile went nowhere. Everything here exists to
// satisfy the fields those three scripts already expect - the names below are theirs, not a
// design choice, and renaming a node breaks the binding.
//
// 用旧版 Text 而不是 TMP / Legacy Text throughout, deliberately: ItemInfoUI and ItemIconComponent
// declare UnityEngine.UI.Text fields, so a TextMeshProUGUI would simply not bind. Convert both
// together later with Tools/Convert All Legacy Text To TMP if the project moves over.
public static class DepotUISetup
{
    private const string UiPrefabDir = "Assets/Arknights/Resources/Prefab/UI";
    private const string ItemMetaDir = "Assets/Arknights/Resources/Meta/Item";
    private const string ItemIconDir = "Assets/Arknights/Resources/Sprite/Item/Icon";
    private const string MaterialDir = "Assets/Arknights/Shaders";
    private const string BlurShader = "Arknights/UI/Blur";
    private const string DepotTileName = "DepotTile";

    // Lifted from the back button CharUI, ShopUI and CharInfoUI share.
    private static readonly Color Slab = new Color(0.30f, 0.31f, 0.33f, 0.90f);
    private static readonly Color Edge = new Color(1f, 1f, 1f, 0.30f);

    // The depot in the reference screens is the front-end's light theme, like HomeUI.
    private static readonly Color Paper = new Color(0.93f, 0.94f, 0.95f, 1f);
    private static readonly Color PaperDim = new Color(0.86f, 0.87f, 0.89f, 1f);
    private static readonly Color Ink = new Color(0.10f, 0.11f, 0.12f, 1f);
    private static readonly Color InkDim = new Color(0.42f, 0.44f, 0.47f, 1f);
    private static readonly Color TabOn = new Color(0.32f, 0.33f, 0.35f, 1f);
    private static readonly Color TabOff = new Color(0.78f, 0.79f, 0.81f, 1f);

    private static Font font;

    // ------------------------------------------------------------------ data

    private readonly struct SeedItem
    {
        public readonly int Id;
        public readonly string Name;
        public readonly int Rarity;
        public readonly ItemType Type;
        public readonly string UseInfo;
        public readonly string Description;
        public readonly string WaysObtain;
        public readonly Color Tint;

        public SeedItem(int id, string name, int rarity, ItemType type,
                        string useInfo, string description, string waysObtain, Color tint)
        {
            Id = id; Name = name; Rarity = rarity; Type = type;
            UseInfo = useInfo; Description = description; WaysObtain = waysObtain; Tint = tint;
        }
    }

    // 存档里有这些ID但没有元数据 / Ids the seeded save already holds with no ItemMeta behind them.
    // 0/1/2 are the three currencies HomeUI reads for its top row, so 0 has to exist; 6 and 7 are
    // the first Upgrade Material entries in the project, which is what gives that tab any content.
    private static readonly SeedItem[] Missing =
    {
        new SeedItem(0, "Originite Prime", 5, ItemType.JI_CHU,
            "Refined Originite. The most valuable resource Rhodes Island keeps on hand.",
            "Refined Originite. The most valuable resource Rhodes Island keeps on hand.",
            "Purchase, Mission Rewards",
            new Color(0.36f, 0.78f, 0.92f)),

        new SeedItem(6, "Orirock", 3, ItemType.YANG_CHENG,
            "Basic upgrade material, used across almost every promotion.",
            "Raw Originite ore, ground down until it is safe to handle. Plentiful, and spent " +
            "as fast as it comes in.",
            "Stage Drops",
            new Color(0.62f, 0.66f, 0.70f)),

        new SeedItem(7, "Sugar Substitute", 3, ItemType.YANG_CHENG,
            "Basic upgrade material for operator promotion.",
            "An industrial sweetener nobody enjoys eating. Turns out to be an excellent binding " +
            "agent, which is the only reason anyone stocks it.",
            "Stage Drops",
            new Color(0.88f, 0.76f, 0.40f)),

        new SeedItem(8, "Purchase Certificate", 4, ItemType.JI_CHU,
            "A certificate of Rhodes Island's commercial operations, redeemable for supplies.",
            "Keeping Rhodes Island running takes enormous funding and a tangle of raw materials, " +
            "so trading through every channel available is simply necessary.",
            "Mission Rewards, Stage Drops",
            new Color(0.85f, 0.32f, 0.30f)),
    };

    [MenuItem("Arknights/Depot/Create Missing Item Metas", false, 0)]
    public static void CreateMissingItemMetas()
    {
        if (!GuardEditMode()) return;

        Directory.CreateDirectory(ItemMetaDir);
        Directory.CreateDirectory(ItemIconDir);

        List<string> made = new List<string>();

        foreach (SeedItem seed in Missing)
        {
            string path = $"{ItemMetaDir}/{seed.Id}.asset";
            if (AssetDatabase.LoadAssetAtPath<ItemMeta>(path) != null) continue;

            ItemMeta meta = ScriptableObject.CreateInstance<ItemMeta>();
            SerializedObject so = new SerializedObject(meta);
            so.FindProperty("id").intValue = seed.Id;
            so.FindProperty("name").stringValue = seed.Name;
            so.FindProperty("rarity").intValue = seed.Rarity;
            so.FindProperty("type").enumValueIndex = (int)seed.Type;
            so.FindProperty("useInfo").stringValue = seed.UseInfo;
            so.FindProperty("description").stringValue = seed.Description;
            so.FindProperty("waysObtain").stringValue = seed.WaysObtain;
            so.FindProperty("icon").objectReferenceValue = MakeIcon(seed);
            so.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(meta, path);
            made.Add($"{seed.Id} ({seed.Name})");
        }

        AssetDatabase.SaveAssets();

        Debug.Log(made.Count == 0
            ? "Every item the save holds already has an ItemMeta."
            : $"Created {made.Count} ItemMeta: {string.Join(", ", made)}\n" +
              $"  Icons generated into {ItemIconDir}. Replace them with real art whenever you have it.");
    }

    /// <summary>
    /// 生成占位图标 / A placeholder icon, in the same spirit as Arknights/Placeholders: a tilted
    /// rounded square over a soft disc, tinted per item so the depot grid reads as distinct
    /// entries rather than six copies of one sprite.
    /// </summary>
    private static Sprite MakeIcon(SeedItem seed)
    {
        const int Size = 96;
        Color[] pixels = new Color[Size * Size];

        float centre = (Size - 1) * 0.5f;
        Color deep = seed.Tint * 0.55f;
        deep.a = 1f;

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float dx = (x - centre) / centre;
                float dy = (y - centre) / centre;

                Color pixel = new Color(0f, 0f, 0f, 0f);

                // Soft disc behind, so the icon reads on any rarity background.
                float disc = Mathf.Sqrt(dx * dx + dy * dy);
                if (disc < 0.92f) pixel = Color.Lerp(deep, seed.Tint, 1f - disc);

                // A diamond core, rotated 45 degrees from the cell.
                float diamond = Mathf.Abs(dx) + Mathf.Abs(dy);
                if (diamond < 0.62f) pixel = Color.Lerp(Color.white, seed.Tint, diamond / 0.62f);
                if (diamond < 0.26f) pixel = Color.white;

                pixels[y * Size + x] = pixel;
            }
        }

        return ImportSprite($"{ItemIconDir}/item_{seed.Id}.png", pixels, Size);
    }

    private static Sprite ImportSprite(string assetPath, Color[] pixels, int size)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.SetPixels(pixels);
        texture.Apply();

        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath),
            texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
    }

    // --------------------------------------------------------------- prefabs

    [MenuItem("Arknights/Depot/Build Depot UI", false, 1)]
    public static void BuildDepotUI()
    {
        if (!GuardEditMode()) return;

        CreateMissingItemMetas();

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) Debug.LogWarning("No builtin font found; Text components will render blank.");

        BuildDepotScreen();
        BuildItemInfoUI();
        bool hooked = HookDepotTile();

        AssetDatabase.SaveAssets();

        Debug.Log("Depot UI built.\n" +
                  $"  {UiPrefabDir}/DepotUI.prefab - grid, three tabs, scrolls vertically\n" +
                  $"  {UiPrefabDir}/ItemInfoUI.prefab - detail popup, blurred backdrop, scrolling body\n" +
                  $"  HomeUI {DepotTileName}: {(hooked ? "opens DepotUI" : "NOT hooked, see warning above")}");
    }

    private static void BuildDepotScreen()
    {
        GameObject root = new GameObject("DepotUI",
            typeof(RectTransform), typeof(CanvasGroup), typeof(UI.Sub.DepotUI));
        Stretch((RectTransform)root.transform);

        UnityEngine.UI.Image backdrop = NewImage("Backdrop", (RectTransform)root.transform, Paper);
        Stretch((RectTransform)backdrop.transform);
        backdrop.raycastTarget = true;

        BuildDepotTopBar((RectTransform)root.transform, out Toggle all,
                         out Toggle basic, out Toggle material);

        RectTransform template = BuildGrid((RectTransform)root.transform);

        UI.Sub.DepotUI house = root.GetComponent<UI.Sub.DepotUI>();
        house.ALL = all;
        house.JI_CHU = basic;
        house.YANG_CHENG = material;
        house.prefab = template.GetComponent<ItemIconComponent>();

        Save(root, "DepotUI");
    }

    private static void BuildDepotTopBar(RectTransform parent, out Toggle all,
                                         out Toggle basic, out Toggle material)
    {
        RectTransform bar = NewRect("TopBar", parent);
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(0f, 120f);
        bar.anchoredPosition = Vector2.zero;

        UnityEngine.UI.Image back = NewImage("BackButton", bar, Slab);
        RectTransform backRect = (RectTransform)back.transform;
        backRect.anchorMin = new Vector2(0f, 1f);
        backRect.anchorMax = new Vector2(0f, 1f);
        backRect.pivot = new Vector2(0f, 1f);
        backRect.anchoredPosition = new Vector2(48f, -26f);
        backRect.sizeDelta = new Vector2(170f, 68f);
        AddEdges(backRect);

        Button backButton = back.gameObject.AddComponent<Button>();
        backButton.targetGraphic = back;
        UnityEditor.Events.UnityEventTools.AddBoolPersistentListener(
            backButton.onClick, parent.GetComponent<UI.Sub.DepotUI>().Hide, true);

        Text backLabel = NewText("Text", backRect, 46, Color.white, TextAnchor.MiddleCenter);
        Stretch((RectTransform)backLabel.transform);
        backLabel.text = "‹";

        // 三个页签互斥 / One group so the three tabs behave as radio buttons, which is what
        // DepotUI assumes when it reads ALL.isOn and falls through to JI_CHU.isOn.
        RectTransform tabs = NewRect("Tabs", bar);
        tabs.anchorMin = new Vector2(1f, 1f);
        tabs.anchorMax = new Vector2(1f, 1f);
        tabs.pivot = new Vector2(1f, 1f);
        tabs.anchoredPosition = new Vector2(-48f, -26f);
        tabs.sizeDelta = new Vector2(690f, 68f);

        HorizontalLayoutGroup layout = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        ToggleGroup group = tabs.gameObject.AddComponent<ToggleGroup>();

        all = NewTab("ALL", tabs, group, "ALL", true);
        basic = NewTab("JI_CHU", tabs, group, Expand.GetName(ItemType.JI_CHU), false);
        material = NewTab("YANG_CHENG", tabs, group, Expand.GetName(ItemType.YANG_CHENG), false);
    }

    private static Toggle NewTab(string name, RectTransform parent, ToggleGroup group,
                                 string label, bool on)
    {
        UnityEngine.UI.Image face = NewImage(name, parent, on ? TabOn : TabOff);

        Toggle toggle = face.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = face;
        toggle.group = group;
        toggle.isOn = on;

        // The tab itself is the swatch, so its own Image is what changes colour.
        toggle.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = toggle.colors;
        colors.normalColor = Color.white;
        toggle.colors = colors;

        Text text = NewText("Label", (RectTransform)face.transform, 26,
                            on ? Color.white : Ink, TextAnchor.MiddleCenter);
        Stretch((RectTransform)text.transform);
        text.text = label;

        // DepotUI.Init recolours this Text on toggle, and finds it with
        // GetComponentInChildren<Text>() - so it has to be the only Text under the tab.
        return toggle;
    }

    private static RectTransform BuildGrid(RectTransform parent)
    {
        RectTransform scrollRoot = NewRect("Grid", parent);
        scrollRoot.anchorMin = Vector2.zero;
        scrollRoot.anchorMax = Vector2.one;
        scrollRoot.offsetMin = new Vector2(48f, 32f);
        scrollRoot.offsetMax = new Vector2(-48f, -120f);

        RectTransform viewport = NewViewport(scrollRoot);

        RectTransform content = NewRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0f, 0f);

        // 八列铺满宽度 / Eight columns across, as in the reference. The grid area is about 2064
        // canvas units wide, so 8 * 220 + 7 * 28 + padding lands just inside it.
        GridLayoutGroup grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(220f, 220f);
        grid.spacing = new Vector2(28f, 28f);
        grid.padding = new RectOffset(8, 8, 8, 8);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 8;

        // Grid sizes the cells; the fitter sizes Content to however many rows result.
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

        return BuildItemIconTemplate(content);
    }

    /// <summary>
    /// One depot cell. DepotUI clones this into its own parent, so it has to stay INACTIVE:
    /// the clones inherit that and DepotUI switches on only as many as the save needs. An
    /// active template would sit in the grid forever as a seventh, empty item.
    /// </summary>
    private static RectTransform BuildItemIconTemplate(RectTransform parent)
    {
        UnityEngine.UI.Image ground = NewImage("ItemIconTemplate", parent, Color.white);
        RectTransform rect = (RectTransform)ground.transform;
        rect.sizeDelta = new Vector2(150f, 150f);

        Button button = ground.gameObject.AddComponent<Button>();
        button.targetGraphic = ground;

        UnityEngine.UI.Image icon = NewImage("Icon", rect, Color.white);
        RectTransform iconRect = (RectTransform)icon.transform;
        Stretch(iconRect);
        iconRect.offsetMin = new Vector2(22f, 22f);
        iconRect.offsetMax = new Vector2(-22f, -22f);
        icon.raycastTarget = false;
        icon.preserveAspect = true;

        Text amount = NewText("Amount", rect, 30, Color.white, TextAnchor.LowerRight);
        RectTransform amountRect = (RectTransform)amount.transform;
        amountRect.anchorMin = Vector2.zero;
        amountRect.anchorMax = new Vector2(1f, 0f);
        amountRect.pivot = new Vector2(0.5f, 0f);
        amountRect.sizeDelta = new Vector2(-12f, 40f);
        amountRect.anchoredPosition = new Vector2(0f, 6f);
        amount.fontStyle = FontStyle.Bold;

        ItemIconComponent component = ground.gameObject.AddComponent<ItemIconComponent>();
        component.ground = ground;
        component.icon = icon;
        component.amount = amount;

        ground.gameObject.SetActive(false);
        return rect;
    }

    // ------------------------------------------------------------- item info

    private static void BuildItemInfoUI()
    {
        GameObject root = new GameObject("ItemInfoUI",
            typeof(RectTransform), typeof(CanvasGroup), typeof(UI.Sub.ItemInfoUI));
        RectTransform rootRect = (RectTransform)root.transform;
        Stretch(rootRect);

        UI.Sub.ItemInfoUI info = root.GetComponent<UI.Sub.ItemInfoUI>();

        // 点背景关闭 / The blurred backdrop doubles as the dismiss button, the way tapping
        // outside the card closes it in the reference screens.
        // 背景压暗一点 / Tinted down, not just blurred. The depot behind is nearly the same
        // shade as the card, so blur alone leaves the two indistinguishable; the reference
        // darkens the backdrop as well, and that is what separates them.
        UnityEngine.UI.Image blur = NewImage("Blur", rootRect, new Color(0.70f, 0.71f, 0.73f, 1f));
        Stretch((RectTransform)blur.transform);
        blur.material = BlurMaterial();
        blur.gameObject.AddComponent<UI.UiBlurCapture>();

        Button dismiss = blur.gameObject.AddComponent<Button>();
        dismiss.targetGraphic = blur;
        UnityEditor.Events.UnityEventTools.AddBoolPersistentListener(dismiss.onClick, info.Hide, true);

        BuildInfoPanel(rootRect, info);
        info.blur = blur;

        Save(root, "ItemInfoUI");
    }

    private static RectTransform BuildInfoPanel(RectTransform parent, UI.Sub.ItemInfoUI info)
    {
        UnityEngine.UI.Image panelImage = NewImage("Panel", parent, Paper);
        RectTransform panel = (RectTransform)panelImage.transform;
        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(1180f, 620f);
        panel.anchoredPosition = Vector2.zero;

        // Name plate, top left. ItemInfoUI force-rebuilds this parent, so it fits its text.
        UnityEngine.UI.Image nameGround = NewImage("NameGround", panel, TabOn);
        RectTransform nameRect = (RectTransform)nameGround.transform;
        nameRect.anchorMin = new Vector2(0f, 1f);
        nameRect.anchorMax = new Vector2(0f, 1f);
        nameRect.pivot = new Vector2(0f, 1f);
        nameRect.anchoredPosition = new Vector2(36f, -32f);
        nameRect.sizeDelta = new Vector2(280f, 56f);

        HorizontalLayoutGroup namePad = nameRect.gameObject.AddComponent<HorizontalLayoutGroup>();
        namePad.padding = new RectOffset(20, 20, 6, 6);
        namePad.childControlWidth = true;
        namePad.childControlHeight = true;
        namePad.childForceExpandWidth = true;
        namePad.childForceExpandHeight = true;

        ContentSizeFitter nameFitter = nameRect.gameObject.AddComponent<ContentSizeFitter>();
        nameFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        nameFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        info.itemName = NewText("ItemName", nameRect, 32, Color.white, TextAnchor.MiddleLeft);
        info.itemName.fontStyle = FontStyle.Bold;

        // Stock readout, top right.
        UnityEngine.UI.Image stock = NewImage("StockGround", panel, PaperDim);
        RectTransform stockRect = (RectTransform)stock.transform;
        stockRect.anchorMin = new Vector2(1f, 1f);
        stockRect.anchorMax = new Vector2(1f, 1f);
        stockRect.pivot = new Vector2(1f, 1f);
        stockRect.anchoredPosition = new Vector2(-36f, -32f);
        stockRect.sizeDelta = new Vector2(320f, 56f);

        UnityEngine.UI.Image caption = NewImage("Caption", stockRect, TabOn);
        RectTransform captionRect = (RectTransform)caption.transform;
        captionRect.anchorMin = new Vector2(0f, 0f);
        captionRect.anchorMax = new Vector2(0f, 1f);
        captionRect.pivot = new Vector2(0f, 0.5f);
        captionRect.sizeDelta = new Vector2(120f, 0f);
        captionRect.anchoredPosition = Vector2.zero;

        Text captionText = NewText("Text", captionRect, 24, Color.white, TextAnchor.MiddleCenter);
        Stretch((RectTransform)captionText.transform);
        captionText.text = "STOCK";

        info.amount = NewText("Amount", stockRect, 32, Ink, TextAnchor.MiddleRight);
        RectTransform amountRect = (RectTransform)info.amount.transform;
        Stretch(amountRect);
        amountRect.offsetMin = new Vector2(130f, 0f);
        amountRect.offsetMax = new Vector2(-18f, 0f);
        info.amount.fontStyle = FontStyle.Bold;

        // Icon, bottom left, over the card the way the reference lays it out.
        info.icon = NewImage("Icon", panel, Color.white);
        RectTransform iconRect = (RectTransform)info.icon.transform;
        iconRect.anchorMin = new Vector2(0f, 0f);
        iconRect.anchorMax = new Vector2(0f, 0f);
        iconRect.pivot = new Vector2(0f, 0f);
        iconRect.anchoredPosition = new Vector2(48f, 48f);
        iconRect.sizeDelta = new Vector2(300f, 300f);
        info.icon.preserveAspect = true;
        info.icon.raycastTarget = false;

        BuildInfoBody(panel, info);
        return panel;
    }

    /// <summary>
    /// 信息区可竖向滚动 / The body scrolls vertically, so a long description or a long list of
    /// sources runs past the card instead of overflowing it.
    /// </summary>
    private static void BuildInfoBody(RectTransform panel, UI.Sub.ItemInfoUI info)
    {
        RectTransform scrollRoot = NewRect("Body", panel);
        scrollRoot.anchorMin = new Vector2(0f, 0f);
        scrollRoot.anchorMax = new Vector2(1f, 1f);
        scrollRoot.offsetMin = new Vector2(372f, 40f);
        scrollRoot.offsetMax = new Vector2(-36f, -108f);

        RectTransform viewport = NewViewport(scrollRoot);

        RectTransform content = NewRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;

        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 14f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // useInfo sits in its own row because ItemInfoUI force-rebuilds its parent.
        RectTransform useRow = NewRect("UseInfoGround", content);
        VerticalLayoutGroup usePad = useRow.gameObject.AddComponent<VerticalLayoutGroup>();
        usePad.childControlWidth = true;
        usePad.childControlHeight = true;
        usePad.childForceExpandWidth = true;
        usePad.childForceExpandHeight = false;

        ContentSizeFitter useFitter = useRow.gameObject.AddComponent<ContentSizeFitter>();
        useFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        useFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        info.useInfo = NewText("UseInfo", useRow, 26, Ink, TextAnchor.UpperLeft);
        info.useInfo.fontStyle = FontStyle.Bold;

        info.description = NewText("Description", content, 24, InkDim, TextAnchor.UpperLeft);
        info.description.fontStyle = FontStyle.Italic;

        Text obtained = NewText("ObtainCaption", content, 22, new Color(0.62f, 0.64f, 0.67f),
                                TextAnchor.UpperLeft);
        obtained.text = "OBTAINED FROM";

        info.waysObtain = NewText("WaysObtain", content, 24, Ink, TextAnchor.UpperLeft);

        ScrollRect scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 40f;
    }

    private static Material BlurMaterial()
    {
        string path = MaterialDir + "/UIBlur.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find(BlurShader);
        if (shader == null)
        {
            Debug.LogWarning($"Shader '{BlurShader}' not found; the popup backdrop will not blur.");
            return null;
        }

        Directory.CreateDirectory(MaterialDir);
        Material material = new Material(shader);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    // ---------------------------------------------------------------- wiring

    private static bool HookDepotTile()
    {
        string homePath = UiPrefabDir + "/HomeUI.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(homePath) == null)
        {
            Debug.LogWarning($"No HomeUI prefab at {homePath}, so nothing opens the depot yet.");
            return false;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(homePath);

        try
        {
            Transform tile = FindDeep(contents.transform, DepotTileName);
            if (tile == null)
            {
                Debug.LogWarning($"HomeUI has no '{DepotTileName}' child.");
                return false;
            }

            if (tile.GetComponent<Graphic>() == null)
            {
                NewImage("Hit", (RectTransform)tile, new Color(0f, 0f, 0f, 0f));
            }

            Button button = tile.GetComponent<Button>() ?? tile.gameObject.AddComponent<Button>();
            button.targetGraphic = tile.GetComponent<Graphic>();

            OpenUIButton open = tile.GetComponent<OpenUIButton>() ?? tile.gameObject.AddComponent<OpenUIButton>();
            open.uiName = "DepotUI";

            PrefabUtility.SaveAsPrefabAsset(contents, homePath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // --------------------------------------------------------------- helpers

    private static bool GuardEditMode()
    {
        if (!Application.isPlaying) return true;
        Debug.LogError("Exit Play Mode first.");
        return false;
    }

    private static void Save(GameObject root, string name)
    {
        Directory.CreateDirectory(UiPrefabDir);
        PrefabUtility.SaveAsPrefabAsset(root, $"{UiPrefabDir}/{name}.prefab");
        Object.DestroyImmediate(root);
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
    /// 遮罩图必须不透明 / A ScrollRect viewport: an Image that is never drawn, only used as the
    /// mask shape.
    ///
    /// 透明度必须是1 / The alpha has to be opaque even though nothing renders. Mask writes the
    /// stencil from the graphic through an alpha clip, so a nearly-transparent image writes
    /// nothing and clips away every child - an empty ScrollRect with no error to explain it.
    /// showMaskGraphic keeps it invisible; the alpha is purely what the stencil reads.
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
