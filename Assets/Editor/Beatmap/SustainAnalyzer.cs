using UnityEngine;

// Decides how long the sound behind an onset keeps ringing, which is what
// turns a Tap into a Hold.
//
// Hold-worthy sounds are the ones that refuse to decay: sustained vocals and
// belts, pads, strings and synth leads, and open hi-hats or ringing cymbals.
// Staccato hits and plucks fall away within a few frames and never qualify.
//
// Summed band energy cannot tell these apart in a busy mix - drums hitting
// every half beat keep any wide band lively whether or not anything is held.
// So this follows a few narrow bands (the held note's own harmonics, or a
// cymbal's ring) and asks how long *they* stay up:
//  - the bands are chosen just after the attack, once a drum transient
//    landing with the note has already died, so they belong to what rings;
//  - only energy the onset added counts - each band is measured above its
//    level just before the onset, so something that was already ringing
//    underneath cannot pass itself off as this onset's sustain;
//  - a drum landing on top later only adds energy, so it cannot end a real
//    sustain, while a hit that dies away drops out of its own bands at once.
public class SustainAnalyzer
{
    // How many of the attack's loudest bands are followed. The median of
    // their levels is what has to stay up, so one noisy band cannot carry it.
    private const int TrackedBands = 6;

    // Frames after the onset searched for the attack's peak.
    private const int PeakSearchFrames = 3;

    // Frames after the peak at which the bands to follow are chosen - about
    // 45 ms at the default hop, long enough for a click or drum transient to
    // have gone.
    private const int SettleFrames = 4;

    // Frames before the onset averaged into each band's baseline.
    private const int BaselineFrames = 3;

    // New energy must be at least this share of the band's baseline to count,
    // so level jitter in a busy band is not mistaken for a new note.
    private const float MinRiseOverBaseline = 0.5f;

    // Consecutive frames below the threshold that end a sustain, so one
    // noisy frame does not.
    private const int FramesBelowToEnd = 2;

    private readonly SpectralFrames frames;
    private readonly float levelFloor;

    private readonly int[] tracked = new int[TrackedBands];
    private readonly float[] reference = new float[TrackedBands];
    private readonly float[] baseline = new float[TrackedBands];
    private readonly float[] ratios = new float[TrackedBands];

    public SustainAnalyzer(SpectralFrames frames)
    {
        this.frames = frames;

        // "Energy plateau": a sustain only counts if it sits at or above the
        // track's typical level, so a quiet reverb tail in a breakdown does
        // not become a hold.
        levelFloor = Median(frames.level);
    }

    // Seconds the sound behind this onset keeps at least `ratio` of its
    // attack level. 0 when it is too quiet to count as a plateau.
    public float SustainSeconds(Onset onset, float ratio, float maxSeconds)
    {
        if (frames.Count == 0 || frames.frameRate <= 0f || frames.bandCount == 0) return 0f;

        int start = Mathf.Clamp(Mathf.RoundToInt(onset.sourceTime * frames.frameRate), 0, frames.Count - 1);

        int peak = start;
        for (int i = start + 1; i <= Mathf.Min(frames.Count - 1, start + PeakSearchFrames); i++)
        {
            if (frames.level[i] > frames.level[peak]) peak = i;
        }

        if (frames.level[peak] < levelFloor) return 0f;

        int settled = peak + SettleFrames;
        if (settled >= frames.Count) return 0f;
        if (!PickRisenBands(start, settled)) return 0f;

        int last = Mathf.Min(frames.Count - 1, peak + Mathf.Max(1, Mathf.RoundToInt(maxSeconds * frames.frameRate)));
        int end = settled;
        int below = 0;
        int counted = 0;
        float levelSum = 0f;

        for (int i = settled + 1; i <= last; i++)
        {
            if (TrackedRatio(i) < ratio)
            {
                if (++below >= FramesBelowToEnd) break;
                continue;
            }

            below = 0;
            end = i;
            levelSum += frames.level[i];
            counted++;
        }

        // Died at the settle point: a hit, not a held sound.
        if (counted == 0) return 0f;

        // Held, but held quietly: not a plateau.
        if (levelSum / counted < levelFloor) return 0f;

        return (end - start) / frames.frameRate;
    }

    // The bands with the most new energy still present at `settled`,
    // measured over the baseline just before `onset`.
    private bool PickRisenBands(int onset, int settled)
    {
        for (int k = 0; k < TrackedBands; k++)
        {
            tracked[k] = -1;
            reference[k] = 0f;
            baseline[k] = 0f;
        }

        int from = Mathf.Max(0, onset - BaselineFrames);
        int count = onset - from;

        for (int band = 0; band < frames.bandCount; band++)
        {
            float before = 0f;
            for (int i = from; i < onset; i++) before += frames.Band(i, band);
            if (count > 0) before /= count;

            float rise = frames.Band(settled, band) - before;
            if (rise <= before * MinRiseOverBaseline || rise <= 0f) continue;

            // Insertion into a small sorted list, largest rise first.
            for (int k = 0; k < TrackedBands; k++)
            {
                if (rise <= reference[k]) continue;

                for (int m = TrackedBands - 1; m > k; m--)
                {
                    tracked[m] = tracked[m - 1];
                    reference[m] = reference[m - 1];
                    baseline[m] = baseline[m - 1];
                }

                tracked[k] = band;
                reference[k] = rise;
                baseline[k] = before;
                break;
            }
        }

        return tracked[TrackedBands - 1] >= 0;
    }

    // Median of the tracked bands' new energy relative to what they had just
    // after the attack.
    private float TrackedRatio(int frame)
    {
        for (int k = 0; k < TrackedBands; k++)
        {
            ratios[k] = (frames.Band(frame, tracked[k]) - baseline[k]) / reference[k];
        }

        System.Array.Sort(ratios);
        return ratios[TrackedBands / 2];
    }

    private static float Median(float[] values)
    {
        if (values.Length == 0) return 0f;

        float[] sorted = (float[])values.Clone();
        System.Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }
}
