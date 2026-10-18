using System.Collections.Generic;
using UnityEngine;

// 一帧一帧地判 / The judging, one frame at a time: which notes ran out unplayed, how the holds
// being held are paying and ending, what each press hit, when fever ends, and the passive's
// clock. Everything that turns input into a score, and nothing that draws.
//
// 设备和服务器跑同一份 / The device judges with this, and the server judges the device's input
// trace with this again - the same source, linked into Promuse.Scoring - and compares. That only
// works if a frame is a pure function of what the trace records: the song time judged at, the
// lanes that went down, the lanes held. So:
//
//   - One song time per frame. The device reads the clock once, records it, and hands that value
//     here; nothing in here reads a clock of its own. (GameManager used to read it again for
//     fever and the passive, and the audio clock can move between two reads in one frame.)
//   - A fixed order inside the frame: holds, then presses lane by lane, then misses, then fever,
//     then the passive. (Misses used to come from NoteSpawner's own Update, in whatever order
//     Unity happened to run the two components.)
//   - No multiply-add a compiler could fuse. A phone's IL2CPP build and the server's JIT round
//     each single operation the same way; a fused one rounds once instead of twice. The hold
//     ticks are counted from where the hold began rather than accumulated, for that reason.
//
// 只报告不画 / What it decides is reported through IRunJudgeListener, so GameManager can draw it;
// the server passes no listener. A listener must not feed back into the judging - it is told
// after the fact, and the server never calls it.

/// <summary>What the judge decided, for whoever draws it. Every call happens inside <see cref="RunJudge.Step"/>.</summary>
public interface IRunJudgeListener
{
    /// <summary>A press landed on a note. <paramref name="holdBegins"/> when it is a hold's head: the note stays, being held.</summary>
    void OnHit(int lane, int index, NoteType type, Judgement judgement, float signedDelta, bool holdBegins);

    /// <summary>One tick of a held body was paid.</summary>
    void OnHoldTick(int lane);

    /// <summary>A hold's tail resolved - held to the end, or let go within the Great window of it.</summary>
    void OnHoldEnd(int lane, int index, Judgement judgement);

    /// <summary>A hold was let go too early: the rest of it is lost.</summary>
    void OnHoldDropped(int lane, int index);

    /// <summary>A note ran out of its window unplayed. Told before the miss is counted, which may end the run.</summary>
    void OnMiss(int lane, int index, NoteType type);

    void OnFeverStart();

    void OnFeverEnd();

    /// <summary>HP ran out. Nothing more is judged until <see cref="RunJudge.Reset"/>.</summary>
    void OnFail();
}

/// <summary>
/// Judges a run frame by frame against a chart. Owns which notes are resolved and which holds are
/// held; the score, combo, HP and fever it moves live on the <see cref="RunState"/> it is given.
/// </summary>
public sealed class RunJudge
{
    private readonly List<NoteData>[] lanes;
    private readonly bool[][] judged;
    private readonly int[] firstOpen;

    // 每条轨道最多一个长按 / The chart never overlaps two holds in one lane, so a lane holds at
    // most one: its index, or -1.
    private readonly int[] holding;
    private readonly float[] holdTicksFrom;
    private readonly int[] holdTicksPaid;

    private readonly JudgeSettings judge;
    private readonly RunState state;
    private ScoreRules score;

    private float feverEndsAt;
    private float lastPassiveTime;

    public RunJudge(IReadOnlyList<NoteData> notes, int laneCount, JudgeSettings judge, ScoreRules score, RunState state)
    {
        this.judge = judge;
        this.score = score;
        this.state = state;

        lanes = SplitByLane(notes, laneCount);
        judged = new bool[lanes.Length][];
        for (int lane = 0; lane < lanes.Length; lane++) judged[lane] = new bool[lanes[lane].Count];

        firstOpen = new int[lanes.Length];
        holding = new int[lanes.Length];
        holdTicksFrom = new float[lanes.Length];
        holdTicksPaid = new int[lanes.Length];

        JudgementCount = CountJudgements(notes);

        ClearBoard();
    }

    /// <summary>
    /// Every judgement a full run makes: a Twin is two halves judged apart, a Hold is judged at its
    /// head and its tail. What accuracy and a win's counts are measured against.
    /// </summary>
    public int JudgementCount { get; private set; }

