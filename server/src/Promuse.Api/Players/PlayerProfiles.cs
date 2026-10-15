namespace Promuse.Api.Players;

/// <summary>
/// The rules behind a profile card, kept apart from the queries so they can be tested without a
/// database.
/// </summary>
public static class PlayerProfiles
{
    /// <summary>One owned operator, as much of it as choosing an assistant needs.</summary>
    public readonly record struct Owned(string CharacterId, int Elite, int Level, int Trust);

    /// <summary>
    /// 名片上站着谁 / Who stands on the card. The desktop character is the player's own choice and
    /// wins whenever it is set. Until a screen sets it, the squad leader stands in - the operator
    /// the player plays with - and failing that the one raised furthest: elite phase, then level,
    /// then trust. The id breaks a tie so the same player always shows the same operator.
    /// </summary>
    public static string? AssistantOf(string? desktopCharacterId, IEnumerable<string?> squad, IEnumerable<Owned> owned)
    {
        if (desktopCharacterId is not null) return desktopCharacterId;

        string? leader = squad.FirstOrDefault(id => id is not null);
        if (leader is not null) return leader;

        return owned
            .OrderByDescending(o => o.Elite)
            .ThenByDescending(o => o.Level)
            .ThenByDescending(o => o.Trust)
            .ThenBy(o => o.CharacterId, StringComparer.Ordinal)
            .Select(o => o.CharacterId)
            .FirstOrDefault();
    }
}
