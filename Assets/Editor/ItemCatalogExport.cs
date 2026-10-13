using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Data.Item;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 物品目录 / Writes every ItemMeta under Resources/Meta/Item to server/data/items.json - id, name
/// and rarity - which the server serves to the admin page, so every item field there is a picker
/// reading "1 · Orundum" instead of a bare number.
///
/// 三处对得上 / Kept in step three ways: ci/check_items.py fails a pull request whose items.json no
/// longer matches the ItemMetas, and the API tests (ItemCatalogTests) fail when the server seeds an
/// item that is not in it. Run this after adding or renaming an ItemMeta.
///
/// 没变就不写 / The file is rewritten only when its content changes, so running this on an unchanged
/// project leaves git clean. There is nothing to reset: the ItemMetas are the source.
/// </summary>
public static class ItemCatalogExport
{
    private const string ItemFolder = "Assets/Arknights/Resources/Meta/Item";
    private const string Target = "server/data/items.json";

    [MenuItem("Arknights/Depot/Export Item Catalog", false, 30)]
    public static void Export()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        var items = new List<ItemMeta>();
        foreach (string guid in AssetDatabase.FindAssets("t:ItemMeta", new[] { ItemFolder }))
        {
            ItemMeta meta = AssetDatabase.LoadAssetAtPath<ItemMeta>(AssetDatabase.GUIDToAssetPath(guid));
            if (meta != null) items.Add(meta);
        }

        // 同号两件 / Two ItemMetas claiming one id would make the picker - and the game - ambiguous.
        foreach (IGrouping<int, ItemMeta> clash in items.GroupBy(i => i.GetID()).Where(g => g.Count() > 1))
        {
            Debug.LogError($"[ItemCatalogExport] Item id {clash.Key} is claimed by " +
                           string.Join(" and ", clash.Select(AssetDatabase.GetAssetPath)) + ". Nothing written.");
            return;
        }

        items.Sort((a, b) => a.GetID().CompareTo(b.GetID()));
        string json = Write(items);
        string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, Target);

        bool unchanged = File.Exists(path) && File.ReadAllText(path) == json;
        if (!unchanged)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        Debug.Log($"[ItemCatalogExport] {items.Count} item(s) from {ItemFolder} -> {Target} " +
                  $"({(unchanged ? "unchanged" : "written")})\n  " +
                  string.Join("\n  ", items.Select(i => $"{i.GetID()} · {i.GetName()} (rarity {i.GetRarity()})")));
    }

    // 一行一件 / One item per line, ids in order: a change to one item is a one-line diff.
    private static string Write(List<ItemMeta> items)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"source\": \"").Append(ItemFolder)
          .Append(" - written by Arknights/Depot/Export Item Catalog, do not edit by hand\",\n");
        sb.Append("  \"items\": [\n");
        for (int i = 0; i < items.Count; i++)
        {
            ItemMeta item = items[i];
            sb.Append("    { \"id\": ").Append(item.GetID())
              .Append(", \"name\": ").Append(Quote(item.GetName()))
              .Append(", \"rarity\": ").Append(item.GetRarity())
              .Append(" }").Append(i < items.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("  ]\n}\n");
        return sb.ToString();
    }

    private static string Quote(string text)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in text ?? "")
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
