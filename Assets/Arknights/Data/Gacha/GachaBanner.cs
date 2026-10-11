using UnityEngine;

namespace Data.Gacha {
    public enum GachaBannerKind {
        /// <summary>限时, 有UP / Time-limited, with a featured operator.</summary>
        Event,

        /// <summary>常驻 / Permanent, no featured operator.</summary>
        Standard
    }

    /// <summary>
    /// 一个寻访卡池的外观 / How one headhunting banner looks: its title, its text, and which
    /// operator to draw large.
    ///
    /// 规则在服务端 / Nothing here decides what a pull gives. The odds, the guarantee, the price,
    /// the schedule and the pool come from GET /v1/gacha/banners and are matched to this asset by
    /// <see cref="bannerId"/>. They used to be fields here too, advertised on the screen while
    /// nothing enforced them; keeping both copies would let the screen promise odds the server
    /// does not roll.
    ///
    /// 两边都要有 / A banner shows only when both sides have it: the server for the rules, this
    /// asset for the art. One the server does not list (ended, retired) simply does not appear.
    /// </summary>
    [CreateAssetMenu(menuName = "Arknights/Gacha/Banner", fileName = "GachaBanner")]
    public class GachaBanner : ScriptableObject {
        [Header("标识 / Stable id - the server's banner_id, and the key every pity count is kept under")]
        public string bannerId;
        public GachaBannerKind kind;

        [Header("展示 / Shown on the card")]
        [Tooltip("Large title. A line break splits it over two lines.")]
        [TextArea(1, 3)]
        public string title;

        public string headline;

        [TextArea(2, 4)]
        public string description;

        [Tooltip("Drawn large with the UP badge. Leave empty for a banner with no featured operator.")]
        public string featuredCharId;

        public bool HasFeatured => !string.IsNullOrEmpty(featuredCharId);
    }
}
