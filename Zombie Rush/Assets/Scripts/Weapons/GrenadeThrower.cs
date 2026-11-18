using System;
using UnityEngine;
using ZombieRush.Core;
using ZombieRush.Data;
using ZombieRush.Player;

namespace ZombieRush.Weapons
{
    /// Frag grenades the way shooters do them (COD, IGI): hold G (or the touch grenade button) to pull the pin and
    /// cook, an arc shows where it will land, let go to throw. The gun stays in hand but holds fire meanwhile.
    /// The fuse runs from the pin pull; cooked too long it goes off in the hand.
    ///   - first person: the gun dips out of view and the grenade comes up in front of the camera;
    ///   - third person (and teammates' copies): the right arm winds up over the shoulder and swings forward.
    /// Count lives here (chest pickups add to it); the HUD listens to CountChanged.
    [DefaultExecutionOrder(1100)]   // after BodyGunPose (1000): the throwing arm overrides the right arm's hold
    public class GrenadeThrower : MonoBehaviour
    {
        public static GrenadeThrower Local { get; private set; }
        /// (count, max) whenever the local player's grenade count changes.
        public static event Action<int, int> CountChanged;

        public int Count { get; private set; }
        public bool Busy => state != State.Idle;
        public bool Cooking => state == State.Cooking;
        /// Seconds of fuse left while cooking (0 when not cooking).
        public float FuseLeft => state == State.Cooking ? Mathf.Max(0f, cfg.fuse - (Time.time - pinAt)) : 0f;

        enum State { Idle, Cooking, Throwing }
        State state;
        float pinAt, throwAt, nextNote, remoteThrowAt = -10f, testHoldUntil = -1f;
        Vector3? testAim;
        bool released, heldLast, remote;

        GrenadeConfig cfg;
        PlayerViewSwitcher view;
        Animator body;
        CharacterController mover;
        Collider[] ownColliders;
        LineRenderer arc, ring;
        Transform tpHeld, overlayCam;
        Vector3[] arcPts = new Vector3[64];

        void Awake()
        {
            cfg = GrenadeConfig.Get();
            view = GetComponentInParent<PlayerViewSwitcher>();
            if (view == null) view = GetComponentInChildren<PlayerViewSwitcher>();
            var weapons = GetComponentInChildren<PlayerWeaponController>();
            remote = (weapons != null && weapons.remote) || (view != null && view.remote);
            var cba = GetComponentInChildren<CharacterBodyAnimator>();
            body = cba != null ? cba.Animator : null;
            mover = GetComponent<CharacterController>();
            ownColliders = GetComponentsInChildren<Collider>();
        }

        void Start()
        {
            if (remote || cfg == null) return;
            Local = this;
            SetCount(cfg.startCount);
        }

        void OnDestroy()
        {
            if (Local == this) { Local = null; GameInput.FireBlocked = false; }
            if (fpArms != null) Destroy(fpArms.gameObject);
        }

        /// Adds grenades (a pickup). Returns how many were taken (0 when full).
        public int Add(int amount)
        {
            if (cfg == null) return 0;
            int got = Mathf.Clamp(cfg.maxCarry - Count, 0, amount);
            if (got > 0) SetCount(Count + got);
            return got;
        }

        public bool Full => cfg != null && Count >= cfg.maxCarry;

        void SetCount(int value)
        {
            Count = Mathf.Max(0, value);
            CountChanged?.Invoke(Count, cfg != null ? cfg.maxCarry : 0);
        }

        /// A new match / restart: back to the starting count.
        public void ResetCount() { Cancel(); if (cfg != null) SetCount(cfg.startCount); }

        // ------------------------------------------------------------------ input and states (local player)

