using System.Text.Json;
using System.Text.RegularExpressions;
using Promuse.Contracts.Events;
using Promuse.Contracts.Stories;
using Promuse.Persistence;

namespace Promuse.Api.Events;

/// <summary>
/// What makes an event definition sound, and how one is read and written - nothing that touches
/// the database, so every rule can be pinned by a plain unit test.
/// </summary>
public static partial class EventRules
{
    public const int MaxTitle = 60;
    public const int MaxDescription = 500;
    public const int MaxDays = 31;
    public const int MaxAmount = 1_000_000;

    [GeneratedRegex("^[a-z0-9_]{1,64}$")]
    private static partial Regex IdPattern();

    public static bool IsValidId(string? id) => id is not null && IdPattern().IsMatch(id);

    private const string StoryIdRule =
        "A story script's file name: lower-case letters, digits and underscores - or leave it empty.";

    public static string Serialize(EventDefinition definition) =>
        JsonSerializer.Serialize(Normalize(definition), SeedEvents.Json);

    public static EventDefinition Parse(string json) =>
        Normalize(JsonSerializer.Deserialize<EventDefinition>(json, SeedEvents.Json)
                  ?? throw new InvalidOperationException("An event holds an empty definition."));

    /// <summary>
    /// 空列表就是没有这个模块 / A module left out of the JSON arrives as null; it means "this event
    /// has no such module", so it is read as an empty list rather than tripping every caller.
    /// </summary>
    public static EventDefinition Normalize(EventDefinition d) => d with
    {
        CheckIn = d.CheckIn ?? [],
        Missions = d.Missions ?? [],
        Milestones = d.Milestones ?? [],
        Shop = d.Shop ?? [],
        Stages = d.Stages ?? [],
    };

    public static bool IsOpen(EventDefinition d, DateTimeOffset now) =>
        d.IsActive && d.StartsAt <= now && now < d.EndsAt;

    public const int MaxStageTitle = 24;

    /// <summary>
    /// 按顺序解锁 / The stage at <paramref name="index"/> is open when it is the first, or the stage
    /// listed before it has been cleared.
    /// </summary>
    public static bool IsUnlocked(EventDefinition d, int index, IReadOnlySet<string> cleared) =>
        index == 0 || (index > 0 && index < d.Stages.Count && cleared.Contains(d.Stages[index - 1].StageId));

