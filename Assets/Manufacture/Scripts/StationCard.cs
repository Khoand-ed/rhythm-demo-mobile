using System;
using Promuse.Contracts.Manufacture;
using TMPro;
using UI;
using UnityEngine;
using UnityEngine.UI;

namespace Promuse.Manufacture {
    /// <summary>
    /// 一个制造站 / One station on the Manufacture screen: what it makes, what it holds, how long until
    /// the next unit and until it is full, and its two buttons.
    ///
    /// 只画数据 / It draws what it is handed and nothing else - no API, no manager - so the screen
    /// decides when to draw it, and a preview can draw it from a sample view.
    ///
    /// Built by Arknights/Manufacture/Build Manufacture UI; the field bindings are the contract.
    /// </summary>
    public class StationCard : MonoBehaviour {
        public static readonly Color FullInk = new Color(0.92f, 0.30f, 0.26f, 1f);

        /// <summary>Collect's face and label while the store is full - the card's yellow, with dark ink on it.</summary>
        public static readonly Color ReadyFace = new Color(0.96f, 0.73f, 0.05f, 1f);
        public static readonly Color ReadyInk = new Color(0.05f, 0.055f, 0.06f, 1f);

        [Tooltip("lmd or exp - ManufactureStations. Which station this card draws.")]
        public string stationId;

        public TextMeshProUGUI title;
        public TextMeshProUGUI level;
        public Image productIcon;
        public TextMeshProUGUI productName;
        public TextMeshProUGUI stored;

        [Tooltip("Stretched by its right anchor, 0 to 1 - no sprite needed.")]
        public RectTransform progressFill;

        public TextMeshProUGUI next;
        public TextMeshProUGUI full;
        public Button collectButton;
        public Button upgradeButton;

        public event Action<StationCard> Collect;
        public event Action<StationCard> Upgrade;

        private bool wired;
        private Color nextInk;
        private Graphic collectFace;
        private TMP_Text collectLabel;
        private Color collectFaceInk;
        private Color collectLabelInk;

        private void Wire() {
            if (wired) return;
            wired = true;

            // 记下预制体的颜色 / The prefab's own colours, put back when the card stops calling out.
            nextInk = next.color;
            collectFace = collectButton.targetGraphic;
            collectLabel = collectButton.GetComponentInChildren<TMP_Text>(true);
            if (collectFace != null) collectFaceInk = collectFace.color;
            if (collectLabel != null) collectLabelInk = collectLabel.color;

            collectButton.onClick.AddListener(() => Collect?.Invoke(this));
            upgradeButton.onClick.AddListener(() => Upgrade?.Invoke(this));
        }

        /// <summary>The station's display name: "LMD STATION", "EXP STATION".</summary>
        public static string TitleOf(string stationId) =>
            stationId == ManufactureStations.Lmd ? "LMD STATION" : stationId == ManufactureStations.Exp ? "EXP STATION" : stationId.ToUpperInvariant();

        /// <summary>
        /// Draws <paramref name="station"/> as it stands at <paramref name="serverNow"/> - <paramref name="reading"/>
        /// is ManufactureClock's count for that moment. <paramref name="busy"/> while a request is out.
        /// </summary>
        public void Bind(StationState station, StationReading reading, DateTimeOffset serverNow, bool busy) {
            Wire();
            StationLevel current = station.Current;
            bool top = station.Next == null;

            title.text = TitleOf(station.StationId);
            level.text = top ? "Lv" + station.Level + "  MAX" : "Lv" + station.Level;

            productIcon.sprite = ItemVisual.Icon(current.ItemId);
            productIcon.enabled = productIcon.sprite != null;
            productName.text = ItemVisual.Name(current.ItemId);
            stored.text = ManufactureFormat.Stored(current, reading.StoredUnits);

            SetFill(ManufactureFormat.Progress(current, reading, serverNow));

            bool stopped = reading.NextUnitAt == null;
            if (reading.NextUnitAt is DateTimeOffset nextAt) {
                next.text = "+" + ManufactureFormat.Amount(current.UnitAmount) + " in " + ManufactureFormat.Countdown(nextAt - serverNow);
                next.color = nextInk;
                full.text = "Full in " + ManufactureFormat.Countdown(reading.FullAt.Value - serverNow);
            } else {
                // 满了就停 / A full store has stopped: nothing more comes until it is collected, and
                // collecting starts the clock again from that moment.
                next.text = "FULL – production stopped";
                next.color = FullInk;
                full.text = "Collect to start it again";
            }

            // 满了就醒目 / A full store's Collect stands out: every minute it waits is production lost.
            if (collectFace != null) collectFace.color = stopped ? ReadyFace : collectFaceInk;
            if (collectLabel != null) collectLabel.color = stopped ? ReadyInk : collectLabelInk;

            collectButton.interactable = !busy && reading.StoredUnits > 0;
            upgradeButton.gameObject.SetActive(!top);
            upgradeButton.interactable = !busy;
        }

        private void SetFill(float done) {
            if (progressFill == null) return;
            progressFill.anchorMax = new Vector2(done, progressFill.anchorMax.y);
        }
    }
}
