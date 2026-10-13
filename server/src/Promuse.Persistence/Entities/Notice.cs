namespace Promuse.Persistence.Entities;

/// <summary>
/// 公告 / A notice on the announcement board - an event, a system message, news.
///
/// Never deleted: switching one off takes it down and keeps who read it.
/// </summary>
public class Notice
{
    public Guid Id { get; set; }

    /// <summary>Bumped on every save; the ETag an admin edit must match.</summary>
    public int Version { get; set; }

    /// <summary>"Event", "System" or "News".</summary>
    public string Category { get; set; } = string.Empty;

    public string ListTitle { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>TextMeshPro rich text. For a notice in blocks, its paragraphs joined.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// The content in blocks - paragraphs and pictures in order, a JSON list of
    /// NoticeBlockDefinition. Null for a notice that is only <see cref="Body"/>.
    /// </summary>
    public string? Blocks { get; set; }

    /// <summary>A file in the banner folder; null for none.</summary>
    public string? Banner { get; set; }

    public DateTimeOffset StartsAt { get; set; }

    public DateTimeOffset? EndsAt { get; set; }

    public bool IsActive { get; set; }

    /// <summary>Higher shows first.</summary>
    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

/// <summary>A notice one player has opened - what clears its unread dot.</summary>
public class NoticeRead
{
    public Guid AccountId { get; set; }

    public Guid NoticeId { get; set; }

    public DateTimeOffset ReadAt { get; set; }
}
