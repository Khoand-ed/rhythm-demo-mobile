using System;
using System.Collections.Generic;
using Tools;
using UnityEngine;

namespace Promuse.Manufacture {
    /// <summary>
    /// 房间里的台词 / What the operators in the Manufacture room say when tapped: a few lines each, in
    /// English, and some for anyone with none of their own.
    ///
    /// 是数据不是代码 / Lines are data: a tracked asset in Assets/Manufacture/Resources/Manufacture/,
    /// edited in the Inspector, loaded through Asset.Load so the resource download can move it into a
    /// bundle later. Created with its first lines by Arknights/Manufacture/Build Manufacture UI and kept
    /// as it is after that.
    /// </summary>
    public class ManufactureLines : ScriptableObject {
        /// <summary>Asset.Load's path: the folder under Resources, and the bundle it may move to.</summary>
        public const string LoadPath = "Manufacture";
        public const string AssetName = "ManufactureLines";

        [Serializable]
        public class Entry {
            [Tooltip("The CharMeta asset name, e.g. AMIYA.")]
            public string charId;

            [TextArea(1, 3)]
            public string[] lines = Array.Empty<string>();
        }

        [Tooltip("Said by an operator with no lines of its own below.")]
        [TextArea(1, 3)]
        public string[] fallback = Array.Empty<string>();

        public List<Entry> entries = new List<Entry>();

        /// <summary>The lines asset, or null when it is missing (Asset.Load reports that).</summary>
        public static ManufactureLines Load() => Asset.Load<ManufactureLines>(LoadPath, AssetName);

        /// <summary>The operator's own lines, or the fallback when they have none.</summary>
        public IReadOnlyList<string> For(string charId) {
            foreach (Entry entry in entries) {
                if (entry != null && entry.lines != null && entry.lines.Length > 0 &&
                    string.Equals(entry.charId, charId, StringComparison.OrdinalIgnoreCase)) {
                    return entry.lines;
                }
            }

            return fallback ?? Array.Empty<string>();
        }

        /// <summary>
        /// A line to say: any but <paramref name="last"/> while there is another to choose, null when there
        /// is nothing to say. <paramref name="roll"/> picks an index below its argument -
        /// UnityEngine.Random.Range in the room, which the server never replays.
        /// </summary>
        public static string Pick(IReadOnlyList<string> lines, string last, Func<int, int> roll) {
            if (lines == null) return null;

            List<string> choices = new List<string>();
            foreach (string line in lines) {
                if (!string.IsNullOrWhiteSpace(line)) choices.Add(line);
            }

            if (choices.Count == 0) return null;
            if (last != null && choices.Exists(line => line != last)) choices.RemoveAll(line => line == last);

            int index = roll(choices.Count);
            return choices[Mathf.Clamp(index, 0, choices.Count - 1)];
        }
    }
}
