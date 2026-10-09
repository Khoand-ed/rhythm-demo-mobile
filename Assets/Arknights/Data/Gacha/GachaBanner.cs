using System;
using System.Globalization;
using UnityEngine;

namespace Data.Gacha {
    public enum GachaBannerKind {
        /// <summary>限时, 有UP / Time-limited, with a featured operator.</summary>
        Event,

        /// <summary>常驻 / Permanent, no featured operator.</summary>
        Standard
    }

    /// <summary>
    /// 一个寻访卡池 / One headhunting banner: what the screen shows for it, and the rules it
    /// advertises.
    ///
    /// 数据而不是代码 / A new banner is a new asset in the library, not a code change.
    ///
    /// 规则目前只用于展示 / The rates, pity threshold and costs here are DISPLAY ONLY for now.
    /// Nothing pulls yet; when the server-side gacha lands it becomes the authority on all of
    /// them, the same way the scoring rules are checked against server/data/ruleset.json. At that
    /// point the schedule and rules should come down from the server and this asset keeps only
    /// what is presentation - title, text and which operator to draw. Keeping both copies would
    /// let the screen advertise odds the server does not roll.
    /// </summary>
    [CreateAssetMenu(menuName = "Arknights/Gacha/Banner", fileName = "GachaBanner")]
    public class GachaBanner : ScriptableObject {
        [Header("标识 / Stable id - the server will key pity by this, so never rename it")]
        public string bannerId;
        public GachaBannerKind kind;

        [Header("展示 / Shown on the card")]
        [Tooltip("Large title. A line break splits it over two lines.")]
        [TextArea(1, 3)]
        public string title;

        public string headline;

        [TextArea(2, 4)]
        public string description;

        [Header("角色 / Operators")]
        [Tooltip("Drawn large with the UP badge. Leave empty for a banner with no featured operator.")]
        public string featuredCharId;

        [Tooltip("Everything this banner can give. Drives the per-operator odds in Details and the " +
                 "smaller portraits behind the featured one.")]
        public string[] poolCharIds = new string[0];

        [Header("时间 / UTC, ISO 8601 (2026-12-31T23:59:59Z). Empty end = permanent")]
        public string startsAtUtc;
        public string endsAtUtc;

        [Header("规则 / Display only until the server gacha exists - see the class note")]
        [Range(0f, 1f)] public float rateFiveStar = 0.02f;
        [Range(0f, 1f)] public float rateFourStar = 0.10f;
        [Range(0f, 1f)] public float rateThreeStar = 0.88f;

        [Tooltip("Pulls in a row without a 5-star before the next one is guaranteed.")]
        public int pityThreshold = 30;

        [Tooltip("Item id paid per pull. 1 is Orundum.")]
        public int currencyItemId = 1;
        public int costSingle = 180;
        public int costMulti = 1800;

        public bool IsPermanent => string.IsNullOrEmpty(endsAtUtc);

        public bool HasFeatured => !string.IsNullOrEmpty(featuredCharId);

        /// <summary>
        /// 读不出来就当没有 / An end that does not parse is treated as no end, with a warning,
        /// rather than as already ended - a typo in an asset must not close a banner.
        /// </summary>
        public bool TryGetEnd(out DateTime endUtc) {
            endUtc = default;
            if (IsPermanent) return false;

            if (TryParseUtc(endsAtUtc, out endUtc)) return true;

            Debug.LogWarning($"[GachaBanner] '{name}' has an end time that does not parse: '{endsAtUtc}'. " +
                             "Treating the banner as permanent.");
            return false;
        }

        public bool TryGetStart(out DateTime startUtc) {
            startUtc = default;
            return !string.IsNullOrEmpty(startsAtUtc) && TryParseUtc(startsAtUtc, out startUtc);
        }

        public bool HasEnded(DateTime nowUtc) {
            return TryGetEnd(out DateTime end) && nowUtc >= end;
        }

        /// <summary>
        /// 不受本机区域设置影响 / Parsed with the invariant culture and pinned to UTC, so a device
        /// set to a Vietnamese or any other locale reads the same instant.
        /// </summary>
        private static bool TryParseUtc(string text, out DateTime utc) {
            return DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out utc);
        }
    }
}
