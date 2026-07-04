using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Lightweight prefab pool for short-lived gameplay VFX (muzzle flash, impacts).
    /// Coroutines run on a persistent host so VFX always return to the pool.
    /// </summary>
    public static class GameplayVfxPool
    {
        private const int MaxPoolSizePerPrefab = 12;
        private const int MaxActiveInstances = 32;

        private static GameplayVfxPoolRunner runner;
        private static Transform poolRoot;
        private static int activeInstances;
        private static readonly Dictionary<int, Stack<GameObject>> PoolsByPrefabId = new Dictionary<int, Stack<GameObject>>(8);

        public static bool TrySpawn(
            GameObject prefab,
            Vector3 position,
            Quaternion rotation,
            float lifetimeSeconds)
        {
            if (prefab == null)
            {
                return false;
            }

            if (activeInstances >= MaxActiveInstances)
            {
                return false;
            }

            EnsureRunner();
            var instance = Acquire(prefab);
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.SetActive(true);
            activeInstances++;
            PlayParticleSystems(instance);

            if (lifetimeSeconds > 0f)
            {
                runner.StartCoroutine(ReleaseAfterSeconds(instance, prefab, lifetimeSeconds));
            }

            return true;
        }

        private static GameObject Acquire(GameObject prefab)
        {
            EnsureRunner();
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
            EnsureInstanceCache(instance);
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

            EnsureRunner();
            instance.SetActive(false);
            activeInstances = Mathf.Max(0, activeInstances - 1);
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

            var cache = EnsureInstanceCache(root);
            var systems = cache.Systems;
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

        private static VfxPoolInstanceCache EnsureInstanceCache(GameObject root)
        {
            if (!root.TryGetComponent<VfxPoolInstanceCache>(out var cache))
            {
                cache = root.AddComponent<VfxPoolInstanceCache>();
            }

            cache.EnsureCached();
            return cache;
        }

        private static void EnsureRunner()
        {
            if (runner != null)
            {
                return;
            }

            var rootObject = new GameObject("GameplayVfxPool");
            Object.DontDestroyOnLoad(rootObject);
            poolRoot = rootObject.transform;
            runner = rootObject.AddComponent<GameplayVfxPoolRunner>();
        }

        /// <summary>
        /// Destroys pooled VFX and stops pending releases. Call when leaving a match.
        /// </summary>
        public static void ResetSession()
        {
            if (runner != null)
            {
                runner.StopAllCoroutines();
            }

            activeInstances = 0;

            if (poolRoot != null)
            {
                for (var i = poolRoot.childCount - 1; i >= 0; i--)
                {
                    var child = poolRoot.GetChild(i);
                    if (child != null)
                    {
                        Object.Destroy(child.gameObject);
                    }
                }
            }

            PoolsByPrefabId.Clear();
        }

        private sealed class VfxPoolInstanceCache : MonoBehaviour
        {
            public ParticleSystem[] Systems;

            public void EnsureCached()
            {
                if (Systems != null && Systems.Length > 0)
                {
                    return;
                }

                Systems = GetComponentsInChildren<ParticleSystem>(true);
            }
        }

        private sealed class GameplayVfxPoolRunner : MonoBehaviour
        {
        }
    }
}
