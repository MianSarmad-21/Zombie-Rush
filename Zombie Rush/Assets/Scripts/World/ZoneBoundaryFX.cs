using UnityEngine;
using ZombieRush.Player;

namespace ZombieRush.World
{
    /// The safe-zone edge: a glowing energy wall with a bright line where it meets the
    /// ground, hugging the terrain all the way round. It warms to red as the player
    /// gets close, flashes gold and grows smoothly when the zone expands.
    /// ZoneManager drives it through SetZone; it previews in the editor too.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class ZoneBoundaryFX : MonoBehaviour
    {
        static readonly int ProximityId = Shader.PropertyToID("_Proximity");
        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly int FadeId = Shader.PropertyToID("_Fade");

        [Tooltip("Use a fixed ring of this radius around this object (safe base, pickups) instead of following the ZoneManager. 0 = zone ring.")]
        public float fixedRadius;

        [Header("Look")]
        public Material wallMaterial;
        public Material groundMaterial;
        public float wallHeight = 6f;
        public float groundBandWidth = 1.6f;
        [Tooltip("Metres below the ground the wall starts, so it never floats on bumps.")]
        public float sink = 0.4f;

        [Header("Feedback")]
        [Tooltip("The wall starts warming to the danger colour this far from the edge.")]
        public float warnDistance = 7f;
        [Tooltip("Warm to the danger colour as the local player nears the edge (off for the safe base ring).")]
        public bool warnNearEdge = true;
        public float growTime = 0.9f;
        public float flashTime = 1.4f;

        Vector3 center;
        float targetRadius = -1f, shownRadius = -1f, builtRadius = -1f, radiusVelocity;
        float flash, fadeIn;

        Mesh wallMesh, groundMesh;
        Renderer wallRenderer, groundRenderer;
        MaterialPropertyBlock block;
        Terrain terrain;

        public void SetZone(Vector3 zoneCenter, float radius, bool animate)
        {
            center = zoneCenter;
            if (animate && shownRadius > 0f && radius > targetRadius) flash = 1f;
            targetRadius = radius;
            if (!animate || shownRadius < 0f) shownRadius = radius;
            Rebuild(force: true);
        }

        void OnEnable()
        {
            EnsureParts();
            // Editor preview before the game sets it: use the ZoneManager's start ring.
            if (targetRadius < 0f && fixedRadius > 0f)
            {
                center = transform.position;
                targetRadius = shownRadius = fixedRadius;
            }
            else if (targetRadius < 0f)
            {
                var zone = FindAnyObjectByType<ZoneManager>();
                if (zone != null)
                {
                    var c = zone.playerBase != null ? zone.playerBase.position : zone.center;
                    center = c;
                    targetRadius = shownRadius = zone.startRadius;
                }
                else
                {
                    center = transform.position;
                    targetRadius = shownRadius = 30f;
                }
            }
            fadeIn = Application.isPlaying ? 0f : 1f;
            Rebuild(force: true);
        }

        void OnDisable()
        {
            if (wallMesh != null) DestroyImmediate(wallMesh);
            if (groundMesh != null) DestroyImmediate(groundMesh);
            wallMesh = groundMesh = null;
        }

        void Update()
        {
            if (Application.isPlaying)
            {
                shownRadius = Mathf.SmoothDamp(shownRadius, targetRadius, ref radiusVelocity, growTime * 0.35f);
                if (Mathf.Abs(shownRadius - targetRadius) < 0.01f) shownRadius = targetRadius;
                flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime / Mathf.Max(0.01f, flashTime));
                fadeIn = Mathf.MoveTowards(fadeIn, 1f, Time.deltaTime);
            }
            Rebuild(force: false);
            UpdateMaterials();
        }

        void UpdateMaterials()
        {
            float proximity = 0f;
            var player = PlayerHealth.Instance;
            if (player != null && warnNearEdge)
            {
                var d = player.transform.position - center;
                d.y = 0f;
                proximity = 1f - Mathf.Clamp01((shownRadius - d.magnitude) / Mathf.Max(0.1f, warnDistance));
            }

            block ??= new MaterialPropertyBlock();
            block.SetFloat(ProximityId, proximity);
            block.SetFloat(FlashId, flash);
            block.SetFloat(FadeId, fadeIn);
            if (wallRenderer != null) wallRenderer.SetPropertyBlock(block);
            if (groundRenderer != null) groundRenderer.SetPropertyBlock(block);
        }

