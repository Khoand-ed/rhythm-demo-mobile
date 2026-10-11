namespace Promuse.Persistence.Entities;

/// <summary>
/// Who someone signs in as. Separate from <see cref="Player"/> because the two
/// answer different questions and change for different reasons: an account is
/// identity and can be banned or have its credentials rotated, a player is
/// progress and is what the game reads every time the Home screen opens.
///
/// Both nullable credential columns are the guest path from the contract: a
/// device signs in with no username at all, and <c>/v1/auth/link</c> fills them
/// in later without the player id changing. Making them required would mean a
/// sign-up wall before the first note.
/// </summary>
public class Account
{
    public Guid Id { get; set; }

    /// <summary>
    /// Stable per install. Unique, because the contract promises that a second
    /// guest sign-in from the same device reopens the same session rather than
    /// creating another player - the uniqueness is enforced here, not trusted
    /// from the request.
    /// </summary>
    public string? DeviceId { get; set; }

    public string? Username { get; set; }

    /// <summary>
    /// Argon2id. Never the password, and never logged - the column exists so
    /// that a verification can happen, not so that anything can be read back.
    /// </summary>
    public string? PasswordHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Null while in good standing. A nullable timestamp rather than a bool, so
    /// the ban carries its own date without a second column that can disagree
    /// with it.
    /// </summary>
    public DateTimeOffset? BannedAt { get; set; }

    /// <summary>
    /// May edit remote config. Read from here on every admin request rather than carried in the
    /// access token, the same way nothing else the server decides on is put in a token: revoking
    /// it takes effect on the next request, not when the token expires. Granted by hand - see
    /// SETUP.md - because no endpoint should be able to mint the first admin.
    /// </summary>
    public bool IsAdmin { get; set; }

    public Player? Player { get; set; }
}
