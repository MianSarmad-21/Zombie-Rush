using UnityEngine;
using ZombieRush.Core;

namespace ZombieRush.UI.Kit
{
    /// Hides this canvas (the gameplay UI) while the lobby screen is up, so only the briefing shows.
    [RequireComponent(typeof(Canvas))]
    public class HideInLobby : MonoBehaviour
    {
        Canvas canvas;

        void Awake()
        {
            canvas = GetComponent<Canvas>();
            GameEvents.OnGameStateChanged += Apply;
        }

        void OnDestroy() => GameEvents.OnGameStateChanged -= Apply;

        void Start() => Apply(GameManager.Instance != null ? GameManager.Instance.State : GameState.Playing);

        void Apply(GameState state) => canvas.enabled = state != GameState.Lobby;
    }
}
