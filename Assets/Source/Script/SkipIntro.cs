using UnityEngine;
using UnityEngine.UI;

// osu!-style intro skip. When a chart leaves a long opening empty, a Skip
// button fades in and jumps the song to just before the first note. It only
// appears when there is something worth skipping, and disappears on its own
// once the notes are close.
//
// Built by Tools/Rhythm/Set Up Skip Button, which also wires the button's
// OnClick to Skip() in the Inspector.
public class SkipIntro : MonoBehaviour
{
    [Tooltip("Fades the whole button in and out.")]
    public CanvasGroup group;

    [Tooltip("Optional bar that fills as the first note approaches.")]
    public Image progress;

    [Tooltip("Only offer a skip that saves at least this many seconds.")]
    public float minimumSkip = 6f;

    [Tooltip("Seconds before the first note leaves its marker that a skip lands - time to get ready.")]
    public float landBefore = 2f;

    [Tooltip("The button hides once the notes are this close anyway.")]
    public float hideWhenWithin = 1f;

    public KeyCode skipKey = KeyCode.Space;
    public KeyCode altSkipKey = KeyCode.Return;

    public float fadeTime = 0.2f;

    // The press that starts the song must not also skip it, so input only
    // counts once the button has been up this long.
    private const float InputGrace = 0.3f;

    private SongChart targetFor;
    private float target = float.NegativeInfinity;
    private float visibleFor;

    void Awake()
    {
        if (group != null) SetShown(0f, false);
    }

    void Update()
    {
        float songTime;
        bool available = Available(out songTime);

        if (group != null)
        {
            float alpha = Mathf.MoveTowards(group.alpha, available ? 1f : 0f,
                                            Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeTime));
            SetShown(alpha, available);
        }

        if (!available)
        {
            visibleFor = 0f;
            return;
        }

        visibleFor += Time.unscaledDeltaTime;

        if (progress != null) progress.fillAmount = Mathf.Clamp01(songTime / Mathf.Max(0.01f, target));

        if (visibleFor >= InputGrace && (Input.GetKeyDown(skipKey) || Input.GetKeyDown(altSkipKey)))
        {
            Skip();
        }
    }

    // Wired to the button's OnClick.
    public void Skip()
    {
        float songTime;
        if (!Available(out songTime) || visibleFor < InputGrace) return;

        NoteSpawner spawner = GameManager.instance.noteSpawner;

        // Spawner first: nothing is on the board yet, so this only moves its
        // cursors, and the notes then spawn from their markers as usual.
        spawner.SeekTo(target);
        Conductor.instance.SkipTo(target);

        visibleFor = 0f;
    }

    private bool Available(out float songTime)
    {
        songTime = 0f;

        Conductor conductor = Conductor.instance;
        GameManager game = GameManager.instance;
        if (conductor == null || game == null || game.noteSpawner == null) return false;
        if (!conductor.IsPlaying || conductor.IsPaused) return false;

        SongChart chart = game.noteSpawner.chart;
        if (chart == null) return false;

        if (chart != targetFor)
        {
            targetFor = chart;
            target = game.noteSpawner.FirstSpawnTime() - landBefore;
        }

        songTime = conductor.SongTime;

        // A song that starts straight away has nothing to skip.
        if (float.IsInfinity(target) || target < minimumSkip) return false;

        return songTime < target - hideWhenWithin;
    }

    private void SetShown(float alpha, bool interactive)
    {
        group.alpha = alpha;
        group.interactable = interactive;
        group.blocksRaycasts = interactive;
    }
}
