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
        /// Lua脚本在磁盘上的位置 / Where a Lua module lives on disk: under Assets/Arknights/Resources/,
        /// so the Editor reads the file as it is saved.
        ///
        /// 设备上读 Resources / On a device there is no such file, and the scripts are in no
        /// AssetBundle: AssetLoader's Asset.Load falls back to Resources.Load("LuaScripts/UI/HomeUI.lua"),
        /// which is this same file built into the player. That is why the folder is under Resources -
        /// outside it, a phone had no Lua at all and the front-end never started.
        /// </summary>
        private static string ScriptDiskPath(string module) {
            return Path.Combine(Application.dataPath, "Arknights", "Resources",
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
            return textAsset == null ? null : textAsset.text.TrimStart('\uFEFF');
        }

        // 从AB包中加载lua文件
        private static byte[] AssetLoader(ref string filepath) {
            //从AB包中获取文件
            filepath = luaScriptsFolder + filepath.Replace(".", "/") + ".lua";
            string[] args = filepath.Split('/');
            string name = args[args.Length - 1];
            TextAsset textAsset = Asset.Load<TextAsset>(filepath.Substring(0, filepath.Length - name.Length), name);
            return textAsset == null ? null : WithoutBom(textAsset.bytes);
        }

        private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

        /// <summary>
        /// 去掉UTF-8 BOM / A script saved with a UTF-8 byte order mark. File.ReadAllText drops it, so
        /// the Editor never notices; a TextAsset's bytes keep it, and Lua reads those three bytes as
        /// code - "unexpected symbol near '&lt;\239&gt;'" on the first line, before the front-end starts.
        /// </summary>
        public static byte[] WithoutBom(byte[] bytes) {
            if (bytes == null || bytes.Length < Utf8Bom.Length) return bytes;
            for (int i = 0; i < Utf8Bom.Length; i++)
                if (bytes[i] != Utf8Bom[i]) return bytes;
            byte[] rest = new byte[bytes.Length - Utf8Bom.Length];
            System.Array.Copy(bytes, Utf8Bom.Length, rest, 0, rest.Length);
            return rest;
        }
    }
}
