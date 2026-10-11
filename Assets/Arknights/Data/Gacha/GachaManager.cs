using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Data.Player;
using Promuse.Contracts.Gacha;
using Promuse.Net;
using Tools;

namespace Data.Gacha {
    /// <summary>
    /// 寻访的规则和结果都来自服务端 / Headhunting as the server runs it: the banners' rules, the
    /// pulls, and the history.
    ///
    /// The headhunting screen asks here and draws what comes back. Nothing in the client rolls,
    /// prices or counts a pull - see GachaService on the server.
    /// </summary>
    public class GachaManager : Single<GachaManager> {
        private readonly List<GachaBannerInfo> banners = new List<GachaBannerInfo>();

        // 服务端时间减本机时间 / The server's clock minus this device's, measured at the last load.
        // Countdowns run on the server's time: the device clock is the player's to change, and a
        // banner that "opens" early on a phone set forward would only fail to pull.
        private TimeSpan clockOffset;

        /// <summary>False until the banners have been fetched at least once.</summary>
        public bool IsLoaded { get; private set; }

        public IReadOnlyList<GachaBannerInfo> Banners => banners;

        /// <summary>Now, on the server's clock.</summary>
        public DateTime ServerNow => DateTime.UtcNow + clockOffset;

        public async Task<ApiResult<GachaBannerList>> LoadAsync() {
            ApiResult<GachaBannerList> result = await PlayerManager.Inst().Api.GetGachaBannersAsync();
            if (!result.IsSuccess) return result;

            banners.Clear();
            banners.AddRange(result.Value.Banners);
            clockOffset = result.Value.ServerTime.UtcDateTime - DateTime.UtcNow;
            IsLoaded = true;

            return result;
        }

        /// <summary>The rules for one banner, or null when the server is not offering it.</summary>
        public GachaBannerInfo Rules(string bannerId) {
            return banners.Find(b => b.BannerId == bannerId);
        }

        /// <summary>
        /// One press of a pull button. On success the player's whole state is replaced with the
        /// server's - the roster and the bag both changed - and the banner's pity count with it.
        /// </summary>
        public async Task<ApiResult<GachaPullResult>> PullAsync(string bannerId, int times) {
            ApiResult<GachaPullResult> result = await PlayerManager.Inst().Api.PullGachaAsync(bannerId, times);
            if (!result.IsSuccess) return result;

            PlayerData player = PlayerManager.Inst().Get();
            if (player != null) player.ApplyServerState(result.Value.Player);

            int index = banners.FindIndex(b => b.BannerId == bannerId);
            if (index >= 0) banners[index] = banners[index] with { PullsSinceFiveStar = result.Value.PullsSinceFiveStar };

            return result;
        }

        public Task<ApiResult<GachaHistoryPage>> HistoryAsync(long? before, int limit) {
            return PlayerManager.Inst().Api.GetGachaHistoryAsync(before, limit);
        }

        /// <summary>Whether the banner takes pulls right now, on the server's clock.</summary>
        public bool IsOpen(GachaBannerInfo rules) {
            if (rules == null) return false;

            DateTime now = ServerNow;
            if (rules.StartsAt.HasValue && now < rules.StartsAt.Value.UtcDateTime) return false;
            return !rules.EndsAt.HasValue || now < rules.EndsAt.Value.UtcDateTime;
        }
    }
}
