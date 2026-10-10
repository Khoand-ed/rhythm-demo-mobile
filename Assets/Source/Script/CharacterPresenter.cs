using System;
using System.Collections.Generic;
using Data.Char;
using Spine.Unity;
using UnityEngine;

/// <summary>
/// 舞台中央的干员 / The operator standing in the middle of the rhythm stage, reacting to the run.
///
/// 只管表现 / Presentation only. GameManager calls the On* methods after the judge has decided, and
/// nothing here is read back by scoring, HP or fever. Nor does it draw on RunState's rolls: the
/// server replays a run from its seed, and a cosmetic choice taken from that stream would shift
/// every roll after it. Each choice below comes from the event itself - the lane, the note type -
/// and never from chance.
///
/// 动画名是数据 / Animation names are data, not code. Every reaction is a Clip: names to try, where
/// to start and stop, how fast. A rig without the first name uses the next, and a rig with none of
/// them skips that reaction. The defaults are the names Arknights Spine rigs share (Idle, Skill_2,
/// Stun, Die...), so any operator exported that way plays without edits; art drawn later can name its
/// clips anything and set them here.
///
/// 一条轨道 / One track does everything. Idle is always either playing or queued behind the current
/// reaction, so every change is a blend from a pose that was really on screen. Spine will not blend
/// into an entry from a track that was empty, which is what a second "overlay" track would do on
/// every hit - the character would pop into each strike.
///
/// 没有骨骼就什么也不画 / An operator with no battle rig leaves the stage empty, which is how the
/// scene looked before this existed.
///
/// Built by Tools/Rhythm/Set Up Character Stage.
/// </summary>
public class CharacterPresenter : MonoBehaviour
{
    [Serializable]
    public class Clip
    {
        [Tooltip("Animation names to try in order; the first one the rig has is used. None found = skipped.")]
        public string[] names = new string[0];

        [Tooltip("Where in the animation to begin, seconds. Skips a wind-up that would make a reaction land late.")]
        public float from;

        [Tooltip("Where to stop, seconds. 0 plays to the end.")]
        public float to;

        [Tooltip("Playback speed.")]
        public float speed = 1f;

        [Tooltip("Seconds to cross-fade in. Short: the next hit is often 0.2s away.")]
        public float blend = 0.04f;

        public Clip() { }

        public Clip(float from, float to, float speed, params string[] names)
        {
            this.from = from;
            this.to = to;
            this.speed = speed;
            this.names = names;
        }
    }

    [Header("Placement")]
    [Tooltip("How tall the operator stands on screen, in world units.")]
    public float targetHeight = 3.8f;

    [Tooltip("From this object, in world units.")]
    public Vector2 offset = Vector2.zero;

    [Tooltip("Below the notes and the lane buttons, which are at 0.")]
    public int sortingOrder = -5;

    [Tooltip("The way the rig faces as exported. Attacks flip it towards the lane.")]
    public bool facesRight = true;

    [Tooltip("Which lane index is on the left. The other one is on the right.")]
    public int leftLane = 0;

    [Tooltip("Shown when the run has no operator, which is what opening the scene straight from the " +
             "Editor gives. Leave empty to show nobody then.")]
    public string previewOperatorId = "AMIYA";

    // 没有初始值 / No initialisers on purpose: Unity hands every serialized Clip back as an empty
    // instance, never null, so "unset" can only mean "no names". FillDefaults fills those in - from
    // Reset() when the component is added in the Editor, and from Show() for one added at run time.
    [Header("Reactions")]
    public Clip idle;
    public Clip enter;
    public Clip tap;
    public Clip twin;
    public Clip holdLoop;
    public Clip holdEnd;
    public Clip miss;
    public Clip fever;
    public Clip fail;
    public Clip win;

    [Header("Beat")]
    [Tooltip("How many beats one idle loop spans. 1 = the idle breathes once per beat.")]
    public float beatsPerIdleLoop = 1f;

    [Tooltip("Lock the idle to the chart's beat grid when it has one. Without a grid the idle still runs " +
             "at the chart's tempo, just not aligned to a downbeat.")]
    public bool lockIdleToBeat = true;

    [Header("Fever")]
    [Tooltip("Multiplied over the whole rig while fever burns.")]
    public Color feverTint = new Color(1f, 0.7f, 0.86f, 1f);

    public float tintSpeed = 6f;

    [Tooltip("Seconds to blend between reactions when a clip does not say.")]
    public float defaultBlend = 0.06f;

    private SkeletonAnimation operatorRig;
    private Spine.TrackEntry idleEntry;
    private readonly Dictionary<Clip, Spine.Animation> resolved = new Dictionary<Clip, Spine.Animation>();

