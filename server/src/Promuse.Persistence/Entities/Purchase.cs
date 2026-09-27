namespace Promuse.Persistence.Entities;

/// <summary>
/// One completed purchase, kept after the fact.
///
/// 为什么要留痕 / The inventory alone cannot answer "where did my Orirock go",
/// and that is the question a player asks when something goes wrong. It is also
/// the only way to tell a duplication bug from a generous player: without a
/// ledger, an inventory that looks wrong is just an inventory that looks wrong.
///
/// 记下当时的价格 / The price is copied in rather than read back through
/// ShopOfferId, because the offer's price can change afterwards and this has to
/// stay a record of what was actually paid.
/// </summary>
public class Purchase
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Player? Player { get; set; }

    public Guid ShopOfferId { get; set; }

    public ShopOffer? Offer { get; set; }

    /// <summary>How many of the offer were bought in this one checkout.</summary>
    public int Quantity { get; set; }

    public int SellItemId { get; set; }

    public int SellAmountTotal { get; set; }

    public int PriceItemId { get; set; }

    public int PriceAmountTotal { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
