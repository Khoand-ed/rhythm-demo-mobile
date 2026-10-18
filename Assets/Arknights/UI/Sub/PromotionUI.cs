using System.Collections.Generic;
using System.Globalization;
using Data.Char;
using Data.Player;
using DG.Tweening;
using Promuse.Contracts.Characters;
using Promuse.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Stack = Promuse.Contracts.Players.ItemStack;

namespace UI.Sub {
    /// <summary>
    /// 精英化 / Promoting an operator to its next elite phase, after the reference: the operator over a
    /// dark band that reads "1 Elite" to "2 Elite" with what the phase unlocks, the level the promotion
    /// needs against the operator's own and the LMD and materials it costs along the bottom, and
    /// "Promote ...?" with cancel and confirm.
    ///
    /// 等级不变 / Promotion keeps the level and raises the cap, so what the band lists is what becomes
    /// reachable: the new cap, and the stats at it. At the last phase its rarity reaches the screen says
    /// so and offers nothing to confirm.
    ///
    /// Opened from the PROMOTION panel on CharInfoUI. Built by Arknights/Character/Build Promotion UI;
    /// field bindings are the contract.
    /// </summary>
    public class PromotionUI : UIBase {
        public const string UIName = "PromotionUI";

        private const string Blue = "#4FC3F7";
        private static readonly Color Met = Color.white;
        private static readonly Color Short = new Color(0.98f, 0.55f, 0.16f, 1f);

        [Header("顶栏 / Top")]
        public Button backButton;
        public Image art;

        [Header("精英 / The phases")]
        public GameObject fromSide;
        public TextMeshProUGUI fromNumber;
        public EliteEmblem fromEmblem;
        public TextMeshProUGUI toNumber;
        public EliteEmblem bigEmblem;
        public GameObject toSide;
        public TextMeshProUGUI details;

        [Header("条件 / What it needs")]
        public GameObject levelCell;
        public Image levelRing;
        public TextMeshProUGUI levelValue;
        public TextMeshProUGUI levelText;
        public GameObject costRow;
        public Image[] costIcons = new Image[0];
        public TextMeshProUGUI[] costTexts = new TextMeshProUGUI[0];

        [Header("确认 / Confirm")]
        public TextMeshProUGUI prompt;
        public Button cancelButton;
        public Button confirmButton;
        public CanvasGroup confirmGroup;
        public TextMeshProUGUI flash;

        private static string pending;

        private string characterId;
        private CharData character;
        private CharMeta meta;
        private bool busy;

        /// <summary>Opens the screen on one of the player's operators.</summary>
        public static void Open(string characterId) {
            pending = characterId;
            UIManager.Inst().Show(UIName);
        }

        // ------------------------------------------------------------------ lifecycle

        public override void Init() {
            backButton.onClick.AddListener(Close);
            cancelButton.onClick.AddListener(Close);
            confirmButton.onClick.AddListener(Submit);
            if (flash != null) flash.gameObject.SetActive(false);
        }

        public override void Show() {
            base.Show();
            canvasGroup.alpha = 0;
            canvasGroup.DOFade(1, 0.25f);

            if (pending != null) characterId = pending;
            pending = null;
            Reload();
        }

        public override void Hide(bool destroy = false) {
            canvasGroup.DOFade(0, 0.2f).OnComplete(() => base.Hide(destroy));
        }

        private void Close() => UIManager.Inst().Hide(UIName, true);

        private void Reload() {
            PlayerData player = PlayerManager.Inst().Get();
            character = player != null ? player.GetCharData(characterId) : null;

            if (character == null || character.GetCharMeta() == null) {
                Close();
                return;
            }

            meta = character.GetCharMeta();
            SplashFrame.Show(art, meta);
            Draw();
        }

        // ------------------------------------------------------------------ drawing

