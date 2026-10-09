using System;
using System.Collections.Generic;
using Data.Item;
using Promuse.Contracts.Shop;
using Promuse.Net;
using Data.Player;
using DG.Tweening;
using Manager;
using Tools;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Sub {
    public class ShopUI : UIBase {
        public const string UIName = "ShopUI";

        public Sprite zzpz_icon;
        public Sprite gjpz_icon;
        public Sprite cgpz_icon;
        public Transform shopItemPrefab;

        private Text cur_zzpz;
        private Text cur_gjpz;
        private Text cur_cgpz;
        private Text cur_lmb;
        private Text cur_ys;
        
        private AudioClip clip;
        private AudioClip loop_clip;

        // 从哪里来回哪里去 / The screen that opened the shop, and so the one closing it returns to.
        // Home unless an opener says otherwise through OpenFrom - which is also what every plain
        // Show("ShopUI") still gets, so the Home tile behaves exactly as it always has.
        private string returnTo = BootIntent.Home;

        // 主界面的循环曲 / Home's loop. The shop replaces whatever was playing with its own music,
        // and Home puts its own back when it is shown - but a screen like Gacha has none to put
        // back, so the shop restores Home's when it returns anywhere else.
        private AudioClip homeLoop;
        
        private PlayerData data;
        private ShopItemPanel shopItemPanel;
        private List<ShopItem> list = new List<ShopItem>();

        public override void Init() {
            shopItemPanel = new ShopItemPanel(this, transform.Find("ShopItemPanel"));
            shopItemPanel.transform.gameObject.SetActive(false);
            cur_zzpz = transform.GetComponent<Text>("CurrencyPanel/ZZPZ/ValueGround/txt_value");
            cur_gjpz = transform.GetComponent<Text>("CurrencyPanel/GJPZ/ValueGround/txt_value");
            cur_cgpz = transform.GetComponent<Text>("CurrencyPanel/CGPZ/ValueGround/txt_value");
            cur_lmb = transform.GetComponent<Text>("CurrencyPanel/LMB/ValueGround/txt_value");
            cur_ys = transform.GetComponent<Text>("CurrencyPanel/YS/ValueGround/txt_value");
            clip = Asset.Load<AudioClip>("Audio/Music/Shop", "m_sys_shop_intro");
            loop_clip = Asset.Load<AudioClip>("Audio/Music/Shop", "m_sys_shop_loop");

            // 和 HomeUI.lua 里的同一首 / The same clip HomeUI.lua loads. If the two ever diverge,
            // coming back from the shop through another screen plays the wrong theme.
            homeLoop = Asset.Load<AudioClip>("Audio/Music/Home", "m_sys_void_loop");
        }

        /// <summary>
        /// 打开商店并记住回到哪里 / Opens the shop and remembers which screen to go back to, so a
        /// shop opened from headhunting closes back onto headhunting instead of the home screen.
        /// </summary>
        public static ShopUI OpenFrom(string opener) {
            ShopUI ui = UIManager.Inst().Show(UIName) as ShopUI;
            if (ui != null) ui.returnTo = string.IsNullOrEmpty(opener) ? BootIntent.Home : opener;
            return ui;
        }

        public void GiveYs(int size) {
            data.AddItem(0, size);
            UpdateView();
        }

        public override void Show() {
            base.Show();
            canvasGroup.alpha = 0;
            canvasGroup.DOFade(1, 0.3f);
            SoundManager.Inst().PlayMusic(clip, false, () => {
                SoundManager.Inst().PlayMusic(loop_clip, true);
            });

            LoadShelfAsync();
        }

        /// <summary>
        /// 标签页不能 await / The tabs are wired straight to ShowPriceItem through
        /// a UnityEvent in the prefab, so they cannot await anything. The fetch
        /// happens once here and the tab handler reads what is already in memory.
        ///
        /// 6 是第一个分区 / Every offer is priced in 6 (Orirock), which is the
        /// first tab, so that is the shelf drawn on opening.
        /// </summary>
        private async void LoadShelfAsync() {
            ApiResult<ShopCatalog> result = await ShopManager.Inst().LoadAsync();

            if (!result.IsSuccess) {
                CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, result.Message);
                return;
            }

            ShowPriceItem(6);
        }

        public override void Hide(bool destroy = false) {
            canvasGroup.DOFade(0, 0.2f).OnComplete(() => { 
                base.Hide(destroy);
            });

            // 用一次就回到默认 / Spent on this close and reset, because the shop is cached rather
            // than destroyed: the next time it opens from the Home tile it must return Home again.
            string target = returnTo;
            returnTo = BootIntent.Home;

            // Home 显示时自己会放音乐, 别的界面不会 / Home restarts its own music when shown;
            // anywhere else would be left playing the shop's, so put Home's loop back first.
            if (target != BootIntent.Home) SoundManager.Inst().PlayMusic(homeLoop, true);

            UIManager.Inst().Show(target);
        }

        public override void UpdateView() {
            data = PlayerManager.Inst().Get();
            cur_zzpz.text = data.GetItemAmount(6).ToString();
            cur_gjpz.text = data.GetItemAmount(7).ToString();
            cur_cgpz.text = data.GetItemAmount(8).ToString();
            cur_lmb.text = data.GetItemAmount(2).ToString();
            cur_ys.text = data.GetItemAmount(0).ToString();
        }

        public void ShowPriceItem(int id) {
            List<ShopItemData> dataList = ShopManager.Inst().GetShopItems(id);
            for (int i = 0; i < dataList.Count || i < list.Count; i++) {
                if (i < dataList.Count) {
                    if (list.Count <= i) {
                        list.Add(new ShopItem(this,Instantiate(shopItemPrefab, shopItemPrefab.parent)));
                    }
                    list[i].SetShopData(dataList[i]);
                    list[i].transform.gameObject.SetActive(true);
                } else {
                    list[i].transform.gameObject.SetActive(false);
                }
            }
        }
        
        public Sprite GetPriceIcon(int id) {
            switch (id) {
                case 6:
                    return zzpz_icon;
                case 7:
                    return gjpz_icon;
                case 8:
                    return cgpz_icon;
            }
            return null;
        }
        
        public class ShopItem {
            internal Transform transform;
            private ShopUI ui;
            
            private ItemIconComponent iic;
            private Text txt_name;
            private Image img_icon;
            private Text txt_price;

            public ShopItem(ShopUI ui, Transform transform) {
                this.transform = transform;
                this.ui = ui;
                iic = transform.GetComponent<ItemIconComponent>();
                txt_name = transform.GetComponent<Text>("Name_Ground/Text");
                img_icon = transform.GetComponent<Image>("Price_Ground/Layout/Price_Icon");
                txt_price = transform.GetComponent<Text>("Price_Ground/Layout/Price");
            }

            public void SetShopData(ShopItemData data) {
                iic.SetMeta(data.GetSell().GetItemMeta());
                iic.SetAmount(data.GetSell().GetAmount());
                txt_name.text = data.GetSell().GetItemMeta().GetName();
                img_icon.sprite = ui.GetPriceIcon(data.GetPrice().GetId());
                txt_price.text = data.GetPrice().GetAmount().ToString();
                iic.RemoveAllListeners();
                iic.AddListener(() => {
                    ui.shopItemPanel.SetShopData(data);
                    ui.shopItemPanel.Show();
                });
            }
        }
        
        public class ShopItemPanel {
            internal Transform transform;
            private CanvasGroup canvasGroup;
            private Material blurryShader;
            private ShopUI ui;
            private ShopItemData data;
            
            private Text txt_sell_name;
            private Image img_sell_use_image;
            private Text txt_sell_use_info;
            private Text txt_sell_amount;
            private Image img_price_icon;
            private Text txt_price_amount;
            private Text txt_buy_amount;
            private Image img_all_price_icon;
            private Text txt_all_price_amount;

            private Button buyButton;

            private int buy_amount;
            private int all_price_amount;

            /// <summary>
            /// 服务端收钱 / The server takes the payment and answers with what the
            /// bag became. The client no longer adds the goods and subtracts the
            /// price itself - it asks, and applies what comes back.
            /// </summary>
            private async void BuyAsync() {
                // 防重复提交 / A second press while the first is in flight is a
                // second purchase. The idempotency key would not stop that: two
                // presses are two different requests, not a retry of one.
                buyButton.interactable = false;

                ApiResult<PurchaseResult> result = await PlayerManager.Inst().Api
                    .PurchaseAsync(data.GetOfferId(), buy_amount);

                buyButton.interactable = true;

                if (!result.IsSuccess) {
                    // INSUFFICIENT_FUNDS reads as "not enough to pay for that",
                    // which is the server's wording and already what a player
                    // needs to be told.
                    CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, result.Message);
                    return;
                }

                // 任务由服务端记 / The BuyShopItem counter used to be ticked here.
                // It is now advanced inside the purchase transaction, one tick per
                // checkout rather than one per unit, so a client that died between
                // the two can no longer under-count and a patched one can no
                // longer count as often as it likes.

                ui.data.ApplyServerState(result.Value.Player);

                CommonDialogUI.Message(CommonDialogUI.GroundType.WHITE,
                    "Purchased: " + data.GetSell().GetItemMeta().GetName()
                    + " x " + result.Value.Granted.Amount).AddBackListener(Hide);

                ui.UpdateView();
            }

            private void SetBuyAmount(int value) {
                buy_amount = value;
                txt_buy_amount.text = value.ToString();
                all_price_amount = data.GetPrice().GetAmount() * value;
                txt_all_price_amount.text = all_price_amount.ToString();
            }

            public ShopItemPanel(ShopUI ui, Transform transform) {
                this.transform = transform;
                this.ui = ui;
                canvasGroup = transform.GetComponent<CanvasGroup>();
                blurryShader = transform.GetComponent<Image>().material;
                txt_sell_name = transform.GetComponent<Text>("ItemNameGround/Text");
                img_sell_use_image = transform.GetComponent<Image>("ItemIconGround/Image");
                txt_sell_use_info = transform.GetComponent<Text>("ItemUseInfo");
                txt_sell_amount = transform.GetComponent<Text>("ItemAmountGround/ItemAmount");
                img_price_icon = transform.GetComponent<Image>("PriceIcon");
                txt_price_amount = transform.GetComponent<Text>("PriceAmount");
                txt_buy_amount = transform.GetComponent<Text>("BuyAmountGround/Text");
                img_all_price_icon = transform.GetComponent<Image>("AllPriceIcon");
                txt_all_price_amount = transform.GetComponent<Text>("AllPriceAmount");
                transform.GetComponent<Button>("CloseButton").onClick.AddListener(Hide);// 关闭界面事件
                
                transform.GetComponent<Button>("AddButton").onClick.AddListener(() => {
                    // 增加数量事件
                    if (ui.data.HasItem(data.GetPrice().GetId(), data.GetPrice().GetAmount() * (buy_amount + 1))) {
                        SetBuyAmount(Math.Min(buy_amount + 1, 99));
                    }
                });
                transform.GetComponent<Button>("TakeButton").onClick.AddListener(() => {
                    // 减少数量事件
                    SetBuyAmount(Math.Max(buy_amount - 1, 0));
                });
                transform.GetComponent<Button>("MinButton").onClick.AddListener(() => {
                    // 最少数量事件
                    SetBuyAmount(ui.data.HasItem(data.GetPrice()) ? 1 : 0);
                });
                transform.GetComponent<Button>("MaxButton").onClick.AddListener(() => {
                    // 最多数量事件
                    SetBuyAmount(Math.Min(ui.data.GetItemAmount(data.GetPrice().GetId()) / data.GetPrice().GetAmount(), 99));
                });
                buyButton = transform.GetComponent<Button>("BuyButton");
                buyButton.onClick.AddListener(() => {
                    // 购买事件
                    if (buy_amount == 0) return;

                    BuyAsync();
                });
            }

            public void SetShopData(ShopItemData data) {
                this.data = data;
                ItemMeta sellMeta = data.GetSell().GetItemMeta();
                txt_sell_name.text = sellMeta.GetName();
                img_sell_use_image.sprite = sellMeta.GetIcon();
                txt_sell_use_info.text = sellMeta.GetUseInfo();
                txt_sell_amount.text = "x " + data.GetSell().GetAmount();
                img_price_icon.sprite = ui.GetPriceIcon(data.GetPrice().GetId());
                txt_price_amount.text = data.GetPrice().GetAmount().ToString();
                SetBuyAmount(0);
                img_all_price_icon.sprite = ui.GetPriceIcon(data.GetPrice().GetId());
                
            }

            public void Show() {
                blurryShader.SetFloat("_Size",0);
                float value = 0;
                DOTween.To(() => value,v => value = v,160,0.5f).OnUpdate(() => blurryShader.SetFloat("_Size",value));
                transform.gameObject.SetActive(true);
                canvasGroup.alpha = 0;
                canvasGroup.DOFade(1, 0.3f);
                LayoutRebuilder.ForceRebuildLayoutImmediate(txt_sell_name.transform.parent as RectTransform);
            }

            public void Hide() {
                float value = blurryShader.GetFloat("_Size");
                DOTween.To(() => value,v => value = v,0,0.2f).OnUpdate(() => blurryShader.SetFloat("_Size",value));
                canvasGroup.DOFade(0, 0.2f).OnComplete(() => {
                    transform.gameObject.SetActive(false);
                });
            }
        }
    }
}