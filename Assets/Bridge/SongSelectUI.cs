using System.Collections.Generic;
using Data.Player;
using DG.Tweening;
using Manager;
using Tools;
using TMPro;
using UI;
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

        infoPanel = new InfoPanel(transform.Find("InfoPanel"));

        BuildMap();
    }

    public override void UpdateView()
    {
        // Null until a login succeeds. The normal route here goes through the
        // login and home screens, but the song list must not be the thing that
        // throws when it is opened without a player - it does not need one.
        PlayerData playerData = PlayerManager.Inst().Get();

        // Sanity is shown to keep the front-end's look; nothing here spends it,
        // so a song can be practised as often as the player likes.
        sanityText.text = playerData != null
            ? $"{playerData.GetReason()}/{playerData.GetMaxReason()}"
            : "--/--";
    }

    public override void Show()
    {
        base.Show();
        canvasGroup.alpha = 0;
        canvasGroup.DOFade(1, 0.3f);
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

        private readonly List<Button> rows = new List<Button>();

        private SongChart selected;

        internal InfoPanel(Transform transform)
        {
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

            startButton = transform.GetComponent<Button>("StartButton");
            startButton.onClick.AddListener(StartSong);

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

            // Nothing is picked yet, so Start stays dead until a difficulty is.
            selected = null;
            startButton.interactable = false;
            txt_stats.text = "";
            SetRating(0);

            // A song with a single difficulty has nothing to choose between.
            if (charts.Count == 1) Select(charts[0]);
        }

        internal void Hide()
        {
            selected = null;
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

        private void StartSong()
        {
            if (selected == null) return;

            SongSession.Set(selected);

            // Cleared before the fade so a second tap cannot start twice - the
            // same guard SelectDungeonUI used before instantiating its dungeon.
            SongChart starting = selected;
            selected = null;
            startButton.interactable = false;

            // SoundManager is DontDestroyOnLoad, so the front-end's music would
            // keep playing underneath the song. HomeUI starts it again when the
            // player comes back, because its Lua show() is what plays it.
            SoundManager.Inst().StopMusic();

            // Hidden, not destroyed - the built map is worth keeping, and
            // HomeUI has to still be underneath for Back to land on.
            UIManager.Inst().Hide(UIName);

            // The UI camera and canvas are DontDestroyOnLoad, so they survive
            // into the gameplay scene and would draw the front-end over the
            // song. Switching the camera off takes the whole front-end out of
            // the way at once; GameStart switches it back on when the player
            // returns. Nothing needs destroying for that.
            Delay.add(() =>
            {
                GameObject uiCamera = UIManager.Inst().GetCamera();
                if (uiCamera != null) uiCamera.SetActive(false);

                SceneManager.LoadScene(GameplayScene);
            }, 0.6f);

            Debug.Log($"Starting {starting.stageId} ({starting.name}).");
        }
    }
}
