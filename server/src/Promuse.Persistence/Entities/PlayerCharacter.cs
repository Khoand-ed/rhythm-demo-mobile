namespace Promuse.Persistence.Entities;

/// <summary>
/// One operator a player owns, and how far they have taken it. Mirrors
/// <c>Data.Char.CharData</c>.
///
/// 只存进度 / Only progression lives here. The artwork, the rarity, the stats and
/// the passive stay client-side content under Resources/Meta/Char, so rebalancing
/// an operator is a content update rather than a database migration - and the
/// server never has to know what a 5-star is.
/// </summary>
public class PlayerCharacter
{
    public Guid AccountId { get; set; }

    public Player? Player { get; set; }

    /// <summary>The id from Resources/Meta/Char - AMIYA, NOVA, ECHO, PULSE.</summary>
    public string CharacterId { get; set; } = string.Empty;

    public int Elite { get; set; }

    public int Level { get; set; } = 1;

    public int Exp { get; set; }

    public int Trust { get; set; }
}
