// Compiled by both sides - see the note at the top of ApiProblem.cs. C# 9 syntax
// is deliberate: Unity 6 compiles at that level.

#nullable enable

using System;
using System.Collections.Generic;
using Promuse.Contracts.Players;

namespace Promuse.Contracts.Gacha
{
    /// <summary>
    /// One operator a banner can give, and what a second copy turns into.
    ///
    /// 星级在服务端 / The rarity is the server's, not read from the client's CharMeta. It decides
    /// which tier's odds an operator is drawn under, and odds the client could edit are odds
    /// nobody enforces.
    /// </summary>
    /// <param name="DuplicateItemId">What a copy the player already owns is converted into.</param>
    public sealed record GachaPoolEntry(
        string CharacterId,
        int Rarity,
        int DuplicateItemId,
        int DuplicateAmount);

    /// <summary>
    /// A banner's rules as the server rolls them. The title, the text and the art stay in the
    /// client's banner asset; everything that decides what a pull gives is here.
    /// </summary>
    /// <param name="RateFiveStar">
    /// Basis points - 200 is 2.00%. Integers, so the three tiers add up to exactly 10000 and the
    /// screen can never show a total of 99.99%.
    /// </param>
    /// <param name="PityThreshold">
    /// Pulls in a row without a 5-star after which the next pull is a 5-star.
    /// </param>
    /// <param name="TicketItemId">
    /// Spent first, one per pull, when the player holds enough for the whole request; otherwise
    /// the pull is paid in <paramref name="CurrencyItemId"/>.
    /// </param>
    /// <param name="PullsSinceFiveStar">The caller's own count on this banner.</param>
    public sealed record GachaBannerInfo(
        string BannerId,
        DateTimeOffset? StartsAt,
        DateTimeOffset? EndsAt,
        int RateFiveStar,
        int RateFourStar,
        int RateThreeStar,
        int PityThreshold,
        int CurrencyItemId,
        int CostSingle,
        int CostMulti,
        int? TicketItemId,
        IReadOnlyList<GachaPoolEntry> Pool,
        int PullsSinceFiveStar);

    /// <summary>
    /// Every banner open now or opening later, in display order. Ended banners are not listed.
    /// </summary>
    public sealed record GachaBannerList(
        IReadOnlyList<GachaBannerInfo> Banners,
        DateTimeOffset ServerTime);

    /// <param name="Times">1 or 10.</param>
    public sealed record GachaPullRequest(string BannerId, int Times);

    /// <param name="IsNew">The player did not own this operator before this pull.</param>
    /// <param name="Converted">What the copy became when it was not new; null when it was.</param>
    /// <param name="Guaranteed">This pull was the pity guarantee, not the odds.</param>
    public sealed record GachaPullOutcome(
        string CharacterId,
        int Rarity,
        bool IsNew,
        ItemStack? Converted,
        bool Guaranteed);

    /// <summary>
    /// 回整个玩家状态 / Carries the whole player back, the way a purchase does: the roster and the
    /// bag both changed, and the client should not work out either.
    /// </summary>
    public sealed record GachaPullResult(
        Guid BatchId,
        string BannerId,
        IReadOnlyList<GachaPullOutcome> Pulls,
        ItemStack Charged,
        int PullsSinceFiveStar,
        PlayerState Player);

    /// <param name="Sequence">Increasing with every pull; the cursor the next page starts below.</param>
    public sealed record GachaHistoryEntry(
        long Sequence,
        Guid BatchId,
        string BannerId,
        string CharacterId,
        int Rarity,
        bool IsNew,
        DateTimeOffset PulledAt);

    /// <param name="NextBefore">Pass as <c>before</c> for the next page; null on the last one.</param>
    public sealed record GachaHistoryPage(
        IReadOnlyList<GachaHistoryEntry> Entries,
        long? NextBefore);
}