        private void Draw() {
            PlayerData player = PlayerManager.Inst().Get();
            int rarity = meta.GetRarity();
            int elite = character.GetElite();
            int level = character.GetLevel();
            int cap = OperatorLevels.MaxLevel(rarity, elite);
            bool canGo = elite < OperatorLevels.MaxElite(rarity);
            int to = canGo ? elite + 1 : elite;

            // 最后一阶只画现在这一阶 / At the last phase only the phase it is in is drawn: the large
            // emblem, and nothing to go from or to.
            fromSide.SetActive(canGo);
            levelCell.SetActive(canGo);
            fromNumber.text = Number(elite);
            fromEmblem.Show(elite);
            toSide.SetActive(canGo);
            toNumber.text = Number(to);
            bigEmblem.Show(to);

            // ---- what the next phase unlocks: the cap, and the stats at it
            if (canGo) {
                int newCap = OperatorLevels.MaxLevel(rarity, to);
                OperatorRules atCap = meta.RulesAt(cap);
                OperatorRules atNewCap = meta.RulesAt(newCap);
                details.text =
                    "Maximum level raised: " + Raise(Number(cap), Number(newCap)) + "\n" +
                    "Max HP at the cap: " + Raise(Number(atCap.maxHp), Number(atNewCap.maxHp)) + "\n" +
                    "Score and Fever at the cap: " + Raise(Modifier(atCap.scoreModifier), Modifier(atNewCap.scoreModifier)) + "\n" +
                    "<size=80%><alpha=#99>Stays at level " + Number(level) + "; EXP continues on the Elite " + to + " curve</size>";
            } else {
                details.text = "Elite " + elite + " is as far as a " + rarity + "-star operator goes.";
            }

            // ---- what it needs: the level, then the LMD and materials
            bool levelMet = level >= cap;
            levelValue.text = Number(cap);
            levelText.text = "<color=" + (levelMet ? "#FFFFFF" : "#FA8C29") + ">" + Number(level) + "</color>/" + Number(cap);
            levelRing.fillAmount = cap > 0 ? Mathf.Clamp01((float)level / cap) : 0f;

            IReadOnlyList<Stack> cost = canGo ? OperatorLevels.PromotionCost(rarity, to) : null;
            costRow.SetActive(cost != null);
            bool affordable = cost != null;

            for (int i = 0; i < costTexts.Length; i++) {
                bool used = cost != null && i < cost.Count;
                costTexts[i].transform.parent.gameObject.SetActive(used);
                if (!used) continue;

                int owned = player != null ? player.GetItemAmount(cost[i].ItemId) : 0;
                bool enough = owned >= cost[i].Amount;
                affordable &= enough;

                costIcons[i].sprite = ItemVisual.Icon(cost[i].ItemId);
                // 龙门币只写要多少 / LMD reads as the amount needed, the way the reference prints it; the
                // materials as owned/needed.
                costTexts[i].text = cost[i].ItemId == OperatorLevels.LmdItemId
                    ? Number(cost[i].Amount)
                    : "<color=" + (enough ? "#FFFFFF" : "#FA8C29") + ">" + Number(owned) + "</color>/" + Number(cost[i].Amount);
                costTexts[i].color = cost[i].ItemId == OperatorLevels.LmdItemId && !enough ? Short : Met;
            }

            // ---- confirm
            prompt.gameObject.SetActive(canGo);
            prompt.text = "Promote <color=" + Blue + ">" + meta.GetEnglishName() + "</color>?";
            confirmButton.gameObject.SetActive(canGo);

            bool can = canGo && levelMet && affordable && !busy;
            confirmButton.interactable = can;
            confirmGroup.alpha = can ? 1f : 0.45f;
        }

        private static string Raise(string before, string after) =>
            "<color=" + Blue + ">" + before + "  >  " + after + "</color>";

        private static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

        private static string Modifier(float value) => "x" + value.ToString("0.000", CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ promote

        private async void Submit() {
            if (busy || character == null) return;

            int to = character.GetElite() + 1;
            busy = true;
            Draw();

            ApiResult<PromoteResult> result = await PlayerManager.Inst().PromoteAsync(characterId, to);

            busy = false;
            if (this == null) return;

            if (!result.IsSuccess) {
                CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, result.Message);
                Reload();
                return;
            }

            // 后面的干员页也要刷新 / The operator page underneath shows the new phase and cap too.
            UIManager.Inst().Update("CharInfoUI");
            Reload();
            Flash("ELITE " + result.Value.ToElite + "   LV CAP " + result.Value.MaxLevel);
        }

        private void Flash(string text) {
            if (flash == null) return;

            flash.text = text;
            flash.gameObject.SetActive(true);
            flash.alpha = 1f;
            flash.transform.localScale = Vector3.one * 0.8f;

            DOTween.Kill(flash);
            DOTween.Kill(flash.transform);
            flash.transform.DOScale(1f, 0.25f).SetEase(Ease.OutBack);
            DOTween.To(() => flash.alpha, a => flash.alpha = a, 0f, 0.5f).SetDelay(1.4f).SetTarget(flash)
                   .OnComplete(() => { if (flash != null) flash.gameObject.SetActive(false); });
        }
    }
}
