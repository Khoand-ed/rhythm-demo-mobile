using System.Collections.Generic;
using Data.Player;
using DG.Tweening;
using Manager;
using Tools;
using TMPro;
using UI;
using UI.Sub;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The bridge between the Arknights front-end and the rhythm scene: a node map
// of every song in the library, a detail panel for the one that is selected,
// and the Start button that hands its chart to SongSession and loads gameplay.
//
// Deliberately outside Assets/Arknights. It needs UIBase (Arknights assembly)
// and SongChart (default assembly) at the same time, and an asmdef can never
// reference the default assembly - only the other way round. The default
// assembly is the one place that can see both halves of the project.
public class SongSelectUI : UIBase
{
    // UIManager keys every screen by its prefab name. UIBase.Name holds the
    // same string but is internal to the Arknights assembly, so this file
    // cannot read it and keeps its own copy.
    public const string UIName = "SongSelectUI";

    public const string GameplayScene = "Main";

    // The scene the front-end lives in, and so where leaving a song returns to.
    public const string SelectScene = "StartMenu";

    [Tooltip("Every chart the player can pick. Assigned by Tools/Rhythm/Wire Song Flow.")]
    public BeatmapLibrary library;

    [Tooltip("Horizontal gap between neighbouring song nodes on the map.")]
    public float nodeSpacing = 280f;

    [Tooltip("How far nodes alternate above and below the map's centre line.")]
    public float nodeStagger = 80f;

    private ScrollRect scroll;
    private RectTransform nodeRoot;
    private RectTransform linkRoot;
    private Button nodeTemplate;
    private TextMeshProUGUI sanityText;
    private InfoPanel infoPanel;
    private LeaderboardPanel leaderboard;

    public override void Init()
    {
        scroll = transform.GetComponent<ScrollRect>("Map");
        nodeRoot = (RectTransform)scroll.content.Find("Nodes");
        linkRoot = (RectTransform)scroll.content.Find("Links");

        nodeTemplate = nodeRoot.GetComponent<Button>("NodeTemplate");
        nodeTemplate.gameObject.SetActive(false);

        // The strip is built like HomeUI's: a dim "SANITY" caption on the left
        // and the number on the right, so only the number is ours to write.
        sanityText = transform.GetComponent<TextMeshProUGUI>("TopBar/Sanity/Value");
        transform.GetComponent<Button>("TopBar/BackButton").onClick
            .AddListener(() => HideAndDestroy(UIName));

        // 旧的预制体没有排行榜 / A prefab built before the leaderboard existed has no panel for it.
        // The song list still works; it just says how to add one.
        Transform board = transform.Find("LeaderboardPanel");
        if (board != null) leaderboard = new LeaderboardPanel(board);
        else Debug.LogWarning("[SongSelectUI] No LeaderboardPanel in the prefab. Run Tools/Rhythm/Wire Song Flow to add it.");

        infoPanel = new InfoPanel(transform.Find("InfoPanel"), leaderboard);

        BuildMap();
    }

    public override void UpdateView()
    {
        // Null until a login succeeds. The normal route here goes through the
        // login and home screens, but the song list must not be the thing that
        // throws when it is opened without a player - it does not need one.
        PlayerData playerData = PlayerManager.Inst().Get();

        // START spends it, PRACTICE does not - so this is the number the player
        // weighs between the two.
        sanityText.text = playerData != null
            ? $"{playerData.GetReason()}/{playerData.GetMaxReason()}"
            : "--/--";
    }

    public override void Show()
    {
        base.Show();
        canvasGroup.alpha = 0;
        canvasGroup.DOFade(1, 0.3f);

        // 补发离线的成绩 / Coming back to the song list is also coming back from a song, often one
        // that ended without a network. Anything still waiting goes out now, before the board is
        // read, so the player's own clear is on it.
        PlayerManager.Inst().FlushPendingRuns();
    }

    public override void Hide(bool destroy = false)
    {
        canvasGroup.DOFade(0, 0.2f).OnComplete(() => base.Hide(destroy));
    }

    // One node per song rather than per chart: a song with four difficulties is
    // a single stop on the map, and the panel lists what is playable there.
    private void BuildMap()
    {
        if (library == null || library.charts.Count == 0)
        {
            Debug.LogError("SongSelectUI has no BeatmapLibrary, or the library is empty. " +
                           "Run Tools/Rhythm/Wire Song Flow, and Beatmap/Import All Beatmaps " +
                           "first if nothing has been imported yet.", this);
            return;
        }

        List<string> songIds = new List<string>();

        foreach (SongChart chart in library.charts)
        {
            if (chart == null || string.IsNullOrEmpty(chart.songId)) continue;
            if (!songIds.Contains(chart.songId)) songIds.Add(chart.songId);
        }

        Vector2 previous = Vector2.zero;

        for (int i = 0; i < songIds.Count; i++)
        {
            List<SongChart> charts = library.FindBySongId(songIds[i]);

            // Zig-zag instead of a straight row, so the chain reads as a route
            // across a map rather than as a list.
            Vector2 position = new Vector2(nodeSpacing * (i + 0.5f),
                                           i % 2 == 0 ? nodeStagger : -nodeStagger);

            if (i > 0) CreateLink(previous, position);
            CreateNode(songIds[i], charts, position);
            previous = position;
        }

        // Wide enough to scroll the last node clear of the detail panel.
        scroll.content.sizeDelta = new Vector2(nodeSpacing * (songIds.Count + 1),
                                               scroll.content.sizeDelta.y);
    }

