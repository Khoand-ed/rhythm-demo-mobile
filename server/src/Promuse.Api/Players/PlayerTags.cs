using Microsoft.EntityFrameworkCore;
using Promuse.Persistence;

namespace Promuse.Api.Players;

/// <summary>
/// 名字后的编号 / The number after a player's name, <c>#0000</c> to <c>#9999</c>.
///
/// 按名字分开编 / Numbered per name, not across the game: ten thousand players may all be called
/// "Doctor" and each still has a tag of four digits. The unique index on (display_name, tag) is the
/// guarantee; drawing from the numbers a name has not used only keeps that index from refusing a
/// sign-up in practice.
/// </summary>
public static class PlayerTags
{
    public const int Count = 10_000;

    /// <summary>
    /// A tag <paramref name="displayName"/> does not have yet, drawn at random from the free ones.
    /// Null when the name has used all ten thousand.
    /// </summary>
    public static async Task<int?> DrawAsync(PromuseDbContext db, string displayName, CancellationToken ct)
    {
        HashSet<int> used = [.. await db.Players.AsNoTracking()
            .Where(p => p.DisplayName == displayName)
            .Select(p => p.Tag!.Value)
            .ToListAsync(ct)];

        if (used.Count >= Count) return null;

        return FreeAt(used, Random.Shared.Next(Count - used.Count));
    }

    /// <summary>
    /// The <paramref name="index"/>-th number in [0, <see cref="Count"/>) that is not in
    /// <paramref name="used"/>, counting from zero. Drawing the index uniformly over the free count
    /// is what makes every free tag equally likely, however crowded the name is.
    /// </summary>
    public static int FreeAt(IReadOnlySet<int> used, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        for (int tag = 0; tag < Count; tag++)
        {
            if (used.Contains(tag)) continue;
            if (index-- == 0) return tag;
        }

        throw new ArgumentOutOfRangeException(nameof(index), "There are fewer free tags than that.");
    }
}