    private int holdsDown;
    private int lastTwinFrame = -1;
    private bool feverOn;
    private bool failed;
    private Color tint = Color.white;

    /// <summary>Whether anybody is standing on the stage.</summary>
    public bool HasOperator
    {
        get { return operatorRig != null; }
    }

    /// <summary>
    /// 呼吸走到哪了 / Where the idle loop is, 0 to 1, or -1 while a reaction is playing instead. For
    /// checking the beat lock; nothing in the game reads it.
    /// </summary>
    public float IdlePhase
    {
        get
        {
            if (operatorRig == null || idleEntry == null) return -1f;
            if (operatorRig.AnimationState.GetCurrent(0) != idleEntry) return -1f;

            float length = idleEntry.AnimationEnd - idleEntry.AnimationStart;
            return length > 0f ? (idleEntry.AnimationTime - idleEntry.AnimationStart) / length : 0f;
        }
    }

    // ------------------------------------------------------------------ stage

    /// <summary>
    /// 请干员上台 / Puts the operator on the stage, or leaves it empty when they have no rig. Safe to
    /// call again: the previous one is taken down first.
    /// </summary>
    public void Show(CharMeta meta)
    {
        Clear();
        FillDefaults(false);

        if (meta == null && !string.IsNullOrEmpty(previewOperatorId)) meta = FindMeta(previewOperatorId);

        SkeletonDataAsset rig = meta != null ? meta.GetBattleRig() : null;
        if (rig == null) return;

        Spine.SkeletonData data = rig.GetSkeletonData(false);
        if (data == null)
        {
            Debug.LogWarning("[CharacterPresenter] The battle rig for " + meta.name + " did not load.", this);
            return;
        }

        operatorRig = SkeletonAnimation.NewSkeletonAnimationGameObject(rig);
        operatorRig.gameObject.name = "Operator (" + meta.name + ")";

        Transform t = operatorRig.transform;
        t.SetParent(transform, false);
        t.localPosition = new Vector3(offset.x, offset.y, 0f);

        // 按高度缩放 / Scaled by height, so a rig drawn at another size still stands as tall as this
        // one. A rig with no bounds (height 0) keeps its own scale.
        float height = data.Height * rig.scale;
        float scale = height > 0.01f ? targetHeight / height : 1f;
        t.localScale = new Vector3(scale, scale, 1f);

        MeshRenderer meshRenderer = operatorRig.GetComponent<MeshRenderer>();
        if (meshRenderer != null) meshRenderer.sortingOrder = sortingOrder;

        operatorRig.AnimationState.Data.DefaultMix = defaultBlend;

        resolved.Clear();
        Begin();
    }

    // 编辑器里加组件时调用 / Called by Unity when the component is added or Reset from its menu.
    private void Reset()
    {
        ResetToDefaults();
    }

    /// <summary>
    /// 全部回到默认 / Every placement value and every reaction back to what a new component gets.
    /// Tools/Rhythm/Reset Character Stage To Default calls this.
    /// </summary>
    public void ResetToDefaults()
    {
        targetHeight = 3.8f;
        offset = Vector2.zero;
        sortingOrder = -5;
        facesRight = true;
        leftLane = 0;
        previewOperatorId = "AMIYA";
        beatsPerIdleLoop = 1f;
        lockIdleToBeat = true;
        feverTint = new Color(1f, 0.7f, 0.86f, 1f);
        tintSpeed = 6f;
        defaultBlend = 0.06f;

        FillDefaults(true);
    }

    // 默认的动画表 / The default reaction table: Arknights rig names, tuned to land on the press.
    // tap skips most of a 0.63s wind-up and plays the rest at 2.2x, so the arm is out about 80ms after
    // the press instead of 630ms.
    private void FillDefaults(bool overwrite)
    {
        idle = Default(overwrite, idle, 0f, 0f, 1f, "Idle", "Relax", "Default");
        enter = Default(overwrite, enter, 0f, 0f, 1f, "Start");
        tap = Default(overwrite, tap, 0.45f, 0.95f, 2.2f, "Attack", "Skill_2");
        twin = Default(overwrite, twin, 0f, 0.4f, 1.3f, "Skill_2", "Skill");
        holdLoop = Default(overwrite, holdLoop, 0.15f, 0.4f, 1f, "Skill_2");
        holdEnd = Default(overwrite, holdEnd, 0f, 0f, 1.6f, "Attack_End");
        miss = Default(overwrite, miss, 0f, 0.5f, 2f, "Stun");
        fever = Default(overwrite, fever, 0f, 0f, 1.4f, "Skill");
        fail = Default(overwrite, fail, 0f, 0f, 1f, "Die");
        win = Default(overwrite, win, 0f, 0f, 1f, "Start");
    }

