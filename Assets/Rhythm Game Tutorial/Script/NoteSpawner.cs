using System.Collections.Generic;
using UnityEngine;

// Walks the chart in song-time order, spawning each note one lead time before
// it is due and retiring it once it is resolved and finished animating.
public class NoteSpawner : MonoBehaviour
{
    public SongChart chart;

    public NotePool pool;

    public LaneConfig[] lanes;

    [Tooltip("How long a missed note pauses at the middle zone before disappearing.")]
    public float standAtMiddleDuration = 0.3f;

    private int nextIndex;
    private List<NoteView>[] activeByLane;
    private Vector3 middlePos;

    void Awake()
    {
        if (lanes == null) lanes = new LaneConfig[0];

        activeByLane = new List<NoteView>[lanes.Length];

        for (int i = 0; i < lanes.Length; i++)
        {
            activeByLane[i] = new List<NoteView>();

            if (!lanes[i].IsValid)
            {
                Debug.LogError($"Lane {i} ({lanes[i].name}) is missing its spawn point or hit point. " +
                               "Run Tools/Rhythm/Set Up Note System.", this);
            }
        }
    }

    void Start()
    {
        middlePos = GameManager.instance.GetMiddleZonePosition();
    }

    void Update()
    {
        if (chart == null || Conductor.instance == null) return;
        if (!Conductor.instance.IsPlaying || Conductor.instance.IsPaused) return;

        float songTime = Conductor.instance.SongTime;

        SpawnDue(songTime);
        UpdateActive(songTime);
    }

    private void SpawnDue(float songTime)
    {
        while (nextIndex < chart.notes.Count)
        {
            NoteData data = chart.notes[nextIndex];

            if (data.lane < 0 || data.lane >= lanes.Length)
            {
                Debug.LogError($"{chart.name}: note {nextIndex} is on lane {data.lane}, which does not exist.", chart);
                nextIndex++;
                continue;
            }

            LaneConfig lane = lanes[data.lane];

            // Without both markers there is no path to travel along; the error
            // is already logged once in Awake rather than every frame.
            if (!lane.IsValid)
            {
                nextIndex++;
                continue;
            }

            // The chart is sorted, so the first note that is not due yet means
            // nothing later is due either.
            if (songTime < data.hitTime - lane.LeadTime(chart.noteSpeed)) break;

            NoteView note = pool.Get();
            note.Bind(data, lane, chart.noteSpeed, middlePos, standAtMiddleDuration);
            note.UpdatePosition(songTime);
            activeByLane[data.lane].Add(note);
            nextIndex++;
        }
    }

    private void UpdateActive(float songTime)
    {
        for (int laneIndex = 0; laneIndex < activeByLane.Length; laneIndex++)
        {
            List<NoteView> notes = activeByLane[laneIndex];
            float window = lanes[laneIndex].hitWindow;

            for (int i = notes.Count - 1; i >= 0; i--)
            {
                NoteView note = notes[i];
                note.UpdatePosition(songTime);

                if (!note.Judged && songTime > note.Data.hitTime + window)
                {
                    note.MarkMissed();
                    GameManager.instance.NoteMissed();
                }

                if (note.IsFinished(songTime))
                {
                    pool.Release(note);
                    notes.RemoveAt(i);
                }
            }
        }
    }

    // The note a press in this lane should resolve against: the earliest
    // unjudged note still inside the hit window, or null if there is none.
    // Resolving at most one note per call preserves the old rule that two
    // notes close together can never both consume the same press.
    public NoteView PeekJudgeable(int laneIndex, float songTime)
    {
        if (activeByLane == null || laneIndex < 0 || laneIndex >= activeByLane.Length) return null;

        List<NoteView> notes = activeByLane[laneIndex];
        float window = lanes[laneIndex].hitWindow;
        NoteView best = null;

        for (int i = 0; i < notes.Count; i++)
        {
            NoteView note = notes[i];

            if (note.Judged) continue;
            if (Mathf.Abs(songTime - note.Data.hitTime) > window) continue;
            if (best == null || note.Data.hitTime < best.Data.hitTime) best = note;
        }

        return best;
    }

    // How much silent run-up the song needs before song time 0 so that every
    // note can begin exactly at its spawn marker. A note is due at
    // hitTime - travelTime; if that is negative the clock has to start there,
    // otherwise the note appears already partway down its path.
    public float GetRequiredLeadIn()
    {
        if (chart == null || lanes == null) return 0f;

        float required = 0f;

        for (int i = 0; i < chart.notes.Count; i++)
        {
            NoteData data = chart.notes[i];

            if (data.lane < 0 || data.lane >= lanes.Length) continue;
            if (!lanes[data.lane].IsValid) continue;

            required = Mathf.Max(required, lanes[data.lane].LeadTime(chart.noteSpeed) - data.hitTime);
        }

        return required;
    }

    // Clears the board and rewinds the chart cursor to a given song time.
    public void SeekTo(float songTime)
    {
        for (int laneIndex = 0; laneIndex < activeByLane.Length; laneIndex++)
        {
            List<NoteView> notes = activeByLane[laneIndex];

            for (int i = 0; i < notes.Count; i++)
            {
                pool.Release(notes[i]);
            }

            notes.Clear();
        }

        nextIndex = 0;

        while (nextIndex < chart.notes.Count && chart.notes[nextIndex].hitTime < songTime)
        {
            nextIndex++;
        }
    }
}
