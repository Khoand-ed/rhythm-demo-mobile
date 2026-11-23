using System.Collections.Generic;
using Data.Char;
using Data.Item;
using Promuse.Contracts.Leaderboards;
using Promuse.Contracts.Runs;
using Promuse.Net;
using ItemStack = Promuse.Contracts.Players.ItemStack;
using TMPro;
using UI;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 结算页 / The parts of the results screen GameManager does not fill itself: the operator behind
/// everything, the song line, EARLY/LATE, the rank badge's ring and tags, HI-SCORE with NEW BEST,
/// and the rewards - a line of EXP and level and a row of item cells.
///
/// GameManager still writes the counts, the accuracy, the rank, the score and the ranking line into
/// its own fields, which point into this screen's groups; this only adds what came after them.
///
/// 节点由工具建 / Built by Tools/Rhythm/Build Results Screen, which converts the scene's Results panel
/// in place. The cell template's child names (Ground, Ground/Icon, Ground/Amount, Ground/FirstTag,
/// Name) are what <see cref="AddCell"/> binds to, so renaming one there breaks it here.
///
/// 只画 / Presentation only. Nothing here reads or moves RunState; the server never sees any of it.
/// </summary>
public class ResultsScreen : MonoBehaviour
{
    /// <summary>What GameManager knows when the screen opens.</summary>
    public struct Summary
    {
        public string songName;
        public string difficulty;
        public CharMeta operatorMeta;
        public string rank;
        public int early;
        public int late;
        public bool failed;
        public bool fullCombo;

        // 有就去取最好成绩 / The stage whose best to fetch for HI-SCORE. Null for practice and for a
        // scene opened straight from the Editor, which show a dash.
        public string hiScoreStage;
    }

    public const string Dash = "—";
    public const int OrundumId = 1;
    public const int PrimeId = 0;

    // 两行六格 / Two rows of six fill the space the right block keeps for them.
    public const int MaxCells = 12;

    private static readonly Color Gold = new Color(0.96f, 0.73f, 0.05f);
    private static readonly Color NoGround = new Color(0.22f, 0.23f, 0.26f, 1f);

    [Header("立绘 / The operator, behind everything")]
    [Tooltip("A SplashFrame's Image (Figure mode). Empty when no operator was chosen.")]
    public Image operatorArt;

    [Header("左 / Song and timing")]
    public TextMeshProUGUI songNameText;
    public TextMeshProUGUI difficultyText;
    public TextMeshProUGUI earlyText;
    public TextMeshProUGUI lateText;

    [Header("中 / Rank badge and tags")]
    [Tooltip("Tinted by the rank letter.")]
    public Image rankRing;
    public GameObject fullComboTag;
    public GameObject failedTag;

    [Header("右 / HI-SCORE")]
    public TextMeshProUGUI hiScoreText;
    public GameObject newBestTag;

    [Header("奖励 / Rewards")]
    [Tooltip("EXP, LEVEL UP, or what the rewards are waiting on. The same node as GameManager.rewardsText.")]
    public TextMeshProUGUI rewardsLine;
    public RectTransform rewardsRow;
    [Tooltip("Copied once per item; kept switched off.")]
    public RectTransform cellTemplate;

    private readonly List<GameObject> cells = new List<GameObject>();

    // 最好成绩 / The best the leaderboard holds, and the score the review just made the best. Shown as
    // the higher of the two that are known, whichever arrives first.
    private int? fetchedBest;
    private int? reviewedBest;

    // 旧的回答不能落到新的一页 / Every fetch takes a ticket and Clear() moves it on, so an answer for an
    // attempt the player has already retried is dropped instead of drawn.
    private int ticket;

    // ------------------------------------------------------------------ calls

