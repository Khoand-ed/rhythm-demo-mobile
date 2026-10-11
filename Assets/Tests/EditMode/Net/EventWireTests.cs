using NUnit.Framework;
using Promuse.Contracts.Events;
using Promuse.Net;

// 活动的线上格式 / The event view exactly as the server sent it - captured from a running API, with
// one day checked in - read through the client's own parser. Enums arrive as names, every module
// is a nested list, and a missing limit is null: all three are things a contract change could
// silently break on the Unity side while the server tests stay green.
public class EventWireTests
{
    private const string Captured = @"{""eventId"":""resonant_dawn"",""definition"":{""title"":""Resonant Dawn"",""subtitle"":""Festival event"",""description"":""AMIYA's headhunting banner is open, and Rhodes Island is celebrating. Check in every day, clear the festival charts for Event Tokens, and spend them in the festival shop."",""startsAt"":""2026-10-01T00:00:00+00:00"",""endsAt"":""2026-12-31T23:59:59+00:00"",""isActive"":true,""currencyItemId"":20,""bannerId"":""event_amiya"",""checkIn"":[{""day"":1,""reward"":{""itemId"":1,""amount"":100}},{""day"":2,""reward"":{""itemId"":2,""amount"":2000}},{""day"":3,""reward"":{""itemId"":20,""amount"":50}},{""day"":4,""reward"":{""itemId"":6,""amount"":20}},{""day"":5,""reward"":{""itemId"":1,""amount"":200}},{""day"":6,""reward"":{""itemId"":20,""amount"":100}},{""day"":7,""reward"":{""itemId"":10,""amount"":1}}],""missions"":[{""missionId"":""clear3"",""description"":""Clear any song 3 times"",""goal"":""PlaySong"",""target"":3,""points"":10,""stageId"":null,""minScore"":null},{""missionId"":""hard1"",""description"":""Clear the festival chart AIW (Hard) once"",""goal"":""ClearStage"",""target"":1,""points"":20,""stageId"":""stage_AIW_hard"",""minScore"":null},{""missionId"":""score100k"",""description"":""Score 100,000 or more on any song"",""goal"":""ReachScore"",""target"":1,""points"":20,""stageId"":null,""minScore"":100000},{""missionId"":""pull10"",""description"":""Headhunt 10 times"",""goal"":""Headhunt"",""target"":10,""points"":10,""stageId"":null,""minScore"":null},{""missionId"":""shop1"",""description"":""Buy anything from the Store once"",""goal"":""BuyShopItem"",""target"":1,""points"":5,""stageId"":null,""minScore"":null}],""milestones"":[{""rewardId"":""m20"",""requiredPoints"":20,""reward"":{""itemId"":20,""amount"":100}},{""rewardId"":""m40"",""requiredPoints"":40,""reward"":{""itemId"":1,""amount"":300}},{""rewardId"":""m65"",""requiredPoints"":65,""reward"":{""itemId"":10,""amount"":2}}],""shop"":[{""offerId"":""permit"",""sell"":{""itemId"":10,""amount"":1},""price"":150,""limit"":3},{""offerId"":""orundum"",""sell"":{""itemId"":1,""amount"":100},""price"":40,""limit"":10},{""offerId"":""certificates"",""sell"":{""itemId"":8,""amount"":10},""price"":30,""limit"":5},{""offerId"":""lmd"",""sell"":{""itemId"":2,""amount"":5000},""price"":20,""limit"":null}],""stages"":[{""stageId"":""stage_AIW"",""currencyPerClear"":30},{""stageId"":""stage_AIW_hard"",""currencyPerClear"":60}]},""isOpen"":true,""currencyBalance"":0,""checkIn"":{""claimed"":1,""canClaimToday"":false,""nextDayAt"":""2026-10-11T21:00:00+00:00""},""points"":0,""missions"":[{""mission"":{""missionId"":""clear3"",""description"":""Clear any song 3 times"",""goal"":""PlaySong"",""target"":3,""points"":10,""stageId"":null,""minScore"":null},""progress"":0,""isComplete"":false,""isClaimed"":false},{""mission"":{""missionId"":""hard1"",""description"":""Clear the festival chart AIW (Hard) once"",""goal"":""ClearStage"",""target"":1,""points"":20,""stageId"":""stage_AIW_hard"",""minScore"":null},""progress"":0,""isComplete"":false,""isClaimed"":false},{""mission"":{""missionId"":""score100k"",""description"":""Score 100,000 or more on any song"",""goal"":""ReachScore"",""target"":1,""points"":20,""stageId"":null,""minScore"":100000},""progress"":0,""isComplete"":false,""isClaimed"":false},{""mission"":{""missionId"":""pull10"",""description"":""Headhunt 10 times"",""goal"":""Headhunt"",""target"":10,""points"":10,""stageId"":null,""minScore"":null},""progress"":0,""isComplete"":false,""isClaimed"":false},{""mission"":{""missionId"":""shop1"",""description"":""Buy anything from the Store once"",""goal"":""BuyShopItem"",""target"":1,""points"":5,""stageId"":null,""minScore"":null},""progress"":0,""isComplete"":false,""isClaimed"":false}],""milestones"":[{""milestone"":{""rewardId"":""m20"",""requiredPoints"":20,""reward"":{""itemId"":20,""amount"":100}},""isClaimed"":false,""isClaimable"":false},{""milestone"":{""rewardId"":""m40"",""requiredPoints"":40,""reward"":{""itemId"":1,""amount"":300}},""isClaimed"":false,""isClaimable"":false},{""milestone"":{""rewardId"":""m65"",""requiredPoints"":65,""reward"":{""itemId"":10,""amount"":2}},""isClaimed"":false,""isClaimable"":false}],""shop"":[{""offer"":{""offerId"":""permit"",""sell"":{""itemId"":10,""amount"":1},""price"":150,""limit"":3},""bought"":0,""remaining"":3},{""offer"":{""offerId"":""orundum"",""sell"":{""itemId"":1,""amount"":100},""price"":40,""limit"":10},""bought"":0,""remaining"":10},{""offer"":{""offerId"":""certificates"",""sell"":{""itemId"":8,""amount"":10},""price"":30,""limit"":5},""bought"":0,""remaining"":5},{""offer"":{""offerId"":""lmd"",""sell"":{""itemId"":2,""amount"":5000},""price"":20,""limit"":null},""bought"":0,""remaining"":null}],""serverTime"":""2026-10-11T04:22:55.4794893+00:00""}";