    private static Clip Default(bool overwrite, Clip current, float from, float to, float speed, params string[] names)
    {
        bool unset = current == null || current.names == null || current.names.Length == 0;
        return overwrite || unset ? new Clip(from, to, speed, names) : current;
    }

    /// <summary>Takes the operator down.</summary>
    public void Clear()
    {
        if (operatorRig != null) Destroy(operatorRig.gameObject);

        operatorRig = null;
        idleEntry = null;
        holdsDown = 0;
        feverOn = false;
        failed = false;
        tint = Color.white;
    }

    // The entrance first, when the rig has one, with idle queued behind it; idle straight away when not.
    private void Begin()
    {
        failed = false;
        feverOn = false;
        holdsDown = 0;
        lastTwinFrame = -1;
        tint = Color.white;

        operatorRig.AnimationState.ClearTracks();
        operatorRig.Skeleton.SetToSetupPose();
        Face(false);

        Spine.TrackEntry entrance = Play(enter, false, false);
        if (entrance == null) idleEntry = PlayIdle(false);
        else idleEntry = PlayIdle(true);
    }

    // ----------------------------------------------------------- GameManager

    /// <summary>
    /// 命中 / A note was hit. A Hold starts its held pose, a Twin plays both arms out once (each half is
    /// judged on its own, so the second call in a frame is ignored), and a Tap strikes towards its lane.
    /// </summary>
    public void OnHit(int lane, Judgement judgement, NoteType type, bool isHold)
    {
        if (operatorRig == null || failed) return;

        if (isHold)
        {
            holdsDown++;
            Face(IsLeft(lane));
            Play(holdLoop, true, false);
            return;
        }

        if (type == NoteType.Twin)
        {
            if (Time.frameCount == lastTwinFrame) return;
            lastTwinFrame = Time.frameCount;
            Play(twin, false, true);
            return;
        }

        Face(IsLeft(lane));
        Play(tap, false, true);
    }

    /// <summary>A hold was carried to its end.</summary>
    public void OnHoldEnd(int lane)
    {
        if (operatorRig == null || failed) return;

        holdsDown = Mathf.Max(0, holdsDown - 1);
        if (holdsDown > 0) return;

        if (Play(holdEnd, false, true) == null) PlayIdle(false);
    }

    /// <summary>A hold was let go early.</summary>
    public void OnHoldDropped(int lane)
    {
        if (operatorRig == null || failed) return;

        holdsDown = Mathf.Max(0, holdsDown - 1);
        if (holdsDown > 0) return;

        if (Play(miss, false, true) == null) PlayIdle(false);
    }

    /// <summary>A note ran out of window unplayed.</summary>
    public void OnMiss(int lane)
    {
        if (operatorRig == null || failed) return;

        if (holdsDown == 0) Play(miss, false, true);
    }

    /// <summary>Hurt by something other than a miss.</summary>
    public void OnDamage()
    {
        if (operatorRig == null || failed) return;

        Play(miss, false, true);
    }

    public void OnFeverStart()
    {
        if (operatorRig == null || failed) return;

        feverOn = true;
        Play(fever, false, true);
    }

    public void OnFeverEnd()
    {
        feverOn = false;
    }

    /// <summary>Out of HP. The last frame of the fall is kept.</summary>
    public void OnFail()
    {
        if (operatorRig == null || failed) return;

        failed = true;
        holdsDown = 0;
        feverOn = false;
        Play(fail, false, false);
    }

    /// <summary>The song ended. A failed run has already fallen over and stays that way.</summary>
    public void OnFinish(bool won)
    {
        if (operatorRig == null || failed || !won) return;

        Play(win, false, false);
    }

    /// <summary>The board was cleared for another attempt.</summary>
    public void OnReset()
    {
        if (operatorRig == null) return;

        Begin();
    }

    // ---------------------------------------------------------------- motion

    private bool IsLeft(int lane)
    {
        return lane == leftLane;
    }

    // 朝向 / Which way the rig faces. Set when a strike picks a side and kept afterwards, so the operator
    // keeps looking at the lane that last had her attention.
    private void Face(bool towardsLeft)
    {
        float sign = (facesRight ? 1f : -1f) * (towardsLeft ? -1f : 1f);
        operatorRig.Skeleton.ScaleX = sign;
    }