        void Update()
        {
            if (remote || cfg == null) return;
            bool held = GameInput.GrenadeHeld || Time.time < testHoldUntil;
            bool pressed = held && !heldLast;
            heldLast = held;

            bool playing = GameManager.Instance == null || GameManager.Instance.State == GameState.Playing;
            var health = PlayerHealth.Instance;
            if (!playing || (health != null && health.IsDead)) { if (state == State.Cooking) Cancel(); return; }

            switch (state)
            {
                case State.Idle:
                    if (pressed) TryPullPin();
                    break;
                case State.Cooking:
                    if (cfg.fuse - (Time.time - pinAt) <= 0f)
                    {
                        if (cfg.explodeInHand) BlowInHand();
                        else StartThrow();
                        break;
                    }
                    if (!held && Time.time - pinAt >= cfg.minHold) StartThrow();
                    break;
                case State.Throwing:
                    float t = (Time.time - throwAt) / Mathf.Max(0.05f, cfg.throwTime);
                    if (!released && t >= cfg.releaseAt) Release();
                    if (t >= 1f) Finish();
                    break;
            }
            UpdateArc();
            UpdateFirstPerson();
        }

        void TryPullPin()
        {
            if (Count <= 0) { Note("NO GRENADES"); return; }
            if (!World.SafeBase.FiringAllowedAt(transform.position)) { Note("NO GRENADES IN THE SAFE BASE"); return; }
            state = State.Cooking;
            pinAt = Time.time;
            SetCount(Count - 1);
            GameInput.FireBlocked = true;
            if (cfg.pinPull != null) Audio.SoundManager.Play(cfg.pinPull, 0.8f);
            if (LogEvents) Debug.Log($"[Grenade] pin pulled at {Time.time:0.00}, {Count} left");
        }

        void Note(string text)
        {
            if (Time.time < nextNote) return;
            nextNote = Time.time + 1.5f;
            UI.PickupToast.Show(text, new Color(1f, 0.6f, 0.35f));
        }

        void StartThrow()
        {
            state = State.Throwing;
            throwAt = Time.time;
            released = false;
            if (cfg.throwSwing != null) Audio.SoundManager.Play(cfg.throwSwing, 0.7f);
        }

        void Release()
        {
            released = true;
            var cam = Cam;
            Vector3 dir = testAim ?? (cam != null ? cam.transform.forward : transform.forward);
            Vector3 origin = ReleasePoint(cam, dir);
            Vector3 velocity = dir * cfg.throwSpeed + Vector3.up * cfg.throwLift;
            if (mover != null) velocity += Vector3.ProjectOnPlane(mover.velocity, Vector3.up) * 0.6f;
            float fuseLeft = cfg.fuse - (Time.time - pinAt);
            int id = Net.GrenadeNet.SendThrow(origin, velocity, fuseLeft);
            GrenadeProjectile.Launch(origin, velocity, fuseLeft, authoritative: true, ownedByLocal: true, id, ownColliders);
        }

        /// Where the grenade leaves the hand: just right of and below the eye (first person) or the right hand (third
        /// person), pulled back if that spot is inside a wall.
        Vector3 ReleasePoint(Camera cam, Vector3 dir)
        {
            Vector3 eye = cam != null ? cam.transform.position : transform.position + Vector3.up * 1.6f;
            Vector3 p;
            Transform hand = body != null && body.isHuman ? body.GetBoneTransform(HumanBodyBones.RightHand) : null;
            if (view != null && view.IsThirdPerson && hand != null) p = hand.position + dir * 0.2f;
            else if (cam != null) p = eye + cam.transform.right * 0.2f - cam.transform.up * 0.12f + dir * 0.35f;
            else p = eye + dir * 0.4f;
            Vector3 from = view != null && view.IsThirdPerson ? transform.position + Vector3.up * 1.5f : eye;
            Vector3 d = p - from;
            if (Physics.SphereCast(from, 0.05f, d.normalized, out var hit, d.magnitude, ~0, QueryTriggerInteraction.Ignore) && !IsOwn(hit.collider))
                p = hit.point + hit.normal * 0.08f;
            return p;
        }

        bool IsOwn(Collider c) { foreach (var o in ownColliders) if (o == c) return true; return false; }

        /// Writes pin and blast moments to the console (play tests).
        public static bool LogEvents;

        void BlowInHand()
        {
            if (LogEvents) Debug.Log($"[Grenade] went off in the hand at {Time.time:0.00}");
            Vector3 at = Cam != null ? Cam.transform.position + Cam.transform.forward * 0.4f - Vector3.up * 0.4f : transform.position + Vector3.up;
            GrenadeProjectile.Blast(at, cfg, hurtZombies: true, hurtLocalPlayer: true);
            Net.GrenadeNet.SendHandBlast(at);
            Finish();
        }

