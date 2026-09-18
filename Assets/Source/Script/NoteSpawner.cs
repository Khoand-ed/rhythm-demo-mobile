using System.Collections.Generic;
using UnityEngine;

// Walks the chart in song-time order, spawning each note one lead time before
// it is due and retiring it once it is resolved and finished animating.
public class NoteSpawner : MonoBehaviour
{
    public SongChart chart;

    [Tooltip("Tap notes, and the fallback for any type whose own pool is unassigned.")]
    public NotePool pool;

    [Tooltip("Hold notes. Built by Tools/Rhythm/Build Note Prefabs.")]
    public NotePool holdPool;

    [Tooltip("Twin notes. Built by Tools/Rhythm/Build Note Prefabs.")]
    public NotePool twinPool;

    public LaneConfig[] lanes;

    [Tooltip("How long a missed note pauses at the middle zone before disappearing.")]
    public float standAtMiddleDuration = 0.3f;

    // The chart split per lane, with every Twin expanded into one entry in
    // each lane. Lanes can have different lead times, so a single cursor over
    // the chart would spawn one half of a Twin early or late.
    private List<NoteData>[] queueByLane;
    private int[] nextByLane;
    private SongChart queuedChart;

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

    // Built lazily because GameManager.Start swaps in the song select's chart,
    // and that may run after this component's own Start.
    private void EnsureQueues()
    {
        if (queuedChart == chart && queueByLane != null) return;

        queuedChart = chart;
        queueByLane = new List<NoteData>[lanes.Length];
        nextByLane = new int[lanes.Length];

        for (int i = 0; i < lanes.Length; i++) queueByLane[i] = new List<NoteData>();

        if (chart == null) return;

        // The chart is sorted, and appending in chart order keeps every lane's
        // queue sorted too.
        for (int i = 0; i < chart.notes.Count; i++)
        {
            NoteData data = chart.notes[i];

            if (data.type == NoteType.Twin)
            {
                for (int lane = 0; lane < Mathf.Min(2, lanes.Length); lane++)
                {
                    NoteData half = data;
                    half.lane = lane;
                    half.duration = 0f;
                    queueByLane[lane].Add(half);
                }

                continue;
            }

            if (data.lane < 0 || data.lane >= lanes.Length)
            {
                Debug.LogError($"{chart.name}: note {i} is on lane {data.lane}, which does not exist.", chart);
                continue;
            }

            queueByLane[data.lane].Add(data);
        }
    }

    private void SpawnDue(float songTime)
    {
        EnsureQueues();

        for (int laneIndex = 0; laneIndex < lanes.Length; laneIndex++)
        {
            LaneConfig lane = lanes[laneIndex];

            // Without both markers there is no path to travel along; the error
            // is already logged once in Awake rather than every frame.
            if (!lane.IsValid) continue;

            List<NoteData> queue = queueByLane[laneIndex];
            float leadTime = lane.LeadTime(chart.noteSpeed);

            // Each queue is sorted, so the first note that is not due yet
            // means nothing later in this lane is due either.
            while (nextByLane[laneIndex] < queue.Count)
            {
                NoteData data = queue[nextByLane[laneIndex]];
                if (songTime < data.hitTime - leadTime) break;

                NoteView note = PoolFor(data.type).Get();
                note.Bind(data, lane, chart.noteSpeed, middlePos, standAtMiddleDuration);
                note.UpdatePosition(songTime);
                activeByLane[laneIndex].Add(note);
                nextByLane[laneIndex]++;
            }
        }
    }

    private NotePool PoolFor(NoteType type)
    {
        if (type == NoteType.Hold && holdPool != null) return holdPool;
        if (type == NoteType.Twin && twinPool != null) return twinPool;
        return pool;
    }

    private void UpdateActive(float songTime)
    {
        JudgeSettings judge = GameManager.instance.judge;

        for (int laneIndex = 0; laneIndex < activeByLane.Length; laneIndex++)
        {
            List<NoteView> notes = activeByLane[laneIndex];

            for (int i = notes.Count - 1; i >= 0; i--)
            {
                NoteView note = notes[i];
                note.UpdatePosition(songTime);

                // The window is a property of the note type now, not the lane.
                if (!note.Judged && songTime > note.Data.hitTime + judge.MaxWindow(note.Data.type))
                {
                    note.MarkMissed();
                    GameManager.instance.NoteMissedInLane(note.Data.type, laneIndex);
                }

                if (note.IsFinished(songTime))
                {
                    Release(note);
                    notes.RemoveAt(i);
                }
            }
        }
    }

