using System.Collections.Generic;
using Data.Item;
using Data.Player;
using Tools;
using UnityEngine;

namespace Data.Mission {
    /// <summary>
    /// 任务进度 / Owns mission progress: the counters, the point total, and what has been handed over.
    ///
    /// 三步, 全部手动 / Three steps, and only the first happens on its own:
    ///   1. 计数 / A counter moves when the player actually does something (Notify).
    ///   2. 领任务 / Reaching a target makes the mission claimable; claiming it pays out its points.
    ///   3. 领奖励 / Points make a reward claimable; claiming it puts the items in the bag.
    ///
    /// 不领就没有 / Nothing skips a step. A finished mission that is never claimed is worth no
    /// points, and points that never reach a threshold hand over nothing. Opening the board grants
    /// nothing at all - the player presses for every step, or presses Claim All to run both claim
    /// passes at once.
    ///
    /// 存在 PlayerPrefs 里 / Progress lives in PlayerPrefs. The project has no save system -
    /// PlayerData is a ScriptableObject loaded out of Resources and nothing writes it back - so
    /// this is the only store here that survives closing the app on a device.
    ///
    /// 只存三样 / Three things are persisted: a counter per (board, goal), a claimed flag per
    /// mission, and a claimed flag per reward. The point total is always recomputed from the
    /// claimed missions, so retuning what a mission pays takes effect at once rather than leaving
    /// a stale number in the save.
    /// </summary>
    public class MissionManager : Single<MissionManager> {
        private const string Prefix = "Promuse.Mission";

        private static readonly MissionTab[] Tabs = { MissionTab.Daily, MissionTab.Weekly };
        private static readonly MissionGoal[] Goals = { MissionGoal.PlaySong, MissionGoal.BuyShopItem };

        private static readonly List<ItemStack> Nothing = new List<ItemStack>();

        private readonly Dictionary<MissionTab, MissionSet> sets = new Dictionary<MissionTab, MissionSet>();

        protected override void Initialization() {
            Seed();
        }

        // ------------------------------------------------------------------ data

        /// <summary>
        /// 两个板子的内容 / The two boards.
        ///
        /// 写死在这里而不是做成资产 / Hard-coded rather than authored as a ScriptableObject: there
        /// are six missions in total and no content pipeline behind them yet. Moving them into an
        /// asset later only changes this method.
        ///
        /// 物品ID对应 Meta/Item / The item ids are the ones under Resources/Meta/Item:
        /// 1 Orundum, 2 LMD, 6 Orirock, 7 Sugar Substitute.
        /// </summary>
        private void Seed() {
            // 奖励门槛必须升序 / Reward thresholds ascend, and the last one on each board is only
            // reachable by claiming every mission on it.
            sets[MissionTab.Daily] = new MissionSet(
                MissionTab.Daily,
                "DAILY MISSIONS",
                new List<MissionDef> {
                    new MissionDef("daily.play1", "Clear any song 1 time(s)", MissionGoal.PlaySong, 1, 1),
                    new MissionDef("daily.play2", "Clear any song 2 time(s)", MissionGoal.PlaySong, 2, 2),
                    new MissionDef("daily.buy1", "Purchase any item from the Store 1 time(s)", MissionGoal.BuyShopItem, 1, 3)
                },
                new List<RewardDef> {
                    new RewardDef("daily.r1", 1, 2, 500),
                    new RewardDef("daily.r2", 2, 1, 100),
                    new RewardDef("daily.r3", 3, 6, 3)
                });

            sets[MissionTab.Weekly] = new MissionSet(
                MissionTab.Weekly,
                "WEEKLY MISSIONS",
                new List<MissionDef> {
                    new MissionDef("weekly.play5", "Clear any song 5 time(s)", MissionGoal.PlaySong, 5, 2),
                    new MissionDef("weekly.play10", "Clear any song 10 time(s)", MissionGoal.PlaySong, 10, 3),
                    new MissionDef("weekly.buy3", "Purchase any item from the Store 3 time(s)", MissionGoal.BuyShopItem, 3, 5)
                },
                new List<RewardDef> {
                    new RewardDef("weekly.r1", 3, 2, 2000),
                    new RewardDef("weekly.r2", 6, 1, 300),
                    new RewardDef("weekly.r3", 10, 7, 5)
                });
        }

