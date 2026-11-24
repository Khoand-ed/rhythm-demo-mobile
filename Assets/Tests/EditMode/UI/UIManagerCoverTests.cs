using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UI;
using UnityEngine;

// 真正的 UIManager 盖住与揭开 / UIManager.Cover and Uncover driven over real screens. ScreenCoverTests
// pins the books with names; this pins what UIManager decides around them: which screens count as below
// a cover (lower in the canvas, open, switched on - or switched off by another cover), what Show and Hide
// do to the books, and covers stacked one over another.
//
// 不走 Inst() / A fresh UIManager, never Inst(): that one builds the UI camera and canvas from a bundle.
// The screens are plain GameObjects under one parent standing in for the canvas, put in its books the
// way Load does - by reflection, as SoundSettingsTests reaches SoundManager - and Awake is called by hand,
// since Edit Mode runs none and UIBase caches its gameObject and transform there. UnLoad is not driven:
// it ends in Object.Destroy, which Edit Mode refuses.
public class UIManagerCoverTests
{
    private sealed class FakeScreen : UIBase { }

    private const string Home = "HomeUI";
    private const string Depot = "DepotUI";
    private const string Cover = "ManufactureUI";
    private const string Second = "GachaUI";

    private static readonly MethodInfo AwakeOf =
        typeof(UIBase).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo NameOf =
        typeof(UIBase).GetField("Name", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo BooksOf =
        typeof(UIManager).GetField("uiDictionary", BindingFlags.Instance | BindingFlags.NonPublic);

    private GameObject canvas;
    private UIManager manager;
    private readonly Dictionary<string, UIBase> screens = new Dictionary<string, UIBase>();

    [SetUp]
    public void BuildACanvas()
    {
        Assert.IsNotNull(AwakeOf, "UIBase.Awake");
        Assert.IsNotNull(NameOf, "UIBase.Name");
        Assert.IsNotNull(BooksOf, "UIManager.uiDictionary");

        canvas = new GameObject("Canvas (test)", typeof(RectTransform));
        manager = new UIManager();
        screens.Clear();
    }

    [TearDown]
    public void ThrowItAway()
    {
        if (canvas != null) Object.DestroyImmediate(canvas);
    }

    /// <summary>Builds a screen under the canvas and puts it in the manager's books, as Load does - not shown.</summary>
    private UIBase Add(string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        UIBase screen = go.AddComponent<FakeScreen>();
        AwakeOf.Invoke(screen, null);
        NameOf.SetValue(screen, name);
        ((Dictionary<string, UIBase>)BooksOf.GetValue(manager)).Add(name, screen);
        screens[name] = screen;
        return screen;
    }

    /// <summary>Builds a screen and opens it on top, through UIManager.Show.</summary>
    private UIBase Open(string name)
    {
        Add(name);
        Assert.AreSame(screens[name], manager.Show(name));
        return screens[name];
    }

    private bool On(string name) => ((Component)screens[name]).gameObject.activeSelf;

    [Test]
    public void Cover_SwitchesOffTheOpenScreensBelow_AndKeepsThemOpen()
    {
        Open(Home);
        Open(Depot);
        UIBase cover = Open(Cover);

        manager.Cover(cover);

        Assert.IsFalse(On(Home));
        Assert.IsFalse(On(Depot));
        Assert.IsTrue(On(Cover), "a cover does not hide itself");
        CollectionAssert.AreEqual(new[] { Home, Depot, Cover }, manager.OpenScreens(), "still open: Back still finds them");
        Assert.AreSame(cover, manager.Top());
    }

    [Test]
    public void Cover_LeavesAScreenAboveIt_AndOneNotOpen()
    {
        Open(Home);
        UIBase cover = Open(Cover);
        Open(Second);
        Add(Depot); // in the books, never shown

        manager.Cover(cover);

        Assert.IsFalse(On(Home));
        Assert.IsTrue(On(Second), "above the cover, so seen");
        Assert.IsTrue(On(Depot), "not open, so not the cover's");

        manager.Uncover(cover);
        Assert.IsTrue(On(Home));
    }

    [Test]
    public void Uncover_GivesBackWhatTheCoverSwitchedOff()
    {
        Open(Home);
        Open(Depot);
        UIBase cover = Open(Cover);
        manager.Cover(cover);

        manager.Uncover(cover);

        Assert.IsTrue(On(Home));
        Assert.IsTrue(On(Depot));
    }

    [Test]
    public void AScreenSwitchedOffSomeOtherWay_IsNotWokenByUncover()
    {
        UIBase home = Open(Home);
        home.gameObject.SetActive(false);
        Open(Depot);
        UIBase cover = Open(Cover);

        manager.Cover(cover);
        manager.Uncover(cover);

        Assert.IsFalse(On(Home), "not the cover's to switch on");
        Assert.IsTrue(On(Depot));
    }

    [Test]
    public void HidingACover_GivesBackWhatItHeld_EvenIfItForgotToUncover()
    {
        Open(Home);
        UIBase cover = Open(Cover);
        manager.Cover(cover);

        manager.Hide(Cover);

        Assert.IsTrue(On(Home));
        Assert.IsFalse(On(Cover));
        CollectionAssert.AreEqual(new[] { Home }, manager.OpenScreens());
    }

    [Test]
    public void AScreenClosedWhileCovered_StaysOff()
    {
        Open(Home);
        Open(Depot);
        UIBase cover = Open(Cover);
        manager.Cover(cover);

        manager.Hide(Depot);
        manager.Uncover(cover);

        Assert.IsTrue(On(Home));
        Assert.IsFalse(On(Depot), "closed, so not switched back on");
    }

    [Test]
    public void AScreenShownOverTheCover_IsSwitchedOffByALaterCover()
    {
        // Show takes it out of the first cover's books: otherwise a second cover would think it hidden
        // already and leave it on, drawn under an opaque screen.
        Open(Home);
        UIBase cover = Open(Cover);
        manager.Cover(cover);

        manager.Show(Home);
        Assert.IsTrue(On(Home), "its own Show switches it on, on top");

        UIBase second = Open(Second);
        manager.Cover(second);

        Assert.IsFalse(On(Home));
        Assert.IsFalse(On(Cover));
    }

    [Test]
    public void StackedCovers_TheTopOneGoingFirst_GiveHomeBackLast()
    {
        Open(Home);
        UIBase cover = Open(Cover);
        manager.Cover(cover);
        UIBase second = Open(Second);
        manager.Cover(second);
        Assert.IsFalse(On(Cover), "the first cover is below the second");

        manager.Hide(Second);
        Assert.IsTrue(On(Cover));
        Assert.IsFalse(On(Home), "the first cover still holds it");

        manager.Hide(Cover);
        Assert.IsTrue(On(Home));
    }

    [Test]
    public void StackedCovers_TheLowerOneGoingFirst_LeaveHomeOffUnderTheTopOne()
    {
        Open(Home);
        UIBase cover = Open(Cover);
        manager.Cover(cover);
        UIBase second = Open(Second);
        manager.Cover(second);

        manager.Hide(Cover);
        Assert.IsFalse(On(Home), "the second cover is still up");

        manager.Hide(Second);
        Assert.IsTrue(On(Home));
        Assert.IsFalse(On(Cover), "closed while covered, so not switched back on");
    }

    [Test]
    public void Forget_DropsTheRecord_AndWakesNothing()
    {
        Open(Home);
        UIBase cover = Open(Cover);
        manager.Cover(cover);

        manager.Forget(cover);
        manager.Uncover(cover);

        Assert.IsFalse(On(Home), "a cover torn down wakes nothing");
    }
}
