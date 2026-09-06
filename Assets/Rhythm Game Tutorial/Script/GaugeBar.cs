using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Drives a Filled Image from a value. Direction and shape come from the Image
// itself - set its Image Type to Filled, then pick Fill Method and Fill Origin -
// so a bar can be made horizontal, vertical or radial without touching code.
// Both the HP bar and the fever bar use this, so anything tuned on one is
// available on the other.
public class GaugeBar : MonoBehaviour
{
    [Tooltip("The Image whose fillAmount this drives. Its Image Type must be Filled.")]
    public Image fill;

    [Tooltip("Optional readout, e.g. \"72 / 100\". Leave empty for no text.")]
    public TextMeshProUGUI label;

    [Tooltip("Label format. {0} is the current value, {1} the maximum.")]
    public string labelFormat = "{0:0} / {1:0}";

    [Tooltip("How fast the bar chases its target. 0 snaps with no easing.")]
    public float smoothing = 8f;

    [Tooltip("Colour across the range, sampled at the current fraction. Flatten it " +
             "for a single colour, or ramp it red at the low end for HP.")]
    public Gradient colorOverValue = new Gradient();

    [Tooltip("Pulse the bar while it is completely full - useful for 'fever ready'.")]
    public bool pulseWhenFull;
    public float pulseSpeed = 4f;
    public float pulseAmount = 0.15f;

    private float displayed;
    private float target;
    private float max = 1f;
    private bool initialised;

    // Current fraction actually being shown, after easing. Handy for other
    // visuals that want to react to the bar rather than to the raw value.
    public float DisplayedFraction
    {
        get { return max > 0f ? displayed / max : 0f; }
    }

    public void SetValue(float current, float maximum)
    {
        max = Mathf.Max(0.0001f, maximum);
        target = Mathf.Clamp(current, 0f, max);

        // First value of a run should not animate up from zero.
        if (!initialised)
        {
            displayed = target;
            initialised = true;
        }
    }

    void Update()
    {
        if (fill == null) return;

        // Unscaled, so the bar still settles while the game is paused with
        // Time.timeScale at 0.
        displayed = smoothing > 0f
            ? Mathf.Lerp(displayed, target, 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime))
            : target;

        float fraction = DisplayedFraction;
        fill.fillAmount = fraction;

        Color color = colorOverValue.Evaluate(fraction);

        if (pulseWhenFull && target >= max)
        {
            // Ride the alpha rather than the scale, so pulsing never disturbs
            // whatever layout the bar has been dragged into.
            float pulse = (Mathf.Sin(Time.unscaledTime * pulseSpeed) + 1f) * 0.5f;
            color.a *= 1f - pulseAmount * pulse;
        }

        fill.color = color;

        if (label != null)
        {
            label.text = string.Format(labelFormat, target, max);
        }
    }
}
