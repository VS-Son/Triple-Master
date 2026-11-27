using System;
using Project.Scripts.Effect;
using Project.Scripts.Manager;
using TMPro;
using UnityEngine;

namespace Project.Scripts.UI.Screen
{
    public class ShopScreen : UIScreen
    {
        [SerializeField] private TMP_Text undoValue;
        [SerializeField] private TMP_Text magicWandValue;
        [SerializeField] private TMP_Text shuffleValue;

        [Header("Header")]
        [SerializeField] private TMP_Text coinValue;

        private void OnEnable()
        {
            PlayerInventoryManager.OnCoinchange += UpdateTextCoin;
            UpdateTextCoin(PlayerInventoryManager.Coin);
        }

        private void OnDisable()
        {
            PlayerInventoryManager.OnCoinchange -= UpdateTextCoin;
        }

        private void UpdateTextCoin(int coin)
        {
            if (coinValue != null) coinValue.text = $"{coin}";
        }

        // Close button in the Store header; same flow as the status bar back button
        public void OnClose()
        {
            UIManager.GetCommon<StatusBar>(CommonUIType.StatusBar).OnBack();
        }

        public void UpdateTextUndo(int currentUndo)
        {
            if (undoValue != null) undoValue.text = $"{currentUndo}";
        }
        public void UpdateTextMagicWand( int currentMagic)
        {
            if (magicWandValue != null) magicWandValue.text = $"{currentMagic}";
        }
        public void UpdateTextShuffle( int currentShuffle)
        {
            if (shuffleValue != null) shuffleValue.text = $"{currentShuffle}";
        }
    }
}
