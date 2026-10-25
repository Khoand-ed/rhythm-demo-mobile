using System.Collections.Generic;
using System.Linq;
using Data.Char;
using Data.Player;
using DG.Tweening;
using Promuse.Contracts.Players;
using Promuse.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Sub {
    /// <summary>
    /// 选助理 / Choosing the assistant - the operator standing on Home and on the profile card: a grid of
    /// square portraits of the operators the player owns, the current one marked. Pressing one chooses it
    /// at once; Home changes underneath and an open profile card follows.
    ///
    /// 默认是 AMIYA / Until the player chooses, AMIYA stands there (PlayerAssistant.Default). Every new
    /// account owns her, and her tile stands for that default. An older account that does not own her gets
    /// a DEFAULT tile instead, with her portrait, which clears the choice.
    ///
    /// Opened from Home's ASSISTANT button and from the assistant on the player's own profile card. Built
    /// by Arknights/Home/Build Assistant UI; field bindings are the contract.
    /// </summary>
    public class AssistantUI : UIBase {
        public const string UIName = "AssistantUI";

        public Button closeButton;
        public Button veilButton;
        public RectTransform grid;
        public RectTransform tileTemplate;

        private readonly List<RectTransform> tiles = new List<RectTransform>();
        private bool busy;

        public static void Open() => UIManager.Inst().Show(UIName);

        // ------------------------------------------------------------------ lifecycle

        public override Button ExitButton() => closeButton;

        public override void Init() {
            closeButton.onClick.AddListener(Close);
            veilButton.onClick.AddListener(Close);
            tileTemplate.gameObject.SetActive(false);
        }

        public override void Show() {
            base.Show();
            canvasGroup.alpha = 0;
            canvasGroup.DOFade(1, 0.2f);
            Draw();
        }

        public override void Hide(bool destroy = false) {
            canvasGroup.DOFade(0, 0.15f).OnComplete(() => base.Hide(destroy));
        }

        private void Close() => UIManager.Inst().Hide(UIName, true);

        // ------------------------------------------------------------------ drawing

        /// <summary>One tile: the operator it chooses, or null for the DEFAULT tile that clears the choice.</summary>
        private struct Entry {
            public string Id;
            public string Portrait;
            public bool IsDefault;
        }

        private void Draw() {
            PlayerData player = PlayerManager.Inst().Get();
            if (player == null) {
                Close();
                return;
            }

            string chosen = player.GetDesktopChar();
            List<Entry> entries = Entries(player);

            while (tiles.Count < entries.Count) tiles.Add((RectTransform)Instantiate(tileTemplate, grid));

            for (int i = 0; i < tiles.Count; i++) {
                bool used = i < entries.Count;
                tiles[i].gameObject.SetActive(used);
                if (used) DrawTile(tiles[i], entries[i], chosen);
            }
        }

        /// <summary>The owned operators, rarest and most raised first, and DEFAULT first when AMIYA is not among them.</summary>
        private static List<Entry> Entries(PlayerData player) {
            List<CharData> owned = player.GetCharList() ?? new List<CharData>();
            List<Entry> entries = new List<Entry>();

            if (owned.All(c => c.GetId() != PlayerAssistant.Default)) {
                entries.Add(new Entry { Id = null, Portrait = PlayerAssistant.Default, IsDefault = true });
            }

            foreach (CharData c in owned
                         .OrderByDescending(c => c.GetCharMeta() != null ? c.GetCharMeta().GetRarity() : 0)
                         .ThenByDescending(c => c.GetElite())
                         .ThenByDescending(c => c.GetLevel())
                         .ThenBy(c => c.GetId(), System.StringComparer.Ordinal)) {
                entries.Add(new Entry { Id = c.GetId(), Portrait = c.GetId() });
            }

            return entries;
        }

        private void DrawTile(RectTransform tile, Entry entry, string chosen) {
            Image portrait = tile.Find("Mask/Portrait").GetComponent<Image>();
            portrait.sprite = ItemVisual.Avatar(entry.Portrait);
            portrait.enabled = portrait.sprite != null;

            // 现在站着的那个 / Marked: the chosen one, or - with nothing chosen - whichever tile stands for AMIYA.
            bool current = chosen != null
                ? entry.Id == chosen
                : entry.IsDefault || entry.Id == PlayerAssistant.Default;
            tile.Find("Selected").gameObject.SetActive(current);
            tile.Find("DefaultTag").gameObject.SetActive(entry.IsDefault);

            Button button = tile.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => Choose(entry.Id, current));
            button.interactable = !busy;
        }

        // ------------------------------------------------------------------ choosing

        private async void Choose(string characterId, bool alreadyThere) {
            if (busy || alreadyThere) return;

            busy = true;
            Draw();

            ApiResult<PlayerData> result = await PlayerManager.Inst().SetAssistantAsync(characterId);

            busy = false;
            if (this == null) return;

            if (!result.IsSuccess) CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, result.Message);

            // 名片若开着也跟着变 / An open profile card shows the new assistant too; Home follows by itself.
            UIManager.Inst().Update(ProfileUI.UIName);
            Draw();
        }
    }
}