    /// <summary>The screen opens: everything here for this attempt, the rewards waiting.</summary>
    public void Show(Summary summary)
    {
        Clear();

        Set(songNameText, summary.songName);
        Set(difficultyText, summary.difficulty);
        Set(earlyText, summary.early.ToString());
        Set(lateText, summary.late.ToString());

        // 没有干员就空着 / No operator, no art: SplashFrame switches the splash off for a null meta.
        if (operatorArt != null) SplashFrame.Show(operatorArt, summary.operatorMeta);

        if (rankRing != null) rankRing.color = RankColour(summary.rank);
        if (fullComboTag != null) fullComboTag.SetActive(summary.fullCombo);
        if (failedTag != null) failedTag.SetActive(summary.failed);

        FetchHiScore(summary.hiScoreStage);
    }

    /// <summary>
    /// 服务端的结论 / What the review said: NEW BEST when the server made this run the player's all-time
    /// entry, and HI-SCORE moves up to it. A flagged or rejected run is not on the board and moves nothing.
    /// </summary>
    public void SetRanking(ScoreReview review)
    {
        bool best = review != null && review.Verdict == ScoreVerdict.Accepted && review.IsPersonalBest;

        if (newBestTag != null) newBestTag.SetActive(best);
        if (best) reviewedBest = review.Score;

        PaintHiScore();
    }

    /// <summary>A known best from somewhere other than the board, or null for a dash.</summary>
    public void SetHiScore(int? best)
    {
        fetchedBest = best;
        PaintHiScore();
    }

    /// <summary>What the rewards are waiting on, or why there are none - the row stays empty.</summary>
    public void SetRewardsStatus(string text)
    {
        ClearCells();
        Set(rewardsLine, text);
    }

    /// <summary>
    /// 这局给了什么 / What the run paid, in the order the server pays it: the first clear's Orundum, what
    /// else the first clear paid, the drops, then the event's currency - each a cell - with the EXP and a
    /// level up on the line above.
    /// </summary>
    public void SetRewards(RunRewards rewards)
    {
        ClearCells();

        if (rewards == null)
        {
            Set(rewardsLine, "No rewards");
            return;
        }

        if (rewards.FirstClearOrundum > 0) AddCell(OrundumId, rewards.FirstClearOrundum, true);
        AddCells(rewards.FirstClearItems, true);
        AddCells(rewards.Drops, false);
        AddCells(rewards.EventItems, false);

        string line = rewards.Exp > 0 ? "+" + rewards.Exp + " EXP" : "";

        if (rewards.LevelAfter > rewards.LevelBefore)
        {
            string level = "<color=#F5BA0D>LEVEL UP</color> " + rewards.LevelBefore + " → " + rewards.LevelAfter +
                           (rewards.PrimeFromLevels > 0 ? "  ·  +" + rewards.PrimeFromLevels + " " + ItemVisual.Name(PrimeId) : "") +
                           "  ·  Sanity refilled";
            line = line.Length > 0 ? line + "\n" + level : level;
        }

        if (line.Length == 0 && cells.Count == 0) line = "No rewards";

        Set(rewardsLine, line);
    }

    /// <summary>Back to empty, for a retry: nothing of the last attempt stays, and its answers are dropped.</summary>
    public void Clear()
    {
        ticket++;
        fetchedBest = null;
        reviewedBest = null;

        ClearCells();
        Set(rewardsLine, "");
        Set(hiScoreText, "");
        Set(earlyText, "");
        Set(lateText, "");

        if (newBestTag != null) newBestTag.SetActive(false);
        if (fullComboTag != null) fullComboTag.SetActive(false);
        if (failedTag != null) failedTag.SetActive(false);
        if (cellTemplate != null) cellTemplate.gameObject.SetActive(false);
    }

    // ---------------------------------------------------------------- HI-SCORE

    /// <summary>
    /// 榜上我的那一行 / The player's own line on the stage's all-time board - its score is their best.
    /// Asked once per results screen; a dash when there is nothing to ask, the board is switched off,
    /// the request fails, or the player has never ranked on this stage.
    /// </summary>
    private async void FetchHiScore(string stageId)
    {
        int mine = ticket;
        PaintHiScore();

        if (string.IsNullOrEmpty(stageId)) return;
        if (!Manager.RemoteConfigManager.Inst().IsOn(f => f.Leaderboards)) return;

        ApiResult<LeaderboardPage> page;
        try
        {
            page = await Data.Player.PlayerManager.Inst().Api.GetLeaderboardAsync(stageId, LeaderboardPeriod.AllTime, 1);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[ResultsScreen] Could not read the best score: " + e.Message);
            return;
        }

        // 界面没了, 或者已经重来 / The scene may be gone, or the player has retried since.
        if (this == null || mine != ticket) return;

        if (!page.IsSuccess)
        {
            Debug.LogWarning("[ResultsScreen] Could not read the best score: " + page.Message);
            return;
        }

        if (page.Value != null && page.Value.Me != null) fetchedBest = page.Value.Me.Score;
        PaintHiScore();
    }

