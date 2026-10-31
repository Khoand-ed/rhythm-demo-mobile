using System;
using System.Collections.Generic;
using Data.Char;
using Data.Player;
using DG.Tweening;
using Manager;
using Promuse.Contracts;
using Promuse.Contracts.Characters;
using Promuse.Contracts.Manufacture;
using Promuse.Net;
using TMPro;
using UI;
using UI.Sub;
using UnityEngine;
using UnityEngine.UI;

namespace Promuse.Manufacture {
    /// <summary>
    /// 制造站界面 / The Manufacture screen: the room and the operators standing in it, its two stations,
    /// collecting, raising a station, and the lock before Manufacture is open.
    ///
    /// 只有它调度 / It orchestrates and nothing else draws itself: the station cards and the upgrade
    /// panel only draw what they are handed, and ManufactureManager is the only one that talks to the
    /// server. The countdowns run from the manager's clock four times a second without a request;
    /// the server is asked again only to act, on opening, and when the app comes back to the front.
    ///
    /// 两层子画布 / The room and the stations each have a Canvas of their own. Every screen shares
    /// one canvas with Home, and a countdown ticking on it would make the whole of it - Home
    /// included - rebuild its batches four times a second; on a child canvas only the child does.
    /// The stations' layer is also switched off for input while the lock or the upgrade panel is
    /// up, rather than trusting draw order across canvases to stop a press reaching it.
    ///
    /// Built by Arknights/Manufacture/Build Manufacture UI; the field bindings are the contract. Opened
    /// from Home's MANUFACTURE tile.
    /// </summary>
    public class ManufactureUI : UIBase {
        public const string UIName = "ManufactureUI";

        /// <summary>What a switched-off Manufacture is called, as the server words it.</summary>
        private const string Feature = "Manufacture";

        [Header("顶栏 / Top bar")]
        public Image lmdIcon;
        public TextMeshProUGUI lmdText;
        public Button collectAllButton;

        [Header("站 / Stations")]
        [Tooltip("The stations' child canvas: switched off for input while an overlay is up.")]
        public CanvasGroup stationLayer;
        public StationCard lmdCard;
        public StationCard expCard;

        [Header("房间 / Room")]
        [Tooltip("Opens the room picker. On the top bar; goes away with the stations.")]
        public Button roomButton;
        public ManufactureRoom room;
        public RoomPicker roomPicker;

        [Header("覆盖 / Overlays")]
        public UpgradePanel upgradePanel;
        public GameObject lockOverlay;
        public TextMeshProUGUI lockText;
        public Button retryButton;

        private float nextTick;
        private bool busy;
        private bool loading;
        private RoomStore roomStore;

        private static ManufactureManager Manager => ManufactureManager.Inst();

        public static void Open() => UIManager.Inst().Show(UIName);

        // ------------------------------------------------------------------ lifecycle

        public override void Init() {
            lmdIcon.sprite = ItemVisual.Icon(OperatorLevels.LmdItemId);
            lmdIcon.enabled = lmdIcon.sprite != null;

            collectAllButton.onClick.AddListener(() => CollectAsync(null));
            retryButton.onClick.AddListener(Load);

            foreach (StationCard card in new[] { lmdCard, expCard }) {
                card.Collect += c => CollectAsync(c.stationId);
                card.Upgrade += OpenUpgrade;
            }

            upgradePanel.Confirm += panel => UpgradeAsync(panel.StationId, panel.FromLevel);
            upgradePanel.Close();

            roomStore = RoomStore.ForCurrentServer();
            roomButton.onClick.AddListener(OpenRoomPicker);
            roomPicker.Confirm += ChooseRoom;
            roomPicker.Close();
        }

