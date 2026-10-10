using System.Collections;
using UnityEngine;

namespace ZombieRush.Zombie
{
    /// Makes a freshly-spawned zombie climb up out of the ground instead of just
    /// popping into existence. AI/collision are held off until it's fully risen.
    /// Stays on the zombie so pooled zombies can rise again on reuse.
    [DisallowMultipleComponent]
    public class ZombieSpawnRise : MonoBehaviour
    {
        public float riseHeight = 2.2f;
        public float riseDuration = 1f;

        CharacterController controller;
        ZombieAI ai;
        ZombieHealth health;
        Coroutine routine;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            ai = GetComponent<ZombieAI>();
            health = GetComponent<ZombieHealth>();
        }

        public void Begin(Vector3 groundPosition)
        {
            if (controller != null) controller.enabled = false;
            if (ai != null) ai.enabled = false;

            transform.position = groundPosition - Vector3.up * riseHeight;
            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(Rise(groundPosition));
        }

        IEnumerator Rise(Vector3 targetPos)
        {
            Vector3 start = transform.position;
            float t = 0f;
            while (t < riseDuration)
            {
                // Shot dead on the way up: stay down, ZombieHealth handles the corpse.
                if (health != null && health.IsDead) { routine = null; yield break; }

                t += Time.deltaTime;
                transform.position = Vector3.Lerp(start, targetPos, Mathf.Clamp01(t / riseDuration));
                yield return null;
            }
            transform.position = targetPos;
            routine = null;

            if (health != null && health.IsDead) yield break;
            if (controller != null) controller.enabled = true;
            if (ai != null) ai.enabled = true;
        }
    }
}
