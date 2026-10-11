// Compiled by both sides - see the note at the top of ApiProblem.cs. C# 9 syntax
// is deliberate: Unity 6 compiles at that level.

#nullable enable

using System;
using System.Collections.Generic;

namespace Promuse.Contracts.Config
{
    /// <param name="Message">Shown to players while it is on. Null for a generic message.</param>
    /// <param name="EndsAt">The expected end, if known - shown, and sent as Retry-After.</param>
    public sealed record MaintenanceWindow(bool Enabled, string? Message, DateTimeOffset? EndsAt);

    /// <summary>
    /// 每个开关都有服务端在守 / Every switch here is enforced by the server, not only drawn by
    /// the client: turning one off refuses the endpoint behind it. A flag only the client obeyed
    /// would be a suggestion an old build could ignore.
    /// </summary>
    /// <param name="Gacha">Headhunting pulls.</param>
    /// <param name="Shop">Shop purchases.</param>
    /// <param name="Ranked">Opening a ranked run. Practice never touches the server.</param>
    /// <param name="Leaderboards">Reading the boards. Scores are still recorded while they are off.</param>
    public sealed record FeatureFlags(bool Gacha, bool Shop, bool Ranked, bool Leaderboards);

    /// <summary>
    /// What an operator edits. Typed rather than a free-form bag of keys: each field has code on
    /// both sides that acts on it, so a field nothing reads cannot be added by a typo.
    /// </summary>
    /// <param name="MinClientVersion">"major.minor.patch"; a build older than this is told to update.</param>
    /// <param name="Announcement">One line shown once per version on the Home screen. Null for none.</param>
    public sealed record ConfigDocument(
        MaintenanceWindow Maintenance,
        string MinClientVersion,
        FeatureFlags Features,
        string? Announcement);

    /// <summary>The live config as every client reads it. The ETag is the version.</summary>
    public sealed record RemoteConfig(
        int Version,
        ConfigDocument Document,
        DateTimeOffset UpdatedAt,
        DateTimeOffset ServerTime);

    // ------------------------------------------------------------------ admin

    /// <param name="Note">Why - kept in the history beside the change.</param>
    public sealed record ConfigUpdateRequest(ConfigDocument Document, string? Note);

    /// <summary>
    /// Puts an earlier version back - as a NEW version, so the history only ever grows and a
    /// rollback can itself be rolled back.
    /// </summary>
    public sealed record ConfigRollbackRequest(int Version, string? Note);

    /// <param name="CreatedBy">The admin's username; null for the seeded first version.</param>
    /// <param name="RolledBackFrom">Set when this version copied an earlier one.</param>
    public sealed record ConfigRevision(
        int Version,
        ConfigDocument Document,
        DateTimeOffset CreatedAt,
        string? CreatedBy,
        string? Note,
        int? RolledBackFrom);

    public sealed record ConfigHistory(IReadOnlyList<ConfigRevision> Revisions);

    /// <summary>
    /// 客户端版本 / The header every game request carries, and the one comparison both sides use.
    ///
    /// 一份比较规则 / Shared so the client deciding "I am out of date" and the server deciding
    /// "refuse this build" can never disagree about whether 1.10 is newer than 1.9.
    /// </summary>
    public static class ClientVersion
    {
        public const string Header = "X-Client-Version";

        /// <summary>
        /// "1", "1.2" and "1.2.3" read as major.minor.patch with the missing parts zero; anything
        /// after a '-' or '+' (a pre-release or build tag) is ignored. False for anything else.
        /// </summary>
        public static bool TryParse(string? text, out int major, out int minor, out int patch)
        {
            major = minor = patch = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string core = text!.Trim();
            int cut = core.IndexOfAny(new[] { '-', '+' });
            if (cut >= 0) core = core.Substring(0, cut);

            string[] parts = core.Split('.');
            if (parts.Length < 1 || parts.Length > 3) return false;

            int[] values = new int[3];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out values[i]))
                {
                    return false;
                }
            }

            major = values[0];
            minor = values[1];
            patch = values[2];
            return true;
        }

        public static bool IsValid(string? text) => TryParse(text, out _, out _, out _);

        /// <summary>
        /// True when <paramref name="client"/> is older than <paramref name="minimum"/>. A client
        /// version that does not parse is treated as not outdated: the header is the client's
        /// own claim, and refusing a build over a malformed string would lock out exactly the
        /// builds least able to explain themselves.
        /// </summary>
        public static bool IsOutdated(string? client, string? minimum)
        {
            if (!TryParse(client, out int a1, out int a2, out int a3)) return false;
            if (!TryParse(minimum, out int b1, out int b2, out int b3)) return false;

            if (a1 != b1) return a1 < b1;
            if (a2 != b2) return a2 < b2;
            return a3 < b3;
        }
    }
}
