using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Lightweight prefab pool for short-lived gameplay VFX (muzzle flash, impacts).
    /// </summary>
    public static class GameplayVfxPool
    {
        private const int MaxPoolSizePerPrefab = 12;

        private static Transform poolRoot;
        private static readonly Dictionary<int, Stack<GameObject>> PoolsByPrefabId = new Dictionary<int, Stack<GameObject>>(8);

        public static bool TrySpawn(
            MonoBehaviour host,
            GameObject prefab,
            Vector3 position,
            Quaternion rotation,
            float lifetimeSeconds)
        {
            if (host == null || prefab == null)
            {
                return false;
            }

            var instance = Acquire(prefab);
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.SetActive(true);
            PlayParticleSystems(instance);

            if (lifetimeSeconds > 0f)
            {
                host.StartCoroutine(ReleaseAfterSeconds(instance, prefab, lifetimeSeconds));
            }

            return true;
        }

        private static GameObject Acquire(GameObject prefab)
        {
            EnsurePoolRoot();
            var prefabId = prefab.GetInstanceID();
            if (PoolsByPrefabId.TryGetValue(prefabId, out var stack))
            {
                while (stack.Count > 0)
                {
                    var pooled = stack.Pop();
                    if (pooled != null)
                    {
                        return pooled;
                    }
                }
            }

            var instance = Object.Instantiate(prefab, poolRoot);
            instance.name = prefab.name;
            return instance;
        }

        private static IEnumerator ReleaseAfterSeconds(GameObject instance, GameObject prefab, float lifetimeSeconds)
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0.01f, lifetimeSeconds));
            Release(instance, prefab);
        }

        private static void Release(GameObject instance, GameObject prefab)
        {
            if (instance == null || prefab == null)
            {
                return;
            }

            EnsurePoolRoot();
            instance.SetActive(false);
            instance.transform.SetParent(poolRoot, false);

            var prefabId = prefab.GetInstanceID();
            if (!PoolsByPrefabId.TryGetValue(prefabId, out var stack))
            {
                stack = new Stack<GameObject>(4);
                PoolsByPrefabId[prefabId] = stack;
            }

            if (stack.Count < MaxPoolSizePerPrefab)
            {
                stack.Push(instance);
                return;
            }

            Object.Destroy(instance);
        }

        private static void PlayParticleSystems(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var systems = root.GetComponentsInChildren<ParticleSystem>(true);
            for (var i = 0; i < systems.Length; i++)
            {
                var ps = systems[i];
                if (ps == null)
                {
                    continue;
                }

                ps.Clear(true);
                ps.Play(true);
            }
        }

        private static void EnsurePoolRoot()
        {
            if (poolRoot != null)
            {
                return;
            }

            var rootObject = new GameObject("GameplayVfxPool");
            Object.DontDestroyOnLoad(rootObject);
            poolRoot = rootObject.transform;
        }
    }
}
