using System;
using System.Collections.Generic;
using System.Globalization;
using Data.HomeContent;
using DG.Tweening;
using Promuse.Contracts.Notices;
using Promuse.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Sub {
    /// <summary>
    /// 公告 / The announcement board: Event, System and News tabs across the top, the tab's notices on
    /// the left with their date and an unread dot, the chosen one on the right - title, banner, text.
    ///
    /// Opens by itself the first time Home shows each day (see HomeDaily), and from Home's alert icon.
    /// Built by Arknights/Home/Build Mail, Notice and Calendar UI. Template child names are the contract.
    /// </summary>
    public class NoticeUI : UIBase {
        public const string UIName = "NoticeUI";

        private static readonly Color TabOn = new Color(0.86f, 0.86f, 0.87f, 1f);
        private static readonly Color TabOff = new Color(0.16f, 0.17f, 0.19f, 1f);
        private static readonly Color TextOn = new Color(0.12f, 0.12f, 0.14f, 1f);
        private static readonly Color TextOff = new Color(1f, 1f, 1f, 0.9f);

        public Button closeButton;

        [Header("页签 / Tabs - Event, System, News in that order")]
        public Button[] tabs = new Button[3];

        [Header("列表 / List")]
        public RectTransform list;
        public RectTransform itemTemplate;
        public TextMeshProUGUI emptyText;

        [Header("内容 / Content")]
        public GameObject content;
        public ScrollRect contentScroll;
        public TextMeshProUGUI titleText;
        public Image banner;
        public LayoutElement bannerLayout;
        public TextMeshProUGUI bodyText;

        private readonly List<RectTransform> items = new List<RectTransform>();
        private readonly List<Notice> shown = new List<Notice>();

        private NoticeCategory category = NoticeCategory.Event;
        private Guid? selected;
        private Action onClosed;
        private int bannerTicket;

        /// <summary>Shows the board; <paramref name="closed"/> runs when the player closes it.</summary>
        public static void Open(Action closed = null) {
            NoticeUI ui = UIManager.Inst().Show(UIName) as NoticeUI;
            if (ui == null) {
                closed?.Invoke();
                return;
            }
            ui.onClosed = closed;
        }

        // ------------------------------------------------------------------ lifecycle

        public override void Init() {
            closeButton.onClick.AddListener(Close);
            for (int i = 0; i < tabs.Length; i++) {
                NoticeCategory which = (NoticeCategory)i;
                tabs[i].onClick.AddListener(() => {
                    category = which;
                    selected = null;
                    Draw();
                });
            }
            itemTemplate.gameObject.SetActive(false);
        }

        public override void Show() {
            base.Show();
            canvasGroup.alpha = 0;
            canvasGroup.DOFade(1, 0.25f);

            NoticeManager.Inst().Changed -= Draw;
            NoticeManager.Inst().Changed += Draw;
            Draw();
            Load();
        }

        public override void Hide(bool destroy = false) {
            NoticeManager.Inst().Changed -= Draw;
            canvasGroup.DOFade(0, 0.2f).OnComplete(() => base.Hide(destroy));
        }

        private void OnDestroy() {
            NoticeManager.Inst().Changed -= Draw;
        }

        private void Close() {
            Action closed = onClosed;
            onClosed = null;
            UIManager.Inst().Hide(UIName, true);
            closed?.Invoke();
        }

        private async void Load() {
            ApiResult<NoticeList> result = await NoticeManager.Inst().LoadAsync();
            if (this == null) return;
            if (!result.IsSuccess) CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, result.Message);
        }

        // ------------------------------------------------------------------ draw

        private void Draw() {
            if (this == null) return;

            IReadOnlyList<Notice> all = NoticeManager.Inst().Notices;

            // 页签 / A tab shows a dot when anything under it is unread.
            for (int i = 0; i < tabs.Length; i++) {
                bool on = (int)category == i;
                bool unread = false;
                foreach (Notice n in all) if ((int)n.Category == i && !n.IsRead) unread = true;

                tabs[i].transform.Find("Face").GetComponent<Image>().color = on ? TabOn : TabOff;
                tabs[i].transform.Find("Label").GetComponent<TextMeshProUGUI>().color = on ? TextOn : TextOff;
                tabs[i].transform.Find("Pointer").gameObject.SetActive(on);
                tabs[i].transform.Find("Dot").gameObject.SetActive(unread);
            }

            shown.Clear();
            foreach (Notice n in all) if (n.Category == category) shown.Add(n);

            emptyText.gameObject.SetActive(shown.Count == 0);
            emptyText.text = all.Count == 0 ? "No notices right now." : "Nothing under this tab.";

            if (selected == null || !shown.Exists(n => n.NoticeId == selected)) selected = shown.Count > 0 ? shown[0].NoticeId : (Guid?)null;

            while (items.Count < shown.Count) items.Add((RectTransform)Instantiate(itemTemplate, list));
            for (int i = 0; i < items.Count; i++) {
                bool used = i < shown.Count;
                items[i].gameObject.SetActive(used);
                if (used) DrawItem(items[i], shown[i]);
            }

            Notice current = shown.Find(n => n.NoticeId == selected);
            content.SetActive(current != null);
            if (current != null) DrawContent(current);
        }

        private void DrawItem(RectTransform item, Notice notice) {
            bool on = notice.NoticeId == selected;
            DateTime local = notice.StartsAt.ToLocalTime().DateTime;

            item.Find("Face").GetComponent<Image>().color = on ? TabOn : TabOff;
            item.Find("Arrow").gameObject.SetActive(on);
            item.Find("DateDay").GetComponent<TextMeshProUGUI>().text = local.Day.ToString(CultureInfo.InvariantCulture);
            item.Find("DateMonth").GetComponent<TextMeshProUGUI>().text = local.Month.ToString(CultureInfo.InvariantCulture);
            TextMeshProUGUI title = item.Find("Title").GetComponent<TextMeshProUGUI>();
            title.text = notice.ListTitle;
            title.color = on ? TextOn : TextOff;
            item.Find("Dot").gameObject.SetActive(!notice.IsRead);

            Button button = item.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            Guid id = notice.NoticeId;
            button.onClick.AddListener(() => {
                selected = id;
                Draw();
            });
        }

        private void DrawContent(Notice notice) {
            bool changed = titleText.text != notice.Title || bodyText.text != notice.Body;
            titleText.text = notice.Title;
            bodyText.text = notice.Body;
            if (changed) contentScroll.verticalNormalizedPosition = 1f;

            NoticeManager.Inst().MarkRead(notice);
            LoadBanner(notice.BannerUrl);
        }

        /// <summary>
        /// 横幅按自己的比例 / The banner keeps its own proportions: its height follows the column's
        /// width, so a wide banner and a tall one both show whole.
        /// </summary>
        private async void LoadBanner(string url) {
            int mine = ++bannerTicket;
            if (string.IsNullOrEmpty(url)) {
                banner.gameObject.SetActive(false);
                return;
            }

            Sprite sprite = await NoticeManager.Inst().BannerAsync(url);
            if (this == null || mine != bannerTicket) return;

            banner.gameObject.SetActive(sprite != null);
            if (sprite == null) return;

            banner.sprite = sprite;
            float width = ((RectTransform)banner.transform.parent).rect.width;
            bannerLayout.preferredHeight = width * sprite.rect.height / Mathf.Max(1f, sprite.rect.width);
        }
    }
}
