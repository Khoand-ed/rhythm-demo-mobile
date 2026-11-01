using System;
using System.Collections.Generic;
using TMPro;
using UI;
using UnityEngine;
using UnityEngine.UI;

namespace Promuse.Manufacture {
    /// <summary>
    /// 选谁进房间 / Choosing who stands in the Manufacture room: a grid of square portraits of the
    /// operators the player owns, the way AssistantUI shows them. A press puts an operator in the room or
    /// takes them out; the mark on a tile says where they stand, 1 to 5 from the left.
    ///
    /// 满了就说 / A sixth is refused, and the panel says why under the count. The room may also be left
    /// empty. Nothing is kept until Confirm: Cancel, the veil and Android Back all leave the room as it was.
    ///
    /// Built by Arknights/Manufacture/Build Manufacture UI; the field bindings are the contract. Each tile
    /// is a copy of <see cref="tileTemplate"/>: a Button with Mask/Portrait (Image) and Selected/Order (TMP).
    /// </summary>
    public class RoomPicker : MonoBehaviour {
        public RectTransform grid;
        public RectTransform tileTemplate;
        public TextMeshProUGUI count;
        public TextMeshProUGUI hint;
        public Button confirmButton;
        public Button cancelButton;

        public bool IsOpen => gameObject.activeSelf;

        /// <summary>The choice in order, empty included, once confirmed.</summary>
        public event Action<IReadOnlyList<string>> Confirm;

        private readonly List<RectTransform> tiles = new List<RectTransform>();
        private readonly List<string> owned = new List<string>();
        private readonly List<string> chosen = new List<string>();
        private bool wired;

        /// <summary>Who would stand if confirmed now, left to right.</summary>
        public IReadOnlyList<string> Chosen => chosen;

        private void Wire() {
            if (wired) return;
            wired = true;
            tileTemplate.gameObject.SetActive(false);
            confirmButton.onClick.AddListener(() => Confirm?.Invoke(new List<string>(chosen)));
            cancelButton.onClick.AddListener(Close);
        }

        /// <summary>Shows the owned operators, with those standing now chosen.</summary>
        public void Open(IEnumerable<string> ownedIds, IReadOnlyList<string> standing) {
            Wire();

            owned.Clear();
            foreach (string id in ownedIds) {
                if (!string.IsNullOrEmpty(id) && !owned.Contains(id)) owned.Add(id);
            }

            chosen.Clear();
            chosen.AddRange(RoomSelection.Resolve(standing, owned));

            hint.gameObject.SetActive(false);
            gameObject.SetActive(true);
            Draw();
        }

        public void Close() => gameObject.SetActive(false);

        /// <summary>A tile pressed: the operator into the room or out of it, or a word when it is full.</summary>
        public void Press(string id) {
            bool changed = RoomSelection.Toggle(chosen, id);
            hint.text = changed ? "" : $"The room holds {RoomSelection.Capacity} operators. Take one out first.";
            hint.gameObject.SetActive(!changed);
            Draw();
        }

        private void Draw() {
            while (tiles.Count < owned.Count) tiles.Add(Instantiate(tileTemplate, grid));

            for (int i = 0; i < tiles.Count; i++) {
                bool used = i < owned.Count;
                tiles[i].gameObject.SetActive(used);
                if (used) DrawTile(tiles[i], owned[i]);
            }

            count.text = chosen.Count + " / " + RoomSelection.Capacity;
        }

        private void DrawTile(RectTransform tile, string id) {
            tile.name = "Tile (" + id + ")";

            Image portrait = tile.Find("Mask/Portrait").GetComponent<Image>();
            portrait.sprite = ItemVisual.Avatar(id);
            portrait.enabled = portrait.sprite != null;

            int place = chosen.IndexOf(id);
            tile.Find("Selected").gameObject.SetActive(place >= 0);
            tile.Find("Selected/Order").GetComponent<TMP_Text>().text = place >= 0 ? (place + 1).ToString() : "";

            Button button = tile.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => Press(id));
        }
    }
}
