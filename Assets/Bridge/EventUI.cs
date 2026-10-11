using System;
using System.Collections.Generic;
using System.Globalization;
using Data.Events;
using Data.Item;
using DG.Tweening;
using Manager;
using Promuse.Contracts.Events;

using Promuse.Net;
using TMPro;
using UI;
using UI.Sub;
using UnityEngine;
using UnityEngine.UI;

// The event screen: the events on the left, the chosen one on the right with a tab for each of
// its modules - overview, check-in, missions and milestones, shop, stages.
//
// Bridge, not Arknights: the stages tab hands a SongChart to CharSelectUI, and both live in the
// default assembly, which an asmdef can never reference.
//
// 只画服务端给的 / Draws exactly what the server answered. Every count, every limit, every
// "claimable" comes from the EventView; pressing a button asks the server, and its answer - the
// event as it now stands plus the whole player - replaces what is on screen.
//
// Built by Arknights/Events/Build Event UI. Template node names are the contract with the builder.
public class EventUI : UIBase
{
    public const string UIName = "EventUI";

    private enum Tab { Overview, CheckIn, Missions, Shop, Stages }

    private static readonly Color Selected = new Color(0.17f, 0.56f, 0.90f, 1f);
    private static readonly Color Unselected = new Color(1f, 1f, 1f, 0.12f);
    private static readonly Color Done = new Color(0.25f, 0.72f, 0.40f, 1f);
    private static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);

    [Header("数据 / The charts the stages tab plays")]
    public BeatmapLibrary library;

    [Header("顶栏 / Top bar")]
    public Button backButton;
    public GameObject currencyPill;
    public Image currencyIcon;
    public TextMeshProUGUI currencyAmount;

    [Header("活动列表 / Event list")]
    public RectTransform eventList;
    public RectTransform eventTemplate;

    [Header("标题 / Heading")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI subtitleText;
    public TextMeshProUGUI timeText;
    public TextMeshProUGUI note;

    [Header("页签 / Tabs")]
    public RectTransform tabRow;
    public RectTransform tabTemplate;

    [Header("页面 / Pages")]
    public GameObject overviewPage;
    public TextMeshProUGUI descriptionText;
    public Button bannerButton;

    public GameObject checkInPage;
    public RectTransform dayGrid;
    public Button checkInButton;
    public TextMeshProUGUI checkInLabel;
    public TextMeshProUGUI checkInNote;

    public GameObject missionsPage;
    public TextMeshProUGUI pointsText;
    public RectTransform milestoneRow;
    public RectTransform missionList;

    public GameObject shopPage;
    public RectTransform shopList;

    public GameObject stagesPage;
    public RectTransform stageList;

    [Header("模板 / Templates, cloned into the pages")]
    public RectTransform rowTemplate;
    public RectTransform cellTemplate;

    private readonly List<RectTransform> eventButtons = new List<RectTransform>();
    private readonly List<RectTransform> tabs = new List<RectTransform>();
    private readonly Dictionary<RectTransform, List<RectTransform>> pools = new Dictionary<RectTransform, List<RectTransform>>();

    private EventView view;
    private Tab tab = Tab.Overview;
    private bool busy;
    private int ticket;
    private float nextTick;

    // ------------------------------------------------------------------ lifecycle

    public override void Init()
    {
        backButton.onClick.AddListener(() => UIManager.Inst().Hide(UIName, true));
        bannerButton.onClick.AddListener(OpenBanner);
        checkInButton.onClick.AddListener(CheckIn);

        eventTemplate.gameObject.SetActive(false);
        tabTemplate.gameObject.SetActive(false);
        rowTemplate.gameObject.SetActive(false);
        cellTemplate.gameObject.SetActive(false);
    }

    public override void Show()
    {
        base.Show();
        canvasGroup.alpha = 0;
        canvasGroup.DOFade(1, 0.25f);

        LoadAsync();
    }

    public override void Hide(bool destroy = false)
    {
        canvasGroup.DOFade(0, 0.2f).OnComplete(() => base.Hide(destroy));
    }

    private void Update()
    {
        if (view == null || Time.unscaledTime < nextTick) return;
        nextTick = Time.unscaledTime + 1f;
        DrawTime();
    }

    // ---------------------------------------------------------------------- data

    private async void LoadAsync()
    {
        note.text = "LOADING...";
        ApiResult<EventList> list = await EventManager.Inst().LoadAsync();
        if (this == null) return;

        if (!list.IsSuccess)
        {
            note.text = "";
            CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, list.Message);
            return;
        }

        DrawEventList();

        EventManager events = EventManager.Inst();
        string id = events.SelectedId ?? events.Featured?.EventId;

        if (id == null)
        {
            note.text = "NO EVENT RIGHT NOW";
            return;
        }

        Select(id);
    }

    private async void Select(string eventId)
    {
        EventManager.Inst().SelectedId = eventId;
        int mine = ++ticket;

        HighlightEvent(eventId);
        note.text = "LOADING...";

        ApiResult<EventView> result = await EventManager.Inst().ViewAsync(eventId);
        if (this == null || mine != ticket) return;

        if (!result.IsSuccess)
        {
            note.text = "";
            CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, result.Message);
            return;
        }

        note.text = "";
        Draw(result.Value);
    }

    // ---------------------------------------------------------------------- draw

    private void Draw(EventView next)
    {
        view = next;
        EventDefinition d = view.Definition;

        titleText.text = d.Title;
        subtitleText.text = d.Subtitle ?? "";
        DrawTime();

        currencyPill.SetActive(d.CurrencyItemId.HasValue);
        if (d.CurrencyItemId is int currency)
        {
            currencyIcon.sprite = Icon(currency);
            currencyIcon.enabled = currencyIcon.sprite != null;
            currencyAmount.text = view.CurrencyBalance.ToString("N0", CultureInfo.InvariantCulture);
        }

        BuildTabs();
        DrawPage();
    }

    private void DrawTime()
    {
        if (view == null) return;

        EventDefinition d = view.Definition;
        bool started = EventManager.Inst().ServerNow >= d.StartsAt.UtcDateTime;
        timeText.text = started
            ? "ENDS IN " + EventManager.Inst().Until(d.EndsAt).ToUpperInvariant()
            : "STARTS IN " + EventManager.Inst().Until(d.StartsAt).ToUpperInvariant();

        if (view.CheckIn is CheckInState checkIn && !checkIn.CanClaimToday && checkIn.Claimed < view.Definition.CheckIn.Count)
        {
            checkInNote.text = "Next day in " + EventManager.Inst().Until(checkIn.NextDayAt);
        }
    }

    private void DrawEventList()
    {
        IReadOnlyList<EventSummary> events = EventManager.Inst().Events;

        while (eventButtons.Count < events.Count)
        {
            eventButtons.Add((RectTransform)Instantiate(eventTemplate, eventList));
        }

        for (int i = 0; i < eventButtons.Count; i++)
        {
            bool used = i < events.Count;
            eventButtons[i].gameObject.SetActive(used);
            if (!used) continue;

            EventSummary e = events[i];
            RectTransform b = eventButtons[i];
            b.name = "Event_" + e.EventId;
            b.Find("Title").GetComponent<TextMeshProUGUI>().text = e.Title;
            b.Find("Detail").GetComponent<TextMeshProUGUI>().text = e.IsOpen
                ? "Ends in " + EventManager.Inst().Until(e.EndsAt)
                : "Starts in " + EventManager.Inst().Until(e.StartsAt);

            Button button = b.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            string id = e.EventId;
            button.onClick.AddListener(() => { if (id != view?.EventId) Select(id); });
        }
    }

    private void HighlightEvent(string eventId)
    {
        foreach (RectTransform b in eventButtons)
        {
            if (!b.gameObject.activeSelf) continue;
            b.Find("Frame").GetComponent<Image>().color = b.name == "Event_" + eventId ? Selected : Unselected;
        }
    }

    /// <summary>One tab per module the event has; Overview always.</summary>
    private void BuildTabs()
    {
        EventDefinition d = view.Definition;
        List<(Tab, string)> wanted = new List<(Tab, string)> { (Tab.Overview, "OVERVIEW") };
        if (d.CheckIn.Count > 0) wanted.Add((Tab.CheckIn, "CHECK-IN"));
        if (d.Missions.Count > 0 || d.Milestones.Count > 0) wanted.Add((Tab.Missions, "MISSIONS"));
        if (d.Shop.Count > 0) wanted.Add((Tab.Shop, "SHOP"));
        if (d.Stages.Count > 0) wanted.Add((Tab.Stages, "STAGES"));

        if (!wanted.Exists(w => w.Item1 == tab)) tab = Tab.Overview;

        while (tabs.Count < wanted.Count) tabs.Add((RectTransform)Instantiate(tabTemplate, tabRow));

        for (int i = 0; i < tabs.Count; i++)
        {
            bool used = i < wanted.Count;
            tabs[i].gameObject.SetActive(used);
            if (!used) continue;

            (Tab which, string label) = wanted[i];
            tabs[i].Find("Label").GetComponent<TextMeshProUGUI>().text = label;
            tabs[i].Find("Frame").GetComponent<Image>().color = which == tab ? Selected : Unselected;

            Button button = tabs[i].GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                tab = which;
                BuildTabs();
                DrawPage();
            });
        }
    }

    private void DrawPage()
    {
        overviewPage.SetActive(tab == Tab.Overview);
        checkInPage.SetActive(tab == Tab.CheckIn);
        missionsPage.SetActive(tab == Tab.Missions);
        shopPage.SetActive(tab == Tab.Shop);
        stagesPage.SetActive(tab == Tab.Stages);

        switch (tab)
        {
            case Tab.Overview: DrawOverview(); break;
            case Tab.CheckIn: DrawCheckIn(); break;
            case Tab.Missions: DrawMissions(); break;
            case Tab.Shop: DrawShop(); break;
            case Tab.Stages: DrawStages(); break;
        }
    }

    private void DrawOverview()
    {
        EventDefinition d = view.Definition;
        descriptionText.text = string.IsNullOrWhiteSpace(d.Description) ? "" : d.Description;
        bannerButton.gameObject.SetActive(!string.IsNullOrEmpty(d.BannerId));
    }

    private void DrawCheckIn()
    {
        IReadOnlyList<CheckInDay> days = view.Definition.CheckIn;
        CheckInState state = view.CheckIn;
        List<RectTransform> cells = Pool(dayGrid, cellTemplate, days.Count);

        for (int i = 0; i < days.Count; i++)
        {
            CheckInDay day = days[i];
            bool taken = state != null && day.Day <= state.Claimed;
            bool next = state != null && day.Day == state.Claimed + 1;

            Cell(cells[i], "DAY " + day.Day, day.Reward,
                taken ? "RECEIVED" : next && state.CanClaimToday ? "TODAY" : "",
                taken ? Done : next ? Selected : Dim, null, null);
        }

        bool can = state != null && state.CanClaimToday;
        SetAction(checkInButton, can);
        checkInLabel.text = can ? "CHECK IN" : state != null && state.Claimed >= days.Count ? "ALL DONE" : "CHECKED IN";
        checkInNote.text = state != null && state.Claimed >= days.Count ? "Every day has been checked in." : "";
        DrawTime();
    }

    private void DrawMissions()
    {
        pointsText.text = "POINTS  " + view.Points.ToString(CultureInfo.InvariantCulture);

        List<RectTransform> cells = Pool(milestoneRow, cellTemplate, view.Milestones.Count);
        for (int i = 0; i < view.Milestones.Count; i++)
        {
            EventMilestoneState m = view.Milestones[i];
            string id = m.Milestone.RewardId;
            Cell(cells[i], m.Milestone.RequiredPoints + " PTS", m.Milestone.Reward,
                m.IsClaimed ? "RECEIVED" : "", m.IsClaimed ? Done : m.IsClaimable ? Selected : Dim,
                m.IsClaimable ? "CLAIM" : null, () => Act(EventManager.Inst().ClaimMilestoneAsync(view.EventId, id)));
        }

        List<RectTransform> rows = Pool(missionList, rowTemplate, view.Missions.Count);
        for (int i = 0; i < view.Missions.Count; i++)
        {
            EventMissionState m = view.Missions[i];
            string id = m.Mission.MissionId;
            string action = m.IsClaimed ? "CLAIMED" : m.IsComplete ? "CLAIM" : "IN PROGRESS";

            Row(rows[i], null, m.Mission.Description,
                $"{m.Progress} / {m.Mission.Target}",
                "+" + m.Mission.Points + " PTS",
                action, m.IsComplete && !m.IsClaimed && view.IsOpen,
                () => Act(EventManager.Inst().ClaimMissionAsync(view.EventId, id)));
        }
    }

    private void DrawShop()
    {
        int? currency = view.Definition.CurrencyItemId;
        List<RectTransform> rows = Pool(shopList, rowTemplate, view.Shop.Count);

        for (int i = 0; i < view.Shop.Count; i++)
        {
            EventShopState s = view.Shop[i];
            string id = s.Offer.OfferId;
            bool soldOut = s.Remaining == 0;
            bool affordable = view.CurrencyBalance >= s.Offer.Price;

            Row(rows[i], Icon(s.Offer.Sell.ItemId),
                ItemName(s.Offer.Sell.ItemId) + "  ×" + s.Offer.Sell.Amount.ToString("N0", CultureInfo.InvariantCulture),
                s.Remaining.HasValue ? s.Remaining + " left" : "No limit",
                s.Offer.Price.ToString("N0", CultureInfo.InvariantCulture) + " " + (currency.HasValue ? ItemName(currency.Value) : ""),
                soldOut ? "SOLD OUT" : "BUY", !soldOut && affordable && view.IsOpen,
                () => Act(EventManager.Inst().BuyAsync(view.EventId, id, 1)));
        }
    }

    private void DrawStages()
    {
        int? currency = view.Definition.CurrencyItemId;
        List<RectTransform> rows = Pool(stageList, rowTemplate, view.Definition.Stages.Count);

        for (int i = 0; i < view.Definition.Stages.Count; i++)
        {
            EventStage stage = view.Definition.Stages[i];
            SongChart chart = Chart(stage.StageId);

            string title = chart != null
                ? (string.IsNullOrEmpty(chart.songName) ? chart.songId : chart.songName) + "   LV." + chart.difficulty
                : stage.StageId;

            Row(rows[i], currency.HasValue ? Icon(currency.Value) : null, title,
                chart != null ? $"{chart.notes.Count} notes" : "Not in this build",
                "+" + stage.CurrencyPerClear.ToString("N0", CultureInfo.InvariantCulture) + " per clear",
                "PLAY", chart != null && view.IsOpen, () => Play(chart));
        }
    }

    // ----------------------------------------------------------------- widgets

    private List<RectTransform> Pool(RectTransform parent, RectTransform template, int count)
    {
        if (!pools.TryGetValue(parent, out List<RectTransform> pool))
        {
            pool = new List<RectTransform>();
            pools[parent] = pool;
        }

        while (pool.Count < count) pool.Add((RectTransform)Instantiate(template, parent));

        for (int i = 0; i < pool.Count; i++) pool[i].gameObject.SetActive(i < count);
        return pool;
    }

    private void Row(RectTransform row, Sprite icon, string title, string detail, string right,
                     string action, bool enabled, Action press)
    {
        Image image = row.Find("Icon").GetComponent<Image>();
        image.sprite = icon;
        image.enabled = icon != null;

        row.Find("Title").GetComponent<TextMeshProUGUI>().text = title;
        row.Find("Detail").GetComponent<TextMeshProUGUI>().text = detail;
        row.Find("Right").GetComponent<TextMeshProUGUI>().text = right;

        Button button = row.Find("Action").GetComponent<Button>();
        button.transform.Find("Label").GetComponent<TextMeshProUGUI>().text = action;
        SetAction(button, enabled);

        button.onClick.RemoveAllListeners();
        if (enabled) button.onClick.AddListener(() => press());
    }

    private void Cell(RectTransform cell, string header, EventReward reward, string state, Color frame,
                      string action, Action press)
    {
        cell.Find("Header").GetComponent<TextMeshProUGUI>().text = header;
        cell.Find("Frame").GetComponent<Image>().color = frame;

        Image image = cell.Find("Icon").GetComponent<Image>();
        image.sprite = Icon(reward.ItemId);
        image.enabled = image.sprite != null;

        cell.Find("Amount").GetComponent<TextMeshProUGUI>().text =
            (image.enabled ? "" : ItemName(reward.ItemId) + " ") + "×" + reward.Amount.ToString("N0", CultureInfo.InvariantCulture);
        cell.Find("State").GetComponent<TextMeshProUGUI>().text = state;

        Button button = cell.Find("Action").GetComponent<Button>();
        button.gameObject.SetActive(action != null);
        button.onClick.RemoveAllListeners();
        if (action != null)
        {
            button.transform.Find("Label").GetComponent<TextMeshProUGUI>().text = action;
            button.onClick.AddListener(() => press());
        }
    }

    private static void SetAction(Button button, bool enabled)
    {
        button.interactable = enabled;
        CanvasGroup group = button.GetComponent<CanvasGroup>();
        if (group != null) group.alpha = enabled ? 1f : 0.45f;
    }

    // ------------------------------------------------------------------ actions

    private void CheckIn()
    {
        if (view != null) Act(EventManager.Inst().CheckInAsync(view.EventId));
    }

    /// <summary>
    /// 一次只办一件事 / One action at a time: a second press while the first is out would be a
    /// second request with its own idempotency key - a second check-in attempt, a second purchase.
    /// </summary>
    private async void Act(System.Threading.Tasks.Task<ApiResult<EventActionResult>> call)
    {
        if (busy) return;
        busy = true;

        ApiResult<EventActionResult> result;
        try
        {
            result = await call;
        }
        finally
        {
            busy = false;
        }

        if (this == null) return;

        if (!result.IsSuccess)
        {
            CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, result.Message);
            return;
        }

        Draw(result.Value.Event);

        if (result.Value.Granted.Count > 0)
        {
            List<string> parts = new List<string>();
            foreach (Promuse.Contracts.Players.ItemStack stack in result.Value.Granted)
            {
                parts.Add(ItemName(stack.ItemId) + " ×" + stack.Amount.ToString("N0", CultureInfo.InvariantCulture));
            }
            CommonDialogUI.Message(CommonDialogUI.GroundType.WHITE, "Received: " + string.Join(", ", parts));
        }
    }

    private void OpenBanner()
    {
        if (view?.Definition.BannerId != null) GachaUI.OpenOn(view.Definition.BannerId);
    }

    /// <summary>
    /// Plays an event stage as a ranked run - the currency is paid by the server when the run
    /// closes - and comes back here afterwards rather than to the song list.
    /// </summary>
    private void Play(SongChart chart)
    {
        if (chart == null) return;

        if (!RemoteConfigManager.Inst().IsOn(f => f.Ranked))
        {
            CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK,
                RemoteConfigManager.Unavailable("Ranked play") + " Event stages need it to pay out.");
            return;
        }

        SongSession.SetReturn(UIName);
        CharSelectUI.Show(chart, PlayMode.Ranked);
    }

    // ------------------------------------------------------------------ lookups

    private SongChart Chart(string stageId)
    {
        if (library == null) return null;
        foreach (SongChart chart in library.charts)
        {
            if (chart != null && chart.stageId == stageId) return chart;
        }
        return null;
    }

    private static Sprite Icon(int itemId)
    {
        try
        {
            ItemMeta meta = ItemManager.Inst().GetMeta(itemId);
            return meta != null ? meta.GetIcon() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string ItemName(int itemId)
    {
        try
        {
            ItemMeta meta = ItemManager.Inst().GetMeta(itemId);
            return meta != null ? meta.GetName() : "Item #" + itemId;
        }
        catch (Exception)
        {
            return "Item #" + itemId;
        }
    }
}
