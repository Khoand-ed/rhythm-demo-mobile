using NUnit.Framework;
using Promuse.Contracts.Runs;
using Promuse.Net;

// 活动关卡的奖励 / What an event stage's clear paid, in the shape the server writes it (camelCase,
// field after field as RunRewards declares them), read through the client's own parser - and a body
// from a server older than the field, which must read as null rather than fail.
public class RunRewardsWireTests
{
    // The rewards of a clear of an event stage: no campaign Orundum or drops, the EXP, and the
    // event's currency.
    private const string EventClear =
        @"{""firstClearOrundum"":0,""drops"":[],""exp"":72,""levelBefore"":3,""levelAfter"":3,""primeFromLevels"":0,""firstClearItems"":[],""eventItems"":[{""itemId"":777,""amount"":25}]}";

    private const string BeforeEventItems =
        @"{""firstClearOrundum"":50,""drops"":[{""itemId"":2,""amount"":350}],""exp"":72,""levelBefore"":9,""levelAfter"":10,""primeFromLevels"":2,""firstClearItems"":[]}";

    [Test]
    public void AnEventStagesCurrency_Reads()
    {
        RunRewards rewards = PromuseApi.TryParse<RunRewards>(EventClear);

        Assert.IsNotNull(rewards);
        Assert.IsNotNull(rewards.EventItems);
        Assert.AreEqual(1, rewards.EventItems.Count);
        Assert.AreEqual((777, 25), (rewards.EventItems[0].ItemId, rewards.EventItems[0].Amount));
        Assert.AreEqual((0, 72), (rewards.FirstClearOrundum, rewards.Exp), "the fields before it still read");
        Assert.AreEqual(0, rewards.FirstClearItems.Count);
    }

    [Test]
    public void AnyOtherRun_ReadsAnEmptyList()
    {
        // 新服务端的普通一局 / A song-list run or a campaign stage, from a server that has the field.
        RunRewards rewards = PromuseApi.TryParse<RunRewards>(
            EventClear.Replace(@"""eventItems"":[{""itemId"":777,""amount"":25}]", @"""eventItems"":[]"));

        Assert.IsNotNull(rewards);
        Assert.IsNotNull(rewards.EventItems, "sent empty, it is empty - not missing");
        Assert.AreEqual(0, rewards.EventItems.Count);
    }

    [Test]
    public void AServerOlderThanTheField_ReadsNull()
    {
        RunRewards rewards = PromuseApi.TryParse<RunRewards>(BeforeEventItems);

        Assert.IsNotNull(rewards, "a body without the field still parses");
        Assert.IsNull(rewards.EventItems, "a server older than the field sends none");
        Assert.AreEqual((50, 350), (rewards.FirstClearOrundum, rewards.Drops[0].Amount));
    }
}