    private void CreateNode(string songId, List<SongChart> charts, Vector2 position)
    {
        Button node = Instantiate(nodeTemplate, nodeRoot);
        node.name = songId;
        node.gameObject.SetActive(true);

        RectTransform rect = (RectTransform)node.transform;
        rect.anchoredPosition = position;

        // Every chart of a song shares its name and author; only the difficulty
        // differs, so the first one speaks for the node.
        SongChart first = charts.Count > 0 ? charts[0] : null;

        rect.GetComponent<TextMeshProUGUI>("Tag").text = songId.ToUpperInvariant();
        rect.GetComponent<TextMeshProUGUI>("Label").text =
            first != null && !string.IsNullOrEmpty(first.songName) ? first.songName : songId;

        node.onClick.AddListener(() => infoPanel.Show(songId, charts));
    }

    // A thin rotated Image standing in for the white route lines on the
    // Arknights stage map. Both ends are node anchoredPositions, so the two
    // share one coordinate space.
    private void CreateLink(Vector2 from, Vector2 to)
    {
        GameObject link = new GameObject("Link", typeof(RectTransform), typeof(Image));

        RectTransform rect = (RectTransform)link.transform;
        rect.SetParent(linkRoot, false);
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = from;

        Vector2 delta = to - from;
        rect.sizeDelta = new Vector2(delta.magnitude, 3f);
        rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);

