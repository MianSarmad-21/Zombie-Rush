using UnityEngine;

namespace ZombieRush.Effects
{
    /// A short-lived, fully code-driven blood burst - no imported VFX asset needed.
    public static class BloodEffect
    {
        static Material cachedMaterial;

        public static void Spawn(Vector3 position)
        {
            var go = new GameObject("BloodBurst");
            go.transform.position = position;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startColor = new Color(0.5f, 0.02f, 0.02f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
            main.startLifetime = 0.7f;
            main.gravityModifier = 2f;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 16) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.12f;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = GetMaterial();

            Object.Destroy(go, 2f);
        }

        static Material GetMaterial()
        {
            if (cachedMaterial != null) return cachedMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            cachedMaterial = new Material(shader) { color = new Color(0.5f, 0.02f, 0.02f) };
            return cachedMaterial;
        }
    }
}
