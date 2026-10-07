using Data.Char;
using UnityEngine;

/// <summary>
/// 两种玩法 / How a song is being played.
///
/// Ranked spends the stage's stamina, and a cleared run counts: it advances the missions and is
/// reviewed for the leaderboards. Practice costs nothing and counts for nothing - no run is opened
/// on the server at all - so a chart can be learned as often as the player likes without any of
/// that turning into progress.
///
/// 重试跟着走 / A retry keeps the mode it was started in: retrying a ranked song opens, and pays
/// for, a new ranked run; retrying practice is free.
/// </summary>
public enum PlayMode
{
    Ranked,
    Practice
}

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

    /// <summary>
    /// 这一局的身份 / The run the server opened for this attempt, and the seed it
    /// issued. Empty when gameplay was entered straight from the Editor without
    /// going through the song select, which is a supported way to work.
    ///
    /// The seed is here because JudgeUpgradePassive rolls per judgement: a result
    /// the server can re-simulate has to have been played against the sequence
    /// the server knows. Nothing reads it yet - Phase 4 does.
    /// </summary>
    public static System.Guid RunId { get; private set; }

    public static long Seed { get; private set; }

    /// <summary>What the run cost, so a ranked retry can say what it will cost again.</summary>
    public static int StaminaCost { get; private set; }

    /// <summary>Ranked unless the player chose Practice; see <see cref="PlayMode"/>.</summary>
    public static PlayMode Mode { get; private set; }

    public static bool HasRun
    {
        get { return RunId != System.Guid.Empty; }
    }

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

    /// <summary>
    /// Written once, by whatever opened the run against the server - or, for practice, with an
    /// empty run id and a seed of the device's own choosing, since nothing is ever reported.
    /// </summary>
    public static void SetRun(System.Guid runId, long seed, int staminaCost)
    {
        RunId = runId;
        Seed = seed;
        StaminaCost = staminaCost;
    }

    public static void SetMode(PlayMode mode)
    {
        Mode = mode;
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
        RunId = System.Guid.Empty;
        Seed = 0;
        StaminaCost = 0;
        Mode = PlayMode.Ranked;
    }
}
