using System;
using Project.Scripts.Constants;
using Project.Scripts.IAP_shop.ShopCellView.PackCellView;
using Project.Scripts.Manager;
using Project.Scripts.Message;
using Project.Scripts.TileMatch.Board;
using Project.Scripts.TileMatch.Manager;
using Project.Scripts.UI.Components.booster;
using UnityEngine;

namespace Project.Scripts.UI.Screen
{
    public class PlayScreen:UIScreen
    {
        [SerializeField] private Booster undo;
        [SerializeField] private Booster magicWand;
        [SerializeField] private Booster shuffle;
        public static event Action<TypeBooster, float> AlphaBooster;
        public static bool IsClick = false;
        public static Action UpdateLevelText;
        
        private void OnEnable()
        {
            GameplayManager.SetBoardActive(true);
            PlayerInventoryManager.OnShowBoosterCountRequested += OnShowBoosterCountRequested;
            PlayerInventoryManager.OnShowCoinPurchaseRequested += OnShowCoinPurchaseRequested;
            PlayerInventoryManager.OnShowAdUnlockRequested += OnShowAdUnlockRequested;
            PackageCellView.OnPurchase += OnPurchase;
            UpdateUnlockBooster();
            UpdateTextBoosters();
            // Mở lại app / vào màn chơi: kiểm tra lượt + coin để hiện đúng số lượng / giá coin / ads
            PlayerInventoryManager.RefreshAllBoosterStates();
        }

        private void OnDisable()
        {
            GameplayManager.SetBoardActive(false);
            PlayerInventoryManager.OnShowBoosterCountRequested -= OnShowBoosterCountRequested;
            PlayerInventoryManager.OnShowCoinPurchaseRequested -= OnShowCoinPurchaseRequested;
            PlayerInventoryManager.OnShowAdUnlockRequested -= OnShowAdUnlockRequested;
            PackageCellView.OnPurchase -= OnPurchase;
        }

        private void OnPurchase()
        {
            UpdateTextBoosters();
        }


        public void OnLoadLevel()
        {
            BoardCollectTile.Instance.ResetBoard();
            TileManager.Instance.ResetTiles();
            //TileManager.Instance.OnInit();
            UpdateLevelText?.Invoke();
            UpdateUnlockBooster();
            PlayerInventoryManager.RefreshAllBoosterStates();
        }

        private void UpdateUnlockBooster()
        {
            if (GameplayManager.IsUndoUnlocked)
            {
                undo.OnDisplayBooster(TypeBooster.Undo, !BoardCollectTile.HasTileInBoard() ? 0.6f : 1f,
                    PlayerInventoryManager.UndoCount);
            }

            if (GameplayManager.IsMagicWandUnlocked)
            {
                magicWand.OnDisplayBooster(TypeBooster.MagicWand, 1f,PlayerInventoryManager.MagicWandCount );

            }
            if (GameplayManager.IsShuffleUnlocked)
            {
                shuffle.OnDisplayBooster(TypeBooster.Shuffle, 1f,PlayerInventoryManager.ShuffleCount);
                
            }       
        }
      
        public static void SetBoosterAlpha(TypeBooster type, float alpha)
        {
            AlphaBooster?.Invoke(type, alpha);
        }

        public void Undo()
        {
            if (GameplayManager.IsUndoUnlocked)
            {
                if (undo.IsDisplay(TypeBooster.Undo))
                {
                    TryUseBooster(TypeBooster.Undo, PlayerInventoryManager.UndoCount, 100,
                        () => BoardCollectTile.Instance.UndoTile(1));
                }
                else
                {
                    ToastMessage.ShowMessage(MessageConstants.MessageNotUndo);
                }
            }
            else
            {
                ToastMessage.ShowMessage(MessageConstants.MessageUnlock(2));
            }

           
        }

        public void MagicWand()
        {
            
            if (GameplayManager.IsMagicWandUnlocked )
            {
                if (!BoardCollectTile.IsMatching && magicWand.IsDisplay(TypeBooster.MagicWand))
                {
                    TryUseBooster(TypeBooster.MagicWand, PlayerInventoryManager.MagicWandCount, 200,
                        () =>
                        {
                            // Bật cờ ngay trước khi tile bay lên board (sau khi đã trả lượt/coin/xem xong ads)
                            IsClick = true;
                            TileManager.Instance.CollectMatchThreeTiles();
                        });
                }
            }
            else
            {
                ToastMessage.ShowMessage(MessageConstants.MessageUnlock(3));
            }
        }

        public void OnShuffle()
        {
            if (GameplayManager.IsShuffleUnlocked)
            {
                if (!TileManager.IsCompleteShuffle && shuffle.IsDisplay(TypeBooster.Shuffle))
                {
                    TryUseBooster(TypeBooster.Shuffle, PlayerInventoryManager.ShuffleCount, 300,
                        () => TileManager.Instance.ShuffleGridTiles());
                }
            }
            else
            {
                ToastMessage.ShowMessage(MessageConstants.MessageUnlock(4));
            }
        }

        // Trả bằng lượt có sẵn > coin > xem quảng cáo; hiệu ứng chỉ chạy khi đã trả xong
        private void TryUseBooster(TypeBooster type, int count, int price, Action effect)
        {
            if (count > 0)
            {
                PlayerInventoryManager.Instance.ConsumeBooster(type, 1);
                effect();
                UpdateTextBoosters();
            }
            else if (PlayerInventoryManager.HasEnoughCoin(price))
            {
                PlayerInventoryManager.SpendCoin(price);
                effect();
                UpdateTextBoosters();
            }
            else
            {
                // effect chỉ chạy sau khi ads đã đóng hẳn và người chơi được thưởng
                AdsManager.ShowRewarded(
                    onRewarded: () =>
                    {
                        if (!isActiveAndEnabled) return;
                        effect();
                        UpdateTextBoosters();
                    },
                    onFailed: () => ToastMessage.ShowMessage(MessageConstants.MessageAdsNotReady));
            }
        }

        private static bool IsUnlocked(TypeBooster type)
        {
            return type switch
            {
                TypeBooster.Undo => GameplayManager.IsUndoUnlocked,
                TypeBooster.MagicWand => GameplayManager.IsMagicWandUnlocked,
                TypeBooster.Shuffle => GameplayManager.IsShuffleUnlocked,
                _ => false
            };
        }

        private void OnShowBoosterCountRequested(TypeBooster type)
        {
            if (!IsUnlocked(type)) return;
            undo.DisplayValue(type);
            magicWand.DisplayValue(type);
            shuffle.DisplayValue(type);
        }

        private void OnShowCoinPurchaseRequested(TypeBooster type)
        {
            if (!IsUnlocked(type)) return;
            undo.DisplayCoin(type);
            magicWand.DisplayCoin(type);
            shuffle.DisplayCoin(type);
        }

        private void OnShowAdUnlockRequested(TypeBooster type)
        {
            if (!IsUnlocked(type)) return;
            undo.DisplayAds(type);
            magicWand.DisplayAds(type);
            shuffle.DisplayAds(type);
        }

        public void UpdateTextBoosters()
        {
            undo.UpdateTextBooster(TypeBooster.Undo,PlayerInventoryManager.UndoCount,100);
            magicWand.UpdateTextBooster(TypeBooster.MagicWand,PlayerInventoryManager.MagicWandCount,200);
            shuffle.UpdateTextBooster(TypeBooster.Shuffle,PlayerInventoryManager.ShuffleCount,300);
        }
        
    }
}