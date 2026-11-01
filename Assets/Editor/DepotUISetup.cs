using System.Collections.Generic;
using System.IO;
using Data.Item;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 仓库界面 / Builds the depot screen and the item detail popup.
//
//   Arknights/Depot/Build Depot UI            - builds what is missing, keeps what exists
//   Arknights/Depot/Reset Depot UI To Default - builds both prefabs again from scratch
//
// DepotUI.cs, ItemInfoUI.cs and ItemIconComponent.cs were already in the project; only their
// prefabs were missing, which is why the Depot tile went nowhere. Everything here exists to
// satisfy the fields those three scripts already expect - the names below are theirs, not a
// design choice, and renaming a node breaks the binding.
//
// 不动已有的 / Build never rebuilds a prefab that exists. It opens it, adds only the parts it lacks,
// points a screen field at its node only where the field points at nothing, and saves it only when
// it added something - so a run on an up-to-date project writes no file at all. It used to build
// both prefabs afresh on every run and save them over the old ones. Saving over a prefab pairs old
// and new objects by name, and the three tab Labels share one, so they came back under new fileIDs
// each time: a diff with nothing in it. Only Reset rebuilds, and asks first; it writes over each
// prefab in place, so the assets keep their GUIDs.
//
// 用旧版 Text 而不是 TMP / Legacy Text throughout, deliberately: ItemInfoUI and ItemIconComponent
// declare UnityEngine.UI.Text fields, so a TextMeshProUGUI would simply not bind. Convert both
// together later with Tools/Convert All Legacy Text To TMP if the project moves over.
public static class DepotUISetup
{
    private const string Tag = "[DepotUISetup]";
    private const string UiPrefabDir = "Assets/Arknights/Resources/Prefab/UI";
    private const string DepotName = "DepotUI";
    private const string ItemInfoName = "ItemInfoUI";
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

        // 作战记录 / The battle records past the Basic one (11), each worth the EXP that
        // Promuse.Contracts.Characters.OperatorLevels.Records gives it. Dropped by the campaign.
        new SeedItem(14, "Frontline Battle Record", 3, ItemType.JI_CHU,
            "Grants 400 operator EXP when used to level an operator up.",
            "A record of frontline engagements. Reading it closely is worth a few hours in the field.",
            "Stage Drops",
            new Color(0.42f, 0.78f, 0.45f)),

        new SeedItem(15, "Tactical Battle Record", 4, ItemType.JI_CHU,
            "Grants 1000 operator EXP when used to level an operator up.",
            "Tactical analysis drawn from many operations. Dense, and worth every page.",
            "Stage Drops",
            new Color(0.30f, 0.58f, 0.92f)),

        new SeedItem(16, "Strategic Battle Record", 5, ItemType.JI_CHU,
            "Grants 2000 operator EXP when used to level an operator up.",
            "A strategic record kept by Rhodes Island's command. Rarely seen outside it.",
            "Stage Drops",
            new Color(0.95f, 0.78f, 0.22f)),

        // 活动代币 / The seeded event's currency (SeedEvents.EventTokenItemId on the server).
        new SeedItem(20, "Event Token", 4, ItemType.JI_CHU,
            "Spent in the event shop while the event runs.",
            "A festival token. Worth a great deal at the festival and nothing at all after it.",
            "Event Stages, Event Rewards",
            new Color(0.95f, 0.62f, 0.25f)),

        // 制造站蓝图 / Manufacture's key items (Promuse.Contracts.Items.KeyItems): held, never spent,
        // each paid once by a story stage's first clear (ManufactureRules.Blueprints on the server).
        new SeedItem(30, "Manufacture Blueprint I", 3, ItemType.JI_CHU,
            "Unlocks Manufacture on Home. Kept, not spent.",
            "Plans for a small production line, signed off by the logistics office. Whoever holds " +
            "them has a workshop waiting.",
            "First clear of 1-1",
            new Color(0.40f, 0.72f, 0.86f)),

        new SeedItem(31, "Manufacture Blueprint II", 4, ItemType.JI_CHU,
            "Lets a Manufacture station reach Lv2. Kept, not spent.",
            "Revised plans with a second shift pencilled in. The margins are full of someone's " +
            "arguments with the supply budget.",
            "First clear of 1-3",
            new Color(0.56f, 0.52f, 0.90f)),

        new SeedItem(32, "Manufacture Blueprint III", 5, ItemType.JI_CHU,
            "Lets a Manufacture station reach Lv3. Kept, not spent.",
            "The full layout, stamped and approved. Every station on it runs without stopping, " +
            "on paper at least.",
            "First clear of 2-3",
            new Color(0.95f, 0.70f, 0.30f)),
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
            ? $"{Tag} Every item the save holds already has an ItemMeta."
            : $"{Tag} Created {made.Count} ItemMeta: {string.Join(", ", made)}\n" +
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
        LoadFont();

