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

    // ------------------------------------------------------------ reports

    /// <summary>Why a player reported someone's profile line.</summary>
    public enum SignatureReportReason
    {
        /// <summary>Spam or advertising.</summary>
        Spam,

        /// <summary>Offensive, hateful or harassing.</summary>
        Offensive,

        /// <summary>Something else; the note says what.</summary>
        Other
    }

    /// <summary>How a moderator closed the reports on a line.</summary>
    public enum SignatureReportResolution
    {
        /// <summary>The line was taken down.</summary>
        Removed,

        /// <summary>The line was found within the rules and left up.</summary>
        Dismissed
    }

    /// <summary>Bounds both sides know, so the report form stops where the server would refuse.</summary>
    public static class SignatureReportLimits
    {
        /// <summary>The optional note's length.</summary>
        public const int NoteMaxLength = 200;

        /// <summary>Reports one player may send in any 24 hours.</summary>
        public const int PerDay = 10;
    }

    /// <param name="Note">Optional, at most 200 characters; what the reason does not say.</param>
    public sealed record SignatureReportRequest(SignatureReportReason Reason, string? Note);

    /// <summary>One report as the moderators' queue shows it.</summary>
    /// <param name="Signature">The line as the reporter saw it, which may since have changed.</param>
    public sealed record AdminSignatureReport(
        long Id,
        Guid ReporterId,
        string ReporterName,
        int ReporterTag,
        SignatureReportReason Reason,
        string? Note,
        string Signature,
        DateTimeOffset At);

    /// <summary>A reported player and every open report on their line, oldest first.</summary>
    /// <param name="Signature">The line as it stands now; null when it is already gone.</param>
    public sealed record AdminReportedPlayer(
        Guid PlayerId,
        string DisplayName,
        int Tag,
        string? Signature,
        DateTimeOffset? LockedUntil,
        int Removals,
        IReadOnlyList<AdminSignatureReport> Reports);

    /// <summary>The players with open reports, the most reported first.</summary>
    public sealed record AdminReportQueue(IReadOnlyList<AdminReportedPlayer> Players);

    /// <param name="Note">Optional, kept with the closed reports. At most 200 characters.</param>
    public sealed record SignatureReportDismissRequest(string? Note);

    // ------------------------------------------------------------ flagged runs

    /// <summary>What a moderator decided about a flagged run.</summary>
    public enum RunReviewDecision
    {
        /// <summary>Ranked on the boards, at the score the server's replay came to.</summary>
        Approved,

        /// <summary>Kept off the boards for good. The clear and its rewards stand.</summary>
        Rejected
    }

    /// <summary>One set of a run's numbers - as the device claimed them, or as the replay counted them.</summary>
    /// <param name="Won">The run ended with HP left: claimed as won, or a replay that did not fail.</param>
    public sealed record AdminRunNumbers(
        int Score,
        int MaxCombo,
        int Perfect,
        int Great,
        int Hit,
        int Miss,
        bool FullCombo,
        bool Won);

    /// <summary>A run the review flagged, with everything a moderator needs to decide it.</summary>
    /// <param name="Flags">The signals that held it off the boards, such as REPLAY_MISMATCH.</param>
    /// <param name="Replay">What the server's replay of the trace counted; null when it could not run.</param>
    /// <param name="RankedScore">The score an approval put on the boards.</param>
    public sealed record AdminFlaggedRun(
        Guid RunId,
        Guid PlayerId,
        string DisplayName,
        int Tag,
        string StageId,
        string? SongName,
        string? CharacterId,
        int CharacterLevel,
        DateTimeOffset SubmittedAt,
        IReadOnlyList<string> Flags,
        AdminRunNumbers Claimed,
        AdminRunNumbers? Replay,
        int? Ceiling,
        int Presses,
        int TraceFrames,
        int? TimingSamples,
        double? TimingMeanMs,
        double? TimingStdDevMs,
        double? FrameIntervalMs,
        RunReviewDecision? Decision,
        string? DecisionNote,
        string? DecidedBy,
        DateTimeOffset? DecidedAt,
        int? RankedScore);

    public sealed record AdminFlaggedRunList(IReadOnlyList<AdminFlaggedRun> Runs);

    /// <param name="Note">
    /// Why. Required to reject - the player is mailed it - and optional to approve. At most 200 characters.
    /// </param>
    public sealed record RunDecisionRequest(string? Note);
}