        void Cancel()
        {
            if (state == State.Idle) return;
            Finish();
        }

        /// Play tests: cook for `cook` seconds and throw along `aim` (no input needed). Adds a grenade if there is none.
        public void TestThrow(Vector3 aim, float cook)
        {
            if (Count <= 0) Add(1);
            testAim = aim.normalized;
            testHoldUntil = Time.time + Mathf.Max(cfg.minHold, cook);
            TryPullPin();
        }

        void Finish()
        {
            state = State.Idle;
            testAim = null;
            GameInput.FireBlocked = false;
            if (arc != null) { arc.enabled = false; ring.enabled = false; }
            if (fpArms != null) fpArms.gameObject.SetActive(false);
        }

        Camera Cam => view != null && view.playerCamera != null ? view.playerCamera : Camera.main;

        // ------------------------------------------------------------------ aim arc

        void UpdateArc()
        {
            bool show = cfg.showArc && state == State.Cooking;
            if (!show) { if (arc != null) { arc.enabled = false; ring.enabled = false; } return; }
            if (arc == null) MakeArc();
            var cam = Cam;
            Vector3 dir = testAim ?? (cam != null ? cam.transform.forward : transform.forward);
            Vector3 p = ReleasePoint(cam, dir);
            Vector3 v = dir * cfg.throwSpeed + Vector3.up * cfg.throwLift;
            if (mover != null) v += Vector3.ProjectOnPlane(mover.velocity, Vector3.up) * 0.6f;
            float dt = cfg.arcSeconds / (arcPts.Length - 1);
            int n = 1; arcPts[0] = p;
            bool landed = false; Vector3 landAt = p, landN = Vector3.up;
            for (; n < arcPts.Length; n++)
            {
                Vector3 next = p + v * dt + 0.5f * Physics.gravity * dt * dt;
                v += Physics.gravity * dt;
                Vector3 seg = next - p;
                if (Physics.Raycast(p, seg.normalized, out var hit, seg.magnitude, ~0, QueryTriggerInteraction.Ignore) && !IsOwn(hit.collider))
                {
                    arcPts[n++] = hit.point; landed = true; landAt = hit.point; landN = hit.normal; break;
                }
                arcPts[n] = p = next;
            }
            arc.enabled = true;
            arc.positionCount = n;
            arc.SetPositions(arcPts);
            ring.enabled = landed;
            if (landed)
            {
                var rot = Quaternion.FromToRotation(Vector3.up, landN);
                for (int i = 0; i < ring.positionCount; i++)
                {
                    float a = i * Mathf.PI * 2f / (ring.positionCount - 1);
                    ring.SetPosition(i, landAt + rot * new Vector3(Mathf.Cos(a) * 0.45f, 0.04f, Mathf.Sin(a) * 0.45f));
                }
            }
        }

        void MakeArc()
        {
            var mat = new Material(Shader.Find("Sprites/Default"));
            LineRenderer Line(string name, int count, float width)
            {
                var go = new GameObject(name);
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true; lr.sharedMaterial = mat; lr.positionCount = count;
                lr.widthMultiplier = width; lr.numCapVertices = 2;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
                var c0 = cfg.arcColor; var c1 = cfg.arcColor; c1.a *= 0.35f;
                lr.startColor = c0; lr.endColor = c1;
                lr.enabled = false;
                return lr;
            }
            arc = Line("GrenadeArc", arcPts.Length, cfg.arcWidth);
            ring = Line("GrenadeLanding", 25, cfg.arcWidth * 1.4f);
            ring.startColor = ring.endColor = cfg.arcColor;
        }

        // ------------------------------------------------------------------ first person: the gun dips, the arm throws

        Transform fpArms, fpGrenade;

