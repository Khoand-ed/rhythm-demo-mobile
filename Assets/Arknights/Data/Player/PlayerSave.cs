using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Data.Item;
using UnityEngine;

namespace Data.Player {
    /// <summary>
    /// PlayerData 的存档 / On-disk save for the mutable half of PlayerData.
    ///
    /// PlayerData 是 Resources 里的 ScriptableObject, 在设备上是只读的 / PlayerData is loaded out of
    /// Resources, and a Resources asset is read-only in a player build. AddItem/TakeItem therefore
    /// only ever touched the in-memory copy: every launch reset the bag to PlayerData.Initialization
    /// plus whatever the seeded .asset carried. In the Editor the mutation appears to stick until the
    /// next reimport, which is why nothing caught it until DepotUI started listing the inventory.
    ///
    /// 存档目录由 companyName/productName 决定 / The save lives under Application.persistentDataPath,
    /// which resolves to AppData/LocalLow/&lt;companyName&gt;/&lt;productName&gt; - currently 67AB/Promuse.
    /// Renaming either in Player Settings orphans every existing save exactly the way it orphans a
    /// deployed AssetBundle: the file stays on disk under the old name and nothing ever looks for it
    /// again. There is no equivalent of "Deploy To Persistent Data Path" to rescue a save, so a
    /// rename costs the player their inventory.
    /// </summary>
    [Serializable]
    public class PlayerSave {

        // 存档格式版本 / Layout version. A file from a newer build is left alone rather than
        // half-read; bump this and handle the old shape when a field's meaning changes.
        public const int CurrentVersion = 1;

        // 存档子目录 / Subfolder under persistentDataPath, so the saves sit apart from the
        // AssetBundles ABManager deploys into the same root.
        private const string Folder = "Save";

        public int version = CurrentVersion;
        public int level;
        public int exp;
        public int reason;
        public List<SavedItem> items = new List<SavedItem>();

        /// <summary>
        /// ItemStack 的存档形式 / The on-disk form of an ItemStack.
        ///
        /// JsonUtility 直接序列化 ItemStack 也可以 / JsonUtility can serialise ItemStack directly:
        /// a List&lt;ItemStack&gt; round-trips fine, and PlayerData itself is the proof. This separate
        /// type exists so the save format does not inherit a gameplay type's private field names -
        /// renaming id or amount on ItemStack would otherwise invalidate every save on disk, and
        /// any field added there for gameplay would start being written into saves by accident.
        /// </summary>
        [Serializable]
        public class SavedItem {
            public int id;
            public int amount;
        }

        /// <summary>
        /// 存档路径 / Absolute path of one player's save.
        ///
        /// 玩家名做文件名, 非法字符换成下划线 / Keyed by player name with the characters the
        /// filesystem rejects replaced by '_'. Two names differing only in those characters would map
        /// to one file; the accounts that exist are plain ASCII, so that is left as a known limit
        /// rather than hidden behind a hash that makes the folder unreadable.
        /// </summary>
        public static string PathFor(string playerName) {
            string fileName = FileNameFor(playerName);
            if (fileName == null) return null;
            return Application.persistentDataPath + "/" + Folder + "/" + fileName + ".json";
        }

        /// <summary>
        /// 把 PlayerData 写进存档 / Writes the mutable fields of data to disk.
        /// </summary>
        public static void Save(PlayerData data) {
            if (data == null) return;

            string path = PathFor(data.GetName());
            if (path == null) {
                Debug.LogError("[PlayerSave] 玩家名为空, 无处存档 / PlayerData has no usable name. " +
                               "The save is keyed by name, so there is nowhere to write it and this " +
                               "player's inventory will not survive a restart.");
                return;
            }

            PlayerSave save = new PlayerSave {
                version = CurrentVersion,
                level = data.GetLevel(),
                exp = data.GetExp(),
                reason = data.GetReason()
            };

            List<ItemStack> stacks = data.GetItems();
            if (stacks != null) {
                foreach (ItemStack stack in stacks) {
                    // TakeItem 清掉归零的堆, 但别把坏数据写进存档 / TakeItem already drops emptied
                    // stacks; skipping them here keeps a bad in-memory value out of the file too.
                    if (stack == null || stack.GetAmount() <= 0) continue;
                    save.items.Add(new SavedItem { id = stack.GetId(), amount = stack.GetAmount() });
                }
            }

            try {
                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

                // 先写临时文件再替换 / Write beside the real file and move it into place. A kill or a
                // flat battery partway through a direct write leaves a truncated save that reads back
                // as an empty bag; this way the previous save survives instead.
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(save), new UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            } catch (Exception e) {
                Debug.LogError($"[PlayerSave] 存档写入失败 / Could not write {path}: {e.Message}");
            }
        }

        /// <summary>
        /// 把存档盖到 PlayerData 上 / Layers a save over data, leaving it untouched when there is no
        /// file yet. Returns true only when something was actually applied.
        ///
        /// The seeded .asset is the starting point: first login finds no file, keeps the seed, and the
        /// first mutation writes one. From then on the save is authoritative for the fields it covers,
        /// so items added to the seeded asset later will not reach a player who already has a save.
        /// That is the price of not resurrecting items they already spent.
        /// </summary>
        public static bool Load(PlayerData data) {
            if (data == null) return false;

            string path = PathFor(data.GetName());
            if (path == null || !File.Exists(path)) return false;

            PlayerSave save;
            try {
                save = JsonUtility.FromJson<PlayerSave>(File.ReadAllText(path));
            } catch (Exception e) {
                Debug.LogError($"[PlayerSave] 存档读取失败 / Could not read {path}: {e.Message}\n" +
                               "  Falling back to the seeded PlayerData asset.");
                return false;
            }

            if (save == null) {
                Debug.LogError($"[PlayerSave] 存档为空 / {path} parsed to nothing. " +
                               "Falling back to the seeded PlayerData asset.");
                return false;
            }

            if (save.version > CurrentVersion) {
                Debug.LogWarning($"[PlayerSave] 存档版本 {save.version} 高于本体 {CurrentVersion} / " +
                                 $"{path} was written by a newer build. Leaving it alone rather than " +
                                 "reading it half-right and overwriting it on the next save.");
                return false;
            }

            data.SetLevel(Mathf.Clamp(save.level, 0, data.GetMaxLevel()));
            data.SetExp(Mathf.Max(0, save.exp));
            // 理智可以超上限 (吃药), 所以只挡负数 / Sanity can legitimately exceed its cap, so only
            // negatives are rejected here.
            data.SetReason(Mathf.Max(0, save.reason));

            // 走 AddItem 而不是直接塞列表 / Rebuild through AddItem rather than assigning the list:
            // it merges any duplicate id a corrupt file might carry and leaves the list sorted.
            data.SetItems(new List<ItemStack>());
            if (save.items != null) {
                foreach (SavedItem item in save.items) {
                    if (item == null || item.amount <= 0) continue;
                    data.AddItem(item.id, item.amount);
                }
            }
            data.ItemSort();
            return true;
        }

        /// <summary>
        /// 文件名, 非法字符换成下划线 / The player name reduced to something the filesystem accepts.
        /// Returns null when nothing usable is left.
        /// </summary>
        private static string FileNameFor(string playerName) {
            if (string.IsNullOrEmpty(playerName)) return null;

            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder builder = new StringBuilder(playerName.Length);
            foreach (char c in playerName) {
                builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }

            string fileName = builder.ToString().Trim();
            return fileName.Length == 0 ? null : fileName;
        }
    }
}
