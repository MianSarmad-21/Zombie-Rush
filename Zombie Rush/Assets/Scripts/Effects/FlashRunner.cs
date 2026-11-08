using System.Collections;
using UnityEngine;

namespace ZombieRush.Effects
{
    /// Sits on a pooled effect: fades its light flash and says when the effect has finished.
    public class FlashRunner : MonoBehaviour
    {
        float busyUntil;
        public bool Busy => Time.time < busyUntil;

        public void Hold(float seconds) => busyUntil = Time.time + seconds;

        public void Flash(Light light, float radius, float intensity = 9f, float fade = 0.35f, float busy = 2.2f)
        {
            busyUntil = Time.time + busy;
            StopAllCoroutines();
            StartCoroutine(Run(light, radius, intensity, fade));
        }

        IEnumerator Run(Light light, float radius, float intensity, float fade)
        {
            light.enabled = true;
            light.range = radius * 2.5f;
            for (float t = 0f; t < fade; t += Time.deltaTime)
            {
                light.intensity = Mathf.Lerp(intensity, 0f, t / fade);
                yield return null;
            }
            light.enabled = false;
        }
    }
}
