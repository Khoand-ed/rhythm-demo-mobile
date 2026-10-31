using System;
using NUnit.Framework;
using Promuse.Contracts.Manufacture;
using Promuse.Manufacture;

// 屏幕上的字 / How Manufacture's numbers read: the countdowns, a level's pace and store, the bar.
public class ManufactureFormatTests
{
    private static readonly StationLevel Lmd = new StationLevel(1, 2, 50, 1800, 16);
    private static readonly StationLevel Records = new StationLevel(3, 14, 1, 9000, 5);

    [Test]
    public void Countdown_ShowsHoursOnlyWhenThereAreAny()
    {
        Assert.AreEqual("1:12:04", ManufactureFormat.Countdown(new TimeSpan(1, 12, 4)));
        Assert.AreEqual("12:31", ManufactureFormat.Countdown(new TimeSpan(0, 12, 31)));
        Assert.AreEqual("0:05", ManufactureFormat.Countdown(TimeSpan.FromSeconds(5)));
        Assert.AreEqual("10:00:00", ManufactureFormat.Countdown(TimeSpan.FromHours(10)));
    }

    [Test]
    public void Countdown_RoundsUpAndNeverGoesNegative()
    {
        // 0.2 s left is still "0:01": the unit has not landed yet.
        Assert.AreEqual("0:01", ManufactureFormat.Countdown(TimeSpan.FromMilliseconds(200)));
        Assert.AreEqual("0:00", ManufactureFormat.Countdown(TimeSpan.FromSeconds(-3)));
    }

    [Test]
    public void ALevelReadsAsItsPaceAndItsStore()
    {
        Assert.AreEqual("+50 every 30:00", ManufactureFormat.Pace(Lmd));
        Assert.AreEqual("Holds 800 (8:00:00)", ManufactureFormat.Holds(Lmd));
        Assert.AreEqual("+1 every 2:30:00", ManufactureFormat.Pace(Records));
        Assert.AreEqual("Holds 5 (12:30:00)", ManufactureFormat.Holds(Records));

        // A Local server running an hour a minute sends its own pace, and it reads as such.
        Assert.AreEqual("+50 every 0:30", ManufactureFormat.Pace(new StationLevel(1, 2, 50, 30, 16)));
    }

    [Test]
    public void TheStoreReadsInItems()
    {
        Assert.AreEqual("450 / 800", ManufactureFormat.Stored(Lmd, 9));
        Assert.AreEqual("0 / 800", ManufactureFormat.Stored(Lmd, 0));
        Assert.AreEqual("12,340", ManufactureFormat.Amount(12340));
        Assert.AreEqual(1800L, ManufactureFormat.Items(new StationLevel(3, 2, 50, 1200, 36), 36));
    }

    [Test]
    public void TheBarShowsTheUnitBeingMade_AndAFullStoreReadsFull()
    {
        DateTimeOffset anchor = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

        // Ten minutes into a 30-minute unit: a third of the way.
        DateTimeOffset now = anchor.AddMinutes(70);
        StationReading reading = ManufactureClock.Read(anchor, Lmd.SecondsPerUnit, Lmd.CapacityUnits, now);
        Assert.AreEqual(1f / 3f, ManufactureFormat.Progress(Lmd, reading, now), 0.001f);

        DateTimeOffset later = anchor.AddHours(9);
        Assert.AreEqual(1f, ManufactureFormat.Progress(Lmd, ManufactureClock.Read(anchor, 1800, 16, later), later));
    }

    [Test]
    public void EachStationHasItsTitle()
    {
        Assert.AreEqual("LMD STATION", StationCard.TitleOf(ManufactureStations.Lmd));
        Assert.AreEqual("EXP STATION", StationCard.TitleOf(ManufactureStations.Exp));
    }
}
