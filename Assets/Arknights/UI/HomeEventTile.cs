using Data.Events;
using Promuse.Contracts.Events;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI {
    /// <summary>
    /// 主页的新闻屏 / The little "breaking news" screen on Home, turned into the way into events: it
    /// shows the event running now - or the next one - with its countdown, and opens the mode
    /// select screen when pressed. With no event it keeps its colour bars and PLEASE STAND BY.
    ///
    /// Placed by Arknights/Events/Build Event UI on HomeUI's NewsPanel.
    /// </summary>
    public class HomeEventTile : MonoBehaviour {
        [Tooltip("Shown while there is no event - the colour bars and PLEASE STAND BY.")]
        public GameObject[] idleOnly = new GameObject[0];

        public TextMeshProUGUI title;
        public TextMeshProUGUI detail;
        public Button button;

        private float nextTick;
        private float nextRefresh;

        private void OnEnable() {
            if (button != null) {
                button.onClick.RemoveListener(Open);
                button.onClick.AddListener(Open);
            }

            EventManager.Inst().Changed += Draw;
            Draw();
            Refresh();
        }

        private void OnDisable() {
            EventManager.Inst().Changed -= Draw;
        }

        private void Update() {
            if (Time.unscaledTime >= nextRefresh) Refresh();

            if (Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 1f;
            Draw();
        }

        // 五分钟问一次 / Asked again every five minutes while Home is up, so an event that starts
        // while the player sits here appears without them leaving the screen.
        private async void Refresh() {
            nextRefresh = Time.unscaledTime + 300f;
            await EventManager.Inst().LoadAsync();
        }

        private void Draw() {
            if (this == null) return;

            EventSummary featured = EventManager.Inst().Featured;
            bool any = featured != null;

            foreach (GameObject idle in idleOnly) {
                if (idle != null) idle.SetActive(!any);
            }

            if (title != null) {
                title.gameObject.SetActive(any);
                if (any) title.text = featured.Title;
            }

            if (detail != null) {
                detail.gameObject.SetActive(any);
                if (any) {
                    detail.text = featured.IsOpen
                        ? "ENDS IN " + EventManager.Inst().Until(featured.EndsAt).ToUpperInvariant()
                        : "STARTS IN " + EventManager.Inst().Until(featured.StartsAt).ToUpperInvariant();
                }
            }
        }

        private void Open() {
            UIManager.Inst().Show(Sub.ModeSelectUI.UIName);
        }
    }
}
