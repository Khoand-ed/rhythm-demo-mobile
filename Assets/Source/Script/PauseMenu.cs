using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The pause button, the three-button pause panel, the 3-2-1 resume countdown and
// the READY/GO run-in at the top of a song. One controller, because all four are
// the same thing: the only places where the song is not simply playing.
//
// Built by Tools/Rhythm/Set Up Pause And Intro, which also switches
// GameManager.startOnFirstInput off so this decides when the song starts.
//
// 计时全部用非缩放时间 / Every wait here is unscaled. PauseGame sets Time.timeScale
// to 0, so a plain WaitForSeconds during the countdown would never return - the
// game would sit on "3" forever with no error to explain it.
public class PauseMenu : MonoBehaviour
{
    [Header("按钮 / Pause button, hidden while the panel or a count is up")]
    public GameObject pauseButton;

    [Header("面板 / Panel")]
    public GameObject panel;
    public CanvasGroup panelGroup;
    public Button quitButton;
    public Button retryButton;
    public Button resumeButton;

    [Header("提示 / The big centred text: READY, GO and the countdown")]
    public GameObject overlay;
    public CanvasGroup overlayGroup;
    public TextMeshProUGUI overlayText;

    [Header("时间 / Timing")]
    [Tooltip("Total seconds from the scene opening to the first sample of the song, " +
             "the note run-up included.")]
    public float introSeconds = 5f;

    [Tooltip("Seconds counted down after Resume is pressed.")]
    public int resumeCountdown = 3;

    // 留给 READY/GO 的最小时间 / The run-up is chart-dependent and could in theory eat
    // the whole intro. READY and GO still get this long, even if that pushes the
    // total past introSeconds - a run-in nobody can read is worse than a late song.
    private const float MinimumReadable = 1.2f;

    private bool busy;

    // ------------------------------------------------------------------- lifecycle

    private void Start()
    {
        if (pauseButton != null) pauseButton.SetActive(false);
        if (panel != null) panel.SetActive(false);
        if (overlay != null) overlay.SetActive(false);

        StartCoroutine(RunIntro());
    }

    private void OnDestroy()
    {
        // 场景切走时把时间缩放还原 / Leaving the scene mid-pause would carry timeScale 0
        // into the next one, freezing every tween in the front-end.
        if (Time.timeScale == 0f) Time.timeScale = 1f;
    }

    // ----------------------------------------------------------------------- intro

    /// <summary>
    /// READY, then GO, then the song - adding up to <see cref="introSeconds"/> in total.
    ///
    /// 跑道算在五秒里 / The note run-up is part of the five seconds, not on top of it:
    /// StartSong schedules the first sample that far ahead, so the visible half of the
    /// run-in is whatever is left once the run-up is subtracted.
    /// </summary>
    private IEnumerator RunIntro()
    {
        GameManager game = GameManager.instance;
        if (game == null) yield break;

        // 等所有 Start 跑完 / One frame, so every Start has run before anything is measured.
        // GameManager.Start is where SongSession's chart is handed to the spawner, and the
        // run-up below is computed from that chart's note speed. Two components on one
        // GameObject have no guaranteed Start order, so reading it in the same frame could
        // measure the scene's fallback chart instead of the one the player picked.
        yield return null;

        float runUp = RunUpSeconds(game);
        float visible = Mathf.Max(MinimumReadable, introSeconds - runUp);

        yield return Show("READY", visible * 0.7f);

        // GO 要跨过跑道 / GO goes up before the song is told to start and stays up
        // through the run-up, so the silent stretch before the first sample is not
        // an empty screen.
        SetOverlay("GO!");
        yield return new WaitForSecondsRealtime(visible * 0.3f);

        game.BeginSong();

        yield return new WaitForSecondsRealtime(runUp);

        yield return Hide();

        if (pauseButton != null) pauseButton.SetActive(true);
    }

    private static float RunUpSeconds(GameManager game)
    {
        if (Conductor.instance == null) return 0f;

        float required = game.noteSpawner != null ? game.noteSpawner.GetRequiredLeadIn() : 0f;

        // StartSong itself takes the larger of the two, so the wait has to match.
        return Mathf.Max(Conductor.instance.startDelay, required);
    }

