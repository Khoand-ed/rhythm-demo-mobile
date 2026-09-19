using System.Collections.Generic;
using UnityEngine;

// Every imported chart in one place. Nothing consumes this yet - it exists so
// the song select screen has a list to bind to, and so importing has somewhere
// to register its results.
[CreateAssetMenu(fileName = "BeatmapLibrary", menuName = "Rhythm/Beatmap Library")]
public class BeatmapLibrary : ScriptableObject
{
    public List<SongChart> charts = new List<SongChart>();

    public SongChart FindByStageId(string stageId)
    {
        for (int i = 0; i < charts.Count; i++)
        {
            if (charts[i] != null && charts[i].stageId == stageId) return charts[i];
        }

        return null;
    }

    // Every difficulty authored for one song, in ascending order.
    public List<SongChart> FindBySongId(string songId)
    {
        List<SongChart> found = new List<SongChart>();

        for (int i = 0; i < charts.Count; i++)
        {
            if (charts[i] != null && charts[i].songId == songId) found.Add(charts[i]);
        }

        found.Sort((a, b) => a.difficulty.CompareTo(b.difficulty));
        return found;
    }
}
