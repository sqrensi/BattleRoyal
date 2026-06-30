using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShooterPrototype.Player
{
    public static class WeaponBlockUtility
    {
        private static readonly HashSet<int> ProcessedSceneHandles = new HashSet<int>();
        private static readonly HashSet<int> ProcessedBotOccluderSceneHandles = new HashSet<int>();

        public static int EnsureSceneWeaponBlocks(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || !WeaponBlockLayers.IsConfigured)
            {
                return 0;
            }

            if (!ProcessedSceneHandles.Add(scene.handle))
            {
                return 0;
            }

            var updated = 0;
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                updated += EnsureHierarchyWeaponBlocks(roots[i].transform);
            }

            if (updated > 0)
            {
                Debug.Log($"[WeaponBlockUtility] Promoted {updated} primitive collider(s) to WeaponBlock in scene '{scene.name}'.");
            }

            return updated;
        }

        public static int EnsureHierarchyWeaponBlocks(Transform root)
        {
            if (root == null)
            {
                return 0;
            }

            var updated = 0;
            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                updated += EnsureColliderWeaponBlock(colliders[i]);
            }

            return updated;
        }

        public static int EnsureColliderWeaponBlock(Collider collider)
        {
            if (collider == null || !collider.enabled || collider.isTrigger)
            {
                return 0;
            }

            if (ShouldSkipWeaponBlockSetup(collider.gameObject))
            {
                return 0;
            }

            if (collider is BoxCollider or CapsuleCollider or SphereCollider)
            {
                return PromotePrimitiveCollider(collider);
            }

            return 0;
        }

        public static int EnsureSceneBotOccluderProxies(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return 0;
            }

            if (!ProcessedBotOccluderSceneHandles.Add(scene.handle))
            {
                return 0;
            }

            var created = 0;
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                created += EnsureHierarchyBotOccluderProxies(roots[i].transform);
            }

            if (created > 0)
            {
                Debug.Log($"[WeaponBlockUtility] Created {created} bot occluder proxy collider(s) in scene '{scene.name}'.");
            }

            return created;
        }

        public static int EnsureHierarchyBotOccluderProxies(Transform root)
        {
            if (root == null)
            {
                return 0;
            }

            var created = 0;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                created += EnsureRendererBotOccluderProxy(renderers[i]);
            }

            return created;
        }

        private static int EnsureRendererBotOccluderProxy(Renderer renderer)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                return 0;
            }

            if (!IsEnvironmentOccluder(renderer))
            {
                return 0;
            }

            if (ShouldSkipWeaponBlockSetup(renderer.gameObject))
            {
                return 0;
            }

            if (renderer.transform.Find("BotOccluderProxy") != null)
            {
                return 0;
            }

            var proxyObject = new GameObject("BotOccluderProxy");
            proxyObject.transform.SetParent(renderer.transform, false);
            proxyObject.transform.localPosition = Vector3.zero;
            proxyObject.transform.localRotation = Quaternion.identity;
            proxyObject.transform.localScale = Vector3.one;

            var bounds = renderer.localBounds;
            var box = proxyObject.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
            // Trigger-only: bot LOS raycasts still hit; CharacterController ignores triggers.
            box.isTrigger = true;

            if (WeaponBlockLayers.IsConfigured)
            {
                proxyObject.layer = WeaponBlockLayers.LayerIndex;
            }

            return 1;
        }

        private static bool IsEnvironmentOccluder(Renderer renderer)
        {
            if (renderer == null)
            {
                return false;
            }

            if (LooksLikeEnvironmentWall(renderer))
            {
                return true;
            }

            var current = renderer.transform;
            while (current != null)
            {
                if (string.Equals(current.name, "env", System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static bool LooksLikeEnvironmentWall(Renderer renderer)
        {
            var name = renderer.name;
            return name.IndexOf("Wall", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Pillar", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Column", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static int RemoveAllWeaponBlockProxiesFromScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return 0;
            }

            var removed = 0;
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                removed += RemoveWeaponBlockProxiesInHierarchy(roots[i].transform);
            }

            if (removed > 0)
            {
                Debug.Log($"[WeaponBlockUtility] Removed {removed} WeaponBlockProxy object(s) from scene '{scene.name}'.");
            }

            return removed;
        }

        private static int RemoveWeaponBlockProxiesInHierarchy(Transform root)
        {
            if (root == null)
            {
                return 0;
            }

            var removed = 0;
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = transforms.Length - 1; i >= 0; i--)
            {
                var transform = transforms[i];
                if (transform == null || transform.name != "WeaponBlockProxy")
                {
                    continue;
                }

#if UNITY_EDITOR
                Object.DestroyImmediate(transform.gameObject);
#else
                Object.Destroy(transform.gameObject);
#endif
                removed++;
            }

            return removed;
        }

        private static int PromotePrimitiveCollider(Collider collider)
        {
            if (collider.gameObject.layer == WeaponBlockLayers.LayerIndex)
            {
                return 0;
            }

            collider.gameObject.layer = WeaponBlockLayers.LayerIndex;
            return 1;
        }

        private static bool ShouldSkipWeaponBlockSetup(GameObject target)
        {
            if (target == null)
            {
                return true;
            }

            if (target.layer == LayerMask.NameToLayer("UI") ||
                target.layer == LayerMask.NameToLayer("Ignore Raycast"))
            {
                return true;
            }

            if (target.GetComponentInParent<CharacterController>(true) != null)
            {
                return true;
            }

            if (target.GetComponentInParent<FpsCharacterController>(true) != null ||
                target.GetComponentInParent<PlayerWeaponMount>(true) != null ||
                target.GetComponentInParent<PlayerBoneHitboxRig>(true) != null)
            {
                return true;
            }

            if (target.GetComponentInParent<PickupSpawnZone>(true) != null ||
                target.GetComponent<TrainingPickupZoneMarker>() != null)
            {
                return true;
            }

            var root = target.transform.root;
            if (root != null)
            {
                var rootName = root.name;
                if (rootName.IndexOf("Player", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    rootName.IndexOf("Remote_", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        public static int RemoveSceneBotOccluderProxies(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return 0;
            }

            var removed = 0;
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                removed += RemoveBotOccluderProxiesInHierarchy(roots[i].transform);
            }

            if (removed > 0)
            {
                Debug.Log($"[WeaponBlockUtility] Removed {removed} bot occluder proxy collider(s) from scene '{scene.name}'.");
            }

            ProcessedBotOccluderSceneHandles.Remove(scene.handle);
            return removed;
        }

        private static int RemoveBotOccluderProxiesInHierarchy(Transform root)
        {
            if (root == null)
            {
                return 0;
            }

            var removed = 0;
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = transforms.Length - 1; i >= 0; i--)
            {
                var transform = transforms[i];
                if (transform == null || transform.name != "BotOccluderProxy")
                {
                    continue;
                }

#if UNITY_EDITOR
                Object.DestroyImmediate(transform.gameObject);
#else
                Object.Destroy(transform.gameObject);
#endif
                removed++;
            }

            return removed;
        }

        public static void ResetProcessedScenesForTests()
        {
            ProcessedSceneHandles.Clear();
            ProcessedBotOccluderSceneHandles.Clear();
        }
    }
}