    public int LaneCount
    {
        get { return lanes.Length; }
    }

    /// <summary>True once HP has run out; every later Step does nothing.</summary>
    public bool Failed
    {
        get { return state.failed; }
    }

    /// <summary>One lane's notes in the order they are judged - the order <see cref="IRunJudgeListener"/> indexes.</summary>
    public IReadOnlyList<NoteData> Lane(int lane)
    {
        return lanes[lane];
    }

    /// <summary>Whether that note has been decided: hit, missed, or being held.</summary>
    public bool IsResolved(int lane, int index)
    {
        return lane >= 0 && lane < judged.Length && index >= 0 && index < judged[lane].Length && judged[lane][index];
    }

    /// <summary>
    /// 按轨道拆谱 / The chart split per lane, with every Twin as one half in each of the first two
    /// lanes. NoteSpawner builds its queues from this too, so a note's index means the same note
    /// to the judge and to the screen. A note on a lane that does not exist is left out.
    /// </summary>
    public static List<NoteData>[] SplitByLane(IReadOnlyList<NoteData> notes, int laneCount)
    {
        List<NoteData>[] split = new List<NoteData>[laneCount];
        for (int lane = 0; lane < laneCount; lane++) split[lane] = new List<NoteData>();

        if (notes == null) return split;

        // 谱面已排序 / The chart is sorted by time, and appending in its order keeps each lane sorted.
        for (int i = 0; i < notes.Count; i++)
        {
            NoteData data = notes[i];

            if (data.type == NoteType.Twin)
            {
                for (int lane = 0; lane < Mathf.Min(2, laneCount); lane++)
                {
                    NoteData half = data;
                    half.lane = lane;
                    half.duration = 0f;
                    split[lane].Add(half);
                }

                continue;
            }

            if (data.lane < 0 || data.lane >= laneCount) continue;

            split[data.lane].Add(data);
        }

        return split;
    }

    public static int CountJudgements(IReadOnlyList<NoteData> notes)
    {
        if (notes == null) return 0;

        int count = 0;
        for (int i = 0; i < notes.Count; i++)
        {
            NoteData note = notes[i];
            count += note.type == NoteType.Twin || IsHold(note) ? 2 : 1;
        }

        return count;
    }

    private static bool IsHold(NoteData note)
    {
        return note.type == NoteType.Hold && note.duration > 0f;
    }

    /// <summary>
    /// The awards to pay. Swapped between attempts, so a value retuned in the Inspector reaches the
    /// next run the way it always did; never during one.
    /// </summary>
    public void SetScore(ScoreRules rules)
    {
        score = rules;
    }

    /// <summary>Back to the top: every note open, nothing held, and the RunState reset with it.</summary>
    public void Reset()
    {
        state.Reset();
        ClearBoard();
    }

    private void ClearBoard()
    {
        for (int lane = 0; lane < lanes.Length; lane++)
        {
            System.Array.Clear(judged[lane], 0, judged[lane].Length);
            firstOpen[lane] = 0;
            holding[lane] = -1;
            holdTicksFrom[lane] = 0f;
            holdTicksPaid[lane] = 0;
        }

        feverEndsAt = 0f;

        // 从零算起 / From zero, as GameManager's own clock was: the first frame of the run-in reads
        // a negative song time, and a delta that is not positive pays the passive nothing.
        lastPassiveTime = 0f;
    }

    // ------------------------------------------------------------------ a frame

    /// <summary>
    /// Judges one frame: <paramref name="songTime"/> as the device recorded it, the lanes that went
    /// down this frame and the lanes being held, one bit per lane.
    /// </summary>
    public void Step(float songTime, int pressedMask, int heldMask, IRunJudgeListener listener)
    {
        if (state.failed) return;

        // 长按先 / Holds first, so a hold whose tail completes this frame frees its lane before a
        // new press in that lane is looked at.
        UpdateHolds(songTime, heldMask, listener);

        for (int lane = 0; lane < lanes.Length; lane++)
        {
            if ((pressedMask & (1 << lane)) != 0) Press(lane, songTime, listener);
        }

        Misses(songTime, listener);
        if (state.failed) return;

        UpdateFever(songTime, listener);
        TickPassive(songTime);
    }