    // 关卡状态 / The same reply as the server writes it since event stages unlock in order and carry
    // their stories: each stage names a title and two story scripts, and the view says per stage
    // whether it is open and cleared.
    private const string CapturedWithStages = @"{""eventId"":""resonant_dawn"",""definition"":{""title"":""Resonant Dawn"",""subtitle"":""Festival event"",""description"":""AMIYA's headhunting banner is open, and Rhodes Island is celebrating. Check in every day, clear the festival charts for Event Tokens, and spend them in the festival shop."",""startsAt"":""2026-10-01T00:00:00+00:00"",""endsAt"":""2026-12-31T23:59:59+00:00"",""isActive"":true,""currencyItemId"":20,""bannerId"":""event_amiya"",""checkIn"":[{""day"":1,""reward"":{""itemId"":1,""amount"":100}},{""day"":2,""reward"":{""itemId"":2,""amount"":2000}},{""day"":3,""reward"":{""itemId"":20,""amount"":50}},{""day"":4,""reward"":{""itemId"":6,""amount"":20}},{""day"":5,""reward"":{""itemId"":1,""amount"":200}},{""day"":6,""reward"":{""itemId"":20,""amount"":100}},{""day"":7,""reward"":{""itemId"":10,""amount"":1}}],""missions"":[{""missionId"":""clear3"",""description"":""Clear any song 3 times"",""goal"":""PlaySong"",""target"":3,""points"":10,""stageId"":null,""minScore"":null},{""missionId"":""hard1"",""description"":""Clear the festival chart AIW (Hard) once"",""goal"":""ClearStage"",""target"":1,""points"":20,""stageId"":""stage_AIW_hard"",""minScore"":null},{""missionId"":""score100k"",""description"":""Score 100,000 or more on any song"",""goal"":""ReachScore"",""target"":1,""points"":20,""stageId"":null,""minScore"":100000},{""missionId"":""pull10"",""description"":""Headhunt 10 times"",""goal"":""Headhunt"",""target"":10,""points"":10,""stageId"":null,""minScore"":null},{""missionId"":""shop1"",""description"":""Buy anything from the Store once"",""goal"":""BuyShopItem"",""target"":1,""points"":5,""stageId"":null,""minScore"":null}],""milestones"":[{""rewardId"":""m20"",""requiredPoints"":20,""reward"":{""itemId"":20,""amount"":100}},{""rewardId"":""m40"",""requiredPoints"":40,""reward"":{""itemId"":1,""amount"":300}},{""rewardId"":""m65"",""requiredPoints"":65,""reward"":{""itemId"":10,""amount"":2}}],""shop"":[{""offerId"":""permit"",""sell"":{""itemId"":10,""amount"":1},""price"":150,""limit"":3},{""offerId"":""orundum"",""sell"":{""itemId"":1,""amount"":100},""price"":40,""limit"":10},{""offerId"":""certificates"",""sell"":{""itemId"":8,""amount"":10},""price"":30,""limit"":5},{""offerId"":""lmd"",""sell"":{""itemId"":2,""amount"":5000},""price"":20,""limit"":null}],""stages"":[{""stageId"":""stage_AIW"",""currencyPerClear"":30,""title"":""RD-1"",""storyBefore"":""resonant_dawn_1_before"",""storyAfter"":""resonant_dawn_1_after""},{""stageId"":""stage_AIW_hard"",""currencyPerClear"":60,""title"":""RD-2"",""storyBefore"":""resonant_dawn_2_before"",""storyAfter"":""resonant_dawn_2_after""}]},""isOpen"":true,""currencyBalance"":0,""checkIn"":{""claimed"":1,""canClaimToday"":false,""nextDayAt"":""2026-10-11T21:00:00+00:00""},""points"":0,""missions"":[{""mission"":{""missionId"":""clear3"",""description"":""Clear any song 3 times"",""goal"":""PlaySong"",""target"":3,""points"":10,""stageId"":null,""minScore"":null},""progress"":0,""isComplete"":false,""isClaimed"":false},{""mission"":{""missionId"":""hard1"",""description"":""Clear the festival chart AIW (Hard) once"",""goal"":""ClearStage"",""target"":1,""points"":20,""stageId"":""stage_AIW_hard"",""minScore"":null},""progress"":0,""isComplete"":false,""isClaimed"":false},{""mission"":{""missionId"":""score100k"",""description"":""Score 100,000 or more on any song"",""goal"":""ReachScore"",""target"":1,""points"":20,""stageId"":null,""minScore"":100000},""progress"":0,""isComplete"":false,""isClaimed"":false},{""mission"":{""missionId"":""pull10"",""description"":""Headhunt 10 times"",""goal"":""Headhunt"",""target"":10,""points"":10,""stageId"":null,""minScore"":null},""progress"":0,""isComplete"":false,""isClaimed"":false},{""mission"":{""missionId"":""shop1"",""description"":""Buy anything from the Store once"",""goal"":""BuyShopItem"",""target"":1,""points"":5,""stageId"":null,""minScore"":null},""progress"":0,""isComplete"":false,""isClaimed"":false}],""milestones"":[{""milestone"":{""rewardId"":""m20"",""requiredPoints"":20,""reward"":{""itemId"":20,""amount"":100}},""isClaimed"":false,""isClaimable"":false},{""milestone"":{""rewardId"":""m40"",""requiredPoints"":40,""reward"":{""itemId"":1,""amount"":300}},""isClaimed"":false,""isClaimable"":false},{""milestone"":{""rewardId"":""m65"",""requiredPoints"":65,""reward"":{""itemId"":10,""amount"":2}},""isClaimed"":false,""isClaimable"":false}],""shop"":[{""offer"":{""offerId"":""permit"",""sell"":{""itemId"":10,""amount"":1},""price"":150,""limit"":3},""bought"":0,""remaining"":3},{""offer"":{""offerId"":""orundum"",""sell"":{""itemId"":1,""amount"":100},""price"":40,""limit"":10},""bought"":0,""remaining"":10},{""offer"":{""offerId"":""certificates"",""sell"":{""itemId"":8,""amount"":10},""price"":30,""limit"":5},""bought"":0,""remaining"":5},{""offer"":{""offerId"":""lmd"",""sell"":{""itemId"":2,""amount"":5000},""price"":20,""limit"":null},""bought"":0,""remaining"":null}],""stages"":[{""stage"":{""stageId"":""stage_AIW"",""currencyPerClear"":30,""title"":""RD-1"",""storyBefore"":""resonant_dawn_1_before"",""storyAfter"":""resonant_dawn_1_after""},""isUnlocked"":true,""isCleared"":true},{""stage"":{""stageId"":""stage_AIW_hard"",""currencyPerClear"":60,""title"":""RD-2"",""storyBefore"":""resonant_dawn_2_before"",""storyAfter"":""resonant_dawn_2_after""},""isUnlocked"":true,""isCleared"":false}],""serverTime"":""2026-10-11T04:22:55.4794893+00:00""}";

