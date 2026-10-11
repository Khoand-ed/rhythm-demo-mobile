// Compiled by both sides - see the note at the top of ApiProblem.cs. C# 9 syntax
// is deliberate: Unity 6 compiles at that level.

#nullable enable

using System;
using System.Collections.Generic;

namespace Promuse.Contracts.Stories
{
    /// <summary>
    /// 剧情热修 / A correction to one line of a story script.
    ///
    /// 剧情在包里 / The stories ship inside the game (Resources/Story/*.story); the server holds only
    /// these corrections, so a typo found after release is fixed without a new build. A line is named
    /// by its script and the id written after it in the file (<c>#a1f3</c>).
    /// </summary>
    /// <param name="Original">
    /// 原文对得上才替换 / The text the correction was written against. The game applies it only
    /// while its own copy of the line still reads exactly this, so once a build carries the fix - or
    /// rewrites the line - the old correction quietly stops applying instead of overwriting it.
    /// </param>
    /// <param name="Text">What the line says instead.</param>
    public sealed record StoryPatch(string StoryId, string LineId, string Original, string Text);

    /// <param name="Revision">Grows with every save; the ETag of this list.</param>
    public sealed record StoryPatchList(int Revision, IReadOnlyList<StoryPatch> Patches);

    // ------------------------------------------------------------------ admin

    public sealed record AdminStoryPatch(
        string StoryId,
        string LineId,
        string Original,
        string Text,
        int Revision,
        DateTimeOffset UpdatedAt,
        string? UpdatedBy);

    public sealed record AdminStoryPatchList(IReadOnlyList<AdminStoryPatch> Patches);

    public sealed record StoryPatchRequest(string Original, string Text);

    /// <summary>The shape of the ids, shared so the admin API and the game agree on them.</summary>
    public static class StoryIds
    {
        public const int MaxStoryId = 64;
        public const int MaxLineText = 500;

        /// <summary>A script's file name: lower-case letters, digits and underscores.</summary>
        public static bool IsStoryId(string? id) => IsMadeOf(id, 1, MaxStoryId, underscore: true);

        /// <summary>The id after a line: 4-8 lower-case letters and digits.</summary>
        public static bool IsLineId(string? id) => IsMadeOf(id, 4, 8, underscore: false);

        private static bool IsMadeOf(string? id, int min, int max, bool underscore)
        {
            if (id == null || id.Length < min || id.Length > max) return false;

            foreach (char c in id)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || (underscore && c == '_');
                if (!ok) return false;
            }

            return true;
        }
    }
}
