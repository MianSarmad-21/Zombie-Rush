using System;
using UnityEngine;

namespace ZombieRush.Core
{
    /// Paid items (real-money in-app purchases, see the GDD's monetization section).
    /// Ownership is saved in PlayerPrefs so a bought item stays unlocked.
    ///
    /// Until a store SDK (Unity IAP / Google Play Billing) is connected, TestMode
    /// grants every purchase instantly so the paid items can be played and shown.
    /// Connecting the real store only means replacing the body of Buy().
    public static class StoreService
    {
        public const string Turret = "zr.turret";

        /// True until the real store is connected: Buy() succeeds without charging.
        public static bool TestMode = true;

        public static event Action<string> Purchased;

        const string OwnedPrefix = "zr.owned.";

        public static bool IsOwned(string productId) => PlayerPrefs.GetInt(OwnedPrefix + productId, 0) == 1;

        public static string PriceLabel(string productId)
        {
            switch (productId)
            {
                case Turret: return "$1.99";
                default: return "";
            }
        }

        public static void Buy(string productId, Action<bool> done)
        {
            if (IsOwned(productId)) { done?.Invoke(true); return; }

            if (!TestMode)
            {
                Debug.LogWarning($"[Zombie Rush] Store not connected yet - cannot buy {productId}.");
                done?.Invoke(false);
                return;
            }

            Debug.Log($"[Zombie Rush] Test purchase of {productId} ({PriceLabel(productId)}) granted - no money charged.");
            Grant(productId);
            done?.Invoke(true);
        }

        public static void Grant(string productId)
        {
            PlayerPrefs.SetInt(OwnedPrefix + productId, 1);
            PlayerPrefs.Save();
            Purchased?.Invoke(productId);
        }

        /// For testing the locked state again (Editor menu Zombie Rush > Store > Reset Purchases).
        public static void Revoke(string productId)
        {
            PlayerPrefs.DeleteKey(OwnedPrefix + productId);
            PlayerPrefs.Save();
        }
    }
}
