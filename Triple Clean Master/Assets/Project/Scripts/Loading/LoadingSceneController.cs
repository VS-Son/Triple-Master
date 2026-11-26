using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Scripts.Loading
{
    public class LoadingSceneController : MonoBehaviour
    {
        [SerializeField] private Image progressFill;
        [SerializeField] private TMP_Text textProgress;
        [SerializeField] private string mainSceneName = "Main";
        [SerializeField] private float minDuration = 2f;

        [Header("Pause")]
        [SerializeField, Range(0f, 1f)] private float pauseAt = 0.8f;
        [SerializeField] private float pauseDuration = 0.6f;

        [Header("Text Loading")]
        [SerializeField] private TMP_Text textLoading;
        [SerializeField] private string loadingLabel = "Loading";
        [SerializeField] private float dotInterval = 0.4f;

        private void Start()
        {
            SetProgress(0f);
            StartCoroutine(LoadMainScene());
            if (textLoading != null) StartCoroutine(AnimateDots());
        }

        private IEnumerator AnimateDots()
        {
            var wait = new WaitForSeconds(dotInterval);
            int dots = 0;
            while (true)
            {
                textLoading.text = loadingLabel + new string('.', dots) + "<alpha=#00>" + new string('.', 3 - dots);
                dots = (dots + 1) % 4;
                yield return wait;
            }
        }

        private IEnumerator LoadMainScene()
        {
            var operation = SceneManager.LoadSceneAsync(mainSceneName);
            operation.allowSceneActivation = false; 

            float time = 0f;
            float progress = 0f;
            bool paused = false;
            while (progress < 1f)
            {
                time += Time.deltaTime;
                float loadProgress = Mathf.Clamp01(operation.progress / 0.9f);
                float timeProgress = Mathf.Clamp01(time / minDuration);
                progress = Mathf.Min(loadProgress, timeProgress);

                if (!paused && progress >= pauseAt)
                {
                    paused = true;
                    SetProgress(pauseAt);
                    yield return new WaitForSeconds(pauseDuration);
                    continue;
                }

                SetProgress(progress);
                yield return null;
            }

            operation.allowSceneActivation = true;
        }

        private void SetProgress(float progress)
        {
            progressFill.fillAmount = progress;
            if (textProgress != null) textProgress.text = $"{Mathf.RoundToInt(progress * 100)}%";
        }
    }
}
