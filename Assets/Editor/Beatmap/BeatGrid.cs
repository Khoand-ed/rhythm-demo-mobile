using System.Collections.Generic;
using UnityEngine;

// The song's musical grid: tempo, where beats fall, and which beat starts a
// bar. Charting decisions in rhythm games are made against this grid, not
// against raw time - a note on beat 1 matters more than one on the last
// sixteenth of the bar, whatever their loudness says.
//
// Assumes 4/4, which covers the overwhelming majority of charted music.
public class BeatGrid
{
    public const int BeatsPerBar = 4;
    public const int SlotsPerBeat = 4;
    public const int SlotsPerBar = BeatsPerBar * SlotsPerBeat;

    public readonly float bpm;
    public readonly float beatPeriod;

    // Time of some beat (the tempo estimate's phase).
    public readonly float phase;

    // Which of the four beats after `phase` is beat 1 of a bar.
    public readonly int downbeatOffset;

    public BeatGrid(float bpm, float phase, int downbeatOffset)
    {
        this.bpm = Mathf.Max(1f, bpm);
        beatPeriod = 60f / this.bpm;
        this.phase = phase;
        this.downbeatOffset = ((downbeatOffset % BeatsPerBar) + BeatsPerBar) % BeatsPerBar;
    }

    public float BarPeriod
    {
        get { return beatPeriod * BeatsPerBar; }
    }

    // Time of the first bar line at or after 0 - written into the beatmap so
    // editors and runtime visuals can draw the same grid.
    public float FirstDownbeat
    {
        get
        {
            float t = phase + downbeatOffset * beatPeriod;
            t -= Mathf.Floor(t / BarPeriod) * BarPeriod;
            return t;
        }
    }

    // Fractional beats since the first downbeat.
    public float BeatPosition(float time)
    {
        return (time - FirstDownbeat) / beatPeriod;
    }

    public int BarIndex(float time)
    {
        return Mathf.FloorToInt((time - FirstDownbeat) / BarPeriod + 0.0001f);
    }

    public float BarStart(int bar)
    {
        return FirstDownbeat + bar * BarPeriod;
    }

    // Sixteenth-note position inside the bar, 0..15.
    public int SlotInBar(float time)
    {
        int slot = Mathf.RoundToInt(BeatPosition(time) * SlotsPerBeat);
        return ((slot % SlotsPerBar) + SlotsPerBar) % SlotsPerBar;
    }

    public bool IsOnBeat(float time)
    {
        return SlotInBar(time) % SlotsPerBeat == 0;
    }

    // How much a note at this position of the bar matters musically - the
    // metrical hierarchy every charter works to: the downbeat, then beat 3,
    // then the backbeats, then off-beat eighths, then sixteenths.
    public float MetricWeight(float time)
    {
        int slot = SlotInBar(time);

        if (slot == 0) return 1f;
        if (slot == 8) return 0.9f;
        if (slot % 4 == 0) return 0.8f;
        if (slot % 2 == 0) return 0.55f;
        return 0.35f;
    }

    // Beat 1 is where the kick drum lands far more often than anywhere else,
    // so the offset whose beats carry the most low-end attack is the bar line.
    public static int DetectDownbeat(List<Onset> onsets, float bpm, float phase)
    {
        float beatPeriod = 60f / Mathf.Max(1f, bpm);
        float[] score = new float[BeatsPerBar];

        for (int i = 0; i < onsets.Count; i++)
        {
            Onset onset = onsets[i];
            float beats = (onset.sourceTime - phase) / beatPeriod;
            int nearest = Mathf.RoundToInt(beats);

            // Only attacks close to a beat say anything about the bar.
            if (Mathf.Abs(beats - nearest) > 0.15f) continue;

            int offset = ((nearest % BeatsPerBar) + BeatsPerBar) % BeatsPerBar;
            score[offset] += onset.strength * onset.lowRatio;
        }

        int best = 0;
        for (int i = 1; i < BeatsPerBar; i++)
        {
            if (score[i] > score[best]) best = i;
        }

        return best;
    }
}

