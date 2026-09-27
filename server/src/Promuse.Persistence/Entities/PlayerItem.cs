namespace Promuse.Persistence.Entities;

/// <summary>
/// One stack in a player's bag. Mirrors <c>Data.Item.ItemStack</c>.
///
/// 一行一种物品, 不是一个 JSON / A row per item rather than a JSON document on the
/// player, because Phase 3 has to spend from this under a purchase. Debiting a
/// currency is an UPDATE with a WHERE that refuses to go negative; against a blob
/// it is read-modify-write, which is the shape every duplication bug takes.
/// </summary>
public class PlayerItem
{
    public Guid AccountId { get; set; }

    public Player? Player { get; set; }

    /// <summary>Ids from Resources/Meta/Item - 1 Orundum, 2 LMD, 6 Orirock.</summary>
    public int ItemId { get; set; }

    /// <summary>
    /// Always positive. A stack that reaches zero is deleted rather than kept,
    /// matching what <c>PlayerData.TakeItem</c> does on the client.
    /// </summary>
    public int Amount { get; set; }
}