        List<string> done = new List<string>
        {
            BuildOrComplete(DepotName, NewDepotRoot, CompleteDepot, "grid, three tabs, scrolls vertically"),
            BuildOrComplete(ItemInfoName, NewItemInfoRoot, CompleteItemInfo,
                            "detail popup, blurred backdrop, scrolling body"),
            HookDepotTile(),
        };

        AssetDatabase.SaveAssets();
        Debug.Log($"{Tag} Depot UI:\n  " + string.Join("\n  ", done));
    }

    [MenuItem("Arknights/Depot/Reset Depot UI To Default", false, 20)]
    public static void ResetDepotUIToDefault()
    {
        if (!GuardEditMode()) return;

        bool sure = Application.isBatchMode || EditorUtility.DisplayDialog(
            "Reset Depot UI",
            $"This builds {PrefabPath(DepotName)} and {PrefabPath(ItemInfoName)} again from scratch, over " +
            "what is there. Anything changed on them by hand - positions, colours, art - is lost.",
            "Reset", "Cancel");
        if (!sure)
        {
            Debug.Log($"{Tag} Reset cancelled; nothing changed.");
            return;
        }

        RebuildDepotUI();
    }

    /// <summary>
    /// 重建 / Builds both prefabs again over the existing ones, keeping their GUIDs, then hooks the
    /// tile. What Reset does once it has asked; called directly only by automation that has decided to.
    /// </summary>
    public static void RebuildDepotUI()
    {
        if (!GuardEditMode()) return;

        CreateMissingItemMetas();
        LoadFont();

        List<string> done = new List<string>
        {
            Rebuild(DepotName, NewDepotRoot, CompleteDepot),
            Rebuild(ItemInfoName, NewItemInfoRoot, CompleteItemInfo),
            HookDepotTile(),
        };

        AssetDatabase.SaveAssets();
        Debug.Log($"{Tag} Depot UI rebuilt from scratch:\n  " + string.Join("\n  ", done));
    }

    /// <summary>
    /// 有就补, 没有才建 / Builds the prefab when there is none. One that exists is kept: only the parts
    /// it lacks are added, and it is saved only when one was - so a second run writes nothing.
    /// </summary>
    private static string BuildOrComplete(string name, System.Func<GameObject> newRoot,
                                          System.Func<GameObject, List<string>> complete, string built)
    {
        string path = PrefabPath(name);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            Save(BuildFresh(newRoot, complete), name);
            return $"{name}: built at {path} - {built}";
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(path);

        try
        {
            List<string> added = complete(contents);
            if (added.Count == 0) return $"{name}: kept as it is";

            PrefabUtility.SaveAsPrefabAsset(contents, path);
            return $"{name}: kept, and added what it lacked - {string.Join(", ", added)}";
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static string Rebuild(string name, System.Func<GameObject> newRoot,
                                  System.Func<GameObject, List<string>> complete)
    {
        Save(BuildFresh(newRoot, complete), name);
        return $"{name}: built again at {PrefabPath(name)}";
    }

    /// <summary>
    /// 新建就是全补 / A new screen is an empty root completed: every part is missing, so every part is
    /// built - by the same code that adds one to an older prefab, so the two come out alike.
    /// </summary>
    private static GameObject BuildFresh(System.Func<GameObject> newRoot,
                                         System.Func<GameObject, List<string>> complete)
    {
        GameObject root = newRoot();
        complete(root);
        return root;
    }

    private static GameObject NewDepotRoot()
    {
        GameObject root = new GameObject(DepotName,
            typeof(RectTransform), typeof(CanvasGroup), typeof(UI.Sub.DepotUI));
        Stretch((RectTransform)root.transform);
        return root;
    }

    /// <summary>
    /// 补上缺的部分 / Adds each part of the depot screen that is missing - the backdrop, the top bar
    /// with its back button and tabs, the grid with its cell template - and leaves every part that is
    /// there as it is. Returns what it added: nothing, on a screen that has them all.
    /// </summary>
    private static List<string> CompleteDepot(GameObject root)
    {
        List<string> added = new List<string>();
        RectTransform rect = (RectTransform)root.transform;

        UI.Sub.DepotUI house = root.GetComponent<UI.Sub.DepotUI>();
        if (house == null)
        {
            Debug.LogWarning($"{Tag} {root.name} has no DepotUI component; Reset Depot UI To Default builds it again.");
            return added;
        }

        Transform backdrop = EnsurePart(rect, "Backdrop", null, "the backdrop", added, () => BuildBackdrop(rect));
        Transform bar = EnsurePart(rect, "TopBar", backdrop, "the top bar", added, () => BuildDepotTopBar(rect, house));
        EnsurePart(rect, "Grid", bar, "the grid", added, () => BuildGrid(rect, house));

        Bind(ref house.ALL, nameof(house.ALL), rect, "TopBar/Tabs/ALL", added);
        Bind(ref house.JI_CHU, nameof(house.JI_CHU), rect, "TopBar/Tabs/JI_CHU", added);
        Bind(ref house.YANG_CHENG, nameof(house.YANG_CHENG), rect, "TopBar/Tabs/YANG_CHENG", added);
        Bind(ref house.prefab, nameof(house.prefab), rect, "Grid/Viewport/Content/ItemIconTemplate", added);

        return added;
    }

    private static RectTransform BuildBackdrop(RectTransform parent)
    {
        UnityEngine.UI.Image backdrop = NewImage("Backdrop", parent, Paper);
        Stretch((RectTransform)backdrop.transform);
        backdrop.raycastTarget = true;
        return (RectTransform)backdrop.transform;
    }

    private static RectTransform BuildDepotTopBar(RectTransform parent, UI.Sub.DepotUI house)
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
        UnityEditor.Events.UnityEventTools.AddBoolPersistentListener(backButton.onClick, house.Hide, true);

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

        house.ALL = NewTab("ALL", tabs, group, "ALL", true);
        house.JI_CHU = NewTab("JI_CHU", tabs, group, Expand.GetName(ItemType.JI_CHU), false);
        house.YANG_CHENG = NewTab("YANG_CHENG", tabs, group, Expand.GetName(ItemType.YANG_CHENG), false);
        return bar;
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

    private static RectTransform BuildGrid(RectTransform parent, UI.Sub.DepotUI house)
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

        house.prefab = BuildItemIconTemplate(content).GetComponent<ItemIconComponent>();
        return scrollRoot;
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

    private static GameObject NewItemInfoRoot()
    {
        GameObject root = new GameObject(ItemInfoName,
            typeof(RectTransform), typeof(CanvasGroup), typeof(UI.Sub.ItemInfoUI));
        Stretch((RectTransform)root.transform);
        return root;
    }

    /// <summary>
    /// 补上缺的部分 / Adds each part of the item popup that is missing - the blurred backdrop that
    /// closes it, the card - and leaves every part that is there as it is. Returns what it added.
    /// </summary>
    private static List<string> CompleteItemInfo(GameObject root)
    {
        List<string> added = new List<string>();
        RectTransform rect = (RectTransform)root.transform;

        UI.Sub.ItemInfoUI info = root.GetComponent<UI.Sub.ItemInfoUI>();
        if (info == null)
        {
            Debug.LogWarning($"{Tag} {root.name} has no ItemInfoUI component; Reset Depot UI To Default builds it again.");
            return added;
        }

        Transform blur = EnsurePart(rect, "Blur", null, "the blurred backdrop", added, () => BuildInfoBlur(rect, info));
        EnsurePart(rect, "Panel", blur, "the card", added, () => BuildInfoPanel(rect, info));

        const string Body = "Panel/Body/Viewport/Content";
        Bind(ref info.blur, nameof(info.blur), rect, "Blur", added);
        Bind(ref info.itemName, nameof(info.itemName), rect, "Panel/NameGround/ItemName", added);
        Bind(ref info.amount, nameof(info.amount), rect, "Panel/StockGround/Amount", added);
        Bind(ref info.icon, nameof(info.icon), rect, "Panel/Icon", added);
        Bind(ref info.useInfo, nameof(info.useInfo), rect, Body + "/UseInfoGround/UseInfo", added);
        Bind(ref info.description, nameof(info.description), rect, Body + "/Description", added);
        Bind(ref info.waysObtain, nameof(info.waysObtain), rect, Body + "/WaysObtain", added);

        return added;
    }

    private static RectTransform BuildInfoBlur(RectTransform parent, UI.Sub.ItemInfoUI info)
    {
        // 点背景关闭 / The blurred backdrop doubles as the dismiss button, the way tapping
        // outside the card closes it in the reference screens.
        // 背景压暗一点 / Tinted down, not just blurred. The depot behind is nearly the same
        // shade as the card, so blur alone leaves the two indistinguishable; the reference
        // darkens the backdrop as well, and that is what separates them.
        UnityEngine.UI.Image blur = NewImage("Blur", parent, new Color(0.70f, 0.71f, 0.73f, 1f));
        Stretch((RectTransform)blur.transform);
        blur.material = BlurMaterial();
        blur.gameObject.AddComponent<UI.UiBlurCapture>();

        Button dismiss = blur.gameObject.AddComponent<Button>();
        dismiss.targetGraphic = blur;
        UnityEditor.Events.UnityEventTools.AddBoolPersistentListener(dismiss.onClick, info.Hide, true);

        info.blur = blur;
        return (RectTransform)blur.transform;
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

    /// <summary>
    /// 只加缺的 / Points Home's Depot tile at the depot, adding only what the tile lacks - a clear hit
    /// area when it draws nothing of its own, a Button, an OpenUIButton naming DepotUI - and saving
    /// HomeUI only when it changed one of them.
    /// </summary>
    private static string HookDepotTile()
    {
        string homePath = UiPrefabDir + "/HomeUI.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(homePath) == null)
        {
            Debug.LogWarning($"{Tag} No HomeUI prefab at {homePath}, so nothing opens the depot yet.");
            return $"HomeUI {DepotTileName}: NOT hooked - no HomeUI prefab";
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(homePath);

        try
        {
            Transform tile = FindDeep(contents.transform, DepotTileName);
            if (tile == null)
            {
                Debug.LogWarning($"{Tag} HomeUI has no '{DepotTileName}' child.");
                return $"HomeUI {DepotTileName}: NOT hooked - no such tile";
            }

            List<string> changed = new List<string>();

            // 透明图也能接收点击 / An alpha-zero Image is still a raycast target: a tile that draws
            // nothing of its own gets one as a child, stretched over it - once, since a second run
            // finds it.
            Graphic face = tile.GetComponent<Graphic>();
            if (face == null)
            {
                Transform hit = tile.Find("Hit");
                if (hit != null)
                {
                    face = hit.GetComponent<Graphic>();
                }
                else
                {
                    face = NewImage("Hit", tile, new Color(0f, 0f, 0f, 0f));
                    Stretch(face.rectTransform);
                    changed.Add("added a hit area");
                }
            }

            // 显式判空, 不用 ?? / Explicit null checks rather than ??: in the Editor a missing
            // component can come back as Unity's fake-null object, which compares equal to null
            // but is not a null reference, so ?? would keep it instead of adding the component.
            Button button = tile.GetComponent<Button>();
            if (button == null)
            {
                button = tile.gameObject.AddComponent<Button>();
                changed.Add("added a Button");
            }
            if (button.targetGraphic == null && face != null)
            {
                button.targetGraphic = face;
                changed.Add("pointed the Button at the tile's graphic");
            }

            OpenUIButton open = tile.GetComponent<OpenUIButton>();
            if (open == null)
            {
                open = tile.gameObject.AddComponent<OpenUIButton>();
                changed.Add("added an OpenUIButton");
            }
            if (open.uiName != DepotName)
            {
                open.uiName = DepotName;
                changed.Add($"named {DepotName} on it");
            }

            if (changed.Count == 0) return $"HomeUI {DepotTileName}: already opens {DepotName}";

            PrefabUtility.SaveAsPrefabAsset(contents, homePath);
            return $"HomeUI {DepotTileName}: now opens {DepotName} - {string.Join(", ", changed)}";
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

    private static void LoadFont()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) Debug.LogWarning($"{Tag} No builtin font found; Text components will render blank.");
    }

    private static string PrefabPath(string name) => $"{UiPrefabDir}/{name}.prefab";

    private static void Save(GameObject root, string name)
    {
        Directory.CreateDirectory(UiPrefabDir);
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(name));
        Object.DestroyImmediate(root);
    }

    /// <summary>
    /// 有就留着, 没有才建 / The screen's part called <paramref name="name"/>: the one that is there,
    /// kept as it is, or a new one from <paramref name="build"/> when there is none - put straight
    /// after the part before it, so a backdrop added to an older prefab still draws behind the rest.
    /// </summary>
    private static Transform EnsurePart(Transform root, string name, Transform previous, string what,
                                        List<string> added, System.Func<RectTransform> build)
    {
        Transform part = root.Find(name);
        if (part != null) return part;

        part = build();
        part.SetSiblingIndex(previous != null ? previous.GetSiblingIndex() + 1 : 0);
        added.Add(what);
        return part;
    }

    /// <summary>
    /// 只绑空的 / Points one of the screen's fields at the node it reads, only when the field points at
    /// nothing: one pointed elsewhere by hand stays where it was put. A node that is not there is
    /// reported rather than built - Reset Depot UI To Default builds the whole screen again.
    /// </summary>
    private static void Bind<T>(ref T field, string fieldName, Transform root, string path, List<string> added)
        where T : Component
    {
        if (field != null) return;

        Transform node = root.Find(path);
        T found = node != null ? node.GetComponent<T>() : null;
        if (found == null)
        {
            Debug.LogWarning($"{Tag} {root.name}.{fieldName} is empty and there is no {typeof(T).Name} at " +
                             $"{path} to point it at; Reset Depot UI To Default builds the screen again.");
            return;
        }

        field = found;
        added.Add($"{fieldName} pointed at {path}");
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
