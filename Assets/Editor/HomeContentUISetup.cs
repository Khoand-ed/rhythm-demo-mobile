using System.Collections.Generic;
using TMPro;
using UI;
using UI.Sub;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 邮箱 / 公告 / 签到 / Builds the three screens behind Home's top-left icons, after the reference
/// screens:
///   - MailUI: the mailbox, full screen, with the open mail on a card over it,
///   - NoticeUI: the announcement board, Event / System / News,
///   - CalendarUI: the month's sign-in,
/// and wires the icons - AlertIcon, MailIcon, CalendarIcon - to them, each with its red dot.
///
/// 找到就不动 / Find-or-create: a prefab that exists is kept, and an icon already wired is left
/// alone. Rebuild deletes the three prefabs and builds them again; Home's wiring stays.
///
/// The icons are drawn from rectangles, like Home's own: the default TMP font has no envelope,
/// tick or calendar glyph, and a missing glyph renders as a box.
/// </summary>
public static class HomeContentUISetup
{
    private const string UiPrefabDir = "Assets/Arknights/Resources/Prefab/UI";

    private static readonly Color Ink = new Color(0.07f, 0.075f, 0.085f, 1f);
    private static readonly Color BarInk = new Color(0.15f, 0.155f, 0.17f, 1f);
    private static readonly Color Row = new Color(0.16f, 0.17f, 0.19f, 1f);
    private static readonly Color Light = new Color(0.86f, 0.86f, 0.87f, 1f);
    private static readonly Color Paper = new Color(0.90f, 0.90f, 0.91f, 1f);
    private static readonly Color PaperInk = new Color(0.14f, 0.14f, 0.16f, 1f);
    private static readonly Color Slab = new Color(0.30f, 0.31f, 0.33f, 1f);
    private static readonly Color Accent = new Color(0.10f, 0.55f, 0.80f, 1f);
    private static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);
    private static readonly Color Yellow = new Color(0.96f, 0.73f, 0.05f, 1f);
    private static readonly Color Red = new Color(0.90f, 0.22f, 0.20f, 1f);
    private static readonly Color Veil = new Color(0f, 0f, 0f, 0.72f);

    // ------------------------------------------------------------------ menu items

    [MenuItem("Arknights/Home/Build Mail, Notice and Calendar UI", false, 30)]
    public static void Build()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        List<string> done = new List<string>
        {
            BuildIfMissing(MailUI.UIName, BuildMail),
            BuildIfMissing(NoticeUI.UIName, BuildNotice),
            BuildIfMissing(CalendarUI.UIName, BuildCalendar),
            HookHome(),
        };

        AssetDatabase.SaveAssets();
        Debug.Log("[HomeContentUISetup] " + string.Join("\n  ", done));
    }

    [MenuItem("Arknights/Home/Rebuild Mail, Notice and Calendar UI", false, 31)]
    public static void Rebuild()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        foreach (string name in new[] { MailUI.UIName, NoticeUI.UIName, CalendarUI.UIName })
        {
            AssetDatabase.DeleteAsset($"{UiPrefabDir}/{name}.prefab");
        }
        Build();
    }

    private static string BuildIfMissing(string name, System.Func<GameObject> build)
    {
        string path = $"{UiPrefabDir}/{name}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return $"{name}: kept";

        GameObject root = build();
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return $"{name}: built at {path}";
    }

    // ================================================================== mail

    private static GameObject BuildMail()
    {
        GameObject root = NewRoot(MailUI.UIName, typeof(MailUI));
        MailUI ui = root.GetComponent<MailUI>();
        RectTransform rect = (RectTransform)root.transform;

        Stretch(NewImage("Backdrop", rect, Ink, true).rectTransform);

        // ---- top: back slab, the Mails tab, the light rule under them
        UnityEngine.UI.Image top = NewImage("TopBar", rect, new Color(0.05f, 0.055f, 0.06f, 1f), false);
        Place(top.rectTransform, TopLeft, TopRight, TopCenter, Vector2.zero, new Vector2(0f, 120f));
        UnityEngine.UI.Image rule = NewImage("Rule", rect, Light, false);
        Place(rule.rectTransform, TopLeft, TopRight, TopCenter, new Vector2(0f, -120f), new Vector2(0f, 14f));

        UnityEngine.UI.Image back = NewImage("BackButton", rect, Color.white, true);
        Place(back.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(42f, -33f), new Vector2(156f, 55f));
        Chevron(back.rectTransform, "Glyph", new Vector2(-46f, 0f), 30f, PaperInk, left: true);
        ui.backButton = back.gameObject.AddComponent<Button>();

        UnityEngine.UI.Image tab = NewImage("MailsTab", rect, Light, false);
        Place(tab.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(247f, 0f), new Vector2(256f, 134f));
        Envelope(tab.rectTransform, "Icon", new Vector2(-58f, 6f), 62f, PaperInk, Light);
        TextMeshProUGUI tabLabel = NewText("Label", tab.rectTransform, 30f, PaperInk, TextAlignmentOptions.Left);
        Place(tabLabel.rectTransform, Center, Center, Center, new Vector2(50f, 6f), new Vector2(120f, 50f));
        tabLabel.text = "Mails";

        // ---- the list
        ui.list = ScrollList(rect, new Vector2(120f, 160f), new Vector2(-120f, -160f), 14f);
        ui.rowTemplate = MailRow(rect);

        TextMeshProUGUI empty = NewText("Empty", rect, 84f, Color.white, TextAlignmentOptions.Center);
        Place(empty.rectTransform, Center, Center, Center, new Vector2(0f, 10f), new Vector2(1200f, 120f));
        empty.text = "No new mail";
        empty.fontStyle = FontStyles.Bold;
        ui.emptyState = empty.gameObject;

        // ---- bottom: count, unread, the two buttons
        UnityEngine.UI.Image bottom = NewImage("BottomBar", rect, BarInk, true);
        Place(bottom.rectTransform, BottomLeft, BottomRight, BottomCenter, Vector2.zero, new Vector2(0f, 136f));
        RectTransform b = bottom.rectTransform;

        Envelope(Spot(b, "IconSpot", BottomLeft, new Vector2(82f, 88f)), "Icon", Vector2.zero, 44f, Color.white, BarInk);
        ui.countText = Label(b, "Count", new Vector2(176f, 88f), new Vector2(110f, 40f), 30f, Color.white, TextAlignmentOptions.Left);
        TextMeshProUGUI unreadLabel = Label(b, "UnreadLabel", new Vector2(282f, 88f), new Vector2(120f, 40f), 30f, Color.white, TextAlignmentOptions.Left);
        unreadLabel.text = "Unread";
        ui.unreadText = Label(b, "Unread", new Vector2(412f, 88f), new Vector2(80f, 40f), 30f, Accent, TextAlignmentOptions.Left);
        TextMeshProUGUI note = Label(b, "Note", new Vector2(56f, 36f), new Vector2(900f, 32f), 24f, Dim, TextAlignmentOptions.Left);
        note.text = "Mail will be deleted starting from the oldest when the limit is reached";

        ui.deleteButton = SolidButton("DeleteButton", b, BottomRight, new Vector2(-566f, 26f), new Vector2(300f, 84f), "Delete Read", Slab, Color.white);
        ui.collectButton = SolidButton("CollectButton", b, BottomRight, new Vector2(-230f, 26f), new Vector2(300f, 84f), "Collect All", Accent, Color.white);

        BuildMailDetail(rect, ui);
        return root;
    }

    /// <summary>One mail on the list: who it is from, what it is, when, and whether it is new.</summary>
    private static RectTransform MailRow(RectTransform parent)
    {
        UnityEngine.UI.Image face = NewImage("RowTemplate", parent, Row, true);
        RectTransform row = face.rectTransform;
        FixedSize(row, 1680f, 140f);
        row.gameObject.AddComponent<CanvasGroup>();
        row.gameObject.AddComponent<Button>().targetGraphic = face;

        UnityEngine.UI.Image portrait = NewImage("Portrait", row, Color.white, false);
        Place(portrait.rectTransform, MiddleLeft, MiddleLeft, MiddleLeft, new Vector2(20f, 0f), new Vector2(112f, 112f));
        portrait.preserveAspect = true;
        Envelope(row, "Envelope", new Vector2(76f - 840f, 0f), 70f, Light, Row);

        TextMeshProUGUI title = NewText("Title", row, 34f, Color.white, TextAlignmentOptions.Left);
        Place(title.rectTransform, TopLeft, TopRight, TopLeft, new Vector2(160f, -22f), new Vector2(-620f, 46f));
        title.fontStyle = FontStyles.Bold;
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.overflowMode = TextOverflowModes.Ellipsis;

        TextMeshProUGUI sender = NewText("Sender", row, 24f, Dim, TextAlignmentOptions.Left);
        Place(sender.rectTransform, BottomLeft, BottomRight, BottomLeft, new Vector2(160f, 24f), new Vector2(-620f, 36f));

        TextMeshProUGUI items = NewText("Items", row, 24f, Accent, TextAlignmentOptions.Right);
        Place(items.rectTransform, MiddleRight, MiddleRight, MiddleRight, new Vector2(-300f, 0f), new Vector2(300f, 36f));

        TextMeshProUGUI date = NewText("Date", row, 24f, Color.white, TextAlignmentOptions.Right);
        Place(date.rectTransform, TopRight, TopRight, TopRight, new Vector2(-30f, -26f), new Vector2(240f, 36f));
        TextMeshProUGUI expires = NewText("Expires", row, 20f, Yellow, TextAlignmentOptions.Right);
        Place(expires.rectTransform, BottomRight, BottomRight, BottomRight, new Vector2(-30f, 24f), new Vector2(260f, 32f));

        UnityEngine.UI.Image dot = NewImage("NewDot", row, Red, false);
        dot.sprite = Knob();
        Place(dot.rectTransform, TopLeft, TopLeft, Center, new Vector2(14f, -14f), new Vector2(22f, 22f));

        row.gameObject.SetActive(false);
        return row;
    }

    /// <summary>The open mail: a light card over a veil, with arrows to the neighbouring mails.</summary>
    private static void BuildMailDetail(RectTransform rect, MailUI ui)
    {
        UnityEngine.UI.Image veil = NewImage("Detail", rect, Veil, true);
        Stretch(veil.rectTransform);
        veil.gameObject.AddComponent<CanvasGroup>();
        ui.detail = veil.gameObject;
        RectTransform v = veil.rectTransform;

        UnityEngine.UI.Image card = NewImage("Card", v, Paper, true);
        Place(card.rectTransform, Center, Center, Center, new Vector2(7f, 13f), new Vector2(1350f, 700f));
        RectTransform c = card.rectTransform;

        UnityEngine.UI.Image head = NewImage("Head", c, new Color(0.95f, 0.95f, 0.96f, 1f), false);
        Place(head.rectTransform, TopLeft, TopRight, TopCenter, Vector2.zero, new Vector2(0f, 86f));

        UnityEngine.UI.Image close = NewImage("CloseButton", c, new Color(0.17f, 0.17f, 0.19f, 1f), true);
        Place(close.rectTransform, TopRight, TopRight, TopLeft, Vector2.zero, new Vector2(92f, 86f));
        Cross(close.rectTransform, "Glyph", Vector2.zero, 46f, Color.white);
        ui.detailClose = close.gameObject.AddComponent<Button>();

        UnityEngine.UI.Image frame = NewImage("PortraitFrame", c, new Color(0.35f, 0.36f, 0.38f, 1f), false);
        Place(frame.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(22f, -20f), new Vector2(170f, 170f));
        ui.portrait = NewImage("Portrait", frame.rectTransform, Color.white, false);
        Stretch(ui.portrait.rectTransform);
        ui.portrait.preserveAspect = true;
        ui.portraitFallback = Envelope(frame.rectTransform, "Envelope", Vector2.zero, 100f, Light, new Color(0.35f, 0.36f, 0.38f, 1f)).gameObject;

        ui.detailTitle = Label(c, "Title", new Vector2(212f, -43f), new Vector2(900f, 60f), 44f, PaperInk, TextAlignmentOptions.Left, TopLeft);
        ui.detailTitle.textWrappingMode = TextWrappingModes.NoWrap;
        ui.detailTitle.overflowMode = TextOverflowModes.Ellipsis;

        UnityEngine.UI.Image pill = NewImage("FromPill", c, new Color(0.06f, 0.06f, 0.07f, 1f), false);
        Place(pill.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(218f, -106f), new Vector2(320f, 38f));
        ui.fromText = NewText("From", pill.rectTransform, 24f, Color.white, TextAlignmentOptions.Left);
        Stretch(ui.fromText.rectTransform);
        ui.fromText.rectTransform.offsetMin = new Vector2(14f, 0f);
        ui.fromText.textWrappingMode = TextWrappingModes.NoWrap;
        ui.dateText = Label(c, "Date", new Vector2(222f, -174f), new Vector2(300f, 34f), 24f, Accent, TextAlignmentOptions.Left, TopLeft);

        UnityEngine.UI.Image line = NewImage("Rule", c, new Color(0f, 0f, 0f, 0.15f), false);
        Place(line.rectTransform, TopLeft, TopRight, TopCenter, new Vector2(0f, -212f), new Vector2(-60f, 2f));

        // ---- body and attachments scroll together
        RectTransform content = ScrollList(c, new Vector2(66f, 24f), new Vector2(-66f, -226f), 24f, out ui.detailScroll);
        ui.bodyText = NewText("Body", content, 28f, PaperInk, TextAlignmentOptions.TopLeft);
        ui.bodyText.lineSpacing = 8f;

        ui.attachmentList = NewRect("Attachments", content);
        VerticalLayoutGroup stack = ui.attachmentList.gameObject.AddComponent<VerticalLayoutGroup>();
        stack.spacing = 18f;
        stack.childControlWidth = stack.childControlHeight = true;
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = false;

        RectTransform att = NewRect("AttachmentTemplate", ui.attachmentList);
        FixedSize(att, 1200f, 204f);
        UnityEngine.UI.Image box = NewImage("Frame", att, new Color(0.80f, 0.80f, 0.81f, 1f), false);
        Place(box.rectTransform, MiddleLeft, MiddleLeft, MiddleLeft, Vector2.zero, new Vector2(204f, 204f));
        UnityEngine.UI.Image icon = NewImage("Icon", box.rectTransform, Color.white, false);
        Place(icon.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(160f, 160f));
        icon.preserveAspect = true;
        TextMeshProUGUI amount = NewText("Amount", box.rectTransform, 24f, PaperInk, TextAlignmentOptions.BottomRight);
        Place(amount.rectTransform, BottomRight, BottomRight, BottomRight, new Vector2(-8f, 6f), new Vector2(180f, 34f));
        amount.fontStyle = FontStyles.Bold;
        TextMeshProUGUI name = NewText("Name", att, 30f, PaperInk, TextAlignmentOptions.TopLeft);
        Place(name.rectTransform, TopLeft, TopRight, TopLeft, new Vector2(244f, -12f), new Vector2(-244f, 44f));
        TextMeshProUGUI description = NewText("Description", att, 24f, new Color(0.45f, 0.45f, 0.48f, 1f), TextAlignmentOptions.TopLeft);
        description.rectTransform.anchorMin = Vector2.zero;
        description.rectTransform.anchorMax = Vector2.one;
        description.rectTransform.offsetMin = new Vector2(244f, 8f);
        description.rectTransform.offsetMax = new Vector2(0f, -62f);
        description.fontStyle = FontStyles.Italic;
        ui.attachmentTemplate = att;
        att.gameObject.SetActive(false);

        // ---- arrows to the mails either side
        UnityEngine.UI.Image previous = NewImage("PreviousButton", v, new Color(0f, 0f, 0f, 0f), true);
        Place(previous.rectTransform, Center, Center, Center, new Vector2(-780f, 0f), new Vector2(120f, 160f));
        DoubleChevron(previous.rectTransform, Color.white, left: true);
        ui.previousButton = previous.gameObject.AddComponent<Button>();

        UnityEngine.UI.Image next = NewImage("NextButton", v, new Color(0f, 0f, 0f, 0f), true);
        Place(next.rectTransform, Center, Center, Center, new Vector2(786f, 0f), new Vector2(120f, 160f));
        DoubleChevron(next.rectTransform, Color.white, left: false);
        ui.nextButton = next.gameObject.AddComponent<Button>();

        veil.gameObject.SetActive(false);
    }

    // ================================================================== notices

    private static GameObject BuildNotice()
    {
        GameObject root = NewRoot(NoticeUI.UIName, typeof(NoticeUI));
        NoticeUI ui = root.GetComponent<NoticeUI>();
        RectTransform rect = (RectTransform)root.transform;

        Stretch(NewImage("Veil", rect, Veil, true).rectTransform);

        UnityEngine.UI.Image panel = NewImage("Panel", rect, new Color(0.21f, 0.21f, 0.22f, 1f), true);
        Place(panel.rectTransform, Center, Center, Center, new Vector2(0f, 0f), new Vector2(1736f, 900f));
        RectTransform p = panel.rectTransform;

        UnityEngine.UI.Image head = NewImage("Head", p, new Color(0.07f, 0.07f, 0.08f, 1f), false);
        Place(head.rectTransform, TopLeft, TopRight, TopCenter, Vector2.zero, new Vector2(0f, 135f));

        string[] names = { "Event", "System", "News" };
        float[] xs = { -284f, 49f, 383f };
        ui.tabs = new Button[3];
        for (int i = 0; i < 3; i++)
        {
            UnityEngine.UI.Image face = NewImage(names[i] + "Tab", p, new Color(0.16f, 0.17f, 0.19f, 1f), true);
            Place(face.rectTransform, TopCenter, TopCenter, TopCenter, new Vector2(xs[i], -49f), new Vector2(312f, 86f));
            face.name = names[i] + "Tab";
            // 名字约定 / NoticeUI recolours "Face": the tab's own Image is that child's parent, so
            // give it a child of the same look and keep the root clear.
            face.color = new Color(0f, 0f, 0f, 0f);
            UnityEngine.UI.Image inner = NewImage("Face", face.rectTransform, new Color(0.16f, 0.17f, 0.19f, 1f), false);
            Stretch(inner.rectTransform);
            TabIcon(face.rectTransform, i);
            TextMeshProUGUI label = NewText("Label", face.rectTransform, 32f, Color.white, TextAlignmentOptions.Center);
            Place(label.rectTransform, Center, Center, Center, new Vector2(30f, 0f), new Vector2(220f, 60f));
            label.text = names[i];
            UnityEngine.UI.Image pointer = NewImage("Pointer", face.rectTransform, Light, false);
            Place(pointer.rectTransform, BottomCenter, BottomCenter, Center, new Vector2(0f, -4f), new Vector2(18f, 18f));
            pointer.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            UnityEngine.UI.Image dot = NewImage("Dot", face.rectTransform, Red, false);
            dot.sprite = Knob();
            Place(dot.rectTransform, TopRight, TopRight, Center, new Vector2(-10f, -10f), new Vector2(20f, 20f));
            ui.tabs[i] = face.gameObject.AddComponent<Button>();
            ui.tabs[i].targetGraphic = inner;
        }

        // ---- the list on the left
        ui.list = ScrollList(p, new Vector2(50f, 40f), new Vector2(-1350f, -180f), 28f);
        ui.itemTemplate = NoticeItem(p);

        // ---- the notice on the right
        UnityEngine.UI.Image sheet = NewImage("Content", p, new Color(0.80f, 0.80f, 0.81f, 1f), true);
        Place(sheet.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(430f, -180f), new Vector2(1238f, 676f));
        ui.content = sheet.gameObject;
        RectTransform column = ScrollList(sheet.rectTransform, new Vector2(34f, 0f), new Vector2(-34f, -10f), 22f, out ui.contentScroll);
        ((VerticalLayoutGroup)column.GetComponent<VerticalLayoutGroup>()).padding = new RectOffset(0, 0, 0, 24);

        UnityEngine.UI.Image titleBar = NewImage("TitleBar", column, new Color(0.15f, 0.15f, 0.16f, 1f), false);
        LayoutElement titleLayout = titleBar.gameObject.AddComponent<LayoutElement>();
        titleLayout.minHeight = titleLayout.preferredHeight = 72f;
        ui.titleText = NewText("Title", titleBar.rectTransform, 40f, Color.white, TextAlignmentOptions.Left);
        Stretch(ui.titleText.rectTransform);
        ui.titleText.rectTransform.offsetMin = new Vector2(14f, 0f);
        ui.titleText.textWrappingMode = TextWrappingModes.NoWrap;
        ui.titleText.overflowMode = TextOverflowModes.Ellipsis;

        ui.banner = NewImage("Banner", column, Color.white, false);
        ui.banner.preserveAspect = true;
        ui.bannerLayout = ui.banner.gameObject.AddComponent<LayoutElement>();
        ui.bannerLayout.preferredHeight = 360f;
        ui.banner.gameObject.SetActive(false);

        ui.bodyText = NewText("Body", column, 28f, PaperInk, TextAlignmentOptions.TopLeft);
        ui.bodyText.lineSpacing = 4f;

        ui.emptyText = Label(p, "Empty", new Vector2(1049f, -518f), new Vector2(900f, 60f), 30f, Dim, TextAlignmentOptions.Center, TopLeft);

        // ---- close, round, at the panel's corner
        ui.closeButton = RoundClose(rect, new Vector2(846f, 434f));
        return root;
    }

    /// <summary>One notice on the left: its date, a slash, its short title, the unread dot.</summary>
    private static RectTransform NoticeItem(RectTransform parent)
    {
        RectTransform item = NewRect("ItemTemplate", parent);
        FixedSize(item, 330f, 84f);

        UnityEngine.UI.Image face = NewImage("Face", item, Row, true);
        Stretch(face.rectTransform);
        AddEdges(face.rectTransform, new Color(1f, 1f, 1f, 0.55f), 2f);
        item.gameObject.AddComponent<Button>().targetGraphic = face;

        UnityEngine.UI.Image dateBox = NewImage("DateBox", item, new Color(0.45f, 0.45f, 0.47f, 1f), false);
        Place(dateBox.rectTransform, MiddleLeft, MiddleLeft, MiddleLeft, new Vector2(8f, 0f), new Vector2(68f, 68f));
        TextMeshProUGUI day = NewText("DateDay", item, 30f, Color.white, TextAlignmentOptions.TopLeft);
        Place(day.rectTransform, MiddleLeft, MiddleLeft, MiddleLeft, new Vector2(12f, 8f), new Vector2(50f, 44f));
        TextMeshProUGUI month = NewText("DateMonth", item, 16f, Color.white, TextAlignmentOptions.BottomRight);
        Place(month.rectTransform, MiddleLeft, MiddleLeft, MiddleLeft, new Vector2(30f, -20f), new Vector2(40f, 24f));
        Bar(dateBox.rectTransform, "Slash", new Vector2(2f, -2f), new Vector2(40f, 2f), 50f, new Color(1f, 1f, 1f, 0.7f));
        UnityEngine.UI.Image divider = NewImage("Divider", item, new Color(1f, 1f, 1f, 0.5f), false);
        Place(divider.rectTransform, MiddleLeft, MiddleLeft, MiddleLeft, new Vector2(86f, 0f), new Vector2(2f, 68f));

        TextMeshProUGUI title = NewText("Title", item, 24f, Color.white, TextAlignmentOptions.Center);
        title.rectTransform.anchorMin = Vector2.zero;
        title.rectTransform.anchorMax = Vector2.one;
        title.rectTransform.offsetMin = new Vector2(92f, 4f);
        title.rectTransform.offsetMax = new Vector2(-10f, -4f);
        title.enableAutoSizing = true;
        title.fontSizeMin = 16f;
        title.fontSizeMax = 24f;

        UnityEngine.UI.Image arrow = NewImage("Arrow", item, Light, false);
        Place(arrow.rectTransform, MiddleRight, MiddleRight, Center, new Vector2(0f, 0f), new Vector2(40f, 40f));
        arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

        UnityEngine.UI.Image dot = NewImage("Dot", item, Red, false);
        dot.sprite = Knob();
        Place(dot.rectTransform, TopRight, TopRight, Center, new Vector2(-6f, -6f), new Vector2(18f, 18f));

        item.gameObject.SetActive(false);
        return item;
    }

    // ================================================================== calendar

    private static GameObject BuildCalendar()
    {
        GameObject root = NewRoot(CalendarUI.UIName, typeof(CalendarUI));
        CalendarUI ui = root.GetComponent<CalendarUI>();
        RectTransform rect = (RectTransform)root.transform;

        Stretch(NewImage("Veil", rect, Veil, true).rectTransform);

        // ---- the panel: dark on the left, yellow on the right, a yellow rule along the top
        RectTransform panel = NewRect("Panel", rect);
        Place(panel, Center, Center, Center, new Vector2(0f, 10f), new Vector2(1566f, 810f));
        UnityEngine.UI.Image yellow = NewImage("Yellow", panel, Yellow, true);
        Place(yellow.rectTransform, BottomRight, TopRight, MiddleRight, Vector2.zero, new Vector2(500f, 0f));
        UnityEngine.UI.Image dark = NewImage("Dark", panel, new Color(0.17f, 0.17f, 0.18f, 0.98f), true);
        dark.rectTransform.anchorMin = Vector2.zero;
        dark.rectTransform.anchorMax = Vector2.one;
        dark.rectTransform.offsetMin = Vector2.zero;
        dark.rectTransform.offsetMax = new Vector2(-500f, -8f);
        UnityEngine.UI.Image topRule = NewImage("TopRule", panel, Yellow, false);
        Place(topRule.rectTransform, TopLeft, TopRight, TopCenter, Vector2.zero, new Vector2(0f, 8f));

        RectTransform d = dark.rectTransform;
        CalendarGlyph(Spot(d, "IconSpot", TopLeft, new Vector2(70f, -40f)), "Icon", Vector2.zero, 74f, Color.white);
        ui.monthText = Label(d, "Month", new Vector2(124f, -40f), new Vector2(260f, 60f), 40f, Color.white, TextAlignmentOptions.Left, TopLeft);
        TextMeshProUGUI blurb = Label(d, "Blurb", new Vector2(290f, -40f), new Vector2(740f, 64f), 18f, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.Left, TopLeft);
        blurb.text = "Sign in once a day for the day's supplies. A missed day is not lost - the next square simply waits for you.";

        ui.grid = NewRect("Grid", d);
        Place(ui.grid, TopLeft, TopLeft, TopLeft, new Vector2(24f, -92f), new Vector2(1010f, 700f));
        GridLayoutGroup grid = ui.grid.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(132f, 130f);
        grid.spacing = new Vector2(10f, 12f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 7;
        ui.cellTemplate = CalendarCell(ui.grid);

        // ---- the side panel
        UnityEngine.UI.Image side = NewImage("Side", panel, new Color(0.24f, 0.24f, 0.25f, 1f), false);
        Place(side.rectTransform, TopRight, TopRight, TopRight, new Vector2(-194f, -8f), new Vector2(336f, 677f));
        RectTransform s = side.rectTransform;

        ClockGlyph(s, "Clock", new Vector2(-64f, -26f), 30f, Color.white);
        ui.timerText = Label(s, "Timer", new Vector2(-6f, -26f), new Vector2(150f, 40f), 28f, Color.white, TextAlignmentOptions.Left, TopCenter);

        UnityEngine.UI.Image pill = NewImage("SupplyPill", s, Color.white, false);
        Place(pill.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(16f, -78f), new Vector2(136f, 34f));
        TextMeshProUGUI supply = NewText("Label", pill.rectTransform, 22f, PaperInk, TextAlignmentOptions.Center);
        Stretch(supply.rectTransform);
        supply.text = "Daily Supply";

        ui.rewardName = Label(s, "RewardName", new Vector2(-20f, -146f), new Vector2(200f, 40f), 28f, Color.white, TextAlignmentOptions.Right, TopRight);
        UnityEngine.UI.Image amountBar = NewImage("AmountBar", s, new Color(0.38f, 0.38f, 0.40f, 1f), false);
        Place(amountBar.rectTransform, TopRight, TopRight, TopRight, new Vector2(-24f, -170f), new Vector2(256f, 42f));
        ui.rewardAmount = NewText("Amount", amountBar.rectTransform, 26f, Color.white, TextAlignmentOptions.Right);
        Stretch(ui.rewardAmount.rectTransform);
        ui.rewardAmount.rectTransform.offsetMax = new Vector2(-10f, 0f);

        // 图标压在数量条上 / The item sits over the end of its amount bar, as in the reference.
        ui.rewardIcon = NewImage("RewardIcon", s, Color.white, false);
        Place(ui.rewardIcon.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(20f, -130f), new Vector2(130f, 110f));
        ui.rewardIcon.preserveAspect = true;

        // ---- signed, bottom right on the yellow
        RectTransform badge = NewRect("Signed", panel);
        Place(badge, BottomRight, BottomRight, BottomRight, new Vector2(-170f, 30f), new Vector2(240f, 60f));
        UnityEngine.UI.Image boxFrame = NewImage("Box", badge, PaperInk, false);
        Place(boxFrame.rectTransform, MiddleLeft, MiddleLeft, MiddleLeft, Vector2.zero, new Vector2(38f, 38f));
        UnityEngine.UI.Image boxInner = NewImage("Inner", boxFrame.rectTransform, Yellow, false);
        Place(boxInner.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(30f, 30f));
        Check(boxFrame.rectTransform, "Tick", new Vector2(2f, 2f), 30f, PaperInk);
        ui.signedText = NewText("Label", badge, 32f, PaperInk, TextAlignmentOptions.Left);
        Place(ui.signedText.rectTransform, MiddleLeft, MiddleLeft, MiddleLeft, new Vector2(52f, 0f), new Vector2(190f, 50f));
        ui.signedText.text = "Signed-in";
        ui.signedBadge = badge.gameObject;

        ui.closeButton = RoundClose(rect, new Vector2(776f, 418f));
        return root;
    }

    /// <summary>One day: its number large and faint, the reward, the amount; ticked once signed.</summary>
    private static RectTransform CalendarCell(RectTransform grid)
    {
        RectTransform cell = NewRect("CellTemplate", grid);
        UnityEngine.UI.Image hit = NewImage("Hit", cell, new Color(1f, 1f, 1f, 0.03f), true);
        Stretch(hit.rectTransform);
        cell.gameObject.AddComponent<Button>().targetGraphic = hit;

        UnityEngine.UI.Image today = NewImage("Today", cell, new Color(0.62f, 0.62f, 0.64f, 1f), false);
        Stretch(today.rectTransform);
        UnityEngine.UI.Image fill = NewImage("Fill", today.rectTransform, Yellow, false);
        Stretch(fill.rectTransform);
        fill.rectTransform.offsetMin = new Vector2(8f, 8f);
        fill.rectTransform.offsetMax = new Vector2(-8f, -8f);

        RectTransform picked = NewRect("Picked", cell);
        Stretch(picked);
        AddEdges(picked, new Color(1f, 1f, 1f, 0.8f), 4f);

        TextMeshProUGUI day = NewText("Day", cell, 66f, new Color(1f, 1f, 1f, 0.32f), TextAlignmentOptions.TopLeft);
        Place(day.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(4f, 6f), new Vector2(120f, 80f));
        day.fontStyle = FontStyles.Bold;

        UnityEngine.UI.Image icon = NewImage("Icon", cell, Color.white, false);
        Place(icon.rectTransform, Center, Center, Center, new Vector2(4f, -2f), new Vector2(96f, 96f));
        icon.preserveAspect = true;

        TextMeshProUGUI amount = NewText("Amount", cell, 20f, Color.white, TextAlignmentOptions.BottomRight);
        Place(amount.rectTransform, BottomRight, BottomRight, BottomRight, new Vector2(-6f, 6f), new Vector2(124f, 50f));
        amount.outlineWidth = 0.2f;
        amount.outlineColor = Color.black;

        UnityEngine.UI.Image shade = NewImage("Shade", cell, new Color(0f, 0f, 0f, 0.38f), false);
        Stretch(shade.rectTransform);
        Check(cell, "Check", new Vector2(4f, 6f), 76f, Color.white);

        cell.gameObject.SetActive(false);
        return cell;
    }

    // ================================================================== Home

    /// <summary>
    /// 左上角三个图标 / Home's AlertIcon, MailIcon and CalendarIcon open the three screens, each with
    /// its red dot. Only what is missing is added.
    /// </summary>
    private static string HookHome()
    {
        string path = UiPrefabDir + "/HomeUI.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return "HomeUI: no prefab, not hooked";

        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        try
        {
            List<string> hooked = new List<string>();
            (string icon, string screen, HomeBadge.Kind kind)[] icons =
            {
                ("AlertIcon", NoticeUI.UIName, HomeBadge.Kind.Notice),
                ("MailIcon", MailUI.UIName, HomeBadge.Kind.Mail),
                ("CalendarIcon", CalendarUI.UIName, HomeBadge.Kind.SignIn),
            };

            foreach ((string iconName, string screen, HomeBadge.Kind kind) in icons)
            {
                Transform icon = FindDeep(contents.transform, iconName);
                if (icon == null) { hooked.Add(iconName + ": not found"); continue; }

                bool changed = false;
                if (icon.GetComponent<Button>() == null) { icon.gameObject.AddComponent<Button>(); changed = true; }

                OpenUIButton open = icon.GetComponent<OpenUIButton>();
                if (open == null) { open = icon.gameObject.AddComponent<OpenUIButton>(); changed = true; }
                if (string.IsNullOrEmpty(open.uiName)) { open.uiName = screen; changed = true; }

                if (icon.GetComponent<HomeBadge>() == null)
                {
                    HomeBadge badge = icon.gameObject.AddComponent<HomeBadge>();
                    badge.kind = kind;
                    UnityEngine.UI.Image dot = NewImage("Badge", icon, Red, false);
                    dot.sprite = Knob();
                    Place(dot.rectTransform, TopRight, TopRight, Center, new Vector2(-4f, -4f), new Vector2(16f, 16f));
                    dot.gameObject.SetActive(false);
                    badge.dot = dot.gameObject;
                    changed = true;
                }

                hooked.Add(iconName + (changed ? " -> " + open.uiName : ": already wired"));
            }

            PrefabUtility.SaveAsPrefabAsset(contents, path);
            return "HomeUI: " + string.Join(", ", hooked);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // ================================================================== glyphs

    private static RectTransform Envelope(RectTransform parent, string name, Vector2 position, float width, Color ink, Color paper)
    {
        RectTransform root = NewRect(name, parent);
        Place(root, Center, Center, Center, position, new Vector2(width, width * 0.7f));
        UnityEngine.UI.Image body = NewImage("Body", root, ink, false);
        Stretch(body.rectTransform);
        // 两片封口从上角斜到中间 / Each flap runs from a top corner down to the middle.
        float flap = width * 0.60f;
        Bar(root, "FlapLeft", new Vector2(-width * 0.25f, width * 0.18f), new Vector2(flap, width * 0.07f), -31f, paper);
        Bar(root, "FlapRight", new Vector2(width * 0.25f, width * 0.18f), new Vector2(flap, width * 0.07f), 31f, paper);
        return root;
    }

    private static void Check(RectTransform parent, string name, Vector2 position, float size, Color color)
    {
        RectTransform root = NewRect(name, parent);
        Place(root, Center, Center, Center, position, new Vector2(size, size));
        Bar(root, "Short", new Vector2(-size * 0.20f, -size * 0.06f), new Vector2(size * 0.40f, size * 0.13f), -45f, color);
        Bar(root, "Long", new Vector2(size * 0.12f, size * 0.06f), new Vector2(size * 0.72f, size * 0.13f), 45f, color);
    }

    private static void Cross(RectTransform parent, string name, Vector2 position, float size, Color color)
    {
        RectTransform root = NewRect(name, parent);
        Place(root, Center, Center, Center, position, new Vector2(size, size));
        Bar(root, "A", Vector2.zero, new Vector2(size, size * 0.11f), 45f, color);
        Bar(root, "B", Vector2.zero, new Vector2(size, size * 0.11f), -45f, color);
    }

    private static void Chevron(RectTransform parent, string name, Vector2 position, float size, Color color, bool left)
    {
        RectTransform root = NewRect(name, parent);
        Place(root, Center, Center, Center, position, new Vector2(size, size));
        float sign = left ? 1f : -1f;
        Bar(root, "Up", new Vector2(0f, size * 0.18f), new Vector2(size * 0.55f, size * 0.08f), 45f * sign, color);
        Bar(root, "Down", new Vector2(0f, -size * 0.18f), new Vector2(size * 0.55f, size * 0.08f), -45f * sign, color);
    }

    private static void DoubleChevron(RectTransform parent, Color color, bool left)
    {
        float lead = left ? -1f : 1f;
        Chevron(parent, "Outer", new Vector2(18f * lead, 0f), 110f, color, left);
        Chevron(parent, "Inner", new Vector2(-14f * lead, 0f), 110f, color, left);
    }

    private static void CalendarGlyph(RectTransform parent, string name, Vector2 position, float size, Color color)
    {
        RectTransform root = NewRect(name, parent);
        Place(root, Center, Center, Center, position, new Vector2(size, size * 0.86f));
        UnityEngine.UI.Image body = NewImage("Body", root, color, false);
        Stretch(body.rectTransform);
        UnityEngine.UI.Image face = NewImage("Face", root, new Color(0.17f, 0.17f, 0.18f, 1f), false);
        face.rectTransform.anchorMin = Vector2.zero;
        face.rectTransform.anchorMax = Vector2.one;
        face.rectTransform.offsetMin = new Vector2(5f, 5f);
        face.rectTransform.offsetMax = new Vector2(-5f, -18f);
        for (int r = 0; r < 2; r++)
        for (int c = 0; c < 4; c++)
        {
            UnityEngine.UI.Image cell = NewImage("Cell" + r + c, face.rectTransform, color, false);
            Place(cell.rectTransform, Center, Center, Center, new Vector2(-21f + c * 14f, 8f - r * 16f), new Vector2(9f, 9f));
        }
    }

    private static void ClockGlyph(RectTransform parent, string name, Vector2 position, float size, Color color)
    {
        UnityEngine.UI.Image ring = NewImage(name, parent, color, false);
        ring.sprite = Knob();
        Place(ring.rectTransform, TopCenter, TopCenter, Center, position, new Vector2(size, size));
        UnityEngine.UI.Image face = NewImage("Face", ring.rectTransform, new Color(0.24f, 0.24f, 0.25f, 1f), false);
        face.sprite = Knob();
        Place(face.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(size - 6f, size - 6f));
        Bar(ring.rectTransform, "Hand", new Vector2(0f, size * 0.15f), new Vector2(3f, size * 0.32f), 0f, color);
        Bar(ring.rectTransform, "Hand2", new Vector2(size * 0.1f, 0f), new Vector2(size * 0.25f, 3f), 0f, color);
    }

    /// <summary>A small picture on each notice tab: a flag, a gear, a burst.</summary>
    private static void TabIcon(RectTransform tab, int which)
    {
        RectTransform root = NewRect("Icon", tab);
        Place(root, MiddleLeft, MiddleLeft, Center, new Vector2(56f, 2f), new Vector2(64f, 64f));
        Color c = new Color(1f, 1f, 1f, 0.35f);
        switch (which)
        {
            case 0:
                for (int r = 0; r < 4; r++)
                for (int k = 0; k < 4; k++)
                {
                    if ((r + k) % 2 == 1) continue;
                    UnityEngine.UI.Image sq = NewImage("Sq" + r + k, root, c, false);
                    Place(sq.rectTransform, Center, Center, Center, new Vector2(-24f + k * 16f, 24f - r * 16f), new Vector2(16f, 16f));
                }
                break;
            case 1:
                UnityEngine.UI.Image gear = NewImage("Gear", root, c, false);
                gear.sprite = Knob();
                Place(gear.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(52f, 52f));
                for (int t = 0; t < 4; t++) Bar(root, "Tooth" + t, Vector2.zero, new Vector2(64f, 12f), t * 45f, c);
                break;
            default:
                for (int t = 0; t < 3; t++) Bar(root, "Ray" + t, new Vector2(-8f + t * 8f, 0f), new Vector2(60f, 8f), 30f + t * 20f, c);
                break;
        }
    }

    private static Button RoundClose(RectTransform parent, Vector2 position)
    {
        UnityEngine.UI.Image disc = NewImage("CloseButton", parent, new Color(0.38f, 0.38f, 0.40f, 1f), true);
        disc.sprite = Knob();
        Place(disc.rectTransform, Center, Center, Center, position, new Vector2(84f, 84f));
        UnityEngine.UI.Image ring = NewImage("Ring", disc.rectTransform, new Color(0.25f, 0.25f, 0.27f, 1f), false);
        ring.sprite = Knob();
        Place(ring.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(74f, 74f));
        Cross(disc.rectTransform, "Glyph", Vector2.zero, 40f, Color.white);
        Button button = disc.gameObject.AddComponent<Button>();
        button.targetGraphic = disc;
        return button;
    }

    // ================================================================== helpers

    private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
    private static readonly Vector2 TopLeft = new Vector2(0f, 1f);
    private static readonly Vector2 TopCenter = new Vector2(0.5f, 1f);
    private static readonly Vector2 TopRight = new Vector2(1f, 1f);
    private static readonly Vector2 MiddleLeft = new Vector2(0f, 0.5f);
    private static readonly Vector2 MiddleRight = new Vector2(1f, 0.5f);
    private static readonly Vector2 BottomLeft = new Vector2(0f, 0f);
    private static readonly Vector2 BottomCenter = new Vector2(0.5f, 0f);
    private static readonly Vector2 BottomRight = new Vector2(1f, 0f);

    private static Sprite Knob() => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

    private static GameObject NewRoot(string name, System.Type component)
    {
        GameObject root = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup), component);
        Stretch((RectTransform)root.transform);
        return root;
    }

    /// <summary>
    /// A zero-sized point pinned to a corner of <paramref name="parent"/>, for a glyph to centre on.
    /// Pinned by anchors rather than computed from the parent's size, which is still zero while a
    /// stretched prefab is being built outside a canvas.
    /// </summary>
    private static RectTransform Spot(RectTransform parent, string name, Vector2 corner, Vector2 position)
    {
        RectTransform spot = NewRect(name, parent);
        Place(spot, corner, corner, Center, position, Vector2.zero);
        return spot;
    }

    /// <summary>A text placed by its left edge, measured from the parent's bottom-left (or another corner).</summary>
    private static TextMeshProUGUI Label(RectTransform parent, string name, Vector2 position, Vector2 size, float fontSize,
                                         Color color, TextAlignmentOptions alignment, Vector2? corner = null)
    {
        Vector2 at = corner ?? BottomLeft;
        TextMeshProUGUI text = NewText(name, parent, fontSize, color, alignment);
        Vector2 pivot = new Vector2(alignment == TextAlignmentOptions.Right ? 1f : alignment == TextAlignmentOptions.Center ? 0.5f : 0f, 0.5f);
        Place(text.rectTransform, at, at, pivot, position, size);
        return text;
    }

    private static Button SolidButton(string name, RectTransform parent, Vector2 corner, Vector2 position, Vector2 size,
                                      string label, Color face, Color ink)
    {
        UnityEngine.UI.Image image = NewImage(name, parent, face, true);
        Place(image.rectTransform, corner, corner, corner, position, size);
        image.gameObject.AddComponent<CanvasGroup>();
        UnityEngine.UI.Image shadow = NewImage("Shadow", image.rectTransform, new Color(0f, 0f, 0f, 0.35f), false);
        Place(shadow.rectTransform, BottomLeft, BottomRight, TopCenter, Vector2.zero, new Vector2(0f, 4f));
        TextMeshProUGUI text = NewText("Label", image.rectTransform, 32f, ink, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        text.text = label;
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        return button;
    }

    /// <summary>A vertical scrolling list inside <paramref name="parent"/>, inset by the offsets; returns its content.</summary>
    private static RectTransform ScrollList(RectTransform parent, Vector2 offsetMin, Vector2 offsetMax, float spacing) =>
        ScrollList(parent, offsetMin, offsetMax, spacing, out _);

    private static RectTransform ScrollList(RectTransform parent, Vector2 offsetMin, Vector2 offsetMax, float spacing, out ScrollRect scroll)
    {
        RectTransform view = NewRect("Scroll", parent);
        view.anchorMin = Vector2.zero;
        view.anchorMax = Vector2.one;
        view.offsetMin = offsetMin;
        view.offsetMax = offsetMax;
        view.gameObject.AddComponent<RectMask2D>();
        UnityEngine.UI.Image hit = NewImage("Hit", view, new Color(0f, 0f, 0f, 0f), true);
        Stretch(hit.rectTransform);

        RectTransform content = NewRect("Content", view);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll = view.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = view;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        return content;
    }

    private static void FixedSize(RectTransform rect, float width, float height)
    {
        rect.sizeDelta = new Vector2(width, height);
        LayoutElement element = rect.gameObject.AddComponent<LayoutElement>();
        element.minHeight = element.preferredHeight = height;
        element.preferredWidth = width;
        element.flexibleHeight = 0f;
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
        Edge(parent, "EdgeBottom", BottomLeft, BottomRight, new Vector2(0f, thickness / 2f), new Vector2(0f, thickness), color);
        Edge(parent, "EdgeLeft", BottomLeft, TopLeft, new Vector2(thickness / 2f, 0f), new Vector2(thickness, 0f), color);
        Edge(parent, "EdgeRight", BottomRight, TopRight, new Vector2(-thickness / 2f, 0f), new Vector2(thickness, 0f), color);
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
