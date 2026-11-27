using DG.Tweening;
using UnityEngine;

namespace Project.Scripts.Effect
{
    // Revive (undo 5 tiles + shuffle the whole layout) runs several tweens per tile, which exceeded the
    // default 200 tweeners and made DOTween grow its pools mid-game (warning + small hitch). Reserve up front.
    public static class TweenSetup
    {
        private const int MaxTweeners = 1000;
        private const int MaxSequences = 100;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            DOTween.Init();
            DOTween.SetTweensCapacity(MaxTweeners, MaxSequences);
        }
    }
}
