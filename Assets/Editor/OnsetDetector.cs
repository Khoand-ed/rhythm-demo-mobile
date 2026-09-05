using System.Collections.Generic;
using UnityEngine;

// One detected note attack.
public struct Onset
{
    // Seconds from the start of the track.
    public float time;

    // Spectral flux at this frame. Used to rank onsets when the chart has to
    // be thinned down to a density target.
    public float strength;

    // Share of this frame's energy below the low/high split, 0..1. Kick-like
    // hits sit near 1, cymbals near 0, which is what drives lane assignment.
    public float lowRatio;
}

// Finds note attacks by spectral flux: the summed *rise* in each frequency bin
// between consecutive frames. Only increases count, so a note's decay never
// registers as a new attack.
public class OnsetDetector
{
    // Frequency below which energy counts as "low" for lane assignment.
    private const float LowBandSplitHz = 400f;

    private readonly int windowSize;
    private readonly int hopSize;
    private readonly Fft fft;

    public OnsetDetector(int windowSize = 1024, int hopSize = 512)
    {
        this.windowSize = windowSize;
        this.hopSize = hopSize;
        fft = new Fft(windowSize);
    }

    public float FrameRate(int sampleRate)
    {
        return (float)sampleRate / hopSize;
    }

    // Per-frame flux, plus the low-band ratio of each frame. Both are returned
    // so tempo estimation can reuse the flux without recomputing the spectrum.
    public void Analyse(float[] mono, int sampleRate, out float[] flux, out float[] lowRatio,
                        System.Func<float, bool> onProgress)
    {
        int frameCount = Mathf.Max(0, 1 + (mono.Length - windowSize) / hopSize);
        flux = new float[frameCount];
        lowRatio = new float[frameCount];

        if (frameCount == 0) return;

        int bins = windowSize / 2;
        int lowBinCount = Mathf.Clamp(
            Mathf.RoundToInt(LowBandSplitHz / (sampleRate / (float)windowSize)), 1, bins - 1);

        float[] window = BuildHannWindow(windowSize);
        float[] re = new float[windowSize];
        float[] im = new float[windowSize];
        float[] magnitude = new float[bins];
        float[] previous = new float[bins];

        for (int frame = 0; frame < frameCount; frame++)
        {
            if (onProgress != null && (frame & 63) == 0)
            {
                if (!onProgress(frame / (float)frameCount)) return;
            }

            int start = frame * hopSize;

            for (int i = 0; i < windowSize; i++)
            {
                re[i] = mono[start + i] * window[i];
                im[i] = 0f;
            }

            fft.Transform(re, im);

            float sum = 0f;
            float lowSum = 0f;
            float total = 0f;

            for (int bin = 0; bin < bins; bin++)
            {
                float value = Mathf.Sqrt(re[bin] * re[bin] + im[bin] * im[bin]);
                magnitude[bin] = value;

                // Only rises count - a decaying note is not a new attack.
                float rise = value - previous[bin];
                if (rise > 0f) sum += rise;

                total += value;
                if (bin < lowBinCount) lowSum += value;
            }

            flux[frame] = sum;
            lowRatio[frame] = total > 0f ? lowSum / total : 0.5f;

            float[] swap = previous;
            previous = magnitude;
            magnitude = swap;
        }

        // Frame 0 is measured against an all-zero "previous" spectrum, so every
        // bin counts as a rise and it always looks like an enormous attack.
        // Left in, it fakes an onset at t=0 and drags the beat phase onto it.
        if (frameCount > 0) flux[0] = 0f;
    }

    // Picks peaks against a moving-average threshold, so a loud chorus does not
    // swallow the onsets in a quiet verse.
    public List<Onset> PickPeaks(float[] flux, float[] lowRatio, float frameRate,
                                 float sensitivity, float minIntervalSeconds)
    {
        List<Onset> onsets = new List<Onset>();
        if (flux.Length == 0) return onsets;

        int meanRadius = Mathf.Max(1, Mathf.RoundToInt(frameRate * 0.15f));
        int peakRadius = 3;
        int minFrameGap = Mathf.Max(1, Mathf.RoundToInt(minIntervalSeconds * frameRate));

        float overallMean = 0f;
        for (int i = 0; i < flux.Length; i++) overallMean += flux[i];
        overallMean /= flux.Length;

        // A floor relative to the whole track stops near-silent passages from
        // producing onsets purely because the local average is tiny there.
        float floor = overallMean * 0.1f;

        int lastAccepted = -minFrameGap;

        for (int i = 0; i < flux.Length; i++)
        {
            float value = flux[i];
            if (value <= 0f) continue;

            int from = Mathf.Max(0, i - meanRadius);
            int to = Mathf.Min(flux.Length - 1, i + meanRadius);

            float localSum = 0f;
            for (int j = from; j <= to; j++) localSum += flux[j];
            float threshold = (localSum / (to - from + 1)) * sensitivity + floor;

            if (value <= threshold) continue;

            bool isPeak = true;
            int peakFrom = Mathf.Max(0, i - peakRadius);
            int peakTo = Mathf.Min(flux.Length - 1, i + peakRadius);

            for (int j = peakFrom; j <= peakTo; j++)
            {
                if (flux[j] > value) { isPeak = false; break; }
            }

            if (!isPeak) continue;
            if (i - lastAccepted < minFrameGap) continue;

            lastAccepted = i;
            onsets.Add(new Onset
            {
                time = i / frameRate,
                strength = value,
                lowRatio = lowRatio[i],
            });
        }

        return onsets;
    }

    private static float[] BuildHannWindow(int size)
    {
        float[] window = new float[size];

        for (int i = 0; i < size; i++)
        {
            window[i] = 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * i / (size - 1)));
        }

        return window;
    }
}
