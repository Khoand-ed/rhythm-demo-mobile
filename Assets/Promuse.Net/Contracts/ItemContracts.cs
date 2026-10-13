// Compiled by both sides - see the note at the top of ApiProblem.cs. C# 9 syntax
// is deliberate: Unity 6 compiles at that level.

#nullable enable

using System.Collections.Generic;

namespace Promuse.Contracts.Items
{
    /// <summary>
    /// 物品目录 / One item the game knows: an ItemMeta, as Arknights/Depot/Export Item Catalog writes
    /// it to server/data/items.json.
    /// </summary>
    public sealed record CatalogItem(int ItemId, string Name, int Rarity);

    /// <summary>Every item, by id - what the admin page's item pickers offer.</summary>
    public sealed record ItemCatalogList(IReadOnlyList<CatalogItem> Items);
}