    [Test]
    public void TheServersEventView_ReadsWhole()
    {
        EventView view = PromuseApi.TryParse<EventView>(Captured);

        Assert.IsNotNull(view);
        Assert.AreEqual("resonant_dawn", view.EventId);
        Assert.IsTrue(view.IsOpen);
        Assert.AreEqual(20, view.Definition.CurrencyItemId);
        Assert.AreEqual("event_amiya", view.Definition.BannerId);

        Assert.AreEqual(7, view.Definition.CheckIn.Count);
        Assert.AreEqual(1, view.CheckIn.Claimed);
        Assert.IsFalse(view.CheckIn.CanClaimToday);

        Assert.AreEqual(EventGoal.ClearStage, view.Missions[1].Mission.Goal, "enums arrive by name");
        Assert.AreEqual("stage_AIW_hard", view.Missions[1].Mission.StageId);
        Assert.AreEqual(100000, view.Missions[2].Mission.MinScore);

        Assert.AreEqual(3, view.Milestones.Count);
        Assert.AreEqual(3, view.Shop[0].Remaining);
        Assert.IsNull(view.Shop[3].Remaining, "an offer without a limit has no remaining count");
        Assert.AreEqual(60, view.Definition.Stages[1].CurrencyPerClear);
    }

    [Test]
    public void EventStages_ReadWithTheirStoriesAndState()
    {
        EventView view = PromuseApi.TryParse<EventView>(CapturedWithStages);

        Assert.IsNotNull(view);
        Assert.AreEqual("RD-1", view.Definition.Stages[0].Title);
        Assert.AreEqual("resonant_dawn_1_before", view.Definition.Stages[0].StoryBefore);
        Assert.AreEqual("resonant_dawn_2_after", view.Definition.Stages[1].StoryAfter);
        Assert.AreEqual(2, view.Stages.Count);
        Assert.IsTrue(view.Stages[0].IsCleared);
        Assert.IsTrue(view.Stages[1].IsUnlocked);
        Assert.IsFalse(view.Stages[1].IsCleared);
    }

    [Test]
    public void AnOlderServersEventView_HasNoStageState()
    {
        // 旧服务端 / Before stages carried state the view had no list and a stage no stories.
        EventView view = PromuseApi.TryParse<EventView>(Captured);

        Assert.IsNull(view.Stages);
        Assert.IsNull(view.Definition.Stages[0].StoryBefore);
    }
}
