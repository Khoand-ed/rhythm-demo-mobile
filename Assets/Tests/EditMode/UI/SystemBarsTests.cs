using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Manager;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// 系统栏 / When the game hides Android's status and navigation bars again (Manager.SystemBars), and the
// Player Settings it relies on. The hide itself is a call into Android and only runs on a phone: these
// pin the decisions around it and the settings, so an edit to either fails here instead of on a device
// where a bar stays over the game.
public class SystemBarsTests
{
    // --- whether to hide -------------------------------------------------------------------------

    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void AVisibleBar_IsHidden_WithNoKeyboardAndNoSplitScreen(bool status, bool nav)
    {
        Assert.IsTrue(SystemBars.ShouldHide(status, nav, keyboardVisible: false, multiWindow: false));
    }

    [Test]
    public void NothingIsHidden_WhenNoBarIsUp()
    {
        Assert.IsFalse(SystemBars.ShouldHide(false, false, keyboardVisible: false, multiWindow: false));
    }

    [Test]
    public void TheKeyboard_PostponesTheHide()
    {
        Assert.IsFalse(SystemBars.ShouldHide(true, true, keyboardVisible: true, multiWindow: false));
        Assert.AreEqual(SystemBars.Verdict.Wait, SystemBars.Decide(keyboardVisible: true, multiWindow: false),
            "kept pending, and hidden once the keyboard closes");
    }

    [Test]
    public void SplitScreen_LeavesTheBarsToTheSystem()
    {
        Assert.IsFalse(SystemBars.ShouldHide(true, true, keyboardVisible: false, multiWindow: true));
        Assert.AreEqual(SystemBars.Verdict.Drop, SystemBars.Decide(keyboardVisible: false, multiWindow: true));
        Assert.AreEqual(SystemBars.Verdict.Drop, SystemBars.Decide(keyboardVisible: true, multiWindow: true),
            "split screen wins over the keyboard: nothing to wait for");
    }

    [Test]
    public void FullScreenWithoutAKeyboard_GoesAhead()
    {
        Assert.AreEqual(SystemBars.Verdict.Hide, SystemBars.Decide(keyboardVisible: false, multiWindow: false));
    }

    // --- the behavior -----------------------------------------------------------------------------

    [TestCase(0)]   // Android 11's default, bars by touch: the one an OPPO A92 started at
    [TestCase(1)]   // Default
    public void ABehaviorThatLeavesABarUp_IsSetToTransient(int behavior)
    {
        Assert.IsTrue(SystemBars.NeedsTransient(behavior));
    }

    [Test]
    public void Transient_IsLeftAsItIs()
    {
        Assert.IsFalse(SystemBars.NeedsTransient(SystemBars.TransientBySwipe));
        Assert.AreEqual((int)UnityEngine.Android.AndroidWindowInsets.SystemBarsBehavior.ShowTransientBarsBySwipe,
            SystemBars.TransientBySwipe, "the number Unity gives transient bars");
    }

    [Test]
    public void API29AndEarlier_AreNotSet_TheirBehaviorIsUndefined()
    {
        Assert.IsFalse(SystemBars.NeedsTransient(-1));
    }

    [TestCase(-1, "Undefined (API 29 or earlier)")]
    [TestCase(0, "ShowBarsByTouch (Android 11's default)")]
    [TestCase(1, "Default")]
    [TestCase(2, "ShowTransientBarsBySwipe")]
    [TestCase(7, "7")]
    public void TheLog_NamesEveryBehavior(int behavior, string name)
    {
        Assert.AreEqual(name, SystemBars.BehaviorName(behavior));
    }

    // --- how soon --------------------------------------------------------------------------------

    [Test]
    public void DuringASong_ABarGoesSooner_ThanInTheMenus()
    {
        Assert.AreEqual(SystemBars.SongDelay, SystemBars.RevealDelay(SystemBars.GameplayScene));
        Assert.AreEqual(SystemBars.MenuDelay, SystemBars.RevealDelay("StartMenu"));
        Assert.Less(SystemBars.SongDelay, SystemBars.MenuDelay, "the right lane is under the navigation bar");
        Assert.LessOrEqual(SystemBars.SongDelay, 0.5f);
        Assert.GreaterOrEqual(SystemBars.MenuDelay, 1f, "time to read what the shade was pulled down for");
    }

