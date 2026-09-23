using System.Collections;
using System.Collections.Generic;
using Data.Char;
using Data.Mission;
using Tools;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class GameManager : MonoBehaviour
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

    private float feverEndsAtSongTime;

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

        scoreText.text = "Score: 0";
        multiText.text = "0";

        SyncTuning();
        state.Reset();

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

            state.totalNotes = noteSpawner != null ? CountJudgements(noteSpawner.chart) : 0;

            ApplyChartToConductor();
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
            HandleNoteInput();
            UpdateFever();

            if(SongFinished() && !resultsScreen.activeInHierarchy)
            {
                resultsScreen.SetActive(true);

                // 通关才算一次 / A run that ran out of HP is not a clear, so it does not feed the
                // "clear any song" missions. The activeInHierarchy guard above already makes this
                // fire once per run rather than once per frame, and Restart turns the screen back
                // off, which is what lets a second run count again.
                if (!state.failed) MissionManager.Inst().Notify(MissionGoal.PlaySong);

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

    // Accuracy is hits over judgements, not over chart entries: a Twin is two
    // halves judged separately, and a Hold is judged at its head and its tail.
    private static int CountJudgements(SongChart chart)
    {
        if (chart == null) return 0;

        int count = 0;

        for (int i = 0; i < chart.notes.Count; i++)
        {
            NoteData note = chart.notes[i];
            bool isHold = note.type == NoteType.Hold && note.duration > 0f;
            count += note.type == NoteType.Twin || isHold ? 2 : 1;
        }

        return count;
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

    // The same one-note-per-key-per-frame rule, except the candidate comes from
    // the spawner's active list and "in range" is a song time window rather
    // than a collider overlap.
    private void HandleSpawnedNoteInput()
    {
        // Start() has already logged what is missing if either is absent.
        if (noteSpawner == null || noteSpawner.lanes == null || Conductor.instance == null) return;

        // Song time is frozen while paused, so a press would be judged against
        // a stale clock, and letting go of a hold would drop it.
        if (Conductor.instance.IsPaused) return;

        float songTime = Conductor.instance.SongTime;

        // Holds first, so a hold whose tail completes this frame frees its
        // lane before a new press in that lane is looked at.
        UpdateHolds(songTime);

        for (int laneIndex = 0; laneIndex < noteSpawner.lanes.Length; laneIndex++)
        {
            KeyCode key = noteSpawner.lanes[laneIndex].key;

            if (!Input.GetKeyDown(key) && !simulatedKeyDownsThisFrame.Contains(key))
            {
                continue;
            }

            // Every press answers, note or not - a button that reacts only
            // when something is there to hit feels laggy.
            PunchButton(key);

            NoteView note = noteSpawner.PeekJudgeable(laneIndex, songTime);

            // A press with nothing in range does nothing, as before.
            if (note == null) continue;

            JudgeNote(note, laneIndex, songTime);
        }
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

    // Grading now runs through JudgeSettings, so Tap/Twin and Hold can have
    // different windows and a press outside the widest one resolves nothing.
    private void JudgeNote(NoteView note, int laneIndex, float songTime)
    {
        Vector3 hitAt = note.transform.position;

        // Negative is early. The sign only matters for the FAST/SLOW tag.
        float signedDelta = songTime - note.Data.hitTime;
        Judgement judgement = judge.Grade(note.Data.type, Mathf.Abs(signedDelta));

        // PeekJudgeable already filtered by MaxWindow, so a Miss here means the
        // press was out of range and should simply not consume the note.
        if (judgement == Judgement.Miss) return;

        if (note.IsHold) note.BeginHold(songTime);
        else note.MarkHit();

        switch (judgement)
        {
            case Judgement.Perfect: PerfectHit(); break;
            case Judgement.Great: GoodHit(); break;
            default: NormalHit(); break;
        }

        if (feedback != null)
        {
            feedback.OnHit(laneIndex, LaneButtonPos(laneIndex), judgement, signedDelta, note.Data.type);
        }
        else
        {
            SpawnLegacyEffect(judgement, hitAt);
        }

        AddFever(fever.GainFor(judgement));
    }

    private void SpawnLegacyEffect(Judgement judgement, Vector3 at)
    {
        GameObject effect = judgement == Judgement.Perfect ? perfectEffect
                          : judgement == Judgement.Great ? goodEffect
                          : hitEffect;

        if (effect != null) Instantiate(effect, at, effect.transform.rotation);
    }

    // Scores the body of every hold being held, and resolves its tail: held to
    // the end is a Perfect, let go within the Great window of the end still
    // counts, and anything earlier drops the rest of the note.
    private void UpdateHolds(float songTime)
    {
        for (int laneIndex = 0; laneIndex < noteSpawner.lanes.Length; laneIndex++)
        {
            NoteView hold = noteSpawner.ActiveHold(laneIndex);
            if (hold == null) continue;

            int ticks = hold.ConsumeHoldTicks(songTime, holdTickInterval);
            for (int i = 0; i < ticks; i++)
            {
                HoldTick();
                if (feedback != null) feedback.OnHoldTick(LaneButtonPos(laneIndex));
            }

            if (songTime >= hold.EndTime)
            {
                CompleteHold(hold, laneIndex, Judgement.Perfect);
                continue;
            }

            if (IsLaneHeld(noteSpawner.lanes[laneIndex].key)) continue;

            float early = hold.EndTime - songTime;

            if (early <= judge.holdPerfect) CompleteHold(hold, laneIndex, Judgement.Perfect);
            else if (early <= judge.holdGreat) CompleteHold(hold, laneIndex, Judgement.Great);
            else
            {
                hold.MarkDropped(songTime);
                HoldDropped();
                if (feedback != null) feedback.OnMiss(laneIndex, LaneButtonPos(laneIndex));
            }
        }
    }

    private void CompleteHold(NoteView hold, int laneIndex, Judgement judgement)
    {
        Vector3 at = hold.transform.position;
        hold.MarkHit();

        if (judgement == Judgement.Perfect) PerfectHit();
        else GoodHit();

        if (feedback != null) feedback.OnHoldEnd(laneIndex, LaneButtonPos(laneIndex), judgement);
        else SpawnLegacyEffect(judgement, at);

        AddFever(fever.GainFor(judgement));
    }

    // Called by NoteSpawner when a note runs out of window unplayed, so the
    // miss can be shown in the lane it happened in.
    public void NoteMissedInLane(NoteType type, int laneIndex)
    {
        NoteMissed(type);

        if (feedback != null && noteSpawner != null && laneIndex >= 0 && laneIndex < noteSpawner.lanes.Length)
        {
            feedback.OnMiss(laneIndex, LaneButtonPos(laneIndex));
        }
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

    private void NoteHit(int baseScore)
    {
        Debug.Log("Hit On Time");

        state.NoteHit(baseScore);

        multiText.text = state.combo.ToString();
        scoreText.text = "Score: " + state.score;

        if (feedback != null) feedback.OnCombo(state.combo);
    }

    // Awarded per 100ms of a held note's body. Deliberately does not touch
    // combo or the multiplier - the spec says only the head and tail do.
    public void HoldTick()
    {
        state.HoldTick(scorePerHoldTick);
        scoreText.text = "Score: " + state.score;
    }

    // Releasing a hold early kills the rest of the note: the spec drops the
    // combo and the Full Combo, but charges no HP for it. The unplayed tail
    // still counts as a miss on the results screen, or accuracy would ignore it.
    public void HoldDropped()
    {
        state.HoldDropped();

        multiText.text = state.combo.ToString();
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
        // Pausing leaves timeScale at 0, and it would stay there in the next
        // scene - every tween and animation in the front-end would freeze.
        Time.timeScale = 1f;

        if (Conductor.instance != null) Conductor.instance.StopSong();

        BootIntent.NextUI = SongSelectUI.UIName;
        SceneManager.LoadScene(SongSelectUI.SelectScene);
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
        state.Reset();

        if (failedText != null) failedText.SetActive(false);
        PushGauges();

        scoreText.text = "Score: 0";
        multiText.text = "0";
        resultsScreen.SetActive(false);
    }

    /// <summary>Clears the run and starts it straight away, with no intro in between.</summary>
    public void RestartSong()
    {
        ResetRun();
        BeginSong();
    }

    public void NoteMissed(NoteType type)
    {
        bool justFailed = state.NoteMissed(type);

        multiText.text = state.combo.ToString();
        PushGauges();

        if (justFailed) FailRun();
    }

    public void Damage(int amount)
    {
        bool justFailed = state.Damage(amount);

        PushGauges();

        if (justFailed) FailRun();
    }

    public void Heal(int amount)
    {
        state.Heal(amount);
        PushGauges();
    }

    // Out of HP: stop the song and let Update's normal end-of-song branch raise
    // the results screen, so the summary is assembled in exactly one place.
    private void FailRun()
    {
        if (failedText != null) failedText.SetActive(true);

        if (!useLegacyNoteHolders && Conductor.instance != null)
        {
            Conductor.instance.StopSong();
        }
        else if (theMusic != null)
        {
            theMusic.Stop();
        }
    }

    private void AddFever(float amount)
    {
        if (state.AddFever(amount)) BeginFeverWindow();

        PushGauges();
    }

    public void ActivateFever()
    {
        if (state.ActivateFever()) BeginFeverWindow();
    }

    // The deadline is the half RunState cannot own: it needs a clock, and this one
    // has to be song time.
    private void BeginFeverWindow()
    {
        feverEndsAtSongTime = SongTimeNow() + fever.feverDuration;
    }

    // Fever runs on song time rather than Time.time, so pausing and restarting
    // cannot desync it - the same reason the note path avoids WaitForSeconds.
    private void UpdateFever()
    {
        if (!state.feverActive)
        {
            if (!fever.autoActivate && state.feverGauge >= fever.maxFever
                && Input.GetKeyDown(fever.manualActivateKey))
            {
                ActivateFever();
            }

            return;
        }

        state.TickFever(feverEndsAtSongTime - SongTimeNow());

        PushGauges();
    }

    private float SongTimeNow()
    {
        return Conductor.instance != null ? Conductor.instance.SongTime : 0f;
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

        try
        {
            CharMeta meta = SongSession.Character.GetCharMeta();
            if (meta == null)
            {
                Debug.LogWarning("[GameManager] The chosen operator has no metadata; " +
                                 "playing without its modifiers.");
                return;
            }

            // 0 说明这份 meta 是模板升级前生成的 / A zero means the asset predates the rhythm
            // fields, not that the operator is meant to score nothing. Fall back to neutral.
            float score = meta.GetScoreModifier();
            float feverGain = meta.GetFeverModifier();

            operatorScoreModifier = score > 0f ? score : 1f;
            operatorFeverModifier = feverGain > 0f ? feverGain : 1f;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[GameManager] Could not read the operator's modifiers, " +
                             "playing without them: " + e.Message);
        }
    }

    // 资产缺失时不要每帧抛异常 / Guarded on the assets, not just the bars. RequireTuning has
    // already said what is missing; this runs every frame and would otherwise bury that one
    // useful error under a wall of identical NullReferenceExceptions.
    private void PushGauges()
    {
        if (hpBar != null && health != null) hpBar.SetValue(state.hp, health.maxHp);
        if (feverBar != null && fever != null) feverBar.SetValue(state.feverGauge, fever.maxFever);
    }
}
