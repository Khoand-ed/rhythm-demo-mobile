namespace Promuse.Persistence.Entities;

/// <summary>
/// 剧情热修 / A correction to one line of a story script that ships inside the game.
///
/// 不删只撤 / Never deleted: taking a correction back clears its text and moves its revision on,
/// so the highest revision always changes with the list and can serve as the list's ETag.
/// </summary>
public class StoryLinePatch
{
    /// <summary>The script's file name, e.g. ev_resonant_dawn_1_before.</summary>
    public string StoryId { get; set; } = string.Empty;

    /// <summary>The id written after the line in the script, e.g. a1f3.</summary>
    public string LineId { get; set; } = string.Empty;

    /// <summary>The text the correction was written against; the game applies it only while its own line still reads this.</summary>
    public string Original { get; set; } = string.Empty;

    /// <summary>The corrected text; null once the correction has been taken back.</summary>
    public string? Text { get; set; }

    /// <summary>From the story_patch_revisions sequence, taken again on every save.</summary>
    public int Revision { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
