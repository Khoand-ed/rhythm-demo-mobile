using UnityEngine;

// Pulses its targets on every beat of the playing chart, harder on each bar
// line, so the player can feel the tempo before the first note arrives - and
// between notes in a sparse section. Driven by song time from the Conductor,
// so it stays locked to the music through pauses and restarts.
//
// Needs the chart's beatPhase (written by the generator and the timeline
// editor). Charts imported without one have no known grid, and are skipped
// rather than pulsed off the beat.
public class BeatPulse : MonoBehaviour
{
    public PunchScale[] targets;

    [Tooltip("Punch strength on an ordinary beat, relative to a press.")]
    public float beatStrength = 0.35f;

    [Tooltip("Punch strength on beat 1 of each bar.")]
    public float barStrength = 0.8f;

    private int lastBeat = int.MinValue;

    void Update()
    {
        Conductor conductor = Conductor.instance;
        if (conductor == null || !conductor.IsPlaying || conductor.IsPaused) return;

        GameManager game = GameManager.instance;
        SongChart chart = game != null && game.noteSpawner != null ? game.noteSpawner.chart : null;
        if (chart == null || chart.bpm <= 0f || chart.beatPhase == 0f) return;

        float songTime = conductor.SongTime;
        float period = 60f / chart.bpm;
        int beat = Mathf.FloorToInt((songTime - chart.beatPhase) / period);

        if (beat == lastBeat) return;

        // Skip the pulse on the first frame (or after a seek), which would
        // fire for a beat that passed while nothing was listening.
        bool fresh = lastBeat != int.MinValue && beat == lastBeat + 1;
        lastBeat = beat;

        if (!fresh || songTime < 0f) return;

        bool downbeat = ((beat % 4) + 4) % 4 == 0;
        float strength = downbeat ? barStrength : beatStrength;

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] != null) targets[i].Punch(strength);
        }
    }
}