        Image image = link.GetComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.5f);
        image.raycastTarget = false;
    }

    // The right-hand detail panel: song identity at the top, one row per
    // difficulty, and the Start button that leaves for the gameplay scene.
    private class InfoPanel
    {
        private static readonly Color RowIdle = new Color(1f, 1f, 1f, 0.08f);
        private static readonly Color RowSelected = new Color(0.17f, 0.56f, 0.90f, 0.95f);
        private static readonly Color RatingOn = new Color(0.30f, 0.70f, 1f);
        private static readonly Color RatingOff = new Color(1f, 1f, 1f, 0.15f);

        private readonly CanvasGroup group;
        private readonly TextMeshProUGUI txt_name;
        private readonly TextMeshProUGUI txt_songId;
        private readonly TextMeshProUGUI txt_author;
        private readonly TextMeshProUGUI txt_stats;
        private readonly Image cover;
        private readonly LayoutElement coverLayout;
        private readonly float coverHeight;
        private readonly Image[] rating;
        private readonly RectTransform difficultyRoot;
        private readonly Button difficultyTemplate;
        private readonly Button startButton;
        private readonly Button practiceButton;
        private readonly LeaderboardPanel leaderboard;

        private readonly List<Button> rows = new List<Button>();

        private SongChart selected;

        internal InfoPanel(Transform transform, LeaderboardPanel leaderboard)
        {
            this.leaderboard = leaderboard;

            group = transform.GetComponent<CanvasGroup>();

            txt_name = transform.GetComponent<TextMeshProUGUI>("Header/Name");
            txt_songId = transform.GetComponent<TextMeshProUGUI>("Header/SongId");
            txt_author = transform.GetComponent<TextMeshProUGUI>("Header/Author");
            txt_stats = transform.GetComponent<TextMeshProUGUI>("Stats");
            cover = transform.GetComponent<Image>("Cover");

            // A chart with no cover art should not leave a hole in the panel,
            // so the row collapses rather than reserving its height for nothing.
            coverLayout = cover.GetComponent<LayoutElement>();
            coverHeight = coverLayout != null ? coverLayout.preferredHeight : 0f;

            Transform ratingRoot = transform.Find("Rating");
            rating = new Image[ratingRoot.childCount];
            for (int i = 0; i < rating.Length; i++)
            {
                rating[i] = ratingRoot.GetChild(i).GetComponent<Image>();
            }

            difficultyRoot = (RectTransform)transform.Find("Difficulties");
            difficultyTemplate = difficultyRoot.GetComponent<Button>("DifficultyTemplate");
            difficultyTemplate.gameObject.SetActive(false);

            // START sits in an Actions row beside PRACTICE since the two modes arrived; a prefab
            // from before that still has it directly under the panel.
            Transform start = transform.Find("Actions/StartButton") ?? transform.Find("StartButton");
            startButton = start.GetComponent<Button>();
            startButton.onClick.AddListener(() => StartSong(PlayMode.Ranked));

            Transform practice = transform.Find("Actions/PracticeButton");
            practiceButton = practice != null ? practice.GetComponent<Button>() : null;
            if (practiceButton != null) practiceButton.onClick.AddListener(() => StartSong(PlayMode.Practice));
            else Debug.LogWarning("[SongSelectUI] No PracticeButton in the prefab. Run Tools/Rhythm/Wire Song Flow to add it.");

            group.gameObject.SetActive(false);
        }

        internal void Show(string songId, List<SongChart> charts)
        {
            if (!group.gameObject.activeSelf)
            {
                group.alpha = 0f;
                group.gameObject.SetActive(true);
                group.DOFade(1f, 0.25f);
            }

            SongChart first = charts.Count > 0 ? charts[0] : null;

            txt_name.text = first != null && !string.IsNullOrEmpty(first.songName) ? first.songName : songId;
            txt_songId.text = songId.ToUpperInvariant();
            txt_author.text = first != null ? first.author : "";

            cover.sprite = first != null ? first.cover : null;
            cover.enabled = cover.sprite != null;
            if (coverLayout != null) coverLayout.preferredHeight = cover.enabled ? coverHeight : 0f;

            BuildRows(charts);

            // Nothing is picked yet, so Start stays dead until a difficulty is, and there is no
            // board to show until there is a chart to show it for.
            selected = null;
            startButton.interactable = false;
            if (practiceButton != null) practiceButton.interactable = false;
            if (leaderboard != null) leaderboard.Hide();
            txt_stats.text = "";
            SetRating(0);

            // A song with a single difficulty has nothing to choose between.
            if (charts.Count == 1) Select(charts[0]);
        }

        internal void Hide()
        {
            selected = null;
            if (leaderboard != null) leaderboard.Hide();
            group.DOFade(0f, 0.2f).OnComplete(() => group.gameObject.SetActive(false));
        }

        // Rows are reused rather than rebuilt, the way DepotUI recycles its item
        // icons, so switching songs does not churn through GameObjects.
        private void BuildRows(List<SongChart> charts)
        {
            for (int i = 0; i < charts.Count || i < rows.Count; i++)
            {
                if (i < charts.Count)
                {
                    if (rows.Count <= i)
                    {
                        rows.Add(Object.Instantiate(difficultyTemplate, difficultyRoot));
                    }

                    SongChart chart = charts[i];
                    Button row = rows[i];

                    row.name = chart.name;
                    row.transform.GetComponent<TextMeshProUGUI>("Label").text =
                        $"{chart.DifficultyLabel}  LV.{chart.difficulty}";
                    row.GetComponent<Image>().color = RowIdle;

                    row.onClick.RemoveAllListeners();
                    row.onClick.AddListener(() => Select(chart));
                }

                rows[i].gameObject.SetActive(i < charts.Count);
            }
        }

        private void Select(SongChart chart)
        {
            selected = chart;

            txt_stats.text = $"BPM {chart.bpm:0.#}    {chart.notes.Count} NOTES";
            SetRating(chart.difficulty);
            startButton.interactable = true;
            if (practiceButton != null) practiceButton.interactable = true;

            // 每个难度一张榜 / Each difficulty is its own chart and so its own board. The caption
            // gives the level rather than DifficultyLabel, whose "Dễ" the default font cannot draw.
            if (leaderboard != null)
            {
                string title = !string.IsNullOrEmpty(chart.songName) ? chart.songName : chart.songId;
                leaderboard.Show(chart.stageId, $"{title}   LV.{chart.difficulty}");
            }

            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].GetComponent<Image>().color = rows[i].name == chart.name ? RowSelected : RowIdle;
            }
        }

        // difficulty runs 1-10; the panel shows it as five hexes, the way the
        // operation rating reads in the reference screens.
        private void SetRating(int difficulty)
        {
            int filled = difficulty <= 0
                ? 0
                : Mathf.Clamp(Mathf.CeilToInt(difficulty / 2f), 1, rating.Length);

            for (int i = 0; i < rating.Length; i++)
            {
                rating[i].color = i < filled ? RatingOn : RatingOff;
            }
        }

        /// <summary>
        /// 选好曲子后去选人 / Hands the chart and the mode to the operator picker; gameplay starts
        /// from there. START is a ranked run that spends stamina, PRACTICE costs and counts nothing.
        ///
        /// 这里不写 SongSession / Deliberately writes nothing to SongSession and does not load the
        /// scene. Both moved to CharSelectUI.Play, because the player can still back out of that
        /// screen - deferring the write is what makes backing out leave nothing behind to clear.
        ///
        /// 也不清 selected / And `selected` is deliberately left alone. Clearing it here, the way
        /// this method used to, would mean backing out of the operator picker drops the player on
        /// a map with a dead START and forces them to re-pick the difficulty every time. A second
        /// tap is harmless: CharSelectUI opens opaque and last-sibling over this screen, and its
        /// own `leaving` latch guards the scene load.
        /// </summary>
        private void StartSong(PlayMode mode)
        {
            if (selected == null) return;

            // 练习不受影响 / Only ranked play can be switched off - practice never reaches the server.
            if (mode == PlayMode.Ranked && !RemoteConfigManager.Inst().IsOn(f => f.Ranked))
            {
                CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK,
                    RemoteConfigManager.Unavailable("Ranked play") + " PRACTICE still works.");
                return;
            }

            CharSelectUI.Show(selected, mode);
        }
    }
}
