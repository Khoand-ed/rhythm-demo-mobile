using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameManager : MonoBehaviour
{

    public AudioSource theMusic;

    public bool startPlaying;

    public BeatScroller theBsRight;

    public BeatScroller theBsLeft;

    public static GameManager instance;

    public int currentScore;
    public int scorePerNote = 100;
    public int scorePerGoodNote = 125;
    public int scorePerPerfectNote = 150;

    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI multiText;

    public int currentMultiplier;
    public int multiplierTracker;
    public int[] multiplierThresholds;

    public int currentCombo;

    public float totalNotes;
    public float normalHits;
    public float goodHits;
    public float perfectHits;
    public float missedHits;

    public GameObject resultsScreen;
    public TextMeshProUGUI percentHitText, normalsText, goodsText, perfectsText, missesText, rankText, finalScoreText;

    // Where the character stands. A missed note travels here and pauses before disappearing.
    public Transform middleZoneMarker;

    public NoteSpawner noteSpawner;

    // Runs the old NoteHolder/BeatScroller path instead of the spawner, so the
    // two can be compared side by side. Removed once the new path is signed off.
    public bool useLegacyNoteHolders = false;

    // Hoisted off NoteObject: every note carried the same three prefabs and the
    // same two windows, so they belong in one place now that judging lives here.
    public GameObject hitEffect, goodEffect, perfectEffect;
    public float perfectWindow = 0.05f;
    public float goodWindow = 0.1f;

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
        scoreText.text = "Score: 0";
        multiText.text = "0";
        currentMultiplier = 1;

        if (useLegacyNoteHolders)
        {
            totalNotes = FindObjectsByType<NoteObject>().Length;
        }
        else
        {
            if (Conductor.instance == null || noteSpawner == null || noteSpawner.chart == null)
            {
                Debug.LogError("The spawner path needs a Conductor, a NoteSpawner and a chart. " +
                               "Run Tools/Rhythm/Set Up Note System and Tools/Rhythm/Extract Chart From Open Scene, " +
                               "or tick useLegacyNoteHolders to stay on the old system.");
            }

            totalNotes = noteSpawner != null && noteSpawner.chart != null ? noteSpawner.chart.notes.Count : 0;
        }

}

// Update is called once per frame
void Update()
    {
        if(!startPlaying)
        {
            if(Input.anyKeyDown)
            {
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
        }else
        {
            HandleNoteInput();

            if(SongFinished() && !resultsScreen.activeInHierarchy)
            {
                resultsScreen.SetActive(true);

                normalsText.text = "" + normalHits;
                goodsText.text = goodHits.ToString();
                perfectsText.text = perfectHits.ToString();
                missesText.text = "" + missedHits;

                float totalHit = normalHits + goodHits + perfectHits;
                float percentHit = (totalHit / totalNotes) * 100f;

                percentHitText.text = percentHit.ToString("F1") + "%";

                rankText.text = CalculateRank(percentHit);

                finalScoreText.text = currentScore.ToString();
            }
        }
    }

    // The Conductor schedules playback a moment ahead, so theMusic.isPlaying is
    // briefly false right after the song starts - only the legacy path can use it.
    private bool SongFinished()
    {
        return useLegacyNoteHolders ? !theMusic.isPlaying : Conductor.instance.IsFinished;
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

        float songTime = Conductor.instance.SongTime;

        for (int laneIndex = 0; laneIndex < noteSpawner.lanes.Length; laneIndex++)
        {
            KeyCode key = noteSpawner.lanes[laneIndex].key;

            if (!Input.GetKeyDown(key) && !simulatedKeyDownsThisFrame.Contains(key))
            {
                continue;
            }

            NoteView note = noteSpawner.PeekJudgeable(laneIndex, songTime);

            // A press with nothing in range does nothing, as before.
            if (note == null) continue;

            JudgeNote(note, songTime);
        }
    }

    // The three-tier grading NoteObject.TryHit() used to do, moved here so the
    // windows live in one place instead of on every note.
    private void JudgeNote(NoteView note, float songTime)
    {
        Vector3 hitAt = note.transform.position;
        float delta = Mathf.Abs(songTime - note.Data.hitTime);

        note.MarkHit();

        if (delta <= perfectWindow)
        {
            Debug.Log("Perfect");
            PerfectHit();
            Instantiate(perfectEffect, hitAt, perfectEffect.transform.rotation);
        }
        else if (delta <= goodWindow)
        {
            Debug.Log("Good");
            GoodHit();
            Instantiate(goodEffect, hitAt, goodEffect.transform.rotation);
        }
        else
        {
            Debug.Log("Hit");
            NormalHit();
            Instantiate(hitEffect, hitAt, hitEffect.transform.rotation);
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

        if (currentMultiplier - 1 < multiplierThresholds.Length)
        {
            multiplierTracker++;

            if (multiplierThresholds[currentMultiplier - 1] <= multiplierTracker)
            {
                multiplierTracker = 0;
                currentMultiplier++;
            }
        }

        currentScore += baseScore * currentMultiplier;
        currentCombo++;

        multiText.text = currentCombo.ToString();
        scoreText.text = "Score: " + currentScore;
    }

    public void NormalHit()
    {
        NoteHit(scorePerNote);
        normalHits++;
    }

    public void GoodHit()
    {
        NoteHit(scorePerGoodNote);
        goodHits++;
    }

    public void PerfectHit()
    {
        NoteHit(scorePerPerfectNote);
        perfectHits++;
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

    public void RestartSong()
    {
        // Restart through StartSong rather than Seek(0), so the run-up is
        // applied again and the first notes still begin at their markers.
        noteSpawner.SeekTo(0f);
        Conductor.instance.StopSong();
        Conductor.instance.StartSong(noteSpawner.GetRequiredLeadIn());

        currentScore = 0;
        currentCombo = 0;
        currentMultiplier = 1;
        multiplierTracker = 0;
        normalHits = 0;
        goodHits = 0;
        perfectHits = 0;
        missedHits = 0;

        scoreText.text = "Score: 0";
        multiText.text = "0";
        resultsScreen.SetActive(false);
    }

    public void NoteMissed()
    {
        Debug.Log("Missed Notes");

        currentMultiplier = 1;
        multiplierTracker = 0;
        currentCombo = 0;

        multiText.text = currentCombo.ToString();

        missedHits++;
    }
}
