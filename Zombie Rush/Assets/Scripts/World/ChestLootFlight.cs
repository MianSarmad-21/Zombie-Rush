using UnityEngine;

namespace ZombieRush.World
{
    /// Loot popping out of an opened chest: flies from the chest in an arc, spinning, lands with a small bounce, then
    /// becomes a normal pickup (its trigger switches on). It grows from small as it leaves the chest, with a glowing
    /// trail of motes behind it.
    public class ChestLootFlight : MonoBehaviour
    {
        Vector3 from, to;
        float duration, height, t;
        Vector3 scale;
        Collider[] triggers;
        Vector3 spin;
        bool landed;

        public static void Launch(GameObject item, Vector3 from, Vector3 to, float duration, float height)
        {
            var f = item.AddComponent<ChestLootFlight>();
            f.from = from; f.to = to; f.duration = duration; f.height = height;
            f.scale = item.transform.localScale;
            f.spin = new Vector3(Random.Range(-360f, 360f), Random.Range(180f, 540f), Random.Range(-180f, 180f));
            f.triggers = item.GetComponentsInChildren<Collider>();
            foreach (var c in f.triggers) c.enabled = false;   // no pick-up in mid-air
            item.transform.position = from;
            item.transform.localScale = f.scale * 0.3f;
            f.AddTrail();
        }

        void AddTrail()
        {
            var go = new GameObject("Trail");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = 0.4f; main.startSpeed = 0f; main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
            main.startColor = new Color(1f, 0.82f, 0.4f, 0.9f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            var em = ps.emission; em.rateOverTime = 70f;
            var sh = ps.shape; sh.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = LootChest.FxMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
        }

        void Update()
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            if (!landed)
            {
                var p = Vector3.Lerp(from, to, k) + Vector3.up * (4f * height * k * (1f - k));
                transform.position = p;
                transform.rotation = Quaternion.Euler(spin * Time.deltaTime) * transform.rotation;
                transform.localScale = scale * Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(k * 2.5f));
                if (k >= 1f)
                {
                    landed = true; t = 0f;
                    var e = transform.eulerAngles; transform.rotation = Quaternion.Euler(0f, e.y, 0f);
                }
                return;
            }
            // a small bounce, then a normal pickup
            float b = Mathf.Clamp01(t / 0.25f);
            transform.position = to + Vector3.up * (0.18f * 4f * b * (1f - b));
            transform.localScale = scale * (1f + 0.12f * Mathf.Sin(b * Mathf.PI));
            if (b >= 1f)
            {
                transform.position = to; transform.localScale = scale;
                foreach (var c in triggers) if (c != null) c.enabled = true;
                var trail = transform.Find("Trail");
                if (trail != null) { var ps = trail.GetComponent<ParticleSystem>(); ps.Stop(); Destroy(trail.gameObject, 0.5f); }
                Destroy(this);
            }
        }
    }
}
