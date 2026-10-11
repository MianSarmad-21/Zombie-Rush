using System.Collections.Generic;
using UnityEngine;
using ZombieRush.Player;

namespace ZombieRush.World
{
    /// The safe-zone edge: a glowing energy wall with a bright line where it meets the
    /// ground, hugging the terrain all the way round. It warms to red as the player
    /// gets close, flashes gold and grows smoothly when the zone expands.
    /// Optional fog curtain (fogMaterial): a tall, thick wall of mist just outside the
    /// edge so players can't see past the zone.
    /// Full circle or half circle (SetShape). ZoneManager drives it through SetZone;
    /// it previews in the editor too and follows the PlayerBase marker when you move/rotate it.
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

        [Tooltip("Half circle: also raise the wall along the straight back edge (off = just the ground line there).")]
        public bool wallOnStraightEdge;

        [Header("Fog curtain (optional - hides everything outside)")]
        [Tooltip("ZombieRush/ZoneFogWall material. Empty = no curtain.")]
        public Material fogMaterial;
        public float fogHeight = 140f;
        [Tooltip("Metres outside the energy wall.")]
        public float fogOffset = 0.6f;

        [Header("Feedback")]
        [Tooltip("The wall starts warming to the danger colour this far from the edge.")]
        public float warnDistance = 7f;
        [Tooltip("Warm to the danger colour as the local player nears the edge (off for the safe base ring).")]
        public bool warnNearEdge = true;
        public float growTime = 0.9f;
        public float flashTime = 1.4f;

        Vector3 center, forward;
        bool straightEdge = true;
        float targetRadius = -1f, shownRadius = -1f, builtRadius = -1f, radiusVelocity;
        Vector3 builtCenter, builtForward;
        float flash, fadeIn;

        Mesh wallMesh, groundMesh, fogMesh;
        Renderer wallRenderer, groundRenderer, fogRenderer;
        MaterialPropertyBlock block;
        Terrain terrain;

