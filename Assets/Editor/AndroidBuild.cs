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
        ForgetGradleApk();

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

        if (ok && GradleApkFolders().Count == 0)
        {
            Debug.LogWarning("[AndroidBuild] Gradle's APK was not under Library/Bee/Android/Prj/<backend>/Gradle/launcher/" +
                             "build/outputs/apk. If Unity has moved its Gradle project, point GradleApkFolders there, or " +
                             "the next build updates this APK in place and keeps its dead space.");
        }

        return ok;
    }

    /// <summary>
    /// 每次从头打包 / Deletes the APK Gradle packed last time, so it packs this one whole. Kept, that file is
    /// updated in place (incremental packaging): an entry that changed is written again elsewhere and its old
    /// bytes stay where they were, referenced by nothing. Unity copies the file out as it is, so each APK
    /// carried the dead space of the builds before it - the first 1.2 with Manufacture weighed 147 MB for
    /// 109 MB of entries. Packing whole costs a few seconds; every other Gradle step stays incremental.
    /// </summary>
    private static void ForgetGradleApk()
    {
        foreach (string folder in GradleApkFolders())
        {
            try
            {
                Directory.Delete(folder, true);
            }
            catch (System.Exception e) when (e is IOException || e is System.UnauthorizedAccessException)
            {
                Debug.LogWarning($"[AndroidBuild] Could not delete {folder} ({e.Message}): this APK may keep the " +
                                 "dead space of the last one.");
            }
        }
    }

    // Unity's Gradle project, one per scripting backend: Library/Bee/Android/Prj/IL2CPP (or Mono2x)/Gradle.
    private static List<string> GradleApkFolders()
    {
        var folders = new List<string>();
        string projects = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "Bee", "Android", "Prj"));
        if (!Directory.Exists(projects)) return folders;

        foreach (string backend in Directory.GetDirectories(projects))
        {
            string apk = Path.Combine(backend, "Gradle", "launcher", "build", "outputs", "apk");
            if (Directory.Exists(apk)) folders.Add(apk);
        }
        return folders;
    }
}
