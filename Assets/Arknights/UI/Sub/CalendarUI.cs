using System;
using System.Collections.Generic;
using System.Globalization;
using Data.HomeContent;
using DG.Tweening;
using Promuse.Contracts.SignIn;
using Promuse.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Stack = Promuse.Contracts.Players.ItemStack;

namespace UI.Sub {
    /// <summary>
    /// 每月签到 / The month's sign-in: a square per day, ticked when claimed, today's framed in yellow;
    /// on the right the reward of the square picked, and the time to the next day.
    ///
    /// 打开就签 / Opening it signs today's square if it is waiting, the way the reference game does -
    /// there is no button to forget. Opens by itself the first time Home shows each day (see
    /// HomeDaily), and from Home's calendar icon.
    ///
    /// Built by Arknights/Home/Build Mail, Notice and Calendar UI. Template child names are the contract.
    /// </summary>
    public class CalendarUI : UIBase {
        public const string UIName = "CalendarUI";

        public Button closeButton;

        [Header("月 / Month")]
        public TextMeshProUGUI monthText;
        public RectTransform grid;
        public RectTransform cellTemplate;

        [Header("右侧 / Side")]
        public TextMeshProUGUI timerText;
        public Image rewardIcon;
        public TextMeshProUGUI rewardName;
        public TextMeshProUGUI rewardAmount;
        public GameObject signedBadge;
        public TextMeshProUGUI signedText;

        private readonly List<RectTransform> cells = new List<RectTransform>();

        private int picked = -1;
        private float nextTick;
        private bool busy;
        private Action onClosed;

        /// <summary>Shows the month (signing today's square if waiting); <paramref name="closed"/> runs on close.</summary>
        public static void Open(Action closed = null) {
            CalendarUI ui = UIManager.Inst().Show(UIName) as CalendarUI;
            if (ui == null) {
                closed?.Invoke();
                return;
            }
            ui.onClosed = closed;
        }

        // ------------------------------------------------------------------ lifecycle

        public override void Init() {
            closeButton.onClick.AddListener(Close);
            cellTemplate.gameObject.SetActive(false);
        }

        public override void Show() {
            base.Show();
            canvasGroup.alpha = 0;
            canvasGroup.DOFade(1, 0.25f);

            picked = -1;
            Draw();
            LoadAndSign();
        }

        public override void Hide(bool destroy = false) {
            canvasGroup.DOFade(0, 0.2f).OnComplete(() => base.Hide(destroy));
        }

        private void Update() {
            if (Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 1f;
            DrawTimer();
        }

        private void Close() {
            Action closed = onClosed;
            onClosed = null;
            UIManager.Inst().Hide(UIName, true);
            closed?.Invoke();
        }

        private async void LoadAndSign() {
            if (busy) return;
            busy = true;
            try {
                ApiResult<SignInMonth> month = await SignInManager.Inst().LoadAsync();
                if (this == null) return;
                if (!month.IsSuccess) {
                    CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, month.Message);
                    return;
                }

                Draw();
                if (!month.Value.CanSignToday) return;

                ApiResult<SignInResult> signed = await SignInManager.Inst().SignAsync();
                if (this == null) return;
                if (!signed.IsSuccess) {
                    CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, signed.Message);
                    return;
                }

                picked = signed.Value.Day - 1;
                Draw();
                Stamp(cells[picked]);
                CommonDialogUI.Message(CommonDialogUI.GroundType.WHITE,
                    "Received: " + ItemVisual.List(new[] { signed.Value.Granted }));
            } finally {
                busy = false;
            }
        }

        // ------------------------------------------------------------------ draw

        private void Draw() {
            if (this == null) return;

            SignInMonth month = SignInManager.Inst().Month;
            if (month == null) {
                monthText.text = "";
                return;
            }

            monthText.text = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month.Month);
            int today = month.CanSignToday ? month.Signed : month.Signed - 1;
            if (picked < 0 || picked >= month.Rewards.Count) picked = Mathf.Clamp(today, 0, month.Rewards.Count - 1);

            while (cells.Count < month.Rewards.Count) cells.Add((RectTransform)Instantiate(cellTemplate, grid));
            for (int i = 0; i < cells.Count; i++) {
                bool used = i < month.Rewards.Count;
                cells[i].gameObject.SetActive(used);
                if (used) DrawCell(cells[i], i, month.Rewards[i], i < month.Signed, i == today);
            }

            DrawSide(month);
            DrawTimer();
        }

        private void DrawCell(RectTransform cell, int index, Stack reward, bool signed, bool today) {
            cell.Find("Day").GetComponent<TextMeshProUGUI>().text = (index + 1).ToString(CultureInfo.InvariantCulture);

            Sprite icon = ItemVisual.Icon(reward.ItemId);
            Image image = cell.Find("Icon").GetComponent<Image>();
            image.sprite = icon;
            image.enabled = icon != null;
            cell.Find("Amount").GetComponent<TextMeshProUGUI>().text =
                (icon == null ? ItemVisual.Name(reward.ItemId) + "\n" : "") + ItemVisual.Amount(reward.Amount);

            cell.Find("Today").gameObject.SetActive(today);
            cell.Find("Picked").gameObject.SetActive(index == picked && !today);
            cell.Find("Shade").gameObject.SetActive(signed);
            cell.Find("Check").gameObject.SetActive(signed);

            Button button = cell.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => {
                picked = index;
                Draw();
            });
        }

        private void DrawSide(SignInMonth month) {
            Stack reward = month.Rewards[picked];
            Sprite icon = ItemVisual.Icon(reward.ItemId);
            rewardIcon.sprite = icon;
            rewardIcon.enabled = icon != null;
            rewardName.text = ItemVisual.Name(reward.ItemId);
            rewardAmount.text = "x" + ItemVisual.Amount(reward.Amount);

            bool doneToday = !month.CanSignToday && month.Signed > 0;
            signedBadge.SetActive(doneToday);
            signedText.text = month.Signed >= month.Rewards.Count ? "All signed" : "Signed-in";
        }

        private void DrawTimer() {
            SignInMonth month = SignInManager.Inst().Month;
            if (month == null) {
                timerText.text = "";
                return;
            }

            TimeSpan left = month.NextDayAt.UtcDateTime - SignInManager.Inst().ServerNow;
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            timerText.text = $"{(int)left.TotalHours:00}:{left.Minutes:00}:{left.Seconds:00}";
        }

        /// <summary>The tick lands: today's square punches in.</summary>
        private static void Stamp(RectTransform cell) {
            Transform check = cell.Find("Check");
            check.localScale = Vector3.one * 1.8f;
            check.DOScale(1f, 0.35f).SetEase(Ease.OutBack);
        }
    }
}
