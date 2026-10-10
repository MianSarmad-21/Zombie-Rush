using UnityEngine;

namespace ZombieRush.Atmosphere
{
    /// Put this in a scene that brings its own sky, sun, fog and post-processing
    /// (e.g. the Post-Apocalyptic city map): EnvironmentApplier then leaves the
    /// scene's lighting alone instead of applying the Day/Night profile.
    [DisallowMultipleComponent]
    public class KeepSceneEnvironment : MonoBehaviour
    {
    }
}
