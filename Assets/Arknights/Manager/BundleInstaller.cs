using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Manager {
    /// <summary>
    /// 把包里的AB包装到可读的地方 / Puts the AssetBundles shipped inside the build where ABManager reads
    /// them.
    ///
    /// 手机上要先拷 / On Android and iOS, ABManager reads bundles from persistentDataPath - the place a
    /// downloaded update would go - but a build carries them in StreamingAssets, inside the APK, where
    /// System.IO cannot see. Without this a phone had no music at all. So the first launch, and the first
    /// launch after an update that changed them, copies every bundle across.
    ///
    /// 清单决定拷不拷 / BuildAssetBundles writes <see cref="ListFile"/> beside the bundles: a stamp line
    /// and every file. A copy of it in persistentDataPath, written only once every file has landed, is
    /// what says the bundles there are this build's; an interrupted copy simply runs again.
    ///
    /// The Editor and Windows read StreamingAssets directly (the Editor deploys with
    /// Arknights/AssetBundles/Deploy To Persistent Data Path), so there this does nothing.
    /// </summary>
    public static class BundleInstaller {
        public const string ListFile = "bundles.txt";

        private static bool installed;

        public static bool NeedsChecking {
            get {
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
                return !installed;
#else
                return false;
#endif
            }
        }

        public static IEnumerator EnsureInstalled() {
            if (!NeedsChecking) yield break;

            string shipped = null;
            using (UnityWebRequest request = UnityWebRequest.Get(StreamingUrl(ListFile))) {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success) {
                    // 没打包AB包 / A build made without bundles: the game runs without its music.
                    Debug.LogWarning($"[BundleInstaller] No {ListFile} in the build ({request.error}); no bundles to install.");
                    installed = true;
                    yield break;
                }

                shipped = request.downloadHandler.text;
            }

            string root = Application.persistentDataPath;
            string localList = Path.Combine(root, ListFile);

            if (File.Exists(localList) && File.ReadAllText(localList) == shipped) {
                installed = true;
                yield break;
            }

            int copied = 0;
            foreach (string raw in shipped.Split('\n')) {
                string relative = raw.Trim();
                if (relative.Length == 0 || relative.StartsWith("#")) continue;

                using (UnityWebRequest request = UnityWebRequest.Get(StreamingUrl(relative))) {
                    yield return request.SendWebRequest();

                    if (request.result != UnityWebRequest.Result.Success) {
                        Debug.LogError($"[BundleInstaller] Could not read {relative} from the build: {request.error}");
                        continue;
                    }

                    string destination = Path.Combine(root, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.WriteAllBytes(destination, request.downloadHandler.data);
                    copied++;
                }
            }

            // 最后写清单 / The list last: only a complete copy is marked as this build's.
            File.WriteAllText(localList, shipped);
            installed = true;
            Debug.Log($"[BundleInstaller] Installed {copied} bundle file(s) into {root}.");
        }

        /// <summary>On Android streamingAssetsPath is already a jar: URL; elsewhere it is a path.</summary>
        private static string StreamingUrl(string relative) {
            string root = Application.streamingAssetsPath;
            return root.Contains("://") ? root + "/" + relative : "file://" + Path.Combine(root, relative);
        }
    }
}
