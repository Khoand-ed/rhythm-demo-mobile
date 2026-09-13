using System.IO;
using UnityEditor;
using UnityEngine;

namespace Arknights.EditorTools {
    /// <summary>
    /// 一键换背景音乐 / One-click background-music swap.
    ///
    /// 资源名是写死在代码里的, 不能改:
    ///   LoginUI.cs:47      Audio/Music/Login   m_sys_title_intro / m_sys_title_loop
    ///   HomeUI.lua.txt:11  Audio/Music/Home    m_sys_void_intro  / m_sys_void_loop
    /// The load names are fixed by code, so the file that lands in the project has to carry them
    /// whatever the track is really called. Pick any mp3 from anywhere - the original is copied,
    /// never moved or renamed in place.
    ///
    /// 一首曲子要装成两份 intro 和 loop:
    /// 界面先不循环地放一遍intro, 在播放结束的回调里才开始循环loop。PlayMusic 遇到空的 clip 会直接
    /// 返回而且不会执行那个回调(SoundManager.cs:38), 所以 intro 少一个, loop 就永远不会响。
    ///
    /// One track is installed under BOTH names on purpose. Each screen plays the intro once and
    /// starts the looping track from its completion callback, and PlayMusic returns early on a null
    /// clip WITHOUT firing that callback - so leaving the intro slot empty means the loop never
    /// starts at all. Installed twice, the track plays through once and then repeats forever.
    /// </summary>
    public static class ImportMusic {
        private const string MusicRoot = "Assets/Arknights/Audio/Music";

        [MenuItem("Arknights/Audio/Set Login Music...", false, 0)]
        public static void SetLoginMusic() {
            Pick("login", "Login", "Audio/Music/Login", "m_sys_title");
        }

        [MenuItem("Arknights/Audio/Set Home Music...", false, 1)]
        public static void SetHomeMusic() {
            Pick("home", "Home", "Audio/Music/Home", "m_sys_void");
        }

        /// <summary>
        /// GameManager.Initialization 一开机就加载战斗音乐, 哪怕还没有战斗场景
        /// GameManager loads the battle track at boot (GameManager.cs:19) whether or not a battle
        /// ever starts, so an empty slot is an error on every launch.
        /// </summary>
        [MenuItem("Arknights/Audio/Set Battle Music...", false, 2)]
        public static void SetBattleMusic() {
            Pick("battle", "Game", "Audio/Music/Game", "m_bat_indust");
        }

        [MenuItem("Arknights/Audio/Set Shop Music...", false, 3)]
        public static void SetShopMusic() {
            Pick("shop", "Shop", "Audio/Music/Shop", "m_sys_shop");
        }

        private static void Pick(string screen, string folder, string bundle, string prefix) {
            string source = EditorUtility.OpenFilePanel("Choose the " + screen + " screen track", "", "mp3,ogg,wav");
            if (string.IsNullOrEmpty(source)) return;
            if (Install(source, folder, bundle, prefix)) OfferBuild();
        }

        /// <summary>
        /// 把一个音频文件装成某个界面的intro+loop / installs one audio file as a screen's intro and loop.
        /// </summary>
        public static bool Install(string sourceFile, string folder, string bundle, string prefix) {
            if (!File.Exists(sourceFile)) {
                Debug.LogError($"[ImportMusic] No such file: {sourceFile}");
                return false;
            }

            string directory = MusicRoot + "/" + folder;
            Directory.CreateDirectory(directory);

            string extension = Path.GetExtension(sourceFile);
            foreach (string role in new[] { "_intro", "_loop" }) {
                string assetPath = directory + "/" + prefix + role + extension;
                File.Copy(sourceFile, assetPath, true);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                Configure(assetPath, bundle);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ImportMusic] '{Path.GetFileName(sourceFile)}' installed as {prefix}_intro and " +
                      $"{prefix}_loop under {directory}, bundle '{bundle}'.");
            return true;
        }

        /// <summary>
        /// 资源不在 Resources 里, 只能从AB包加载, 所以装完必须重新打包
        /// These live outside Resources, so the AssetBundle is the only way the game can reach them -
        /// a track that is imported but not rebuilt simply will not play.
        /// </summary>
        private static void OfferBuild() {
            if (EditorUtility.DisplayDialog("Rebuild AssetBundles?",
                    "The track is in the project, but the game loads music from the AssetBundles.\n\n" +
                    "Rebuild them now so it actually plays?", "Rebuild", "Later")) {
                BuildAssetBundles.Build();
            } else {
                Debug.LogWarning("[ImportMusic] Not rebuilt. Run Arknights/AssetBundles/Build For Active " +
                                 "Target before the new track will be heard.");
            }
        }

        private static void Configure(string assetPath, string bundle) {
            AudioImporter importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
            if (importer == null) {
                Debug.LogWarning($"[ImportMusic] {assetPath} did not import as audio.");
                return;
            }

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.CompressedInMemory;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = false;

            importer.assetBundleName = bundle;
            importer.SaveAndReimport();
        }
    }
}
