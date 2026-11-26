using System;
using Unity.Services.LevelPlay;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Project.Scripts.Manager
{
    // Lớp bọc quảng cáo LevelPlay: game chỉ gọi ShowRewarded, không gọi thẳng SDK.
    public class AdsManager : Singleton<AdsManager>
    {
        [SerializeField] private string appKey;
        [SerializeField] private string rewardedAdUnitId;

        // Đợi tối đa bao lâu sau khi đóng ads để nhận OnAdRewarded (một số mạng gửi sau OnAdClosed)
        private const float RewardGraceTime = 1.5f;
        // Trễ nhỏ sau khi đóng ads để game resume xong rồi mới chạy hiệu ứng thưởng
        private const float RewardDelayAfterClose = 0.2f;

        private LevelPlayRewardedAd _rewardedAd;
        private Action _onRewarded;
        private Action _onFailed;
        private Action _onCancel;
        private bool _isShowing;

        // Event của SDK có thể đến ngoài main thread hoặc khi app đang pause (ads che màn hình),
        // nên callback SDK chỉ set cờ; mọi xử lý game chạy trong Update sau khi ads đã đóng.
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

        /// <param name="onRewarded">Chạy sau khi ads đã đóng và người chơi được thưởng.</param>
        /// <param name="onFailed">Ads chưa sẵn sàng hoặc hiển thị lỗi.</param>
        /// <param name="onCancel">Người chơi đóng ads sớm, không nhận thưởng.</param>
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

        // Khóa toàn bộ click (UI + tiles đều đi qua EventSystem) từ lúc gọi ShowAd
        // đến khi ads đóng và thưởng đã xử lý xong.
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
