using Data.Char;
using UnityEngine;

// Carries the player's picks from the front-end into the gameplay scene: the
// chart chosen on the song select screen, and the operator chosen on the
// character select screen after it. A plain static rather than a
// DontDestroyOnLoad object because it holds two references and no behaviour:
// nothing to update, nothing to clean up.
//
// 角色是 CharData 不是 id / Character is a CharData rather than a string id.
// Assets/Bridge/ exists precisely to span the two halves, and CharData already
// reaches its metadata through GetCharMeta(), so holding it here saves the far
// end a second Resources lookup. It comes out of PlayerData's charList, and
// PlayerData is a ScriptableObject loaded from Resources - not a scene object -
// so the reference survives SceneManager.LoadScene.
public static class SongSession
{
    public static SongChart Chart { get; private set; }

    public static CharData Character { get; private set; }

    public static bool HasChart
    {
        get { return Chart != null; }
    }

    public static bool HasCharacter
    {
        get { return Character != null; }
    }

    public static void Set(SongChart chart)
    {
        Chart = chart;
    }

    public static void SetCharacter(CharData character)
    {
        Character = character;
    }

    // Cleared once gameplay has taken the chart, so opening the gameplay scene
    // directly in the Editor still falls back to whatever the scene has wired.
    //
    // 先取再清 / Whatever eventually consumes Character must copy it before this
    // runs, the way GameManager.Start already does for the chart:
    //     noteSpawner.chart = SongSession.Chart;
    //     SongSession.Clear();
    // Reading Character after the clear finds null - which is what a results
    // screen asking "who did I just play as?" would otherwise trip over.
    public static void Clear()
    {
        Chart = null;
        Character = null;
    }
}