        public override void Show() {
            base.Show();
            canvasGroup.alpha = 0;
            canvasGroup.DOFade(1, 0.25f);

            // UIManager.Show calls Show again on a screen already open: subscribe once, however often.
            Manager.Changed -= Draw;
            Manager.Changed += Draw;
            upgradePanel.Close();
            roomPicker.Close();

            // What is known already, at once - a view from the last visit keeps ticking - and the
            // fresh one corrects it when it arrives.
            Draw();
            Load();
        }

        public override void Hide(bool destroy = false) {
            Manager.Changed -= Draw;
            canvasGroup.DOFade(0, 0.2f).OnComplete(() => base.Hide(destroy));
        }

        private void OnDestroy() {
            Manager.Changed -= Draw;
        }

        // 回到前台就重读 / Back from the background: the device's clock may have been moved while the
        // game slept, and another device may have collected. A fresh view settles both.
        private void OnApplicationPause(bool paused) {
            if (!paused && isActiveAndEnabled) Load();
        }

        private void Update() {
            if (Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 0.25f;
            DrawStations();
        }

        /// <summary>
        /// 先关面板 / Android Back closes the room picker or the upgrade panel first, keeping nothing the
        /// picker had not confirmed; only with nothing open over the screen does it leave, through its own
        /// back button.
        /// </summary>
        public override void OnBack() {
            if (roomPicker.IsOpen) {
                roomPicker.Close();
                DrawStations();
                return;
            }
            if (upgradePanel.IsOpen) {
                upgradePanel.Close();
                DrawStations();
                return;
            }
            base.OnBack();
        }

        // ------------------------------------------------------------------ server

        private async void Load() {
            if (loading) return;

            // 客户端先说 / The config already says it is off: say so without asking the server, which
            // would only refuse.
            if (!RemoteConfigManager.Inst().IsOn(f => f.Manufacture)) {
                ShowLock(RemoteConfigManager.Unavailable(Feature), retry: false);
                return;
            }

            loading = true;
            if (Manager.View == null) ShowLock("Loading…", retry: false);

            try {
                ApiResult<ManufactureView> result = await Manager.LoadAsync();
                if (this == null) return;

                // A success arrives through Manager.Changed, which draws.
                if (!result.IsSuccess) {
                    ShowLock(result.Is(ErrorCodes.FeatureDisabled) ? RemoteConfigManager.Unavailable(Feature) : result.Message,
                             retry: !result.Is(ErrorCodes.FeatureDisabled));
                }
            } finally {
                loading = false;
            }
        }

        private async void CollectAsync(string stationId) {
            if (busy) return;
            busy = true;
            DrawStations();

            try {
                ApiResult<ManufactureActionResult> result = await Manager.CollectAsync(stationId);
                if (this == null) return;

                if (result.IsSuccess) {
                    CommonDialogUI.Message(CommonDialogUI.GroundType.WHITE, "Received: " + ItemVisual.List(result.Value.Granted));
                } else {
                    CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, result.Message);
                }
            } finally {
                busy = false;
                if (this != null) Draw();
            }
        }

        private void OpenUpgrade(StationCard card) {
            StationState station = Manager.Station(card.stationId);
            if (station == null || busy) return;

            PlayerData player = PlayerManager.Inst().Get();
            upgradePanel.Open(station, Manager.ReadNow(station), id => player != null ? player.GetItemAmount(id) : 0);
            DrawStations();
        }

        private async void UpgradeAsync(string stationId, int fromLevel) {
            if (busy) return;
            busy = true;
            upgradePanel.confirmButton.interactable = false;

            try {
                ApiResult<ManufactureActionResult> result = await Manager.UpgradeAsync(stationId, fromLevel);
                if (this == null) return;

                upgradePanel.Close();
                if (result.IsSuccess) {
                    string raised = StationCard.TitleOf(stationId) + " is now Lv" + (fromLevel + 1) + ".";
                    if (result.Value.Granted.Count > 0) raised += "\nCollected first: " + ItemVisual.List(result.Value.Granted);
                    CommonDialogUI.Message(CommonDialogUI.GroundType.WHITE, raised);
                } else {
                    CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, result.Message);
                }
            } finally {
                busy = false;
                if (this != null) Draw();
            }
        }

