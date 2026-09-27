namespace Promuse.Persistence.Entities;

/// <summary>
/// One of the four squad positions. Mirrors the fixed-length array
/// <c>PlayerData.GetSquad()</c> normalises to.
///
/// 槽位是数据的一部分 / Four rows with an explicit slot number rather than a list
/// of the operators chosen, because the position is meaningful: slot 0 is not
/// "the first one picked", it is a place on the screen. A list would lose which
/// gap an empty slot left behind.
/// </summary>
public class SquadSlot
{
    public Guid AccountId { get; set; }

    public Player? Player { get; set; }

    /// <summary>0 to 3. Always all four rows, even when every one is empty.</summary>
    public int Slot { get; set; }

    /// <summary>Null for an empty slot.</summary>
    public string? CharacterId { get; set; }
}
