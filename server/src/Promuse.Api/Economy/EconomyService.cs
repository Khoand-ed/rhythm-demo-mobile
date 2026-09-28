using Microsoft.EntityFrameworkCore;
using Promuse.Api.Infrastructure;
using Promuse.Api.Missions;
using Promuse.Api.Players;
using Promuse.Contracts.Missions;
using Promuse.Contracts.Players;
using Promuse.Contracts.Shop;
using Promuse.Persistence;
using Promuse.Persistence.Entities;

namespace Promuse.Api.Economy;

/// <summary>
/// The shop, with the server holding the prices.
///
/// 客户端只说要什么 / A purchase request names an offer and a quantity. It cannot
/// name a price and it cannot name a resulting balance - both used to be the
/// client's arithmetic, which meant the amount a player paid was a number their
/// own device chose.
/// </summary>
public sealed class EconomyService(
    PromuseDbContext db,
    TimeProvider clock,
    PlayerService players,
    Inventory inventory,
    MissionService missions)
{
    /// <summary>Matches the client's own cap on a single checkout.</summary>
    public const int MaxQuantity = 99;

    public async Task<Outcome<ShopCatalog>> GetCatalogAsync(CancellationToken ct)
    {
        List<Promuse.Contracts.Shop.ShopOffer> offers = await db.ShopOffers
            .AsNoTracking()
            .Where(o => o.IsActive)
            .OrderBy(o => o.SortOrder)
            .Select(o => new Promuse.Contracts.Shop.ShopOffer(
                o.Id, o.SellItemId, o.SellAmount, o.PriceItemId, o.PriceAmount))
            .ToListAsync(ct);

        return Outcome<ShopCatalog>.Ok(new ShopCatalog(offers, clock.GetUtcNow()));
    }

    /// <summary>
    /// 一次结账, 全有或全无 / One checkout, all or nothing. The debit, the credit,
    /// the ledger row and the state version move together or not at all - a
    /// crash between the debit and the credit would otherwise take a player's
    /// currency and give them nothing.
    /// </summary>
    public async Task<Outcome<PurchaseResult>> PurchaseAsync(
        Guid accountId, Guid offerId, int quantity, CancellationToken ct)
    {
        if (quantity < 1 || quantity > MaxQuantity)
        {
            return Outcome<PurchaseResult>.Fail(ApiProblems.ValidationFailed(
                new Dictionary<string, string[]>
                {
                    ["quantity"] = [$"Must be between 1 and {MaxQuantity}."],
                }));
        }

        Persistence.Entities.ShopOffer? offer = await db.ShopOffers
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == offerId && o.IsActive, ct);

        if (offer is null) return Outcome<PurchaseResult>.Fail(ApiProblems.OfferNotFound());

        DateTimeOffset now = clock.GetUtcNow();
        int priceTotal = offer.PriceAmount * quantity;
        int sellTotal = offer.SellAmount * quantity;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        if (!await inventory.TryDebitAsync(accountId, offer.PriceItemId, priceTotal, ct))
        {
            return Outcome<PurchaseResult>.Fail(ApiProblems.InsufficientFunds());
        }

        await inventory.CreditAsync(accountId, offer.SellItemId, sellTotal, ct);

        var purchase = new Purchase
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            ShopOfferId = offer.Id,
            Quantity = quantity,
            SellItemId = offer.SellItemId,
            SellAmountTotal = sellTotal,
            PriceItemId = offer.PriceItemId,
            PriceAmountTotal = priceTotal,
            CreatedAt = now,
        };

        db.Purchases.Add(purchase);
        await db.SaveChangesAsync(ct);

        // 版本要动 / Bumped so the ETag the client is holding stops matching. A
        // purchase changes the save, and a client that kept reading 304 would
        // show an inventory it no longer has.
        await inventory.BumpStateVersionAsync(accountId, now, ct);

        // 一次结账算一次 / One checkout, one tick - not one per unit, so buying 99
        // of something does not clear a "purchase 3 times" mission on its own.
        // Inside the transaction, so a rollback takes the tick with it.
        await missions.NotifyAsync(accountId, MissionGoal.BuyShopItem, ct);

        await transaction.CommitAsync(ct);

        Outcome<PlayerState> state = await players.GetStateAsync(accountId, ct);

        if (!state.IsSuccess) return Outcome<PurchaseResult>.Fail(state.Problem!);

        return Outcome<PurchaseResult>.Ok(new PurchaseResult(
            purchase.Id,
            new Promuse.Contracts.Players.ItemStack(offer.SellItemId, sellTotal),
            new Promuse.Contracts.Players.ItemStack(offer.PriceItemId, priceTotal),
            state.Value!));
    }

}
