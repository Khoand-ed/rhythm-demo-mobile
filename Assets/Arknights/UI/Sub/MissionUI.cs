using System.Collections.Generic;
using Data.Item;
using Data.Mission;
using DG.Tweening;
using Tools;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Sub {
    /// <summary>
    /// 任务界面 / The mission board: rewards down the left, missions down the right, one pair of
    /// lists per tab.
    ///
    /// 全部手动领 / Nothing is handed over without a press. Opening this screen grants nothing; a
    /// finished mission pays its points when its own CLAIM is pressed, and a reward hands over its
    /// items when its own CLAIM is pressed. CLAIM ALL in the header runs both passes at once, for
    /// the current tab only - a row button never claims anything but its own row.
    ///
    /// 弹窗暂时关着 / The reward popup is off for now. MissionRewardUI and its prefab are still
    /// built and ready; nothing opens them, because a claim is now an explicit press and the press
    /// is its own confirmation. ClaimReward and ClaimAll both return what they handed over, so
    /// passing that to MissionRewardUI.Show in <see cref="AfterClaim"/> is all it takes to bring
    /// the popup back.
    ///
    /// 节点名字是绑定的一部分 / The node paths the row wrappers below look up are load-bearing.
    /// MissionUISetup builds exactly these names and renaming one there breaks the binding here
    /// with nothing but a null reference to explain it.
    /// </summary>
    public class MissionUI : UIBase {
        public const string UIName = "MissionUI";

        public Toggle dailyTab;
        public Toggle weeklyTab;

        public Text boardTitle;
        public Text pointsLabel;
        public Image pointsFill;

        [Header("一键全领 / Claim All")]
        public Button claimAllButton;
        public Text claimAllLabel;

        [Header("模板 / Row templates, both inactive and both inside their own scroll content")]
        public RectTransform rewardTemplate;
        public RectTransform missionTemplate;

        private MissionTab tab = MissionTab.Daily;

        private readonly List<RewardRow> rewardRows = new List<RewardRow>();
        private readonly List<MissionRow> missionRows = new List<MissionRow>();

        public override void Init() {
            BindTab(dailyTab, MissionTab.Daily);
            BindTab(weeklyTab, MissionTab.Weekly);

            claimAllButton.onClick.AddListener(ClaimAll);
        }

        /// <summary>
        /// 两个页签互斥 / The two tabs share a ToggleGroup, so one going on always follows another
        /// going off. Only the on-edge redraws; acting on both would rebuild the lists twice.
        /// </summary>
        private void BindTab(Toggle toggle, MissionTab value) {
            Text label = toggle.transform.GetComponentInChildren<Text>();

            toggle.onValueChanged.AddListener(on => {
                label.DOColor(on ? Color.white : Color.black, 0.2f);
                if (!on) return;

                tab = value;
                Rebuild();
            });
        }

        /// <summary>
        /// 只画, 不发 / Draw, and nothing else. This used to hand over anything owed on open, which
        /// is exactly the behaviour the manual claim replaces.
        /// </summary>
        public override void UpdateView() {
            Rebuild();
        }

        // ----------------------------------------------------------------- claiming

        private void ClaimAll() {
            MissionManager.Inst().ClaimAll(tab);
            AfterClaim();
        }

        /// <summary>
        /// 领完之后 / Runs after any claim: the point total, every row state and the Claim All
        /// button can all have moved, and the currency row on the home screen behind this one may
        /// be out of date now that items have actually changed hands.
        /// </summary>
        private void AfterClaim() {
            RefreshHome();
            Rebuild();
        }

        /// <summary>
        /// 刷新主界面货币 / Rewards land in the same stacks HomeUI's top row reads, and that row
        /// only refreshes when something asks it to. Update is a no-op if HomeUI is not loaded, so
        /// this costs nothing when the board was opened from somewhere else.
        ///
        /// 刷新失败不能连累本界面 / Wrapped because HomeUI is driven by Lua: refreshing it runs
        /// arbitrary script through RuaUI, and anything that throws in there would come back up
        /// here and leave the mission board blank. A stale currency readout is a far smaller
        /// problem than the screen the player is looking at failing to draw.
        /// </summary>
        private static void RefreshHome() {
            try {
                UIManager.Inst().Update(BootIntent.Home);
            } catch (System.Exception e) {
                Debug.LogWarning($"[MissionUI] HomeUI refresh failed, its currency row may be stale: {e.Message}");
            }
        }

        // ------------------------------------------------------------------ drawing

        private void Rebuild() {
            MissionManager manager = MissionManager.Inst();
            MissionSet set = manager.GetSet(tab);

            int points = manager.GetPoints(tab);
            int max = set.MaxPoints;

            boardTitle.text = set.Title;
            pointsLabel.text = $"{points} / {max} PTS";
            SetFill(pointsFill, max > 0 ? (float) points / max : 0f);

            // 没东西领就压灰 / Greyed and dead when there is nothing waiting, so the button says
            // whether a press is worth making rather than only what it would do.
            bool anything = manager.HasClaimable(tab);
            claimAllButton.interactable = anything;
            claimAllLabel.text = anything ? "CLAIM ALL" : "NOTHING TO CLAIM";

            DrawRewards(manager, set);
            DrawMissions(manager, set);
        }

        /// <summary>
        /// 行是复用的 / Rows are pooled rather than rebuilt: switching tabs is a redraw of the same
        /// objects, so the two boards can hold different row counts without churning the hierarchy.
        /// </summary>
        private void DrawRewards(MissionManager manager, MissionSet set) {
            while (rewardRows.Count < set.Rewards.Count) {
                rewardRows.Add(new RewardRow(Instantiate(rewardTemplate, rewardTemplate.parent)));
            }

            for (int i = 0; i < rewardRows.Count; i++) {
                bool used = i < set.Rewards.Count;
                rewardRows[i].SetActive(used);
                if (!used) continue;

                // 捕获副本 / A local copy, because the listener below outlives this iteration.
                RewardDef reward = set.Rewards[i];

                rewardRows[i].Draw(reward,
                                   manager.IsRewardClaimed(tab, reward),
                                   manager.IsRewardClaimable(tab, reward),
                                   () => { manager.ClaimReward(tab, reward); AfterClaim(); });
            }
        }

        private void DrawMissions(MissionManager manager, MissionSet set) {
            while (missionRows.Count < set.Missions.Count) {
                missionRows.Add(new MissionRow(Instantiate(missionTemplate, missionTemplate.parent)));
            }

            for (int i = 0; i < missionRows.Count; i++) {
                bool used = i < set.Missions.Count;
                missionRows[i].SetActive(used);
                if (!used) continue;

                MissionDef mission = set.Missions[i];

                missionRows[i].Draw(mission,
                                    manager.GetProgress(tab, mission),
                                    manager.IsMissionClaimed(tab, mission),
                                    manager.IsMissionClaimable(tab, mission),
                                    () => { manager.ClaimMission(tab, mission); AfterClaim(); });
            }
        }

        /// <summary>
        /// 取元数据不能炸 / Resolves an item's metadata without letting a failure reach the caller.
        ///
        /// 不只是返回 null / A missing meta does not merely come back null: ItemStack.GetItemMeta
        /// goes through Asset.Load, and ABManager throws outright when the bundle behind it is
        /// missing or already held open. Missing bundles are a normal state in this project - the
        /// audio ones are not even in the repository - so a reward row has to survive one. Without
        /// this the whole board fails to draw because one icon could not be found.
        /// </summary>
        private static ItemMeta SafeMeta(ItemStack stack) {
            try {
                return stack.GetItemMeta();
            } catch (System.Exception e) {
                Debug.LogWarning($"[MissionUI] No metadata for item {stack.GetId()}, drawing it without an icon: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 拉进度条 / Sets a progress bar by moving its right anchor.
        ///
        /// 不用 fillAmount / Not fillAmount: that needs Image.Type.Filled, which needs a sprite,
        /// and the only sprite always available is the builtin 9-sliced rounded rect. Filled does
        /// not slice - it stretches - so the rounded caps smear into a blurry pill. An anchor needs
        /// no sprite and stays a crisp rectangle at every width. MissionUISetup.NewFill builds the
        /// bar to suit this: anchored bottom-left, zero offsets, so width is purely the anchor.
        /// </summary>
        private static void SetFill(Image fill, float ratio) {
            RectTransform rect = fill.rectTransform;
            rect.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // ------------------------------------------------------------------- frame

        public override void Show() {
            base.Show();
            canvasGroup.alpha = 0;
            canvasGroup.DOFade(1, 0.3f);
        }

        public override void Hide(bool destroy = false) {
            canvasGroup.DOFade(0, 0.2f).OnComplete(() => base.Hide(destroy));
        }

        // -------------------------------------------------------------------- rows

        /// <summary>
        /// 一行奖励 / One reward row: the point total it opens at, what it pays, and where it is
        /// between locked, waiting to be claimed, and spent. The threshold is never deducted -
        /// passing it leaves every cheaper reward open behind this one.
        /// </summary>
        private class RewardRow {
            private readonly GameObject root;
            private readonly Text requirement;
            private readonly Text itemName;
            private readonly ItemIconComponent icon;
            private readonly Button claim;
            private readonly GameObject done;

            internal RewardRow(RectTransform row) {
                root = row.gameObject;
                requirement = row.GetComponent<Text>("PointBadge/Value");
                itemName = row.GetComponent<Text>("Name");
                icon = row.GetComponent<ItemIconComponent>("Icon");
                claim = row.GetComponent<Button>("Claim");
                done = row.Find("Done").gameObject;
                root.SetActive(true);
            }

            internal void SetActive(bool value) => root.SetActive(value);

            internal void Draw(RewardDef reward, bool claimed, bool claimable, UnityEngine.Events.UnityAction onClaim) {
                requirement.text = reward.RequiredPoints.ToString();

                ItemStack stack = new ItemStack(reward.ItemId, reward.Amount);
                ItemMeta meta = SafeMeta(stack);

                // 缺元数据不要连累整行 / Without a meta, SetMeta would throw on GetRarity().
                // Drawing the row without its icon says more than an exception does.
                if (meta != null) {
                    icon.SetMeta(meta);
                    icon.SetAmount(reward.Amount);
                    itemName.text = meta.GetName();

                    icon.RemoveAllListeners();
                    icon.AddListener(() => ItemInfoUI.Show(stack));
                } else {
                    itemName.text = $"Item {reward.ItemId}";
                }

                // 领过的压暗 / Claimed rewards grey out, like the "Completed" sets in the
                // reference: what is left at full strength is what there is still to play for.
                // The fading is the Done layer's own wash - the row has no CanvasGroup, because
                // fading the whole row would fade the COMPLETED banner with it and let the item
                // name underneath read straight through the word.
                done.SetActive(claimed);

                claim.gameObject.SetActive(claimable);
                claim.onClick.RemoveAllListeners();
                if (claimable) claim.onClick.AddListener(onClaim);
            }
        }

        /// <summary>
        /// 一行任务 / One mission row, in one of three states: still counting, finished with its
        /// points waiting to be taken, or spent.
        /// </summary>
        private class MissionRow {
            private readonly GameObject root;
            private readonly Text description;
            private readonly Text award;
            private readonly Text progressLabel;
            private readonly Image progressFill;
            private readonly GameObject awardBlock;
            private readonly Button claim;
            private readonly Text claimPoints;
            private readonly GameObject done;

            internal MissionRow(RectTransform row) {
                root = row.gameObject;
                description = row.GetComponent<Text>("Description");
                award = row.GetComponent<Text>("Award/Value");
                progressLabel = row.GetComponent<Text>("Award/Progress/Label");
                progressFill = row.GetComponent<Image>("Award/Progress/Fill");
                awardBlock = row.Find("Award").gameObject;
                claim = row.GetComponent<Button>("Claim");
                claimPoints = row.GetComponent<Text>("Claim/Points");
                done = row.Find("Done").gameObject;
                root.SetActive(true);
            }

            internal void SetActive(bool value) => root.SetActive(value);

            internal void Draw(MissionDef mission, int progress, bool claimed, bool claimable,
                               UnityEngine.Events.UnityAction onClaim) {
                description.text = mission.Description;
                award.text = $"x{mission.Points}";
                progressLabel.text = $"{progress}/{mission.Target}";
                SetFill(progressFill, mission.Target > 0 ? (float) progress / mission.Target : 0f);

                claimPoints.text = $"+{mission.Points} PTS";

                // 领取键盖住进度块 / The claim button takes the progress block's place rather than
                // sitting beside it: the two occupy the same corner, and a finished bar reading
                // 2/2 next to a button offering the same thing is one element too many.
                awardBlock.SetActive(!claimable);
                claim.gameObject.SetActive(claimable);
                done.SetActive(claimed);

                claim.onClick.RemoveAllListeners();
                if (claimable) claim.onClick.AddListener(onClaim);
            }
        }
    }
}
