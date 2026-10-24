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
    /// 干员升级 / Levelling an operator up, after the reference's level-up screen: the operator on the
    /// left with what the level changes - max HP, score and fever - the target level and its EXP, the
    /// battle records to spend and the LMD they cost; on the right the level dial and Level Up.
    ///
    /// 拨号自动选 / Turning the dial picks the records for that level - as few as reach it, with the
    /// least EXP left over - and pressing a record adds one by hand, which moves the dial to wherever
    /// the EXP now reaches. Everything shown is worked out with OperatorLevels, the rules the server
    /// levels with, so the number previewed is the number the server answers with.
    ///
    /// Opened from the level panel on CharInfoUI. Built by Arknights/Character/Build Level Up UI;
    /// field bindings are the contract.
    /// </summary>
    public class LevelUpUI : UIBase {
        public const string UIName = "LevelUpUI";

        private const string Gold = "#F5B800";
        private static readonly Color Raised = new Color(0.96f, 0.72f, 0.05f, 1f);
        private static readonly Color Plain = Color.white;
        private static readonly Color Short = new Color(0.92f, 0.30f, 0.26f, 1f);

        [Header("顶栏 / Top")]
        public Button backButton;
        public Image art;
        public TextMeshProUGUI operatorName;

        [Header("数值 / What the level changes")]
        public TextMeshProUGUI hpFrom;
        public TextMeshProUGUI hpTo;
        public TextMeshProUGUI scoreFrom;
        public TextMeshProUGUI scoreTo;
        public TextMeshProUGUI feverFrom;
        public TextMeshProUGUI feverTo;

        [Header("目标 / Target")]
        public TextMeshProUGUI targetLevel;
        public TextMeshProUGUI expText;
        public Image expBar;

        [Header("作战记录 / The records, smallest first, as OperatorLevels.Records")]
        public Image[] recordIcons = new Image[0];
        public TextMeshProUGUI[] recordOwned = new TextMeshProUGUI[0];
        public TextMeshProUGUI[] recordChosen = new TextMeshProUGUI[0];
        public Button[] recordAdd = new Button[0];
        public Button[] recordRemove = new Button[0];
        public TextMeshProUGUI lmdCost;
        public Image lmdIcon;

        [Header("拨号 / The dial")]
        public LevelWheel wheel;
        public TextMeshProUGUI wheelLevel;
        public TextMeshProUGUI[] wheelAbove = new TextMeshProUGUI[0];
        public TextMeshProUGUI wheelBelow;
        public Image ring;
        public TextMeshProUGUI hint;

        [Header("升级 / Level up")]
        public Button levelUpButton;
        public CanvasGroup levelUpGroup;
        public TextMeshProUGUI flash;

        private static string pending;

        private string characterId;
        private CharData character;
        private CharMeta meta;
        private int elite;
        private int fromLevel;
        private int fromExp;
        private int cap;

        private readonly int[] chosen = new int[4];
        private bool busy;

        /// <summary>Opens the screen on one of the player's operators.</summary>
        public static void Open(string characterId) {
            pending = characterId;
            UIManager.Inst().Show(UIName);
        }

        // ------------------------------------------------------------------ lifecycle

        public override Button ExitButton() => backButton;

        public override void Init() {
            backButton.onClick.AddListener(() => UIManager.Inst().Hide(UIName, true));
            wheel.Stepped += Turn;
            levelUpButton.onClick.AddListener(Submit);

            for (int i = 0; i < Records.Count && i < recordAdd.Length; i++) {
                int slot = i;
                recordAdd[i].onClick.AddListener(() => Add(slot));
                recordRemove[i].onClick.AddListener(() => Remove(slot));
                recordIcons[i].sprite = ItemVisual.Icon(Records[i].ItemId);
            }

            if (lmdIcon != null) lmdIcon.sprite = ItemVisual.Icon(OperatorLevels.LmdItemId);
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

        /// <summary>Reads the operator, the records and the LMD again, and clears the choice.</summary>
        private void Reload() {
            PlayerData player = PlayerManager.Inst().Get();
            character = player != null ? player.GetCharData(characterId) : null;

            if (character == null || character.GetCharMeta() == null) {
                UIManager.Inst().Hide(UIName, true);
                return;
            }

            meta = character.GetCharMeta();
            elite = character.GetElite();
            fromLevel = character.GetLevel();
            fromExp = character.GetExp();
            cap = character.GetMaxLevel();

            System.Array.Clear(chosen, 0, chosen.Length);

            SplashFrame.Show(art, meta);
            operatorName.text = meta.GetEnglishName();
            Draw();
        }

        // ------------------------------------------------------------------ choosing

        private static IReadOnlyList<ExpRecord> Records => OperatorLevels.Records;

        private static int Owned(int slot) {
            PlayerData player = PlayerManager.Inst().Get();
            return player != null ? player.GetItemAmount(Records[slot].ItemId) : 0;
        }

        private int ChosenExp() {
            int total = 0;
            for (int i = 0; i < Records.Count; i++) total += chosen[i] * Records[i].Exp;
            return total;
        }

        private LevelGain Gain(int exp) => OperatorLevels.Gain(elite, fromLevel, fromExp, cap, exp);

        /// <summary>The highest level every record the player owns would reach.</summary>
        private int Reachable() {
            int all = 0;
            for (int i = 0; i < Records.Count; i++) all += Owned(i) * Records[i].Exp;
            return Gain(all).Level;
        }

        private void Turn(int step) {
            if (busy || fromLevel >= cap) return;

            int shown = Gain(ChosenExp()).Level;
            int next = Mathf.Clamp(shown + step, fromLevel, Reachable());
            if (next == shown) return;

            Choose(next);
            Draw();
        }

        /// <summary>
        /// 最少浪费 / The records for <paramref name="level"/>: the largest first while they fit under
        /// the EXP it takes, then the smallest single record that covers what is left, then any that
        /// turn out not to be needed are put back. Short of records, it takes them all.
        /// </summary>
        private void Choose(int level) {
            System.Array.Clear(chosen, 0, chosen.Length);
            if (level <= fromLevel) return;

            int need = -fromExp;
            for (int l = fromLevel; l < level; l++) need += OperatorLevels.ExpToNext(elite, l);

            int left = need;
            for (int i = Records.Count - 1; i >= 0; i--) {
                int take = Mathf.Min(Owned(i), left / Records[i].Exp);
                chosen[i] = take;
                left -= take * Records[i].Exp;
            }

            if (left > 0) {
                bool covered = false;
                for (int i = 0; i < Records.Count && !covered; i++) {
                    if (Owned(i) > chosen[i] && Records[i].Exp >= left) {
                        chosen[i]++;
                        left -= Records[i].Exp;
                        covered = true;
                    }
                }

                for (int i = Records.Count - 1; i >= 0 && left > 0; i--) {
                    while (left > 0 && Owned(i) > chosen[i]) {
                        chosen[i]++;
                        left -= Records[i].Exp;
                    }
                }
            }

            for (int i = Records.Count - 1; i >= 0; i--) {
                while (chosen[i] > 0 && ChosenExp() - Records[i].Exp >= need) chosen[i]--;
            }
        }

        private void Add(int slot) {
            // 到顶就不能再加 / Once the choice reaches the cap, another record would land past it whole.
            if (busy || Owned(slot) <= chosen[slot] || Gain(ChosenExp()).Level >= cap) return;

            chosen[slot]++;
            Draw();
        }

        private void Remove(int slot) {
            if (busy || chosen[slot] == 0) return;

            chosen[slot]--;
            Draw();
        }

        // ------------------------------------------------------------------ drawing

        private void Draw() {
            PlayerData player = PlayerManager.Inst().Get();
            int lmdOwned = player != null ? player.GetItemAmount(OperatorLevels.LmdItemId) : 0;

            bool capped = fromLevel >= cap;
            int total = ChosenExp();
            LevelGain gain = Gain(total);
            int shown = total > 0 ? gain.Level : fromLevel;
            int barExp = total > 0 ? gain.Exp : fromExp;
            int lmd = OperatorLevels.LmdFor(elite, gain.Applied);
            bool atCap = shown >= cap;

            // ---- the dial
            wheelLevel.text = Number(shown);
            for (int i = 0; i < wheelAbove.Length; i++) {
                int level = shown + i + 1;
                wheelAbove[i].text = level <= cap ? Number(level) : "";
            }
            wheelBelow.text = shown - 1 >= fromLevel ? Number(shown - 1) : "";

            float fill = atCap ? 1f : (float)barExp / OperatorLevels.ExpToNext(elite, shown);
            ring.fillAmount = fill;
            expBar.fillAmount = fill;

            // 到顶了就指向精英化 / At the cap, point at promotion when there is a phase left to reach.
            bool promotable = elite < OperatorLevels.MaxElite(meta.GetRarity());
            hint.text = capped ? (promotable ? "MAX LEVEL\nPromote to raise it" : "MAX LEVEL")
                      : Reachable() == fromLevel ? "No battle records\nto spend"
                      : "Slide level\nto auto-select";

            // ---- the target
            targetLevel.text = "LV " + Number(shown);
            expText.text = atCap
                ? "EXP <color=" + Gold + ">MAX</color>"
                : "EXP <color=" + Gold + ">" + Number(barExp) + "</color>/" + Number(OperatorLevels.ExpToNext(elite, shown));

            // ---- what the level changes
            OperatorRules before = meta.RulesAt(fromLevel);
            OperatorRules after = meta.RulesAt(shown);
            Pair(hpFrom, hpTo, Number(before.maxHp), Number(after.maxHp));
            Pair(scoreFrom, scoreTo, Modifier(before.scoreModifier), Modifier(after.scoreModifier));
            Pair(feverFrom, feverTo, Modifier(before.feverModifier), Modifier(after.feverModifier));

            // ---- the records and the LMD
            for (int i = 0; i < Records.Count && i < recordOwned.Length; i++) {
                int owned = Owned(i);
                recordOwned[i].text = Number(owned);
                recordChosen[i].text = chosen[i] > 0 ? Number(chosen[i]) : "";
                recordChosen[i].transform.parent.gameObject.SetActive(chosen[i] > 0);
                recordAdd[i].interactable = !busy && !capped && owned > chosen[i] && !atCap;
                recordRemove[i].gameObject.SetActive(chosen[i] > 0);
            }

            lmdCost.text = Number(lmd);
            lmdCost.color = lmd > lmdOwned ? Short : Plain;

            bool can = !busy && !capped && total > 0 && lmd <= lmdOwned;
            levelUpButton.interactable = can;
            levelUpGroup.alpha = can ? 1f : 0.45f;
        }

        private static void Pair(TextMeshProUGUI from, TextMeshProUGUI to, string before, string after) {
            from.text = before;
            to.text = after;
            to.color = after == before ? Plain : Raised;
        }

        private static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

        private static string Modifier(float value) => "x" + value.ToString("0.000", CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ level up

        private async void Submit() {
            if (busy) return;

            List<Stack> spend = new List<Stack>();
            for (int i = 0; i < Records.Count; i++) {
                if (chosen[i] > 0) spend.Add(new Stack(Records[i].ItemId, chosen[i]));
            }
            if (spend.Count == 0) return;

            busy = true;
            Draw();

            ApiResult<LevelUpResult> result = await PlayerManager.Inst().LevelUpAsync(characterId, spend);

            busy = false;
            if (this == null) return;

            if (!result.IsSuccess) {
                CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, result.Message);
                Reload();
                return;
            }

            // 后面的干员页也要刷新 / The operator page underneath reads the new level as well.
            UIManager.Inst().Update("CharInfoUI");
            Reload();
            Flash(result.Value.ToLevel > result.Value.FromLevel
                ? "LEVEL UP  " + result.Value.FromLevel + " > " + result.Value.ToLevel
                : "EXP +" + Number(result.Value.ExpApplied));
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
            DOTween.To(() => flash.alpha, a => flash.alpha = a, 0f, 0.5f).SetDelay(1.1f).SetTarget(flash)
                   .OnComplete(() => { if (flash != null) flash.gameObject.SetActive(false); });
        }
    }
}
