using System.Collections;
using UnityEngine;
using ZombieRush.Audio;
using ZombieRush.Core;
using ZombieRush.Data;

namespace ZombieRush.Effects
{
    /// Answers the game's events with feedback, all tuned in FeelConfig:
    ///   a hit       : a crisp tick;
    ///   a headshot  : a wet crack and a phone tap;
    ///   a kill      : a low thud, a small camera jolt and a tap (only kills the player made, not turret kills);
    ///   hurt        : a camera jolt and a strong tap, and an arrow pointing at the attacker;
    ///   the last kill of a wave: a bigger jolt and, playing solo, a moment of slow motion.
    /// Registers itself; no scene object needed. Per-shot feel (kick, tracers, impacts) is in WeaponBase.
    public class FeelDirector : MonoBehaviour
    {
        const float TurretKillWindow = 0.35f;   // a kill this soon after the player's own hit counts as theirs

        static FeelDirector runner;
        static float lastLocalHit = -10f, lastTick = -10f, lastHealth = -1f;
        static bool waveActive;
        static float defaultFixedDelta = 0.02f;
        static Coroutine slowMo;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            defaultFixedDelta = Time.fixedDeltaTime;
            GameEvents.OnEnemyHit -= HandleHit; GameEvents.OnEnemyHit += HandleHit;
            GameEvents.OnHeadshot -= HandleHeadshot; GameEvents.OnHeadshot += HandleHeadshot;
            GameEvents.OnZombieKilled -= HandleKill; GameEvents.OnZombieKilled += HandleKill;
            GameEvents.OnPlayerHealthChanged -= HandleHealth; GameEvents.OnPlayerHealthChanged += HandleHealth;
            GameEvents.OnPlayerHurtFrom -= HandleHurtFrom; GameEvents.OnPlayerHurtFrom += HandleHurtFrom;
            GameEvents.OnWaveStarted -= HandleWaveStarted; GameEvents.OnWaveStarted += HandleWaveStarted;
            GameEvents.OnWaveCompleted -= HandleWaveCompleted; GameEvents.OnWaveCompleted += HandleWaveCompleted;
            GameEvents.OnZombiesRemainingChanged -= HandleRemaining; GameEvents.OnZombiesRemainingChanged += HandleRemaining;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= HandleSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        static void HandleSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
        {
            lastHealth = -1f;
            waveActive = false;
            multiKills = 0; spree = 0; lastKillAt = -10f;
            ScreenGrade.Ensure();   // the green mute and the low-health look, made fresh for each level
        }

        // ---- kill streaks: kills close together chain into multi-kills; kills without being hurt make a spree
        static int multiKills, spree;
        static float lastKillAt = -10f;
        static readonly string[] SpreeNames = { "KILLING SPREE", "RAMPAGE", "UNSTOPPABLE" };

        static void CountStreak(FeelConfig cfg)
        {
            float now = Time.unscaledTime;
            multiKills = now - lastKillAt <= cfg.multiKillWindow ? multiKills + 1 : 1;
            lastKillAt = now;
            spree++;

            string call = null, sub = null;
            Color color = new Color(1f, 0.78f, 0.25f);
            int s = cfg.spreeKills != null ? System.Array.IndexOf(cfg.spreeKills, spree) : -1;
            if (s >= 0)
            {
                call = SpreeNames[Mathf.Min(s, SpreeNames.Length - 1)];
                sub = $"{spree} KILLS WITHOUT A SCRATCH";
                color = new Color(1f, 0.32f, 0.2f);
            }
            else if (multiKills >= 2)
            {
                call = multiKills == 2 ? "DOUBLE KILL" : multiKills == 3 ? "TRIPLE KILL" : "MULTI KILL";
                sub = multiKills >= 4 ? $"x{multiKills}" : "";
            }
            if (call == null) return;
            ZombieRush.UI.KillStreakUI.Show(call, sub, color);
            SoundManager.Play(ProceduralSfx.Streak, cfg.streakVolume, 0f);
        }

        static void HandleHit()
        {
            lastLocalHit = Time.unscaledTime;
            if (Time.unscaledTime - lastTick < 0.035f) return;   // one tick per moment, however many hits land
            lastTick = Time.unscaledTime;
            SoundManager.Play(ProceduralSfx.HitTick, FeelConfig.Current.hitTickVolume, 0.06f);
        }

        static void HandleHeadshot()
        {
            var cfg = FeelConfig.Current;
            SoundManager.Play(ProceduralSfx.Headshot, cfg.headshotVolume, 0.05f);
            Haptics.Tap(cfg.headshotTap);
        }

        static void HandleKill(ZombieKilledInfo info)
        {
            if (Time.unscaledTime - lastLocalHit > TurretKillWindow) return;   // the turret's kill, not the player's
            var cfg = FeelConfig.Current;
            SoundManager.Play(ProceduralSfx.KillThud, cfg.killVolume, 0.05f);
            CameraShake.Add(cfg.killShake);
            Haptics.Tap(cfg.killTap);
            CountStreak(cfg);
        }

        static void HandleHealth(float current, float max)
        {
            float drop = lastHealth >= 0f ? lastHealth - current : 0f;
            lastHealth = current;
            if (drop < 3f) return;   // poison ticks and the like: the red flash is enough
            spree = 0;               // hurt: the spree is over
            var cfg = FeelConfig.Current;
            CameraShake.Add(cfg.hitTakenShake * Mathf.Clamp(drop / 20f, 0.4f, 1.3f));
            Haptics.Tap(cfg.hitTakenTap);
        }

        static void HandleHurtFrom(Vector3 source, float damage) => ZombieRush.UI.DamageIndicatorUI.Show(source, damage);

        static void HandleWaveStarted(int wave) => waveActive = true;
        static void HandleWaveCompleted(int wave, float breather) => waveActive = false;

        static void HandleRemaining(int remaining)
        {
            if (!waveActive || remaining > 0) return;
            waveActive = false;
            LastKill();
        }

        /// The wave's last zombie just fell.
        static void LastKill()
        {
            var cfg = FeelConfig.Current;
            CameraShake.Add(cfg.lastKillShake);
            Haptics.Tap(cfg.killTap);
            if (ZombieRush.Net.NetSession.InSquad) return;   // a squad's world can't slow down for one player
            if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;
            if (runner == null)
            {
                var go = new GameObject("FeelDirector");
                DontDestroyOnLoad(go);
                runner = go.AddComponent<FeelDirector>();
            }
            if (slowMo != null) runner.StopCoroutine(slowMo);
            slowMo = runner.StartCoroutine(SlowMotion(cfg));
        }

        /// Slows the game, then eases it back to full speed. Leaves time alone while the game is paused or over.
        static IEnumerator SlowMotion(FeelConfig cfg)
        {
            float seconds = Mathf.Max(0.05f, cfg.slowMoSeconds);
            float end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end)
            {
                if (GameManager.Instance == null || GameManager.Instance.State == GameState.Playing)
                {
                    float left = (end - Time.unscaledTime) / seconds;            // 1 -> 0
                    float scale = Mathf.Lerp(1f, cfg.slowMoScale, Mathf.Clamp01(left / 0.4f));   // eases back over the last 40%
                    Time.timeScale = scale;
                    Time.fixedDeltaTime = defaultFixedDelta * scale;
                }
                yield return null;
            }
            if (Time.timeScale > 0f) Time.timeScale = 1f;   // 0 = paused: GameManager restores it on resume
            Time.fixedDeltaTime = defaultFixedDelta;
            slowMo = null;
        }
    }
}