    /// <summary>
    /// 手动开狂热 / Opens fever by hand, when the rules do not open it by themselves. Not in the
    /// input trace, so the server cannot replay it - which is why it refuses a ruleset with
    /// fever.autoActivate off.
    /// </summary>
    public void ActivateFever(float songTime, IRunJudgeListener listener)
    {
        if (state.failed) return;
        if (state.ActivateFever()) BeginFeverWindow(songTime, listener);
    }

    private void Press(int lane, float songTime, IRunJudgeListener listener)
    {
        // 一次按只判一个 / One press resolves at most one note: the earliest still open and inside
        // its window. A press with nothing in range does nothing.
        List<NoteData> notes = lanes[lane];
        bool[] done = judged[lane];
        int found = -1;

        for (int i = firstOpen[lane]; i < notes.Count; i++)
        {
            NoteData note = notes[i];
            float window = judge.MaxWindow(note.type);

            // Sorted by time: once a note is further ahead than the widest window, so is the rest.
            if (note.hitTime - songTime > Mathf.Max(judge.tapHit, judge.holdGreat)) break;

            if (done[i]) continue;
            if (Mathf.Abs(songTime - note.hitTime) > window) continue;

            found = i;
            break;
        }

        if (found < 0) return;

        NoteData hit = notes[found];

        // Negative is early. The sign only matters for the FAST/SLOW tag.
        float signedDelta = songTime - hit.hitTime;
        Judgement judgement = judge.Grade(hit.type, Mathf.Abs(signedDelta));
        if (judgement == Judgement.Miss) return;

        // 被动可以升级判定 / The passive may rewrite what landed - after the line above, so no
        // passive turns a press that was out of range into a hit. Everything after reads the
        // rewritten value: a promoted Great pays and looks exactly like a Perfect.
        if (state.passive != null) judgement = state.passive.Regrade(judgement, state);

        Resolve(lane, found);

        bool begins = IsHold(hit);
        if (begins)
        {
            holding[lane] = found;
            holdTicksFrom[lane] = Mathf.Max(songTime, hit.hitTime);
            holdTicksPaid[lane] = 0;
        }

        Award(judgement);

        // 先动作后狂热 / Told before the fever gain, so a hit that opens fever lets the fever pose win.
        if (listener != null) listener.OnHit(lane, found, hit.type, judgement, signedDelta, begins);

        AddFever(GainFor(judgement), songTime, listener);
    }

    /// <summary>
    /// Pays the body of every hold being held and resolves its tail: held to the end is a Perfect,
    /// let go within the Great window of the end still counts, anything earlier drops the rest.
    /// </summary>
    private void UpdateHolds(float songTime, int heldMask, IRunJudgeListener listener)
    {
        for (int lane = 0; lane < lanes.Length; lane++)
        {
            int index = holding[lane];
            if (index < 0) continue;

            NoteData hold = lanes[lane][index];
            float end = hold.hitTime + hold.duration;

            PayTicks(lane, songTime, end, listener);

            if (songTime >= end)
            {
                CompleteHold(lane, index, Judgement.Perfect, songTime, listener);
                continue;
            }

            if ((heldMask & (1 << lane)) != 0) continue;

            float early = end - songTime;

            if (early <= judge.holdPerfect) CompleteHold(lane, index, Judgement.Perfect, songTime, listener);
            else if (early <= judge.holdGreat) CompleteHold(lane, index, Judgement.Great, songTime, listener);
            else
            {
                holding[lane] = -1;

                // 只断连, 不扣血 / Drops the combo and the Full Combo, costs no HP, and the tail
                // counts as a miss on the results screen.
                state.HoldDropped();
                if (listener != null) listener.OnHoldDropped(lane, index);
            }
        }
    }

