using Manager;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using XLua;

// 设备上的Lua / A phone reads each Lua script as a TextAsset's bytes (LuaEnvManager.AssetLoader), not
// from disk as the Editor does. Global.lua.txt was saved with a UTF-8 byte order mark: the Editor's
// File.ReadAllText dropped it, the phone handed it to Lua, and the front-end stopped on its first line.
// These compile every script the way the phone loads it.
public class LuaDeviceLoadTests
{
    private const string Scripts = "Assets/Arknights/Resources/LuaScripts";

    [Test]
    public void WithoutBom_DropsTheMarkAndNothingElse()
    {
        CollectionAssert.AreEqual(new byte[] { 0x2D, 0x2D }, LuaEnvManager.WithoutBom(new byte[] { 0xEF, 0xBB, 0xBF, 0x2D, 0x2D }));
        CollectionAssert.AreEqual(new byte[] { 0x2D, 0x2D }, LuaEnvManager.WithoutBom(new byte[] { 0x2D, 0x2D }));
        CollectionAssert.AreEqual(new byte[0], LuaEnvManager.WithoutBom(new byte[] { 0xEF, 0xBB, 0xBF }));
        CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB }, LuaEnvManager.WithoutBom(new byte[] { 0xEF, 0xBB }));
        Assert.IsNull(LuaEnvManager.WithoutBom(null));
    }

    [Test]
    public void EveryScript_CompilesFromItsTextAssetBytes()
    {
        string[] guids = AssetDatabase.FindAssets("t:TextAsset", new[] { Scripts });
        Assert.IsNotEmpty(guids, "no Lua scripts under " + Scripts);
        using (var lua = new LuaEnv())
        {
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                TextAsset script = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                byte[] bytes = LuaEnvManager.WithoutBom(script.bytes);
                Assert.DoesNotThrow(() => lua.LoadString<LuaFunction>(bytes, path).Dispose(), path);
            }
        }
    }
}
