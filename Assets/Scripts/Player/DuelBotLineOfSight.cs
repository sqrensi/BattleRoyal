using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShooterPrototype.Player
{
    public static class DuelBotLineOfSight
    {
        private const float EyeHeight = 1.35f;
        private const float BlockMargin = 0.12f;

        private static readonly float[] AimHeights = { 1.62f, 1.2f, 0.9f };
        private static Collider[] sceneOccluders = Array.Empty<Collider>();
        private static int cachedSceneHandle = int.MinValue;

        public static void PrepareScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            WeaponBlockUtility.EnsureSceneWeaponBlocks(scene);
            RebuildOccluders(scene);
        }

        public static void RebuildOccluders(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                sceneOccluders = Array.Empty<Collider>();
                cachedSceneHandle = int.MinValue;
                return;
            }

            cachedSceneHandle = scene.handle;
            var occluders = new List<Collider>(512);
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                CollectOccluders(roots[i].transform, occluders);
            }

            sceneOccluders = occluders.ToArray();
        }

        public static bool CanSeeTarget(Transform viewer, Transform target)
        {
            if (viewer == null || target == null)
            {
                return false;
            }

            EnsureOccludersForScene(viewer.gameObject.scene);

            var origin = viewer.position + Vector3.up * EyeHeight;
            for (var i = 0; i < AimHeights.Length; i++)
            {
                var aimPoint = target.position + Vector3.up * AimHeights[i];
                if (HasClearLine(origin, aimPoint, viewer, target))
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsureOccludersForScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            if (cachedSceneHandle != scene.handle || sceneOccluders.Length == 0)
            {
                PrepareScene(scene);
            }
        }

        private static void CollectOccluders(Transform root, List<Collider> occluders)
        {
            if (root == null)
            {
                return;
            }

            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider == null || !collider.enabled || collider.isTrigger)
                {
                    continue;
                }

                if (IsPlayerOrBotHierarchy(collider.transform))
                {
                    continue;
                }

                occluders.Add(collider);
            }
        }

        private static bool HasClearLine(Vector3 origin, Vector3 targetPoint, Transform viewer, Transform target)
        {
            var delta = targetPoint - origin;
            var distance = delta.magnitude;
            if (distance <= 0.05f)
            {
                return true;
            }

            var direction = delta / distance;
            var maxBlockDistance = distance - BlockMargin;
            if (maxBlockDistance <= 0.05f)
            {
                return true;
            }

            if (sceneOccluders.Length > 0 && IsBlockedByOccluders(origin, direction, maxBlockDistance, viewer, target))
            {
                return false;
            }

            var solidMask = ResolveSolidMask();
            if (Physics.Raycast(
                    origin,
                    direction,
                    out var physicsHit,
                    maxBlockDistance,
                    solidMask,
                    QueryTriggerInteraction.Ignore))
            {
                if (!BelongsToTransform(physicsHit.collider != null ? physicsHit.collider.transform : null, target))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsBlockedByOccluders(
            Vector3 origin,
            Vector3 direction,
            float maxDistance,
            Transform viewer,
            Transform target)
        {
            if (sceneOccluders.Length <= 0)
            {
                return false;
            }

            var ray = new Ray(origin, direction);
            for (var i = 0; i < sceneOccluders.Length; i++)
            {
                var collider = sceneOccluders[i];
                if (collider == null || !collider.enabled)
                {
                    continue;
                }

                if (BelongsToTransform(collider.transform, viewer) ||
                    BelongsToTransform(collider.transform, target))
                {
                    continue;
                }

                if (!collider.Raycast(ray, out var hit, maxDistance))
                {
                    continue;
                }

                if (hit.distance < maxDistance - 0.02f)
                {
                    return true;
                }
            }

            return false;
        }

        private static int ResolveSolidMask()
        {
            var mask = PickupGroundLayers.EnvironmentMask.value;
            if (WeaponBlockLayers.IsConfigured)
            {
                mask |= WeaponBlockLayers.Mask;
            }

            return mask != 0 ? mask : Physics.DefaultRaycastLayers;
        }

        private static bool BelongsToTransform(Transform colliderTransform, Transform owner)
        {
            if (colliderTransform == null || owner == null)
            {
                return false;
            }

            return colliderTransform == owner || colliderTransform.IsChildOf(owner);
        }

        private static bool IsPlayerOrBotHierarchy(Transform transform)
        {
            if (transform == null)
            {
                return true;
            }

            if (transform.GetComponentInParent<PlayerHealth>() != null ||
                transform.GetComponentInParent<PlayerBoneHitboxRig>() != null ||
                transform.GetComponentInParent<FpsCharacterController>() != null ||
                transform.GetComponentInParent<DuelNavBotController>() != null)
            {
                return true;
            }

            var root = transform.root;
            if (root == null)
            {
                return false;
            }

            var rootName = root.name;
            return rootName.IndexOf("Player", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rootName.IndexOf("Remote_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rootName.IndexOf("DuelNavBot", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void ResetForTests()
        {
            sceneOccluders = Array.Empty<Collider>();
            cachedSceneHandle = int.MinValue;
            WeaponBlockUtility.ResetProcessedScenesForTests();
        }
    }
}
