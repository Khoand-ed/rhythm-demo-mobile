using System.Collections.Generic;
using System.Threading.Tasks;
using Promuse.Contracts.Missions;
using Promuse.Net;
using Tools;
using UnityEngine;

namespace Data.Mission {
    /// <summary>
    /// 任务板在服务端 / The boards live on the server.
    ///
    /// 这里不再算任何东西 / This used to hold the definitions, the counters in
    /// PlayerPrefs, and every rule about what was claimable. All of that is gone.
    /// The server sends boards with the progress, the points and the claimable
    /// flags already worked out, and this is a cache of the last answer plus the
    /// calls that change it.
    ///
    /// 不再有 Notify / There is no Notify. Counting happens where the thing
    /// actually happens - a purchase advances BuyShopItem inside the purchase
    /// transaction, and a won run advances PlaySong when the run is closed - so a
    /// client that forgot to call it can no longer under-count, and one that
    /// called it twice can no longer over-count.
    /// </summary>
    public class MissionManager : Single<MissionManager> {

        private MissionBoards boards;

        /// <summary>Null until a load has succeeded.</summary>
        public MissionBoards Boards => boards;

        public bool IsLoaded => boards != null;

        public async Task<ApiResult<MissionBoards>> LoadAsync() {
            ApiResult<MissionBoards> result =
                await Player.PlayerManager.Inst().Api.GetMissionBoardsAsync();

            if (result.IsSuccess) boards = result.Value;

            return result;
        }

        /// <summary>Null before the first load, which screens treat as an empty board.</summary>
        public MissionBoard GetBoard(MissionTab tab) {
            if (boards == null) return null;

            return tab == MissionTab.Weekly ? boards.Weekly : boards.Daily;
        }

        /// <summary>
        /// 总分是定义的和 / The denominator of the points bar: every mission on the
        /// board, whether or not it has been claimed. Computed here rather than
        /// sent, because it is a sum of numbers the board already carries.
        /// </summary>
        public int MaxPoints(MissionTab tab) {
            MissionBoard board = GetBoard(tab);

            if (board == null) return 0;

            int total = 0;
            foreach (MissionState mission in board.Missions) total += mission.Points;
            return total;
        }

        /// <summary>
        /// 也算上连锁的 / Whether Claim All would do anything. A mission waiting to
        /// be claimed counts even when no reward is claimable yet: claiming it may
        /// be exactly what pushes the total past the next threshold.
        /// </summary>
        public bool HasClaimable(MissionTab tab) {
            MissionBoard board = GetBoard(tab);

            if (board == null) return false;

            foreach (MissionState mission in board.Missions) {
                if (mission.IsComplete && !mission.IsClaimed) return true;
            }

            foreach (RewardState reward in board.Rewards) {
                if (reward.IsClaimable) return true;
            }

            return false;
        }

        // --------------------------------------------------------------- claims

        public Task<ApiResult<ClaimResult>> ClaimMissionAsync(MissionTab tab, string missionId) {
            return ClaimAsync(() => Player.PlayerManager.Inst().Api.ClaimMissionAsync(tab, missionId));
        }

        public Task<ApiResult<ClaimResult>> ClaimRewardAsync(MissionTab tab, string rewardId) {
            return ClaimAsync(() => Player.PlayerManager.Inst().Api.ClaimRewardAsync(tab, rewardId));
        }

        /// <summary>
        /// 两轮, 顺序要紧 / Claim All runs the two passes in order: every finished
        /// mission first, then every reward the points now reach. Reversing them
        /// would leave a reward unclaimed that the mission pass had just unlocked.
        ///
        /// 客户端循环 / A loop of individual calls rather than one endpoint. Each
        /// claim is already idempotent and already refuses a second take, so the
        /// only thing a bulk endpoint would add is a way for half of it to be
        /// ambiguous when one row fails.
        /// </summary>
        public async Task<ApiResult<ClaimResult>> ClaimAllAsync(MissionTab tab) {
            ApiResult<ClaimResult> last = default;
            bool any = false;

            // 每轮都用最新的板子 / Re-read from the cache each time: a claim returns
            // the whole board, so the next iteration sees what the previous one
            // unlocked.
            for (int guard = 0; guard < 32; guard++) {
                MissionState pending = FirstClaimableMission(tab);

                if (pending == null) break;

                last = await ClaimMissionAsync(tab, pending.MissionId);
                any = true;

                if (!last.IsSuccess) return last;
            }

            for (int guard = 0; guard < 32; guard++) {
                RewardState pending = FirstClaimableReward(tab);

                if (pending == null) break;

                last = await ClaimRewardAsync(tab, pending.RewardId);
                any = true;

                if (!last.IsSuccess) return last;
            }

            if (!any) Debug.Log("[MissionManager] Claim All had nothing to claim.");

            return last;
        }

        // ------------------------------------------------------------ internals

        private async Task<ApiResult<ClaimResult>> ClaimAsync(
            System.Func<Task<ApiResult<ClaimResult>>> claim) {

            ApiResult<ClaimResult> result = await claim();

            if (!result.IsSuccess) return result;

            // 回来的就是最新的 / A claim answers with the whole board and the whole
            // player, so nothing has to be re-fetched and no screen has to guess
            // what changed.
            boards = result.Value.Boards;

            Player.PlayerData player = Player.PlayerManager.Inst().Get();
            if (player != null) player.ApplyServerState(result.Value.Player);

            return result;
        }

        private MissionState FirstClaimableMission(MissionTab tab) {
            MissionBoard board = GetBoard(tab);

            if (board == null) return null;

            foreach (MissionState mission in board.Missions) {
                if (mission.IsComplete && !mission.IsClaimed) return mission;
            }

            return null;
        }

        private RewardState FirstClaimableReward(MissionTab tab) {
            MissionBoard board = GetBoard(tab);

            if (board == null) return null;

            foreach (RewardState reward in board.Rewards) {
                if (reward.IsClaimable) return reward;
            }

            return null;
        }
    }
}