        /// Half circle opening toward `halfForward` (zero = full circle). `straightEdge` also
        /// builds the wall along the flat side (the safe base ring leaves that to the zone wall).
        public void SetShape(Vector3 halfForward, bool straightEdge)
        {
            halfForward.y = 0f;
            forward = halfForward.sqrMagnitude > 0.0001f ? halfForward.normalized : Vector3.zero;
            this.straightEdge = straightEdge;
        }

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
            if (targetRadius < 0f) PreviewFromScene();
            fadeIn = Application.isPlaying ? 0f : 1f;
            Rebuild(force: true);
        }

        /// Editor preview (and the first frame in play) before the game sets the zone.
        void PreviewFromScene()
        {
            if (fixedRadius > 0f)
            {
                var safe = GetComponentInParent<SafeBase>();
                if (safe != null && safe.ring == this)
                {
                    var s = safe.Shape;
                    center = s.center;
                    SetShape(s.forward, false);
                    targetRadius = shownRadius = safe.radius;
                    return;
                }
                center = transform.parent != null ? transform.parent.position : transform.position;
                targetRadius = shownRadius = fixedRadius;
                return;
            }
            var zone = FindAnyObjectByType<ZoneManager>();
            if (zone != null)
            {
                center = zone.ArcCenter;
                SetShape(zone.Forward, true);
                targetRadius = shownRadius = zone.startRadius;
            }
            else
            {
                center = transform.position;
                targetRadius = shownRadius = 30f;
            }
        }

        void OnDisable()
        {
            if (wallMesh != null) DestroyImmediate(wallMesh);
            if (groundMesh != null) DestroyImmediate(groundMesh);
            if (fogMesh != null) DestroyImmediate(fogMesh);
            wallMesh = groundMesh = fogMesh = null;
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
            else if (fixedRadius <= 0f || transform.parent == null || transform.parent.GetComponent<SafeBase>() != null)
            {
                PreviewFromScene();   // follow the PlayerBase marker while editing (gear beams stay put)
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
                float inside = new ZoneShape(center, shownRadius, forward).DistanceInside(player.transform.position);
                proximity = 1f - Mathf.Clamp01(inside / Mathf.Max(0.1f, warnDistance));
            }

            block ??= new MaterialPropertyBlock();
            block.SetFloat(ProximityId, proximity);
            block.SetFloat(FlashId, flash);
            block.SetFloat(FadeId, fadeIn);
            if (wallRenderer != null) wallRenderer.SetPropertyBlock(block);
            if (groundRenderer != null) groundRenderer.SetPropertyBlock(block);
            if (fogRenderer != null) fogRenderer.SetPropertyBlock(block);
        }

        void EnsureParts()
        {
            // The old placeholder cylinder (if any) is replaced by the parts below.
            if (TryGetComponent<MeshRenderer>(out var oldRenderer)) oldRenderer.enabled = false;
            if (TryGetComponent<Collider>(out var oldCollider)) oldCollider.enabled = false;

            wallRenderer = Part("ZoneWall", wallMaterial, ref wallMesh);
            groundRenderer = Part("ZoneGroundLine", groundMaterial, ref groundMesh);
            if (fogMaterial != null) fogRenderer = Part("ZoneFog", fogMaterial, ref fogMesh);
            else
            {
                var fogT = transform.Find("ZoneFog");
                if (fogT != null) DestroyImmediate(fogT.gameObject);
                fogRenderer = null;
            }
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

        // Outline scratch: local XZ point, outward normal, metres along the outline.
        readonly List<Vector3> outlinePoints = new List<Vector3>();
        readonly List<Vector3> outlineNormals = new List<Vector3>();
        readonly List<float> outlineU = new List<float>();
        int arcPointCount;

        void BuildOutline(float r)
        {
            outlinePoints.Clear(); outlineNormals.Clear(); outlineU.Clear();
            var shape = new ZoneShape(Vector3.zero, r, forward);
            bool half = shape.Half;
            float arcLength = (half ? Mathf.PI : 2f * Mathf.PI) * r;
            int segments = Mathf.Clamp(Mathf.CeilToInt(r * (half ? 1.5f : 3f)), half ? 48 : 96, 720);
            // Full ring: the pattern repeats every 2 m, rounded so it meets itself seamlessly.
            float around = half ? arcLength : Mathf.Max(1, Mathf.Round(arcLength / 2f)) * 2f;

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                var dir = shape.Direction(t);
                outlinePoints.Add(dir * r);
                outlineNormals.Add(dir);
                outlineU.Add(t * around);
            }
            arcPointCount = outlinePoints.Count;
            if (half && straightEdge)
            {
                // Back along the straight edge (right end -> left end); outward = behind the base.
                int n = Mathf.Max(8, Mathf.CeilToInt(r));
                var right = shape.Right;
                for (int i = 1; i <= n; i++)
                {
                    float t = (float)i / n;
                    outlinePoints.Add(right * Mathf.Lerp(r, -r, t));
                    outlineNormals.Add(-forward);
                    outlineU.Add(around + t * 2f * r);
                }
            }
        }

        void Rebuild(bool force)
        {
            if (wallMesh == null || groundMesh == null || (fogMaterial != null && fogMesh == null)) EnsureParts();
            if (!force && Mathf.Abs(shownRadius - builtRadius) < 0.005f
                && (center - builtCenter).sqrMagnitude < 0.0001f && (forward - builtForward).sqrMagnitude < 0.0001f) return;
            builtRadius = shownRadius;
            builtCenter = center;
            builtForward = forward;

            transform.SetPositionAndRotation(center, Quaternion.identity);
            transform.localScale = Vector3.one;
            terrain = Terrain.activeTerrain;

            float r = Mathf.Max(1f, shownRadius);
            BuildOutline(r);
            int count = outlinePoints.Count;

            var wallV = new Vector3[count * 2];
            var wallUV = new Vector2[wallV.Length];
            var groundV = new Vector3[count * 2];
            var groundUV = new Vector2[groundV.Length];
            bool fog = fogMesh != null && fogMaterial != null;
            var fogV = fog ? new Vector3[count * 2] : null;
            var fogUV = fog ? new Vector2[count * 2] : null;
            float half = groundBandWidth * 0.5f;

            for (int i = 0; i < count; i++)
            {
                var local = outlinePoints[i];
                var normal = outlineNormals[i];
                float u = outlineU[i];
                float ground = GroundHeight(center + local) - center.y;

                wallV[i * 2] = new Vector3(local.x, ground - sink, local.z);
                wallV[i * 2 + 1] = new Vector3(local.x, ground + wallHeight, local.z);
                wallUV[i * 2] = new Vector2(u, -sink);
                wallUV[i * 2 + 1] = new Vector2(u, wallHeight);

                var inner = local - normal * half;
                var outer = local + normal * half;
                groundV[i * 2] = new Vector3(inner.x, GroundHeight(center + inner) - center.y + 0.06f, inner.z);
                groundV[i * 2 + 1] = new Vector3(outer.x, GroundHeight(center + outer) - center.y + 0.06f, outer.z);
                groundUV[i * 2] = new Vector2(u, -1f);
                groundUV[i * 2 + 1] = new Vector2(u, 1f);

                if (fog)
                {
                    var f = local + normal * fogOffset;
                    float fg = GroundHeight(center + f) - center.y;
                    fogV[i * 2] = new Vector3(f.x, fg - 2f, f.z);
                    fogV[i * 2 + 1] = new Vector3(f.x, fg + fogHeight, f.z);
                    fogUV[i * 2] = new Vector2(u, -2f);
                    fogUV[i * 2 + 1] = new Vector2(u, fogHeight);
                }
            }

            var tris = new int[(count - 1) * 6];
            for (int i = 0; i < count - 1; i++)
            {
                int v = i * 2, k = i * 6;
                tris[k] = v; tris[k + 1] = v + 1; tris[k + 2] = v + 2;
                tris[k + 3] = v + 2; tris[k + 4] = v + 1; tris[k + 5] = v + 3;
            }

            // The tall wall follows the round edge only (unless asked); the straight back edge just gets
            // the ground line, so the back of the map stays in view.
            int wallSegments = (wallOnStraightEdge ? count : arcPointCount) - 1;
            var wallTris = tris;
            if (wallSegments < count - 1)
            {
                wallTris = new int[wallSegments * 6];
                System.Array.Copy(tris, wallTris, wallTris.Length);
            }
            Fill(wallMesh, wallV, wallUV, wallTris, r, wallHeight);
            Fill(groundMesh, groundV, groundUV, tris, r, 1f);
            if (fog) Fill(fogMesh, fogV, fogUV, tris, r + fogOffset, fogHeight);
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
