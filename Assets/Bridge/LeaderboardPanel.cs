using System.Collections.Generic;
using System.Globalization;
using Data.Player;
using DG.Tweening;
using Manager;
using Promuse.Contracts.Leaderboards;
using Promuse.Net;
using TMPro;
using Tools;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 排行榜面板 / The leaderboard beside the song list: one stage - one difficulty - at a time,
/// with a WEEKLY and an ALL-TIME tab, the top ten, and the player's own line pinned underneath
/// wherever they stand.
///
/// 每个难度一张榜 / Every difficulty of a song is its own chart with its own stageId, so picking a
/// difficulty is what picks the board. Nothing here knows about songs.
///
/// 旧的回答不能盖住新的 / A load can still be in flight when the player picks another difficulty,
/// and the slower answer may arrive second. Every load takes a ticket and only the latest one is
/// allowed to draw, so a board never shows one chart's scores under another chart's name.
///
/// 节点名字是绑定的一部分 / The node paths looked up below are built by SongSelectUISetup and are
/// load-bearing: renaming one there breaks the binding here.
/// </summary>
public sealed class LeaderboardPanel
{
    public const int PageSize = 10;

    private static readonly Color TabOn = new Color(0.17f, 0.56f, 0.90f, 0.95f);
    private static readonly Color TabOff = new Color(1f, 1f, 1f, 0.08f);
    private static readonly Color RowIdle = new Color(1f, 1f, 1f, 0.05f);
    private static readonly Color RowMine = new Color(0.17f, 0.56f, 0.90f, 0.40f);
    private static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);

    // 记住上次的页签 / Kept for the session, so moving between songs keeps the board the player
    // last chose rather than snapping back to the first tab every time.
    private static LeaderboardPeriod period = LeaderboardPeriod.Weekly;

    private readonly GameObject root;
    private readonly CanvasGroup group;
    private readonly TextMeshProUGUI chartLabel;
    private readonly TextMeshProUGUI note;
    private readonly Image weeklyTab;
    private readonly Image allTimeTab;
    private readonly RectTransform rowTemplate;
    private readonly Row me;
    private readonly List<Row> rows = new List<Row>();

    private string stageId;
    private int ticket;

    public LeaderboardPanel(Transform transform)
    {
        root = transform.gameObject;
        group = transform.GetComponent<CanvasGroup>();

        chartLabel = transform.GetComponent<TextMeshProUGUI>("Header/Chart");
        note = transform.GetComponent<TextMeshProUGUI>("Note");

        weeklyTab = transform.GetComponent<Image>("Tabs/WeeklyTab");
        allTimeTab = transform.GetComponent<Image>("Tabs/AllTimeTab");
        weeklyTab.GetComponent<Button>().onClick.AddListener(() => Switch(LeaderboardPeriod.Weekly));
        allTimeTab.GetComponent<Button>().onClick.AddListener(() => Switch(LeaderboardPeriod.AllTime));

        rowTemplate = (RectTransform)transform.Find("Rows/RowTemplate");
        rowTemplate.gameObject.SetActive(false);

        me = new Row(transform.Find("Me"));

        root.SetActive(false);
    }

    /// <summary>Opens the panel on a chart, or moves it to another one.</summary>
    public void Show(string stage, string caption)
    {
        // 先停掉还在跑的淡出 / Kill a fade-out still running first. Picking a song with a single
        // difficulty hides and reshows the board in the same frame, and the hide's fade would
        // otherwise finish a moment later and switch off the board that was just opened.
        group.DOKill();

        if (!root.activeSelf)
        {
            group.alpha = 0f;
            root.SetActive(true);
        }

        if (group.alpha < 1f) group.DOFade(1f, 0.2f);

        stageId = stage;
        chartLabel.text = caption;
        Load();
    }

    public void Hide()
    {
        stageId = null;

        // 作废还在路上的 / Anything still loading is now for a panel nobody is looking at.
        ticket++;

        if (root != null && root.activeSelf)
        {
            group.DOKill();
            group.DOFade(0f, 0.15f).OnComplete(() =>
            {
                if (root != null) root.SetActive(false);
            });
        }
    }

    private void Switch(LeaderboardPeriod to)
    {
        if (period == to) return;

        period = to;
        Load();
    }

    private async void Load()
    {
        if (string.IsNullOrEmpty(stageId)) return;

        int mine = ++ticket;
        string stage = stageId;

        PaintTabs();
        DrawRows(null, null);

        if (!RemoteConfigManager.Inst().IsOn(f => f.Leaderboards))
        {
            note.text = "LEADERBOARD UNAVAILABLE";
            return;
        }

        note.text = "LOADING...";

        ApiResult<LeaderboardPage> result =
            await PlayerManager.Inst().Api.GetLeaderboardAsync(stage, period, PageSize);

        // 界面没了, 或者已经有更新的请求 / The screen may be gone, or a newer load may have been
        // started while this one was out. Either way this answer is no longer the one to show.
        if (root == null || mine != ticket) return;

        if (!result.IsSuccess)
        {
            Debug.LogWarning($"[LeaderboardPanel] {stage} {period}: {result.Message}");
            note.text = "LEADERBOARD UNAVAILABLE";
            return;
        }

        LeaderboardPage page = result.Value;

        DrawRows(page.Entries, page.Me);
        note.text = page.Entries.Count == 0 ? "NO SCORES YET - BE THE FIRST" : Countdown(page);
    }

    private void PaintTabs()
    {
        weeklyTab.color = period == LeaderboardPeriod.Weekly ? TabOn : TabOff;
        allTimeTab.color = period == LeaderboardPeriod.AllTime ? TabOn : TabOff;
    }

    /// <summary>
    /// 用服务端时间算 / Counted from the server's clock, never the device's: the device clock is
    /// the player's to change, and a countdown is only honest if it ends when the board does.
    /// </summary>
    private static string Countdown(LeaderboardPage page)
    {
        if (page.Period != LeaderboardPeriod.Weekly || page.ResetsAt == null) return "PERSONAL BESTS OF ALL TIME";

        System.TimeSpan left = page.ResetsAt.Value - page.ServerTime;
        if (left < System.TimeSpan.Zero) left = System.TimeSpan.Zero;

        return left.TotalDays >= 1
            ? $"RESETS IN {(int)left.TotalDays}D {left.Hours}H"
            : $"RESETS IN {left.Hours}H {left.Minutes}M";
    }

    // ------------------------------------------------------------------- rows

    /// <summary>
    /// The page, then the player's own line. When they are on the page their row is highlighted
    /// and the pinned line is hidden, so the same score is never listed twice.
    /// </summary>
    private void DrawRows(IReadOnlyList<LeaderboardEntry> entries, LeaderboardEntry mine)
    {
        int count = entries != null ? entries.Count : 0;
        bool mineOnPage = false;

        while (rows.Count < count)
        {
            rows.Add(new Row(Object.Instantiate(rowTemplate, rowTemplate.parent)));
        }

        for (int i = 0; i < rows.Count; i++)
        {
            bool used = i < count;
            rows[i].SetActive(used);
            if (!used) continue;

            bool isMine = mine != null && entries[i].PlayerId == mine.PlayerId;
            mineOnPage |= isMine;

            rows[i].Draw(entries[i].Rank.ToString(CultureInfo.InvariantCulture), entries[i], isMine ? RowMine : RowIdle);
        }

        if (entries == null)
        {
            me.SetActive(false);
            return;
        }

        if (mine == null)
        {
            me.SetActive(true);
            me.DrawEmpty("-", "YOU - NO SCORE HERE YET");
            return;
        }

        me.SetActive(!mineOnPage);
        if (!mineOnPage) me.Draw("#" + mine.Rank.ToString(CultureInfo.InvariantCulture), mine, RowMine);
    }

    private sealed class Row
    {
        private readonly GameObject root;
        private readonly Image background;
        private readonly TextMeshProUGUI rank;
        private readonly TextMeshProUGUI name;
        private readonly TextMeshProUGUI score;
        private readonly TextMeshProUGUI character;

        internal Row(Transform row)
        {
            root = row.gameObject;
            background = row.GetComponent<Image>();
            rank = row.GetComponent<TextMeshProUGUI>("Rank");
            name = row.GetComponent<TextMeshProUGUI>("Name");
            score = row.GetComponent<TextMeshProUGUI>("Score");
            character = row.GetComponent<TextMeshProUGUI>("Operator");
            root.SetActive(true);
        }

        internal void SetActive(bool value) => root.SetActive(value);

        internal void Draw(string rankText, LeaderboardEntry entry, Color tint)
        {
            background.color = tint;
            rank.text = rankText;
            name.text = entry.DisplayName;
            name.color = Color.white;
            score.text = entry.Score.ToString("N0", CultureInfo.InvariantCulture);
            character.text = entry.CharacterId ?? "";
        }

        internal void DrawEmpty(string rankText, string message)
        {
            background.color = RowIdle;
            rank.text = rankText;
            name.text = message;
            name.color = Dim;
            score.text = "";
            character.text = "";
        }
    }
}
