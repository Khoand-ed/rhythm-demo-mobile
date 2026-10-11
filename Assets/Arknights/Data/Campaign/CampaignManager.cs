using System;
using System.Threading.Tasks;
using Data.Player;
using Promuse.Contracts.Campaign;
using Promuse.Net;
using Tools;

namespace Data.Campaign {
    /// <summary>
    /// 主线 / The main campaign as the server holds it for this player: chapters, stages, which are
    /// open and cleared. What a stage pays is decided when its run closes, never here.
    /// </summary>
    public class CampaignManager : Single<CampaignManager> {
        public CampaignView View { get; private set; }

        /// <summary>The chapter the player last opened, so coming back from a stage lands on it.</summary>
        public string SelectedChapter { get; set; }

        public event Action Changed;

        public async Task<ApiResult<CampaignView>> LoadAsync() {
            ApiResult<CampaignView> result = await PlayerManager.Inst().Api.GetCampaignAsync();
            if (!result.IsSuccess) return result;

            View = result.Value;
            Changed?.Invoke();
            return result;
        }
    }
}
