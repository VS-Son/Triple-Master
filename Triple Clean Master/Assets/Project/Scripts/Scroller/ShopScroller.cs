using System.Collections.Generic;
using System.Linq;
using EnhancedUI.EnhancedScroller;
using Project.Scriptable;
using Project.Scripts.IAP_shop.ShopCellView;
using Project.Scripts.IAP_shop.ShopCellView.PackCellView;
using Project.Scripts.IAP_shop.ShopData;
using UnityEngine;

namespace Project.Scripts.Scroller
{
    public class ShopScroller: MonoBehaviour, IEnhancedScrollerDelegate
    {
        [SerializeField] private EnhancedScroller scroller;
        [SerializeField] private ListShopItemPrefab itemPrefab;
        [SerializeField] private ShopDataConfig shopDataConfig;
        private readonly Dictionary<ShopItemType, EnhancedScrollerCellView> _itemDict = new();
        private List<ShopItemDataBase> _allItems = new ();
        private List<ShopItemDataBase> _allItemsOriginal = new();
        private const string PurchasedKeyPrefix = "ShopPurchased_";

        [ContextMenu(nameof(RefundPurchased))]
        private void Start()
        {
            OnInit();
            // Sort after the data is loaded so already-bought packs start at the bottom of the list
            UpdatePurchasedNonConsumable();
        }

        // The ScriptableObject is not saved on device, so the purchased state lives in PlayerPrefs per item id
        private static string PurchasedKey(ShopItemDataBase item) => PurchasedKeyPrefix + item.id;

        private static void LoadPurchasedState(IEnumerable<ShopItemDataBase> items)
        {
            foreach (var item in items)
            {
                item.isPurchase = PlayerPrefs.GetInt(PurchasedKey(item), 0) == 1;
            }
        }

        private static void SavePurchasedState(ShopItemDataBase item)
        {
            PlayerPrefs.SetInt(PurchasedKey(item), item.isPurchase ? 1 : 0);
            PlayerPrefs.Save();
        }

        private void OnInit()
        {
            scroller.Delegate = this;
            foreach (var item in itemPrefab.items)
            {
                if (item.item!=null && !_itemDict.ContainsKey(item.shopItemType))
                {
                    _itemDict[item.shopItemType] = item.item;
                }
                
            }
            _allItems = shopDataConfig.GetRangData();
            LoadPurchasedState(_allItems);
            _allItemsOriginal = new List<ShopItemDataBase>(_allItems);
            scroller.ReloadData();

        }

        public int GetNumberOfCells(EnhancedScroller scroller)
        {
            return _allItems.Count;
        }

        public float GetCellViewSize(EnhancedScroller scroller, int dataIndex)
        {
            if (dataIndex < 0 || dataIndex >= _allItems.Count)
            {
                return 200f;
            }

            ShopItemDataBase itemData = _allItems[dataIndex];
            switch (itemData.ItemType)
            {
                case ShopItemType.Package:
                    return 320f;
                case ShopItemType.Coin:
                    return 136f;
                case ShopItemType.Ads:
                    return 150f;
                default:
                    return 200f;
            }
        }

        public EnhancedScrollerCellView GetCellView(EnhancedScroller scroller, int dataIndex, int cellIndex)
        {
            if (dataIndex <0 || dataIndex >= _allItems.Count)
            {
                return null;
            }

            ShopItemDataBase itemData = _allItems[dataIndex];
            EnhancedScrollerCellView cellView = null;
            if (!_itemDict.TryGetValue(itemData.ItemType, out var item))
            {
                return null;
            }

            cellView = scroller.GetCellView(item);
            if (cellView is IShopCellView data)
            {
                data.SetData(itemData, dataIndex);
                data.Onclick = null;
                data.Onclick += OnClick;
            }

            return cellView;
        }

        private void OnClick(IShopCellView cellView)
        {
            ShopItemDataBase data = null;
            switch (cellView)
            {
                case PackageCellView packageCellView:
                    data = packageCellView.Data();
                    packageCellView.OnPurchasing(data);
                    break;
                case CoinPackCellView coinPackCellView:
                    data = coinPackCellView.Data();
                    coinPackCellView.OnPurchasing(data);
                    break;
                case RemoveAdsCellView packageCellView:
                    data = packageCellView.Data();
                    packageCellView.OnPurchasing(data);
                    break;
            }

            if (data is {PurchaseType: PurchaseType.NonConsumable})
            {
                SavePurchasedState(data);
                UpdatePurchasedNonConsumable();
            }
        }

        private void UpdatePurchasedNonConsumable()
        {
            var notPurchase = _allItemsOriginal.Where(item => !item.isPurchase);
            var purchased = _allItemsOriginal.Where(item => item.isPurchase);
            _allItems = notPurchase.Concat(purchased).ToList();
            scroller.ReloadData();
        }

        public void RefundPurchased()
        {
            foreach (var item in _allItems)
            {
                item.isPurchase = false;
                SavePurchasedState(item);
            }

            _allItems = new List<ShopItemDataBase>(_allItemsOriginal);

            scroller.ReloadData();
        }
    }
}
