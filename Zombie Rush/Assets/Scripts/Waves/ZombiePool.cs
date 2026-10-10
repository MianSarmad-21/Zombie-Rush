using System.Collections.Generic;
using UnityEngine;
using ZombieRush.Zombie;

namespace ZombieRush.Waves
{
    /// Reuses zombie instances instead of Instantiate/Destroy every wave. Spawning
    /// a skinned, animated character is one of the most expensive things that
    /// happens mid-fight on a phone, and waves spawn several at once.
    public class ZombiePool
    {
        readonly Dictionary<GameObject, Stack<ZombieHealth>> free = new Dictionary<GameObject, Stack<ZombieHealth>>();
        readonly Dictionary<ZombieHealth, GameObject> prefabOf = new Dictionary<ZombieHealth, GameObject>();
        readonly System.Action<ZombieHealth> onDied;

        public ZombiePool(System.Action<ZombieHealth> onDied) => this.onDied = onDied;

        /// Returns an active zombie at the given pose, or null if the prefab has no ZombieHealth.
        public ZombieHealth Get(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            var stack = GetStack(prefab);
            while (stack.Count > 0)
            {
                var pooled = stack.Pop();
                if (pooled == null) continue; // destroyed externally
                pooled.transform.SetPositionAndRotation(position, rotation);
                pooled.gameObject.SetActive(true);
                return pooled;
            }
            return Create(prefab, position, rotation, active: true);
        }

        /// Creates inactive instances ahead of time so the first wave doesn't hitch.
        public void Prewarm(GameObject prefab, int count)
        {
            var stack = GetStack(prefab);
            for (int i = stack.Count; i < count; i++)
            {
                var z = Create(prefab, Vector3.down * 500f, Quaternion.identity, active: false);
                if (z == null) return;
                stack.Push(z);
            }
        }

        public int FreeCount(GameObject prefab) => free.TryGetValue(prefab, out var s) ? s.Count : 0;

        ZombieHealth Create(GameObject prefab, Vector3 position, Quaternion rotation, bool active)
        {
            var go = Object.Instantiate(prefab, position, rotation);
            var zombie = go.GetComponent<ZombieHealth>();
            if (zombie == null)
            {
                Debug.LogError($"ZombiePool: prefab '{prefab.name}' has no ZombieHealth component.", prefab);
                Object.Destroy(go);
                return null;
            }

            if (go.GetComponent<ZombieSpawnRise>() == null) go.AddComponent<ZombieSpawnRise>();
            zombie.Died += onDied;
            zombie.ReleaseToPool = Release;
            prefabOf[zombie] = prefab;
            if (!active) go.SetActive(false);
            return zombie;
        }

        void Release(ZombieHealth zombie)
        {
            if (zombie == null) return;
            zombie.gameObject.SetActive(false);
            if (prefabOf.TryGetValue(zombie, out var prefab)) GetStack(prefab).Push(zombie);
            else Object.Destroy(zombie.gameObject);
        }

        Stack<ZombieHealth> GetStack(GameObject prefab)
        {
            if (!free.TryGetValue(prefab, out var stack))
            {
                stack = new Stack<ZombieHealth>();
                free[prefab] = stack;
            }
            return stack;
        }
    }
}
