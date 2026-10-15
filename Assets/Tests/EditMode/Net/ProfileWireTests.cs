using NUnit.Framework;
using Promuse.Contracts.Players;
using Promuse.Net;

// 名片的线上格式 / A profile card and the save's new fields in the shape the server writes them
// (camelCase, nulls written out), read through the client's own parser.
public class ProfileWireTests
{
    private const string Card =
        @"{""playerId"":""6f1c2b8e-3d4a-4f5b-9c6d-7e8f9a0b1c2d"",""displayName"":""Doctor42"",""tag"":7,""level"":12,""signature"":""Chơi nhạc mỗi ngày"",""assistantId"":""NOVA"",""registeredAt"":""2026-05-26T08:00:00+00:00"",""campaignProgress"":""2-1"",""operators"":3,""rhythm"":{""plays"":40,""songsCleared"":5,""fullCombos"":2,""bestScore"":981250},""isSelf"":false}";

    private const string NewCard =
        @"{""playerId"":""6f1c2b8e-3d4a-4f5b-9c6d-7e8f9a0b1c2d"",""displayName"":""Doctor42"",""tag"":0,""level"":1,""signature"":null,""assistantId"":""PULSE"",""registeredAt"":""2026-10-15T08:00:00+00:00"",""campaignProgress"":null,""operators"":1,""rhythm"":{""plays"":0,""songsCleared"":0,""fullCombos"":0,""bestScore"":null},""isSelf"":true}";

    private const string State =
        @"{""playerId"":""6f1c2b8e-3d4a-4f5b-9c6d-7e8f9a0b1c2d"",""displayName"":""Doctor42"",""tag"":420,""level"":1,""exp"":0,""expToNextLevel"":500,""stamina"":{""current"":82,""max"":82,""nextPointAt"":null,""fullAt"":null},""characters"":[],""squad"":[null,null,null,null],""desktopCharacterId"":null,""signature"":""hello"",""inventory"":[],""stateVersion"":3,""serverTime"":""2026-10-15T08:00:00+00:00""}";

    [Test]
    public void ACard_ReadsEverythingItShows()
    {
        PlayerProfile card = PromuseApi.TryParse<PlayerProfile>(Card);

        Assert.IsNotNull(card);
        Assert.AreEqual(("Doctor42", 7, 12), (card.DisplayName, card.Tag, card.Level));
        Assert.AreEqual("Chơi nhạc mỗi ngày", card.Signature);
        Assert.AreEqual("NOVA", card.AssistantId);
        Assert.AreEqual(2026, card.RegisteredAt.Year);
        Assert.AreEqual("2-1", card.CampaignProgress);
        Assert.AreEqual(3, card.Operators);
        Assert.AreEqual(new RhythmRecord(40, 5, 2, 981250), card.Rhythm);
        Assert.IsFalse(card.IsSelf);
    }

    [Test]
    public void ANewPlayersCard_ReadsItsNullsAsNothingYet()
    {
        PlayerProfile card = PromuseApi.TryParse<PlayerProfile>(NewCard);

        Assert.IsNotNull(card);
        Assert.IsNull(card.Signature);
        Assert.IsNull(card.CampaignProgress);
        Assert.IsNull(card.Rhythm.BestScore);
        Assert.IsTrue(card.IsSelf);
    }

    [Test]
    public void TheSave_CarriesTheTagAndSignature()
    {
        PlayerState state = PromuseApi.TryParse<PlayerState>(State);

        Assert.IsNotNull(state);
        Assert.AreEqual(420, state.Tag);
        Assert.AreEqual("hello", state.Signature);
    }
}