        /// Throw clip time (s) for the moment now: cooking = the raise then a looping sway; throwing = the throw
        /// stretched over cfg.throwTime. Returns false when nothing is in hand.
        bool ClipTime(AnimationClip cook, AnimationClip thr, out AnimationClip clip, out float time, out bool grenadeShown)
        {
            clip = null; time = 0f; grenadeShown = false;
            if (state == State.Cooking && cook != null)
            {
                float tc = Time.time - pinAt, raise = Mathf.Min(cfg.cookRaise, cook.length * 0.9f);
                time = tc < raise ? tc : raise + Mathf.Repeat(tc - raise, Mathf.Max(0.05f, cook.length - raise));
                clip = cook; grenadeShown = true; return true;
            }
            if (state == State.Throwing && thr != null)
            {
                float p = Mathf.Clamp01((Time.time - throwAt) / Mathf.Max(0.05f, cfg.throwTime));
                time = p * thr.length; clip = thr; grenadeShown = p < cfg.releaseAt; return true;
            }
            return false;
        }

        void UpdateFirstPerson()
        {
            bool fp = view != null && !view.IsThirdPerson;
            if (overlayCam == null && Cam != null) overlayCam = Cam.transform.Find("FirstPersonViewmodelCamera");
            Transform parent = overlayCam != null ? overlayCam : (Cam != null ? Cam.transform : null);
            bool any = ClipTime(cfg.fpCook, cfg.fpThrow, out var clip, out var time, out var shown);
            if (!fp || cfg.fpArms == null || parent == null || !any) { if (fpArms != null) fpArms.gameObject.SetActive(false); return; }
            if (fpArms == null)
            {
                fpArms = Instantiate(cfg.fpArms, parent, false).transform;
                fpArms.name = "GrenadeArm_FP";
                int layer = LayerMask.NameToLayer(FirstPersonViewmodel.LayerName);
                foreach (var t in fpArms.GetComponentsInChildren<Transform>(true))
                {
                    if (layer >= 0) t.gameObject.layer = layer;
                    if (t.name == "FPGrenade") fpGrenade = t;
                }
                foreach (var r in fpArms.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
                foreach (var r in fpArms.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.updateWhenOffscreen = true;
            }
            if (fpArms.parent != parent) fpArms.SetParent(parent, false);
            fpArms.localPosition = Vector3.zero; fpArms.localRotation = Quaternion.identity;
            fpArms.gameObject.SetActive(true);
            ClipSampler.Sample(fpArms.gameObject, clip, time);
            if (fpGrenade != null) fpGrenade.gameObject.SetActive(shown);
        }

        // ------------------------------------------------------------------ third person: the throwing arm

        /// A teammate threw: play the arm swing on this copy.
        public void PlayRemoteThrow() => remoteThrowAt = Time.time;

        float dipNow;

        /// The gun dips out of view while a grenade is in hand: the viewmodel camera (posed by BodyGunPose every
        /// frame, just before this) turns up, so everything it draws drops down the screen.
        void ApplyGunDip()
        {
            if (remote || cfg == null) return;
            bool fp = view != null && !view.IsThirdPerson;
            dipNow = Mathf.MoveTowards(dipNow, state != State.Idle && fp ? 1f : 0f, Time.deltaTime / 0.12f);
            if (overlayCam == null && Cam != null) overlayCam = Cam.transform.Find("FirstPersonViewmodelCamera");
            if (overlayCam == null || dipNow <= 0f) return;
            float k = dipNow * dipNow * (3f - 2f * dipNow);
            overlayCam.rotation = overlayCam.rotation * Quaternion.Euler(-cfg.fpGunDip * k, -6f * k, 0f);
        }

        void LateUpdate()
        {
            ApplyGunDip();
            ThirdPersonArm();
        }

        // the clip proxy: an invisible copy of the skeleton the throw clips are sampled onto; the right arm, hand and
        // fingers are copied from it onto the body, the chest's turn added as a change from the rest pose
        GameObject proxy;
        System.Collections.Generic.Dictionary<string, Transform> proxyBones, bodyBones;
        System.Collections.Generic.Dictionary<string, Quaternion> proxyRest;
        Transform proxyGrenade, proxyHand;
        float tpWeight;
        AnimationClip lastTpClip; float lastTpTime;

        bool MakeProxy()
        {
            if (proxy != null) return true;
            if (cfg.tpProxy == null || body == null) return false;
            proxy = Instantiate(cfg.tpProxy);
            proxy.name = "GrenadeClipProxy"; proxy.hideFlags = HideFlags.HideInHierarchy;
            foreach (var r in proxy.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            if (!proxy.TryGetComponent(out Animator anim)) anim = proxy.AddComponent<Animator>();
            anim.enabled = false;
            proxyBones = new System.Collections.Generic.Dictionary<string, Transform>();
            foreach (var t in proxy.GetComponentsInChildren<Transform>(true)) if (!proxyBones.ContainsKey(t.name)) proxyBones[t.name] = t;
            bodyBones = new System.Collections.Generic.Dictionary<string, Transform>();
            foreach (var t in body.GetComponentsInChildren<Transform>(true)) if (!bodyBones.ContainsKey(t.name)) bodyBones[t.name] = t;
            proxyBones.TryGetValue("TPGrenade", out proxyGrenade);
            proxyBones.TryGetValue("RightHand", out proxyHand);
            proxyRest = new System.Collections.Generic.Dictionary<string, Quaternion>();
            if (cfg.tpRest != null) ClipSampler.Sample(proxy, cfg.tpRest, 0f);
            if (cfg.tpDeltaBones != null)
                foreach (var n in cfg.tpDeltaBones) if (proxyBones.TryGetValue(n, out var t)) proxyRest[n] = t.localRotation;
            return true;
        }

        void ThirdPersonArm()
        {
            if (cfg == null || body == null) return;
            // what plays: the local player's own state (third person only), or a teammate's throw on its copy
            AnimationClip clip = null; float time = 0f; bool shown = false, on;
            if (remote)
            {
                float p = (Time.time - remoteThrowAt) / Mathf.Max(0.05f, cfg.throwTime);
                on = p >= 0f && p <= 1f;
                if (on && cfg.tpThrow != null) { clip = cfg.tpThrow; time = Mathf.Clamp01(p) * clip.length; shown = p < cfg.releaseAt; }
            }
            else on = view != null && view.IsThirdPerson && ClipTime(cfg.tpCook, cfg.tpThrow, out clip, out time, out shown);
            tpWeight = Mathf.MoveTowards(tpWeight, on ? 1f : 0f, Time.deltaTime / 0.12f);
            if (tpWeight <= 0f || !MakeProxy()) { ShowTpGrenade(false); return; }
            if (clip != null) { ClipSampler.Sample(proxy, clip, time); lastTpClip = clip; lastTpTime = time; }
            else if (lastTpClip != null) ClipSampler.Sample(proxy, lastTpClip, lastTpTime);   // fading out: hold the last pose
            float w = tpWeight * tpWeight * (3f - 2f * tpWeight);
            if (cfg.tpCopyBones != null)
                foreach (var n in cfg.tpCopyBones)
                    if (bodyBones.TryGetValue(n, out var b) && proxyBones.TryGetValue(n, out var s))
                        b.localRotation = Quaternion.Slerp(b.localRotation, s.localRotation, w);
            if (cfg.tpDeltaBones != null)
                foreach (var n in cfg.tpDeltaBones)
                    if (bodyBones.TryGetValue(n, out var b) && proxyBones.TryGetValue(n, out var s) && proxyRest.TryGetValue(n, out var r))
                        b.localRotation = b.localRotation * Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(r) * s.localRotation, w);
            ShowTpGrenade(shown && on);
            if (tpHeld != null && tpHeld.gameObject.activeSelf && proxyGrenade != null && proxyHand != null && bodyBones.TryGetValue("RightHand", out var hand))
            {
                // the grenade where the clip holds it: its place in the copy's hand, on the body's hand
                var rel = proxyHand.worldToLocalMatrix * proxyGrenade.localToWorldMatrix;
                var m = hand.localToWorldMatrix * rel;
                tpHeld.SetPositionAndRotation(m.GetColumn(3), m.rotation);
            }
        }

        void ShowTpGrenade(bool show)
        {
            if (!show) { if (tpHeld != null) tpHeld.gameObject.SetActive(false); return; }
            if (tpHeld == null && cfg.heldPrefab != null)
            {
                tpHeld = Instantiate(cfg.heldPrefab).transform;
                tpHeld.name = "GrenadeInHand_TP";
                foreach (var t in tpHeld.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
                foreach (var r in tpHeld.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            if (tpHeld != null) tpHeld.gameObject.SetActive(true);
        }

        void OnDisable() { if (proxy != null) Destroy(proxy); if (tpHeld != null) tpHeld.gameObject.SetActive(false); }
    }
}
