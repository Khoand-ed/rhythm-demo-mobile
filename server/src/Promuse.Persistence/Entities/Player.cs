namespace Promuse.Persistence.Entities;

/// <summary>
/// The save that <c>PlayerData</c> never had. Phase 0 carries only what
/// <c>GET /v1/players/me</c> needs to answer at all; characters, inventory and
/// the squad arrive with Phase 2, each as its own table rather than a JSON blob
/// in this one - an inventory that has to be read and written atomically under
/// a purchase is a row, not a document.
/// </summary>
public class Player
{
    /// <summary>Shared with <see cref="Account.Id"/>: one account, one player.</summary>
    public Guid AccountId { get; set; }

    public Account? Account { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public int Level { get; set; } = 1;

    public int Exp { get; set; }

    /// <summary>
    /// <c>PlayerData.reason</c> (理智). Stored as the value at
    /// <see cref="StaminaUpdatedAt"/>, never as a number that ticks: the API
    /// computes what it is *now* from those two and the server clock.
    ///
    /// 这是防作弊的一半 / That is the whole anti-cheat argument for this field.
    /// A stored countdown has to be advanced by something, and the only thing
    /// that knows time has passed on a phone is the phone.
    /// </summary>
    public int Stamina { get; set; }

    public DateTimeOffset StaminaUpdatedAt { get; set; }

    public string? DesktopCharacterId { get; set; }

    /// <summary>
    /// Bumped on every accepted write, and what the ETag encodes. This is the
    /// column that makes <c>If-Match</c> mean something: two devices writing
    /// from the same read will disagree here, and the second one is refused with
    /// 412 instead of silently winning.
    /// </summary>
    public int StateVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<PlayerCharacter> Characters { get; set; } = [];

    public ICollection<PlayerItem> Items { get; set; } = [];

    /// <summary>Always four rows. See <see cref="SquadSlot"/>.</summary>
    public ICollection<SquadSlot> Squad { get; set; } = [];
}
