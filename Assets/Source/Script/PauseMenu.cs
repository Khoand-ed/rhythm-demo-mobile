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

    // READY/GO and the run-up while they play, the 3-2-1 while it counts: held so a Back or the app
    // going to the background can stop them part-way (Interrupt).
    private Coroutine intro;
    private Coroutine countdown;

    // A run-in cut short by the app going to the background, to play again from READY on return.
    private bool introAgain;

    // ------------------------------------------------------------------- lifecycle

    private void Start()
    {
        if (pauseButton != null) pauseButton.SetActive(false);
        if (panel != null) panel.SetActive(false);
        if (overlay != null) overlay.SetActive(false);

        intro = StartCoroutine(RunIntro());
    }

    private void Update()
    {
        // 安卓返回键 / Android's Back arrives as Escape (Esc in the Editor does the same). The front-end's
        // BackKey sleeps with the UI camera while a song plays, so here this is the one that answers.
        if (Input.GetKeyDown(KeyCode.Escape)) OnBack();
    }

#if !UNITY_EDITOR
    // 切到后台 / On a phone only. In the Editor focus comes and goes with every click on another
    // window, and a song that paused each time would be no use to work on; Interrupt and Return are
    // public for trying it there.
    private void OnApplicationPause(bool paused)
    {
        if (paused) Interrupt();
        else Return();
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused) Interrupt();
        else Return();
    }
#endif

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
        if (game == null)
        {
            intro = null;
            yield break;
        }

        // 等加载画面撤走 / Wait for the loading screen to lift. It is still fading out over the
        // first frames of the scene, and a READY counted down underneath it is a READY the
        // player never saw. Instant on a Retry, which never shows it.
        yield return new WaitWhile(() => UI.LoadingScreen.IsVisible);

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

        intro = null;
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

        ShowPanel();

        busy = false;
    }

    private void ShowPanel()
    {
        if (pauseButton != null) pauseButton.SetActive(false);
        if (panel != null) panel.SetActive(true);

        if (panelGroup != null)
        {
            panelGroup.alpha = 0f;
            // 时间缩放为0, 补间必须走非缩放 / timeScale is already 0 here, so the tween
            // has to be told to ignore it or it never advances.
            panelGroup.DOFade(1f, 0.18f).SetUpdate(true);
        }
    }

    /// <summary>Wired to the green button: counts 3-2-1, then hands control back.</summary>
    public void Resume()
    {
        if (busy) return;
        countdown = StartCoroutine(RunResume());
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
        countdown = null;
    }

    /// <summary>
    /// Wired to the orange button: clears the run, then plays the run-in again.
    ///
    /// 按模式重试 / In the mode the song was started in. A practice retry starts at once; a ranked
    /// one waits for the server to open - and charge for - the next run, and when it is refused
    /// (not enough Sanity, no network) the panel stays up with the reason on screen.
    /// </summary>
    public void Retry()
    {
        if (busy) return;

        busy = true;

        GameManager.instance.RequestRetry(
            () => StartCoroutine(RunRetry()),
            () => busy = false);
    }

    /// <summary>
    /// The results screen's RETRY, once GameManager has the next run ready: the same reset and
    /// READY/GO run-in a pause-menu retry plays.
    /// </summary>
    public void RestartWithIntro()
    {
        StopAllCoroutines();
        intro = null;
        countdown = null;
        introAgain = false;
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

        // Its own coroutine rather than nested here, so Interrupt can stop it like the first one.
        intro = StartCoroutine(RunIntro());
        yield break;
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

    // ------------------------------------------------------------- back, background

    /// <summary>
    /// 返回键 / Android's Back during a song: what the two buttons would do. A running song pauses;
    /// on the panel it resumes (3-2-1); during the 3-2-1 it goes back to the panel. READY/GO ignores
    /// it - the song is about to start, and its run-up cannot be paused cleanly. On the results screen
    /// it is that screen's own back button.
    /// </summary>
    public void OnBack()
    {
        GameManager game = GameManager.instance;
        if (game == null) return;

        if (game.resultsScreen != null && game.resultsScreen.activeInHierarchy)
        {
            PressResultsBack(game);
            return;
        }

        if (countdown != null)
        {
            Interrupt();
            return;
        }

        if (panel != null && panel.activeSelf)
        {
            Resume();
            return;
        }

        if (intro == null) Pause();
    }

    /// <summary>
    /// 切到后台 / The app went to the background, or lost focus to a call or the notification shade:
    ///   - a running song pauses, its panel up, waiting for Resume;
    ///   - a 3-2-1 stops, back at the panel - the song is still paused;
    ///   - READY/GO is stopped and plays again from READY when the player is back (<see cref="Return"/>).
    ///     Its waits run on the real clock, which kept going: left alone, the song would start the
    ///     moment the app came back. During the run-up after GO the song has already been scheduled,
    ///     so the run is put back to its start (ResetRun, as a Retry does) - nothing has been judged,
    ///     the first note is still on its way.
    /// </summary>
    public void Interrupt()
    {
        GameManager game = GameManager.instance;
        if (game == null) return;
        if (game.resultsScreen != null && game.resultsScreen.activeInHierarchy) return;

        if (countdown != null)
        {
            StopCoroutine(countdown);
            countdown = null;
            HideOverlayNow();
            ShowPanel();
            busy = false;
            return;
        }

        if (intro != null)
        {
            StopCoroutine(intro);
            intro = null;
            HideOverlayNow();
            if (game.startPlaying) game.ResetRun();
            introAgain = true;
            return;
        }

        Pause();
    }

    /// <summary>The app is back: a run-in <see cref="Interrupt"/> cut short plays again from READY.</summary>
    public void Return()
    {
        if (!introAgain) return;

        introAgain = false;
        intro = StartCoroutine(RunIntro());
    }

    // The results screen's back button, wired to GameManager.ReturnToSongSelect in the scene by
    // Tools/Rhythm/Wire Song Flow. Pressed as a tap would - not at all while it is switched off.
    private static void PressResultsBack(GameManager game)
    {
        foreach (Button button in game.resultsScreen.GetComponentsInChildren<Button>(true))
        {
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                if (button.onClick.GetPersistentMethodName(i) != nameof(GameManager.ReturnToSongSelect)) continue;

                if (button.isActiveAndEnabled && button.IsInteractable()) button.onClick.Invoke();
                return;
            }
        }
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

    private void HideOverlayNow()
    {
        if (overlay == null) return;

        if (overlayGroup != null) overlayGroup.DOKill();
        overlay.SetActive(false);
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
