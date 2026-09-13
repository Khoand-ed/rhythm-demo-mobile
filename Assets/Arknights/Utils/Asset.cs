using Manager;
using UnityEngine;

namespace Tools {
    public class Asset {
        public static T Load<T>(string path, string name) where T : Object {
            T asset = ABManager.Inst().LoadAsset<T>(name, path) ?? Resources.Load<T>(ResourcePath(path, name));
            if (asset == null) ReportMissing(path, name);
            return asset;
        }

        public static Object Load(string path, string name) {
            Object asset = ABManager.Inst().LoadAsset<Object>(name, path) ?? Resources.Load(ResourcePath(path, name));
            if (asset == null) ReportMissing(path, name);
            return asset;
        }

        public static T[] LoadAll<T>(string path, string name) where T : Object {
            return Resources.LoadAll<T>(ResourcePath(path, name));
        }

        public static Object[] LoadAll(string path, string name) {
            return Resources.LoadAll(ResourcePath(path, name));
        }

        /// <summary>
        /// Resources 的路径只认正斜杠 / Resources paths must use forward slashes.
        /// Path.Combine would emit a backslash on Windows ("Prefab/UI\Camera"), which
        /// Resources.Load never resolves - that silently disabled the whole fallback.
        /// </summary>
        private static string ResourcePath(string path, string name) {
            if (string.IsNullOrEmpty(path)) return name;
            return path.Replace('\\', '/').TrimEnd('/') + "/" + name;
        }

        /// <summary>
        /// 资源在AB包和Resources中都找不到时的报错
        /// Neither the AssetBundle nor Resources supplied the asset. The game ships without its
        /// art/data, so say exactly what is missing instead of letting the caller throw on null.
        /// </summary>
        private static void ReportMissing(string bundleName, string assetName) {
            Debug.LogError(
                $"[Asset] 资源缺失 / Missing asset '{assetName}' from bundle '{bundleName}'.\n" +
                $"  Looked in AssetBundle: {ABManager.Inst().ABPath}{bundleName}\n" +
                $"  Then in Resources:     {ResourcePath(bundleName, assetName)}\n" +
                $"  Game assets are not part of this repository - see SETUP.md for how to build the " +
                $"AssetBundles and place them in the path above.");
        }
    }
}
