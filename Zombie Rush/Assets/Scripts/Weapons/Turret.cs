using UnityEngine;
using ZombieRush.Audio;
using ZombieRush.Core;
using ZombieRush.Zombie;

namespace ZombieRush.Weapons
{
    /// Auto turret (paid item). Rises out of the ground, turns its head toward the
    /// nearest zombie it can see, fires hitscan bursts from its gun points with a
    /// tracer + flash, and powers down when its time runs out.
    ///
    /// The head is turned with an absolute yaw in LateUpdate, after the model's
    /// own Animator (which plays the "Shoot" recoil clip), so the two never fight.
    public class Turret : MonoBehaviour
    {
        [Header("Model")]
        public Transform head;
        public Transform[] muzzles;
        [Tooltip("Direction the barrels point, in the head's local space (set by the install tool).")]
        public Vector3 barrelAxis = Vector3.forward;
        public Animator animator;

        [Header("Combat")]
        public float range = 24f;
        public float damage = 16f;
        public float fireRate = 6f;
        public float turnSpeed = 320f;
        [Tooltip("Starts shooting once the barrel is within this many degrees of the target.")]
        public float aimTolerance = 8f;
        public LayerMask hitMask = ~0;

        [Header("Life")]
        public float lifetime = 60f;
        public float riseTime = 0.6f;

        [Header("FX")]
        public Material tracerMaterial;
        public Color flashColor = new Color(1f, 0.72f, 0.35f);

        public float TimeLeft01 => Mathf.Clamp01(timeLeft / Mathf.Max(0.01f, lifetime));
        public bool Expired => state == State.Gone;

        enum State { Rising, Active, Sinking, Gone }
        State state;
        float stateTime;
        float timeLeft;
        Vector3 groundPos;
        float sinkDepth;
        bool deployed;

        ZombieHealth target;
        float retargetTimer;
        float nextShot;
        int muzzleIndex;

        Quaternion headRest;      // head world rotation when placed (yaw 0)
        Vector3 restBarrelFlat;    // barrel direction at yaw 0, flattened
        float yaw;

        LineRenderer[] tracers;
        float[] tracerTimer;
        Light flash;
        float flashTimer;

        static readonly int ShootHash = Animator.StringToHash("Shoot");
        static readonly RaycastHit[] hits = new RaycastHit[8];

        /// Called by TurretDeployer right after Instantiate.
        public void Deploy(Vector3 ground, Quaternion facing)
        {
            deployed = true;
            groundPos = ground;
            transform.SetPositionAndRotation(ground, facing);
            var bounds = new Bounds(ground, Vector3.zero);
            foreach (var r in GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            sinkDepth = Mathf.Max(0.5f, bounds.max.y - ground.y + 0.05f);
            transform.position = ground - Vector3.up * sinkDepth;
            timeLeft = lifetime;
            state = State.Rising;
            stateTime = 0f;

            var lib = SoundManager.Library;
            if (lib != null) SoundManager.PlayAt(lib.turretDeploy, ground + Vector3.up, 1f, 30f);
        }

        void Start()
        {
            if (!deployed) Deploy(transform.position, transform.rotation); // dropped into a scene by hand
            if (head == null) head = transform;
            headRest = head.rotation;
            restBarrelFlat = Vector3.ProjectOnPlane(head.TransformDirection(barrelAxis), Vector3.up);
            if (restBarrelFlat.sqrMagnitude < 1e-4f) restBarrelFlat = transform.forward;
            restBarrelFlat.Normalize();

            BuildFx();
        }

        void BuildFx()
        {
            int count = Mathf.Max(1, muzzles != null ? muzzles.Length : 0);
            tracers = new LineRenderer[count];
            tracerTimer = new float[count];
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Tracer" + i);
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.widthMultiplier = 0.05f;
                lr.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.25f));
                lr.sharedMaterial = tracerMaterial;
                lr.startColor = new Color(1f, 0.85f, 0.5f, 1f);
                lr.endColor = new Color(1f, 0.6f, 0.2f, 0f);
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.enabled = false;
                tracers[i] = lr;
            }

