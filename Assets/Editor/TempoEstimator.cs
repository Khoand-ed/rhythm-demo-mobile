using UnityEngine;

public struct TempoEstimate
{
    public float bpm;

    // Seconds from track start at which beat one falls. Note that this is NOT
    // the beatmap `offset` field - it is baked into absolute note times.
    public float phase;

    // Winning autocorrelation peak over the mean. Above ~3 is a confident
    // reading; near 1 means the track has no steady pulse to find.
    public float confidence;

    // How far clear the winner was of its best non-octave rival. A value near
    // 1 means a second tempo fit almost as well - worth overriding by hand.
    public float rivalRatio;
}

// Estimates tempo by autocorrelating the onset-strength signal, then recovers
// the beat phase by sliding a pulse train across it.
public class TempoEstimator
{
    private const float MinBpm = 60f;
    private const float MaxBpm = 200f;

    // Tempo detection's classic failure is the octave error - reporting 120 as
    // 60 or 240. Autocorrelation genuinely peaks at every multiple of the true
    // period, so the raw maximum cannot resolve it. Weighting by a log-Gaussian
    // centred on a typical tempo is the standard tie-breaker.
    private const float PreferredBpm = 120f;
    private const float PreferredSpreadOctaves = 0.9f;

    public TempoEstimate Estimate(float[] flux, float frameRate)
    {
        TempoEstimate result = new TempoEstimate { bpm = PreferredBpm, phase = 0f };
        if (flux == null || flux.Length < 16) return result;

        float[] signal = Normalise(flux);

        int minLag = Mathf.Max(1, Mathf.RoundToInt(frameRate * 60f / MaxBpm));
        int maxLag = Mathf.Min(signal.Length - 1, Mathf.RoundToInt(frameRate * 60f / MinBpm));
        if (maxLag <= minLag) return result;

        float[] score = new float[maxLag + 1];
        float best = 0f;
        int bestLag = minLag;
        float sum = 0f;
        int counted = 0;

        for (int lag = minLag; lag <= maxLag; lag++)
        {
            float correlation = 0f;
            for (int i = lag; i < signal.Length; i++) correlation += signal[i] * signal[i - lag];
            correlation /= signal.Length - lag;

            float bpm = 60f * frameRate / lag;
            score[lag] = correlation * TempoPrior(bpm);

            sum += score[lag];
            counted++;

            if (score[lag] > best)
            {
                best = score[lag];
                bestLag = lag;
            }
        }

        float mean = counted > 0 ? sum / counted : 0f;

        // Compare the winner against its own half and double explicitly: the
        // prior decides which octave is most plausible.
        bestLag = ResolveOctave(score, bestLag, minLag, maxLag, frameRate);

        // Autocorrelation settles the octave but is stuck on integer lags, and
        // near 126 BPM one lag step is almost 3 BPM. Left there, a sub-1% tempo
        // error accumulates tens of milliseconds by the end of a track and
        // pushes late notes out of the Perfect window.
        float phaseFrames;
        float periodFrames = RefineByComb(signal, bestLag, out phaseFrames);

        result.bpm = 60f * frameRate / periodFrames;
        result.confidence = mean > 0f ? score[bestLag] / mean : 0f;
        result.rivalRatio = RivalRatio(score, bestLag, minLag, maxLag);
        result.phase = phaseFrames / frameRate;

        return result;
    }

    // Treats the onset signal as a train of impulses and finds the period whose
    // complex sum has the greatest magnitude. Period is continuous here, so
    // there is no resolution floor, and the argument of that sum hands back the
    // beat phase for free.
    //
    // Driven by the raw flux rather than by picked onsets on purpose: tempo
    // should not shift when the onset sensitivity is adjusted.
    private static float RefineByComb(float[] signal, int coarsePeriodFrames, out float phaseFrames)
    {
        const int Steps = 600;
        const float SearchSpan = 0.06f;

        float bestMagnitude = -1f;
        float bestPeriod = coarsePeriodFrames;

        phaseFrames = 0f;

        float from = coarsePeriodFrames * (1f - SearchSpan);
        float to = coarsePeriodFrames * (1f + SearchSpan);

        for (int step = 0; step <= Steps; step++)
        {
            float period = Mathf.Lerp(from, to, step / (float)Steps);
            if (period < 1f) continue;

            float sumRe = 0f;
            float sumIm = 0f;

            for (int i = 0; i < signal.Length; i++)
            {
                float weight = signal[i];
                if (weight <= 0f) continue;

                float angle = 2f * Mathf.PI * i / period;
                sumRe += weight * Mathf.Cos(angle);
                sumIm += weight * Mathf.Sin(angle);
            }

            float magnitude = Mathf.Sqrt(sumRe * sumRe + sumIm * sumIm);
            if (magnitude <= bestMagnitude) continue;

            bestMagnitude = magnitude;
            bestPeriod = period;

            float phase = Mathf.Atan2(sumIm, sumRe) / (2f * Mathf.PI) * period;
            phaseFrames = phase < 0f ? phase + period : phase;
        }

        return bestPeriod;
    }

    private static int ResolveOctave(float[] score, int bestLag, int minLag, int maxLag, float frameRate)
    {
        int chosen = bestLag;
        float chosenScore = score[bestLag];

        int[] candidates = { bestLag / 2, bestLag * 2 };

        for (int i = 0; i < candidates.Length; i++)
        {
            int lag = candidates[i];
            if (lag < minLag || lag > maxLag) continue;

            if (score[lag] > chosenScore)
            {
                chosenScore = score[lag];
                chosen = lag;
            }
        }

        return chosen;
    }

    // Best score outside a neighbourhood of the winner and its octaves, over
    // the winner. Close to 1 means another tempo fit nearly as well.
    private static float RivalRatio(float[] score, int bestLag, int minLag, int maxLag)
    {
        float rival = 0f;

        for (int lag = minLag; lag <= maxLag; lag++)
        {
            if (IsNear(lag, bestLag) || IsNear(lag, bestLag * 2) || IsNear(lag, bestLag / 2)) continue;
            if (score[lag] > rival) rival = score[lag];
        }

        return score[bestLag] > 0f ? rival / score[bestLag] : 1f;
    }

    private static bool IsNear(int lag, int target)
    {
        int tolerance = Mathf.Max(1, Mathf.RoundToInt(target * 0.1f));
        return Mathf.Abs(lag - target) <= tolerance;
    }

    private static float TempoPrior(float bpm)
    {
        float octaves = Mathf.Log(bpm / PreferredBpm, 2f);
        return Mathf.Exp(-0.5f * (octaves * octaves) / (PreferredSpreadOctaves * PreferredSpreadOctaves));
    }

    // Removing the mean stops the autocorrelation being dominated by the
    // signal's DC level rather than its periodicity.
    private static float[] Normalise(float[] source)
    {
        float[] result = new float[source.Length];

        float mean = 0f;
        for (int i = 0; i < source.Length; i++) mean += source[i];
        mean /= source.Length;

        float peak = 0f;
        for (int i = 0; i < source.Length; i++)
        {
            result[i] = Mathf.Max(0f, source[i] - mean);
            if (result[i] > peak) peak = result[i];
        }

        if (peak > 0f)
        {
            for (int i = 0; i < result.Length; i++) result[i] /= peak;
        }

        return result;
    }
}
