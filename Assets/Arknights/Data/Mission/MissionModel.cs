using System.Collections.Generic;

namespace Data.Mission {
    // 任务板 / Which board a mission belongs to.
    //
    // 两个板子各算各的 / The two boards count independently: finishing one song advances the
    // play counter on both, but Daily points never spill into Weekly and vice versa.
    public enum MissionTab {
        Daily,
        Weekly
    }

    /// <summary>
    /// 任务计数器 / What a mission watches.
    ///
    /// 同一个板子上盯同一个计数器的任务共用一个数 / Missions on the same board that watch the same
    /// goal share a single counter, which is why "play 1 time" and "play 2 times" both move when
    /// one song ends - they read the same number against different targets.
    ///
    /// 名字会写进存档键 / The member name is written into the PlayerPrefs key, so renaming one
    /// silently resets that counter for everyone who already has progress.
    /// </summary>
    public enum MissionGoal {
        PlaySong,
        BuyShopItem
    }

    /// <summary>一条任务 / One mission row: a counter, a target on it, and the points it pays.</summary>
    public class MissionDef {
        public readonly string Id;
        public readonly string Description;
        public readonly MissionGoal Goal;
        public readonly int Target;
        public readonly int Points;

        public MissionDef(string id, string description, MissionGoal goal, int target, int points) {
            Id = id;
            Description = description;
            Goal = goal;
            Target = target;
            Points = points;
        }
    }

    /// <summary>
    /// 一份奖励 / One reward row.
    ///
    /// RequiredPoints 是累计门槛, 不是消耗 / RequiredPoints is a cumulative threshold, not a price:
    /// the board's point total is compared against it and never spent, so reaching 3 points opens
    /// the 1, 2 and 3 point rewards all at once. Keep the list sorted ascending - the UI draws it
    /// in order and the reader expects the cheapest at the top.
    /// </summary>
    public class RewardDef {
        public readonly string Id;
        public readonly int RequiredPoints;
        public readonly int ItemId;
        public readonly int Amount;

        public RewardDef(string id, int requiredPoints, int itemId, int amount) {
            Id = id;
            RequiredPoints = requiredPoints;
            ItemId = itemId;
            Amount = amount;
        }
    }

    /// <summary>一个板子的全部内容 / Everything one board holds.</summary>
    public class MissionSet {
        public readonly MissionTab Tab;
        public readonly string Title;
        public readonly IList<MissionDef> Missions;
        public readonly IList<RewardDef> Rewards;

        public MissionSet(MissionTab tab, string title, IList<MissionDef> missions, IList<RewardDef> rewards) {
            Tab = tab;
            Title = title;
            Missions = missions;
            Rewards = rewards;
        }

        /// <summary>
        /// 全部任务做完能拿多少分 / The points on offer if every mission is finished. The UI shows
        /// it as the denominator, and it is what makes an unreachable reward threshold obvious.
        /// </summary>
        public int MaxPoints {
            get {
                int total = 0;
                foreach (MissionDef mission in Missions) total += mission.Points;
                return total;
            }
        }
    }
}