        public MissionSet GetSet(MissionTab tab) => sets[tab];

        // ----------------------------------------------------------- reading state

        public int GetCount(MissionTab tab, MissionGoal goal) {
            return PlayerPrefs.GetInt(CounterKey(tab, goal), 0);
        }

        /// <summary>做到几分之几 / Progress on one mission, capped at its target so a bar never overfills.</summary>
        public int GetProgress(MissionTab tab, MissionDef mission) {
            return Mathf.Min(GetCount(tab, mission.Goal), mission.Target);
        }

        /// <summary>做完了 / The work is done. Says nothing about whether the points were taken.</summary>
        public bool IsComplete(MissionTab tab, MissionDef mission) {
            return GetCount(tab, mission.Goal) >= mission.Target;
        }

        public bool IsMissionClaimed(MissionTab tab, MissionDef mission) {
            return PlayerPrefs.GetInt(MissionKey(tab, mission), 0) == 1;
        }

        /// <summary>可以领了 / Finished, and the points are still sitting there waiting.</summary>
        public bool IsMissionClaimable(MissionTab tab, MissionDef mission) {
            return IsComplete(tab, mission) && !IsMissionClaimed(tab, mission);
        }

        /// <summary>
        /// 板子上的分 / Points on a board: the sum of every mission whose points have been claimed.
        ///
        /// 只算领过的 / Claimed, not merely finished. A mission the player has not pressed is worth
        /// nothing yet, which is the whole point of the manual step.
        /// </summary>
        public int GetPoints(MissionTab tab) {
            int total = 0;

            foreach (MissionDef mission in sets[tab].Missions) {
                if (IsMissionClaimed(tab, mission)) total += mission.Points;
            }

            return total;
        }

        public bool IsRewardClaimed(MissionTab tab, RewardDef reward) {
            return PlayerPrefs.GetInt(RewardKey(tab, reward), 0) == 1;
        }

        public bool IsRewardClaimable(MissionTab tab, RewardDef reward) {
            return GetPoints(tab) >= reward.RequiredPoints && !IsRewardClaimed(tab, reward);
        }

        /// <summary>
        /// 有东西可领吗 / Whether Claim All would do anything on this board.
        ///
        /// 也算上连锁的 / A mission waiting to be claimed counts even when no reward is claimable
        /// yet: claiming it may be exactly what pushes the total past the next threshold, and
        /// ClaimAll runs the two passes in that order.
        /// </summary>
        public bool HasClaimable(MissionTab tab) {
            foreach (MissionDef mission in sets[tab].Missions) {
                if (IsMissionClaimable(tab, mission)) return true;
            }

            foreach (RewardDef reward in sets[tab].Rewards) {
                if (IsRewardClaimable(tab, reward)) return true;
            }

            return false;
        }

        // -------------------------------------------------------------- recording

        /// <summary>
        /// 记一次 / Record that something happened, once per event.
        ///
        /// 只动计数 / Counters only. This runs from gameplay and from the shop, where there is no
        /// screen to report a payout on and no reason to hand one over behind the player's back.
        /// Everything past the counter waits for a press on the mission board.
        ///
        /// 两个板子一起加 / Both boards advance: one song played counts towards the daily play
        /// mission and the weekly one at the same time, so callers never choose a board.
        /// </summary>
        public void Notify(MissionGoal goal, int amount = 1) {
            if (amount <= 0) return;

            foreach (MissionTab tab in Tabs) {
                string key = CounterKey(tab, goal);
                PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(key, 0) + amount);
            }

            PlayerPrefs.Save();
        }

        // ---------------------------------------------------------------- claiming

