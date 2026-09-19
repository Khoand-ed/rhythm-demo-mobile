using UnityEngine;

// A quick scale "punch" - grow on the beat, spring back - plus an optional
// colour flash. Used on the lane buttons (every press) and on the combo
// counter (every hit, with a flash on milestones).
//
// Runs on unscaled time so it still reads during the frame a pause lands on,
// and never drifts: it always springs back to the scale it woke up with.
public class PunchScale : MonoBehaviour
{
    [Tooltip("Extra scale at the peak of a punch, e.g. 0.15 = 15% bigger.")]
    public float amount = 0.15f;

    [Tooltip("Seconds to spring back to rest.")]
    public float duration = 0.12f;

    [Tooltip("Seconds a Flash colour takes to fade back.")]
    public float flashDuration = 0.35f;

    private Vector3 restScale;
    private float punchAge = float.MaxValue;
    private float punchStrength = 1f;

    private TMPro.TMP_Text text;
    private SpriteRenderer sprite;
    private Color restColor;
    private Color flashColor;
    private float flashAge = float.MaxValue;

    void Awake()
    {
        restScale = transform.localScale;

        text = GetComponent<TMPro.TMP_Text>();
        sprite = GetComponent<SpriteRenderer>();

        if (text != null) restColor = text.color;
        else if (sprite != null) restColor = sprite.color;
    }

    public void Punch(float strength = 1f)
    {
        punchAge = 0f;
        punchStrength = strength;
    }

    public void Flash(Color color)
    {
        flashAge = 0f;
        flashColor = color;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        if (punchAge < duration)
        {
            punchAge += dt;
            float t = Mathf.Clamp01(punchAge / Mathf.Max(0.0001f, duration));

            // Fast attack, eased release: full size at once, then settle.
            float inv = 1f - t;
            transform.localScale = restScale * (1f + amount * punchStrength * inv * inv);
        }
        else if (transform.localScale != restScale)
        {
            transform.localScale = restScale;
        }

        if (flashAge < flashDuration)
        {
            flashAge += dt;
            float t = Mathf.Clamp01(flashAge / Mathf.Max(0.0001f, flashDuration));
            SetColor(Color.Lerp(flashColor, restColor, t * t));
        }
    }

    private void SetColor(Color color)
    {
        if (text != null) text.color = color;
        else if (sprite != null) sprite.color = color;
    }
}
