using System;
using UnityEngine;

namespace ZombieRush.Core
{
    /// Everything the HUD needs to draw the ammo readout in one payload.
    public struct AmmoInfo
    {
        public string WeaponName;
        public Sprite Icon;
        public int Magazine;
        public int MagazineSize;
        public int Reserve;
        public bool IsReloading;
        public float ReloadDuration; // seconds, so UI can animate a reload bar without polling the weapon
        public bool Unlimited;
    }

    /// Raised once per zombie death. The zombie decides its own drops; whoever
    /// owns coins/ammo listens, so zombies never reference player systems directly.
    public struct ZombieKilledInfo
    {
        public Vector3 Position;
        public int Coins;
        public int Ammo;
    }

    /// Central event bus. UI and gameplay systems subscribe here instead of
    /// referencing each other directly. Subscribers must unsubscribe in
    /// OnDisable/OnDestroy because these are static and survive scene reloads.
    public static class GameEvents
    {
        // Player
        public static event Action<float, float> OnPlayerHealthChanged; // current, max
        public static event Action OnPlayerDied;
        public static event Action<AmmoInfo> OnAmmoChanged;
        public static event Action<int> OnCoinsChanged; // total coins
        public static event Action<float, float> OnPlayerArmorChanged; // current, max

        // Combat
        public static event Action OnEnemyHit;
        public static event Action<ZombieKilledInfo> OnZombieKilled;

        // Waves
        public static event Action<int> OnWaveAnnounced;
        public static event Action<int> OnWaveStarted;
        public static event Action<int, float> OnWaveCompleted; // wave, breather seconds
        public static event Action<int> OnZombiesRemainingChanged;

        // Zone
        public static event Action<float, float, int> OnZoneChanged; // radius, max radius, expand cost
        public static event Action OnZoneExpandFailed;

        // Safe base
        public static event Action<bool> OnSafeBaseChanged; // local player is inside the base
        public static event Action OnBattleStarted;          // first time a player steps out of the base
        public static event Action<string, string> OnNotice; // short on-screen message: title, subtitle

        // Flow
        public static event Action<GameState> OnGameStateChanged;

        public static void RaisePlayerHealthChanged(float current, float max) => OnPlayerHealthChanged?.Invoke(current, max);
        public static void RaisePlayerDied() => OnPlayerDied?.Invoke();
        public static void RaiseAmmoChanged(AmmoInfo info) => OnAmmoChanged?.Invoke(info);
        public static void RaiseCoinsChanged(int total) => OnCoinsChanged?.Invoke(total);
        public static void RaisePlayerArmorChanged(float current, float max) => OnPlayerArmorChanged?.Invoke(current, max);

        public static void RaiseEnemyHit() => OnEnemyHit?.Invoke();
        public static void RaiseZombieKilled(ZombieKilledInfo info) => OnZombieKilled?.Invoke(info);

        public static void RaiseWaveAnnounced(int wave) => OnWaveAnnounced?.Invoke(wave);
        public static void RaiseWaveStarted(int wave) => OnWaveStarted?.Invoke(wave);
        public static void RaiseWaveCompleted(int wave, float breather) => OnWaveCompleted?.Invoke(wave, breather);
        public static void RaiseZombiesRemainingChanged(int remaining) => OnZombiesRemainingChanged?.Invoke(remaining);

        public static void RaiseZoneChanged(float radius, float max, int cost) => OnZoneChanged?.Invoke(radius, max, cost);
        public static void RaiseZoneExpandFailed() => OnZoneExpandFailed?.Invoke();

        public static void RaiseSafeBaseChanged(bool inside) => OnSafeBaseChanged?.Invoke(inside);
        public static void RaiseBattleStarted() => OnBattleStarted?.Invoke();
        public static void RaiseNotice(string title, string subtitle) => OnNotice?.Invoke(title, subtitle);

        public static void RaiseGameStateChanged(GameState state) => OnGameStateChanged?.Invoke(state);
    }
}
