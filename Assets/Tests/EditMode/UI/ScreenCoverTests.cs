using System.Collections.Generic;
using NUnit.Framework;
using UI;

// 盖住的账 / The books UIManager.Cover keeps: which screens a full screen switched off while it is up,
// and which it gives back as it goes. Plain names stand in for screens - the bookkeeping never looks at
// one. Only what UIManager can produce today is pinned: one cover over the screens below it.
public class ScreenCoverTests
{
    private const string Home = "HomeUI";
    private const string Depot = "DepotUI";
    private const string Cover = "ManufactureUI";

    private static bool AllOpen(string screen) => true;

    [Test]
    public void Covering_SwitchesOffExactlyTheScreensBelow()
    {
        var covers = new ScreenCovers<string>();

        List<string> off = covers.Cover(Cover, new[] { Home, Depot });

        CollectionAssert.AreEqual(new[] { Home, Depot }, off);
        Assert.IsTrue(covers.IsCovered(Home));
        Assert.IsTrue(covers.IsCovered(Depot));
        Assert.IsTrue(covers.IsCovering(Cover));
        Assert.IsFalse(covers.IsCovered(Cover), "a cover does not hide itself");
    }

    [Test]
    public void Covering_LeavesOutTheCoverItself_AndAScreenNamedTwice()
    {
        var covers = new ScreenCovers<string>();

        List<string> off = covers.Cover(Cover, new[] { Home, Cover, Home, null });

        CollectionAssert.AreEqual(new[] { Home }, off);
    }

    [Test]
    public void CoveringTwice_DoesNothingTheSecondTime()
    {
        var covers = new ScreenCovers<string>();
        covers.Cover(Cover, new[] { Home });

        List<string> again = covers.Cover(Cover, new[] { Home, Depot });

        Assert.IsEmpty(again);
        Assert.IsFalse(covers.IsCovered(Depot), "the first record stands");
        CollectionAssert.AreEqual(new[] { Home }, covers.Uncover(Cover, AllOpen));
    }

    [Test]
    public void Uncovering_GivesBackWhatWasSwitchedOff_Once()
    {
        var covers = new ScreenCovers<string>();
        covers.Cover(Cover, new[] { Home, Depot });

        CollectionAssert.AreEqual(new[] { Home, Depot }, covers.Uncover(Cover, AllOpen));
        Assert.IsFalse(covers.IsCovered(Home));
        Assert.IsFalse(covers.IsCovering(Cover));

        Assert.IsEmpty(covers.Uncover(Cover, AllOpen), "a second uncover has nothing left to give");
    }

    [Test]
    public void UncoveringAScreenThatNeverCovered_GivesNothing()
    {
        var covers = new ScreenCovers<string>();
        covers.Cover(Cover, new[] { Home });

        Assert.IsEmpty(covers.Uncover(Depot, AllOpen));
        Assert.IsTrue(covers.IsCovered(Home), "the real cover still holds it");
    }

    [Test]
    public void AScreenClosedWhileCovered_IsNotSwitchedBackOn()
    {
        var covers = new ScreenCovers<string>();
        covers.Cover(Cover, new[] { Home, Depot });

        List<string> on = covers.Uncover(Cover, screen => screen != Depot);

        CollectionAssert.AreEqual(new[] { Home }, on);
        Assert.IsFalse(covers.IsCovered(Depot), "nor still counted as hidden");
    }

    [Test]
    public void AScreenShownAgainOnTop_IsLeftToItsOwnShow()
    {
        // UIManager.Show releases a screen before showing it over the cover.
        var covers = new ScreenCovers<string>();
        covers.Cover(Cover, new[] { Home, Depot });

        covers.Release(Home);

        Assert.IsFalse(covers.IsCovered(Home));
        CollectionAssert.AreEqual(new[] { Depot }, covers.Uncover(Cover, AllOpen));
    }

    [Test]
    public void Forgetting_DropsTheRecord_AndSwitchesNothingOn()
    {
        // A cover destroyed mid-teardown: the record goes, and nothing is woken.
        var covers = new ScreenCovers<string>();
        covers.Cover(Cover, new[] { Home });

        covers.Forget(Cover);

        Assert.IsFalse(covers.IsCovering(Cover));
        Assert.IsFalse(covers.IsCovered(Home));
        Assert.IsEmpty(covers.Uncover(Cover, AllOpen));
    }

    [Test]
    public void ACoverAfterAnUncover_CoversAfresh()
    {
        // Leaving and coming back: the screen fades in again and covers again.
        var covers = new ScreenCovers<string>();
        covers.Cover(Cover, new[] { Home });
        covers.Uncover(Cover, AllOpen);

        CollectionAssert.AreEqual(new[] { Home }, covers.Cover(Cover, new[] { Home }));
    }
}
