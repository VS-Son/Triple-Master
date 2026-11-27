using System;
using Project.Scripts.Constants;
using Project.Scripts.Manager;
using Project.Scripts.Scroller;
using Project.Scripts.TileMatch.Board;
using Project.Scripts.UI.Popup;
using TMPro;
using UnityEngine;
using Button = UnityEngine.UI.Button;

namespace Project.Scripts.UI.Screen
{
    public enum ShopOpenSource
    {
        HomeScreen,
        Gameplay
    }
    public class StatusBar : UICommon
    {
        public TMP_Text textTitle;
        public TMP_Text textCoin;
        public GameObject setting;
        public GameObject back;
        public Button statusCoin;
        public Transform iconCoin;
        public static ShopOpenSource Source;

        private void OnEnable()
        {
            PlayerInventoryManager.OnCoinchange += UpdateTextCoin;
            HomeScreen.UpdateLevelText += UpdateLevelText;
            HomeScreen.OnActveStatusBar += SetActiveStatusOnPlay;
            NextScreen.OnActiveStatus += SetActiveStatusOnNext;
            NextScreen.UpdateLevelText += UpdateLevelText;
            PlayScreen.UpdateLevelText += UpdateLevelText;


            UpdateLevelText();
            if (StateUI.IsState(ScreenType.HomeScreen))
            {
                textTitle.gameObject.SetActive(false);
            }
        }

        private void OnDisable()
        {
            PlayerInventoryManager.OnCoinchange -= UpdateTextCoin;
            HomeScreen.UpdateLevelText -= UpdateLevelText;
            HomeScreen.OnActveStatusBar -= SetActiveStatusOnPlay;
            NextScreen.OnActiveStatus -= SetActiveStatusOnNext;
            NextScreen.UpdateLevelText -= UpdateLevelText;
            PlayScreen.UpdateLevelText -= UpdateLevelText;
        }

        public void OnEnableStatusCoin(bool isActive)
        {
            statusCoin.enabled = isActive;
        }
        public void OnSetting()
        {
            AudioManager.Instance.PlaySfx(AudioConstants.HighPitchDefault);
            ShowButtonSetting(false);
            ShowButtonBack(false);
            statusCoin.enabled = false;
            var popup = UIManager.OpenPopup<PopupSetting>(PopupType.Setting);
            popup.OnClosePopup -= HandleSettingClosed;
            popup.OnClosePopup += HandleSettingClosed;
        }
        private void HandleSettingClosed()
        {
            ShowButtonSetting(true);
        }
        public void OnBack()
        {
            AudioManager.Instance.PlaySfx(AudioConstants.HighPitchDefault);
            if (StateUI.IsState(ScreenType.PlayScreen))
            {
                StateUI.ChangeState(ScreenType.HomeScreen);
                AudioManager.Instance.StopBGM();
                GameplayManager.SetBoardActive(false);
                GameplayManager.SetTileManagerActive(false);
                back.SetActive(false);
                setting.SetActive(true);
                textTitle.gameObject.SetActive(false);
                return;
            }
            switch (Source)
            {
                case ShopOpenSource.HomeScreen:
                    StateUI.ChangeState(ScreenType.HomeScreen);
                    AudioManager.Instance.StopBGM();
                    GameplayManager.SetBoardActive(false);
                    back.SetActive(false);
                    setting.SetActive(true);
                    textTitle.gameObject.SetActive(false);
                    UIManager.CloseUI<ShopScreen>(ScreenType.ShopScreen);
                    statusCoin.enabled = true;
                    UIManager.GetUI<HomeScreen>(ScreenType.HomeScreen).UpdayeTextLevel();
                    break;

                case ShopOpenSource.Gameplay:
                    StateUI.ChangeState(ScreenType.PlayScreen);
                    GameplayManager.SetTileManagerActive(true);
                    UIManager.CloseUI<ShopScreen>(ScreenType.ShopScreen);
                    statusCoin.enabled = true;
                    textTitle.gameObject.SetActive(true);
                    UpdateLevelText();
                    break;
            }
        }

        public void OnShop()
        {
            // Opening the Store hides the board, which would stop a clear animation half-way
            if (StateUI.IsState(ScreenType.PlayScreen) && BoardCollectTile.IsBusy) return;
            AudioManager.Instance.PlaySfx(AudioConstants.HighPitchDefault);
            var source = StateUI.IsState(ScreenType.PlayScreen) ? ShopOpenSource.Gameplay : ShopOpenSource.HomeScreen;
            StateUI.ChangeState(ScreenType.ShopScreen);
            if (source == ShopOpenSource.Gameplay)
            {
                // PlayScreen.OnDisable already hides the 7-slot board; hide the tile layout too so it doesn't show through the Store
                GameplayManager.SetTileManagerActive(false);
            }
            setting.SetActive(false);
            back.SetActive(true);
            statusCoin.enabled = false;
            // The Store shows its own wooden "Store" sign, so the text title stays hidden here
            textTitle.gameObject.SetActive(false);
            UIManager.GetUI<ShopScreen>(ScreenType.ShopScreen).UpdateTextUndo(PlayerInventoryManager.UndoCount);
            UIManager.GetUI<ShopScreen>(ScreenType.ShopScreen).UpdateTextMagicWand(PlayerInventoryManager.MagicWandCount);
            UIManager.GetUI<ShopScreen>(ScreenType.ShopScreen).UpdateTextShuffle(PlayerInventoryManager.ShuffleCount);
            

            Source = source;
        }
        

        public  void UpdateLevelText()
        {
            textTitle.text = $"Level {GameplayManager.CurrentLevel}";
        }

        private void UpdateTextCoin(int coin)
        {
            textCoin.text = coin >= 1000 ? $"{coin/1000}K" : $"{coin.ToString()}";
        }

        private void SetActiveStatusOnNext(bool isActive)
        {
            statusCoin.enabled = isActive;
            textTitle.gameObject.SetActive(isActive);
            back.SetActive(isActive);
        }

        private void SetActiveStatusOnPlay(bool isHide, bool isShow)
        {
            ShowButtonSetting(isHide);
            ShowButtonBack(isShow);
            textTitle.gameObject.SetActive(isShow);
        }

        private void ShowButtonSetting(bool isShow)
        {
            setting.SetActive(isShow);
        }
        private void ShowButtonBack(bool isShow)
        {
            back.SetActive(isShow);
        }
    }
}
