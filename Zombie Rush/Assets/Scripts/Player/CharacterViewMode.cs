using UnityEngine;
using UnityEngine.Rendering;

namespace ZombieRush.Player
{
    public enum CharacterView { FirstPerson, ThirdPerson }

    /// First-person / third-person switch for a character body. In first person the local
    /// player's own camera does not see the body, but it still casts its shadow; in third
    /// person the whole body renders. Squad mates are always shown in third person.
    public class CharacterViewMode : MonoBehaviour
    {
        [SerializeField] CharacterView view = CharacterView.ThirdPerson;

        Renderer[] renderers;

        public CharacterView View => view;

        void Awake()
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
            var mode = view == CharacterView.FirstPerson ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            foreach (var r in renderers)
                if (r != null) r.shadowCastingMode = mode;
        }

        void OnValidate()
        {
            if (Application.isPlaying) Apply();
        }
    }
}
