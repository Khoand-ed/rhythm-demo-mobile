using NUnit.Framework;
using Promuse.Contracts.Campaign;
using Promuse.Contracts.Runs;
using Promuse.Net;

// 主线和奖励的线上格式 / The campaign view and a run's rewards in the shape the server writes them,
// read through the client's own parser - and a completion from a server older than rewards.
public class CampaignWireTests
{
    private const string View =
        @"{""chapters"":[{""chapterId"":""chapter_1"",""title"":""Chapter 1: First Light"",""isUnlocked"":true,""stages"":[{""stage"":{""stageId"":""main_1_1"",""code"":""1-1"",""chartId"":""stage_001"",""firstClearOrundum"":50,""drops"":[{""itemId"":2,""min"":300,""max"":500,""chance"":10000,""rarity"":""Common""},{""itemId"":13,""min"":1,""max"":1,""chance"":1000,""rarity"":""Rare""}],""storyBefore"":""campaign_1_1_before"",""storyAfter"":null},""isUnlocked"":true,""isCleared"":false,""staminaCost"":6}]}],""serverTime"":""2026-10-12T01:00:00+00:00""}";

    private const string Rewards =
        @"{""firstClearOrundum"":50,""drops"":[{""itemId"":2,""amount"":350}],""exp"":72,""levelBefore"":9,""levelAfter"":10,""primeFromLevels"":2}";

    [Test]
    public void TheCampaignView_ReadsItsStagesAndDrops()
    {
        CampaignView view = PromuseApi.TryParse<CampaignView>(View);

        Assert.IsNotNull(view);
        CampaignStageState first = view.Chapters[0].Stages[0];
        Assert.AreEqual(("1-1", "stage_001", 50, 6), (first.Stage.Code, first.Stage.ChartId, first.Stage.FirstClearOrundum, first.StaminaCost));
        Assert.AreEqual(DropRarity.Rare, first.Stage.Drops[1].Rarity, "rarity arrives by name");
        Assert.AreEqual("campaign_1_1_before", first.Stage.StoryBefore);
        Assert.IsNull(first.Stage.StoryAfter);
    }

    [Test]
    public void ARunsRewards_Read()
    {
        RunRewards rewards = PromuseApi.TryParse<RunRewards>(Rewards);

        Assert.IsNotNull(rewards);
        Assert.AreEqual((50, 72, 9, 10, 2), (rewards.FirstClearOrundum, rewards.Exp, rewards.LevelBefore, rewards.LevelAfter, rewards.PrimeFromLevels));
        Assert.AreEqual(350, rewards.Drops[0].Amount);
        Assert.IsNull(rewards.FirstClearItems, "a server older than the field sends none");
    }

    [Test]
    public void AFirstClearsOtherRewards_Read()
    {
        RunRewards rewards = PromuseApi.TryParse<RunRewards>(
            Rewards.Replace(@"""primeFromLevels"":2}", @"""primeFromLevels"":2,""firstClearItems"":[{""itemId"":30,""amount"":1}]}"));

        Assert.IsNotNull(rewards);
        Assert.AreEqual(1, rewards.FirstClearItems.Count);
        Assert.AreEqual((30, 1), (rewards.FirstClearItems[0].ItemId, rewards.FirstClearItems[0].Amount));
    }
}
