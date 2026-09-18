using UnityEngine;

// A short-lived pooled effect: a judgement word, a FAST/SLOW tag or a burst
// ring. It pops in slightly oversized, settles, drifts up and fades - the
// small overshoot is most of what makes a hit feel like it landed.
//
// HitFeedback owns the pool; this only animates and reports when it is done.
public class FeedbackPop : MonoBehaviour
{
    [Tooltip("Seconds from appearing to fully faded.")]
    public float lifetime = 0.45f;

    [Tooltip("Scale on the first frame, relative to the prefab's own scale.")]
    public float startScale = 1.4f;

    [Tooltip("Scale it ends on. Popups settle to 1; burst rings expand past it.")]
    public float endScale = 1f;

    [Tooltip("Seconds to get from startScale to endScale. Short for a snappy pop, " +
             "the full lifetime for a ring that keeps expanding.")]
    public float scaleTime = 0.09f;

    [Tooltip("World units it drifts upward over its lifetime.")]
    public float rise = 0.3f;

    [Tooltip("Share of the lifetime before it starts fading.")]
    [Range(0f, 1f)]
    public float fadeStart = 0.55f;

    private SpriteRenderer[] sprites;
    private TMPro.TMP_Text[] texts;
    private Color[] spriteColors;
    private Color[] textColors;
    private ParticleSystem[] particles;

    private Vector3 baseScale;
    private float scaleThisPlay = 1f;
    private Color tintThisPlay = Color.white;
    private Vector3 origin;
    private float age;
    private System.Action<FeedbackPop> onDone;

    // The pool key: the prefab this instance was made from.
    public FeedbackPop Source { get; set; }

    void Awake()
    {
        baseScale = transform.localScale;

        sprites = GetComponentsInChildren<SpriteRenderer>(true);
        spriteColors = new Color[sprites.Length];
        for (int i = 0; i < sprites.Length; i++) spriteColors[i] = sprites[i].color;

        texts = GetComponentsInChildren<TMPro.TMP_Text>(true);
        textColors = new Color[texts.Length];
        for (int i = 0; i < texts.Length; i++) textColors[i] = texts[i].color;

        particles = GetComponentsInChildren<ParticleSystem>(true);
    }

    // `tint` multiplies the prefab's own colours; white leaves them as authored.
    public void Play(Vector3 position, Color tint, float scale, System.Action<FeedbackPop> done)
    {
        origin = position;
        age = 0f;
        onDone = done;

        transform.position = position;
        transform.localScale = baseScale * scale * startScale;
        scaleThisPlay = scale;
        tintThisPlay = tint;

        gameObject.SetActive(true);
        ApplyAlpha(1f);

        for (int i = 0; i < particles.Length; i++)
        {
            particles[i].Clear(true);
            particles[i].Play(true);
        }
    }

    // Ends early, e.g. when a newer judgement replaces this one in its lane.
    public void Stop()
    {
        if (!gameObject.activeSelf) return;
        Finish();
    }

    void Update()
    {
        age += Time.deltaTime;
        float t = lifetime > 0f ? age / lifetime : 1f;

        if (t >= 1f)
        {
            Finish();
            return;
        }

        float s = scaleTime > 0f ? EaseOutCubic(Mathf.Clamp01(age / scaleTime)) : 1f;
        transform.localScale = baseScale * scaleThisPlay * Mathf.LerpUnclamped(startScale, endScale, s);
        transform.position = origin + Vector3.up * (rise * EaseOutCubic(t));

        float fade = t <= fadeStart ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (t - fadeStart) / Mathf.Max(0.0001f, 1f - fadeStart));
        ApplyAlpha(fade);
    }

    private void ApplyAlpha(float alpha)
    {
        for (int i = 0; i < sprites.Length; i++)
        {
            Color c = spriteColors[i] * tintThisPlay;
            c.a = spriteColors[i].a * tintThisPlay.a * alpha;
            sprites[i].color = c;
        }

        for (int i = 0; i < texts.Length; i++)
        {
            Color c = textColors[i] * tintThisPlay;
            c.a = textColors[i].a * tintThisPlay.a * alpha;
            texts[i].color = c;
        }
    }

    private void Finish()
    {
        gameObject.SetActive(false);

        System.Action<FeedbackPop> done = onDone;
        onDone = null;
        if (done != null) done(this);
    }

    private static float EaseOutCubic(float x)
    {
        float inv = 1f - x;
        return 1f - inv * inv * inv;
    }
}
