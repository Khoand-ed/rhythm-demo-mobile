// Compiled by both sides - see the note at the top of ApiProblem.cs. C# 9 syntax
// is deliberate: Unity 6 compiles at that level.

#nullable enable

using System;
using System.Collections.Generic;
using Promuse.Contracts.Players;

namespace Promuse.Contracts.Mail
{
    /// <summary>
    /// One mail in the player's box.
    ///
    /// 打开就领取 / Opening a mail is what reads it and collects what it carries, in one step, the
    /// way the reference game does: a mail is either new, or opened with its items already in the bag.
    /// </summary>
    /// <param name="SenderCharacterId">An operator whose portrait signs the mail; null for none.</param>
    /// <param name="ExpiresAt">After this the mail is gone, and with it anything not collected.</param>
    /// <param name="IsOpened">Opened - read, and its attachments collected.</param>
    public sealed record MailItem(
        Guid MailId,
        string Title,
        string Body,
        string Sender,
        string? SenderCharacterId,
        IReadOnlyList<ItemStack> Attachments,
        DateTimeOffset SentAt,
        DateTimeOffset ExpiresAt,
        bool IsOpened);

    /// <param name="Mails">Newest first, at most <paramref name="Capacity"/>: past that the oldest drop off.</param>
    /// <param name="Unopened">How many are new - the badge on Home.</param>
    public sealed record MailBox(
        IReadOnlyList<MailItem> Mails,
        int Unopened,
        int Capacity,
        DateTimeOffset ServerTime);

    /// <summary>What opening, collecting or deleting answers with: what was given, the box now, the whole player.</summary>
    public sealed record MailActionResult(
        IReadOnlyList<ItemStack> Granted,
        MailBox Box,
        PlayerState Player);

    // ------------------------------------------------------------------ admin

    /// <param name="ToUsername">One player by username; null sends to every player who exists now.</param>
    /// <param name="ExpiresInDays">How long the mail stays in a box, 1-365.</param>
    public sealed record MailSendRequest(
        string Title,
        string Body,
        string Sender,
        string? SenderCharacterId,
        IReadOnlyList<ItemStack> Attachments,
        int ExpiresInDays,
        string? ToUsername);

    /// <param name="Opened">How many players have opened it.</param>
    /// <param name="IsRetracted">Taken back: no longer in anyone's box.</param>
    public sealed record AdminMail(
        Guid MailId,
        string Title,
        string Body,
        string Sender,
        string? SenderCharacterId,
        IReadOnlyList<ItemStack> Attachments,
        DateTimeOffset SentAt,
        DateTimeOffset ExpiresAt,
        string? ToUsername,
        string? SentBy,
        int Opened,
        bool IsRetracted);

    public sealed record AdminMailList(IReadOnlyList<AdminMail> Mails);
}