        void EnsureParts()
        {
            // The old placeholder cylinder (if any) is replaced by the parts below.
            if (TryGetComponent<MeshRenderer>(out var oldRenderer)) oldRenderer.enabled = false;
            if (TryGetComponent<Collider>(out var oldCollider)) oldCollider.enabled = false;

            wallRenderer = Part("ZoneWall", wallMaterial, ref wallMesh);
            groundRenderer = Part("ZoneGroundLine", groundMaterial, ref groundMesh);
        }

        MeshRenderer Part(string partName, Material material, ref Mesh mesh)
        {
            var t = transform.Find(partName);
            if (t == null)
            {
                var go = new GameObject(partName) { hideFlags = HideFlags.DontSave };
                t = go.transform;
                t.SetParent(transform, false);
            }
            if (!t.TryGetComponent<MeshFilter>(out var filter)) filter = t.gameObject.AddComponent<MeshFilter>();
            if (!t.TryGetComponent<MeshRenderer>(out var renderer)) renderer = t.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            if (mesh == null) mesh = new Mesh { name = partName, hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            filter.sharedMesh = mesh;
            return renderer;
        }

        void Rebuild(bool force)
        {
            if (wallMesh == null || groundMesh == null) EnsureParts();
            if (!force && Mathf.Abs(shownRadius - builtRadius) < 0.005f) return;
            builtRadius = shownRadius;

            transform.SetPositionAndRotation(center, Quaternion.identity);
            transform.localScale = Vector3.one;
            terrain = Terrain.activeTerrain;

            float r = Mathf.Max(1f, shownRadius);
            int segments = Mathf.Clamp(Mathf.CeilToInt(r * 3f), 96, 720);
            // Pattern repeats every 2 m; round so it meets itself seamlessly.
            float around = Mathf.Max(1, Mathf.Round(2f * Mathf.PI * r / 2f)) * 2f;

            var wallV = new Vector3[(segments + 1) * 2];
            var wallUV = new Vector2[wallV.Length];
            var groundV = new Vector3[(segments + 1) * 2];
            var groundUV = new Vector2[groundV.Length];
            float half = groundBandWidth * 0.5f;

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float a = t * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float u = t * around;

                var edge = center + dir * r;
                float ground = GroundHeight(edge) - center.y;
                var local = dir * r;

                wallV[i * 2] = new Vector3(local.x, ground - sink, local.z);
                wallV[i * 2 + 1] = new Vector3(local.x, ground + wallHeight, local.z);
                wallUV[i * 2] = new Vector2(u, -sink);
                wallUV[i * 2 + 1] = new Vector2(u, wallHeight);

                var inner = dir * (r - half);
                var outer = dir * (r + half);
                groundV[i * 2] = new Vector3(inner.x, GroundHeight(center + inner) - center.y + 0.06f, inner.z);
                groundV[i * 2 + 1] = new Vector3(outer.x, GroundHeight(center + outer) - center.y + 0.06f, outer.z);
                groundUV[i * 2] = new Vector2(u, -1f);
                groundUV[i * 2 + 1] = new Vector2(u, 1f);
            }

            var tris = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                int v = i * 2, k = i * 6;
                tris[k] = v; tris[k + 1] = v + 1; tris[k + 2] = v + 2;
                tris[k + 3] = v + 2; tris[k + 4] = v + 1; tris[k + 5] = v + 3;
            }

            Fill(wallMesh, wallV, wallUV, tris, r, wallHeight);
            Fill(groundMesh, groundV, groundUV, tris, r, 1f);
        }

        static void Fill(Mesh mesh, Vector3[] v, Vector2[] uv, int[] tris, float r, float height)
        {
            mesh.Clear();
            mesh.vertices = v;
            mesh.uv = uv;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.up * height * 0.5f, new Vector3(r * 2f + 4f, height + 40f, r * 2f + 4f));
        }

        /// Terrain height, lifted onto any road or pavement mesh lying just above it
        /// (a short ray, so building roofs are never picked).
        float GroundHeight(Vector3 p)
        {
            float h = terrain != null ? terrain.SampleHeight(p) + terrain.transform.position.y : center.y;
            var origin = new Vector3(p.x, h + 1.5f, p.z);
            if (Physics.Raycast(origin, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore)
                && hit.normal.y > 0.7f && !hit.collider.transform.IsChildOf(transform) && !(hit.collider is CharacterController))
                h = hit.point.y;
            return h;
        }
    }
}
