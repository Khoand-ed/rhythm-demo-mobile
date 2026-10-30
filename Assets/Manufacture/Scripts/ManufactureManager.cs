using System;
using System.Threading.Tasks;
using Data.Player;
using Promuse.Contracts;
using Promuse.Contracts.Manufacture;
using Promuse.Contracts.Players;
using Promuse.Net;
using Tools;

namespace Promuse.Manufacture {
    /// <summary>
    /// 制造站的数据 / Manufacture as the server last described it - the stations, what each held, the
    /// server's clock to count down by - and the one place that asks the server to act. Screens read
    /// it and listen to <see cref="Changed"/>; none of them calls the API.
    ///
    /// 共用的钟 / Between two loads the count goes on here through ManufactureClock, the same
    /// definition the server decides with, from the anchor and rate the last view carried. What a
    /// station shows at a moment is what the server would pay at that moment; nothing is guessed.
    ///
    /// 被拒就重读 / A refusal that means the screen was behind - the station already raised, nothing
    /// left to collect, a blueprint the view did not know about - reads Manufacture again before the
    /// refusal goes back to the screen, so what the player sees next is the truth. A transport failure
    /// keeps the view: nothing is known to have changed.
    /// </summary>
    public class ManufactureManager : Single<ManufactureManager> {
        /// <summary>The refusals that mean the view on screen was behind what the server holds.</summary>
        private static readonly string[] Behind = {
            ErrorCodes.StateConflict,
            ErrorCodes.NothingToClaim,
            ErrorCodes.LevelCapped,
            ErrorCodes.ManufactureLocked,
            ErrorCodes.BlueprintRequired,
        };

        // 服务端时间减本机时间 / Server clock minus device clock at the last view, for the countdowns.
        private TimeSpan clockOffset;

        /// <summary>The last view the server answered with; null until the first load.</summary>
        public ManufactureView View { get; private set; }

        public DateTime ServerNow => DateTime.UtcNow + clockOffset;

        /// <summary>Raised whenever <see cref="View"/> is replaced.</summary>
        public event Action Changed;

        // ------------------------------------------------------------------ calls

        public async Task<ApiResult<ManufactureView>> LoadAsync() {
            ApiResult<ManufactureView> result = await PlayerManager.Inst().Api.GetManufactureAsync();
            if (result.IsSuccess) Accept(result.Value, result.Value.Player);
            return result;
        }

        /// <summary>Collects <paramref name="stationId"/>, or every station that has something when it is null.</summary>
        public Task<ApiResult<ManufactureActionResult>> CollectAsync(string stationId = null) =>
            ActAsync(PlayerManager.Inst().Api.CollectManufactureAsync(stationId));

        /// <summary>Raises <paramref name="stationId"/> from <paramref name="fromLevel"/>, the level the screen showed.</summary>
        public Task<ApiResult<ManufactureActionResult>> UpgradeAsync(string stationId, int fromLevel) =>
            ActAsync(PlayerManager.Inst().Api.UpgradeStationAsync(stationId, fromLevel));

        // ------------------------------------------------------------------ reads

        /// <summary>The station called <paramref name="stationId"/> in the last view; null when there is none.</summary>
        public StationState Station(string stationId) {
            if (View == null) return null;
            foreach (StationState station in View.Stations) {
                if (station.StationId == stationId) return station;
            }
            return null;
        }

        /// <summary>What <paramref name="station"/> holds right now, by the server's clock.</summary>
        public StationReading ReadNow(StationState station) =>
            Read(station, new DateTimeOffset(ServerNow, TimeSpan.Zero));

        /// <summary>What <paramref name="station"/> holds at <paramref name="serverNow"/>, counted from the last view.</summary>
        public static StationReading Read(StationState station, DateTimeOffset serverNow) =>
            ManufactureClock.Read(station.AnchorAt, station.Current.SecondsPerUnit, station.Current.CapacityUnits, serverNow);

        // ---------------------------------------------------------------- answers

        /// <summary>
        /// Takes a view the server answered with: keeps it and the server's clock, applies the save
        /// when one came with it - the bag changed - and tells the screens.
        /// </summary>
        public void Accept(ManufactureView view, PlayerState player = null) {
            View = view;
            clockOffset = view.ServerTime.UtcDateTime - DateTime.UtcNow;

            if (player != null) {
                PlayerData data = PlayerManager.Inst().Get();
                if (data != null) data.ApplyServerState(player);
            }

            Changed?.Invoke();
        }

        private async Task<ApiResult<ManufactureActionResult>> ActAsync(Task<ApiResult<ManufactureActionResult>> call) {
            ApiResult<ManufactureActionResult> result = await call;

            if (result.IsSuccess) {
                Accept(result.Value.View, result.Value.Player);
                return result;
            }

            if (IsBehind(result.Problem)) await LoadAsync();
            return result;
        }

        /// <summary>Whether <paramref name="problem"/> means the view was behind - and reading again would show why.</summary>
        public static bool IsBehind(ApiProblem problem) =>
            problem != null && Array.IndexOf(Behind, problem.Code) >= 0;
    }
}