    [Test]
    public void InTheMenus_ATransientBar_HasTimeToGoByItself()
    {
        // 菜单里系统先藏 / A phone that honours transient bars hides one itself after SystemAutoHide. Were
        // the game's hide first, every phone would log "Hid the ... after it showed" and the log could not
        // tell a phone that leaves the bar from one that takes it away. Margin for a late frame.
        Assert.GreaterOrEqual(SystemBars.MenuDelay, SystemBars.SystemAutoHide + 0.5f);
        Assert.LessOrEqual(SystemBars.MenuDelay, 5f, "a bar the phone leaves still goes soon");
    }

    [Test]
    public void TheGameplayScene_IsTheOneSongSelectLoads()
    {
        // SongSelectUI lives in the default assembly, which neither Arknights nor this test can see.
        string source = File.ReadAllText(Path.Combine(Application.dataPath, "Bridge", "SongSelectUI.cs"));
        Match declared = Regex.Match(source, @"GameplayScene\s*=\s*""([^""]+)""");
        Assert.IsTrue(declared.Success, "SongSelectUI.GameplayScene not found");
        Assert.AreEqual(declared.Groups[1].Value, SystemBars.GameplayScene);

        Assert.IsTrue(EditorBuildSettings.scenes.Any(s =>
                Path.GetFileNameWithoutExtension(s.path) == SystemBars.GameplayScene),
            "the gameplay scene is in the build");
    }

    // --- logging ---------------------------------------------------------------------------------

    [Test]
    public void AFailingCall_WarnsAtMostOnceAMinute()
    {
        Assert.IsTrue(SystemBars.WarningDue(float.NegativeInfinity, 0f), "the first failure always warns");
        Assert.IsFalse(SystemBars.WarningDue(10f, 10f + SystemBars.WarningInterval - 1f));
        Assert.IsTrue(SystemBars.WarningDue(10f, 10f + SystemBars.WarningInterval));
    }

    [Test]
    public void TheLogNamesTheBars()
    {
        Assert.AreEqual("status bar", SystemBars.Describe(true, false));
        Assert.AreEqual("navigation bar", SystemBars.Describe(false, true));
        Assert.AreEqual("status and navigation bars", SystemBars.Describe(true, true));
        Assert.AreEqual("no bar", SystemBars.Describe(false, false));
    }

    // --- the settings it relies on ---------------------------------------------------------------

    [Test]
    public void TheGameStarts_WithNoBar()
    {
        Assert.AreEqual(AndroidWindowInsetsType.None, PlayerSettings.Android.requestedVisibleInsets,
            "Player > Android > Resolution and Presentation > Insets: Status Bars and Navigation Bars off");
    }

    [Test]
    public void ABarASwipeShows_IsAskedToGoByItself()
    {
        Assert.AreEqual(AndroidSystemBarsBehavior.ShowTransientBarsBySwipe, PlayerSettings.Android.systemBarsBehavior,
            "Insets > System Bars Behavior: Default leaves a revealed bar up until the game hides it");
    }

    [Test]
    public void TheGameDrawsUnderTheBars()
    {
        // A bar shown at run time lies over the game rather than shrinking the window, so UIManager's
        // canvas size, measured once, stays right.
        Assert.IsTrue(PlayerSettings.Android.renderOutsideSafeArea);
    }

    [Test]
    public void NothingElse_SwitchesTheWindowMode()
    {
        // Screen's fullScreen, fullScreenMode and SetResolution switch the window, not the bars, and can
        // recreate the surface mid-song. SystemBars is the one place that touches the bars.
        string[] offenders = Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\bScreen\s*\.\s*(SetResolution|fullScreen)"))
            .Select(f => f.Substring(Application.dataPath.Length + 1).Replace('\\', '/'))
            .ToArray();
        CollectionAssert.IsEmpty(offenders);
    }
}