    private Spine.Animation Resolve(Clip clip)
    {
        if (clip == null || clip.names == null) return null;
        if (resolved.TryGetValue(clip, out Spine.Animation cached)) return cached;

        Spine.Animation found = null;
        foreach (string animationName in clip.names)
        {
            if (string.IsNullOrEmpty(animationName)) continue;

            found = operatorRig.Skeleton.Data.FindAnimation(animationName);
            if (found != null) break;
        }

        // 找不到也记住 / A miss is remembered too, so a rig that lacks a clip is not searched every hit.
        resolved[clip] = found;
        return found;
    }

    // Plays one clip on the only track. A one-shot optionally queues idle behind itself. Returns null
    // when the rig has no such clip - callers fall back or do nothing.
    private Spine.TrackEntry Play(Clip clip, bool loop, bool thenIdle)
    {
        Spine.Animation animation = Resolve(clip);
        if (animation == null) return null;

        Spine.AnimationState state = operatorRig.AnimationState;
        Spine.TrackEntry entry = state.SetAnimation(0, animation, loop);

        float start = Mathf.Clamp(clip.from, 0f, animation.Duration);
        entry.AnimationStart = start;
        entry.AnimationEnd = clip.to > 0f ? Mathf.Clamp(clip.to, start, animation.Duration) : animation.Duration;
        entry.TimeScale = clip.speed;
        entry.MixDuration = clip.blend;

        if (thenIdle && !loop) idleEntry = PlayIdle(true);

        return entry;
    }

    // Idle, either now or queued behind whatever is playing. Remembered, because the beat lock has to
    // find it again.
    private Spine.TrackEntry PlayIdle(bool queued)
    {
        Spine.Animation animation = Resolve(idle);
        if (animation == null) return null;

        Spine.AnimationState state = operatorRig.AnimationState;
        Spine.TrackEntry entry = queued
            ? state.AddAnimation(0, animation, true, 0f)
            : state.SetAnimation(0, animation, true);

        float start = Mathf.Clamp(idle.from, 0f, animation.Duration);
        entry.AnimationStart = start;
        entry.AnimationEnd = idle.to > 0f ? Mathf.Clamp(idle.to, start, animation.Duration) : animation.Duration;
        entry.TimeScale = idle.speed;
        return entry;
    }

    // ------------------------------------------------------------------ frame

    private void Update()
    {
        if (operatorRig == null) return;

        SyncIdle();
        ApplyTint();
    }

    // 呼吸跟着拍子走 / The idle breathes with the music. Driven from the Conductor's song time and the
    // chart's tempo, so it stays in step through pauses, seeks and retries - the same reasoning as
    // BeatPulse, which does this for the HUD.
    private void SyncIdle()
    {
        if (idleEntry == null) return;
        if (operatorRig.AnimationState.GetCurrent(0) != idleEntry) return;

        GameManager game = GameManager.instance;
        SongChart chart = game != null && game.noteSpawner != null ? game.noteSpawner.chart : null;
        Conductor conductor = Conductor.instance;

        float loopSeconds = idleEntry.AnimationEnd - idleEntry.AnimationStart;
        if (loopSeconds <= 0f) return;

        if (chart == null || chart.bpm <= 0f || conductor == null || !conductor.IsPlaying)
        {
            idleEntry.TimeScale = idle.speed;
            return;
        }

        float loopsPerSecond = chart.bpm / 60f / Mathf.Max(0.01f, beatsPerIdleLoop);
        idleEntry.TimeScale = loopsPerSecond * loopSeconds;

        // 有拍子网格才锁相位 / Only a chart with a beat grid says where the downbeat is; without one the
        // tempo above is all there is to follow.
        if (!lockIdleToBeat || chart.beatPhase == 0f || conductor.IsPaused) return;

        float loops = (conductor.SongTime - chart.beatPhase) * loopsPerSecond;
        float phase = loops - Mathf.Floor(loops);

        // 这一帧还会前进一步 / The state advances by one more step this frame, so land one step short.
        float step = Time.deltaTime * idleEntry.TimeScale;
        idleEntry.TrackTime = Mathf.Repeat(phase * loopSeconds - step, loopSeconds);
    }

    private void ApplyTint()
    {
        Color target = feverOn ? feverTint : Color.white;
        float k = 1f - Mathf.Exp(-tintSpeed * Time.deltaTime);
        tint = Color.Lerp(tint, target, k);

        Spine.Skeleton skeleton = operatorRig.Skeleton;
        skeleton.R = tint.r;
        skeleton.G = tint.g;
        skeleton.B = tint.b;
    }

    private static CharMeta FindMeta(string id)
    {
        try
        {
            return CharManager.Inst().GetMeta(id);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[CharacterPresenter] No metadata for '" + id + "': " + e.Message);
            return null;
        }
    }
}
