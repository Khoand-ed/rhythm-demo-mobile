using System.Collections;
using System.Collections.Generic;
using Data.Char;
using Tools;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Promuse.Contracts.Runs;
using Promuse.Net;

// 判定在 RunJudge / The judging itself lives in RunJudge (Rhythm.Core), which the server runs again
// over the input trace to check a result. What stays here is reading the input, recording it, and
// drawing what the judge decided - the IRunJudgeListener half.
public class GameManager : MonoBehaviour, IRunJudgeListener
{

    public AudioSource theMusic;

    public bool startPlaying;

    [Tooltip("Left on, the song starts on the first input. Tools/Rhythm/Set Up Pause And Intro " +
             "turns it off, so the READY/GO sequence decides when the song starts instead.")]
    public bool startOnFirstInput = true;

    public BeatScroller theBsRight;

    public BeatScroller theBsLeft;

    public static GameManager instance;

    public int scorePerNote = 100;
    public int scorePerGoodNote = 125;
    public int scorePerPerfectNote = 150;

    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI multiText;

    public int[] multiplierThresholds;

    public GameObject resultsScreen;
    public TextMeshProUGUI percentHitText, normalsText, goodsText, perfectsText, missesText, rankText, finalScoreText;

    [Tooltip("Optional. Left unassigned, the results screen simply omits these.")]
    public TextMeshProUGUI maxComboText, fullComboText;

    [Tooltip("Optional. What the server made of the run - its rank, under review, or not verified. " +
             "Left unassigned, the verdict is only logged.")]
    public TextMeshProUGUI rankingText;

    // 这一局给了什么 / What the run paid - first-clear Orundum, drops, EXP, a level up - under the
    // ranking line. Added by Tools/Rhythm/Wire Song Flow.
    public TextMeshProUGUI rewardsText;

    [Tooltip("The results screen's RETRY. Its label is rewritten per run to say what another attempt " +
             "costs. Added by Tools/Rhythm/Wire Song Flow.")]
    public Button resultsRetryButton;
    public TextMeshProUGUI resultsRetryLabel;

    [Tooltip("One line over everything else - why a retry was refused, for instance. Added by " +
             "Tools/Rhythm/Wire Song Flow; left unassigned, the message only reaches the log.")]
    public GameObject noticeRoot;
    public TextMeshProUGUI noticeText;

    // Where the character stands. A missed note travels here and pauses before disappearing.
    public Transform middleZoneMarker;

    public NoteSpawner noteSpawner;

    // Runs the old NoteHolder/BeatScroller path instead of the spawner, so the
    // two can be compared side by side. Removed once the new path is signed off.
    public bool useLegacyNoteHolders = false;

    // Hoisted off NoteObject: every note carried the same three prefabs, so they
    // belong in one place now that judging lives here.
    public GameObject hitEffect, goodEffect, perfectEffect;

    [Tooltip("Sounds, judgement popups, FAST/SLOW and bursts. Built by Tools/Rhythm/Set Up Hit Feedback; " +
             "left empty, hits fall back to the three effect prefabs above.")]
    public HitFeedback feedback;

    [Tooltip("The operator standing in the middle. Purely cosmetic: it reacts to what the judge decided and " +
             "never feeds back into it. Built by Tools/Rhythm/Set Up Character Stage; left empty, nobody " +
             "stands there.")]
    public CharacterPresenter character;

    // 三份调参资产 / The three tuning assets. Assign them here; Tools/Rhythm/Create Tuning
    // Assets makes them at their default values if they do not exist yet.
    //
    // 不再内嵌 / Deliberately references rather than inline instances: as inline fields these
    // values were serialized into Main.unity, where a balance change was invisible in review
    // and could not be shared between scenes or varied per difficulty.
    public JudgeSettings judge;
    public HealthSettings health;
    public FeverSettings fever;

    [Tooltip("Points per 100ms of a held note's body. Awards no combo.")]
    public int scorePerHoldTick = 10;

    [Tooltip("Seconds of held body per scorePerHoldTick.")]
    public float holdTickInterval = 0.1f;

    [Tooltip("Reads fingers still resting on a lane, for hold notes. Found in the scene if left empty.")]
    public TouchInputZone touchZone;

    public GaugeBar hpBar;
    public GaugeBar feverBar;

    [Tooltip("Shown when HP runs out. The scene's existing FailedText fits.")]
    public GameObject failedText;

    // Score, combo, the multiplier ladder, HP and the fever gauge, with the rules
    // that move them. Lives in Rhythm.Core so those rules can be tested without a
    // scene; what stays here is the tuning above, the labels and the gauges.
    //
    // Serialized so a run's counters are still readable in the Inspector while it
    // plays. Reset() in Start is what keeps a value saved into the scene from
    // leaking into the next run.
    public RunState state = new RunState();

    // 开局抓一次, 之后一直用 / Captured once at the top of Start and kept, because
    // SongSession.Clear() runs a few lines later and a retry goes through SyncTuning again
    // with nothing left to read. Defaults of 1 mean "no operator", which is what opening
    // this scene directly in the Editor gets.
    private float operatorScoreModifier = 1f;
    private float operatorFeverModifier = 1f;
    private int operatorMaxHp;
    private PassiveSO operatorPassive;

    // 整份 meta 留着, 舞台要用它找骨骼 / The whole meta is kept as well, because the stage needs it to
    // find the operator's rig - and by then SongSession has been cleared.
    private CharMeta operatorMeta;

    // 同样的理由 / Same reason, same place: the run id is read off SongSession before the chart
    // handover clears it. Empty means there is nothing to close.
    private System.Guid runId;

    // The seed the server issued with the run. RunState rolls the passives from it, and the server
    // keeps the same number, so the rolls can be drawn again when the run is replayed.
    private long runSeed;

