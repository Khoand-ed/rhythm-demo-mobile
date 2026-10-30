using System;
using NUnit.Framework;
using Promuse.Contracts;
using Promuse.Contracts.Manufacture;
using Promuse.Manufacture;

// 制造站的数据层 / What the screens read from ManufactureManager, without a server: the view it keeps,
// the server's clock it counts by, and which refusals send it back to the server for a fresh view.
// A manager of its own per test - the singleton is never touched.
public class ManufactureManagerTests
{
    private static readonly DateTimeOffset Anchor = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    private static StationState Lmd(DateTimeOffset anchor) => new StationState(
        "lmd", 1, 3, new StationLevel(1, 2, 50, 1800, 16), 0, anchor, anchor.AddMinutes(30), anchor.AddHours(8), null);

    private static ManufactureView ViewAt(DateTimeOffset serverTime, DateTimeOffset anchor) =>
        new ManufactureView(true, null, new[] { Lmd(anchor) }, serverTime);

    [Test]
    public void Accept_KeepsTheViewAndCountsByTheServersClock()
    {
        var manager = new ManufactureManager();
        int changed = 0;
        manager.Changed += () => changed++;

        // The server is two hours ahead of this device; the countdowns must follow the server.
        DateTimeOffset serverTime = DateTimeOffset.UtcNow.AddHours(2);
        ManufactureView view = ViewAt(serverTime, serverTime.AddMinutes(-70));
        manager.Accept(view);

        Assert.AreSame(view, manager.View);
        Assert.AreEqual(1, changed);
        Assert.That((manager.ServerNow - serverTime.UtcDateTime).Duration(), Is.LessThan(TimeSpan.FromSeconds(5)));

        // Seventy minutes after the anchor by the server's clock: two units, whatever this device's clock says.
        Assert.AreEqual(2, manager.ReadNow(manager.Station("lmd")).StoredUnits);
    }

    [Test]
    public void Read_IsTheSharedClockAtAGivenMoment()
    {
        StationState lmd = Lmd(Anchor);

        Assert.AreEqual(0, ManufactureManager.Read(lmd, Anchor.AddMinutes(29)).StoredUnits);
        Assert.AreEqual(1, ManufactureManager.Read(lmd, Anchor.AddMinutes(30)).StoredUnits);

        StationReading full = ManufactureManager.Read(lmd, Anchor.AddHours(12));
        Assert.AreEqual(16, full.StoredUnits);
        Assert.IsNull(full.NextUnitAt);
    }

    [Test]
    public void Station_FindsOneByIdOrNothing()
    {
        var manager = new ManufactureManager();
        Assert.IsNull(manager.Station("lmd"), "nothing before the first view");

        manager.Accept(ViewAt(DateTimeOffset.UtcNow, Anchor));

        Assert.AreEqual("lmd", manager.Station("lmd").StationId);
        Assert.IsNull(manager.Station("exp"));
        Assert.IsNull(manager.Station("trading_post"));
    }

    [Test]
    public void OnlyRefusalsThatMeanTheViewWasBehind_ReadManufactureAgain()
    {
        foreach (string behind in new[]
                 {
                     ErrorCodes.StateConflict, ErrorCodes.NothingToClaim, ErrorCodes.LevelCapped,
                     ErrorCodes.ManufactureLocked, ErrorCodes.BlueprintRequired,
                 })
        {
            Assert.IsTrue(ManufactureManager.IsBehind(Problem(behind)), behind);
        }

        // Short of LMD, switched off, or never reached the server: a fresh view would show nothing new.
        foreach (string not in new[]
                 {
                     ErrorCodes.InsufficientFunds, ErrorCodes.FeatureDisabled, ErrorCodes.Maintenance,
                     Promuse.Net.TransportErrorCodes.Offline, Promuse.Net.TransportErrorCodes.Timeout,
                 })
        {
            Assert.IsFalse(ManufactureManager.IsBehind(Problem(not)), not);
        }

        Assert.IsFalse(ManufactureManager.IsBehind(null));
    }

    private static ApiProblem Problem(string code) => new ApiProblem("about:blank", "Refused", 409, code);
}