    private void Release(NoteView note)
    {
        NotePool owner = note.Pool != null ? note.Pool : pool;
        owner.Release(note);
    }

    // The note a press in this lane should resolve against: the earliest
    // unjudged note still inside the hit window, or null if there is none.
    // Resolving at most one note per call preserves the old rule that two
    // notes close together can never both consume the same press.
    public NoteView PeekJudgeable(int laneIndex, float songTime)
    {
        if (activeByLane == null || laneIndex < 0 || laneIndex >= activeByLane.Length) return null;

        List<NoteView> notes = activeByLane[laneIndex];
        JudgeSettings judge = GameManager.instance.judge;
        NoteView best = null;

        for (int i = 0; i < notes.Count; i++)
        {
            NoteView note = notes[i];

            if (note.Judged) continue;
            if (Mathf.Abs(songTime - note.Data.hitTime) > judge.MaxWindow(note.Data.type)) continue;
            if (best == null || note.Data.hitTime < best.Data.hitTime) best = note;
        }

        return best;
    }

    // The hold currently being held down in this lane, or null. The chart
    // generator never overlaps two holds in one lane, so there is at most one.
    public NoteView ActiveHold(int laneIndex)
    {
        if (activeByLane == null || laneIndex < 0 || laneIndex >= activeByLane.Length) return null;

        List<NoteView> notes = activeByLane[laneIndex];

        for (int i = 0; i < notes.Count; i++)
        {
            if (notes[i].Holding) return notes[i];
        }

        return null;
    }

    // How much silent run-up the song needs before song time 0 so that every
    // note can begin exactly at its spawn marker. A note is due at
    // hitTime - travelTime; if that is negative the clock has to start there,
    // otherwise the note appears already partway down its path.
    public float GetRequiredLeadIn()
    {
        if (chart == null || lanes == null) return 0f;

        EnsureQueues();

        float required = 0f;

        for (int laneIndex = 0; laneIndex < lanes.Length; laneIndex++)
        {
            if (!lanes[laneIndex].IsValid || queueByLane[laneIndex].Count == 0) continue;

            // Only the earliest note in each lane can set the requirement.
            float first = queueByLane[laneIndex][0].hitTime;
            required = Mathf.Max(required, lanes[laneIndex].LeadTime(chart.noteSpeed) - first);
        }

        return required;
    }

    // Song time at which the first note of the chart leaves its spawn marker,
    // or +infinity for an empty chart. The intro Skip lands just before this.
    public float FirstSpawnTime()
    {
        if (chart == null || lanes == null) return float.PositiveInfinity;

        EnsureQueues();

        float first = float.PositiveInfinity;

        for (int laneIndex = 0; laneIndex < lanes.Length; laneIndex++)
        {
            if (!lanes[laneIndex].IsValid || queueByLane[laneIndex].Count == 0) continue;

            float spawn = queueByLane[laneIndex][0].hitTime - lanes[laneIndex].LeadTime(chart.noteSpeed);
            first = Mathf.Min(first, spawn);
        }

        return first;
    }

    // Clears the board and rewinds the chart cursor to a given song time.
    public void SeekTo(float songTime)
    {
        for (int laneIndex = 0; laneIndex < activeByLane.Length; laneIndex++)
        {
            List<NoteView> notes = activeByLane[laneIndex];

            for (int i = 0; i < notes.Count; i++)
            {
                Release(notes[i]);
            }

            notes.Clear();
        }

        EnsureQueues();

        for (int laneIndex = 0; laneIndex < lanes.Length; laneIndex++)
        {
            List<NoteData> queue = queueByLane[laneIndex];
            int next = 0;

            while (next < queue.Count && queue[next].hitTime < songTime) next++;

            nextByLane[laneIndex] = next;
        }
    }
}
