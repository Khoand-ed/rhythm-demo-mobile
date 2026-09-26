using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 建立并接上三份调参资产 / Creates the three tuning assets and wires them onto the GameManager
/// in the open scene.
///
/// 之前这些值存在 Main.unity 里 / These values used to be serialized inline on GameManager,
/// which meant a balance change never showed up as a reviewable diff, two scenes could not
/// share one pass, and a per-difficulty variant needed a duplicated component. They are assets
/// now; this tool is what makes that reproducible rather than a one-off hand edit.
///
/// LaneConfig 故意不在此列 / LaneConfig is deliberately NOT converted. It holds Transform
/// references to the spawn and hit markers, and a ScriptableObject cannot reference a scene
/// object - Unity drops the reference on save. Lane geometry belongs to the scene.
/// </summary>
public static class TuningAssetSetup
{
    private const string Folder = "Assets/Source/Tuning";

    private const string JudgePath = Folder + "/JudgeSettings.asset";
    private const string HealthPath = Folder + "/HealthSettings.asset";
    private const string FeverPath = Folder + "/FeverSettings.asset";

    [MenuItem("Tools/Rhythm/Create Tuning Assets")]
    public static void Create()
    {
        if (Application.isPlaying)
        {
            Debug.LogError("[TuningAssetSetup] Exit Play Mode first.");
            return;
        }

        EnsureFolder();

        // 已存在就不动 / Find-or-create: an asset already adjusted by hand is left exactly as
        // it is. Re-running this must never quietly undo a balance pass.
        JudgeSettings judge = FindOrCreate<JudgeSettings>(JudgePath, out bool judgeMade);
        HealthSettings health = FindOrCreate<HealthSettings>(HealthPath, out bool healthMade);
        FeverSettings fever = FindOrCreate<FeverSettings>(FeverPath, out bool feverMade);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        int wired = Wire(judge, health, fever);

        Debug.Log("[TuningAssetSetup] Judge " + (judgeMade ? "created" : "kept") +
                  ", Health " + (healthMade ? "created" : "kept") +
                  ", Fever " + (feverMade ? "created" : "kept") +
                  ". Wired onto " + wired + " GameManager(s) in the open scene." +
                  (wired > 0 ? " Save the scene to keep it." : ""));
    }

    /// <summary>
    /// 把三份资产重置回类的默认值 / Resets all three assets to the defaults their classes
    /// declare. The paired explicit reset for the find-or-create above, and the only way back
    /// once a balance pass has gone somewhere unplayable.
    /// </summary>
    [MenuItem("Tools/Rhythm/Reset Tuning Assets To Default")]
    public static void Reset()
    {
        if (Application.isPlaying)
        {
            Debug.LogError("[TuningAssetSetup] Exit Play Mode first.");
            return;
        }

        EnsureFolder();

        Overwrite<JudgeSettings>(JudgePath);
        Overwrite<HealthSettings>(HealthPath);
        Overwrite<FeverSettings>(FeverPath);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[TuningAssetSetup] All three tuning assets reset to their class defaults.");
    }

    // ------------------------------------------------------------------ helpers

    private static void EnsureFolder()
    {
        if (AssetDatabase.IsValidFolder(Folder)) return;

        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
    }

    private static T FindOrCreate<T>(string path, out bool created) where T : ScriptableObject
    {
        T existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null)
        {
            created = false;
            return existing;
        }

        T made = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(made, path);
        created = true;
        return made;
    }

    /// <summary>
    /// 覆盖字段而不是换资产 / Overwrites the values in place rather than deleting and recreating,
    /// so every reference to the asset survives the reset.
    /// </summary>
    private static void Overwrite<T>(string path) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
        {
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<T>(), path);
            return;
        }

        T fresh = ScriptableObject.CreateInstance<T>();
        EditorUtility.CopySerialized(fresh, asset);
        Object.DestroyImmediate(fresh);

        EditorUtility.SetDirty(asset);
    }

    private static int Wire(JudgeSettings judge, HealthSettings health, FeverSettings fever)
    {
        int count = 0;

        foreach (GameManager gm in Object.FindObjectsByType<GameManager>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            SerializedObject so = new SerializedObject(gm);

            // 只填空的 / Only fills empty slots, so a scene pointed at its own variant keeps it.
            bool changed = FillIfEmpty(so, "judge", judge);
            changed |= FillIfEmpty(so, "health", health);
            changed |= FillIfEmpty(so, "fever", fever);

            if (!changed) continue;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(gm.gameObject.scene);
            count++;
        }

        return count;
    }

    private static bool FillIfEmpty(SerializedObject so, string field, Object value)
    {
        SerializedProperty p = so.FindProperty(field);
        if (p == null || p.objectReferenceValue != null) return false;

        p.objectReferenceValue = value;
        return true;
    }
}