    /// <summary>
    /// Field path to what is wrong with it; empty when the definition can be saved.
    /// <paramref name="stages"/> and <paramref name="banners"/> are what the server knows, so an
    /// event cannot point a mission at a chart that does not exist.
    /// </summary>
    public static Dictionary<string, string[]> Validate(
        EventDefinition? raw, IReadOnlySet<string> stages, IReadOnlySet<string> banners)
    {
        var errors = new Dictionary<string, string[]>();
        void Add(string field, string message) =>
            errors[field] = errors.TryGetValue(field, out string[]? had) ? [.. had, message] : [message];

        if (raw is null)
        {
            Add("definition", "Required.");
            return errors;
        }

        EventDefinition d = Normalize(raw);

        if (string.IsNullOrWhiteSpace(d.Title) || d.Title.Length > MaxTitle) Add("title", $"1-{MaxTitle} characters.");
        if (d.Subtitle is { Length: > MaxTitle }) Add("subtitle", $"At most {MaxTitle} characters.");
        if (d.Description is { Length: > MaxDescription }) Add("description", $"At most {MaxDescription} characters.");
        if (d.StartsAt >= d.EndsAt) Add("endsAt", "Must be after startsAt.");
        if (d.CurrencyItemId is < 0) Add("currencyItemId", "Must not be negative.");
        if (d.BannerId is not null && !banners.Contains(d.BannerId)) Add("bannerId", $"No banner '{d.BannerId}'.");

        // ---- check-in: day 1, 2, 3... with no gaps
        if (d.CheckIn.Count > MaxDays) Add("checkIn", $"At most {MaxDays} days.");
        for (int i = 0; i < d.CheckIn.Count; i++)
        {
            if (d.CheckIn[i].Day != i + 1) Add($"checkIn[{i}].day", $"Days run 1, 2, 3... - expected {i + 1}.");
            Reward($"checkIn[{i}].reward", d.CheckIn[i].Reward);
        }

        // ---- missions
        Unique("missions", d.Missions.Select(m => m.MissionId));
        for (int i = 0; i < d.Missions.Count; i++)
        {
            EventMission m = d.Missions[i];
            string at = $"missions[{i}]";

            if (!IsValidId(m.MissionId)) Add($"{at}.missionId", "Lower-case letters, digits and underscores.");
            if (string.IsNullOrWhiteSpace(m.Description) || m.Description.Length > 120) Add($"{at}.description", "1-120 characters.");
            if (m.Target is < 1 or > 10_000) Add($"{at}.target", "Between 1 and 10000.");
            if (m.Points is < 0 or > 1_000) Add($"{at}.points", "Between 0 and 1000.");

            switch (m.Goal)
            {
                case EventGoal.ClearStage when m.StageId is null:
                    Add($"{at}.stageId", "A ClearStage mission needs the chart it counts.");
                    break;
                case EventGoal.ReachScore when m.MinScore is null or < 1:
                    Add($"{at}.minScore", "A ReachScore mission needs a score of at least 1.");
                    break;
                case EventGoal.PlaySong or EventGoal.BuyShopItem or EventGoal.Headhunt when m.StageId is not null || m.MinScore is not null:
                    Add($"{at}", $"A {m.Goal} mission takes no stage or score.");
                    break;
            }

            if (m.StageId is not null && !stages.Contains(m.StageId)) Add($"{at}.stageId", $"No chart '{m.StageId}'.");
        }

        // ---- milestones: reachable with the points the missions pay
        int available = d.Missions.Sum(m => m.Points);
        Unique("milestones", d.Milestones.Select(r => r.RewardId));
        for (int i = 0; i < d.Milestones.Count; i++)
        {
            EventMilestone r = d.Milestones[i];
            string at = $"milestones[{i}]";

            if (!IsValidId(r.RewardId)) Add($"{at}.rewardId", "Lower-case letters, digits and underscores.");
            if (r.RequiredPoints < 1) Add($"{at}.requiredPoints", "At least 1.");
            else if (r.RequiredPoints > available) Add($"{at}.requiredPoints", $"The missions only pay {available} points in all.");
            Reward($"{at}.reward", r.Reward);
        }

        // ---- shop and stages both need a currency
        if ((d.Shop.Count > 0 || d.Stages.Count > 0) && d.CurrencyItemId is null)
        {
            Add("currencyItemId", "An event with a shop or stages needs a currency.");
        }

        Unique("shop", d.Shop.Select(o => o.OfferId));
        for (int i = 0; i < d.Shop.Count; i++)
        {
            EventShopOffer o = d.Shop[i];
            string at = $"shop[{i}]";

            if (!IsValidId(o.OfferId)) Add($"{at}.offerId", "Lower-case letters, digits and underscores.");
            if (o.Price is < 1 or > MaxAmount) Add($"{at}.price", $"Between 1 and {MaxAmount}.");
            if (o.Limit is < 1) Add($"{at}.limit", "At least 1, or leave it empty for no limit.");
            if (d.CurrencyItemId is { } currency && o.Sell?.ItemId == currency) Add($"{at}.sell", "Cannot sell the event's own currency for itself.");
            Reward($"{at}.sell", o.Sell);
        }

        Unique("stages", d.Stages.Select(s => s.StageId));
        for (int i = 0; i < d.Stages.Count; i++)
        {
            EventStage s = d.Stages[i];
            if (!stages.Contains(s.StageId)) Add($"stages[{i}].stageId", $"No chart '{s.StageId}'.");
            if (s.CurrencyPerClear is < 1 or > MaxAmount) Add($"stages[{i}].currencyPerClear", $"Between 1 and {MaxAmount}.");
            if (s.Title is not null && (string.IsNullOrWhiteSpace(s.Title) || s.Title.Length > MaxStageTitle))
            {
                Add($"stages[{i}].title", $"1-{MaxStageTitle} characters, or leave it empty for the chart's name.");
            }
            if (s.StoryBefore is not null && !StoryIds.IsStoryId(s.StoryBefore)) Add($"stages[{i}].storyBefore", StoryIdRule);
            if (s.StoryAfter is not null && !StoryIds.IsStoryId(s.StoryAfter)) Add($"stages[{i}].storyAfter", StoryIdRule);
        }

        return errors;

        void Reward(string at, EventReward? reward)
        {
            if (reward is null) { Add(at, "Required."); return; }
            if (reward.ItemId < 0) Add($"{at}.itemId", "Must not be negative.");
            if (reward.Amount is < 1 or > MaxAmount) Add($"{at}.amount", $"Between 1 and {MaxAmount}.");
        }

        void Unique(string at, IEnumerable<string> ids)
        {
            foreach (string id in ids.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key))
            {
                Add(at, $"'{id}' appears more than once.");
            }
        }
    }

    /// <summary>
    /// The missions a happening advances. Pure, so which missions a clear or a pull counts towards
    /// is pinned by tests rather than inferred from a database.
    /// </summary>
    public static IEnumerable<EventMission> Matching(
        EventDefinition d, EventGoal happened, string? stageId, int? score, bool accepted)
    {
        foreach (EventMission m in d.Missions)
        {
            bool counts = (m.Goal, happened) switch
            {
                (EventGoal.PlaySong, EventGoal.PlaySong) => true,
                (EventGoal.ClearStage, EventGoal.PlaySong) => m.StageId == stageId,
                (EventGoal.ReachScore, EventGoal.PlaySong) =>
                    accepted && score >= m.MinScore && (m.StageId is null || m.StageId == stageId),
                (EventGoal.BuyShopItem, EventGoal.BuyShopItem) => true,
                (EventGoal.Headhunt, EventGoal.Headhunt) => true,
                _ => false,
            };

            if (counts) yield return m;
        }
    }
}
