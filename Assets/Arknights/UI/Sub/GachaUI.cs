using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Data.Char;
using Data.Gacha;
using Data.Player;
using DG.Tweening;
using TMPro;
using Tools;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Sub {
    /// <summary>
    /// 寻访 / The headhunting screen: one tab per banner, a card describing the selected one, and
    /// the two pull buttons.
    ///
    /// 还不能抽 / Nothing pulls yet. The buttons are real and show the cost against the player's
    /// Orundum, but pressing one says so rather than pretending: the rolls and the pity counter
    /// have to live on the server, because odds decided by the client are odds the client can
    /// change. When that lands, Pull() is the one place that changes.
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

        [Header("数据 / The banners, in tab order")]
        public GachaBannerLibrary library;

        [Header("顶栏 / Top bar")]
        public Button backButton;
        public RectTransform tabRow;
        public RectTransform tabTemplate;
        public TextMeshProUGUI originiteAmount;
        public TextMeshProUGUI orundumAmount;
        public Button originitePlus;

        [Header("翻页 / Side arrows, and swiping the card itself")]
        public Button prevButton;
        public Button nextButton;
        public SwipePager swipe;

        [Header("卡面 / The banner card")]
        public TextMeshProUGUI kindLabel;
        public TextMeshProUGUI titleText;
        public TextMeshProUGUI headlineText;
        public TextMeshProUGUI guaranteeText;
        public TextMeshProUGUI descriptionText;
        public TextMeshProUGUI timeCaption;
        public TextMeshProUGUI timeValue;

        [Header("角色 / Operator art")]
        public Image featuredImage;
        public GameObject featuredPlate;
        public GameObject upBadge;
        public TextMeshProUGUI featuredName;
        public Image featuredStars;
        public TextMeshProUGUI featuredEpithet;

        [Tooltip("Smaller portraits behind the featured operator.")]
        public RectTransform lineupBehind;
        public Image lineupBehindTemplate;

        [Tooltip("The whole pool side by side, for a banner with no featured operator.")]
        public RectTransform lineupFull;
        public Image lineupFullTemplate;

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

        private readonly List<TabView> tabs = new List<TabView>();
        private readonly List<Image> lineupClones = new List<Image>();

        private int current;
        private float nextTick;

        // ------------------------------------------------------------------ lifecycle

        public override void Init() {
            backButton.onClick.AddListener(Back);
            originitePlus.onClick.AddListener(OpenShop);
            shopButton.onClick.AddListener(OpenShop);

            // 箭头先归位再翻 / Arrows and tabs snap the card home first, in case a swipe's slide is
            // still running. The swipe itself pages through Step directly: it calls back while the
            // card is out of sight, and snapping then would cut off its own slide back in.
            prevButton.onClick.AddListener(() => { swipe.Snap(); Step(-1); });
            nextButton.onClick.AddListener(() => { swipe.Snap(); Step(+1); });
            swipe.Paged += Step;

            detailsButton.onClick.AddListener(ShowDetails);
            detailsClose.onClick.AddListener(() => detailsOverlay.SetActive(false));
            historyButton.onClick.AddListener(() =>
                CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK,
                    "No headhunting history yet - pulls are not open."));

            singleButton.onClick.AddListener(() => Pull(1));
            multiButton.onClick.AddListener(() => Pull(10));

            BuildTabs();
        }

        public override void Show() {
            base.Show();
            detailsOverlay.SetActive(false);

            canvasGroup.alpha = 0;
            canvasGroup.DOFade(1, 0.3f);
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
            Draw();
        }

        // 每秒刷新倒计时和钱包 / Once a second: the countdown, and the wallet with it. SHOP and the
        // Originite + open ShopUI on top of this screen, and nothing tells this screen when that
        // closes - re-reading two item counts a second keeps the balance and the red/white costs
        // honest after a purchase without coupling the two screens.
        private void Update() {
            if (Time.unscaledTime < nextTick) return;

            nextTick = Time.unscaledTime + 1f;
            GachaBanner banner = Current;
            if (banner == null) return;

            DrawTime(banner);
            DrawCurrencies();
            DrawCosts(banner);
        }

        // ---------------------------------------------------------------------- tabs

        private GachaBanner Current {
            get {
                if (library == null || library.banners.Count == 0) return null;
                return library.banners[Mathf.Clamp(current, 0, library.banners.Count - 1)];
            }
        }

        private void BuildTabs() {
            tabTemplate.gameObject.SetActive(false);

            int count = library != null ? library.banners.Count : 0;
            for (int i = 0; i < count; i++) {
                int index = i;
                TabView tab = new TabView(Instantiate(tabTemplate, tabRow), library.banners[i]);
                tab.Button.onClick.AddListener(() => { swipe.Snap(); Select(index); });
                tabs.Add(tab);
            }

            // 只有一个卡池就不需要翻页 / One banner leaves nothing to page to - by arrow or by swipe.
            prevButton.gameObject.SetActive(count > 1);
            nextButton.gameObject.SetActive(count > 1);
            swipe.Pageable = count > 1;

            if (count == 0) {
                Debug.LogWarning("[GachaUI] The banner library is empty or unassigned. " +
                                 "Run Arknights/Gacha/Build Gacha UI.");
            }
        }

        private void Step(int delta) {
            int count = tabs.Count;
            if (count == 0) return;
            Select(((current + delta) % count + count) % count);
        }

        private void Select(int index) {
            if (index == current) return;
            current = index;
            Draw();
        }

        // ---------------------------------------------------------------------- draw

        private void Draw() {
            for (int i = 0; i < tabs.Count; i++) tabs[i].SetSelected(i == current);

            GachaBanner banner = Current;
            if (banner == null) {
                titleText.text = "No banners";
                return;
            }

            kindLabel.text = banner.kind == GachaBannerKind.Event ? "EVENT HEADHUNTING" : "STANDARD HEADHUNTING";
            titleText.text = banner.title;
            headlineText.text = banner.headline;
            descriptionText.text = banner.description;

            // 保底说明从数值生成 / Generated from the threshold rather than typed into the asset,
            // so the sentence can never disagree with the number it describes.
            guaranteeText.text = $"No 5-star in {banner.pityThreshold} pulls? The next one is guaranteed.";

            DrawArt(banner);
            DrawTime(banner);
            DrawCosts(banner);
        }

        private void DrawArt(GachaBanner banner) {
            foreach (Image clone in lineupClones) Destroy(clone.gameObject);
            lineupClones.Clear();

            CharMeta featured = banner.HasFeatured ? SafeMeta(banner.featuredCharId) : null;

            featuredImage.gameObject.SetActive(featured != null);
            featuredPlate.SetActive(featured != null);
            lineupBehind.gameObject.SetActive(featured != null);
            lineupFull.gameObject.SetActive(featured == null);

            if (featured != null) {
                featuredImage.sprite = featured.GetImage();
                featuredImage.color = featured.GetImage() != null ? Color.white : Color.clear;

                upBadge.SetActive(banner.kind == GachaBannerKind.Event);
                featuredName.text = featured.GetEnglishName();
                featuredStars.sprite = SafeSprite(() => CharManager.Inst().GetStarImage("info_" + featured.GetRarity()));
                featuredStars.enabled = featuredStars.sprite != null;
                featuredEpithet.text = featured.GetPassive() != null ? featured.GetPassive().passiveName : "";

                FillLineup(lineupBehind, lineupBehindTemplate, banner, banner.featuredCharId);
            } else {
                FillLineup(lineupFull, lineupFullTemplate, banner, null);
            }
        }

        private void FillLineup(RectTransform root, Image template, GachaBanner banner, string skip) {
            template.gameObject.SetActive(false);

            foreach (string id in banner.poolCharIds) {
                if (id == skip) continue;

                CharMeta meta = SafeMeta(id);
                if (meta == null || meta.GetImage() == null) continue;

                Image clone = Instantiate(template, root);
                clone.sprite = meta.GetImage();
                clone.gameObject.SetActive(true);
                lineupClones.Add(clone);
            }
        }

        private void DrawTime(GachaBanner banner) {
            DateTime now = DateTime.UtcNow;

            if (banner.TryGetStart(out DateTime start) && now < start) {
                timeCaption.text = "OPENS IN";
                timeValue.text = Span(start - now);
                SetPullable(false);
                return;
            }

            if (!banner.TryGetEnd(out DateTime end)) {
                timeCaption.text = "AVAILABILITY";
                timeValue.text = "Permanent";
                SetPullable(true);
                return;
            }

            if (now >= end) {
                timeCaption.text = "TIME REMAINING";
                timeValue.text = "Ended";
                SetPullable(false);
                return;
            }

            timeCaption.text = "TIME REMAINING";
            timeValue.text = Span(end - now);
            SetPullable(true);
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

        private void DrawCosts(GachaBanner banner) {
            int owned = SafeAmount(banner.currencyItemId);

            singleCost.text = banner.costSingle.ToString("N0", CultureInfo.InvariantCulture);
            multiCost.text = banner.costMulti.ToString("N0", CultureInfo.InvariantCulture);

            // 买不起就标红 / Red when the player cannot cover it - the one thing worth knowing
            // before the button is pressed.
            singleCost.color = owned >= banner.costSingle ? Affordable : Short;
            multiCost.color = owned >= banner.costMulti ? Affordable : Short;
        }

        private void DrawCurrencies() {
            originiteAmount.text = SafeAmount(OriginiteId).ToString("N0", CultureInfo.InvariantCulture);
            orundumAmount.text = SafeAmount(OrundumId).ToString("N0", CultureInfo.InvariantCulture);
        }

        // -------------------------------------------------------------------- actions

        /// <summary>
        /// 抽卡的唯一入口 / The single place a pull will start from. Until the server owns the
        /// rolls and the pity counter, it explains instead of rolling locally - a client-side roll
        /// would have to be thrown away, and would teach players odds nobody enforces.
        /// </summary>
        private void Pull(int times) {
            GachaBanner banner = Current;
            if (banner == null) return;

            DateTime now = DateTime.UtcNow;

            if (banner.TryGetStart(out DateTime start) && now < start) {
                CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, "This banner has not opened yet.");
                return;
            }

            if (banner.HasEnded(now)) {
                CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, "This banner has ended.");
                return;
            }

            CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK,
                $"Headhunting x{times} is not open yet. Pulls arrive with the server-side update.");
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

        private void ShowDetails() {
            GachaBanner banner = Current;
            if (banner == null) return;

            detailsBody.text = DescribeRules(banner);
            detailsOverlay.SetActive(true);
        }

        /// <summary>
        /// 每个角色的概率 = 星级概率 / 同星级人数 / Each operator's odds are their tier's rate split
        /// evenly across the operators of that tier in the pool, as the GDD lays out.
        /// </summary>
        private static string DescribeRules(GachaBanner banner) {
            StringBuilder s = new StringBuilder();

            s.AppendLine($"<size=120%><b>{banner.title.Replace('\n', ' ')}</b></size>");
            s.AppendLine();

            // 不跟随设备区域 / Every number here is formatted invariantly. On a device set to
            // Vietnamese, N0 would print "1.800" - the thousands separator reading as a decimal
            // point. Percentages are written as value * 100 with a literal %, because even the
            // invariant P2 format puts a space before the sign ("2.00 %").
            if (banner.TryGetEnd(out DateTime end)) {
                string from = banner.TryGetStart(out DateTime start) ? Inv($"{start:yyyy-MM-dd HH:mm}") : "now";
                s.AppendLine(Inv($"Period: {from} to {end:yyyy-MM-dd HH:mm} (UTC)"));
            } else {
                s.AppendLine("Period: permanent");
            }

            s.AppendLine(Inv($"Cost: {banner.costSingle:N0} Orundum per pull, {banner.costMulti:N0} for ten"));
            s.AppendLine();

            s.AppendLine("<b>Odds</b>");
            Tier(s, banner, 5, banner.rateFiveStar);
            Tier(s, banner, 4, banner.rateFourStar);
            Tier(s, banner, 3, banner.rateThreeStar);

            float total = banner.rateFiveStar + banner.rateFourStar + banner.rateThreeStar;
            if (Mathf.Abs(total - 1f) > 0.0005f) {
                s.AppendLine(Inv($"<color=#FF7373>The tier rates add up to {total * 100f:0.00}%, not 100%.</color>"));
            }

            s.AppendLine();
            s.AppendLine("<b>Guarantee</b>");
            s.AppendLine($"If {banner.pityThreshold} pulls in a row bring no 5-star, the next pull is a 5-star. " +
                         "The count resets the moment a 5-star appears, and each banner keeps its own count. " +
                         "Inside a ten-pull every pull counts on its own.");
            s.AppendLine();
            s.Append("<color=#8C8C8C>Pulls are not open yet. These are the odds this banner will use.</color>");

            return s.ToString();
        }

        private static void Tier(StringBuilder s, GachaBanner banner, int rarity, float rate) {
            List<CharMeta> members = new List<CharMeta>();
            foreach (string id in banner.poolCharIds) {
                CharMeta meta = SafeMeta(id);
                if (meta != null && meta.GetRarity() == rarity) members.Add(meta);
            }

            s.Append(Inv($"{rarity}-star  {rate * 100f:0.00}%"));

            if (members.Count == 0) {
                s.AppendLine("   <color=#FF7373>no operator of this tier in the pool</color>");
                return;
            }

            float each = rate / members.Count;
            s.Append("   ");
            for (int i = 0; i < members.Count; i++) {
                if (i > 0) s.Append(", ");
                s.Append(Inv($"{members[i].GetEnglishName()} {each * 100f:0.00}%"));
            }
            s.AppendLine();
        }

        // ------------------------------------------------------------------- helpers

        /// <summary>
        /// 查不到就当没有 / A missing operator must cost the screen a portrait, not the screen.
        /// CharManager loads metadata on demand, and that load can throw as well as return null.
        /// </summary>
        private static CharMeta SafeMeta(string id) {
            if (string.IsNullOrEmpty(id)) return null;

            try {
                return CharManager.Inst().GetMeta(id);
            } catch (Exception e) {
                Debug.LogWarning($"[GachaUI] No metadata for operator '{id}': {e.Message}");
                return null;
            }
        }

        private static Sprite SafeSprite(Func<Sprite> get) {
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

        private static string Span(TimeSpan left) {
            if (left.TotalMinutes < 1) return "Less than a minute";
            return left.Days > 0
                ? $"{left.Days}d {left.Hours}h {left.Minutes}m"
                : $"{left.Hours}h {left.Minutes}m";
        }

        /// <summary>
        /// 一个页签 / One tab. Node names are the contract with the builder: Frame, Crop/Thumb,
        /// Label.
        /// </summary>
        private class TabView {
            public readonly Button Button;
            private readonly Image frame;
            private readonly RectTransform rect;

            public TabView(RectTransform root, GachaBanner banner) {
                rect = root;
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
                rect.DOKill();
                rect.DOScale(selected ? 1.08f : 1f, 0.15f);
            }
        }
    }
}
