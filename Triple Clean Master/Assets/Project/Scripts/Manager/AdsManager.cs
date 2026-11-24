using System;
using Unity.Services.LevelPlay;
using UnityEngine;

namespace Project.Scripts.Manager
{
    // Lớp bọc quảng cáo LevelPlay: game chỉ gọi ShowRewarded, không gọi thẳng SDK.
    public class AdsManager : Singleton<AdsManager>
    {
        [SerializeField] private string appKey;
        [SerializeField] private string rewardedAdUnitId;

        private LevelPlayRewardedAd _rewardedAd;
        private Action _onRewarded;
        private Action _onFailed;
        private bool _isShowing;

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

        public static void ShowRewarded(Action onRewarded, Action onFailed = null)
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
            instance._onRewarded = onRewarded;
            instance._onFailed = onFailed;
            ad.ShowAd();
        }

        // Thưởng ngay khi nhận event, vì một số mạng gửi OnAdRewarded sau OnAdClosed
        private void OnAdRewarded(LevelPlayAdInfo info, LevelPlayReward reward)
        {
            var callback = _onRewarded;
            _onRewarded = null;
            callback?.Invoke();
        }

        private void OnAdClosed(LevelPlayAdInfo info)
        {
            _isShowing = false;
            _onFailed = null;
            _rewardedAd.LoadAd();
        }

        private void OnAdDisplayFailed(LevelPlayAdInfo info, LevelPlayAdError error)
        {
            Debug.LogWarning($"[Ads] Rewarded display failed: {error}");
            _isShowing = false;
            _onRewarded = null;
            var callback = _onFailed;
            _onFailed = null;
            callback?.Invoke();
            _rewardedAd.LoadAd();
        }
    }
}
