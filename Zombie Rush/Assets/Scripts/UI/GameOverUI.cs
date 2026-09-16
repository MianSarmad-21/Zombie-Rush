using UnityEngine;
using ZombieRush.Core;

namespace ZombieRush.UI
{
    /// "YOU DIED" message shown the moment the player's health hits 0, right
    /// before GameManager auto-restarts the level from Wave 1.
    public class GameOverUI : MonoBehaviour
    {
        public CanvasGroup group;

        void Awake()
        {
            if (group != null) group.alpha = 0f;
        }

        void OnEnable() => GameEvents.OnPlayerDied += HandlePlayerDied;
        void OnDisable() => GameEvents.OnPlayerDied -= HandlePlayerDied;

        void HandlePlayerDied()
        {
            if (group != null) group.alpha = 1f;
        }
    }
}
