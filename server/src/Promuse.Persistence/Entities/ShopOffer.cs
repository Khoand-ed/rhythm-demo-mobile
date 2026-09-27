namespace Promuse.Persistence.Entities;

/// <summary>
/// One row of the shop, and the server's opinion of what it costs.
///
/// 价格必须在服务端 / The price lives here and nowhere else. The client used to
/// read it from ShopItemDataList.asset and do the arithmetic itself, which meant
/// the number a player paid was a number their device chose. Moving the catalog
/// here is most of what "server-authoritative economy" means in practice.
///
/// 改价不用发版 / It is a table rather than configuration baked into the build,
/// so a price change is an UPDATE rather than a store release.
/// </summary>
public class ShopOffer
{
    public Guid Id { get; set; }

    /// <summary>What the player receives. An id from Resources/Meta/Item.</summary>
    public int SellItemId { get; set; }

    public int SellAmount { get; set; }

    /// <summary>What they pay with - today every offer is priced in 6, Orirock.</summary>
    public int PriceItemId { get; set; }

    public int PriceAmount { get; set; }

    /// <summary>
    /// Retiring an offer hides it without deleting it, so the purchases that
    /// reference it keep something to point at.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// The order the shop screen draws them in. Explicit because otherwise the
    /// row order is whatever the planner felt like and the shop reshuffles
    /// itself between visits.
    /// </summary>
    public int SortOrder { get; set; }
}
