using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把实时调参组件挂上去 / Attaches <see cref="HomeParallaxTuner"/> to the live HomeUI so the
/// parallax can be dialled in while it is actually running.
///
/// 守卫方向和别的工具相反 / The Play Mode guard here is INVERTED compared with every other
/// tool in this project. The rest refuse to run during Play Mode because they edit assets;
/// this one requires Play Mode, because there is no live HomeUI to attach to outside it.
/// That is deliberate, not an oversight.
///
/// The motion itself stays in HomeUI.lua.txt - this never writes to it. Use "Copy Lua block"
/// in the Inspector and paste the result over the constants there once the feel is right.
/// </summary>
public static class HomeParallaxTunerSetup
{
    private const string HomePrefabPath = "Assets/Arknights/Resources/Prefab/UI/HomeUI.prefab";
    private const string LuaPath = "Assets/Arknights/LuaScripts/UI/HomeUI.lua.txt";

    private const string Move1 = "move1";
    private const string Move2 = "move2";

    [MenuItem("Arknights/Home/Parallax Tuner")]
    public static void Attach()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("[HomeParallaxTunerSetup] Enter Play Mode first, then open HomeUI. " +
                           "This tool tunes the running screen - there is nothing to attach to " +
                           "while the editor is stopped.");
            return;
        }

        Transform move2 = FindLiveMove2();
        if (move2 == null)
        {
            Debug.LogError("[HomeParallaxTunerSetup] No live HomeUI found. Log in and get to the " +
                           "home screen, then run this again.");
            return;
        }

        Transform move1 = move2.parent.Find(Move1);
        if (move1 == null)
        {
            Debug.LogError("[HomeParallaxTunerSetup] Found '" + Move2 + "' but no sibling '" +
                           Move1 + "'. The prefab's parallax groups have been renamed.");
            return;
        }

        GameObject host = move2.parent.gameObject;

        // 幂等 / Idempotent: re-running focuses the existing tuner rather than stacking another.
        HomeParallaxTuner tuner = host.GetComponent<HomeParallaxTuner>();
        if (tuner == null) tuner = host.AddComponent<HomeParallaxTuner>();

        tuner.move1 = move1;
        tuner.move2 = move2;

        // 静止位置要从 prefab 读 / The rest positions have to come from the prefab asset. The Lua
        // captures them in init() before anything moves, so by now the live transforms are
        // already displaced and sampling them would bake the current offset in as the origin.
        if (!ReadBasePositions(out Vector3 base1, out Vector3 base2))
        {
            Debug.LogError("[HomeParallaxTunerSetup] Could not read rest positions from " +
                           HomePrefabPath + ". Aborting rather than tuning against a wrong origin.");
            Object.DestroyImmediate(tuner);
            return;
        }

        tuner.move1Base = base1;
        tuner.move2Base = base2;

        ApplyLuaValues(tuner);

        Selection.activeGameObject = host;

        Debug.Log("[HomeParallaxTunerSetup] Tuner attached to '" + host.name +
                  "', seeded from " + LuaPath + ". Drag the sliders in the Inspector while the " +
                  "game runs, then press 'Copy Lua block' and paste over the constants in the Lua. " +
                  "Values are NOT saved automatically - nothing here writes to the Lua.");
    }

    [MenuItem("Arknights/Home/Remove Parallax Tuner")]
    public static void Remove()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("[HomeParallaxTunerSetup] Only meaningful during Play Mode. " +
                           "The tuner is never saved, so leaving Play Mode already removes it.");
            return;
        }

        Transform move2 = FindLiveMove2();
        if (move2 == null)
        {
            Debug.Log("[HomeParallaxTunerSetup] No live HomeUI - nothing to remove.");
            return;
        }

        HomeParallaxTuner tuner = move2.parent.GetComponent<HomeParallaxTuner>();
        if (tuner == null)
        {
            Debug.Log("[HomeParallaxTunerSetup] No tuner attached.");
            return;
        }

        Object.DestroyImmediate(tuner);
        Debug.Log("[HomeParallaxTunerSetup] Tuner removed. The Lua drives the parallax again " +
                  "from the next frame.");
    }

    /// <summary>
    /// 找场上活着的 move2 / Finds the running HomeUI by its parallax pair rather than by the
    /// object's name, which differs between the prefab and its clone.
    /// </summary>
    private static Transform FindLiveMove2()
    {
        Transform[] all = Object.FindObjectsByType<Transform>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (Transform t in all)
        {
            if (t.name != Move2 || t.parent == null) continue;
            if (t.parent.Find(Move1) != null) return t;
        }

        return null;
    }

    private static bool ReadBasePositions(out Vector3 base1, out Vector3 base2)
    {
        base1 = Vector3.zero;
        base2 = Vector3.zero;

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HomePrefabPath);
        if (prefab == null) return false;

        Transform m1 = FindDeep(prefab.transform, Move1);
        Transform m2 = FindDeep(prefab.transform, Move2);
        if (m1 == null || m2 == null) return false;

        base1 = m1.localPosition;
        base2 = m2.localPosition;
        return true;
    }

    /// <summary>
    /// 从 Lua 读当前值当起点 / Seeds the sliders from whatever the Lua currently says, so the
    /// tuner always starts from the shipped state instead of from a copy that silently rots.
    /// </summary>
    public static void ApplyLuaValues(HomeParallaxTuner tuner)
    {
        string path = Path.Combine(Directory.GetCurrentDirectory(), LuaPath);
        if (!File.Exists(path))
        {
            Debug.LogWarning("[HomeParallaxTunerSetup] " + LuaPath + " not found; keeping the " +
                             "component's own defaults.");
            return;
        }

        string src = File.ReadAllText(path);

        tuner.move1X = Constant(src, "MOVE1_X", tuner.move1X);
        tuner.move1Y = Constant(src, "MOVE1_Y", tuner.move1Y);
        tuner.move2X = Constant(src, "MOVE2_X", tuner.move2X);
        tuner.move2Y = Constant(src, "MOVE2_Y", tuner.move2Y);
        tuner.move2TiltX = Constant(src, "MOVE2_TILT_X", tuner.move2TiltX);
        tuner.move2TiltY = Constant(src, "MOVE2_TILT_Y", tuner.move2TiltY);
        tuner.followSpeed = Constant(src, "FOLLOW_SPEED", tuner.followSpeed);
        tuner.idleSeconds = Constant(src, "IDLE_SECONDS", tuner.idleSeconds);
    }

    private static float Constant(string src, string name, float fallback)
    {
        // 只认行首的 local 声明 / Anchored to a line-start `local`, so a mention inside a comment
        // cannot be picked up instead of the real declaration.
        Match m = Regex.Match(src, @"^local\s+" + name + @"\s*=\s*(-?[0-9]*\.?[0-9]+)",
                              RegexOptions.Multiline);

        if (!m.Success)
        {
            Debug.LogWarning("[HomeParallaxTunerSetup] Could not find 'local " + name +
                             "' in the Lua; using " + fallback + ".");
            return fallback;
        }

        return float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), name);
            if (found != null) return found;
        }

        return null;
    }
}

