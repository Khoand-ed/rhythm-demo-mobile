using System;
using System.Collections.Generic;
using System.Globalization;
using Data.Campaign;
using Data.Story;
using DG.Tweening;
using Manager;
using Promuse.Contracts.Campaign;
using Promuse.Net;
using TMPro;
using UI;
using UI.Sub;
using UnityEngine;
using UnityEngine.UI;

// The main campaign: chapters on the left, the chosen chapter's stages on the right, played in
// order. Each stage shows what its first clear pays and what it may drop; its story plays before
// the stage the first time and after its first clear, and the STORY button replays both.
//
// Bridge, not Arknights: a stage hands a SongChart to CharSelectUI, and both live in the default
// assembly. SONG LIST opens the old song map, kept for practice and the leaderboards.
//
// 只画服务端给的 / Draws what the server answered: which stages are open, cleared, what they pay.
// Built by Arknights/Campaign/Build Campaign UI. Template node names are the contract with it.
public class CampaignUI : UIBase
{
    public const string UIName = "CampaignUI";

    private static readonly Color Selected = new Color(0.98f, 0.78f, 0.25f, 1f);
    private static readonly Color Unselected = new Color(1f, 1f, 1f, 0.12f);
    private static readonly Color Common = new Color(0.75f, 0.76f, 0.78f, 1f);
    private static readonly Color Uncommon = new Color(0.35f, 0.65f, 0.95f, 1f);
    private static readonly Color Rare = new Color(0.98f, 0.78f, 0.25f, 1f);

    [Header("数据 / The charts the stages play")]
    public BeatmapLibrary library;

    [Header("顶栏 / Top bar")]
    public Button backButton;
    public Button songListButton;

    [Header("章节 / Chapters")]
    public RectTransform chapterList;
    public RectTransform chapterTemplate;

    [Header("关卡 / Stages")]
    public TextMeshProUGUI chapterTitle;
    public TextMeshProUGUI note;
    public RectTransform stageList;
    public RectTransform stageTemplate;

    /// <summary>
    /// 打完再演 / The stage just played from here, kept across the song, so its after-story plays when
    /// this screen comes back and the stage reads cleared. Static: the song is another scene.
    /// </summary>
    private static string playedStage;

    private readonly List<RectTransform> chapters = new List<RectTransform>();
    private readonly List<RectTransform> stages = new List<RectTransform>();

    // ------------------------------------------------------------------ lifecycle

    public override void Init()
    {
        backButton.onClick.AddListener(() => UIManager.Inst().Hide(UIName, true));
        songListButton.onClick.AddListener(() => UIManager.Inst().Show(SongSelectUI.UIName));
        chapterTemplate.gameObject.SetActive(false);
        stageTemplate.gameObject.SetActive(false);
    }

    public override void Show()
    {
        base.Show();
        canvasGroup.alpha = 0;
        canvasGroup.DOFade(1, 0.25f);

        CampaignManager.Inst().Changed -= Draw;
        CampaignManager.Inst().Changed += Draw;
        Draw();
        LoadAsync();
    }

    public override void Hide(bool destroy = false)
    {
        CampaignManager.Inst().Changed -= Draw;
        canvasGroup.DOFade(0, 0.2f).OnComplete(() => base.Hide(destroy));
    }

    private void OnDestroy()
    {
        CampaignManager.Inst().Changed -= Draw;
    }

    private async void LoadAsync()
    {
        if (CampaignManager.Inst().View == null) note.text = "LOADING...";

        ApiResult<CampaignView> result = await CampaignManager.Inst().LoadAsync();
        if (this == null) return;

        if (!result.IsSuccess)
        {
            note.text = "";
            CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, result.Message);
            return;
        }