    private void PaintHiScore()
    {
        if (hiScoreText == null) return;

        int? best = fetchedBest;
        if (reviewedBest.HasValue && (!best.HasValue || reviewedBest.Value > best.Value)) best = reviewedBest;

        hiScoreText.text = "<color=#FFFFFF8C>HI-SCORE</color>   " + (best.HasValue ? best.Value.ToString() : Dash);
    }

    // ------------------------------------------------------------------ cells

    private void AddCells(IReadOnlyList<ItemStack> stacks, bool firstClear)
    {
        if (stacks == null) return;

        foreach (ItemStack stack in stacks) AddCell(stack.ItemId, stack.Amount, firstClear);
    }

    /// <summary>
    /// One item: its rarity ground, its icon, "×amount" and its name. An item without a meta still
    /// gets a cell - a plain ground, no icon, "Item #id" - rather than vanishing from what was paid.
    /// </summary>
    private void AddCell(int itemId, int amount, bool firstClear)
    {
        if (rewardsRow == null || cellTemplate == null || amount <= 0) return;

        if (cells.Count >= MaxCells)
        {
            Debug.LogWarning($"[ResultsScreen] More than {MaxCells} rewards; item {itemId} x{amount} is not shown.");
            return;
        }

        RectTransform cell = Instantiate(cellTemplate, rewardsRow, false);
        cell.name = "Cell" + cells.Count;
        cell.gameObject.SetActive(true);
        cells.Add(cell.gameObject);

        Image ground = Find<Image>(cell, "Ground");
        if (ground != null)
        {
            Sprite sprite = Ground(itemId);
            ground.sprite = sprite;
            ground.color = sprite != null ? Color.white : NoGround;
        }

        Image icon = Find<Image>(cell, "Ground/Icon");
        if (icon != null)
        {
            Sprite sprite = ItemVisual.Icon(itemId);
            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }

        Set(Find<TextMeshProUGUI>(cell, "Ground/Amount"), "×" + ItemVisual.Amount(amount));
        Set(Find<TextMeshProUGUI>(cell, "Name"), ItemVisual.Name(itemId));

        Transform first = cell.Find("Ground/FirstTag");
        if (first != null) first.gameObject.SetActive(firstClear);
    }

    private void ClearCells()
    {
        foreach (GameObject cell in cells)
        {
            if (cell != null) Destroy(cell);
        }

        cells.Clear();
    }

    /// <summary>The rarity's ground, the depot's own; null for an item with no meta.</summary>
    private static Sprite Ground(int itemId)
    {
        try
        {
            ItemMeta meta = ItemManager.Inst().GetMeta(itemId);
            return meta != null ? ItemManager.Inst().GetItemGround(meta.GetRarity()) : null;
        }
        catch (System.Exception)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- helpers

    private static Color RankColour(string rank)
    {
        switch (rank)
        {
            case "S": return Gold;
            case "A": return new Color(1f, 0.55f, 0.25f);
            case "B": return new Color(0.40f, 0.78f, 1f);
            case "C": return new Color(0.45f, 0.85f, 0.50f);
            case "D": return new Color(0.70f, 0.70f, 0.74f);
            default: return new Color(0.92f, 0.30f, 0.30f);
        }
    }

    private static T Find<T>(Transform root, string path) where T : Component
    {
        Transform node = root.Find(path);
        return node != null ? node.GetComponent<T>() : null;
    }

    private static void Set(TextMeshProUGUI text, string value)
    {
        if (text != null) text.text = value ?? "";
    }
}
