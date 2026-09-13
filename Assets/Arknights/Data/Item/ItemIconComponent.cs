using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Data.Item {
    public class ItemIconComponent : MonoBehaviour {

        public Image ground;
        public Image icon;
        public Text amount;
        
        private ItemMeta meta;

        public ItemMeta GetMeta() => meta;
        
        public void SetMeta(ItemMeta value) {
            meta = value;
            ground.sprite = ItemManager.Inst().GetItemGround(meta.GetRarity());
            icon.sprite = meta.GetIcon();
        }

        public void SetAmount(int value) {
            if (amount) {
                // 原本是"万"(一万), 英文没有这个单位, 改成按千进位的K
                // The original abbreviated to 万 (ten thousand); English has no such unit, so this
                // groups by thousands instead. The 10000 threshold is kept so short counts stay exact.
                if (value >= 10000) {
                    amount.text = $"{value / 1000f:0.#}K";
                } else {
                    amount.text = $"{value}";
                }
                LayoutRebuilder.ForceRebuildLayoutImmediate(amount.transform.parent as RectTransform);
            }
        }

        public void AddListener(UnityAction method) {
            GetComponent<Button>().onClick.AddListener(method);
        }

        public void RemoveAllListeners() {
            GetComponent<Button>().onClick.RemoveAllListeners();
        }
    }
}