        /// <summary>
        /// 领任务的分 / Takes one mission's points. Returns whether it actually did anything, so a
        /// caller can tell a real claim from a press on something already spent.
        /// </summary>
        public bool ClaimMission(MissionTab tab, MissionDef mission) {
            if (!IsMissionClaimable(tab, mission)) return false;

            PlayerPrefs.SetInt(MissionKey(tab, mission), 1);
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>
        /// 领一份奖励 / Hands over one reward, and returns what was handed over.
        ///
        /// 拿不到存档就不标记 / With no reachable save there is no inventory to add to, so the flag
        /// is left alone and the reward stays claimable. Reaching the player can throw outright
        /// rather than return null - PlayerManager builds itself on first use and its constructor
        /// loads out of an AssetBundle - and a failure there must not escape into the UI.
        /// </summary>
        public List<ItemStack> ClaimReward(MissionTab tab, RewardDef reward) {
            if (!IsRewardClaimable(tab, reward)) return Nothing;

            PlayerData player;

            try {
                player = PlayerManager.Inst().Get();
            } catch (System.Exception e) {
                Debug.LogWarning($"[MissionManager] Cannot reach the player save, reward not handed over: {e.Message}");
                return Nothing;
            }

            if (player == null) {
                Debug.LogWarning("[MissionManager] Nobody is logged in, so there is no inventory to add the reward to.");
                return Nothing;
            }

            player.AddItem(reward.ItemId, reward.Amount);
            player.ItemSort();

            PlayerPrefs.SetInt(RewardKey(tab, reward), 1);
            PlayerPrefs.Save();

            return new List<ItemStack> { new ItemStack(reward.ItemId, reward.Amount) };
        }

        /// <summary>
        /// 一键全领 / Claims everything claimable on one board, and returns every item it handed over.
        ///
        /// 先任务后奖励 / Missions first, then rewards, and the order is the whole trick: claiming
        /// the missions is what raises the point total, so the rewards pass then sees thresholds
        /// that were out of reach a moment earlier. Doing it the other way round would leave the
        /// player pressing the button twice for one press worth of progress.
        ///
        /// 只管当前板子 / One board only. The button belongs to the tab being looked at, and
        /// silently emptying the other one would hide progress the player never saw.
        /// </summary>
        public List<ItemStack> ClaimAll(MissionTab tab) {
            MissionSet set = sets[tab];

            foreach (MissionDef mission in set.Missions) {
                ClaimMission(tab, mission);
            }

            List<ItemStack> granted = new List<ItemStack>();

            foreach (RewardDef reward in set.Rewards) {
                granted.AddRange(ClaimReward(tab, reward));
            }

            return granted;
        }

        // ------------------------------------------------------------------ reset

        /// <summary>
        /// 清空全部进度 / Wipe every counter and claimed flag on both boards.
        ///
        /// 没有按真实时间的日/周重置 / There is deliberately no clock-driven daily or weekly reset:
        /// nothing here reads the calendar, so progress only goes away when this is called. The
        /// Arknights/Mission menu exposes it, which is also what keeps a test run reproducible.
        /// </summary>
        public void ResetAll() {
            foreach (MissionTab tab in Tabs) {
                foreach (MissionGoal goal in Goals) {
                    PlayerPrefs.DeleteKey(CounterKey(tab, goal));
                }

                foreach (MissionDef mission in sets[tab].Missions) {
                    PlayerPrefs.DeleteKey(MissionKey(tab, mission));
                }

                foreach (RewardDef reward in sets[tab].Rewards) {
                    PlayerPrefs.DeleteKey(RewardKey(tab, reward));
                }
            }

            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------------- keys

        private static string CounterKey(MissionTab tab, MissionGoal goal) {
            return $"{Prefix}.{tab}.Counter.{goal}";
        }

        private static string MissionKey(MissionTab tab, MissionDef mission) {
            return $"{Prefix}.{tab}.Mission.{mission.Id}";
        }

        private static string RewardKey(MissionTab tab, RewardDef reward) {
            return $"{Prefix}.{tab}.Claimed.{reward.Id}";
        }
    }
}
