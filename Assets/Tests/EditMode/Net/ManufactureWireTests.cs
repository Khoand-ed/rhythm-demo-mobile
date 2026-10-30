using System;
using NUnit.Framework;
using Promuse.Contracts.Manufacture;
using Promuse.Net;

// 制造站的线上格式 / Manufacture's answers in the shape the server writes them - camelCase, offsets
// on every moment, null where a station is full or at its top level - read through the client's own
// parser; and the production clock both sides share, run here on Unity's runtime as the server's
// tests run it on .NET.
public class ManufactureWireTests
{
    private const string Player =
        @"{""playerId"":""6f1c2b8e-3d4a-4f5b-9c6d-7e8f9a0b1c2d"",""displayName"":""Doctor42"",""tag"":7,""level"":3,""exp"":0,""expToNextLevel"":700,""stamina"":{""current"":82,""max"":82,""nextPointAt"":null,""fullAt"":null},""characters"":[],""squad"":[null,null,null,null],""desktopCharacterId"":null,""signature"":null,""inventory"":[{""itemId"":2,""amount"":1100},{""itemId"":30,""amount"":1}],""stateVersion"":9,""serverTime"":""2026-11-01T09:10:00+00:00""}";

    // The LMD station 70 minutes in (two units, the next at 09:30); the record station full and at its top.
    private const string Stations =
        @"[{""stationId"":""lmd"",""level"":1,""maxLevel"":3,""current"":{""level"":1,""itemId"":2,""unitAmount"":50,""secondsPerUnit"":1800,""capacityUnits"":16},""storedUnits"":2,""anchorAt"":""2026-11-01T08:00:00+00:00"",""nextUnitAt"":""2026-11-01T09:30:00+00:00"",""fullAt"":""2026-11-01T16:00:00+00:00"",""next"":{""to"":{""level"":2,""itemId"":2,""unitAmount"":50,""secondsPerUnit"":1440,""capacityUnits"":25},""cost"":[{""itemId"":2,""amount"":8000}],""blueprintItemId"":31,""blueprintStageCode"":""1-3""}}," +
        @"{""stationId"":""exp"",""level"":3,""maxLevel"":3,""current"":{""level"":3,""itemId"":14,""unitAmount"":1,""secondsPerUnit"":9000,""capacityUnits"":5},""storedUnits"":5,""anchorAt"":""2026-10-31T09:00:00+00:00"",""nextUnitAt"":null,""fullAt"":null,""next"":null}]";

    private const string OpenView =
        @"{""isUnlocked"":true,""unlockStageCode"":null,""stations"":" + Stations + @",""serverTime"":""2026-11-01T09:10:00+00:00"",""player"":null}";

    private const string LockedView =
        @"{""isUnlocked"":false,""unlockStageCode"":""1-1"",""stations"":[],""serverTime"":""2026-11-01T09:10:00+00:00"",""player"":null}";

    [Test]
    public void AnOpenView_ReadsItsStationsAndTheirNextLevel()
    {
        ManufactureView view = PromuseApi.TryParse<ManufactureView>(OpenView);

        Assert.IsNotNull(view);
        Assert.IsTrue(view.IsUnlocked);
        Assert.IsNull(view.UnlockStageCode);
        Assert.IsNull(view.Player, "a read that handed nothing out carries no save");
        Assert.AreEqual(new DateTimeOffset(2026, 11, 1, 9, 10, 0, TimeSpan.Zero), view.ServerTime);

        StationState lmd = view.Stations[0];
        Assert.AreEqual(("lmd", 1, 3, 2), (lmd.StationId, lmd.Level, lmd.MaxLevel, lmd.StoredUnits));
        Assert.AreEqual(new StationLevel(1, 2, 50, 1800, 16), lmd.Current);
        Assert.AreEqual(new DateTimeOffset(2026, 11, 1, 9, 30, 0, TimeSpan.Zero), lmd.NextUnitAt);
        Assert.AreEqual(new DateTimeOffset(2026, 11, 1, 16, 0, 0, TimeSpan.Zero), lmd.FullAt);
        Assert.AreEqual(new StationLevel(2, 2, 50, 1440, 25), lmd.Next.To);
        Assert.AreEqual((2, 8000), (lmd.Next.Cost[0].ItemId, lmd.Next.Cost[0].Amount));
        Assert.AreEqual((31, "1-3"), (lmd.Next.BlueprintItemId, lmd.Next.BlueprintStageCode));
    }

