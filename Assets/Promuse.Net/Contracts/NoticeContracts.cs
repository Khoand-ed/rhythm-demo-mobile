// Compiled by both sides - see the note at the top of ApiProblem.cs. C# 9 syntax
// is deliberate: Unity 6 compiles at that level.

#nullable enable

using System;
using System.Collections.Generic;

namespace Promuse.Contracts.Notices
{
    /// <summary>The tab a notice sits under.</summary>
    public enum NoticeCategory
    {
        Event,
        System,
        News
    }

    /// <summary>
    /// 公告 / One notice as players see it.
    /// </summary>
    /// <param name="ListTitle">The short title on the list on the left.</param>
    /// <param name="Body">TextMeshPro rich text - &lt;b&gt;, &lt;size&gt;, &lt;color&gt; and the like.</param>
    /// <param name="BannerUrl">Path of the banner image on this server, e.g. /v1/notices/banners/ab12.png; null for none.</param>
    public sealed record Notice(
        Guid NoticeId,
        NoticeCategory Category,
        string ListTitle,
        string Title,
        string Body,
        string? BannerUrl,
        DateTimeOffset StartsAt,
        DateTimeOffset? EndsAt,
        int SortOrder,
        bool IsRead);

    /// <summary>The notices showing now, highest sort order first, then newest.</summary>
    public sealed record NoticeList(IReadOnlyList<Notice> Notices, DateTimeOffset ServerTime);

    // ------------------------------------------------------------------ admin

    /// <param name="Banner">A file name returned by the banner upload; null for none.</param>
    /// <param name="EndsAt">Null keeps it up until switched off.</param>
    public sealed record NoticeDefinition(
        NoticeCategory Category,
        string ListTitle,
        string Title,
        string Body,
        string? Banner,
        DateTimeOffset StartsAt,
        DateTimeOffset? EndsAt,
        bool IsActive,
        int SortOrder);

    /// <param name="Version">The ETag for the next save.</param>
    public sealed record AdminNotice(
        Guid NoticeId,
        int Version,
        NoticeDefinition Definition,
        DateTimeOffset UpdatedAt,
        string? UpdatedBy);

    public sealed record AdminNoticeList(IReadOnlyList<AdminNotice> Notices);

    public sealed record NoticeUpsertRequest(NoticeDefinition Definition);

    /// <param name="Banner">The stored file name, to put in a definition.</param>
    /// <param name="Url">Where it is served, to preview it.</param>
    public sealed record BannerUpload(string Banner, string Url);
}
