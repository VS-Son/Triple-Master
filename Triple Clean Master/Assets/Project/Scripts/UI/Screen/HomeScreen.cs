using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Project.Scripts.Constants;
using Project.Scripts.Manager;
using Project.Scripts.TileMatch.Manager;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Scripts.UI.Screen
{
   public class HomeScreen : UIScreen
   {
      //public override TypeScreen Type => TypeScreen.HomeScreen;
      [SerializeField] private Transform btPlay;
      [SerializeField] private List<RectTransform> logoTiles;
      [SerializeField] private Image bannerExplorer;
      [SerializeField] private TMP_Text textLevel;
      public static Action UpdateLevelText;
      public static Action<bool,bool> OnActveStatusBar;
      private void Start()
      {
         foreach (var tileIndex in logoTiles)
         {
            tileIndex.transform.localScale = new Vector3(0, 0);
         }
         btPlay.localScale = Vector3.zero;
         PlayIntro();
         UpdayeTextLevel();
      }

      private Sequence _introSequence;

      private void PlayIntro()
      {
         _introSequence?.Kill();
         _introSequence = DOTween.Sequence();
         foreach (var tile in logoTiles)
         {
            _introSequence.Append(tile.DOScale(new Vector3(1.23f, 1.23f), 0.25f));
         }
         _introSequence.Append(bannerExplorer.DOFillAmount(1f, 1.5f).SetEase(Ease.Linear));
         _introSequence.Append(btPlay.DOScale(1, 0.4f));
      }

      private void OnDestroy()
      {
         _introSequence?.Kill();
      }
      public void OnPlay()
      {
         AudioManager.Instance.PlaySfx(AudioConstants.HighPitchDefault);
         StateUI.ChangeState(ScreenType.PlayScreen);
         AudioManager.Instance.PlayBGM(AudioConstants.BGM, 1);
         AudioManager.Instance.PlaySfx(AudioConstants.HighPitchDefault);
         UpdateLevelText?.Invoke();
         OnActveStatusBar?.Invoke(false,true);
         TileManager.ZoomScaleTile();
         GameplayManager.SetBoardActive(true);
         GameplayManager.SetTileManagerActive(true);
      }

      public void UpdayeTextLevel()
      {
         textLevel.text = $"Level {GameplayManager.CurrentLevel}";
      }
   }
}
