using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShooterPrototype.Player
{
    public static class DuelBotLineOfSight
    {
        private const float EyeHeight = 1.35f;
        private const float BlockMargin = 0.12f;

        private static readonly float[] AimHeights = { 1.62f, 1.2f, 0.9f };

        public static void PrepareScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            WeaponBlockUtility.EnsureSceneWeaponBlocks(scene);
        }

        public static bool CanSeeTarget(Transform viewer, Transform target)
        {
            if (viewer == null || target == null)
            {
                return false;
            }

            var origin = viewer.position + Vector3.up * EyeHeight;
            for (var i = 0; i < AimHeights.Length; i++)
            {
                var aimPoint = target.position + Vector3.up * AimHeights[i];
                if (HasClearLine(origin, aimPoint, target))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasClearLine(Vector3 origin, Vector3 targetPoint, Transform target)
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

        public static void ResetForTests()
        {
            ResetCachedSceneState();
        }

        public static void ResetCachedSceneState()
        {
        }
    }
}
