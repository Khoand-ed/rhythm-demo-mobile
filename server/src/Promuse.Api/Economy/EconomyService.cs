using Microsoft.EntityFrameworkCore;
using Promuse.Api.Infrastructure;
using Promuse.Api.Players;
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
    PlayerService players)
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

        if (!await TryDebitAsync(accountId, offer.PriceItemId, priceTotal, ct))
        {
            return Outcome<PurchaseResult>.Fail(ApiProblems.InsufficientFunds());
        }

        await CreditAsync(accountId, offer.SellItemId, sellTotal, ct);

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
        await BumpStateVersionAsync(accountId, now, ct);

        await transaction.CommitAsync(ct);

        Outcome<PlayerState> state = await players.GetStateAsync(accountId, ct);

        if (!state.IsSuccess) return Outcome<PurchaseResult>.Fail(state.Problem!);

        return Outcome<PurchaseResult>.Ok(new PurchaseResult(
            purchase.Id,
            new Promuse.Contracts.Players.ItemStack(offer.SellItemId, sellTotal),
            new Promuse.Contracts.Players.ItemStack(offer.PriceItemId, priceTotal),
            state.Value!));
    }

    // ------------------------------------------------------------ internals

    /// <summary>
    /// Takes <paramref name="amount"/> of an item, or reports that the player
    /// cannot afford it. Never overdraws.
    ///
    /// 为什么要先加锁 / The row is locked before anything is decided, and the
    /// first version of this did not do that. A stack is removed at zero -
    /// matching PlayerData.TakeItem on the client, and required by the
    /// `amount > 0` constraint - so spending the last of something is a DELETE
    /// while spending part of it is an UPDATE. Choosing between them from two
    /// separately-guarded statements looked race-safe and was not:
    ///
    ///   balance 3P, three concurrent checkouts of P each.
    ///   The third runs `DELETE ... WHERE amount = P` against a snapshot still
    ///   showing 2P, matches nothing, then blocks on `UPDATE ... WHERE amount > P`.
    ///   By the time it wakes the balance is exactly P, `P > P` is false, and a
    ///   player who could afford the purchase is told they cannot.
    ///
    /// An integration test caught that - two succeeded where three should have.
    /// Taking the lock first collapses it to an ordinary read-modify-write: the
    /// second transaction waits, then reads the committed value and branches on
    /// a number that cannot change underneath it.
    /// </summary>
    private async Task<bool> TryDebitAsync(Guid accountId, int itemId, int amount, CancellationToken ct)
    {
        // 0 means no row: the CHECK constraint makes a stored zero impossible,
        // so the two cases cannot be confused.
        int current = await db.Database
            .SqlQuery<int>(
                $"""
                 SELECT amount AS "Value" FROM player_items
                 WHERE account_id = {accountId} AND item_id = {itemId}
                 FOR UPDATE
                 """)
            .FirstOrDefaultAsync(ct);

        if (current < amount) return false;

        if (current == amount)
        {
            await db.PlayerItems
                .Where(i => i.AccountId == accountId && i.ItemId == itemId)
                .ExecuteDeleteAsync(ct);

            return true;
        }

        await db.PlayerItems
            .Where(i => i.AccountId == accountId && i.ItemId == itemId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Amount, i => i.Amount - amount), ct);

        return true;
    }

    /// <summary>
    /// Adds to a stack, creating it if the player had none.
    ///
    /// 用 upsert 而不是先读后写 / A raw upsert rather than read-then-insert,
    /// because two purchases of the same item arriving together would both find
    /// no row and both try to insert - and the second would fail on the primary
    /// key. ON CONFLICT makes that case an addition instead of an error.
    /// </summary>
    private Task CreditAsync(Guid accountId, int itemId, int amount, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO player_items (account_id, item_id, amount)
             VALUES ({accountId}, {itemId}, {amount})
             ON CONFLICT (account_id, item_id)
             DO UPDATE SET amount = player_items.amount + EXCLUDED.amount
             """, ct);

    private Task BumpStateVersionAsync(Guid accountId, DateTimeOffset now, CancellationToken ct) =>
        db.Players
            .Where(p => p.AccountId == accountId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.StateVersion, p => p.StateVersion + 1)
                .SetProperty(p => p.UpdatedAt, now), ct);
}
