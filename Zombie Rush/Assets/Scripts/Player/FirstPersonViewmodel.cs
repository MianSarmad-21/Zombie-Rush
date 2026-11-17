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
            FitLensMask();
        }

        // ---- scope lens mask: a depth-only disc in front of the arms-and-gun camera, the size of the scope's
        // glass on screen. The gun and arms behind it are not drawn, so through the lens only the world shows
        // (a real scope's tube hides the gun in front of the eye); round and below the eyepiece the gun shows.
        Transform lensMask;
        float lensFraction;   // lens radius as a share of half the screen height, 0 = off

        /// Scope glass radius on screen (share of half the screen height); 0 turns the mask off.
        public void SetLensMask(float fractionOfHalfHeight)
        {
            lensFraction = Mathf.Max(0f, fractionOfHalfHeight);
            if (lensFraction > 0f && lensMask == null) BuildLensMask();
            if (lensMask != null && lensMask.gameObject.activeSelf != lensFraction > 0f) lensMask.gameObject.SetActive(lensFraction > 0f);
            FitLensMask();
        }

        void BuildLensMask()
        {
            if (overlay == null || layer < 0) return;
            var shader = Resources.Load<Shader>("ZombieRush/ScopeDepthMask");
            if (shader == null) shader = Shader.Find("ZombieRush/ScopeDepthMask");
            if (shader == null) return;
            const int n = 64;
            var v = new Vector3[n + 1];
            var tris = new int[n * 3];
            v[0] = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                v[i + 1] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                tris[i * 3] = 0; tris[i * 3 + 1] = i + 1; tris[i * 3 + 2] = (i + 1) % n + 1;
            }
            var mesh = new Mesh { name = "ScopeLensMask", vertices = v, triangles = tris };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            var go = new GameObject("ScopeLensMask");
            go.layer = layer;
            go.transform.SetParent(overlay.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = new Material(shader) { name = "ScopeDepthMask (runtime)" };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            lensMask = go.transform;
        }

        void FitLensMask()
        {
            if (lensMask == null || overlay == null || lensFraction <= 0f) return;
            // just past the near clip, sized so it covers the glass exactly from the camera's point of view
            float d = overlay.nearClipPlane * 1.5f;
            float radius = d * Mathf.Tan(overlay.fieldOfView * 0.5f * Mathf.Deg2Rad) * lensFraction;
            lensMask.localPosition = new Vector3(0f, 0f, d);
            lensMask.localRotation = Quaternion.identity;
            lensMask.localScale = new Vector3(radius, radius, 1f);
        }
    }
}
