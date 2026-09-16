using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using ZombieRush.Waves;

namespace ZombieRush.UI
{
    /// "WAVE X INCOMING" banner shown for the announce window before a wave
    /// starts spawning, plus a small confirmation flash when a wave clears.
    public class WaveBannerUI : MonoBehaviour
    {
        public GameObject bannerRoot;
        public Text bannerText;
        public float fadeSpeed = 6f;

        CanvasGroup group;
        float targetAlpha;
        Coroutine hideRoutine;

        void Awake()
        {
            group = bannerRoot != null ? bannerRoot.GetComponent<CanvasGroup>() : null;
        }

        // Subscribing here (not OnEnable) matters: OnEnable can run before
        // WaveManager.Awake() has set Instance, depending on object order in the
        // scene, silently skipping the subscription. Start() is guaranteed to run
        // after every object's Awake().
        //
        // Also important: this script lives on bannerRoot itself, so it must NEVER
        // SetActive(false) that object - doing so would disable this component too,
        // firing OnDisable and unsubscribing permanently after the first hide (that
        // was the actual bug behind "wave 2 doesn't announce"). Visibility is done
        // purely via the CanvasGroup's alpha instead, so the GameObject - and this
        // script - stay active and subscribed for the whole session.
        void Start()
        {
            if (group != null) group.alpha = 0f;

            if (WaveManager.Instance != null)
            {
                WaveManager.Instance.OnWaveAnnounced += HandleWaveAnnounced;
                WaveManager.Instance.OnWaveCompleted += HandleWaveCompleted;
            }
        }

        void OnDisable()
        {
            if (WaveManager.Instance != null)
            {
                WaveManager.Instance.OnWaveAnnounced -= HandleWaveAnnounced;
                WaveManager.Instance.OnWaveCompleted -= HandleWaveCompleted;
            }
        }

        void Update()
        {
            if (group == null) return;
            group.alpha = Mathf.MoveTowards(group.alpha, targetAlpha, fadeSpeed * Time.deltaTime);
        }

        void HandleWaveAnnounced(int wave)
        {
            if (bannerText != null) bannerText.text = $"WAVE {wave} INCOMING";
            ShowFor(WaveManager.Instance != null ? WaveManager.Instance.announceDuration : 3f);
        }

        void HandleWaveCompleted(int wave)
        {
            if (bannerText != null) bannerText.text = $"WAVE {wave} CLEARED";
            ShowFor(2f);
        }

        void ShowFor(float seconds)
        {
            targetAlpha = 1f;
            if (hideRoutine != null) StopCoroutine(hideRoutine);
            hideRoutine = StartCoroutine(HideAfter(seconds));
        }

        IEnumerator HideAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            targetAlpha = 0f;
        }
    }
}
