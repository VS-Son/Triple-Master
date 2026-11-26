using System;
using Unity.Services.LevelPlay;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Project.Scripts.Manager
{
    public class AdsManager : Singleton<AdsManager>
    {
        [SerializeField] private string appKey;
        [SerializeField] private string rewardedAdUnitId;

        private const float RewardGraceTime = 1.5f;
        private const float RewardDelayAfterClose = 0.2f;

        private LevelPlayRewardedAd _rewardedAd;
        private Action _onRewarded;
        private Action _onFailed;
        private Action _onCancel;
        private bool _isShowing;

        private volatile bool _rewardEarned;
        private volatile bool _adClosed;
        private volatile bool _displayFailed;
        private float _closedTime = -1f;

        private void Start()
        {
            LevelPlay.OnInitSuccess += OnInitSuccess;
            LevelPlay.OnInitFailed += OnInitFailed;
            LevelPlay.Init(appKey);
        }

        private void OnDestroy()
        {
            LevelPlay.OnInitSuccess -= OnInitSuccess;
            LevelPlay.OnInitFailed -= OnInitFailed;
            SetInputBlocked(false);
            _rewardedAd?.DestroyAd();
        }

        private void OnInitSuccess(LevelPlayConfiguration config)
        {
            _rewardedAd = new LevelPlayRewardedAd(rewardedAdUnitId);
            _rewardedAd.OnAdRewarded += OnAdRewarded;
            _rewardedAd.OnAdClosed += OnAdClosed;
            _rewardedAd.OnAdDisplayFailed += OnAdDisplayFailed;
            _rewardedAd.OnAdLoadFailed += error => Debug.LogWarning($"[Ads] Rewarded load failed: {error}");
            _rewardedAd.LoadAd();
        }

        private void OnInitFailed(LevelPlayInitError error)
        {
            Debug.LogWarning($"[Ads] LevelPlay init failed: {error}");
        }

        public static void ShowRewarded(Action onRewarded, Action onFailed = null, Action onCancel = null)
        {
            var instance = Instance;
            if (instance._isShowing) return;

            var ad = instance._rewardedAd;
            if (ad == null || !ad.IsAdReady())
            {
                onFailed?.Invoke();
                ad?.LoadAd();
                return;
            }

            instance._isShowing = true;
            instance._rewardEarned = false;
            instance._adClosed = false;
            instance._displayFailed = false;
            instance._closedTime = -1f;
            instance._onRewarded = onRewarded;
            instance._onFailed = onFailed;
            instance._onCancel = onCancel;
            instance.SetInputBlocked(true);
            ad.ShowAd();
        }

        private EventSystem _blockedEventSystem;

        private void SetInputBlocked(bool blocked)
        {
            if (blocked)
            {
                var es = EventSystem.current;
                if (es == null) return;
                _blockedEventSystem = es;
                es.enabled = false;
            }
            else if (_blockedEventSystem != null)
            {
                _blockedEventSystem.enabled = true;
                _blockedEventSystem = null;
            }
        }

        private void Update()
        {
            if (!_isShowing) return;

            if (_displayFailed)
            {
                Finish(_onFailed);
                _rewardedAd?.LoadAd();
                return;
            }

            if (!_adClosed) return;

            if (_closedTime < 0f)
            {
                _closedTime = Time.realtimeSinceStartup;
                _rewardedAd?.LoadAd();
            }

            float elapsed = Time.realtimeSinceStartup - _closedTime;
            if (_rewardEarned)
            {
                if (elapsed >= RewardDelayAfterClose) Finish(_onRewarded);
            }
            else if (elapsed >= RewardGraceTime)
            {
                Finish(_onCancel);
            }
        }

        private void Finish(Action callback)
        {
            _isShowing = false;
            _rewardEarned = false;
            _adClosed = false;
            _displayFailed = false;
            _closedTime = -1f;
            _onRewarded = null;
            _onFailed = null;
            _onCancel = null;
            SetInputBlocked(false);
            callback?.Invoke();
        }

        private void OnAdRewarded(LevelPlayAdInfo info, LevelPlayReward reward)
        {
            _rewardEarned = true;
        }

        private void OnAdClosed(LevelPlayAdInfo info)
        {
            _adClosed = true;
        }

        private void OnAdDisplayFailed(LevelPlayAdInfo info, LevelPlayAdError error)
        {
            Debug.LogWarning($"[Ads] Rewarded display failed: {error}");
            _displayFailed = true;
        }
    }
}
