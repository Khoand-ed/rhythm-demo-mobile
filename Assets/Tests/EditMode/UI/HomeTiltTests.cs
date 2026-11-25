using System.IO;
using NUnit.Framework;
using UI;
using UnityEngine;

// 每帧只读一次 / What Home's Lua reads from UI.HomeTilt every frame. The Lua reaches it by
// reflection - only xLua's own example types have generated wrappers - and every property read
// boxes its value, so reading Available, X, Y and FollowsPointer each frame took Home from 0.54 to
// 3.8 KB of garbage a frame on the phone. One read of Target a frame, the rest once.
public class HomeTiltTests
{
    private static string HomeLua()
    {
        return File.ReadAllText(Path.Combine(Application.dataPath,
            "Arknights", "Resources", "LuaScripts", "UI", "HomeUI.lua.txt"));
    }

    [Test]
    public void Target_IsStillUntilAReadingComesIn()
    {
        var host = new GameObject("TiltHost");
        try
        {
            HomeTilt tilt = HomeTilt.On(host);

            Assert.IsFalse(tilt.Available);
            Assert.AreEqual(Vector2.zero, tilt.Target);
        }
        finally
        {
            Object.DestroyImmediate(host);
        }
    }

    [TestCase("tilt.Available")]
    [TestCase("tilt.X")]
    [TestCase("tilt.Y")]
    [TestCase("DateTime.Now")]
    public void HomeLua_DoesNotReadThisEveryFrame(string read)
    {
        StringAssert.DoesNotContain(read, HomeLua(),
            "HomeUI.lua reads this through reflection; a frame reads tilt.Target once, and the clock comes from os.date.");
    }

    [Test]
    public void HomeLua_ReadsFollowsPointerOnlyOnce()
    {
        string lua = HomeLua();
        int first = lua.IndexOf("tilt.FollowsPointer", System.StringComparison.Ordinal);

        Assert.GreaterOrEqual(first, 0, "init reads it once");
        Assert.AreEqual(-1, lua.IndexOf("tilt.FollowsPointer", first + 1, System.StringComparison.Ordinal),
                        "anywhere else it would be read every frame");
    }
}
