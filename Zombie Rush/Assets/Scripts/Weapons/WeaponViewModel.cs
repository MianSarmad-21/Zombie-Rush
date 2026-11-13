using UnityEngine;

namespace ZombieRush.Weapons
{
    /// First-person feel for a held gun: recoil kick, muzzle flash (particles + a short light
    /// pulse), a draw animation when the gun is brought up, and a reload with the gun dipping,
    /// the magazine dropping out and back in, and the action being worked. Only moves `model`
    /// (a child), so the weapon object itself keeps its place under the camera.
    [RequireComponent(typeof(WeaponBase))]
    public class WeaponViewModel : MonoBehaviour
    {
        public Transform model;
        public Transform muzzle;
        public ParticleSystem muzzleFlash;
        public Light flashLight;

        [Header("Recoil")]
        public float kickBack = 0.05f;
        public float kickPitch = 5f;
        public float returnSpeed = 12f;

        [Header("Reload")]
        public float reloadDip = 0.12f;
        public float reloadTilt = 30f;
        [Tooltip("How far the magazine drops out of the gun, metres (pistols; found by name).")]
        public float magDrop = 0.14f;

        [Header("Draw")]
        [Tooltip("Seconds for the gun to come up when it is selected.")]
        public float drawTime = 0.3f;
        public float drawDrop = 0.22f;
        public float drawTilt = 40f;

        WeaponBase weapon;
        Vector3 restPos;
        Quaternion restRot;
        float recoil;     // 0..1, decays back to rest
        float reloadBlend;
        float lightTimer;
        float draw;       // 1 = just selected (gun low), 0 = up
        float pumpTimer = -1f;
        float kickRoll;

        Transform mag, slider;
        GunRig rig;   // a Blender-animated gun moves itself: only the flash is done here
        Vector3 magLocal, sliderLocal;
        bool partsDirty;

        void Awake()
        {
            weapon = GetComponent<WeaponBase>();
            if (model != null) rig = model.GetComponentInChildren<GunRig>(true);
            if (model != null && rig == null)
            {
                restPos = model.localPosition; restRot = model.localRotation;
                mag = weapon.Style == GunStyle.Shotgun ? null : Find(model, "magazine");
                slider = weapon.Style == GunStyle.Shotgun ? null : (Find(model, "slider") ?? Find(model, "slide"));
                if (mag != null) magLocal = mag.localPosition;
                if (slider != null) sliderLocal = slider.localPosition;
            }
            if (flashLight != null) flashLight.enabled = false;
        }

        static Transform Find(Transform root, string key)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if (t != root && n.Contains(key) && !n.Contains("location")) return t;
            }
            return null;
        }

        void OnEnable()
        {
            weapon.Fired += HandleFired;
            draw = 1f;
            pumpTimer = -1f;
        }

        void OnDisable()
        {
            weapon.Fired -= HandleFired;
            recoil = reloadBlend = 0f;
            lightTimer = 0f;
            if (flashLight != null) flashLight.enabled = false;
            if (model != null && rig == null) { model.localPosition = restPos; model.localRotation = restRot; }
            RestoreParts();
        }

        void HandleFired()
        {
            recoil = Mathf.Min(1f, recoil + 0.75f);
            kickRoll = Random.Range(-1f, 1f);
            if (weapon.Style == GunStyle.Shotgun) pumpTimer = 0f;
            if (muzzleFlash != null) muzzleFlash.Play(true);
            if (flashLight != null) { flashLight.enabled = true; lightTimer = 0.05f; }
        }

        float StyleKick => weapon.Style == GunStyle.Shotgun ? 1.8f : weapon.Style == GunStyle.Pistol ? 1.2f : 1f;

        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            recoil = Mathf.MoveTowards(recoil, 0f, returnSpeed * dt * Mathf.Max(0.2f, recoil));
            float p = weapon.ReloadProgress;
            reloadBlend = Mathf.MoveTowards(reloadBlend, weapon.IsReloading ? 1f : 0f, dt * 5f);
            draw = Mathf.MoveTowards(draw, 0f, dt / Mathf.Max(0.05f, drawTime));

            if (lightTimer > 0f)
            {
                lightTimer -= dt;
                if (lightTimer <= 0f && flashLight != null) flashLight.enabled = false;
            }

            if (model == null || rig != null) return;
            float r = recoil * recoil * (3f - 2f * recoil);
            float d = reloadBlend * reloadBlend * (3f - 2f * reloadBlend);
            float dr = Smooth(draw);
            float k = StyleKick;

            // Reload beats: the gun dips and rolls, jerks when the magazine is pulled (0.40), pops up
            // when the new one goes in (0.80) and kicks again as the action is worked (0.93).
            float pull = Bell(p, 0.40f, 0.05f), seat = Bell(p, 0.80f, 0.04f), rack = Bell(p, 0.93f, 0.04f);
            Vector3 pos = restPos + new Vector3(0f, -reloadDip * d - 0.012f * pull + 0.018f * seat, -kickBack * r * k - 0.02f * rack);
            float pitch = -kickPitch * r * k + reloadTilt * d - 4f * pull + 5f * seat - 3f * rack;
            float roll = -10f * d + kickRoll * 2f * r;

            // Shotgun: pump after each shot.
            if (pumpTimer >= 0f)
            {
                pumpTimer += dt;
                float u = (pumpTimer - 0.2f) / 0.45f;
                if (u > 1f) pumpTimer = -1f;
                else if (u > 0f)
                {
                    float s = Mathf.Sin(Mathf.PI * u);
                    pos += new Vector3(0f, 0.01f * s, -0.035f * s);
                    pitch += 3f * s;
                }
            }

            // Draw: comes up from below.
            pos += new Vector3(0.02f * dr, -drawDrop * dr, -0.05f * dr);
            pitch += drawTilt * dr;
            roll += 8f * dr;

            model.localPosition = pos;
            model.localRotation = Quaternion.Euler(pitch, 0f, roll) * restRot;

            AnimateParts(p, r);
        }

        static float Bell(float p, float centre, float width)
        {
            if (p <= 0f) return 0f;
            return Mathf.Clamp01(1f - Mathf.Abs(p - centre) / width);
        }

        void AnimateParts(float p, float r)
        {
            bool active = p > 0.001f || (slider != null && r > 0.001f);
            if (!active) { if (partsDirty) RestoreParts(); return; }
            partsDirty = true;

            if (mag != null && mag.parent != null)
            {
                // Out and down 0.30-0.44, hidden (in the pouch) until 0.64, back up 0.64-0.80.
                float m = p < 0.30f ? 0f : p < 0.44f ? Mathf.InverseLerp(0.30f, 0.44f, p) : p < 0.64f ? 1f : p < 0.80f ? Mathf.InverseLerp(0.80f, 0.64f, p) : 0f;
                mag.gameObject.SetActive(!(p > 0.46f && p < 0.64f));
                Vector3 rest = mag.parent.TransformPoint(magLocal);
                mag.position = rest - model.up * (magDrop * Smooth(m));
            }
            if (slider != null && slider.parent != null)
            {
                float rack = p > 0.90f && p < 0.97f ? Mathf.Sin(Mathf.PI * Mathf.InverseLerp(0.90f, 0.97f, p)) : 0f;
                slider.position = slider.parent.TransformPoint(sliderLocal) - model.forward * (0.03f * Mathf.Clamp01(rack + r));
            }
        }

        void RestoreParts()
        {
            partsDirty = false;
            if (mag != null) { mag.gameObject.SetActive(true); mag.localPosition = magLocal; }
            if (slider != null) slider.localPosition = sliderLocal;
        }
    }
}
