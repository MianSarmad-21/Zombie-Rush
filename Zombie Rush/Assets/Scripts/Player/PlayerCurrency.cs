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
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start() => GameEvents.RaiseCoinsChanged(Coins);

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
