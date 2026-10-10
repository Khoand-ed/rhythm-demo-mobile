namespace Promuse.Persistence.Entities;

/// <summary>
/// One headhunting banner, and the server's opinion of what a pull on it gives.
///
/// 规则在服务端 / The odds, the pity threshold, the price and the schedule live here and
/// nowhere else. The client's GachaBanner asset keeps only presentation - title, text, which
/// operator to draw large - because odds a client decides are odds a client can change.
///
/// 改概率不用发版 / A table rather than code, so opening a new banner or changing a price is
/// an INSERT or an UPDATE rather than a release.
/// </summary>
public class GachaBanner
{
    /// <summary>
    /// Stable and readable - "standard", "event_amiya". The client matches its art to this, and
    /// every player's pity count is keyed by it, so it is never renamed.
    /// </summary>
    public string BannerId { get; set; } = string.Empty;

    /// <summary>Null opens it from the start of time.</summary>
    public DateTimeOffset? StartsAt { get; set; }

    /// <summary>Null keeps it open for good - the standard banner.</summary>
    public DateTimeOffset? EndsAt { get; set; }

    /// <summary>Basis points; the three add up to exactly 10000.</summary>
    public int RateFiveStar { get; set; }

    public int RateFourStar { get; set; }

    public int RateThreeStar { get; set; }

    /// <summary>Pulls in a row without a 5-star after which the next pull is one.</summary>
    public int PityThreshold { get; set; }

    public int CurrencyItemId { get; set; }

    public int CostSingle { get; set; }

    public int CostMulti { get; set; }

    /// <summary>Spent first, one per pull, when the player has enough for the whole request.</summary>
    public int? TicketItemId { get; set; }

    /// <summary>Retiring a banner hides it without deleting the history that points at it.</summary>
    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public List<GachaPoolEntry> Pool { get; set; } = [];
}

/// <summary>
/// One operator a banner can give.
///
/// 星级跟着卡池 / The rarity is stored per banner entry rather than looked up from the client's
/// CharMeta, because it is what the odds are applied to - see <see cref="GachaBanner"/>.
/// </summary>
public class GachaPoolEntry
{
    public string BannerId { get; set; } = string.Empty;

    public GachaBanner? Banner { get; set; }

    public string CharacterId { get; set; } = string.Empty;

    public int Rarity { get; set; }

    /// <summary>What a copy the player already owns turns into - Purchase Certificates today.</summary>
    public int DuplicateItemId { get; set; }

    public int DuplicateAmount { get; set; }
}

/// <summary>
/// How many pulls in a row a player has made on one banner without a 5-star.
///
/// 每个卡池分开 / Per banner: luck on the event banner does nothing for the standard one, as
/// the Details screen promises.
/// </summary>
public class GachaPity
{
    public Guid AccountId { get; set; }

    public Player? Player { get; set; }

    public string BannerId { get; set; } = string.Empty;

    public int PullsSinceFiveStar { get; set; }
}

/// <summary>
/// One pull, kept after the fact - the headhunting history screen, and the answer to "what did
/// my 1800 Orundum get me".
///
/// 序号做主键 / A database sequence for the key rather than a Guid, because the history is read
/// newest first a page at a time, and an increasing number is both the order and the cursor.
/// </summary>
public class GachaPull
{
    public long Id { get; set; }

    public Guid AccountId { get; set; }

    public Player? Player { get; set; }

    /// <summary>The request this pull came from: a ten-pull is ten rows with one batch id.</summary>
    public Guid BatchId { get; set; }

    public string BannerId { get; set; } = string.Empty;

    public string CharacterId { get; set; } = string.Empty;

    public int Rarity { get; set; }

    public bool IsNew { get; set; }

    public bool Guaranteed { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
