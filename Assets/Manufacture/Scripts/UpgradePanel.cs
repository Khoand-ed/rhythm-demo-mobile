using System;
using Promuse.Contracts.Manufacture;
using Promuse.Contracts.Players;
using TMPro;
using UI;
using UnityEngine;
using UnityEngine.UI;

namespace Promuse.Manufacture {
    /// <summary>
    /// 升级确认 / Raising a station: what changes, what it needs, and the button that does it.
    ///
    /// 先说清楚再按 / Everything the server would check is shown before the press - the blueprint,
    /// held or where to get it, and the LMD against what the player holds - and the confirm button
    /// stays off until both are there. The server still decides; this only saves the player a refusal.
    ///
    /// Built by Arknights/Manufacture/Build Manufacture UI; the field bindings are the contract.
    /// </summary>
    public class UpgradePanel : MonoBehaviour {
        public static readonly Color OkInk = new Color(0.45f, 0.85f, 0.50f, 1f);
        public static readonly Color ShortInk = new Color(0.92f, 0.30f, 0.26f, 1f);

        public TextMeshProUGUI title;
        public TextMeshProUGUI product;
        public TextMeshProUGUI pace;
        public TextMeshProUGUI holds;
        public TextMeshProUGUI blueprint;
        public TextMeshProUGUI cost;
        public TextMeshProUGUI note;
        public Button confirmButton;
        public Button cancelButton;

        /// <summary>The station on show, and the level it was seen at - what the server is asked to raise it from.</summary>
        public string StationId { get; private set; }
        public int FromLevel { get; private set; }

        public bool IsOpen => gameObject.activeSelf;

        public event Action<UpgradePanel> Confirm;

        private bool wired;

        private void Wire() {
            if (wired) return;
            wired = true;
            confirmButton.onClick.AddListener(() => Confirm?.Invoke(this));
            cancelButton.onClick.AddListener(Close);
        }

        /// <summary>
        /// Shows raising <paramref name="station"/> to its next level. <paramref name="held"/> is how many
        /// of an item the player holds, from their bag as the screen knows it; <paramref name="reading"/>
        /// is what the station holds now.
        ///
        /// 站里的也算 / The server collects the station before it takes the price, so what the LMD
        /// station holds counts toward raising it - and the button agrees with the server about it.
        /// </summary>
        public void Open(StationState station, StationReading reading, Func<int, int> held) {
            Wire();
            StationUpgrade next = station.Next;
            if (next == null) {
                Close();
                return;
            }

            StationId = station.StationId;
            FromLevel = station.Level;

            StationLevel from = station.Current;
            StationLevel to = next.To;

            title.text = StationCard.TitleOf(station.StationId) + "  Lv" + from.Level + " → Lv" + to.Level;
            product.text = from.ItemId == to.ItemId
                ? "Makes " + ItemVisual.Name(to.ItemId)
                : "Makes " + ItemVisual.Name(from.ItemId) + " → " + ItemVisual.Name(to.ItemId);
            pace.text = ManufactureFormat.Pace(from) + "   →   " + ManufactureFormat.Pace(to);
            holds.text = ManufactureFormat.Holds(from) + "   →   " + ManufactureFormat.Holds(to);

            bool hasBlueprint = held(next.BlueprintItemId) > 0;
            blueprint.text = hasBlueprint
                ? ItemVisual.Name(next.BlueprintItemId) + " - held, not spent"
                : ItemVisual.Name(next.BlueprintItemId) + " - clear " + next.BlueprintStageCode + " to get it";
            blueprint.color = hasBlueprint ? OkInk : ShortInk;

            bool canPay = true;
            string costText = "";
            foreach (ItemStack price in next.Cost) {
                long fromStation = price.ItemId == from.ItemId ? ManufactureFormat.Items(from, reading.StoredUnits) : 0;
                long missing = price.Amount - (held(price.ItemId) + fromStation);
                if (missing > 0) canPay = false;
                if (costText.Length > 0) costText += "\n";
                costText += ItemVisual.Name(price.ItemId) + "  " + ManufactureFormat.Amount(price.Amount) +
                            "  (you have " + ManufactureFormat.Amount(held(price.ItemId)) +
                            (fromStation > 0 ? ", +" + ManufactureFormat.Amount(fromStation) + " from the station" : "") + ")" +
                            // 差多少 / Short of it, by how much - the reason the button is off.
                            (missing > 0 ? "  - " + ManufactureFormat.Amount(missing) + " short" : "");
            }
            cost.text = costText;
            cost.color = canPay ? Color.white : ShortInk;

            note.text = "What the station holds now is collected first, at its current level.";

            confirmButton.interactable = hasBlueprint && canPay;
            gameObject.SetActive(true);
        }

        public void Close() => gameObject.SetActive(false);
    }
}