    // 这一局怎么玩 / Ranked or practice, read off SongSession with the run. A retry plays the same
    // mode again: a ranked retry opens, and pays for, a new run; a practice retry is free.
    private PlayMode playMode;

    // What this run cost, so RETRY can say what another one will.
    private int runCost;

    // 是否和服务端打交道 / Whether this session plays against the server at all. False for practice,
    // and for the gameplay scene opened straight from the Editor, which has no run to report.
    private bool scored;

    // The operator's id, kept for opening the next run on a ranked retry - by then SongSession has
    // long been cleared.
    private string operatorId;

    private bool openingRun;
    private Coroutine noticeRoutine;

    // 每帧的输入 / Every gameplay frame's input, for the server. Kept across a retry's reset only
    // in the sense that BeginTrace empties it: the trace sent is the attempt that was reported.
    private readonly InputTraceWriter inputTrace = new InputTraceWriter();

    // 判定 / The judge for the chart being played, built once the chart is known. Null on the legacy
    // path and when the scene is missing its spawner, chart or tuning.
    private RunJudge runJudge;

    // The song time of the frame being judged, for the views the listener moves.
    private float stepTime;

    // What the score and combo labels show, so they are rebuilt only when a number changes.
    private int shownScore = -1;
    private int shownCombo = -1;

    private readonly Dictionary<KeyCode, List<NoteObject>> activeNotesByKey = new Dictionary<KeyCode, List<NoteObject>>();
    private readonly Dictionary<KeyCode, float> hitZoneXByKey = new Dictionary<KeyCode, float>();
    private readonly Dictionary<KeyCode, ButtonController> buttonsByKey = new Dictionary<KeyCode, ButtonController>();
    private readonly HashSet<KeyCode> simulatedKeyDownsThisFrame = new HashSet<KeyCode>();

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;

        // Hit zones are read from the actual on-screen buttons rather than
        // hardcoded, so NoteObject never needs to know lane geometry.
        foreach (ButtonController button in FindObjectsByType<ButtonController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            hitZoneXByKey[button.keyToPress] = button.transform.position.x;
            buttonsByKey[button.keyToPress] = button;
        }

        if (touchZone == null) touchZone = FindAnyObjectByType<TouchInputZone>();

        RequireTuning();

        ApplyNoteSystemMode();
    }

    // The legacy holders and the spawner are mutually exclusive - only one of
    // them may be feeding notes to the judge.
    private void ApplyNoteSystemMode()
    {
        if (theBsLeft != null) theBsLeft.gameObject.SetActive(useLegacyNoteHolders);
        if (theBsRight != null) theBsRight.gameObject.SetActive(useLegacyNoteHolders);
        if (noteSpawner != null) noteSpawner.gameObject.SetActive(!useLegacyNoteHolders);
    }

    // Returns the world-space x position notes for this key should be hit at.
    public float GetHitZoneX(KeyCode key)
    {
        return hitZoneXByKey.TryGetValue(key, out float x) ? x : 0f;
    }

    public Vector3 GetMiddleZonePosition()
    {
        if (middleZoneMarker == null)
        {
            Debug.LogWarning("GameManager.middleZoneMarker is not assigned.");
            return Vector3.zero;
        }

        return middleZoneMarker.position;
    }

    // A key held down or a finger still resting on that half of the screen.
    // Hold notes and the button sprites both read this.
    public bool IsLaneHeld(KeyCode key)
    {
        if (Input.GetKey(key)) return true;
        return touchZone != null && touchZone.IsHeld(key);
    }

    // Lets touch input (see TouchInputZone) drive the same note queue as the keyboard.
    public void SimulateKeyDown(KeyCode key)
    {
        simulatedKeyDownsThisFrame.Add(key);

        if (buttonsByKey.TryGetValue(key, out ButtonController button))
        {
            button.FlashPressed();
        }
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // 必须在 SongSession.Clear() 之前 / Before anything else, because the chart handover
        // below clears SongSession and the operator would go with it.
        CaptureOperator();
        CaptureRun();

        // 没有舞台或没有骨骼就什么都不画 / No stage, or an operator without a rig, draws nothing.
        if (character != null) character.Show(operatorMeta);

        if (noticeRoot != null) noticeRoot.SetActive(false);

        scoreText.text = "Score: 0";
        multiText.text = "0";

        SyncTuning();
        state.Reset();
        BeginTrace();

        if (failedText != null) failedText.SetActive(false);
        PushGauges();

        if (useLegacyNoteHolders)
        {
            state.totalNotes = FindObjectsByType<NoteObject>().Length;
        }
        else
        {
            // The song select screen leaves its pick here. Taking it before the
            // checks below means the scene's own chart is only a fallback, so
            // opening this scene directly in the Editor still plays something.
            if (SongSession.HasChart && noteSpawner != null)
            {
                noteSpawner.chart = SongSession.Chart;
                SongSession.Clear();
            }

            if (Conductor.instance == null || noteSpawner == null || noteSpawner.chart == null)
            {
                Debug.LogError("The spawner path needs a Conductor, a NoteSpawner and a chart. " +
                               "Run Tools/Rhythm/Set Up Note System and Beatmap/Import All Beatmaps, " +
                               "or tick useLegacyNoteHolders to stay on the old system.");
            }

            state.totalNotes = noteSpawner != null && noteSpawner.chart != null
                ? RunJudge.CountJudgements(noteSpawner.chart.notes)
                : 0;

            ApplyChartToConductor();
            BuildJudge();
        }

}

