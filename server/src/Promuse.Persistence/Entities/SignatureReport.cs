using Promuse.Contracts.Moderation;

namespace Promuse.Persistence.Entities;

/// <summary>
/// 举报 / One player reporting another's profile line.
///
/// 记下当时看到的 / The line is kept as the reporter saw it: the player may change it before a
/// moderator looks, and a report is about what was read, not what is there now.
///
/// 不删只关 / Closed rather than deleted when a moderator takes the line down or dismisses the
/// reports: a closed report is what stops the same player reporting the same line again, and what
/// a moderator reads back when the line is reported a second time.
/// </summary>
public class SignatureReport
{
    public long Id { get; set; }

    public Guid ReporterId { get; set; }

    /// <summary>The player whose line was reported.</summary>
    public Guid TargetId { get; set; }

    /// <summary>The line as the reporter saw it.</summary>
    public string Signature { get; set; } = string.Empty;

    public SignatureReportReason Reason { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Null while it waits for a moderator.</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    public SignatureReportResolution? Resolution { get; set; }

    /// <summary>The moderator's note: the removal's reason, or what a dismissal said.</summary>
    public string? ResolutionNote { get; set; }

    /// <summary>The moderator who closed it. Null while open, and once that account is gone.</summary>
    public Guid? ResolvedBy { get; set; }

    /// <summary>The thank-you mail the reporter was sent when it closed.</summary>
    public Guid? MailId { get; set; }
}
