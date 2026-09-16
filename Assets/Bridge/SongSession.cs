using UnityEngine;

// Carries the player's pick from the song select screen into the gameplay
// scene. A plain static rather than a DontDestroyOnLoad object because it holds
// one reference and no behaviour: nothing to update, nothing to clean up.
public static class SongSession
{
    public static SongChart Chart { get; private set; }

    public static bool HasChart
    {
        get { return Chart != null; }
    }

    public static void Set(SongChart chart)
    {
        Chart = chart;
    }

    // Cleared once gameplay has taken the chart, so opening the gameplay scene
    // directly in the Editor still falls back to whatever the scene has wired.
    public static void Clear()
    {
        Chart = null;
    }
}
