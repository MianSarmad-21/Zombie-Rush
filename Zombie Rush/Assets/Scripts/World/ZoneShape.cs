using UnityEngine;

namespace ZombieRush.World
{
    /// A flat (XZ) zone outline: a full circle, or a half circle whose straight edge runs
    /// through `center` with the round side facing `forward` (the base sits against the
    /// straight edge at the city's side, the zone grows across the city in front of it).
    /// Shared by the fight zone, the safe base, the wall FX and zombie spawning.
    public struct ZoneShape
    {
        public Vector3 center;
        /// Flat unit direction the half circle opens toward; zero = full circle.
        public Vector3 forward;
        public float radius;

        public ZoneShape(Vector3 center, float radius, Vector3 forward)
        {
            this.center = center;
            this.radius = radius;
            forward.y = 0f;
            this.forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.zero;
        }

        public bool Half => forward != Vector3.zero;
        public Vector3 Right => Vector3.Cross(Vector3.up, forward);

        /// Inside test; a positive margin grows the shape (radius and straight edge).
        public bool Contains(Vector3 p, float margin = 0f)
        {
            var d = Flat(p - center);
            float r = radius + margin;
            if (d.sqrMagnitude > r * r) return false;
            return !Half || Vector3.Dot(d, forward) >= -margin;
        }

        /// Metres to the nearest edge (negative = outside).
        public float DistanceInside(Vector3 p)
        {
            var d = Flat(p - center);
            float dist = radius - d.magnitude;
            return Half ? Mathf.Min(dist, Vector3.Dot(d, forward)) : dist;
        }

        /// The point itself if inside, else the nearest point `inset` metres inside the edge.
        public Vector3 ClampInside(Vector3 p, float inset = 0f)
        {
            var d = Flat(p - center);
            if (Half)
            {
                float along = Vector3.Dot(d, forward);
                if (along < inset) d += forward * (inset - along);
            }
            float r = Mathf.Max(0f, radius - inset);
            if (d.sqrMagnitude > r * r) d = d.normalized * r;
            if (Half)
            {
                float along = Vector3.Dot(d, forward);
                if (along < inset) d += forward * (inset - along);
            }
            return new Vector3(center.x + d.x, p.y, center.z + d.z);
        }

        /// Just outside the round edge, in the direction of `p` (used to keep zombies out of the base).
        public Vector3 OutsidePoint(Vector3 p, float outside)
        {
            var d = Flat(p - center);
            if (Half)
            {
                float along = Vector3.Dot(d, forward);
                if (along < 0f) d -= forward * along;
            }
            if (d.sqrMagnitude < 0.0001f) d = Half ? forward : Vector3.forward;
            var e = center + d.normalized * (radius + outside);
            e.y = p.y;
            return e;
        }

        /// Direction along the round edge: t = 0..1 sweeps the whole circle, or the half circle side to side.
        public Vector3 Direction(float t)
        {
            if (!Half)
            {
                float a = t * Mathf.PI * 2f;
                return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            }
            float h = (t - 0.5f) * Mathf.PI;
            return forward * Mathf.Cos(h) + Right * Mathf.Sin(h);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