/// <summary>Adds the two buttons that make the tuner worth having.</summary>
[CustomEditor(typeof(HomeParallaxTuner))]
public class HomeParallaxTunerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        HomeParallaxTuner tuner = (HomeParallaxTuner)target;

        EditorGUILayout.Space();

        if (tuner.move2 == null)
        {
            EditorGUILayout.HelpBox(
                "Not wired up. Run Arknights/Home/Parallax Tuner rather than adding this by hand.",
                MessageType.Warning);
            return;
        }

        EditorGUILayout.HelpBox(
            "TILT X is the up/down tilt (driven by vertical pointer movement).\n" +
            "TILT Y is the left/right tilt (driven by horizontal movement).\n\n" +
            "Nothing here is saved. Copy the block and paste it over the constants in " +
            "HomeUI.lua.txt before leaving Play Mode.",
            MessageType.Info);

        EditorGUILayout.Space();

        if (GUILayout.Button("Copy Lua block", GUILayout.Height(28f)))
        {
            string block = tuner.ToLuaBlock();
            EditorGUIUtility.systemCopyBuffer = block;
            Debug.Log("[HomeParallaxTuner] Copied to the clipboard:\n\n" + block);
        }

        if (GUILayout.Button("Reset to the Lua's current values"))
        {
            HomeParallaxTunerSetup.ApplyLuaValues(tuner);
            Debug.Log("[HomeParallaxTuner] Sliders reset to what HomeUI.lua.txt says.");
        }
    }
}
