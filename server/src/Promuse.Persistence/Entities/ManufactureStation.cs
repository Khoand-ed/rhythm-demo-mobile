namespace Promuse.Persistence.Entities;

/// <summary>
/// 制造站 / One of a player's Manufacture stations: its level and the moment its production is
/// counted from - nothing else.
///
/// 不存数量 / What it holds is never stored. Like Sanity, the store is worked out from the anchor
/// and the time (ManufactureClock), so nothing ticks in the database and a row is only written
/// when the player collects or raises the station.
/// </summary>
public class ManufactureStation
{
    public Guid AccountId { get; set; }

    /// <summary>lmd or exp - ManufactureStations.</summary>
    public string StationId { get; set; } = string.Empty;

    public int Level { get; set; }

    /// <summary>Always a whole second: see ManufactureClock.</summary>
    public DateTimeOffset AnchorAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