    [Test]
    public void AFullStationAtItsTop_HasNoDeadlinesAndNoNextLevel()
    {
        StationState exp = PromuseApi.TryParse<ManufactureView>(OpenView).Stations[1];

        Assert.AreEqual(("exp", 3, 5), (exp.StationId, exp.Level, exp.StoredUnits));
        Assert.IsNull(exp.NextUnitAt);
        Assert.IsNull(exp.FullAt);
        Assert.IsNull(exp.Next);
    }

    [Test]
    public void ALockedView_NamesTheStageThatOpensIt()
    {
        ManufactureView view = PromuseApi.TryParse<ManufactureView>(LockedView);

        Assert.IsNotNull(view);
        Assert.IsFalse(view.IsUnlocked);
        Assert.AreEqual("1-1", view.UnlockStageCode);
        Assert.AreEqual(0, view.Stations.Count);
    }

    [Test]
    public void AViewThatHandedOutABlueprint_CarriesTheSave()
    {
        ManufactureView view = PromuseApi.TryParse<ManufactureView>(
            OpenView.Replace(@"""player"":null}", @"""player"":" + Player + "}"));

        Assert.IsNotNull(view.Player);
        Assert.AreEqual(9, view.Player.StateVersion);
        Assert.AreEqual((30, 1), (view.Player.Inventory[1].ItemId, view.Player.Inventory[1].Amount));
    }

    [Test]
    public void AnActionResult_ReadsWhatWasGrantedTheViewAndTheSave()
    {
        ManufactureActionResult result = PromuseApi.TryParse<ManufactureActionResult>(
            @"{""view"":" + OpenView + @",""granted"":[{""itemId"":2,""amount"":100}],""player"":" + Player + "}");

        Assert.IsNotNull(result);
        Assert.AreEqual((2, 100), (result.Granted[0].ItemId, result.Granted[0].Amount));
        Assert.AreEqual(2, result.View.Stations.Count);
        Assert.AreEqual(1100, result.Player.Inventory[0].Amount);
    }

    [Test]
    public void TheSharedClock_CountsTheSameOnUnitysRuntime()
    {
        StationState lmd = PromuseApi.TryParse<ManufactureView>(OpenView).Stations[0];
        StationLevel at = lmd.Current;

        // What the server reported, read back from the anchor and the rate it sent.
        StationReading now = ManufactureClock.Read(lmd.AnchorAt, at.SecondsPerUnit, at.CapacityUnits,
            new DateTimeOffset(2026, 11, 1, 9, 10, 0, TimeSpan.Zero));
        Assert.AreEqual(lmd.StoredUnits, now.StoredUnits);
        Assert.AreEqual(lmd.NextUnitAt, now.NextUnitAt);
        Assert.AreEqual(lmd.FullAt, now.FullAt);

        // Eight hours in it is full and has stopped.
        StationReading full = ManufactureClock.Read(lmd.AnchorAt, at.SecondsPerUnit, at.CapacityUnits, lmd.AnchorAt.AddHours(9));
        Assert.AreEqual((16, (DateTimeOffset?)null), (full.StoredUnits, full.NextUnitAt));

        // Collected at 70 minutes, the remainder is kept: the anchor moves exactly two units on.
        Assert.AreEqual(lmd.AnchorAt.AddMinutes(60),
            ManufactureClock.AnchorAfterCollect(lmd.AnchorAt, at.SecondsPerUnit, at.CapacityUnits, lmd.AnchorAt.AddMinutes(70)));
    }
}
