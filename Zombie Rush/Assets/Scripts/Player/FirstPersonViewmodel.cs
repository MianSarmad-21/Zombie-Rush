using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ZombieRush.Player
{
    /// The first-person arms-and-gun camera, the way shooters draw their "viewmodel": a second camera
    /// stacked on the main one that draws only the FirstPersonView layer (the first-person arms and the
    /// camera's guns) on top of the world. The gun never pokes into walls, it gets its own field of view,
    /// and it can be drawn from a spot other than the eyes (each gun's WeaponData.eyeOffset) while the
    /// world, and every shot, still comes from the eyes.
    /// Added to the main camera at runtime by BodyGunPose; does nothing if the layer is missing.
    public class FirstPersonViewmodel : MonoBehaviour
    {
        public const string LayerName = "FirstPersonView";

        Camera main, overlay;
        int layer = -1;
        bool on;

        public static FirstPersonViewmodel For(Camera mainCamera)
        {
            if (mainCamera == null) return null;
            var vm = mainCamera.GetComponent<FirstPersonViewmodel>();
            if (vm == null) vm = mainCamera.gameObject.AddComponent<FirstPersonViewmodel>();
            return vm;
        }

        /// Scope view: the first-person arms and guns are not drawn at all (overlay off, main camera skips them).
        public bool Hidden;

        /// Test switch: draw everything with the main camera, as before the overlay existed.
        public static bool DebugOff;

        /// True when the layer exists and the overlay camera could be set up.
        public bool Ready => overlay != null && !DebugOff;

        void Awake()
        {
            main = GetComponent<Camera>();
            layer = LayerMask.NameToLayer(LayerName);
            if (main == null || layer < 0) return;

            var go = new GameObject("FirstPersonViewmodelCamera");
            go.transform.SetParent(transform, false);
            overlay = go.AddComponent<Camera>();
            overlay.cullingMask = 1 << layer;
            overlay.clearFlags = CameraClearFlags.Depth;
            overlay.nearClipPlane = 0.01f;
            overlay.farClipPlane = 10f;
            overlay.fieldOfView = main.fieldOfView;
            var data = overlay.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Overlay;
            data.renderShadows = false;
            var mainData = main.GetUniversalAdditionalCameraData();
            if (!mainData.cameraStack.Contains(overlay)) mainData.cameraStack.Add(overlay);
            overlay.enabled = false;
            SetOn(false);
        }

        void OnDestroy()
        {
            if (main != null && overlay != null)
            {
                var mainData = main.GetUniversalAdditionalCameraData();
                if (mainData != null) mainData.cameraStack.Remove(overlay);
            }
        }

        /// Puts an object (and everything under it) on the first-person layer.
        public void Claim(GameObject root)
        {
            if (layer < 0 || root == null) return;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        /// While on, the main camera skips the first-person layer and the overlay draws it; while off,
        /// the main camera draws it as usual (third person, guns without body clips, the moment of a swap).
        public void SetOn(bool value)
        {
            if (main == null || layer < 0) return;
            on = value && overlay != null && !DebugOff && !Hidden;
            if (overlay != null) overlay.enabled = on;
            if (on || Hidden) main.cullingMask &= ~(1 << layer);
            else main.cullingMask |= 1 << layer;
        }

        /// Where the overlay draws from this frame (world), its field of view and near clip.
        public void Pose(Vector3 position, Quaternion rotation, float fov, float nearClip)
        {
            if (overlay == null) return;
            overlay.transform.SetPositionAndRotation(position, rotation);
            overlay.fieldOfView = fov;
            overlay.nearClipPlane = Mathf.Max(0.005f, nearClip);
        }
    }
}
