using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Arknights.EditorTools {
    /// <summary>
    /// 构建AB包到StreamingAssets / Builds the game's AssetBundles into StreamingAssets.
    ///
    /// ABManager expects two things at runtime:
    ///   1. a "manifest bundle" named after the platform ("Win", "Android", "IOS"), and
    ///   2. one bundle file per bundle name, e.g. "Prefab/UI".
    /// BuildPipeline names the manifest bundle after the output folder, so this script renames
    /// it to the platform name ABManager.SingleABName asks for.
    /// </summary>
    public static class BuildAssetBundles {
        private const string OutputFolder = "Assets/StreamingAssets";

        [MenuItem("Arknights/AssetBundles/Build For Active Target", false, 0)]
        public static void Build() {
            string[] bundles = AssetDatabase.GetAllAssetBundleNames();
            if (bundles.Length == 0) {
                Debug.LogError("[BuildAssetBundles] No AssetBundle names are assigned to any asset, so there is " +
                               "nothing to build. Assign bundle names first - see SETUP.md.");
                return;
            }

            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            string platformName = GetPlatformBundleName(target);
            if (platformName == null) {
                Debug.LogError($"[BuildAssetBundles] ABManager has no bundle path for build target '{target}'. " +
                               "Switch to Windows, Android or iOS before building.");
                return;
            }

            Directory.CreateDirectory(OutputFolder);
            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
                OutputFolder, BuildAssetBundleOptions.None, target);
            if (manifest == null) {
                Debug.LogError("[BuildAssetBundles] BuildPipeline.BuildAssetBundles failed - see the console above.");
                return;
            }

            RenameManifestBundle(platformName);
            AssetDatabase.Refresh();
            Debug.Log($"[BuildAssetBundles] Built {bundles.Length} bundle(s) into {OutputFolder}/ " +
                      $"with manifest bundle '{platformName}'.");

            if (UsesPersistentDataPath(target)) DeployToPersistentDataPath(platformName);
        }

        /// <summary>
        /// Android/iOS下 ABManager 读的是 persistentDataPath, 不是 StreamingAssets
        /// Under UNITY_ANDROID / UNITY_IOS, ABManager.ABPath resolves to
        /// Application.persistentDataPath, so bundles built into StreamingAssets are invisible to
        /// Play mode. Copy them across, otherwise nothing you build is ever loaded in the Editor.
        /// </summary>
        private static bool UsesPersistentDataPath(BuildTarget target) {
            return target == BuildTarget.Android || target == BuildTarget.iOS;
        }

        private static void DeployToPersistentDataPath(string platformName) {
            string destRoot = Application.persistentDataPath;
            Directory.CreateDirectory(destRoot);

            int copied = 0;
            foreach (string source in Directory.GetFiles(OutputFolder, "*", SearchOption.AllDirectories)) {
                if (source.EndsWith(".meta")) continue;
                string relative = source.Substring(OutputFolder.Length).TrimStart('/', '\\');
                string dest = Path.Combine(destRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(source, dest, true);
                copied++;
            }

            Debug.Log($"[BuildAssetBundles] Copied {copied} file(s) to {destRoot}\n" +
                      $"  ABManager will look for the manifest bundle at: {destRoot}/{platformName}");
        }

        /// <summary>
        /// persistentDataPath 藏在 AppData/LocalLow 下面, 不好找 / That folder is hard to find by hand.
        /// </summary>
        [MenuItem("Arknights/AssetBundles/Reveal Bundle Folder", false, 2)]
        public static void RevealBundleFolder() {
            string path = UsesPersistentDataPath(EditorUserBuildSettings.activeBuildTarget)
                ? Application.persistentDataPath
                : Path.GetFullPath(OutputFolder);
            Directory.CreateDirectory(path);
            EditorUtility.RevealInFinder(path);
            Debug.Log("[BuildAssetBundles] Bundle folder for the active target: " + path);
        }

        /// <summary>
        /// 列出已分配的AB包名 / Reports which bundle names exist and which the game actually asks for.
        /// </summary>
        [MenuItem("Arknights/AssetBundles/List Assigned Bundle Names", false, 1)]
        public static void ListBundles() {
            string[] assigned = AssetDatabase.GetAllAssetBundleNames();
            var required = new List<string> {
                "Prefab/UI", "Prefab/Game",
                "Meta/Char", "Meta/Monster", "Meta/Dungeon", "Meta/Item",
                "Sprite/Char", "Sprite/Item",
                "Audio/Music/Login", "Audio/Music/Game", "Audio/Music/Shop",
                "Data/Shop", "Data/User"
            };

            var assignedSet = new HashSet<string>(assigned);
            var missing = required.FindAll(r => !assignedSet.Contains(r));

            Debug.Log($"[BuildAssetBundles] {assigned.Length} bundle name(s) assigned: " +
                      (assigned.Length == 0 ? "(none)" : string.Join(", ", assigned)));
            if (missing.Count > 0)
                Debug.LogWarning($"[BuildAssetBundles] Bundles the game loads but that are not assigned: " +
                                 string.Join(", ", missing));
        }

        /// <summary>
        /// 与ABManager.SingleABName保持一致 / Must match ABManager.SingleABName.
        /// </summary>
        private static string GetPlatformBundleName(BuildTarget target) {
            switch (target) {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return "Win";
                case BuildTarget.Android:
                    return "Android";
                case BuildTarget.iOS:
                    return "IOS";
                default:
                    return null;
            }
        }

        /// <summary>
        /// BuildPipeline以输出目录命名总包, 这里改成ABManager期望的平台名
        /// BuildPipeline names the manifest bundle after the output directory; rename it.
        /// </summary>
        private static void RenameManifestBundle(string platformName) {
            string builtName = Path.GetFileName(OutputFolder); // "StreamingAssets"
            if (builtName == platformName) return;

            foreach (string suffix in new[] { "", ".manifest" }) {
                string from = Path.Combine(OutputFolder, builtName + suffix);
                string to = Path.Combine(OutputFolder, platformName + suffix);
                if (!File.Exists(from)) continue;
                if (File.Exists(to)) File.Delete(to);
                File.Move(from, to);
            }
        }
    }
}
