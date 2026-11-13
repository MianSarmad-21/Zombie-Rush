using System.Collections.Generic;
using UnityEngine;
using ZombieRush.Data;
using ZombieRush.Effects;
using ZombieRush.Player;

namespace ZombieRush.Zombie
{
    /// A sticky goo puddle left by Spitter goo: slows and slowly hurts players standing in it,
    /// then shrinks away. Checks players by distance, so it needs no physics trigger. Pooled.
    public class GooPuddle : MonoBehaviour
    {
        const int MaxPool = 12;
        const float TickInterval = 0.2f;
        static readonly List<GooPuddle> pool = new List<GooPuddle>();
        static readonly RaycastHit[] groundHits = new RaycastHit[8];

        SpitterConfig cfg;
        float damageScale;
        float age;
        float tick;
        float radius;

        public static void Spawn(Vector3 position, SpitterConfig cfg, float damageScale)
        {
            var p = Get();
            if (p == null) return;
            p.Begin(GroundBelow(position), cfg, damageScale);
        }

        static GooPuddle Get()
        {
            GooPuddle oldest = null;
            foreach (var g in pool)
            {
                if (g == null) continue;
                if (!g.gameObject.activeSelf) return g;
                if (oldest == null || g.age > oldest.age) oldest = g;
            }
            if (pool.Count >= MaxPool) return oldest; // reuse the oldest puddle
            var go = new GameObject("GooPuddle");
            DontDestroyOnLoad(go);
            go.AddComponent<MeshFilter>().sharedMesh = FxMaterials.Primitive(PrimitiveType.Cylinder);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = FxMaterials.Lit("GooPuddle", new Color(0.25f, 0.6f, 0.05f), new Color(0.15f, 0.6f, 0.03f));
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var p = go.AddComponent<GooPuddle>();
            pool.Add(p);
            return p;
        }

        static Vector3 GroundBelow(Vector3 p)
        {
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 1.5f, Vector3.down, groundHits, 6f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var h = groundHits[i];
                if (h.collider.GetComponentInParent<ZombieHealth>() != null || h.collider.GetComponentInParent<PlayerHealth>() != null) continue;
                if (h.point.y > best) best = h.point.y;
            }
            if (!float.IsNegativeInfinity(best)) p.y = best;
            return p;
        }

        void Begin(Vector3 position, SpitterConfig config, float scale)
        {
            cfg = config;
            damageScale = scale;
            age = 0f;
            tick = 0f;
            radius = cfg.puddleRadius * Random.Range(0.85f, 1.1f);
            transform.SetPositionAndRotation(position + Vector3.up * 0.02f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            transform.localScale = Vector3.zero;
            gameObject.SetActive(true);
        }

        void Update()
        {
            age += Time.deltaTime;
            float life = cfg.puddleDuration;
            if (age >= life) { gameObject.SetActive(false); return; }

            // Splash out fast, sit, then shrink away over the last second.
            float grow = Mathf.Clamp01(age / 0.15f);
            float fade = Mathf.Clamp01((life - age) / 1f);
            float r = radius * grow * Mathf.Lerp(0.3f, 1f, fade);
            transform.localScale = new Vector3(r * 2f, 0.012f, r * 2f * 0.85f);

            tick -= Time.deltaTime;
            if (tick > 0f) return;
            tick = TickInterval;
            foreach (var p in PlayerHealth.All)
            {
                if (p == null || p.IsDead) continue;
                Vector3 d = p.transform.position - transform.position;
                if (Mathf.Abs(d.y) > 1.2f) continue;
                d.y = 0f;
                if (d.sqrMagnitude > r * r) continue;
                var pc = p.GetComponent<PlayerController>();
                if (pc != null) pc.ApplySlow(cfg.puddleSlow, TickInterval + 0.25f);
                if (cfg.puddleDps > 0f) p.TakeDamage(cfg.puddleDps * TickInterval * damageScale);
            }
        }
    }
}
