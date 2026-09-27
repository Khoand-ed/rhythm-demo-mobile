using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Data.Item;
using Promuse.Contracts.Shop;
using Promuse.Net;
using Tools;

namespace Manager {
    /// <summary>
    /// 货架来自服务端 / The shelf comes from the server.
    ///
    /// It used to be a field initialiser reading ShopItemDataList.asset, which
    /// meant the price a player paid was a number their own device held. The
    /// asset is now unused; the catalogue, the prices and the arithmetic all live
    /// on the other side of the wire.
    ///
    /// 先加载再翻页 / Loaded once when the screen opens, then served from memory.
    /// The tabs are wired straight to ShowPriceItem through a UnityEvent in the
    /// prefab and cannot await anything, so the fetch happens in Show() and the
    /// tab handler reads what is already here.
    /// </summary>
    public class ShopManager : Single<ShopManager> {

        private readonly List<ShopItemData> offers = new List<ShopItemData>();

        /// <summary>False until a catalogue has been fetched at least once.</summary>
        public bool IsLoaded { get; private set; }

        public async Task<ApiResult<ShopCatalog>> LoadAsync() {
            ApiResult<ShopCatalog> result =
                await Data.Player.PlayerManager.Inst().Api.GetShopCatalogAsync();

            if (!result.IsSuccess) return result;

            offers.Clear();
            foreach (ShopOffer offer in result.Value.Offers) {
                offers.Add(new ShopItemData(offer));
            }

            IsLoaded = true;
            return result;
        }

        /// <summary>
        /// The offers priced in one currency, which is what a tab shows. Empty
        /// rather than throwing before the first load - a shelf that is not
        /// there yet is a shelf with nothing on it.
        /// </summary>
        public List<ShopItemData> GetShopItems(int priceID) {
            return offers.FindAll(i => i.GetPrice().GetId() == priceID);
        }
    }

    /// <summary>
    /// One row of the shelf, built from what the server sent.
    ///
    /// 保留 ItemStack / Still exposes ItemStack, because the shop screen binds to
    /// GetSell() and GetPrice() and reaches through them for the icon and the
    /// name. Those come from Resources/Meta/Item and stay client-side: the server
    /// holds ids and amounts, and has no opinion about what anything looks like.
    /// </summary>
    [Serializable]
    public class ShopItemData {

        /// <summary>
        /// 买东西要这个 / What a purchase names. The client sends this and a
        /// quantity and nothing else - it cannot name a price.
        /// </summary>
        private readonly Guid offerId;

        private readonly ItemStack sellItem;
        private readonly ItemStack priceItem;

        public ShopItemData(ShopOffer offer) {
            offerId = offer.OfferId;
            sellItem = new ItemStack(offer.SellItemId, offer.SellAmount);
            priceItem = new ItemStack(offer.PriceItemId, offer.PriceAmount);
        }

        public Guid GetOfferId() => offerId;
        public ItemStack GetSell() => sellItem;
        public ItemStack GetPrice() => priceItem;
    }
}