// Update is called once per frame
void Update()
    {
        if(!startPlaying)
        {
            if(startOnFirstInput && Input.anyKeyDown)
            {
                BeginSong();
            }
        }else
        {
            // Fever and the passive's clock are judged with the notes, inside RunJudge.Step.
            HandleNoteInput();

            if(SongFinished() && !resultsScreen.activeInHierarchy)
            {
                resultsScreen.SetActive(true);
                LabelRetry();

                // 服务端记一笔 / The run is closed on the server, which is where the mission
                // counters live. The activeInHierarchy guard above already makes this fire once
                // per run rather than once per frame, and Restart turns the screen back off,
                // which is what lets a second run count again - but the run id is not reissued
                // by a restart, so the report is keyed on it and a replayed one is refused.
                ReportRun(!state.failed);

                if (character != null) character.OnFinish(!state.failed);

                normalsText.text = "" + state.normalHits;
                goodsText.text = state.goodHits.ToString();
                perfectsText.text = state.perfectHits.ToString();
                missesText.text = "" + state.missedHits;

                float percentHit = state.Accuracy;

                percentHitText.text = percentHit.ToString("F1") + "%";

                rankText.text = CalculateRank(percentHit);

                finalScoreText.text = state.score.ToString();

                if (maxComboText != null) maxComboText.text = state.maxCombo.ToString();

                if (fullComboText != null)
                {
                    fullComboText.text = state.IsFullCombo ? "FULL COMBO" : "";
                }
            }
        }
    }

    // The judge for this chart, over the RunState the tuning was just synced into. Accuracy is
    // hits over its judgements, not over chart entries: a Twin is two halves judged separately,
    // and a Hold is judged at its head and its tail (RunJudge.CountJudgements).
    private void BuildJudge()
    {
        runJudge = null;

        if (noteSpawner == null || noteSpawner.chart == null || noteSpawner.lanes == null || judge == null) return;

        runJudge = new RunJudge(noteSpawner.chart.notes, noteSpawner.lanes.Length, judge, ScoreRulesNow(), state);
        runJudge.Reset();
        PushHud();
    }

    /// <summary>Whether the judge has already decided that note - the spawner skips drawing it.</summary>
    public bool IsNoteResolved(int lane, int index)
    {
        return runJudge != null && runJudge.IsResolved(lane, index);
    }

    // The chart owns its own audio and sync offset, so a different song is a
    // different chart asset rather than a scene edit.
    private void ApplyChartToConductor()
    {
        if (Conductor.instance == null || noteSpawner == null || noteSpawner.chart == null) return;

        SongChart chart = noteSpawner.chart;
        Conductor.instance.songOffset = chart.offset;

        if (chart.clip != null && Conductor.instance.theMusic != null)
        {
            Conductor.instance.theMusic.clip = chart.clip;
        }
        else if (chart.clip == null)
        {
            Debug.LogWarning($"{chart.name} has no gameplay clip; falling back to whatever the scene's AudioSource holds.", chart);
        }
    }

    // The Conductor schedules playback a moment ahead, so theMusic.isPlaying is
    // briefly false right after the song starts - only the legacy path can use it.
    private bool SongFinished()
    {
        if (state.failed) return true;
        if (useLegacyNoteHolders) return !theMusic.isPlaying;

        return Conductor.instance != null && Conductor.instance.IsFinished;
    }

    private static string CalculateRank(float percentHit)
    {
        (float minPercent, string rank)[] rankThresholds =
        {
            (95f, "S"),
            (85f, "A"),
            (70f, "B"),
            (55f, "C"),
            (40f, "D"),
        };

        foreach (var (minPercent, rank) in rankThresholds)
        {
            if (percentHit > minPercent)
            {
                return rank;
            }
        }

        return "F";
    }

    private void HandleNoteInput()
    {
        if (useLegacyNoteHolders)
        {
            HandleLegacyNoteInput();
        }
        else
        {
            HandleSpawnedNoteInput();
        }

        simulatedKeyDownsThisFrame.Clear();
    }

    // Resolves at most one queued note per key per frame, so two notes
    // that are close together can never both consume the same key press.
    private void HandleLegacyNoteInput()
    {
        foreach (var kvp in activeNotesByKey)
        {
            List<NoteObject> notes = kvp.Value;
            if (notes.Count == 0)
            {
                continue;
            }

            if (Input.GetKeyDown(kvp.Key) || simulatedKeyDownsThisFrame.Contains(kvp.Key))
            {
                NoteObject note = notes[0];
                notes.RemoveAt(0);
                note.TryHit();
            }
        }
    }

    /// <summary>
    /// 一帧 / One frame: read the clock once, read the lanes once, record exactly that, and judge
    /// exactly that. The server replays the recording through the same RunJudge, so nothing the
    /// judge consumes may come from anywhere but these three values.
    /// </summary>
    private void HandleSpawnedNoteInput()
    {
        // Start() has already logged what is missing if any is absent.
        if (noteSpawner == null || noteSpawner.lanes == null || Conductor.instance == null || runJudge == null) return;

        // Song time is frozen while paused, so a press would be judged against
        // a stale clock, and letting go of a hold would drop it.
        if (Conductor.instance.IsPaused) return;

        // 结束了就不再记 / Once the run has failed or its results are up, nothing more is judged
        // or recorded: the trace sent ends where the run ended.
        if (runJudge.Failed || resultsScreen.activeInHierarchy) return;

        float songTime = Conductor.instance.SongTime;
        ReadLanes(out int pressed, out int held);

        inputTrace.Record(songTime, pressed, held);

        // Every press answers, note or not - a button that reacts only
        // when something is there to hit feels laggy.
        for (int laneIndex = 0; laneIndex < noteSpawner.lanes.Length && laneIndex < 8; laneIndex++)
        {
            if ((pressed & (1 << laneIndex)) != 0) PunchButton(noteSpawner.lanes[laneIndex].key);
        }

        stepTime = songTime;
        runJudge.Step(songTime, pressed, held, this);

        // 手动开狂热 / Fever by hand, only when the rules do not open it themselves. Not in the
        // trace: the server refuses such a ruleset, so a ranked run never takes this path.
        if (!fever.autoActivate && state.feverGauge >= fever.maxFever && Input.GetKeyDown(fever.manualActivateKey))
        {
            runJudge.ActivateFever(songTime, this);
        }

        PushHud();
    }

    private void PunchButton(KeyCode key)
    {
        if (buttonsByKey.TryGetValue(key, out ButtonController button)) button.Punch();
    }

    // Where a lane's feedback appears: its button, not the note, so popups
    // always land in the same readable spot.
    private Vector3 LaneButtonPos(int laneIndex)
    {
        LaneConfig lane = noteSpawner.lanes[laneIndex];
        return lane.IsValid ? lane.HitPos : Vector3.zero;
    }

    // ------------------------------------------------------------- what the judge decided

    // 只画 / Drawing only. These are told after RunJudge has moved the state, and must never move it
    // themselves: the server judges the same frames with no listener at all.

    public void OnHit(int lane, int index, NoteType type, Judgement judgement, float signedDelta, bool holdBegins)
    {
        NoteView view = noteSpawner.ViewOf(lane, index);
        Vector3 hitAt = view != null ? view.transform.position : LaneButtonPos(lane);

        if (view != null)
        {
            if (holdBegins) view.BeginHold();
            else view.MarkHit();
        }

        if (feedback != null)
        {
            feedback.OnCombo(state.combo);
            feedback.OnHit(lane, LaneButtonPos(lane), judgement, signedDelta, type);
        }
        else
        {
            SpawnLegacyEffect(judgement, hitAt);
        }

        if (character != null) character.OnHit(lane, judgement, type, holdBegins);
    }

    public void OnHoldTick(int lane)
    {
        if (feedback != null) feedback.OnHoldTick(LaneButtonPos(lane));
    }

    public void OnHoldEnd(int lane, int index, Judgement judgement)
    {
        NoteView view = noteSpawner.ViewOf(lane, index);
        Vector3 at = view != null ? view.transform.position : LaneButtonPos(lane);
        if (view != null) view.MarkHit();

        if (feedback != null)
        {
            feedback.OnCombo(state.combo);
            feedback.OnHoldEnd(lane, LaneButtonPos(lane), judgement);
        }
        else
        {
            SpawnLegacyEffect(judgement, at);
        }

        if (character != null) character.OnHoldEnd(lane);
    }

    public void OnHoldDropped(int lane, int index)
    {
        NoteView view = noteSpawner.ViewOf(lane, index);
        if (view != null) view.MarkDropped(stepTime);

        if (character != null) character.OnHoldDropped(lane);
        if (feedback != null) feedback.OnMiss(lane, LaneButtonPos(lane));
    }

    public void OnMiss(int lane, int index, NoteType type)
    {
        NoteView view = noteSpawner.ViewOf(lane, index);
        if (view != null) view.MarkMissed();

        // 先反应后结算 / Told before the miss is counted, which can end the run: the fall must be
        // the last thing said.
        if (character != null) character.OnMiss(lane);
        if (feedback != null) feedback.OnMiss(lane, LaneButtonPos(lane));
    }

    public void OnFeverStart()
    {
        if (character != null) character.OnFeverStart();
    }

    public void OnFeverEnd()
    {
        if (character != null) character.OnFeverEnd();
    }

    public void OnFail()
    {
        PushHud();
        FailRun();
    }

    /// <summary>The score and combo labels and the gauges, from the state as the judge left it.</summary>
    private void PushHud()
    {
        if (state.score != shownScore)
        {
            shownScore = state.score;
            scoreText.text = "Score: " + state.score;
        }

        if (state.combo != shownCombo)
        {
            shownCombo = state.combo;
            multiText.text = state.combo.ToString();
        }

        PushGauges();
    }

    private void SpawnLegacyEffect(Judgement judgement, Vector3 at)
    {
        GameObject effect = judgement == Judgement.Perfect ? perfectEffect
                          : judgement == Judgement.Great ? goodEffect
                          : hitEffect;

        if (effect != null) Instantiate(effect, at, effect.transform.rotation);
    }

    public void RegisterNote(KeyCode key, NoteObject note)
    {
        if (!activeNotesByKey.TryGetValue(key, out List<NoteObject> notes))
        {
            notes = new List<NoteObject>();
            activeNotesByKey[key] = notes;
        }

        notes.Add(note);
    }

    public void UnregisterNote(KeyCode key, NoteObject note)
    {
        if (activeNotesByKey.TryGetValue(key, out List<NoteObject> notes))
        {
            notes.Remove(note);
        }
    }

    // 旧路径用 / The legacy NoteObject path only: the spawner path pays through RunJudge.
    private void NoteHit(int baseScore)
    {
        state.NoteHit(baseScore);
        PushHud();

        if (feedback != null) feedback.OnCombo(state.combo);
    }

    public void NormalHit()
    {
        NoteHit(scorePerNote);
        state.normalHits++;
    }

    public void GoodHit()
    {
        NoteHit(scorePerGoodNote);
        state.goodHits++;
    }

    public void PerfectHit()
    {
        NoteHit(scorePerPerfectNote);
        state.perfectHits++;
    }

    // Pausing is safe on the new path because nothing there uses WaitForSeconds
    // or Time.deltaTime - note positions come from song time, which stops with
    // the Conductor.
    public void PauseGame()
    {
        Conductor.instance.Pause();
        Time.timeScale = 0f;
    }

    public void ResumeGame()
    {
        Time.timeScale = 1f;
        Conductor.instance.Resume();
    }

    // Hooked to the results screen's Back button by Tools/Rhythm/Wire Song Flow.
    // The front-end lives in another scene, so leaving means loading it again
    // and telling GameStart to open the song list instead of the login screen.
    public void ReturnToSongSelect()
    {
        // 中途离开就关局 / Leaving mid-song walks away from a ranked run that is still open: close
        // it as not cleared, so the record says what happened instead of leaving it open forever.
        // After the results screen the run is already closed and runId is empty.
        if (scored && runId != System.Guid.Empty)
        {
            AbandonRun(runId);
            runId = System.Guid.Empty;
        }

        // Pausing leaves timeScale at 0, and it would stay there in the next
        // scene - every tween and animation in the front-end would freeze.
        Time.timeScale = 1f;

        if (Conductor.instance != null) Conductor.instance.StopSong();

        // 从活动来就回活动 / Back to where the song was picked: the event screen for an event
        // stage, the song list otherwise.
        BootIntent.NextUI = SongSession.ReturnUI ?? SongSelectUI.UIName;
        BootIntent.Beneath = SongSession.ReturnBeneath;
        UI.LoadingScreen.LoadScene(SongSelectUI.SelectScene);
    }

    /// <summary>
    /// Starts the song. Safe to call twice - the second call does nothing.
    ///
    /// Split out of Update so the READY/GO intro can decide the moment instead of
    /// the first tap. PauseMenu calls this at the end of its sequence; with no
    /// PauseMenu in the scene, startOnFirstInput keeps the original behaviour.
    /// </summary>
    public void BeginSong()
    {
        if (startPlaying) return;

        startPlaying = true;

        if (useLegacyNoteHolders)
        {
            theBsLeft.hasStarted = true;
            theBsRight.hasStarted = true;

            theMusic.Play();
        }
        else if (Conductor.instance != null)
        {
            // The run-up has to cover the longest marker-to-button trip,
            // or the earliest notes cannot start at their spawn point.
            Conductor.instance.StartSong(noteSpawner != null ? noteSpawner.GetRequiredLeadIn() : 0f);
        }
    }

    /// <summary>
    /// Puts the run back to zero without starting it.
    ///
    /// Separate from RestartSong because the pause menu's Retry runs the READY/GO
    /// intro in between - it needs the board cleared first and the song started
    /// several seconds later.
    /// </summary>
    public void ResetRun()
    {
        // Back to the start through StopSong rather than Seek(0), so BeginSong
        // applies the run-up again and the first notes still begin at their markers.
        noteSpawner.SeekTo(0f);
        Conductor.instance.StopSong();
        startPlaying = false;

        SyncTuning();
        if (runJudge != null)
        {
            // Awards retuned in the Inspector since the last attempt reach this one.
            runJudge.SetScore(ScoreRulesNow());
            runJudge.Reset();
        }
        else
        {
            state.Reset();
        }
        BeginTrace();

        if (character != null) character.OnReset();

        if (failedText != null) failedText.SetActive(false);
        PushGauges();

        PushHud();
        if (rankingText != null) rankingText.text = "";
        if (rewardsText != null) rewardsText.text = "";
        resultsScreen.SetActive(false);
    }

    /// <summary>Clears the run and starts it straight away, with no intro in between.</summary>
    public void RestartSong()
    {
        ResetRun();
        BeginSong();
    }

    // 旧路径用 / The legacy NoteObject path only.
    public void NoteMissed(NoteType type)
    {
        bool justFailed = state.NoteMissed(type);

        PushHud();

        if (justFailed) FailRun();
    }

    // Out of HP: stop the song and let Update's normal end-of-song branch raise
    // the results screen, so the summary is assembled in exactly one place.
    private void FailRun()
    {
        if (failedText != null) failedText.SetActive(true);
        if (character != null) character.OnFail();

        if (!useLegacyNoteHolders && Conductor.instance != null)
        {
            Conductor.instance.StopSong();
        }
        else if (theMusic != null)
        {
            theMusic.Stop();
        }
    }

    // The tuning stays serialized on this component and the state object only
    // borrows it, so a value edited in the Inspector still reaches the rules.
    // Re-pointed on every reset rather than once at startup, because resizing an
    // array in the Inspector hands back a new instance instead of mutating the
    // old one - the state would otherwise keep grading against the array the
    // scene had when it loaded.
    /// <summary>
    /// 少一份资产就说清楚 / Names any missing tuning asset once, loudly, instead of letting it
    /// surface later as a NullReferenceException from somewhere in the scoring path.
    ///
    /// Does not substitute defaults: a run graded against silently invented windows looks like
    /// it worked and is worse than one that refuses to start.
    /// </summary>
    private void RequireTuning()
    {
        string missing = "";
        if (judge == null) missing += " judge";
        if (health == null) missing += " health";
        if (fever == null) missing += " fever";

        if (missing.Length == 0) return;

        Debug.LogError("[GameManager] Missing tuning asset(s):" + missing +
                       ". Run Tools/Rhythm/Create Tuning Assets, then assign them on this " +
                       "component. Gameplay will not grade correctly until then.", this);
    }

    private void SyncTuning()
    {
        state.health = health;
        state.fever = fever;
        state.multiplierThresholds = multiplierThresholds;

        state.scoreModifier = operatorScoreModifier;
        state.feverModifier = operatorFeverModifier;
        state.passive = operatorPassive;
        state.operatorMaxHp = operatorMaxHp;

        // Reset rewinds the rolls to this, so a retry draws exactly what the first attempt drew.
        state.seed = runSeed;
    }

    /// <summary>The awards as GameManager's fields hold them now - what RunJudge pays and the result describes.</summary>
    private ScoreRules ScoreRulesNow()
    {
        return new ScoreRules
        {
            perfect = scorePerPerfectNote,
            great = scorePerGoodNote,
            hit = scorePerNote,
            holdTick = scorePerHoldTick,
            holdTickInterval = holdTickInterval,
            multiplierThresholds = multiplierThresholds ?? new int[0],
        };
    }

    // ------------------------------------------------------------------- retry

    /// <summary>Wired to the results screen's RETRY button.</summary>
    public void RetryFromResults()
    {
        RequestRetry(() =>
        {
            // The same run-in a pause-menu retry plays; without the pause menu, straight in.
            PauseMenu menu = FindAnyObjectByType<PauseMenu>();
            if (menu != null) menu.RestartWithIntro();
            else RestartSong();
        }, null);
    }

    /// <summary>
    /// 按原来的模式再来一局 / Runs the next attempt in the mode this one was started in, then calls
    /// <paramref name="onReady"/> to actually restart.
    ///
    /// 练习直接开始 / Practice - and the Editor's run-less gameplay scene - restart at once, for free.
    ///
    /// 排位要先开新局 / A ranked retry is a new ranked run: the server opens it and charges the stage
    /// again before anything restarts. The new run is opened FIRST and the abandoned one closed only
    /// once that succeeds - the other order would leave a player who cannot afford the retry with
    /// their current run already thrown away. When it is refused, the reason goes on screen and
    /// nothing moves: the player stays where they were and decides for themselves.
    /// </summary>
    public void RequestRetry(System.Action onReady, System.Action onRefused)
    {
        if (openingRun) return;

        if (!scored)
        {
            onReady();
            return;
        }

        OpenNextRunAsync(onReady, onRefused);
    }

    private async void OpenNextRunAsync(System.Action onReady, System.Action onRefused)
    {
        string stageId = noteSpawner != null && noteSpawner.chart != null ? noteSpawner.chart.stageId : null;

        if (string.IsNullOrEmpty(stageId) || string.IsNullOrEmpty(operatorId))
        {
            ShowNotice("THIS SONG CANNOT START A RANKED RUN");
            if (onRefused != null) onRefused();
            return;
        }

        openingRun = true;
        ShowNotice("OPENING A NEW RUN...", 0f);

        ApiResult<RunTicket> ticket = await Data.Player.PlayerManager.Inst().Api.StartRunAsync(
            stageId, operatorId, SongSession.EventId, SongSession.CampaignStageId);

        openingRun = false;

        // 场景可能已经换了 / The scene may have changed while the request was out.
        if (this == null) return;

        if (!ticket.IsSuccess)
        {
            // "This stage costs 6 and you have 3." - the server's own words, which already say what
            // a player needs to know.
            ShowNotice(ticket.Message);
            if (onRefused != null) onRefused();
            return;
        }

        Data.Player.PlayerData player = Data.Player.PlayerManager.Inst().Get();
        if (player != null) player.ApplyServerState(ticket.Value.Player);

        // Still open when a ranked song is retried part-way; empty after the results screen, which
        // closed it.
        System.Guid abandoned = runId;

        runId = ticket.Value.RunId;
        runSeed = ticket.Value.Seed;
        runCost = ticket.Value.StaminaSpent;

        // 新的一局按它自己的等级 / The new run is reviewed at the level on its own ticket, which a
        // level-up since the last one would have moved.
        ApplyOperatorLevel(ticket.Value.CharacterLevel);

        if (abandoned != System.Guid.Empty) AbandonRun(abandoned);

        HideNotice();
        onReady();
    }

    /// <summary>
    /// 放弃的局要关上 / Closes a run the player walked away from - retried or quit part-way - as
    /// not cleared: it counts for nothing, but the record says it ended rather than leaving it
    /// open forever. Fire and forget; a run left open by a failure here is harmless.
    /// </summary>
    private static async void AbandonRun(System.Guid run)
    {
        ApiResult<RunCompletion> result = await Data.Player.PlayerManager.Inst().Api.CompleteRunAsync(run, false, null);

        if (!result.IsSuccess) Debug.LogWarning($"[GameManager] Could not close abandoned run {run}: {result.Message}");
    }

    /// <summary>The results screen's RETRY says what another attempt costs, before it is pressed.</summary>
    private void LabelRetry()
    {
        if (resultsRetryLabel == null) return;

        resultsRetryLabel.text = scored ? $"RETRY  -{runCost} SANITY"
                               : playMode == PlayMode.Practice ? "RETRY (PRACTICE)"
                               : "RETRY";
    }

    // ------------------------------------------------------------------ notice

    /// <summary>
    /// One line over everything, for as long as <paramref name="seconds"/>, or until replaced when
    /// that is 0. Unscaled time, because the pause menu has the game at timeScale 0.
    /// </summary>
    private void ShowNotice(string text, float seconds = 3.5f)
    {
        Debug.Log("[GameManager] " + text);

        if (noticeRoot == null || noticeText == null) return;

        noticeText.text = text;
        noticeRoot.SetActive(true);

        if (noticeRoutine != null) StopCoroutine(noticeRoutine);
        noticeRoutine = seconds > 0f ? StartCoroutine(HideNoticeAfter(seconds)) : null;
    }

    private IEnumerator HideNoticeAfter(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        noticeRoutine = null;
        HideNotice();
    }

    private void HideNotice()
    {
        if (noticeRoutine != null)
        {
            StopCoroutine(noticeRoutine);
            noticeRoutine = null;
        }

        if (noticeRoot != null) noticeRoot.SetActive(false);
    }

    /// <summary>
    /// 这局的身份 / The run the server opened for this attempt and the seed it issued, taken
    /// before the chart handover clears SongSession.
    ///
    /// 可以是空的 / Empty when the gameplay scene was opened directly in the Editor, which is a
    /// supported way to work: there is no run to close, so <see cref="ReportRun"/> does nothing,
    /// and the seed of 0 still gives the passives a sequence to draw from.
    /// </summary>
    private void CaptureRun()
    {
        runId = SongSession.RunId;
        runSeed = SongSession.Seed;
        runCost = SongSession.StaminaCost;
        playMode = SongSession.Mode;
        scored = playMode == PlayMode.Ranked && runId != System.Guid.Empty;
    }

    /// <summary>
    /// 每帧读一次 / This frame's input as the trace records it and the judge consumes it: the lanes
    /// that went down, and the lanes being held - keyboard or touch. Read once, so what is recorded
    /// and what is judged cannot differ.
    /// </summary>
    private void ReadLanes(out int pressed, out int held)
    {
        pressed = 0;
        held = 0;

        for (int lane = 0; lane < noteSpawner.lanes.Length && lane < 8; lane++)
        {
            KeyCode key = noteSpawner.lanes[lane].key;

            if (Input.GetKeyDown(key) || simulatedKeyDownsThisFrame.Contains(key)) pressed |= 1 << lane;
            if (IsLaneHeld(key)) held |= 1 << lane;
        }
    }

    /// <summary>A fresh trace, at the top of every attempt - a retry included.</summary>
    private void BeginTrace()
    {
        inputTrace.Begin(noteSpawner != null && noteSpawner.lanes != null ? noteSpawner.lanes.Length : 0);
    }

    /// <summary>
    /// 结束时交给服务端 / Hands the result to the server, which reviews it, counts it towards the
    /// missions if it is a clear, and ranks it if it is accepted.
    ///
    /// 不等它 / The results screen does not wait: the player's own numbers are already on it, and
    /// the server's verdict fills in the ranking line when it arrives.
    /// </summary>
    private async void ReportRun(bool won)
    {
        // 练习不上报 / Practice reports nothing: it opened no run, and counts for nothing by design.
        if (!scored)
        {
            ShowRanking(playMode == PlayMode.Practice ? "PRACTICE - NOT SCORED" : "");
            return;
        }

        if (runId == System.Guid.Empty) return;

        // 只报一次 / Cleared first, so a results screen that is somehow shown twice cannot send
        // this twice. The server would refuse the second one anyway - the run is already closed,
        // and the idempotency key is the run id - but not sending it is cheaper than being told.
        System.Guid reporting = runId;
        runId = System.Guid.Empty;

        // 等待之前就定下 / Built before the await: the state is final now, and a retry pressed
        // while the request is in flight would otherwise reset it under the result.
        RunResult outcome = BuildResult();

        ShowRanking("Submitting...");

        ApiResult<RunCompletion> result = await Data.Player.PlayerManager.Inst().Api
            .CompleteRunAsync(reporting, won, outcome);

        if (!result.IsSuccess)
        {
            // 网络问题就先存着 / A network failure is not the end of the result: it waits on the
            // device and goes out on the next sign-in or visit to the song list, under the run's
            // own id as its key, so sending it late can never count it twice. Anything else is a
            // refusal sending again would not change.
            if (RetryPolicy.IsTransient(result.Problem))
            {
                Data.Player.PlayerManager.Inst().Api.Pending.Add(reporting, won, outcome, System.DateTimeOffset.UtcNow);
                Debug.LogWarning($"[GameManager] Run {reporting} could not be sent ({result.Message}); queued.");
                ShowRanking("SAVED - WILL SEND WHEN ONLINE");
                return;
            }

            Debug.LogWarning($"[GameManager] Could not close run {reporting}: {result.Message}");
            ShowRanking("Not recorded");
            return;
        }

        Data.Player.PlayerData player = Data.Player.PlayerManager.Inst().Get();
        if (player != null) player.ApplyServerState(result.Value.Player);

        ScoreReview review = result.Value.Review;

        if (review != null && review.Verdict == ScoreVerdict.Rejected)
        {
            // 开发时要看得到 / Spelled out in the log: a rejection on an honest device is a bug,
            // most often the ruleset - RULESET_MISMATCH means server/data/ruleset.json no longer
            // matches the tuning assets.
            Debug.LogWarning($"[GameManager] Run {reporting} was rejected: {string.Join(", ", review.Reasons)}");
        }

        string verdict = DescribeReview(review);
        Debug.Log($"[GameManager] Run {reporting} closed: {(review != null ? review.Verdict.ToString() : "no result")}. {verdict}");

        ShowRanking(verdict);
        ShowRewards(result.Value.Rewards);
    }

    private void ShowRewards(RunRewards rewards)
    {
        if (this == null || rewardsText == null) return;
        rewardsText.text = DescribeRewards(rewards);
    }

    /// <summary>
    /// "FIRST CLEAR +50 Orundum · FIRST CLEAR Manufacture Blueprint I · LMD ×350 · Orirock ×2 ·
    /// +72 EXP", and on a new line "LEVEL UP 9 → 10 · +2 Originite Prime". Empty when nothing was paid.
    /// </summary>
    private static string DescribeRewards(RunRewards rewards)
    {
        if (rewards == null) return "";

        var parts = new System.Collections.Generic.List<string>();
        if (rewards.FirstClearOrundum > 0) parts.Add("<color=#F5BA0D>FIRST CLEAR</color> +" + rewards.FirstClearOrundum + " " + global::UI.ItemVisual.Name(1));

        // 首通的其他奖励 / What else the first clear paid - a Manufacture blueprint. Null from a
        // server older than the field.
        if (rewards.FirstClearItems != null)
        {
            foreach (Promuse.Contracts.Players.ItemStack item in rewards.FirstClearItems)
            {
                parts.Add("<color=#F5BA0D>FIRST CLEAR</color> " + global::UI.ItemVisual.Name(item.ItemId) +
                          (item.Amount > 1 ? " ×" + global::UI.ItemVisual.Amount(item.Amount) : ""));
            }
        }

        foreach (Promuse.Contracts.Players.ItemStack drop in rewards.Drops)
        {
            parts.Add(global::UI.ItemVisual.Name(drop.ItemId) + " ×" + global::UI.ItemVisual.Amount(drop.Amount));
        }
        if (rewards.Exp > 0) parts.Add("+" + rewards.Exp + " EXP");

        string text = string.Join("  ·  ", parts);
        if (rewards.LevelAfter > rewards.LevelBefore)
        {
            text += "\n<color=#F5BA0D>LEVEL UP</color> " + rewards.LevelBefore + " → " + rewards.LevelAfter +
                    (rewards.PrimeFromLevels > 0 ? "  ·  +" + rewards.PrimeFromLevels + " " + global::UI.ItemVisual.Name(0) : "") +
                    "  ·  Sanity refilled";
        }

        return text;
    }

    /// <summary>
    /// What this run was played under, as the shared ruleset describes it. The server computes the
    /// same fingerprint from server/data/ruleset.json; a difference is the two sides disagreeing
    /// about the rules, and is reported as such instead of as an impossible score.
    /// </summary>
    private RunResult BuildResult()
    {
        Ruleset rules = new Ruleset
        {
            version = Ruleset.CurrentVersion,
            score = ScoreRulesNow(),
            judge = JudgeRules.From(judge),
            health = HealthRules.From(health),
            fever = FeverRules.From(fever),
        };

        // The values the run actually used: CaptureOperator has already applied the fallbacks.
        OperatorRules op = new OperatorRules
        {
            scoreModifier = operatorScoreModifier,
            feverModifier = operatorFeverModifier,
            maxHp = operatorMaxHp,
            passive = PassiveRules.From(operatorPassive),
        };

        if (inputTrace.Overflowed)
        {
            Debug.LogWarning("[GameManager] The input trace hit its frame limit; the server will refuse it as incomplete.");
        }

        // The counters are floats on RunState for the accuracy maths; they only ever hold whole
        // numbers.
        return new RunResult(
            state.score,
            state.maxCombo,
            Mathf.RoundToInt(state.perfectHits),
            Mathf.RoundToInt(state.goodHits),
            Mathf.RoundToInt(state.normalHits),
            Mathf.RoundToInt(state.missedHits),
            state.IsFullCombo,
            RulesetFingerprint.Compute(rules, op),
            TracePacker.Pack(inputTrace.ToArray()));
    }

    private void ShowRanking(string text)
    {
        // 场景可能已经换了 / The scene may have changed while the request was out.
        if (this == null || rankingText == null) return;

        rankingText.text = text;
    }

    private static string DescribeReview(ScoreReview review)
    {
        if (review == null) return "";

        switch (review.Verdict)
        {
            case ScoreVerdict.Accepted:
                string line = review.IsPersonalBest ? "NEW BEST   " : "";
                if (review.AllTimeRank.HasValue) line += "#" + review.AllTimeRank.Value + " all-time";
                if (review.WeeklyRank.HasValue) line += "   #" + review.WeeklyRank.Value + " this week";
                return line;

            case ScoreVerdict.Flagged:
                return "Under review - not ranked yet";

            default:
                return "Not verified - not ranked";
        }
    }

    /// <summary>
    /// Reads the chosen operator's two rhythm modifiers off its metadata.
    ///
    /// 这里是两半相接的地方 / This is where the two halves of the project meet: CharMeta is an
    /// Arknights type and RunState lives in Rhythm.Core, which cannot reference it. GameManager
    /// is in the default assembly and can see both, so it reads the values here and hands
    /// across plain floats.
    ///
    /// Guarded throughout - GetCharMeta() reaches Resources and can come back null or throw
    /// for a save that names an operator whose asset is gone. A missing operator costs the
    /// player their bonus, which is worth a warning; it must not cost them the run.
    /// </summary>
    private void CaptureOperator()
    {
        if (!SongSession.HasCharacter) return;

        operatorId = SongSession.Character.GetId();

        // 票上的等级优先 / The ticket's level for a ranked run, the operator's own for practice or a
        // session entered from the Editor.
        int level = SongSession.CharacterLevel > 0 ? SongSession.CharacterLevel : SongSession.Character.GetLevel();

        try
        {
            CharMeta meta = SongSession.Character.GetCharMeta();
            if (meta == null)
            {
                Debug.LogWarning("[GameManager] The chosen operator has no metadata; " +
                                 "playing without its modifiers.");
                return;
            }

            operatorMeta = meta;
            operatorPassive = meta.GetPassive();
            ApplyOperatorLevel(level);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[GameManager] Could not read the operator's modifiers, " +
                             "playing without them: " + e.Message);
        }
    }

    /// <summary>
    /// 按等级取数值 / The operator's modifiers and max HP at <paramref name="level"/>, through the
    /// shared rules (CharMeta.RulesAt -> OperatorRules.AtLevel) the server reviews with. Those carry
    /// the old fallbacks too: a modifier of 0 is a meta from before the rhythm fields and plays at 1,
    /// and a max HP of 0 leaves HealthSettings to decide.
    /// </summary>
    private void ApplyOperatorLevel(int level)
    {
        if (operatorMeta == null) return;

        OperatorRules rules = operatorMeta.RulesAt(level);
        operatorScoreModifier = rules.scoreModifier;
        operatorFeverModifier = rules.feverModifier;
        operatorMaxHp = rules.maxHp;
    }

    // 资产缺失时不要每帧抛异常 / Guarded on the assets, not just the bars. RequireTuning has
    // already said what is missing; this runs every frame and would otherwise bury that one
    // useful error under a wall of identical NullReferenceExceptions.
    private void PushGauges()
    {
        // 条要读同一个上限 / The bar reads RunState's ceiling, not the asset's: with an
        // operator on 300 HP, scaling against the asset's 100 would peg it past full all run.
        if (hpBar != null && state.MaxHp > 0) hpBar.SetValue(state.hp, state.MaxHp);
        if (feverBar != null && fever != null) feverBar.SetValue(state.feverGauge, fever.maxFever);
    }
}
