using UnityEngine;
using UnityEngine.Rendering;

namespace ZombieRush.Player
{
    public enum CharacterView { FirstPerson, ThirdPerson }

    /// First-person / third-person switch for a character body. In first person the local
    /// player's own camera does not see the body (only its forearms and hands, firstPersonOnly),
    /// but it still casts its shadow; in third person the whole body renders. Squad mates are always shown in third person.
    public class CharacterViewMode : MonoBehaviour
    {
        [SerializeField] CharacterView view = CharacterView.ThirdPerson;
        [Tooltip("Shown only in first person, to the player's own camera (the forearms and hands that hold the gun).")]
        public Renderer[] firstPersonOnly;

        Renderer[] renderers;

        public CharacterView View => view;

        void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            Apply();
        }

        /// Picks up renderers added after Awake (e.g. the gun copied into the hand).
        public void RefreshRenderers()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            Apply();
        }

        public void SetView(CharacterView value)
        {
            view = value;
            Apply();
        }

        public void Toggle() => SetView(view == CharacterView.FirstPerson ? CharacterView.ThirdPerson : CharacterView.FirstPerson);

        void Apply()
        {
            if (renderers == null) return;
            bool fp = view == CharacterView.FirstPerson;
            var mode = fp ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (firstPersonOnly != null && System.Array.IndexOf(firstPersonOnly, r) >= 0)
                {
                    // Visible in first person only, no shadow (the full body already casts one).
                    r.enabled = fp;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    continue;
                }
                r.shadowCastingMode = mode;
            }
        }

        void OnValidate()
        {
            if (Application.isPlaying) Apply();
        }
    }
}
