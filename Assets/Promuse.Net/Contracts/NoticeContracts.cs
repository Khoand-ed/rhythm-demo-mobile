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

    /// <summary>What one block of a notice's content is.</summary>
    public enum NoticeBlockType
    {
        Text,
        Image
    }

    /// <summary>
    /// 内容块 / One block of a notice as players see it: a paragraph, or a picture between paragraphs.
    /// </summary>
    /// <param name="Text">A Text block's TextMeshPro rich text; null for an Image.</param>
    /// <param name="ImageUrl">An Image block's path on this server, served like a banner; null for Text.</param>
    public sealed record NoticeBlock(NoticeBlockType Type, string? Text, string? ImageUrl);

    /// <summary>
    /// 公告 / One notice as players see it.
    /// </summary>
    /// <param name="ListTitle">The short title on the list on the left.</param>
    /// <param name="Body">TextMeshPro rich text - &lt;b&gt;, &lt;size&gt;, &lt;color&gt; and the like. For a
    /// notice written in blocks, its paragraphs joined - what a build from before blocks shows.</param>
    /// <param name="BannerUrl">Path of the banner image on this server, e.g. /v1/notices/banners/ab12.png; null for none.</param>
    /// <param name="Blocks">The content in order, paragraphs and pictures; null for a notice that is
    /// only <paramref name="Body"/>.</param>
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
        bool IsRead,
        IReadOnlyList<NoticeBlock>? Blocks = null);

    /// <summary>The notices showing now, highest sort order first, then newest.</summary>
    public sealed record NoticeList(IReadOnlyList<Notice> Notices, DateTimeOffset ServerTime);

    // ------------------------------------------------------------------ admin

    /// <summary>One block as the admin page writes it.</summary>
    /// <param name="Text">A Text block's rich text.</param>
    /// <param name="Image">An Image block's file name, returned by the image upload (the same one banners use).</param>
    public sealed record NoticeBlockDefinition(NoticeBlockType Type, string? Text, string? Image);

    /// <param name="Body">The text, for a notice with no blocks. With blocks it is ignored and
    /// written from their paragraphs.</param>
    /// <param name="Banner">A file name returned by the banner upload; null for none.</param>
    /// <param name="EndsAt">Null keeps it up until switched off.</param>
    /// <param name="Blocks">Paragraphs and pictures in order; null or empty for a notice that is only
    /// <paramref name="Body"/>.</param>
    public sealed record NoticeDefinition(
        NoticeCategory Category,
        string ListTitle,
        string Title,
        string Body,
        string? Banner,
        DateTimeOffset StartsAt,
        DateTimeOffset? EndsAt,
        bool IsActive,
        int SortOrder,
        IReadOnlyList<NoticeBlockDefinition>? Blocks = null);

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
