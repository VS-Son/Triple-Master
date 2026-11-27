using DG.Tweening;
using Project.Scripts.Constants;
using Project.Scripts.Manager;
using Project.Scripts.Message;
using Project.Scripts.TileMatch.Board;
using Project.Scripts.TileMatch.Manager;
using Project.Scripts.UI.Components.booster;
using TMPro;
using UnityEngine;

namespace Project.Scripts.UI.Screen
{
    public class PopupRevive : UIPopup
    {
        public Transform bg;
        [SerializeField] private TMP_Text textLevel;
        [Header("Time Duration")] [SerializeField]
        private float timeScale;
        private void Start()
        {
            SetScale();

        }

        private void OnEnable()
        {
            bg.DOScale(1, timeScale);
            textLevel.text = ("Level " + GameplayManager.CurrentLevel);
        }

        // Revive is a rewarded ad: the board is only restored after the ad has been watched
        public void OnRevive()
        {
            AudioManager.Instance.PlaySfx(AudioConstants.HighPitchDefault);
            AdsManager.ShowRewarded(
                onRewarded: Revive,
                onFailed: () => ToastMessage.ShowMessage(MessageConstants.MessageAdsNotReady));
        }

        private void Revive()
        {
            if (!gameObject.activeInHierarchy) return;
            BoardCollectTile.Instance.UndoTile(5);
            StatusBar.Source = ShopOpenSource.Gameplay;
            Close();
            SetScale();
        }

        public void OnReplay()
        {
            AudioManager.Instance.PlaySfx(AudioConstants.HighPitchDefault);
            PlayScreen.SetBoosterAlpha(TypeBooster.Undo, 0.6f);
            BoardCollectTile.Instance.ResetBoard();
            TileManager.Instance.ResetTiles();
            StatusBar.Source = ShopOpenSource.Gameplay;
            Close();
            SetScale();

        }

        private void SetScale()
        {
            bg.localScale = new Vector3(0.1f, 0.1f);
            
        }
    }
}
