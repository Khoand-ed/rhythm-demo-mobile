using Microsoft.EntityFrameworkCore;
using Promuse.Persistence;

namespace Promuse.Api.Economy;

/// <summary>
/// The only two ways a player's bag changes.
///
/// 一份实现 / Extracted when missions needed to hand out items too. Two copies of
/// the upsert below would be two places to get the ON CONFLICT wrong, and the
/// debit's locking argument is subtle enough that it must not be re-derived by
/// whoever writes the next feature that spends something.
///
/// Both assume the caller has opened a transaction. Neither opens one, because
/// giving an item is never the whole of what a caller is doing - it is half of a
/// purchase or half of a claim, and the other half has to move with it.
/// </summary>
public sealed class Inventory(PromuseDbContext db)
{
    /// <summary>
    /// Adds to a stack, creating it if the player had none.
    ///
    /// 用 upsert 而不是先读后写 / A raw upsert rather than read-then-insert,
    /// because two grants of the same item arriving together would both find no
    /// row and the second would fail on the primary key. ON CONFLICT makes that
    /// case an addition instead of an error.
    /// </summary>
    public Task CreditAsync(Guid accountId, int itemId, int amount, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO player_items (account_id, item_id, amount)
             VALUES ({accountId}, {itemId}, {amount})
             ON CONFLICT (account_id, item_id)
             DO UPDATE SET amount = player_items.amount + EXCLUDED.amount
             """, ct);

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
    public async Task<bool> TryDebitAsync(Guid accountId, int itemId, int amount, CancellationToken ct)
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
    /// Bumped whenever the save changes, so an ETag a client is holding stops
    /// matching and a 304 cannot show them an inventory they no longer have.
    /// </summary>
    public Task BumpStateVersionAsync(Guid accountId, DateTimeOffset now, CancellationToken ct) =>
        db.Players
            .Where(p => p.AccountId == accountId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.StateVersion, p => p.StateVersion + 1)
                .SetProperty(p => p.UpdatedAt, now), ct);
}
