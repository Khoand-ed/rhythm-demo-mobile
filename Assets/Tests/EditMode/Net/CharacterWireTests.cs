using NUnit.Framework;
using Promuse.Contracts.Characters;
using Promuse.Contracts.Runs;
using Promuse.Net;

// 升级的线上格式 / A level-up's answer and a run ticket's level in the shape the server writes them,
// read through the client's own parser - and the shared levelling rules the level-up screen
// previews with, run here on Unity's runtime as the server's tests run them on .NET.
public class CharacterWireTests
{
    private const string Result =
        @"{""characterId"":""PULSE"",""fromLevel"":1,""toLevel"":12,""expApplied"":2200,""lmdSpent"":660,""player"":{""playerId"":""6f1c2b8e-3d4a-4f5b-9c6d-7e8f9a0b1c2d"",""displayName"":""Doctor42"",""tag"":7,""level"":1,""exp"":0,""expToNextLevel"":500,""stamina"":{""current"":82,""max"":82,""nextPointAt"":null,""fullAt"":null},""characters"":[{""characterId"":""PULSE"",""elite"":0,""level"":12,""exp"":60,""trust"":0}],""squad"":[null,null,null,null],""desktopCharacterId"":null,""signature"":null,""inventory"":[{""itemId"":2,""amount"":4340}],""stateVersion"":4,""serverTime"":""2026-10-18T08:00:00+00:00""}}";

    private const string Ticket =
        @"{""runId"":""0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d"",""stageId"":""stage_001"",""seed"":42,""staminaSpent"":6,""player"":{""playerId"":""6f1c2b8e-3d4a-4f5b-9c6d-7e8f9a0b1c2d"",""displayName"":""Doctor42"",""tag"":7,""level"":1,""exp"":0,""expToNextLevel"":500,""stamina"":{""current"":76,""max"":82,""nextPointAt"":null,""fullAt"":null},""characters"":[],""squad"":[null,null,null,null],""desktopCharacterId"":null,""signature"":null,""inventory"":[],""stateVersion"":5,""serverTime"":""2026-10-18T08:00:00+00:00""},""serverTime"":""2026-10-18T08:00:00+00:00"",""characterLevel"":12}";

    [Test]
    public void ALevelUp_ReadsWhatItCostAndTheSaveAfter()
    {
        LevelUpResult result = PromuseApi.TryParse<LevelUpResult>(Result);

        Assert.IsNotNull(result);
        Assert.AreEqual(("PULSE", 1, 12), (result.CharacterId, result.FromLevel, result.ToLevel));
        Assert.AreEqual((2200, 660), (result.ExpApplied, result.LmdSpent));
        Assert.AreEqual(12, result.Player.Characters[0].Level);
    }

    [Test]
    public void ATicket_CarriesTheLevelTheRunIsReviewedAt()
    {
        RunTicket ticket = PromuseApi.TryParse<RunTicket>(Ticket);

        Assert.IsNotNull(ticket);
        Assert.AreEqual(12, ticket.CharacterLevel);
    }

    private const string Promoted =
        @"{""characterId"":""PULSE"",""fromElite"":0,""toElite"":1,""level"":40,""maxLevel"":55,""spent"":[{""itemId"":2,""amount"":10000},{""itemId"":6,""amount"":8},{""itemId"":7,""amount"":3}],""player"":{""playerId"":""6f1c2b8e-3d4a-4f5b-9c6d-7e8f9a0b1c2d"",""displayName"":""Doctor42"",""tag"":7,""level"":1,""exp"":0,""expToNextLevel"":500,""stamina"":{""current"":82,""max"":82,""nextPointAt"":null,""fullAt"":null},""characters"":[{""characterId"":""PULSE"",""elite"":1,""level"":40,""exp"":0,""trust"":0}],""squad"":[null,null,null,null],""desktopCharacterId"":null,""signature"":null,""inventory"":[],""stateVersion"":6,""serverTime"":""2026-10-18T08:00:00+00:00""}}";

    [Test]
    public void APromotion_ReadsThePhaseTheCapAndWhatItCost()
    {
        PromoteResult result = PromuseApi.TryParse<PromoteResult>(Promoted);

        Assert.IsNotNull(result);
        Assert.AreEqual((0, 1, 40, 55), (result.FromElite, result.ToElite, result.Level, result.MaxLevel));
        Assert.AreEqual(3, result.Spent.Count);
        Assert.AreEqual(1, result.Player.Characters[0].Elite);
    }

    [Test]
    public void ThePromotionTable_IsTheOneTheServerCharges()
    {
        // The same rows OperatorLevelRulesTests checks on .NET.
        Assert.IsNull(OperatorLevels.PromotionCost(3, 2), "a 3-star stops at Elite 1");
        Assert.AreEqual(10000, OperatorLevels.PromotionCost(3, 1)[0].Amount);
        Assert.AreEqual(180000, OperatorLevels.PromotionCost(6, 2)[0].Amount);
        Assert.AreEqual(55, OperatorLevels.MaxLevel(3, 1));
    }

    [Test]
    public void TheSharedLevellingRules_AgreeWithTheServersTests()
    {
        // The same numbers OperatorLevelRulesTests asserts on .NET.
        Assert.AreEqual(new LevelGain(3, 40, 300, 0), OperatorLevels.Gain(0, 1, 0, 40, 300));
        Assert.AreEqual(new LevelGain(40, 0, 880, 1120), OperatorLevels.Gain(0, 39, 0, 40, 2000));
        Assert.AreEqual(60, OperatorLevels.LmdFor(0, 200));
        Assert.AreEqual(40, OperatorLevels.MaxLevel(3, 0));
    }
}
