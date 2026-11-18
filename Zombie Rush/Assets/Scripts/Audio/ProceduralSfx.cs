using UnityEngine;

namespace ZombieRush.Audio
{
    /// Short feedback sounds made in code when first needed: no audio files, so the game's download size
    /// does not grow. A crisp tick for a hit, a wet crack for a headshot, a low thud for a kill.
    public static class ProceduralSfx
    {
        const int Rate = 22050;
        static AudioClip hitTick, headshot, killThud;

        public static AudioClip HitTick => hitTick != null ? hitTick : hitTick = Make("ZR_HitTick", 0.05f, HitTickSample);
        public static AudioClip Headshot => headshot != null ? headshot : headshot = Make("ZR_Headshot", 0.13f, HeadshotSample);
        public static AudioClip KillThud => killThud != null ? killThud : killThud = Make("ZR_KillThud", 0.22f, KillSample);
        static AudioClip rotor;
        /// A seamless 2 s helicopter loop: blade "whop" pulses (21 per loop, about 10.5 a second for four blades),
        /// a low engine hum and a faint turbine whine. The end is cross-faded into the start so it loops cleanly.
        public static AudioClip RotorLoop
        {
            get
            {
                if (rotor != null) return rotor;
                const float seconds = 2f;
                int n = Mathf.CeilToInt(seconds * Rate), fade = Rate / 10;
                var data = new float[n + fade];
                float lp = 0f, lp2 = 0f; uint seed = 0x2545F491u;
                const float pulses = 21f / seconds;
                for (int i = 0; i < data.Length; i++)
                {
                    float t = i / (float)Rate;
                    float ph = t * pulses % 1f;
                    float env = Mathf.Exp(-ph * 9f) * (ph < 0.02f ? ph / 0.02f : 1f);      // sharp attack, quick decay
                    lp += (Noise(ref seed) - lp) * 0.06f;                                    // dark, chopped air
                    lp2 += (lp - lp2) * 0.3f;
                    float whop = lp2 * env * 3.2f + Mathf.Sin(2f * Mathf.PI * 52f * t) * env * 0.5f;
                    float hum = Mathf.Sin(2f * Mathf.PI * 74f * t) * 0.12f + Mathf.Sin(2f * Mathf.PI * 148f * t) * 0.05f;
                    float whine = Mathf.Sin(2f * Mathf.PI * 1180f * t) * 0.025f;
                    data[i] = whop + hum + whine;
                }
                // fold the extra tail over the start: the loop point is seamless
                for (int i = 0; i < fade; i++)
                {
                    float k = i / (float)fade;
                    data[i] = data[i] * k + data[n + i] * (1f - k);
                }
                var loop = new float[n];
                float peak = 0f;
                for (int i = 0; i < n; i++) { loop[i] = data[i]; peak = Mathf.Max(peak, Mathf.Abs(loop[i])); }
                for (int i = 0; i < n; i++) loop[i] *= 0.85f / Mathf.Max(1e-4f, peak);
                rotor = AudioClip.Create("ZR_RotorLoop", n, 1, Rate, false);
                rotor.SetData(loop, 0);
                return rotor;
            }
        }

        static AudioClip streak;
        public static AudioClip Streak => streak != null ? streak : streak = Make("ZR_Streak", 0.75f, StreakSample);

        // a short rising whoosh into a deep, slightly driven sub hit (the cinematic "boom" under a call-out)
        static float StreakSample(float t, ref float state, ref uint seed)
        {
            const float hit = 0.12f;
            state += (Noise(ref seed) - state) * (t < hit ? 0.05f + t : 0.12f);   // the whoosh opens up as it rises
            if (t < hit) return state * (t / hit) * (t / hit) * 1.2f;
            float u = t - hit;
            const float glide = 0.25f, k = (90f - 38f) / glide;
            float cycles = u < glide ? 90f * u - 0.5f * k * u * u : 90f * glide - 0.5f * k * glide * glide + 38f * (u - glide);
            float sub = Mathf.Sin(2f * Mathf.PI * cycles) * Mathf.Exp(-u * 4f);
            float crack = state * Mathf.Exp(-u * 18f) * 1.5f;
            float x = (sub * 1.4f + crack);
            return x / (1f + Mathf.Abs(x));   // soft drive: thicker, never harsh
        }

        delegate float Sample(float t, ref float state, ref uint seed);

        static AudioClip Make(string name, float seconds, Sample sample)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            float state = 0f, peak = 0f;
            uint seed = 0x9E3779B9u;
            for (int i = 0; i < n; i++)
            {
                data[i] = sample(i / (float)Rate, ref state, ref seed);
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            }
            float gain = peak > 0f ? 0.9f / peak : 1f;
            // a few samples of fade at both ends: no click from a cut-off wave
            for (int i = 0; i < n; i++)
            {
                float edge = Mathf.Min(1f, Mathf.Min(i, n - 1 - i) / 40f);
                data[i] *= gain * edge;
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Noise(ref uint seed)
        {
            seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
            return (seed & 0xFFFFFF) / (float)0x7FFFFF - 1f;
        }

        // two bright tones with a very fast decay plus a breath of noise: the classic hit marker "tick"
        static float HitTickSample(float t, ref float state, ref uint seed)
        {
            float env = Mathf.Exp(-t * 95f);
            return env * (Mathf.Sin(2f * Mathf.PI * 2900f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 1750f * t) * 0.4f)
                 + Noise(ref seed) * Mathf.Exp(-t * 260f) * 0.35f;
        }

        // a sharp click, a muffled knock and a wet burst of low-passed noise
        static float HeadshotSample(float t, ref float state, ref uint seed)
        {
            state += (Noise(ref seed) - state) * 0.18f;   // one-pole low-pass of the noise
            float click = t < 0.012f ? Mathf.Sin(2f * Mathf.PI * 3400f * t) * (1f - t / 0.012f) : 0f;
            float knock = Mathf.Sin(2f * Mathf.PI * 720f * t) * Mathf.Exp(-t * 45f);
            float wet = state * Mathf.Exp(-t * 28f) * 2.2f;
            return click * 0.7f + knock * 0.6f + wet;
        }

        // a low sine falling in pitch with a soft noise body
        static float KillSample(float t, ref float state, ref uint seed)
        {
            state += (Noise(ref seed) - state) * 0.08f;
            // pitch glides 150 -> 52 Hz over 0.16 s; the phase is the integral of that glide
            const float glide = 0.16f, k = (150f - 52f) / glide;
            float cycles = t < glide ? 150f * t - 0.5f * k * t * t
                                     : 150f * glide - 0.5f * k * glide * glide + 52f * (t - glide);
            float body = Mathf.Sin(2f * Mathf.PI * cycles) * Mathf.Exp(-t * 13f);
            return body + state * Mathf.Exp(-t * 35f) * 1.6f;
        }
    }
}
