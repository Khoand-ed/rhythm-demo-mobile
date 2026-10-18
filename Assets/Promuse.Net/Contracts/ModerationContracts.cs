// Compiled by both sides - see the note at the top of ApiProblem.cs. C# 9 syntax
// is deliberate: Unity 6 compiles at that level.
//
// 只有管理页用 / Only the admin page reads these today; they live with the other contracts so the
// shapes are written down once, like the mail and event admin contracts.

#nullable enable

using System;
using System.Collections.Generic;

namespace Promuse.Contracts.Moderation
{
    /// <summary>What a moderator did to a player's profile line.</summary>
    public enum SignatureModerationAction
    {
        /// <summary>The line was taken down, and perhaps locked against a new one.</summary>
        Removed,

        /// <summary>A lock was lifted before its time.</summary>
        Unlocked
    }

    /// <summary>
    /// One player as the signature moderation list shows them.
    /// </summary>
    /// <param name="SignatureUpdatedAt">When the player last changed the line; null if never.</param>
    /// <param name="LockedUntil">
    /// Until when the player cannot write a line, or SignatureLock.Permanent for never; null when it
    /// is not locked.
    /// </param>
    /// <param name="Removals">How many times a moderator has taken this player's line down.</param>
    public sealed record AdminSignature(
        Guid PlayerId,
        string DisplayName,
        int Tag,
        string? Signature,
        DateTimeOffset? SignatureUpdatedAt,
        DateTimeOffset? LockedUntil,
        int Removals);

    public sealed record AdminSignatureList(IReadOnlyList<AdminSignature> Players);

    /// <param name="Reason">Why, in a sentence: kept in the history and, when mailed, told to the player. 1-200 characters.</param>
    /// <param name="LockDays">Days the player cannot write a new line, 0-365; 0 is no lock.</param>
    /// <param name="LockPermanently">Locked until a moderator lifts it. Overrides LockDays.</param>
    /// <param name="NotifyByMail">Send the player a mail saying the line was removed, why, and for how long it is locked.</param>
    public sealed record SignatureRemoveRequest(
        string Reason,
        int LockDays,
        bool LockPermanently,
        bool NotifyByMail);

    /// <param name="Reason">Optional, kept in the history. At most 200 characters.</param>
    public sealed record SignatureUnlockRequest(string? Reason);

    /// <summary>One entry of the moderation history, newest first.</summary>
    /// <param name="RemovedSignature">The line as it stood when it was taken down; null for an unlock, or a lock on an empty line.</param>
    /// <param name="LockedUntil">The lock the action left in place; null for none.</param>
    /// <param name="Mailed">The player was sent a mail about it.</param>
    /// <param name="By">The moderator's username; null once that account is gone.</param>
    public sealed record SignatureModerationEntry(
        long Id,
        Guid PlayerId,
        string DisplayName,
        int Tag,
        SignatureModerationAction Action,
        string? RemovedSignature,
        string? Reason,
        DateTimeOffset? LockedUntil,
        bool Mailed,
        string? By,
        DateTimeOffset At);

    public sealed record SignatureModerationHistory(IReadOnlyList<SignatureModerationEntry> Entries);
}
