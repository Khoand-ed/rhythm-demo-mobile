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

    /// <summary>
    /// 游戏里的名字 / The name everyone else sees, never the username - that one is for signing
    /// in. Null from signing up until the player chooses it on the name screen
    /// (<c>PlayerService.SetNameAsync</c>), and kept for good after. A guest is named at once,
    /// "Doctor" and a number. Nothing that shows a player to others - a leaderboard line, a
    /// signature, a report - can be made without it, so only the owner ever sees it null.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// 名字后面的四位数 / The four digits after the name, as in <c>Amiya#0420</c>. Two players may
    /// share a display name - every guest is "Doctor" and a number - but never a name and a tag, so
    /// the pair is what tells them apart on a profile. Drawn with the name, from the numbers that
    /// name has not used yet, and never changed after. Null exactly when the name is.
    /// </summary>
    public int? Tag { get; set; }

    /// <summary>
    /// The line the player writes on their profile, which everyone who opens it reads. Null when
    /// there is none. Stored only as <c>PlayerProfiles.NormalizeSignature</c> leaves it.
    /// </summary>
    public string? Signature { get; set; }

    /// <summary>
    /// When the player last changed <see cref="Signature"/>. Orders the moderators' list of recent
    /// lines; null for a player who never wrote one.
    /// </summary>
    public DateTimeOffset? SignatureUpdatedAt { get; set; }

    /// <summary>
    /// 管理员锁 / Set by a moderator: until then the player cannot write a signature. Null, or a
    /// moment already past, is no lock; <c>SignatureLock.Permanent</c> is one that never lifts.
    /// What was done and why is in <see cref="SignatureModeration"/>.
    /// </summary>
    public DateTimeOffset? SignatureLockedUntil { get; set; }

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
