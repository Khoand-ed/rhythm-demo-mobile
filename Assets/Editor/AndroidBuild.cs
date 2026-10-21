using System.Collections.Generic;
using System.IO;
using Arknights.EditorTools;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 安卓开发包 / Builds a development APK for a phone, in the one order that works:
///   1. the AssetBundles for Android into StreamingAssets, with the list BundleInstaller copies out of
///      the APK on the first launch (otherwise a phone has no music),
///   2. the player, a Development build - the only kind allowed to call the dev API over plain HTTP
///      (Player Settings: insecureHttpOption = development builds only).
///
/// 输出在仓库外 / The APK lands outside the repository, in ../builds/Promuse-&lt;version&gt;-&lt;server&gt;-dev.apk
/// next to the project folder, so no build output is ever committed. The server is the one chosen under
/// Arknights/Build/Server (ServerTargetMenu), in the name so two builds for two servers never overwrite
/// or pass for each other.
///
/// From the Editor: Arknights/Build/Android Development APK. From a shell, with the Editor closed:
///   Unity -batchmode -quit -projectPath . -buildTarget Android -executeMethod AndroidBuild.BatchDevelopmentApk
/// </summary>
public static class AndroidBuild
{
    [MenuItem("Arknights/Build/Android Development APK", false, 0)]
    public static void BuildDevelopmentApk()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        Build();
    }

    /// <summary>For -executeMethod: exits 0 when the APK was built, 1 otherwise.</summary>
    public static void BatchDevelopmentApk()
    {
        bool built = Build();
        EditorApplication.Exit(built ? 0 : 1);
    }

    public static string OutputPath =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "builds",
                                      $"Promuse-{PlayerSettings.bundleVersion}-{Server}-dev.apk"));

    private static string Server
    {
        get
        {
            Promuse.Net.ServerTarget target = ServerTargetMenu.Find();
            return (target != null ? target.environment : Promuse.Net.ServerEnvironment.Local).ToString().ToLowerInvariant();
        }
    }

    private static bool Build()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
            !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
        {
            Debug.LogError("[AndroidBuild] Could not switch to Android. Is Android Build Support installed?");
            return false;
        }

        BuildAssetBundles.Build();

        var scenes = new List<string>();
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled) scenes.Add(scene.path);
        }

        string output = OutputPath;
        Directory.CreateDirectory(Path.GetDirectoryName(output));

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes.ToArray(),
            locationPathName = output,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.Development,
        });

        BuildSummary summary = report.summary;
        bool ok = summary.result == BuildResult.Succeeded;

        string line = $"[AndroidBuild] {summary.result}: {scenes.Count} scene(s), version {PlayerSettings.bundleVersion} " +
                      $"(code {PlayerSettings.Android.bundleVersionCode}), {summary.totalSize / (1024 * 1024)} MB in " +
                      $"{summary.totalTime:mm\\:ss}, {summary.totalErrors} error(s)\n  {output}";
        if (ok) Debug.Log(line + "\n  Install: adb install -r \"" + output + "\"");
        else Debug.LogError(line);

        return ok;
    }
}
