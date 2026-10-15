using System.Globalization;
using System.Text;

namespace Promuse.Api.Players;

/// <summary>
/// The rules behind a profile card, kept apart from the queries so they can be tested without a
/// database.
/// </summary>
public static class PlayerProfiles
{
    /// <summary>
    /// 按看到的字数算 / Counted as a reader counts: one per character on screen, so an accented
    /// Vietnamese letter or an emoji is one, however many UTF-16 units it takes.
    /// </summary>
    public const int SignatureMaxLength = 60;

    /// <summary>The column's own bound (PromuseDbContext), in UTF-16 units.</summary>
    public const int SignatureMaxStored = 256;

    /// <summary>
    /// 别人会读到的字 / What the player wrote, made fit to show on someone else's screen: one line,
    /// every kind of white space as a single plain space, and nothing invisible left in it.
    /// Invisible means control and format characters - a right-to-left override can make a line
    /// read backwards on every card it is shown on - plus unpaired surrogates, private-use and
    /// unassigned code points. Combining marks stay: they are half of a Vietnamese letter.
    ///
    /// 不在这里合成 / Not composed to NFC here. The server runs with InvariantGlobalization, where
    /// String.Normalize leaves anything past ASCII exactly as it was, so a call here would only look
    /// like it did something. The game composes before it sends (PlayerManager.SetSignatureAsync),
    /// and the length is counted by what a reader sees either way.
    ///
    /// Blank clears the signature: returns null with no error. Too long returns an error and no
    /// value - it is refused, never cut, because a cut line says something the player did not write.
    /// </summary>
    public static (string? Value, string? Error) NormalizeSignature(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);

        var clean = new StringBuilder(raw.Length);
        bool space = false;

        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];

            if (char.IsWhiteSpace(c))
            {
                space = clean.Length > 0;
                continue;
            }

            if (char.IsHighSurrogate(c) && i + 1 < raw.Length && char.IsLowSurrogate(raw[i + 1]))
            {
                if (!Visible(CharUnicodeInfo.GetUnicodeCategory(raw, i))) { i++; continue; }

                if (space) clean.Append(' ');
                space = false;
                clean.Append(c).Append(raw[++i]);
                continue;
            }

            if (!Visible(CharUnicodeInfo.GetUnicodeCategory(c))) continue;

            if (space) clean.Append(' ');
            space = false;
            clean.Append(c);
        }

        if (clean.Length == 0) return (null, null);

        string value = clean.ToString();

        if (new StringInfo(value).LengthInTextElements > SignatureMaxLength || value.Length > SignatureMaxStored)
            return (null, $"At most {SignatureMaxLength} characters.");

        return (value, null);
    }

    private static bool Visible(UnicodeCategory category) => category is not (
        UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate
        or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned);

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