    // ----------------------------------------------------------------------- pause

    /// <summary>Wired to the pause button.</summary>
    public void Pause()
    {
        if (busy || !CanPause()) return;

        busy = true;

        GameManager.instance.PauseGame();

        if (pauseButton != null) pauseButton.SetActive(false);
        if (panel != null) panel.SetActive(true);

        if (panelGroup != null)
        {
            panelGroup.alpha = 0f;
            // 时间缩放为0, 补间必须走非缩放 / timeScale is already 0 here, so the tween
            // has to be told to ignore it or it never advances.
            panelGroup.DOFade(1f, 0.18f).SetUpdate(true);
        }

        busy = false;
    }

    /// <summary>Wired to the green button: counts 3-2-1, then hands control back.</summary>
    public void Resume()
    {
        if (busy) return;
        StartCoroutine(RunResume());
    }

    private IEnumerator RunResume()
    {
        busy = true;

        if (panel != null) panel.SetActive(false);

        for (int n = resumeCountdown; n >= 1; n--)
        {
            SetOverlay(n.ToString());
            Punch();
            yield return new WaitForSecondsRealtime(1f);
        }

        yield return Hide();

        GameManager.instance.ResumeGame();

        if (pauseButton != null) pauseButton.SetActive(true);

        busy = false;
    }

    /// <summary>Wired to the orange button: clears the run, then plays the run-in again.</summary>
    public void Retry()
    {
        if (busy) return;
        StartCoroutine(RunRetry());
    }

    private IEnumerator RunRetry()
    {
        busy = true;

        if (panel != null) panel.SetActive(false);
        if (pauseButton != null) pauseButton.SetActive(false);

        // 先还原时间再重置 / timeScale back first: ResetRun stops the Conductor, and
        // everything after this point wants the game running again.
        Time.timeScale = 1f;
        GameManager.instance.ResetRun();

        busy = false;

        yield return RunIntro();
    }

    /// <summary>Wired to the pink button.</summary>
    public void Quit()
    {
        if (busy) return;

        busy = true;
        StopAllCoroutines();

        // ReturnToSongSelect puts timeScale back itself, and loads the front-end scene.
        GameManager.instance.ReturnToSongSelect();
    }

    // ------------------------------------------------------------------- can pause

    /// <summary>
    /// 只有正在跑的歌才能暂停 / Only a song that is actually running can be paused.
    ///
    /// Guards the three states where the button would otherwise do something odd:
    /// during the run-in before the song starts, while a countdown is already going,
    /// and once the results screen is up.
    /// </summary>
    private bool CanPause()
    {
        GameManager game = GameManager.instance;
        if (game == null || !game.startPlaying) return false;
        if (game.resultsScreen != null && game.resultsScreen.activeInHierarchy) return false;

        Conductor conductor = Conductor.instance;
        return conductor != null && conductor.IsPlaying && !conductor.IsPaused;
    }

    // --------------------------------------------------------------------- overlay

    private IEnumerator Show(string text, float seconds)
    {
        SetOverlay(text);
        Punch();
        yield return new WaitForSecondsRealtime(seconds);
    }

    private void SetOverlay(string text)
    {
        if (overlay == null || overlayText == null) return;

        overlay.SetActive(true);
        overlayText.text = text;

        if (overlayGroup != null) overlayGroup.alpha = 1f;
    }

    private void Punch()
    {
        if (overlayText == null) return;

        RectTransform rect = overlayText.rectTransform;
        rect.DOKill();
        rect.localScale = Vector3.one * 1.35f;
        rect.DOScale(1f, 0.25f).SetEase(Ease.OutBack).SetUpdate(true);
    }

    private IEnumerator Hide()
    {
        if (overlay == null) yield break;

        if (overlayGroup != null)
        {
            overlayGroup.DOKill();
            overlayGroup.DOFade(0f, 0.18f).SetUpdate(true);
            yield return new WaitForSecondsRealtime(0.18f);
        }

        overlay.SetActive(false);
    }
}
