using System.Text.Json;
using Promuse.Api.Events;
using Promuse.Api.Gacha;
using Promuse.Contracts.Campaign;
using Promuse.Contracts.Players;
using Promuse.Contracts.Stories;
using Promuse.Persistence;

namespace Promuse.Api.Campaign;

/// <summary>
/// What makes a campaign sound, the order its stages open in, and how a clear's drops are rolled -
/// nothing that touches the database, so each rule is pinned by a plain unit test.
/// </summary>
public static class CampaignRules
{
    public const int MaxCode = 8;
    public const int MaxTitle = 60;
    public const int MaxDropsPerStage = 6;
    public const int MaxOrundum = 10_000;
    public const int MaxAmount = 1_000_000;
    public const int Whole = 10_000;

    public static CampaignDefinition Parse(string json) =>
        Normalize(JsonSerializer.Deserialize<CampaignDefinition>(json, SeedEvents.Json)
                  ?? throw new InvalidOperationException("The campaign holds an empty definition."));

    public static string Serialize(CampaignDefinition d) => JsonSerializer.Serialize(Normalize(d), SeedEvents.Json);

    /// <summary>A list left out of the JSON arrives as null; it means none.</summary>
    public static CampaignDefinition Normalize(CampaignDefinition d) => new(
        [.. (d.Chapters ?? []).Select(c => c with
        {
            Stages = [.. (c.Stages ?? []).Select(s => s with { Drops = s.Drops ?? [] })],
        })]);

    /// <summary>Every stage in play order, across chapters, with the chapter it belongs to.</summary>
    public static List<(CampaignChapter Chapter, CampaignStage Stage)> Flatten(CampaignDefinition d) =>
        [.. d.Chapters.SelectMany(c => c.Stages.Select(s => (c, s)))];

    /// <summary>
    /// 按顺序解锁 / The stage at <paramref name="index"/> in play order is open when it is the first, or
    /// the stage before it - in the same chapter or at the end of the last one - has been cleared.
    /// </summary>
    public static bool IsUnlocked(IReadOnlyList<(CampaignChapter Chapter, CampaignStage Stage)> order, int index,
                                  IReadOnlySet<string> cleared) =>
        index == 0 || (index > 0 && index < order.Count && cleared.Contains(order[index - 1].Stage.StageId));

    /// <summary>
    /// 走到哪了 / The latest stage in play order the player has cleared - what a profile shows as
    /// their progress - or null before the first. Taken from the order, not from when each clear
    /// happened, so replaying 1-1 after 2-3 still reads as 2-3.
    /// </summary>
    public static CampaignStage? Furthest(IReadOnlyList<(CampaignChapter Chapter, CampaignStage Stage)> order,
                                          IReadOnlySet<string> cleared)
    {
        for (int i = order.Count - 1; i >= 0; i--)
        {
            if (cleared.Contains(order[i].Stage.StageId)) return order[i].Stage;
        }

        return null;
    }

    /// <summary>
    /// 每样单独掷 / Each drop is rolled on its own: it drops when the dice land under its chance, in an
    /// amount between Min and Max. The dice are the server's - a client never chooses what it got.
    /// </summary>
    public static List<ItemStack> Roll(IReadOnlyList<CampaignDrop> drops, IGachaDice dice)
    {
        var rolled = new List<ItemStack>();
        foreach (CampaignDrop drop in drops)
        {
            if (dice.Next(Whole) >= drop.Chance) continue;
            int amount = drop.Max > drop.Min ? drop.Min + dice.Next(drop.Max - drop.Min + 1) : drop.Min;
            rolled.Add(new ItemStack(drop.ItemId, amount));
        }
        return rolled;
    }

    /// <summary>Field path to what is wrong; empty when the campaign can be saved.</summary>
    public static Dictionary<string, string[]> Validate(CampaignDefinition? raw, IReadOnlySet<string> charts)
    {
        var errors = new Dictionary<string, string[]>();
        void Add(string field, string message) =>
            errors[field] = errors.TryGetValue(field, out string[]? had) ? [.. had, message] : [message];

        if (raw is null)
        {
            Add("definition", "Required.");
            return errors;
        }

        CampaignDefinition d = Normalize(raw);
        var chapterIds = new HashSet<string>();
        var stageIds = new HashSet<string>();

        for (int c = 0; c < d.Chapters.Count; c++)
        {
            CampaignChapter chapter = d.Chapters[c];
            string at = $"chapters[{c}]";

            if (!EventRules.IsValidId(chapter.ChapterId)) Add($"{at}.chapterId", "Lower-case letters, digits and underscores.");
            else if (!chapterIds.Add(chapter.ChapterId)) Add($"{at}.chapterId", $"'{chapter.ChapterId}' appears more than once.");
            if (string.IsNullOrWhiteSpace(chapter.Title) || chapter.Title.Length > MaxTitle) Add($"{at}.title", $"1-{MaxTitle} characters.");

            for (int s = 0; s < chapter.Stages.Count; s++)
            {
                CampaignStage stage = chapter.Stages[s];
                string st = $"{at}.stages[{s}]";

                if (!EventRules.IsValidId(stage.StageId)) Add($"{st}.stageId", "Lower-case letters, digits and underscores.");
                else if (!stageIds.Add(stage.StageId)) Add($"{st}.stageId", $"'{stage.StageId}' appears more than once.");
                if (string.IsNullOrWhiteSpace(stage.Code) || stage.Code.Length > MaxCode) Add($"{st}.code", $"1-{MaxCode} characters, e.g. 1-1.");
                if (!charts.Contains(stage.ChartId ?? "")) Add($"{st}.chartId", $"No chart '{stage.ChartId}'.");
                if (stage.FirstClearOrundum is < 0 or > MaxOrundum) Add($"{st}.firstClearOrundum", $"Between 0 and {MaxOrundum}.");
                if (stage.StoryBefore is not null && !StoryIds.IsStoryId(stage.StoryBefore)) Add($"{st}.storyBefore", "A story script's file name, or empty.");
                if (stage.StoryAfter is not null && !StoryIds.IsStoryId(stage.StoryAfter)) Add($"{st}.storyAfter", "A story script's file name, or empty.");
                if (stage.Drops.Count > MaxDropsPerStage) Add($"{st}.drops", $"At most {MaxDropsPerStage}.");

                for (int i = 0; i < stage.Drops.Count; i++)
                {
                    CampaignDrop drop = stage.Drops[i];
                    string dt = $"{st}.drops[{i}]";
                    if (drop is null) { Add(dt, "Required."); continue; }
                    if (drop.ItemId < 0) Add($"{dt}.itemId", "Must not be negative.");
                    if (drop.Min is < 1 or > MaxAmount) Add($"{dt}.min", $"Between 1 and {MaxAmount}.");
                    if (drop.Max < drop.Min || drop.Max > MaxAmount) Add($"{dt}.max", $"Between min and {MaxAmount}.");
                    if (drop.Chance is < 1 or > Whole) Add($"{dt}.chance", "Between 1 and 10000 (basis points: 10000 always drops).");
                    if (!Enum.IsDefined(drop.Rarity)) Add($"{dt}.rarity", "Common, Uncommon or Rare.");
                }
            }
        }

        return errors;
    }
}
