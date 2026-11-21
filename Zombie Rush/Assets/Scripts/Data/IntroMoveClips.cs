using UnityEngine;

namespace ZombieRush.Data
{
    /// The body clip the match intro plays on everyone in the helicopter: sitting on a low troop seat, feet on the
    /// floor, pistol held low between the knees (Blender/player_anim.py SEATS["Sit_Pistol"]). Lives in Resources
    /// (ZombieRush/Cutscene/IntroMoves); IntroClipsTools fills it from ShadowOperative_Anims.fbx.
    [CreateAssetMenu(fileName = "IntroMoves", menuName = "Zombie Rush/Intro Move Clips")]
    public class IntroMoveClips : ScriptableObject
    {
        public const string ResourcePath = "ZombieRush/Cutscene/IntroMoves";

        public AnimationClip sit;
        [Tooltip("The seat the clip sits on, metres in game: its top above the body's origin, which stands on the floor (Blender SEAT_Z x the prefab's scale).")]
        public float seatHeight = 0.34f;
        [Tooltip("The seat's front edge, metres in game ahead of the body's origin (Blender SEAT_EDGE x the prefab's scale).")]
        public float seatEdge = 0.21f;
    }
}
