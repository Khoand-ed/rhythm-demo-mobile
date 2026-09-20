using System.Collections.Generic;
using Data.Item;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Sub {
    /// <summary>
    /// 领取成功弹窗 / The "reward received" popup: one card listing everything the mission board
    /// just handed over.
    ///
    /// 只是报喜, 不发奖 / It announces, it does not grant. MissionManager has already put the items
    /// in the bag by the time this opens, so dismissing it - or never seeing it at all - costs the
    /// player nothing.
    ///
    /// 一次只开一个模糊弹窗 / Exactly one blurred popup may be up at a time. UiBlurCapture shares a
    /// single material and render texture across the session, so the cells here are deliberately
    /// not clickable: opening ItemInfoUI on top of this would have the two fighting over that one
    /// capture. The same items are inspectable from the Depot.
    /// </summary>
    public class MissionRewardUI : UIBase {
        public const string UIName = "MissionRewardUI";

        public Image blur;
        public Text title;

        [Header("模板 / One reward cell, inactive, inside the grid it is cloned into")]
        public RectTransform cellTemplate;

        private readonly List<RectTransform> cells = new List<RectTransform>();

        public static void Show(List<ItemStack> items) {
            if (items == null || items.Count == 0) return;

            MissionRewardUI ui = UIManager.Inst().Show(UIName) as MissionRewardUI;
            if (ui == null) return;

            ui.Draw(items);
        }

        private void Draw(List<ItemStack> items) {
            title.text = items.Count == 1 ? "MISSION REWARD" : $"MISSION REWARDS x{items.Count}";

            while (cells.Count < items.Count) {
                cells.Add(Instantiate(cellTemplate, cellTemplate.parent));
            }

            for (int i = 0; i < cells.Count; i++) {
                bool used = i < items.Count;
                cells[i].gameObject.SetActive(used);
                if (!used) continue;

                ItemStack stack = items[i];
                ItemMeta meta = SafeMeta(stack);

                ItemIconComponent icon = cells[i].GetComponent<ItemIconComponent>("Icon");
                Text label = cells[i].GetComponent<Text>("Name");

                // 缺元数据就只报数量 / Without an ItemMeta, SetMeta would throw on GetRarity().
                // Reporting the id and the amount still tells the player what they got.
                if (meta != null) {
                    icon.SetMeta(meta);
                    icon.SetAmount(stack.GetAmount());
                    label.text = $"{meta.GetName()} x{stack.GetAmount()}";
                } else {
                    label.text = $"Item {stack.GetId()} x{stack.GetAmount()}";
                }
            }
        }

        /// <summary>
        /// 取元数据不能炸 / Resolves an item's metadata without letting a failure reach the caller.
        ///
        /// 这里更要稳 / This matters even more here than on the board: the items are already in the
        /// player's bag by the time this opens, so an exception would throw away the only notice
        /// they get about a reward they have in fact been given.
        /// </summary>
        private static ItemMeta SafeMeta(ItemStack stack) {
            try {
                return stack.GetItemMeta();
            } catch (System.Exception e) {
                Debug.LogWarning($"[MissionRewardUI] No metadata for item {stack.GetId()}, naming it by id: {e.Message}");
                return null;
            }
        }

        public override void Show() {
            base.Show();

            Material blurryShader = blur.material;
            blurryShader.SetFloat("_Size", 0);
            float value = 0;
            DOTween.To(() => value, v => value = v, 160, 0.5f).OnUpdate(() => blurryShader.SetFloat("_Size", value));

            canvasGroup.alpha = 0;
            canvasGroup.DOFade(1, 0.3f);
        }

        public override void Hide(bool destroy = false) {
            Material blurryShader = blur.material;
            float value = blurryShader.GetFloat("_Size");
            DOTween.To(() => value, v => value = v, 0, 0.2f).OnUpdate(() => blurryShader.SetFloat("_Size", value));

            canvasGroup.DOFade(0, 0.2f).OnComplete(() => base.Hide(destroy));
        }
    }
}