        // ------------------------------------------------------------------ draw

        private void Draw() {
            if (this == null) return;

            PlayerData player = PlayerManager.Inst().Get();
            lmdText.text = ManufactureFormat.Amount(player != null ? player.GetItemAmount(OperatorLevels.LmdItemId) : 0);

            ManufactureView view = Manager.View;
            if (view == null) return;

            if (!view.IsUnlocked) {
                ShowLock("Clear " + view.UnlockStageCode + " to unlock Manufacture.", retry: false);
                return;
            }

            lockOverlay.SetActive(false);
            DrawRoom();
            DrawStations();
        }

        /// <summary>The stations as they stand now by the server's clock - the part redrawn on every tick.</summary>
        private void DrawStations() {
            ManufactureView view = Manager.View;
            bool open = view != null && view.IsUnlocked;

            bool anything = false;
            DateTimeOffset now = new DateTimeOffset(Manager.ServerNow, TimeSpan.Zero);
            foreach (StationCard card in new[] { lmdCard, expCard }) {
                StationState station = open ? Manager.Station(card.stationId) : null;
                card.gameObject.SetActive(station != null);
                if (station == null) continue;

                StationReading reading = ManufactureManager.Read(station, now);
                card.Bind(station, reading, now, busy);
                if (reading.StoredUnits > 0) anything = true;
            }

            // 盖住时不让按 / Nothing under an overlay can be pressed. Collect all sits on the top bar,
            // above the lock, so it goes away with the stations it would collect: a locked, loading or
            // failed screen offers nothing to press but Back and Retry.
            bool covered = lockOverlay.activeSelf || upgradePanel.IsOpen || roomPicker.IsOpen;
            stationLayer.interactable = !covered;
            stationLayer.blocksRaycasts = !covered;

            collectAllButton.gameObject.SetActive(open && !lockOverlay.activeSelf);
            collectAllButton.interactable = !busy && anything;
            roomButton.gameObject.SetActive(open && !lockOverlay.activeSelf);
        }

        private void ShowLock(string message, bool retry) {
            lockText.text = message;
            retryButton.gameObject.SetActive(retry);
            lockOverlay.SetActive(true);
            upgradePanel.Close();
            roomPicker.Close();
            room.Clear();
            DrawStations();
        }

        // ------------------------------------------------------------------ room

        /// <summary>The operators the player owns, in the order the save lists them.</summary>
        private static List<string> OwnedOperators() {
            List<string> ids = new List<string>();
            PlayerData player = PlayerManager.Inst().Get();
            List<CharData> owned = player != null ? player.GetCharList() : null;
            if (owned == null) return ids;

            foreach (CharData character in owned) {
                if (character != null) ids.Add(character.GetId());
            }
            return ids;
        }

        /// <summary>Who stands in the room: the choice kept on this device for this server, or the default.</summary>
        private List<string> RoomChoice() => RoomSelection.Resolve(roomStore.Load(), OwnedOperators());

        // 同一批人不重建 / ManufactureRoom keeps the operators already standing when asked for the same ones,
        // so this runs on every redraw for free.
        private void DrawRoom() => room.Show(RoomChoice());

        private void OpenRoomPicker() {
            ManufactureView view = Manager.View;
            if (busy || view == null || !view.IsUnlocked) return;

            roomPicker.Open(OwnedOperators(), RoomChoice());
            DrawStations();
        }

        /// <summary>
        /// 确认后才保存 / Confirmed: kept for this server, and an operator no longer owned drops out of the
        /// list as it is written.
        /// </summary>
        private void ChooseRoom(IReadOnlyList<string> ids) {
            roomStore.Save(RoomSelection.Resolve(ids, OwnedOperators()));
            roomPicker.Close();
            DrawRoom();
            DrawStations();
        }
    }
}
