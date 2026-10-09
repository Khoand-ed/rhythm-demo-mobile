using System;
using System.Collections.Generic;
using Data.Char;
using Data.Gacha;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Sub {
    /// <summary>
    /// 一张卡池卡 / One banner card: the rules column on the left, the operator art on the right.
    ///
    /// 两张轮流 / GachaUI has two of these, taking turns under its SwipePager - one on screen, the
    /// other drawn with the neighbour while a page change brings it in. Everything on the card
    /// lives here, so the two can show different banners at the same time; everything around the
    /// card (tabs, wallet, pull buttons) stays on GachaUI and follows whichever card is in front.
    ///
    /// Built twice by Arknights/Gacha/Build Gacha UI.
    /// </summary>
    public class GachaBannerCard : MonoBehaviour {
        [Header("卡面 / Rules column")]
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

        private readonly List<Image> lineupClones = new List<Image>();

        /// <summary>Which banner this card shows, as an index into the library; -1 for none.</summary>
        public int Index { get; private set; } = -1;

        public GachaBanner Banner { get; private set; }

        // ---------------------------------------------------------------------- draw

        public void Draw(GachaBanner banner, int index) {
            Banner = banner;
            Index = index;

            if (banner == null) {
                kindLabel.text = "";
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
            DrawTime();
        }

        /// <summary>
        /// 倒计时 / The time line under the rules: opens in, time remaining, permanent, or ended.
        /// GachaUI calls it once a second.
        /// </summary>
        public void DrawTime() {
            if (Banner == null) return;

            DateTime now = DateTime.UtcNow;

            if (Banner.TryGetStart(out DateTime start) && now < start) {
                timeCaption.text = "OPENS IN";
                timeValue.text = Span(start - now);
                return;
            }

            if (!Banner.TryGetEnd(out DateTime end)) {
                timeCaption.text = "AVAILABILITY";
                timeValue.text = "Permanent";
                return;
            }

            timeCaption.text = "TIME REMAINING";
            timeValue.text = now >= end ? "Ended" : Span(end - now);
        }

        private void DrawArt(GachaBanner banner) {
            foreach (Image clone in lineupClones) Destroy(clone.gameObject);
            lineupClones.Clear();

            CharMeta featured = banner.HasFeatured ? GachaUI.SafeMeta(banner.featuredCharId) : null;

            featuredImage.gameObject.SetActive(featured != null);
            featuredPlate.SetActive(featured != null);
            lineupBehind.gameObject.SetActive(featured != null);
            lineupFull.gameObject.SetActive(featured == null);

            if (featured != null) {
                featuredImage.sprite = featured.GetImage();
                featuredImage.color = featured.GetImage() != null ? Color.white : Color.clear;

                upBadge.SetActive(banner.kind == GachaBannerKind.Event);
                featuredName.text = featured.GetEnglishName();
                featuredStars.sprite = GachaUI.SafeSprite(() => CharManager.Inst().GetStarImage("info_" + featured.GetRarity()));
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

                CharMeta meta = GachaUI.SafeMeta(id);
                if (meta == null || meta.GetImage() == null) continue;

                Image clone = Instantiate(template, root);
                clone.sprite = meta.GetImage();
                clone.gameObject.SetActive(true);
                lineupClones.Add(clone);
            }
        }

        private static string Span(TimeSpan left) {
            if (left.TotalMinutes < 1) return "Less than a minute";
            return left.Days > 0
                ? $"{left.Days}d {left.Hours}h {left.Minutes}m"
                : $"{left.Hours}h {left.Minutes}m";
        }
    }
}
