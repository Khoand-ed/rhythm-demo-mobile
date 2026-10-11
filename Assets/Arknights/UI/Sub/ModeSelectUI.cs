using Data.Events;
using DG.Tweening;
using Promuse.Contracts.Events;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Sub {
    /// <summary>
    /// 选模式 / Where Home's Operation tile leads: EVENT, the live events, and STORY, the main
    /// campaign - the stages every player has, which open the song list.
    ///
    /// The news screen on Home skips this and goes straight to the event screen.
    ///
    /// Built by Arknights/Events/Build Event UI.
    /// </summary>
    public class ModeSelectUI : UIBase {
        public const string UIName = "ModeSelectUI";

        /// <summary>Opened by name - it lives in the Bridge assembly, which this one cannot see.</summary>
        public const string EventUIName = "EventUI";

        /// <summary>The song list, the main campaign's stages - Bridge too, so also by name.</summary>
        public const string StoryUIName = "SongSelectUI";

        public Button backButton;

        [Header("活动卡 / Event card")]
        public Button eventCard;
        public CanvasGroup eventGroup;
        public TextMeshProUGUI eventTitle;
        public TextMeshProUGUI eventDetail;

        [Header("剧情卡 / Story card - the main campaign")]
        public Button storyCard;

        private float nextTick;

        public override void Init() {
            backButton.onClick.AddListener(() => UIManager.Inst().Hide(UIName, true));
            eventCard.onClick.AddListener(OpenEvents);
            storyCard.onClick.AddListener(() => UIManager.Inst().Show(StoryUIName));
        }

        public override void Show() {
            base.Show();
            canvasGroup.alpha = 0;
            canvasGroup.DOFade(1, 0.25f);

            Draw();
            Refresh();
        }

        public override void Hide(bool destroy = false) {
            canvasGroup.DOFade(0, 0.2f).OnComplete(() => base.Hide(destroy));
        }

        private void Update() {
            if (Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 1f;
            Draw();
        }

        private async void Refresh() {
            await EventManager.Inst().LoadAsync();
            if (this != null) Draw();
        }

        private void Draw() {
            EventSummary featured = EventManager.Inst().Featured;
            int count = EventManager.Inst().Events.Count;

            eventGroup.alpha = featured != null ? 1f : 0.5f;

            if (featured == null) {
                eventTitle.text = "No event right now";
                eventDetail.text = "Check back soon.";
                return;
            }

            eventTitle.text = featured.Title;
            string when = featured.IsOpen
                ? "Ends in " + EventManager.Inst().Until(featured.EndsAt)
                : "Starts in " + EventManager.Inst().Until(featured.StartsAt);
            eventDetail.text = count > 1 ? $"{when}   ·   {count} events" : when;
        }

        private void OpenEvents() {
            if (EventManager.Inst().Events.Count == 0) {
                CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, "There is no event running right now.");
                return;
            }

            UIManager.Inst().Show(EventUIName);
        }
    }
}
