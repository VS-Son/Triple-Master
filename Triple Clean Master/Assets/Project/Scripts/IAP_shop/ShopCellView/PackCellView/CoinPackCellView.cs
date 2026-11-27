using System;
using EnhancedUI.EnhancedScroller;
using Project.Scripts.IAP_shop.ShopData;
using Project.Scripts.Constants;
using Project.Scripts.Manager;
using Project.Scripts.Message;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Scripts.IAP_shop.ShopCellView.PackCellView
{
    public class CoinPackCellView : EnhancedScrollerCellView,IShopCellView
    {
        public string id;
        public Image iconCoin;
        public TMP_Text textAmountCoin;
        public OfferType offerType;
        public TMP_Text priceText;
        public bool isPurchased;
        private CoinPackData _data;
        public Action<IShopCellView> Onclick { get; set; }

        public void OnClickPurchase()
        {
            Onclick.Invoke(this);
        }

        private const string FreeClaimedKeyPrefix = "ShopFreeClaimed_";

        // A Free pack is free only the first time (saved across app restarts); after that it needs a rewarded ad
        private static bool IsFreeClaimed(CoinPackData data) => PlayerPrefs.GetInt(FreeClaimedKeyPrefix + data.id, 0) == 1;

        private static void MarkFreeClaimed(CoinPackData data)
        {
            PlayerPrefs.SetInt(FreeClaimedKeyPrefix + data.id, 1);
            PlayerPrefs.Save();
        }

        private static bool NeedsAd(CoinPackData data)
        {
            return data.offerType == OfferType.Ads || (data.offerType == OfferType.Free && IsFreeClaimed(data));
        }

        public void OnPurchasing(ShopItemDataBase database)
        {
            if (database is not CoinPackData coinPackData || !id.Equals(coinPackData.id)) return;

            switch (coinPackData.offerType)
            {
                case OfferType.Buy:
                    PlayerInventoryManager.AddCoin(coinPackData.coin);
                    break;

                case OfferType.Free when !IsFreeClaimed(coinPackData):
                    MarkFreeClaimed(coinPackData);
                    PlayerInventoryManager.AddCoin(coinPackData.coin);
                    UpdatePriceText();
                    break;

                default:
                    AdsManager.ShowRewarded(
                        onRewarded: () => PlayerInventoryManager.AddCoin(coinPackData.coin),
                        onFailed: () => ToastMessage.ShowMessage(MessageConstants.MessageAdsNotReady));
                    break;
            }
        }

        public void SetData(ShopItemDataBase dataBase, int index)
        {
            if (dataBase is CoinPackData data)
            {
                _data = data;
                id = data.id;
                iconCoin.sprite = data.icon;
                textAmountCoin.text = $"{data.coin}";
                offerType = data.offerType;
                isPurchased = data.isPurchase;
                UpdatePriceText();
            }
        }

        private void UpdatePriceText()
        {
            if (_data == null) return;
            if (_data.offerType == OfferType.Buy) priceText.text = $"{_data.price:N0}₫";
            else priceText.text = NeedsAd(_data) ? "Watch Ad" : "FREE!";
        }

        public ShopItemDataBase Data()
        {
            return _data;
        }
    }
}
