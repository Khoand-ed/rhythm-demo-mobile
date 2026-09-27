// Compiled by both sides - see the note at the top of ApiProblem.cs. C# 9 syntax
// is deliberate: Unity 6 compiles at that level.

#nullable enable

using System;
using System.Collections.Generic;
using Promuse.Contracts.Players;

namespace Promuse.Contracts.Shop
{
    /// <summary>
    /// One row of the shop as the client sees it.
    ///
    /// 价格来自服务端 / The price arrives from the server rather than being read
    /// out of ShopItemDataList.asset. That asset is what the client used to do
    /// the arithmetic with, which meant the amount a player paid was a number
    /// their own device chose.
    /// </summary>
    public sealed record ShopOffer(
        Guid OfferId,
        int SellItemId,
        int SellAmount,
        int PriceItemId,
        int PriceAmount);

    public sealed record ShopCatalog(
        IReadOnlyList<ShopOffer> Offers,
        DateTimeOffset ServerTime);

    /// <param name="Quantity">
    /// How many of the offer, in one checkout. The client may not send a total
    /// price: it names what it wants and the server works out the cost, because
    /// a total the client computed is a total the client can choose.
    /// </param>
    public sealed record PurchaseRequest(Guid OfferId, int Quantity);

    /// <summary>
    /// 回整个玩家状态 / Carries the whole player back, the way the Phase 2 writes
    /// do. The client then never has to work out what its inventory became, and
    /// never holds a state version it has not seen the body for.
    /// </summary>
    public sealed record PurchaseResult(
        Guid PurchaseId,
        ItemStack Granted,
        ItemStack Charged,
        PlayerState Player);
}
