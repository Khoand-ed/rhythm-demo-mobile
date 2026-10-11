namespace Promuse.Persistence.Entities;

/// <summary>
/// 一封邮件 / One mail an administrator sent, to one player or to everyone.
///
/// 群发只存一份 / A mail to everyone is one row, not one per player: who has opened or deleted it
/// is in <see cref="MailReceipt"/>, written when they do. Sending to ten thousand players is then
/// one insert, and a player who never opens their box costs nothing.
///
/// 只发给已有的人 / A mail to everyone reaches the players who existed when it was sent - an
/// apology for an outage is not owed to someone who signed up the week after.
/// </summary>
public class MailMessage
{
    public Guid Id { get; set; }

    /// <summary>One player; null for everyone who existed at <see cref="SentAt"/>.</summary>
    public Guid? ToAccountId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public string Sender { get; set; } = string.Empty;

    public string? SenderCharacterId { get; set; }

    /// <summary>The attachments as JSON - a list of ItemStack (jsonb).</summary>
    public string Attachments { get; set; } = "[]";

    public DateTimeOffset SentAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set when an administrator takes it back: it leaves every box. Already-collected items stay collected.</summary>
    public DateTimeOffset? RetractedAt { get; set; }

    public Guid? SentBy { get; set; }
}

/// <summary>
/// What one player did with one mail. The row existing is the opening - and the collection of the
/// attachments - so opening twice is a primary-key conflict rather than a check that could race.
/// </summary>
public class MailReceipt
{
    public Guid AccountId { get; set; }

    public Guid MailId { get; set; }

    public DateTimeOffset OpenedAt { get; set; }

    /// <summary>Set by Delete Read: the mail leaves this player's box.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
