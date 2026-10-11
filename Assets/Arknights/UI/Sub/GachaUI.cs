using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Data.Char;
using Data.Gacha;
using Data.Player;
using DG.Tweening;
using Promuse.Contracts;
using Promuse.Contracts.Gacha;
using Promuse.Net;
using TMPro;
using Tools;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Sub {
    /// <summary>
    /// 寻访 / The headhunting screen: one tab per banner, a card describing the selected one, the
    /// two pull buttons, the results and the history.
    ///
    /// 服务端说了算 / The server decides everything a pull is: which banners are open, their odds,
    /// the guarantee, the price, and every card. This screen draws the banners the server lists
    /// that it also has art for (GachaBannerLibrary), asks GachaManager to pull, and shows what
    /// came back. It used to advertise odds from its own assets while nothing enforced them.
    ///
    /// Built by Arknights/Gacha/Build Gacha UI, which also hooks HomeUI's HEADHUNTING tile.
    /// </summary>
    public class GachaUI : UIBase {
        public const string UIName = "GachaUI";

        private const int OriginiteId = 0;
        private const int OrundumId = 1;

        private static readonly Color Selected = new Color(0.17f, 0.56f, 0.90f, 1f);
        private static readonly Color Unselected = new Color(1f, 1f, 1f, 0.25f);
        private static readonly Color Affordable = Color.white;
        private static readonly Color Short = new Color(1f, 0.45f, 0.45f, 1f);
        private static readonly Color OrundumGem = new Color(0.86f, 0.30f, 0.45f, 1f);
        private static readonly Color PermitGem = new Color(0.36f, 0.80f, 0.86f, 1f);

        [Header("数据 / Art for each banner the server may list, matched by bannerId")]
        public GachaBannerLibrary library;

        [Header("顶栏 / Top bar")]
        public Button backButton;
        public RectTransform tabRow;
        public RectTransform tabTemplate;
        public TextMeshProUGUI originiteAmount;
        public TextMeshProUGUI orundumAmount;
        public Button originitePlus;

        [Header("翻页 / Side arrows, and the two banner cards that page under the swipe")]
        public Button prevButton;
        public Button nextButton;
        public SwipePager swipe;

        [Header("底栏 / Bottom row")]
        public Button shopButton;
        public Button detailsButton;
        public Button historyButton;

        public Button singleButton;
        public TextMeshProUGUI singleCost;
        public Button multiButton;
        public TextMeshProUGUI multiCost;

        [Header("详情 / Details overlay")]
        public GameObject detailsOverlay;
        public TextMeshProUGUI detailsBody;
        public Button detailsClose;

        [Header("结果与记录 / Results and history")]
        public GachaResultView resultView;
        public GachaHistoryView historyView;

        private readonly List<TabView> tabs = new List<TabView>();

        // 两边都有的卡池 / The banners both sides have: listed by the server, drawn by the library.
        private readonly List<GachaBanner> shown = new List<GachaBanner>();

        // 前卡上的卡池 / The banner on the front card. Set when a page change is decided, not when
        // its slide ends, so the tabs and pull buttons switch the moment the finger lifts.
        private int current;
        private float nextTick;
        private bool busy;
        private int loadTicket;

        // ------------------------------------------------------------------ lifecycle

        public override void Init() {
            backButton.onClick.AddListener(Back);
            originitePlus.onClick.AddListener(OpenShop);
            shopButton.onClick.AddListener(OpenShop);

            // 箭头和手指同一个动作 / The arrows page with the same slide a swipe ends in. The pager
            // asks for the neighbour when it needs one, and says which card is in front once a
            // change is decided - the card itself knows which banner it shows.
            prevButton.onClick.AddListener(() => swipe.Slide(-1));
            nextButton.onClick.AddListener(() => swipe.Slide(+1));
            swipe.Prepare += (page, direction) => DrawCard(page, Wrap(current + direction));
            swipe.Paged += page => {
                current = Card(page).Index;
                DrawAround();
            };

            detailsButton.onClick.AddListener(ShowDetails);
            detailsClose.onClick.AddListener(() => detailsOverlay.SetActive(false));
            historyButton.onClick.AddListener(ShowHistory);

            singleButton.onClick.AddListener(() => Pull(1));
            multiButton.onClick.AddListener(() => Pull(10));

            if (resultView != null) resultView.Closed += DrawAround;

            tabTemplate.gameObject.SetActive(false);
        }

        public override void Show() {
            base.Show();
            detailsOverlay.SetActive(false);
            if (resultView != null) resultView.gameObject.SetActive(false);
            if (historyView != null) historyView.gameObject.SetActive(false);

            canvasGroup.alpha = 0;
            canvasGroup.DOFade(1, 0.3f);

            LoadBannersAsync();
        }

        public override void Hide(bool destroy = false) {
            canvasGroup.DOFade(0, 0.2f).OnComplete(() => base.Hide(destroy));
        }

        /// <summary>
        /// UIManager calls this after Show, and again whenever something asks the screen to
        /// refresh - coming back from the shop with new Orundum, for instance.
        /// </summary>
        public override void UpdateView() {
            DrawCurrencies();
            if (shown.Count > 0) Draw();
        }

        // 每秒刷新倒计时和钱包 / Once a second: the countdown, and the wallet with it. SHOP and the
        // Originite + open ShopUI on top of this screen, and nothing tells this screen when that
        // closes - re-reading two item counts a second keeps the balance and the red/white costs
        // honest after a purchase without coupling the two screens.
        private void Update() {
            if (Time.unscaledTime < nextTick) return;

            nextTick = Time.unscaledTime + 1f;
            DrawCurrencies();

            GachaBannerInfo rules = CurrentRules;
            if (rules == null) return;

            // 两张都走 / Both cards tick: mid-swipe, the one arriving is in view too.
            foreach (RectTransform page in swipe.pages) Card(page).DrawTime();

            DrawCosts(rules);
            SetPullable(GachaManager.Inst().IsOpen(rules) && !busy);
        }

        // ------------------------------------------------------------------- banners

        /// <summary>
        /// 每次打开都重新取 / Fetched on every open: a banner may have opened or ended since, and the
        /// pity counts are the player's own and only the server has them right.
        /// </summary>
        private async void LoadBannersAsync() {
            int mine = ++loadTicket;
            SetPullable(false);

            ApiResult<GachaBannerList> result = await GachaManager.Inst().LoadAsync();

            if (this == null || mine != loadTicket) return;

            if (!result.IsSuccess) {
                CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, result.Message);
                return;
            }

            RebuildTabs();
            current = Mathf.Clamp(current, 0, Mathf.Max(0, shown.Count - 1));
            Draw();
        }

        private GachaBanner Current => BannerAt(current);

        private GachaBannerInfo CurrentRules => RulesAt(current);

        private GachaBanner BannerAt(int index) {
            if (shown.Count == 0) return null;
            return shown[Mathf.Clamp(index, 0, shown.Count - 1)];
        }

        private GachaBannerInfo RulesAt(int index) {
            GachaBanner banner = BannerAt(index);
            return banner != null ? GachaManager.Inst().Rules(banner.bannerId) : null;
        }

        /// <summary>
        /// Library order, filtered to what the server lists. Rebuilt only when that set changes,
        /// so reopening the screen does not reshuffle or flash the tabs.
        /// </summary>
        private void RebuildTabs() {
            List<GachaBanner> next = new List<GachaBanner>();

            if (library != null) {
                foreach (GachaBanner banner in library.banners) {
                    if (banner != null && GachaManager.Inst().Rules(banner.bannerId) != null) next.Add(banner);
                }
            }

            foreach (GachaBannerInfo rules in GachaManager.Inst().Banners) {
                if (!next.Exists(b => b.bannerId == rules.BannerId)) {
                    Debug.LogWarning($"[GachaUI] The server lists banner '{rules.BannerId}' but the library has " +
                                     "no art for it, so it is not shown. Add a GachaBanner asset with that bannerId.");
                }
            }

            bool same = next.Count == shown.Count;
            for (int i = 0; same && i < next.Count; i++) same = next[i] == shown[i];
            if (same && tabs.Count == shown.Count) return;

            foreach (TabView tab in tabs) Destroy(tab.Root.gameObject);
            tabs.Clear();
            shown.Clear();
            shown.AddRange(next);

            for (int i = 0; i < shown.Count; i++) {
                int index = i;
                TabView tab = new TabView(Instantiate(tabTemplate, tabRow), shown[i]);
                tab.Button.onClick.AddListener(() => SlideTo(index));
                tabs.Add(tab);
            }

            // 只有一个卡池就不需要翻页 / One banner leaves nothing to page to - by arrow or by swipe.
            prevButton.gameObject.SetActive(shown.Count > 1);
            nextButton.gameObject.SetActive(shown.Count > 1);
            swipe.Pageable = shown.Count > 1;

            if (shown.Count == 0) {
                Debug.LogWarning("[GachaUI] No banner is both listed by the server and drawn by the library.");
            }
        }

        private int Wrap(int index) {
            int count = shown.Count;
            return count == 0 ? 0 : (index % count + count) % count;
        }

        /// <summary>
        /// 页签往它所在的那边滑 / A tab slides towards where it sits: one right of the current tab
        /// pages like ›, one left of it like ‹, and lands straight on its own banner without
        /// passing the ones in between.
        /// </summary>
        private void SlideTo(int index) {
            if (index == current) return;
            swipe.Slide(index > current ? +1 : -1, page => DrawCard(page, index));
        }

        // ---------------------------------------------------------------------- draw

        private void Draw() {
            DrawCard(swipe.Front, current);
            DrawAround();
        }

        private void DrawCard(RectTransform page, int index) {
            Card(page).Draw(BannerAt(index), RulesAt(index), index);
        }

        // 卡外的一切跟着前卡 / Everything around the cards follows the front one: the lit tab,
        // the costs, and whether the pull buttons are lit.
        private void DrawAround() {
            for (int i = 0; i < tabs.Count; i++) tabs[i].SetSelected(i == current);

            GachaBannerInfo rules = CurrentRules;
            if (rules == null) {
                SetPullable(false);
                return;
            }

            DrawCosts(rules);
            SetPullable(GachaManager.Inst().IsOpen(rules) && !busy);
        }

        private static GachaBannerCard Card(RectTransform page) {
            return page.GetComponent<GachaBannerCard>();
        }

        // 结束或未开的卡池按钮变暗 / An ended or unopened banner dims its buttons, but they stay
        // clickable so the press can say why rather than doing nothing.
        //
        // 用 CanvasGroup 不用 CrossFadeAlpha / Through a CanvasGroup on the button, not
        // CrossFadeAlpha on its graphic: the Button's own colour tint writes the same canvas
        // renderer alpha on every hover and press, so a dim set there is undone by the pointer.
        // The group also dims the label and cost along with the face.
        private void SetPullable(bool open) {
            float alpha = open ? 1f : 0.45f;
            SetAlpha(singleButton, alpha);
            SetAlpha(multiButton, alpha);
        }

        private static void SetAlpha(Button button, float alpha) {
            CanvasGroup group = button.GetComponent<CanvasGroup>();
            if (group != null) group.alpha = alpha;
        }

        /// <summary>
        /// 显示真正会扣的东西 / Shows what a press would actually spend: permits when the player
        /// holds enough for the whole request - the server spends those first - otherwise the
        /// banner's currency, red when even that falls short.
        /// </summary>
        private void DrawCosts(GachaBannerInfo rules) {
            DrawCost(singleCost, rules, 1, rules.CostSingle);
            DrawCost(multiCost, rules, 10, rules.CostMulti);
        }

        private static void DrawCost(TextMeshProUGUI label, GachaBannerInfo rules, int times, int cost) {
            bool permits = rules.TicketItemId.HasValue && SafeAmount(rules.TicketItemId.Value) >= times;

            label.text = permits
                ? "×" + times.ToString(CultureInfo.InvariantCulture)
                : cost.ToString("N0", CultureInfo.InvariantCulture);
            label.color = permits || SafeAmount(rules.CurrencyItemId) >= cost ? Affordable : Short;

            Transform gem = label.transform.parent != null ? label.transform.parent.Find("Gem") : null;
            Graphic gemGraphic = gem != null ? gem.GetComponent<Graphic>() : null;
            if (gemGraphic != null) gemGraphic.color = permits ? PermitGem : OrundumGem;
        }

        private void DrawCurrencies() {
            originiteAmount.text = SafeAmount(OriginiteId).ToString("N0", CultureInfo.InvariantCulture);
            orundumAmount.text = SafeAmount(OrundumId).ToString("N0", CultureInfo.InvariantCulture);
        }

        // -------------------------------------------------------------------- actions

        /// <summary>
        /// 抽卡的唯一入口 / The single place a pull starts from. The checks here only decide what to
        /// say before asking; the server checks all of them again and its answer is the one that
        /// counts.
        /// </summary>
        private async void Pull(int times) {
            if (busy) return;

            GachaBanner banner = Current;
            GachaBannerInfo rules = CurrentRules;
            if (banner == null || rules == null) return;

            if (!GachaManager.Inst().IsOpen(rules)) {
                bool early = rules.StartsAt.HasValue && GachaManager.Inst().ServerNow < rules.StartsAt.Value.UtcDateTime;
                CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK,
                    early ? "This banner has not opened yet." : "This banner has ended.");
                return;
            }

            busy = true;
            SetPullable(false);

            ApiResult<GachaPullResult> result;
            try {
                result = await GachaManager.Inst().PullAsync(banner.bannerId, times);
            } finally {
                busy = false;
            }

            if (this == null) return;

            DrawCurrencies();
            DrawAround();

            if (!result.IsSuccess) {
                string message = result.Is(ErrorCodes.InsufficientFunds)
                    ? $"Not enough to headhunt ×{times}. Top up Orundum in the Store, or collect Headhunting Permits."
                    : result.Message;
                CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, message);
                return;
            }

            // 卡面上的保底数也要跟着动 / The card's own copy of the rules is now stale.
            DrawCard(swipe.Front, current);

            if (resultView != null) resultView.Show(result.Value, Footer(result.Value, rules));
        }

        private static string Footer(GachaPullResult result, GachaBannerInfo rules) {
            int certificates = 0;
            foreach (GachaPullOutcome pull in result.Pulls) {
                if (pull.Converted != null) certificates += pull.Converted.Amount;
            }

            string paid = result.Charged.ItemId == rules.TicketItemId
                ? Inv($"Paid {result.Charged.Amount} Headhunting Permit{(result.Charged.Amount == 1 ? "" : "s")}")
                : Inv($"Paid {result.Charged.Amount:N0} Orundum");

            string copies = certificates > 0 ? Inv($"   ·   Duplicates: +{certificates} Purchase Certificates") : "";

            return Inv($"{paid}{copies}   ·   No 5-star for {result.PullsSinceFiveStar} / {rules.PityThreshold}");
        }

        // 商店关掉时回到这里 / Opened through OpenFrom, so closing the shop comes back here rather
        // than dropping the player on the home screen.
        private void OpenShop() {
            ShopUI.OpenFrom(UIName);
        }

        /// <summary>
        /// 返回主界面 / Back to the home screen underneath.
        ///
        /// 刷新主界面货币 / Home's currency row only redraws when something asks it to. It used to be
        /// asked by the shop on every close; now that the shop returns here instead, a purchase made
        /// on the way would leave Home showing the old balance - so closing asks. Wrapped, the same
        /// way MissionUI does it: the refresh runs Lua, and a stale number is a far smaller problem
        /// than this screen failing to close.
        /// </summary>
        private void Back() {
            UIManager.Inst().Hide(UIName, true);

            try {
                UIManager.Inst().Update(BootIntent.Home);
            } catch (Exception e) {
                Debug.LogWarning($"[GachaUI] HomeUI refresh failed, its currency row may be stale: {e.Message}");
            }
        }

        private void ShowHistory() {
            if (historyView == null) return;

            historyView.Open(id => {
                GachaBanner banner = library != null ? library.banners.Find(b => b != null && b.bannerId == id) : null;
                return banner != null ? banner.title.Replace('\n', ' ') : id;
            });
        }

        private void ShowDetails() {
            GachaBanner banner = Current;
            GachaBannerInfo rules = CurrentRules;
            if (banner == null || rules == null) return;

            detailsBody.text = DescribeRules(banner, rules);
            detailsOverlay.SetActive(true);
        }

        /// <summary>
        /// 每个角色的概率 = 星级概率 / 同星级人数 / Each operator's odds are their tier's rate split
        /// evenly across the operators of that tier in the pool - exactly how the server draws:
        /// a tier from the odds, then an operator uniformly within it. Every number here is the
        /// server's.
        /// </summary>
        private static string DescribeRules(GachaBanner banner, GachaBannerInfo rules) {
            StringBuilder s = new StringBuilder();

            s.AppendLine($"<size=120%><b>{banner.title.Replace('\n', ' ')}</b></size>");
            s.AppendLine();

            // 不跟随设备区域 / Every number here is formatted invariantly. On a device set to
            // Vietnamese, N0 would print "1.800" - the thousands separator reading as a decimal
            // point. Percentages are written as basis points / 100 with a literal %, because even
            // the invariant P2 format puts a space before the sign ("2.00 %").
            if (rules.EndsAt.HasValue) {
                string from = rules.StartsAt.HasValue ? Inv($"{rules.StartsAt.Value.UtcDateTime:yyyy-MM-dd HH:mm}") : "now";
                s.AppendLine(Inv($"Period: {from} to {rules.EndsAt.Value.UtcDateTime:yyyy-MM-dd HH:mm} (UTC)"));
            } else {
                s.AppendLine("Period: permanent");
            }

            s.AppendLine(Inv($"Cost: {rules.CostSingle:N0} Orundum per pull, {rules.CostMulti:N0} for ten") +
                         (rules.TicketItemId.HasValue ? " - Headhunting Permits are spent first, one per pull, when you hold enough." : ""));
            s.AppendLine();

            s.AppendLine("<b>Odds</b>");
            Tier(s, rules, 5, rules.RateFiveStar);
            Tier(s, rules, 4, rules.RateFourStar);
            Tier(s, rules, 3, rules.RateThreeStar);
            s.AppendLine();

            s.AppendLine("<b>Guarantee</b>");
            s.AppendLine($"If {rules.PityThreshold} pulls in a row bring no 5-star, the next pull is a 5-star. " +
                         "The count resets the moment a 5-star appears, and each banner keeps its own count. " +
                         "Inside a ten-pull every pull counts on its own.");
            s.AppendLine(Inv($"Your count on this banner: {rules.PullsSinceFiveStar} / {rules.PityThreshold}"));
            s.AppendLine();

            s.AppendLine("<b>Duplicates</b>");
            s.Append("An operator you already own becomes Purchase Certificates instead: ");
            List<string> parts = new List<string>();
            foreach (int rarity in new[] { 5, 4, 3 }) {
                GachaPoolEntry sample = null;
                foreach (GachaPoolEntry entry in rules.Pool) {
                    if (entry.Rarity == rarity) { sample = entry; break; }
                }
                if (sample != null) parts.Add(Inv($"{rarity}-star {sample.DuplicateAmount}"));
            }
            s.Append(string.Join(", ", parts));
            s.AppendLine(".");

            return s.ToString();
        }

        private static void Tier(StringBuilder s, GachaBannerInfo rules, int rarity, int basisPoints) {
            List<GachaPoolEntry> members = new List<GachaPoolEntry>();
            foreach (GachaPoolEntry entry in rules.Pool) {
                if (entry.Rarity == rarity) members.Add(entry);
            }

            s.Append(Inv($"{rarity}-star  {basisPoints / 100f:0.00}%"));

            if (members.Count == 0) {
                s.AppendLine();
                return;
            }

            float each = basisPoints / 100f / members.Count;
            s.Append("   ");
            for (int i = 0; i < members.Count; i++) {
                if (i > 0) s.Append(", ");
                CharMeta meta = SafeMeta(members[i].CharacterId);
                string name = meta != null ? meta.GetEnglishName() : members[i].CharacterId;
                s.Append(Inv($"{name} {each:0.00}%"));
            }
            s.AppendLine();
        }

        // ------------------------------------------------------------------- helpers

        /// <summary>
        /// 查不到就当没有 / A missing operator must cost the screen a portrait, not the screen.
        /// CharManager loads metadata on demand, and that load can throw as well as return null.
        /// </summary>
        internal static CharMeta SafeMeta(string id) {
            if (string.IsNullOrEmpty(id)) return null;

            try {
                return CharManager.Inst().GetMeta(id);
            } catch (Exception e) {
                Debug.LogWarning($"[GachaUI] No metadata for operator '{id}': {e.Message}");
                return null;
            }
        }

        internal static Sprite SafeSprite(Func<Sprite> get) {
            try {
                return get();
            } catch (Exception) {
                return null;
            }
        }

        private static int SafeAmount(int itemId) {
            try {
                return PlayerManager.Inst().Get().GetItemAmount(itemId);
            } catch (Exception e) {
                Debug.LogWarning($"[GachaUI] Could not read item {itemId}: {e.Message}");
                return 0;
            }
        }

        private static string Inv(FormattableString text) {
            return FormattableString.Invariant(text);
        }

        /// <summary>
        /// 一个页签 / One tab. Node names are the contract with the builder: Frame, Crop/Thumb,
        /// Label.
        /// </summary>
        private class TabView {
            public readonly Button Button;
            public readonly RectTransform Root;
            private readonly Image frame;

            public TabView(RectTransform root, GachaBanner banner) {
                Root = root;
                root.gameObject.SetActive(true);
                root.name = "Tab_" + banner.bannerId;

                Button = root.GetComponent<Button>();
                frame = root.Find("Frame").GetComponent<Image>();

                Transform crop = root.Find("Crop");
                Image thumb = crop.Find("Thumb").GetComponent<Image>();
                TextMeshProUGUI label = root.Find("Label").GetComponent<TextMeshProUGUI>();

                // 有主推就放头像, 否则放字 / The featured operator's face where there is one,
                // the banner kind in words where there is not.
                CharMeta meta = banner.HasFeatured ? SafeMeta(banner.featuredCharId) : null;
                Sprite avatar = meta != null ? meta.GetAvatar() : null;

                crop.gameObject.SetActive(avatar != null);
                thumb.sprite = avatar;
                label.gameObject.SetActive(avatar == null);

                // 按图片比例铺满 / Cover with the sprite's own proportions, so a non-square
                // avatar is cropped rather than stretched.
                AspectRatioFitter cover = thumb.GetComponent<AspectRatioFitter>();
                if (cover != null && avatar != null && avatar.rect.height > 0f) {
                    cover.aspectRatio = avatar.rect.width / avatar.rect.height;
                }
                label.text = banner.kind == GachaBannerKind.Standard ? "STANDARD" : banner.bannerId.ToUpperInvariant();
            }

            public void SetSelected(bool selected) {
                frame.color = selected ? Selected : Unselected;
                Root.DOKill();
                Root.DOScale(selected ? 1.08f : 1f, 0.15f);
            }
        }
    }
}
