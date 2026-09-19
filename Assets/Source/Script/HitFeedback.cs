using System.Collections.Generic;
using UnityEngine;

// Everything the player sees and hears the instant a press lands. Rhythm
// games feel good because every hit answers back at once - a sound on the
// beat, the judgement popping at the button, a ring bursting out of it - and
// because the answer says *how* good (Perfect vs Great) and which way it was
// off (FAST / SLOW), so the player can correct.
//
// Built and wired by Tools/Rhythm/Set Up Hit Feedback; every prefab, clip and
// colour here is an ordinary reference to restyle in the Inspector.
public class HitFeedback : MonoBehaviour
{
    [Header("Hit sounds")]
    public AudioSource hitSource;
    public AudioClip tapClip;
    public AudioClip twinClip;
    public AudioClip holdEndClip;

    [Range(0f, 1f)]
    public float hitVolume = 0.8f;

    [Header("Judgement popups")]
    public FeedbackPop perfectPopup;
    public FeedbackPop greatPopup;
    public FeedbackPop hitPopup;
    public FeedbackPop missPopup;

    [Tooltip("Where the judgement appears, relative to the lane's button.")]
    public Vector3 popupOffset = new Vector3(0f, 1.3f, 0f);

    [Header("Timing tags")]
    public FeedbackPop fastTag;
    public FeedbackPop slowTag;

    [Tooltip("Relative to the lane's button. Shown on anything short of Perfect.")]
    public Vector3 timingTagOffset = new Vector3(0f, 0.75f, 0f);

    [Header("Bursts")]
    public FeedbackPop burst;
    public Color perfectColor = new Color(1f, 0.84f, 0.3f);
    public Color greatColor = new Color(0.4f, 0.85f, 1f);
    public Color hitColor = new Color(1f, 1f, 1f, 0.8f);
    public Color holdColor = new Color(0.55f, 0.9f, 1f, 0.7f);

    [Tooltip("Burst size for a Twin, relative to a single hit.")]
    public float twinBurstScale = 1.35f;

    [Tooltip("Burst size for the sparkle while a hold is held.")]
    public float holdTickBurstScale = 0.45f;

    [Header("Combo")]
    public PunchScale comboPunch;

    [Tooltip("Every this many combo, the counter flashes and punches harder.")]
    public int comboMilestone = 50;

    public Color milestoneColor = new Color(1f, 0.84f, 0.3f);

    private readonly Dictionary<FeedbackPop, Stack<FeedbackPop>> idle = new Dictionary<FeedbackPop, Stack<FeedbackPop>>();

    // One judgement and one timing tag per lane at a time: a newer result
    // replaces the older one instead of stacking a pile of words.
    private readonly Dictionary<int, FeedbackPop> judgementByLane = new Dictionary<int, FeedbackPop>();
    private readonly Dictionary<int, FeedbackPop> tagByLane = new Dictionary<int, FeedbackPop>();

    public void OnHit(int lane, Vector3 buttonPos, Judgement judgement, float signedDelta, NoteType type)
    {
        PlaySound(type == NoteType.Twin && twinClip != null ? twinClip : tapClip);
        ShowJudgement(lane, buttonPos, judgement);
        ShowTimingTag(lane, buttonPos, judgement, signedDelta);
        Burst(buttonPos, ColorFor(judgement), type == NoteType.Twin ? twinBurstScale : 1f);
    }

    // The head of a hold was hit: same answer as a tap, and the body keeps
    // sparkling through OnHoldTick while it is held.
    public void OnHoldStart(int lane, Vector3 buttonPos, Judgement judgement, float signedDelta)
    {
        OnHit(lane, buttonPos, judgement, signedDelta, NoteType.Hold);
    }

    public void OnHoldTick(Vector3 buttonPos)
    {
        Burst(buttonPos, holdColor, holdTickBurstScale);
    }

    public void OnHoldEnd(int lane, Vector3 buttonPos, Judgement judgement)
    {
        PlaySound(holdEndClip != null ? holdEndClip : tapClip);
        ShowJudgement(lane, buttonPos, judgement);
        Burst(buttonPos, ColorFor(judgement), 1.15f);
    }

    public void OnMiss(int lane, Vector3 buttonPos)
    {
        ShowJudgement(lane, buttonPos, Judgement.Miss);
    }

    public void OnCombo(int combo)
    {
        if (comboPunch == null || combo <= 0) return;

        bool milestone = comboMilestone > 0 && combo % comboMilestone == 0;
        comboPunch.Punch(milestone ? 2.2f : 1f);
        if (milestone) comboPunch.Flash(milestoneColor);
    }

    private void PlaySound(AudioClip clip)
    {
        if (hitSource == null || clip == null) return;
        hitSource.PlayOneShot(clip, hitVolume);
    }

    private void ShowJudgement(int lane, Vector3 buttonPos, Judgement judgement)
    {
        FeedbackPop prefab = PopupFor(judgement);
        if (prefab == null) return;

        FeedbackPop previous;
        if (judgementByLane.TryGetValue(lane, out previous) && previous != null) previous.Stop();

        judgementByLane[lane] = Spawn(prefab, buttonPos + popupOffset, Color.white, 1f);

        // A result with no timing to report must not leave an old tag behind.
        FeedbackPop tag;
        if (tagByLane.TryGetValue(lane, out tag) && tag != null) tag.Stop();
    }

    private void ShowTimingTag(int lane, Vector3 buttonPos, Judgement judgement, float signedDelta)
    {
        // A Perfect needs no correction, so it gets no tag.
        if (judgement == Judgement.Perfect || judgement == Judgement.Miss) return;

        FeedbackPop prefab = signedDelta < 0f ? fastTag : slowTag;
        if (prefab == null) return;

        tagByLane[lane] = Spawn(prefab, buttonPos + timingTagOffset, Color.white, 1f);
    }

    private void Burst(Vector3 position, Color color, float scale)
    {
        if (burst != null) Spawn(burst, position, color, scale);
    }

    private FeedbackPop Spawn(FeedbackPop prefab, Vector3 position, Color tint, float scale)
    {
        Stack<FeedbackPop> stack;
        if (!idle.TryGetValue(prefab, out stack))
        {
            stack = new Stack<FeedbackPop>();
            idle[prefab] = stack;
        }

        FeedbackPop pop = stack.Count > 0 ? stack.Pop() : Create(prefab);
        pop.Play(position, tint, scale, Return);
        return pop;
    }

    private FeedbackPop Create(FeedbackPop prefab)
    {
        FeedbackPop pop = Instantiate(prefab, transform);
        pop.Source = prefab;
        pop.gameObject.SetActive(false);
        return pop;
    }

    private void Return(FeedbackPop pop)
    {
        if (pop.Source != null) idle[pop.Source].Push(pop);
    }

    private FeedbackPop PopupFor(Judgement judgement)
    {
        switch (judgement)
        {
            case Judgement.Perfect: return perfectPopup;
            case Judgement.Great: return greatPopup;
            case Judgement.Hit: return hitPopup;
            default: return missPopup;
        }
    }

    private Color ColorFor(Judgement judgement)
    {
        switch (judgement)
        {
            case Judgement.Perfect: return perfectColor;
            case Judgement.Great: return greatColor;
            default: return hitColor;
        }
    }
}
