using Promuse.Contracts.Moderation;

namespace Promuse.Persistence.Entities;

/// <summary>
/// 签名审核记录 / One thing a moderator did to a player's profile line: took it down (and perhaps
/// locked it), or lifted a lock.
///
/// 只增不改 / Append-only, like the config history: the row is the record of what was done and
/// why, so it is never edited. The line that was taken down is kept here, because the player's own
/// row no longer has it - and a moderator asked "what did I say?" needs the answer.
/// </summary>
public class SignatureModeration
{
    public long Id { get; set; }

    /// <summary>The player whose line it was.</summary>
    public Guid AccountId { get; set; }

    public SignatureModerationAction Action { get; set; }

    /// <summary>The line as it stood when taken down. Null for an unlock, or a lock on an empty line.</summary>
    public string? RemovedSignature { get; set; }

    public string? Reason { get; set; }

    /// <summary>The lock this left in place; null for none.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>The mail the player was sent about it, if one was.</summary>
    public Guid? MailId { get; set; }

    /// <summary>The moderator. Null once that account is gone - the record outlives it.</summary>
    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
