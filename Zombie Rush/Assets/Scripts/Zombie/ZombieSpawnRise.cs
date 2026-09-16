using System.Collections;
using UnityEngine;

namespace ZombieRush.Zombie
{
    /// Makes a freshly-spawned zombie climb up out of the ground instead of just
    /// popping into existence. AI/collision are held off until it's fully risen.
    public class ZombieSpawnRise : MonoBehaviour
    {
        public float riseHeight = 2.2f;
        public float riseDuration = 1f;

        CharacterController controller;
        ZombieAI ai;

        public void Init(Vector3 groundPosition)
        {
            controller = GetComponent<CharacterController>();
            ai = GetComponent<ZombieAI>();

            if (controller != null) controller.enabled = false;
            if (ai != null) ai.enabled = false;

            transform.position = groundPosition - Vector3.up * riseHeight;
            StartCoroutine(Rise(groundPosition));
        }

        IEnumerator Rise(Vector3 targetPos)
        {
            Vector3 start = transform.position;
            float t = 0f;
            while (t < riseDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / riseDuration);
                transform.position = Vector3.Lerp(start, targetPos, k);
                yield return null;
            }
            transform.position = targetPos;

            if (controller != null) controller.enabled = true;
            if (ai != null) ai.enabled = true;
            Destroy(this);
        }
    }
}
