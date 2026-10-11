using System;
using System.Collections.Generic;
using System.Globalization;
using Data.Char;
using Data.Gacha;
using Promuse.Contracts.Gacha;
using Promuse.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Sub {
    /// <summary>
    /// 寻访记录 / The player's pulls, newest first, ten to a page.
    ///
    /// 按游标翻页 / Paged by the server's cursor, not by page number: a page is "the ten below the
    /// last one I saw", so pulling on another device while this is open never shifts or repeats a
    /// row. Going back is a stack of the cursors already used.
    ///
    /// 旧的回答不能盖住新的 / Every load takes a ticket and only the latest may draw - the same guard
    /// the leaderboard panel uses - so pressing NEXT twice quickly cannot leave page one's rows under
    /// page three's number.
    ///
    /// Built by Arknights/Gacha/Build Gacha UI. Row children: Time, Banner, Operator, Stars, Result.
    /// </summary>
    public class GachaHistoryView : MonoBehaviour {
        public const int PageSize = 10;

        public RectTransform rows;
        public RectTransform rowTemplate;
        public TextMeshProUGUI pageLabel;
        public TextMeshProUGUI note;
        public Button prevButton;
        public Button nextButton;
        public Button closeButton;

        private readonly List<Row> drawn = new List<Row>();

        // 每页的起点 / Where each page seen so far started; null is the newest page.
        private readonly Stack<long?> previous = new Stack<long?>();
        private long? current;
        private long? next;
        private int ticket;

        private Func<string, string> bannerTitle = id => id;

        private void Awake() {
            rowTemplate.gameObject.SetActive(false);
            prevButton.onClick.AddListener(Back);
            nextButton.onClick.AddListener(Forward);
            closeButton.onClick.AddListener(() => gameObject.SetActive(false));
        }

        /// <param name="titleOf">Turns a banner id into the name the player knows it by.</param>
        public void Open(Func<string, string> titleOf) {
            bannerTitle = titleOf ?? (id => id);
            gameObject.SetActive(true);

            previous.Clear();
            current = null;
            Load();
        }

        private void Forward() {
            if (next == null) return;

            previous.Push(current);
            current = next;
            Load();
        }

        private void Back() {
            if (previous.Count == 0) return;

            current = previous.Pop();
            Load();
        }

        private async void Load() {
            int mine = ++ticket;

            prevButton.interactable = false;
            nextButton.interactable = false;
            note.text = "LOADING...";
            Draw(null);

            ApiResult<GachaHistoryPage> result = await GachaManager.Inst().HistoryAsync(current, PageSize);

            if (this == null || mine != ticket) return;

            if (!result.IsSuccess) {
                Debug.LogWarning($"[GachaHistoryView] {result.Message}");
                note.text = "HISTORY UNAVAILABLE";
                prevButton.interactable = previous.Count > 0;
                return;
            }

            next = result.Value.NextBefore;
            Draw(result.Value.Entries);

            note.text = result.Value.Entries.Count == 0 && previous.Count == 0 ? "NO HEADHUNTING YET" : "";
            pageLabel.text = "PAGE " + (previous.Count + 1).ToString(CultureInfo.InvariantCulture);
            prevButton.interactable = previous.Count > 0;
            nextButton.interactable = next != null;
        }

        private void Draw(IReadOnlyList<GachaHistoryEntry> entries) {
            int count = entries != null ? entries.Count : 0;

            while (drawn.Count < count) {
                drawn.Add(new Row((RectTransform)Instantiate(rowTemplate, rows)));
            }

            for (int i = 0; i < drawn.Count; i++) {
                bool used = i < count;
                drawn[i].Root.SetActive(used);
                if (used) drawn[i].Draw(entries[i], bannerTitle);
            }
        }

        private sealed class Row {
            public readonly GameObject Root;
            private readonly TextMeshProUGUI time;
            private readonly TextMeshProUGUI banner;
            private readonly TextMeshProUGUI character;
            private readonly Image stars;
            private readonly TextMeshProUGUI outcome;

            public Row(RectTransform root) {
                Root = root.gameObject;
                time = root.Find("Time").GetComponent<TextMeshProUGUI>();
                banner = root.Find("Banner").GetComponent<TextMeshProUGUI>();
                character = root.Find("Operator").GetComponent<TextMeshProUGUI>();
                stars = root.Find("Stars").GetComponent<Image>();
                outcome = root.Find("Result").GetComponent<TextMeshProUGUI>();
            }

            public void Draw(GachaHistoryEntry entry, Func<string, string> titleOf) {
                // 本地时间 / Local time - this is a record for the player, not a schedule.
                time.text = entry.PulledAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                banner.text = titleOf(entry.BannerId);

                CharMeta meta = GachaUI.SafeMeta(entry.CharacterId);
                character.text = meta != null ? meta.GetEnglishName() : entry.CharacterId;
                character.color = GachaResultView.RarityColor(entry.Rarity);

                stars.sprite = GachaUI.SafeSprite(() => CharManager.Inst().GetStarImage("info_" + entry.Rarity));
                stars.enabled = stars.sprite != null;

                outcome.text = entry.IsNew ? "NEW" : "DUPLICATE";
                outcome.color = entry.IsNew ? new Color(1f, 0.42f, 0.30f, 1f) : new Color(1f, 1f, 1f, 0.55f);
            }
        }
    }
}