    /// <summary>
    /// 从开头算, 不累加 / Counted as whole intervals since the hold began, and paid the difference
    /// from what was paid already - never by adding interval after interval to a running time,
    /// which is the multiply-add two compilers may round differently.
    /// </summary>
    private void PayTicks(int lane, float songTime, float end, IRunJudgeListener listener)
    {
        if (score.holdTickInterval <= 0f) return;

        float until = Mathf.Min(songTime, end);
        float held = until - holdTicksFrom[lane];
        int due = Mathf.FloorToInt(held / score.holdTickInterval);

        for (int tick = holdTicksPaid[lane]; tick < due; tick++)
        {
            state.HoldTick(score.holdTick);
            if (listener != null) listener.OnHoldTick(lane);
        }

        if (due > holdTicksPaid[lane]) holdTicksPaid[lane] = due;
    }

    private void CompleteHold(int lane, int index, Judgement judgement, float songTime, IRunJudgeListener listener)
    {
        holding[lane] = -1;

        Award(judgement == Judgement.Perfect ? Judgement.Perfect : Judgement.Great);
        if (listener != null) listener.OnHoldEnd(lane, index, judgement);

        AddFever(GainFor(judgement), songTime, listener);
    }

    /// <summary>
    /// Every open note whose window has closed, lane by lane in time order. A miss can end the
    /// run; nothing after it is judged.
    /// </summary>
    private void Misses(float songTime, IRunJudgeListener listener)
    {
        for (int lane = 0; lane < lanes.Length; lane++)
        {
            List<NoteData> notes = lanes[lane];
            bool[] done = judged[lane];

            for (int i = firstOpen[lane]; i < notes.Count; i++)
            {
                NoteData note = notes[i];

                // Not even due yet: nothing at or after it has run out either.
                if (note.hitTime > songTime) break;

                if (done[i]) continue;
                if (songTime <= note.hitTime + judge.MaxWindow(note.type)) continue;

                Resolve(lane, i);

                // 先反应后结算 / Told before it is counted, which can end the run: the fall must be
                // the last thing the screen says.
                if (listener != null) listener.OnMiss(lane, i, note.type);

                if (state.NoteMissed(note.type))
                {
                    if (listener != null) listener.OnFail();
                    return;
                }
            }
        }
    }

    private void UpdateFever(float songTime, IRunJudgeListener listener)
    {
        if (!state.feverActive) return;

        if (state.TickFever(feverEndsAt - songTime) && listener != null) listener.OnFeverEnd();
    }

    /// <summary>
    /// 用歌曲时间 / The passive's clock is song time between recorded frames, so it cannot tick
    /// through a pause - a paused frame is not judged at all - and a retry starts it from zero.
    /// </summary>
    private void TickPassive(float songTime)
    {
        float delta = songTime - lastPassiveTime;
        lastPassiveTime = songTime;

        // 负数是倒回去了 / A negative delta is the clock going back, as it does at the top of a
        // run: skipped, never paid or rewound.
        if (delta > 0f) state.TickPassive(delta);
    }

    // ------------------------------------------------------------------ awards

    private void Award(Judgement judgement)
    {
        switch (judgement)
        {
            case Judgement.Perfect:
                state.NoteHit(score.perfect);
                state.perfectHits++;
                break;

            case Judgement.Great:
                state.NoteHit(score.great);
                state.goodHits++;
                break;

            default:
                state.NoteHit(score.hit);
                state.normalHits++;
                break;
        }
    }

    private float GainFor(Judgement judgement)
    {
        return state.fever != null ? state.fever.GainFor(judgement) : 0f;
    }

    private void AddFever(float amount, float songTime, IRunJudgeListener listener)
    {
        if (state.AddFever(amount)) BeginFeverWindow(songTime, listener);
    }

    private void BeginFeverWindow(float songTime, IRunJudgeListener listener)
    {
        // 开窗时结算一次加成 / The passive's bonus is decided once, as the window opens: the
        // deadline is a fixed point in song time, not a target that moves while fever burns.
        float extra = state.passive != null ? state.passive.ExtraFeverSeconds(state) : 0f;
        float duration = state.fever != null ? state.fever.feverDuration : 0f;

        feverEndsAt = songTime + duration + extra;

        if (listener != null) listener.OnFeverStart();
    }

    private void Resolve(int lane, int index)
    {
        judged[lane][index] = true;

        bool[] done = judged[lane];
        while (firstOpen[lane] < done.Length && done[firstOpen[lane]]) firstOpen[lane]++;
    }
}
