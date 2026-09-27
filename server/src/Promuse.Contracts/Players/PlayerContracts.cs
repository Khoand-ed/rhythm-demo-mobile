namespace Promuse.Contracts.Players;

/// <summary>
/// Everything the Home screen binds to, in one response.
/// </summary>
/// <param name="ExpToNextLevel">
/// Computed server-side from the level rather than recomputed by the client, so
/// retuning the curve needs no app update and the two can never disagree.
/// </param>
/// <param name="StateVersion">
/// Increments on every accepted write, and what the ETag encodes. In the body as
/// well as the header so a conflict can be explained to the player rather than
/// only handled by the transport.
/// </param>
public sealed record PlayerState(
    Guid PlayerId,
    string DisplayName,
    int Level,
    int Exp,
    int ExpToNextLevel,
    Stamina Stamina,
    IReadOnlyList<CharacterState> Characters,
    IReadOnlyList<string?> Squad,
    string? DesktopCharacterId,
    IReadOnlyList<ItemStack> Inventory,
    int StateVersion,
    DateTimeOffset ServerTime);

/// <summary>
/// <c>PlayerData.reason</c> (理智).
/// </summary>
/// <param name="Current">
/// Computed at read time from the stored value and the moment it was true, never
/// stored as a number that ticks. The only thing that knows time has passed on a
/// phone is the phone.
/// </param>
/// <param name="NextPointAt">
/// When the next point lands, or null at full. 客户端自己倒数 / This and
/// <paramref name="FullAt"/> exist so the client can render a countdown without
/// asking again - which is also why a 304 on this resource is not a problem.
/// </param>
public sealed record Stamina(
    int Current,
    int Max,
    DateTimeOffset? NextPointAt,
    DateTimeOffset? FullAt);

/// <summary>Mirrors <c>Data.Char.CharData</c>.</summary>
public sealed record CharacterState(
    string CharacterId,
    int Elite,
    int Level,
    int Exp,
    int Trust);

/// <summary>Mirrors <c>Data.Item.ItemStack</c>.</summary>
public sealed record ItemStack(int ItemId, int Amount);

/// <param name="Squad">
/// Exactly four entries, null for an empty slot - the shape
/// <c>PlayerData.GetSquad()</c> normalises to. Fixed length rather than a list of
/// the operators chosen, because the slot position is meaningful.
/// </param>
public sealed record SquadRequest(IReadOnlyList<string?> Squad);

/// <param name="CharacterId">Null clears it. Must be a character the player owns.</param>
public sealed record DesktopCharacterRequest(string? CharacterId);
