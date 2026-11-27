using System;
using System.Collections;
using DG.Tweening;
using Project.Scripts.Constants;
using Project.Scripts.Effect;
using Project.Scripts.Manager;
using Project.Scripts.TileMatch.Board;
using Project.Scripts.TileMatch.Manager;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Project.Scripts.UI.Screen
{
    public class NextScreen : UIScreen
    {
        [SerializeField] private TMP_Text textNext;
        [SerializeField] private Slider progressionFill;
        [SerializeField] private TMP_Text textProgression;
        [SerializeField] private Button btNext;
        [SerializeField] private Animator animGift;
        [SerializeField] private Image iconGift;
        [SerializeField] private int amountReward;
        [SerializeField] private int giftProgression;
        private const string ProgressionKey = "giftProgression";
        public static event Action<int> GetCoin;
        private static int NextLevel => GameplayManager.CurrentLevel + 1;

        public static Action<bool> OnActiveStatus;
        public static Action UpdateLevelText;

        private void Awake()    
        {
            LoadCountProgression();
        }

        private static readonly Vector3 BtNextShowScale = Vector3.one;

        private void OnEnable()
        {
            //LoadCountProgression();
            HideNextButton(false);
            textNext.text = "Level " + ( GameplayManager.CurrentLevel + 1);
            OnActiveStatus?.Invoke(false);

            if (giftProgression <= 0)
            {
                progressionFill.gameObject.SetActive(true);
                iconGift.gameObject.SetActive(true);

            }
            ProgressionFill();
            CoinEffect.OnCompleteGoal += OnCompleteGoal;


        }

        private void OnDisable()
        {
            CoinEffect.OnCompleteGoal -= OnCompleteGoal;
            if (giftProgression >= 4)
            {
                giftProgression = 0;
            }

        }
        

        private void ProgressionFill()
        {
            progressionFill.value = giftProgression/4f;
            giftProgression = Mathf.Min(giftProgression + 1, 4);
            SaveCountProgression();
            textProgression.text = $"Provence {giftProgression}/4";
            var amountValue = progressionFill.maxValue / 4f;
            var targetValue = Mathf.Min(progressionFill.value + amountValue, progressionFill.maxValue);
            StartCoroutine(IncreaseProgressionFill(progressionFill.value,targetValue, 0.3f ));


        }

        private IEnumerator IncreaseProgressionFill(float from, float to, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                progressionFill.value = Mathf.Lerp(from, to, elapsed / duration);
                yield return null;
            }
            progressionFill.value = to;
            if (giftProgression <= 3)
            {
                OnCompleteGoal();
            }
            if (to >= progressionFill.maxValue)
            {
                HideNextButton(true);
                animGift.gameObject.SetActive(true);
                progressionFill.gameObject.SetActive(false);
                animGift.enabled = true;
                iconGift.gameObject.SetActive(false);
                StartCoroutine(OpenRewardGift());
                progressionFill.value = 0.5f;
                giftProgression = 0;
                textProgression.text = $"Provence {giftProgression}/4";

            }
        }
        private bool _canClickNext;

        private void HideNextButton(bool animate)
        {
            btNext.transform.DOKill();
            _canClickNext = false;
            if (animate)
            {
                btNext.transform.DOScale(Vector3.zero, 0.2f).SetEase(Ease.InBack);
            }
            else
            {
                btNext.transform.localScale = Vector3.zero;
            }
        }

        private void OnCompleteGoal()
        {
            btNext.transform.DOKill();
            btNext.transform.DOScale(BtNextShowScale, 0.6f).SetEase(Ease.OutBack).OnComplete(() =>
            {
                btNext.enabled = true;
                _canClickNext = true;
            });
        }

        IEnumerator OpenRewardGift()
        {
            while (animGift.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f || animGift.IsInTransition(0))
            {
                yield return null;
            }
            animGift.enabled = false;
            animGift.gameObject.SetActive(false);
            GetCoin?.Invoke(amountReward);

        }

        public void OnNext()
        {
            if (!_canClickNext) return;
            _canClickNext = false;
            AudioManager.Instance.PlaySfx(AudioConstants.HighPitchDefault);
            TileManager.ZoomScaleTile();
            if (giftProgression >= 4)
            {
                giftProgression = 0;
            }
            TileManager.Instance.NextLevel();
            BoardCollectTile.Instance.ResetBoard();
            UpdateLevelText?.Invoke();
            OnActiveStatus?.Invoke(true);
            StateUI.ChangeState(ScreenType.PlayScreen);
            Close();
        }

        private void LoadCountProgression()
        {
            giftProgression = PlayerPrefs.GetInt(ProgressionKey);
        }

        private void SaveCountProgression()
        {
            PlayerPrefs.SetInt(ProgressionKey, giftProgression);
            PlayerPrefs.Save();
        }
    }
}
