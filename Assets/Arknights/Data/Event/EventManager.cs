using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Data.Player;
using Promuse.Contracts.Events;
using Promuse.Net;
using Tools;

namespace Data.Events {
    /// <summary>
    /// 活动 / Events as the server runs them: which are on, the one the player is looking at, and
    /// every action in one. Nothing here decides a reward or a count - the screens draw what comes
    /// back, and every action answers with the whole player, which is applied here.
    /// </summary>
    public class EventManager : Single<EventManager> {
        private readonly List<EventSummary> events = new List<EventSummary>();

        // 服务端时间减本机时间 / Server clock minus device clock at the last read. Countdowns run on
        // the server's time, the way the headhunting banners do.
        private TimeSpan clockOffset;

        /// <summary>Events running or upcoming, ending soonest first.</summary>
        public IReadOnlyList<EventSummary> Events => events;

        /// <summary>
        /// 记住看的是哪个 / The event the player last opened, so coming back from one of its stages
        /// lands on the same event rather than on the first in the list.
        /// </summary>
        public string SelectedId { get; set; }

        public DateTime ServerNow => DateTime.UtcNow + clockOffset;

        /// <summary>Raised whenever the list changes - the Home tile listens.</summary>
        public event Action Changed;

        /// <summary>The one to put on the Home tile: running and ending soonest, else the next to start.</summary>
        public EventSummary Featured {
            get {
                EventSummary upcoming = null;
                foreach (EventSummary e in events) {
                    if (e.IsOpen) return e;
                    if (upcoming == null || e.StartsAt < upcoming.StartsAt) upcoming = e;
                }
                return upcoming;
            }
        }

        public async Task<ApiResult<EventList>> LoadAsync() {
            ApiResult<EventList> result = await PlayerManager.Inst().Api.GetEventsAsync();
            if (!result.IsSuccess) return result;

            events.Clear();
            events.AddRange(result.Value.Events);
            clockOffset = result.Value.ServerTime.UtcDateTime - DateTime.UtcNow;

            if (SelectedId != null && !events.Exists(e => e.EventId == SelectedId)) SelectedId = null;

            Changed?.Invoke();
            return result;
        }

        public Task<ApiResult<EventView>> ViewAsync(string eventId) {
            return PlayerManager.Inst().Api.GetEventAsync(eventId);
        }

        public Task<ApiResult<EventActionResult>> CheckInAsync(string eventId) =>
            Apply(PlayerManager.Inst().Api.CheckInAsync(eventId));

        public Task<ApiResult<EventActionResult>> ClaimMissionAsync(string eventId, string missionId) =>
            Apply(PlayerManager.Inst().Api.ClaimEventMissionAsync(eventId, missionId));

        public Task<ApiResult<EventActionResult>> ClaimMilestoneAsync(string eventId, string rewardId) =>
            Apply(PlayerManager.Inst().Api.ClaimEventMilestoneAsync(eventId, rewardId));

        public Task<ApiResult<EventActionResult>> BuyAsync(string eventId, string offerId, int quantity) =>
            Apply(PlayerManager.Inst().Api.BuyEventOfferAsync(eventId, offerId, quantity));

        private static async Task<ApiResult<EventActionResult>> Apply(Task<ApiResult<EventActionResult>> call) {
            ApiResult<EventActionResult> result = await call;

            if (result.IsSuccess) {
                PlayerData player = PlayerManager.Inst().Get();
                if (player != null) player.ApplyServerState(result.Value.Player);
            }

            return result;
        }

        /// <summary>"3d 4h", "5h 12m", "Less than a minute" - the time from now to <paramref name="when"/>.</summary>
        public string Until(DateTimeOffset when) {
            TimeSpan left = when.UtcDateTime - ServerNow;
            if (left <= TimeSpan.Zero) return "Ended";
            if (left.TotalMinutes < 1) return "Less than a minute";
            return left.Days > 0 ? $"{left.Days}d {left.Hours}h" : $"{left.Hours}h {left.Minutes}m";
        }
    }
}