        PlayAfterStory();
    }

    // ------------------------------------------------------------------ draw

    private void Draw()
    {
        if (this == null) return;

        CampaignView view = CampaignManager.Inst().View;
        if (view == null) return;
        note.text = view.Chapters.Count == 0 ? "No chapters yet." : "";

        CampaignChapterState current = Current(view);
        if (current != null) CampaignManager.Inst().SelectedChapter = current.ChapterId;

        // ---- chapters
        while (chapters.Count < view.Chapters.Count) chapters.Add((RectTransform)Instantiate(chapterTemplate, chapterList));
        for (int i = 0; i < chapters.Count; i++)
        {
            bool used = i < view.Chapters.Count;
            chapters[i].gameObject.SetActive(used);
            if (!used) continue;

            CampaignChapterState chapter = view.Chapters[i];
            int cleared = 0;
            foreach (CampaignStageState s in chapter.Stages) if (s.IsCleared) cleared++;

            RectTransform b = chapters[i];
            b.Find("Title").GetComponent<TextMeshProUGUI>().text = chapter.Title;
            b.Find("Progress").GetComponent<TextMeshProUGUI>().text =
                chapter.IsUnlocked ? $"{cleared} / {chapter.Stages.Count} cleared" : "LOCKED";
            b.Find("Frame").GetComponent<UnityEngine.UI.Image>().color = chapter == current ? Selected : Unselected;
            b.GetComponent<CanvasGroup>().alpha = chapter.IsUnlocked ? 1f : 0.5f;

            Button button = b.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            string id = chapter.ChapterId;
            bool open = chapter.IsUnlocked;
            button.onClick.AddListener(() =>
            {
                if (!open)
                {
                    CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, "Clear the chapter before it to open this one.");
                    return;
                }
                CampaignManager.Inst().SelectedChapter = id;
                Draw();
            });
        }

        // ---- the chapter's stages
        chapterTitle.text = current != null ? current.Title : "";
        int count = current != null ? current.Stages.Count : 0;
        while (stages.Count < count) stages.Add((RectTransform)Instantiate(stageTemplate, stageList));
        for (int i = 0; i < stages.Count; i++)
        {
            bool used = i < count;
            stages[i].gameObject.SetActive(used);
            if (used) DrawStage(stages[i], current.Stages[i]);
        }
    }

    /// <summary>The chapter chosen before, if it is still there and open; else the last open one.</summary>
    private static CampaignChapterState Current(CampaignView view)
    {
        string wanted = CampaignManager.Inst().SelectedChapter;
        CampaignChapterState lastOpen = null;
        foreach (CampaignChapterState c in view.Chapters)
        {
            if (c.ChapterId == wanted && c.IsUnlocked) return c;
            if (c.IsUnlocked) lastOpen = c;
        }
        return lastOpen ?? (view.Chapters.Count > 0 ? view.Chapters[0] : null);
    }

    private void DrawStage(RectTransform row, CampaignStageState state)
    {
        CampaignStage stage = state.Stage;
        SongChart chart = Chart(stage.ChartId);

        row.Find("Code").GetComponent<TextMeshProUGUI>().text = stage.Code;
        row.Find("Title").GetComponent<TextMeshProUGUI>().text = chart != null
            ? (string.IsNullOrEmpty(chart.songName) ? chart.songId : chart.songName) + "   LV." + chart.difficulty
            : stage.ChartId;
        row.Find("Detail").GetComponent<TextMeshProUGUI>().text =
            chart == null ? "Not in this build"
            : !state.IsUnlocked ? "Clear the stage before it to unlock"
            : state.IsCleared ? "CLEARED"
            : stage.FirstClearOrundum > 0 ? $"FIRST CLEAR  +{stage.FirstClearOrundum} {ItemVisual.Name(1)}"
            : "";
        row.Find("Cost").GetComponent<TextMeshProUGUI>().text = "SANITY " + state.StaminaCost.ToString(CultureInfo.InvariantCulture);
        row.GetComponent<CanvasGroup>().alpha = state.IsUnlocked ? 1f : 0.55f;

        DrawDrops((RectTransform)row.Find("Drops"), stage.Drops);

        // ---- STORY: the before-story once open, the after-story once cleared
        string before = state.IsUnlocked ? stage.StoryBefore : null;
        string after = state.IsCleared ? stage.StoryAfter : null;
        bool story = StoryManager.Exists(before) || StoryManager.Exists(after);
        Button storyButton = row.Find("Story").GetComponent<Button>();
        storyButton.gameObject.SetActive(story);
        storyButton.onClick.RemoveAllListeners();
        if (story) storyButton.onClick.AddListener(() => StoryUI.Play(before, () => StoryUI.Play(after, null)));

        // ---- PLAY
        Button play = row.Find("Action").GetComponent<Button>();
        play.transform.Find("Label").GetComponent<TextMeshProUGUI>().text = state.IsUnlocked ? "PLAY" : "LOCKED";
        bool enabled = chart != null && state.IsUnlocked;
        play.interactable = enabled;
        CanvasGroup group = play.GetComponent<CanvasGroup>();
        if (group != null) group.alpha = enabled ? 1f : 0.45f;
        play.onClick.RemoveAllListeners();
        if (enabled) play.onClick.AddListener(() => Play(state, chart));
    }

    /// <summary>What a stage may drop: the item and how likely, in words - the odds themselves are the server's.</summary>
    private static void DrawDrops(RectTransform drops, IReadOnlyList<CampaignDrop> list)
    {
        Transform template = drops.Find("DropTemplate");
        template.gameObject.SetActive(false);

        int have = drops.childCount - 1;
        for (int i = have; i < list.Count; i++) Instantiate(template, drops).name = "Drop";

        int at = 0;
        for (int i = 0; i < drops.childCount; i++)
        {
            Transform child = drops.GetChild(i);
            if (child == template) continue;

            bool used = at < list.Count;
            child.gameObject.SetActive(used);
            if (used)
            {
                CampaignDrop drop = list[at];
                Sprite icon = ItemVisual.Icon(drop.ItemId);
                UnityEngine.UI.Image image = child.Find("Icon").GetComponent<UnityEngine.UI.Image>();
                image.sprite = icon;
                image.enabled = icon != null;
                TextMeshProUGUI label = child.Find("Rarity").GetComponent<TextMeshProUGUI>();
                label.text = icon == null ? ItemVisual.Name(drop.ItemId) : drop.Rarity.ToString().ToUpperInvariant();
                label.color = drop.Rarity == DropRarity.Rare ? Rare : drop.Rarity == DropRarity.Uncommon ? Uncommon : Common;
            }
            at++;
        }
    }

    // ------------------------------------------------------------------ play

    /// <summary>
    /// Plays the stage as a ranked run of the campaign - the server clears it and pays it when the run
    /// closes - and comes back here afterwards. Its before-story plays first, the first time.
    /// </summary>
    private void Play(CampaignStageState state, SongChart chart)
    {
        if (!RemoteConfigManager.Inst().IsOn(f => f.Ranked))
        {
            CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK,
                RemoteConfigManager.Unavailable("Ranked play") + " Campaign stages need it to count.");
            return;
        }

        string stageId = state.Stage.StageId;
        playedStage = stageId;

        string before = state.Stage.StoryBefore;
        bool first = !string.IsNullOrEmpty(before) && !StoryManager.Inst().HasSeen(before);

        StoryUI.Play(first ? before : null, () =>
        {
            SongSession.SetReturn(UIName, null, stageId);
            CharSelectUI.Show(chart, PlayMode.Ranked);
        });
    }

    /// <summary>Back from a stage played here: if it now reads cleared, its after-story plays once.</summary>
    private void PlayAfterStory()
    {
        CampaignView view = CampaignManager.Inst().View;
        if (playedStage == null || view == null) return;

        foreach (CampaignChapterState chapter in view.Chapters)
        foreach (CampaignStageState s in chapter.Stages)
        {
            if (s.Stage.StageId != playedStage || !s.IsCleared) continue;

            playedStage = null;
            string after = s.Stage.StoryAfter;
            if (!string.IsNullOrEmpty(after) && !StoryManager.Inst().HasSeen(after)) StoryUI.Play(after, null);
            return;
        }
    }

    private SongChart Chart(string stageId)
    {
        if (library == null) return null;
        foreach (SongChart chart in library.charts)
        {
            if (chart != null && chart.stageId == stageId) return chart;
        }
        return null;
    }
}
