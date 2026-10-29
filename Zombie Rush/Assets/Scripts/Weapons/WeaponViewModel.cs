using UnityEngine;

namespace ZombieRush.Weapons
{
    /// First-person feel for a held gun: recoil kick, muzzle flash (particles + a
    /// short light pulse) and lowering the gun while reloading. Only moves `model`
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

        WeaponBase weapon;
        Vector3 restPos;
        Quaternion restRot;
        float recoil;     // 0..1, decays back to rest
        float reloadBlend;
        float lightTimer;

        void Awake()
        {
            weapon = GetComponent<WeaponBase>();
            if (model != null) { restPos = model.localPosition; restRot = model.localRotation; }
            if (flashLight != null) flashLight.enabled = false;
        }

        void OnEnable() => weapon.Fired += HandleFired;

        void OnDisable()
        {
            weapon.Fired -= HandleFired;
            recoil = reloadBlend = 0f;
            lightTimer = 0f;
            if (flashLight != null) flashLight.enabled = false;
            if (model != null) { model.localPosition = restPos; model.localRotation = restRot; }
        }

        void HandleFired()
        {
            recoil = Mathf.Min(1f, recoil + 0.75f);
            if (muzzleFlash != null) muzzleFlash.Play(true);
            if (flashLight != null) { flashLight.enabled = true; lightTimer = 0.05f; }
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            recoil = Mathf.MoveTowards(recoil, 0f, returnSpeed * dt * Mathf.Max(0.2f, recoil));
            reloadBlend = Mathf.MoveTowards(reloadBlend, weapon.IsReloading ? 1f : 0f, dt * 5f);

            if (lightTimer > 0f)
            {
                lightTimer -= dt;
                if (lightTimer <= 0f && flashLight != null) flashLight.enabled = false;
            }

            if (model == null) return;
            float r = recoil * recoil * (3f - 2f * recoil);
            float d = reloadBlend * reloadBlend * (3f - 2f * reloadBlend);
            model.localPosition = restPos + new Vector3(0f, -reloadDip * d, -kickBack * r);
            model.localRotation = Quaternion.Euler(-kickPitch * r + reloadTilt * d, 0f, -10f * d) * restRot;
        }
    }
}
