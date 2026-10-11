using System;
using System.Collections.Generic;
using System.Globalization;
using Data.Char;
using Data.Item;
using Settings;
using UnityEngine;

namespace UI {
    /// <summary>
    /// 物品和头像怎么显示 / How an item id or an operator id turns into a picture and a name, the same
    /// way on the mail, notice and sign-in screens. An id with no meta shows as "Item #id" without an
    /// icon rather than throwing - an event may pay an item before its meta exists.
    /// </summary>
    public static class ItemVisual {
        public static Sprite Icon(int itemId) {
            ItemMeta meta = Meta(itemId);
            return meta != null ? meta.GetIcon() : null;
        }

        public static string Name(int itemId) {
            ItemMeta meta = Meta(itemId);
            return meta != null && !string.IsNullOrEmpty(meta.GetName()) ? meta.GetName() : "Item #" + itemId;
        }

        public static string Description(int itemId) {
            ItemMeta meta = Meta(itemId);
            return meta != null ? meta.GetDescription() ?? "" : "";
        }

        public static string Amount(int amount) => amount.ToString("N0", CultureInfo.InvariantCulture);

        public static string List(IEnumerable<Promuse.Contracts.Players.ItemStack> stacks) {
            List<string> parts = new List<string>();
            foreach (Promuse.Contracts.Players.ItemStack stack in stacks) parts.Add(Name(stack.ItemId) + " ×" + Amount(stack.Amount));
            return string.Join(", ", parts);
        }

        /// <summary>
        /// An operator's avatar, if the id names one. Asked of Resources first so a name that is not an
        /// operator is not reported as a missing asset.
        /// </summary>
        public static Sprite Avatar(string characterId) {
            if (string.IsNullOrEmpty(characterId)) return null;
            if (Resources.Load<CharMeta>(GameSettings.CHAR_META_PATH + "/" + characterId) == null) return null;

            CharMeta meta = CharManager.Inst().GetMeta(characterId);
            return meta == null ? null : meta.GetAvatar() != null ? meta.GetAvatar() : meta.GetImage();
        }

        private static ItemMeta Meta(int itemId) {
            try {
                return ItemManager.Inst().GetMeta(itemId);
            } catch (Exception) {
                return null;
            }
        }
    }
}
