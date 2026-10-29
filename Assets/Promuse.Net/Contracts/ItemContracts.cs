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

    /// <summary>
    /// 钥匙道具 / Items that are held, never spent: having one opens something. The Manufacture
    /// blueprints are paid once, on a story stage's first clear.
    ///
    /// 不能花 / Nothing may take one away. The server hands a blueprint back to anyone who has cleared
    /// its stage but does not hold it - which is how players who cleared it before Manufacture existed
    /// get theirs - so a key item that could be spent would be spent, handed back and spent again,
    /// without end. Every debit refuses these ids, and the admin page refuses them as a price.
    /// </summary>
    public static class KeyItems
    {
        public const int ManufactureBlueprint1 = 30;
        public const int ManufactureBlueprint2 = 31;
        public const int ManufactureBlueprint3 = 32;

        public static readonly IReadOnlyList<int> All = new[]
        {
            ManufactureBlueprint1,
            ManufactureBlueprint2,
            ManufactureBlueprint3,
        };

        public static bool Contains(int itemId)
        {
            foreach (int id in All)
            {
                if (id == itemId) return true;
            }

            return false;
        }
    }
}
