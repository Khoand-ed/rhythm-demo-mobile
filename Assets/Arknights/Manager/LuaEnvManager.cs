using System.IO;
using System.Text;
using Tools;
using UnityEngine;
using XLua;

namespace Manager {
    public class LuaEnvManager : Single<LuaEnvManager> {
        private const string luaScriptsFolder = "LuaScripts/";

        //管理Lua的虚拟机
        private LuaEnv luaEnv;

        public LuaEnv LuaEnv() => luaEnv;

        /// <summary>
        /// 只要管理器存在了，虚拟机就存在
        /// </summary>
        protected override void Initialization() {
            luaEnv = new LuaEnv();
            luaEnv.AddLoader(LuaFolderLoader);
            luaEnv.AddLoader(AssetLoader);
            luaEnv.DoString("require('Global.Global')", "chunk", luaEnv.Global);
        }

        public void DoFile(string luaFileName, string chunkName = "chunk", LuaTable env = null) {
            luaEnv.DoString($"require('{luaFileName}')", "chunk", env);
        }

        /// <summary>
        /// Lua脚本在磁盘上的位置 / Where a Lua module lives on disk.
        ///
        /// Application.dataPath is Assets/, but the scripts live under Assets/Arknights/.
        /// Without the extra segment every require missed and fell through to AssetLoader.
        /// luaScriptsFolder stays relative so the AssetBundle names used by LoadLuaText and
        /// AssetLoader are unaffected.
        /// </summary>
        private static string ScriptDiskPath(string module) {
            return Path.Combine(Application.dataPath, "Arknights",
                luaScriptsFolder + module.Replace(".", "/") + ".lua.txt");
        }

        private static byte[] LuaFolderLoader(ref string filepath) {
            string scriptPath = ScriptDiskPath(filepath);
            filepath = luaScriptsFolder + filepath.Replace(".", "/") + ".lua.txt";
            return !File.Exists(scriptPath) ? null : Encoding.UTF8.GetBytes(File.ReadAllText(scriptPath));
        }

        public static string LoadLuaText(string filepath) {
            //和LuaFolderLoader一样先查磁盘, 这样没有AB包也能跑RuaUI界面
            //Check disk first, mirroring LuaFolderLoader, so RuaUI screens work without bundles.
            string diskPath = ScriptDiskPath(filepath);
            if (File.Exists(diskPath)) return File.ReadAllText(diskPath);

            filepath = luaScriptsFolder + filepath.Replace(".", "/") + ".lua";
            string[] args = filepath.Split('/');
            string name = args[args.Length - 1];
            TextAsset textAsset = Asset.Load<TextAsset>(filepath.Substring(0, filepath.Length - name.Length), name);
            return textAsset == null ? null : textAsset.text;
        }

        // 从AB包中加载lua文件
        private static byte[] AssetLoader(ref string filepath) {
            //从AB包中获取文件
            filepath = luaScriptsFolder + filepath.Replace(".", "/") + ".lua";
            string[] args = filepath.Split('/');
            string name = args[args.Length - 1];
            TextAsset textAsset = Asset.Load<TextAsset>(filepath.Substring(0, filepath.Length - name.Length), name);
            return textAsset == null ? null : textAsset.bytes;
        }
    }
}