            var lightGo = new GameObject("MuzzleLight");
            lightGo.transform.SetParent(head, false);
            flash = lightGo.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.range = 6f;
            flash.intensity = 3f;
            flash.color = flashColor;
            flash.shadows = LightShadows.None;
            flash.enabled = false;
        }

        void Update()
        {
            stateTime += Time.deltaTime;
            switch (state)
            {
                case State.Rising:
                {
                    float k = Mathf.Clamp01(stateTime / Mathf.Max(0.01f, riseTime));
                    k = 1f - (1f - k) * (1f - k);
                    transform.position = groundPos - Vector3.up * sinkDepth * (1f - k);
                    if (k >= 1f) { state = State.Active; stateTime = 0f; }
                    break;
                }
                case State.Active:
                    timeLeft -= Time.deltaTime;
                    if (timeLeft <= 0f) { BeginSink(); break; }
                    UpdateCombat();
                    break;
                case State.Sinking:
                {
                    float k = Mathf.Clamp01(stateTime / 0.8f);
                    transform.position = groundPos - Vector3.up * sinkDepth * k * k;
                    if (k >= 1f) { state = State.Gone; Destroy(gameObject); }
                    break;
                }
            }
            UpdateFx();
        }

        void BeginSink()
        {
            state = State.Sinking;
            stateTime = 0f;
            target = null;
            var lib = SoundManager.Library;
            if (lib != null) SoundManager.PlayAt(lib.turretPowerDown, groundPos + Vector3.up, 1f, 30f);
        }

        /// Removed early (e.g. the player placed one too many).
        public void Retire()
        {
            if (state == State.Rising || state == State.Active) BeginSink();
        }

        void UpdateCombat()
        {
            if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;

            retargetTimer -= Time.deltaTime;
            if (retargetTimer <= 0f || target == null || target.IsDead || !target.isActiveAndEnabled)
            {
                retargetTimer = 0.25f;
                target = FindTarget();
            }
            if (target == null) return;

            Vector3 aimPoint = AimPoint(target);
            Vector3 toTarget = Vector3.ProjectOnPlane(aimPoint - head.position, Vector3.up);
            float wanted = Vector3.SignedAngle(restBarrelFlat, toTarget, Vector3.up);
            float delta = Mathf.DeltaAngle(yaw, wanted);
            yaw += Mathf.Clamp(delta, -turnSpeed * Time.deltaTime, turnSpeed * Time.deltaTime);

            if (Mathf.Abs(Mathf.DeltaAngle(yaw, wanted)) <= aimTolerance && Time.time >= nextShot)
            {
                nextShot = Time.time + 1f / Mathf.Max(0.1f, fireRate);
                Shoot(aimPoint);
            }
        }

        void LateUpdate()
        {
            if (head != null) head.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * headRest;
        }

        void OnDrawGizmosSelected()
        {
            if (head == null) return;
            Gizmos.color = Color.red;
            Gizmos.DrawRay(head.position, head.TransformDirection(barrelAxis).normalized * 2f);
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, range);
        }

        static Vector3 AimPoint(ZombieHealth z) => z.transform.position + Vector3.up * 1.2f;

        ZombieHealth FindTarget()
        {
            ZombieHealth best = null;
            float bestSqr = range * range;
            Vector3 eye = head.position;
            foreach (var z in ZombieHealth.Active)
            {
                if (z == null || z.IsDead) continue;
                Vector3 p = AimPoint(z);
                float sqr = (p - eye).sqrMagnitude;
                if (sqr >= bestSqr || !CanSee(eye, p, z)) continue;
                best = z;
                bestSqr = sqr;
            }
            return best;
        }

        /// Line of sight: the first thing hit (ignoring this turret) must be the zombie itself.
        bool CanSee(Vector3 from, Vector3 to, ZombieHealth z)
        {
            Vector3 dir = to - from;
            float dist = dir.magnitude;
            if (dist < 0.01f) return true;
            int n = Physics.RaycastNonAlloc(from, dir / dist, hits, dist, hitMask, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            Collider first = null;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].collider.transform.IsChildOf(transform)) continue;
                if (hits[i].distance < nearest) { nearest = hits[i].distance; first = hits[i].collider; }
            }
            return first == null || first.GetComponentInParent<ZombieHealth>() == z;
        }

        void Shoot(Vector3 aimPoint)
        {
            int count = muzzles != null && muzzles.Length > 0 ? muzzles.Length : 1;
            int i = muzzleIndex++ % count;
            Vector3 from = muzzles != null && muzzles.Length > 0 && muzzles[i] != null ? muzzles[i].position : head.position;

            // Slight spread so a burst doesn't look laser-perfect.
            Vector3 dir = (aimPoint - from).normalized;
            dir = Quaternion.Euler(Random.Range(-1.2f, 1.2f), Random.Range(-1.2f, 1.2f), 0f) * dir;
            Vector3 end = from + dir * range;

            int n = Physics.RaycastNonAlloc(from, dir, hits, range, hitMask, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            Collider hitCol = null;
            for (int h = 0; h < n; h++)
            {
                if (hits[h].collider.transform.IsChildOf(transform)) continue;
                if (hits[h].distance < nearest) { nearest = hits[h].distance; hitCol = hits[h].collider; end = hits[h].point; }
            }
            if (hitCol != null)
            {
                var zombie = hitCol.GetComponentInParent<ZombieHealth>();
                if (zombie != null) zombie.TakeDamage(damage, false);
                else hitCol.GetComponentInParent<IDamageable>()?.TakeDamage(damage);
            }

            if (tracers != null && i < tracers.Length)
            {
                tracers[i].SetPosition(0, from);
                tracers[i].SetPosition(1, end);
                tracers[i].enabled = true;
                tracerTimer[i] = 0.05f;
            }
            if (flash != null)
            {
                flash.transform.position = from;
                flash.enabled = true;
                flashTimer = 0.04f;
            }
            if (animator != null && animator.isActiveAndEnabled) animator.SetTrigger(ShootHash);

            var lib = SoundManager.Library;
            if (lib != null) SoundManager.PlayAt(lib.turretShot, from, 0.75f, 40f, 4);
        }

        void UpdateFx()
        {
            if (tracers != null)
                for (int i = 0; i < tracers.Length; i++)
                {
                    if (tracerTimer[i] <= 0f) continue;
                    tracerTimer[i] -= Time.deltaTime;
                    if (tracerTimer[i] <= 0f) tracers[i].enabled = false;
                }
            if (flashTimer > 0f)
            {
                flashTimer -= Time.deltaTime;
                if (flashTimer <= 0f && flash != null) flash.enabled = false;
            }
        }
    }
}
