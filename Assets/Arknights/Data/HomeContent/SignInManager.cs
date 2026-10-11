using System;
using System.Threading.Tasks;
using Data.Player;
using Promuse.Contracts.SignIn;
using Promuse.Net;
using Tools;

namespace Data.HomeContent {
    /// <summary>
    /// 每月签到 / The month's sign-in as the server counts it. Signing answers with the month and the
    /// whole player, applied here.
    /// </summary>
    public class SignInManager : Single<SignInManager> {
        // 服务端时间减本机时间 / Server clock minus device clock at the last read, for the countdown.
        private TimeSpan clockOffset;

        public SignInMonth Month { get; private set; }

        /// <summary>Today's square is waiting - the dot on Home's calendar icon.</summary>
        public bool CanSign => Month != null && Month.CanSignToday;

        public DateTime ServerNow => DateTime.UtcNow + clockOffset;

        public event Action Changed;

        public async Task<ApiResult<SignInMonth>> LoadAsync() {
            ApiResult<SignInMonth> result = await PlayerManager.Inst().Api.GetSignInAsync();
            if (result.IsSuccess) Set(result.Value);
            return result;
        }

        public async Task<ApiResult<SignInResult>> SignAsync() {
            ApiResult<SignInResult> result = await PlayerManager.Inst().Api.SignInTodayAsync();
            if (!result.IsSuccess) return result;

            PlayerData player = PlayerManager.Inst().Get();
            if (player != null) player.ApplyServerState(result.Value.Player);
            Set(result.Value.Month);
            return result;
        }

        private void Set(SignInMonth month) {
            Month = month;
            clockOffset = month.ServerTime.UtcDateTime - DateTime.UtcNow;
            Changed?.Invoke();
        }
    }
}
