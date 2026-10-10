using UnityEngine;
using ZombieRush.Core;

namespace ZombieRush.Player
{
    /// Coins earned from (some) zombie kills, spent on zone expansions.
    public class PlayerCurrency : MonoBehaviour
    {
        public static PlayerCurrency Instance { get; private set; }

        public int Coins { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable() => GameEvents.OnZombieKilled += HandleZombieKilled;
        void OnDisable() => GameEvents.OnZombieKilled -= HandleZombieKilled;

        void Start() => GameEvents.RaiseCoinsChanged(Coins);

        void HandleZombieKilled(ZombieKilledInfo info) => AddCoins(info.Coins);

        public void AddCoins(int amount)
        {
            if (amount <= 0) return;
            Coins += amount;
            GameEvents.RaiseCoinsChanged(Coins);
        }

        public bool TrySpendCoins(int amount)
        {
            if (amount <= 0) return true;
            if (Coins < amount) return false;
            Coins -= amount;
            GameEvents.RaiseCoinsChanged(Coins);
            return true;
        }
    }
}
