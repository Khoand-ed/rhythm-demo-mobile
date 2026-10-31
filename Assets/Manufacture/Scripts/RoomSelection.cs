using System;
using System.Collections.Generic;
using Data.Player;
using UnityEngine;

namespace Promuse.Manufacture {
    /// <summary>
    /// 房间里站谁 / Who stands in the Manufacture room: up to five of the operators the player owns, as
    /// chosen on this device. The rules are pure, so the tests can hold them; <see cref="RoomStore"/> keeps
    /// the choice.
    ///
    /// 选择只在本机 / The choice is the device's, not the account's: another phone starts from the
    /// default. That costs nothing on the server, and who stands in the room changes no number.
    /// </summary>
    public static class RoomSelection {
        /// <summary>How many operators the room holds.</summary>
        public const int Capacity = 5;

        /// <summary>Never chosen on this device: the operators owned, in the order the save lists them, up to five.</summary>
        public static List<string> Default(IEnumerable<string> owned) {
            List<string> room = new List<string>();
            if (owned == null) return room;

            foreach (string id in owned) {
                if (room.Count == Capacity) break;
                if (!string.IsNullOrEmpty(id) && !room.Contains(id)) room.Add(id);
            }

            return room;
        }

        /// <summary>
        /// Who stands: the saved choice where there is one, the default where there is none (null).
        ///
        /// 对不上的就跳过 / An operator chosen and no longer owned, or unknown to this build, is left out
        /// here, and drops out of the list the next time it is saved. An empty choice is a choice:
        /// nobody stands.
        /// </summary>
        public static List<string> Resolve(IReadOnlyList<string> saved, IEnumerable<string> owned) {
            if (saved == null) return Default(owned);

            HashSet<string> have = new HashSet<string>(owned ?? Array.Empty<string>(), StringComparer.Ordinal);
            List<string> room = new List<string>();
            foreach (string id in saved) {
                if (room.Count == Capacity) break;
                if (!string.IsNullOrEmpty(id) && have.Contains(id) && !room.Contains(id)) room.Add(id);
            }

            return room;
        }

        /// <summary>
        /// A tile pressed in the picker: takes the operator out if chosen, or adds them at the end. False,
        /// with nothing changed, when the room is already full.
        /// </summary>
        public static bool Toggle(List<string> chosen, string id) {
            if (chosen.Remove(id)) return true;
            if (chosen.Count >= Capacity) return false;

            chosen.Add(id);
            return true;
        }
    }

    /// <summary>
    /// 每个服务器各存各的 / The room's choice on this device, per server: a PlayerPrefs key made with
    /// Api.Scoped, so a Staging build never shows the operators chosen against Production.
    /// </summary>
    public sealed class RoomStore {
        /// <summary>The key before scoping.</summary>
        public const string Key = "Promuse.Manufacture.Room";

        private readonly string key;

        /// <param name="scopedKey">The key already scoped to a server; see <see cref="ForCurrentServer"/>.</param>
        public RoomStore(string scopedKey) {
            key = scopedKey;
        }

        /// <summary>The store for the server this build signs in to.</summary>
        public static RoomStore ForCurrentServer() => new RoomStore(PlayerManager.Inst().Api.Scoped(Key));

        /// <summary>The saved choice in order, or null when nothing has ever been chosen here.</summary>
        public List<string> Load() {
            if (!PlayerPrefs.HasKey(key)) return null;

            List<string> ids = new List<string>();
            foreach (string id in PlayerPrefs.GetString(key, "").Split(',')) {
                string trimmed = id.Trim();
                if (trimmed.Length > 0) ids.Add(trimmed);
            }

            return ids;
        }

        /// <summary>Keeps a choice, empty included.</summary>
        public void Save(IEnumerable<string> ids) {
            PlayerPrefs.SetString(key, string.Join(",", ids ?? Array.Empty<string>()));
            PlayerPrefs.Save();
        }
    }
}
