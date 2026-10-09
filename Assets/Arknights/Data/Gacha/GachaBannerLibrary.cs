using System.Collections.Generic;
using UnityEngine;

namespace Data.Gacha {
    /// <summary>
    /// 卡池列表 / The banners the headhunting screen offers, in tab order.
    ///
    /// The screen holds a direct reference to this asset, so adding a banner is adding it to the
    /// list - no code, and no Resources lookup by name that a rename could break.
    /// </summary>
    [CreateAssetMenu(menuName = "Arknights/Gacha/Banner Library", fileName = "GachaBannerLibrary")]
    public class GachaBannerLibrary : ScriptableObject {
        public List<GachaBanner> banners = new List<GachaBanner>();
    }
}
