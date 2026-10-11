// Compiled by both sides - see the note at the top of ApiProblem.cs. C# 9 syntax
// is deliberate: Unity 6 compiles at that level.

#nullable enable

using System;
using System.Collections.Generic;

namespace Promuse.Contracts.Campaign
{
    /// <summary>How the stage screen labels a drop's chance - the exact odds stay on the server's side of the screen.</summary>
    public enum DropRarity
    {
        Common,
        Uncommon,
        Rare
    }

    /// <summary>
    /// 掉落 / One thing a stage may drop on a clear: rolled on its own, every clear.
    /// </summary>
    /// <param name="Chance">In basis points: 10000 always drops, 2500 one clear in four.</param>
    /// <param name="Min">How many when it drops; a random amount between Min and Max inclusive.</param>
    public sealed record CampaignDrop(int ItemId, int Min, int Max, int Chance, DropRarity Rarity);

    /// <summary>
    /// 主线关卡 / One stage of the main campaign: a chart, played from the campaign screen, in order.
    /// </summary>
    /// <param name="StageId">The campaign stage, e.g. main_1_1 - not the chart, which several stages may share.</param>
    /// <param name="Code">What the stage is called on screen, e.g. "1-1".</param>
    /// <param name="ChartId">The chart played, a SongChart.stageId.</param>
    /// <param name="FirstClearOrundum">Paid once, on the first clear.</param>
    /// <param name="StoryBefore">A story script in the game, played before the stage the first time.</param>
    /// <param name="StoryAfter">Played after the stage's first clear.</param>
    public sealed record CampaignStage(
        string StageId,
        string Code,
        string ChartId,
        int FirstClearOrundum,
        IReadOnlyList<CampaignDrop> Drops,
        string? StoryBefore = null,
        string? StoryAfter = null);

    public sealed record CampaignChapter(string ChapterId, string Title, IReadOnlyList<CampaignStage> Stages);

    /// <summary>
    /// 主线是一份文档 / The whole campaign is one document, edited whole on the admin page. Stages
    /// unlock in the order they are listed, across chapters: a chapter opens with its first stage.
    /// </summary>
    public sealed record CampaignDefinition(IReadOnlyList<CampaignChapter> Chapters);

    // ------------------------------------------------------------------ player

    /// <param name="StaminaCost">The chart's Sanity price, charged when the stage is entered.</param>
    public sealed record CampaignStageState(CampaignStage Stage, bool IsUnlocked, bool IsCleared, int StaminaCost);

    public sealed record CampaignChapterState(
        string ChapterId,
        string Title,
        bool IsUnlocked,
        IReadOnlyList<CampaignStageState> Stages);

    public sealed record CampaignView(IReadOnlyList<CampaignChapterState> Chapters, DateTimeOffset ServerTime);

    // ------------------------------------------------------------------ admin

    /// <param name="Version">The ETag for the next save.</param>
    public sealed record AdminCampaign(
        int Version,
        CampaignDefinition Definition,
        DateTimeOffset UpdatedAt,
        string? UpdatedBy);

    public sealed record CampaignUpsertRequest(CampaignDefinition Definition);
}
