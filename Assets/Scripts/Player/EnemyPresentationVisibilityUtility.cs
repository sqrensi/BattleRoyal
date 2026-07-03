using System.Collections.Generic;
using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Shared animator / mesh settings and visibility gate wiring for remotes and bots.
    /// </summary>
    public static class EnemyPresentationVisibilityUtility
    {
        private static readonly Plane[] FrustumPlaneScratch = new Plane[6];
        private static readonly Dictionary<int, EnemyPresentationVisibilityGate> GatesByRootId =
            new Dictionary<int, EnemyPresentationVisibilityGate>(32);
        private static Camera cachedFrustumCamera;
        private static int cachedFrustumFrame = -1;
        private static float enemyPresentationMaxDistanceSqr =
            GameplayPerformanceOptions.EnemyPresentationMaxDistance *
            GameplayPerformanceOptions.EnemyPresentationMaxDistance;

        public static void ConfigureEnemyPresentation(GameObject root)
        {
            if (root == null || MainMenuPlayerPreview.IsMenuPreviewSpawn)
            {
                return;
            }

            ConfigureEnemyAnimators(root);
            ConfigureEnemyRenderers(root);
            PlayerRenderingUtility.DisablePlayerShadows(root);
            EnsureVisibilityGate(root);
        }

        public static void EnsureVisibilityGate(GameObject root)
        {
            if (root == null || MainMenuPlayerPreview.IsMenuPreviewSpawn)
            {
                return;
            }

            if (root.GetComponent<EnemyPresentationVisibilityGate>() == null)
            {
                root.AddComponent<EnemyPresentationVisibilityGate>();
            }
        }

        public static bool IsPresentationActive(GameObject root)
        {
            if (root == null)
            {
                return false;
            }

            var rootId = root.GetInstanceID();
            if (GatesByRootId.TryGetValue(rootId, out var gate))
            {
                return gate != null && gate.PresentationActive;
            }

            return true;
        }

        internal static void RegisterPresentationGate(EnemyPresentationVisibilityGate gate)
        {
            if (gate == null)
            {
                return;
            }

            GatesByRootId[gate.gameObject.GetInstanceID()] = gate;
        }

        internal static void UnregisterPresentationGate(EnemyPresentationVisibilityGate gate)
        {
            if (gate == null)
            {
                return;
            }

            GatesByRootId.Remove(gate.gameObject.GetInstanceID());
        }

        public static Camera ResolveLocalPlayerCamera()
        {
            return GameplayRuntimeCache.LocalPlayerCamera;
        }

        public static bool IsWithinPresentationDistance(Camera camera, Vector3 worldPosition)
        {
            if (camera == null)
            {
                return true;
            }

            return (camera.transform.position - worldPosition).sqrMagnitude <= enemyPresentationMaxDistanceSqr;
        }

        public static bool IsVisibleToCamera(Camera camera, Renderer[] renderers, Vector3 worldPosition)
        {
            if (!IsWithinPresentationDistance(camera, worldPosition))
            {
                return false;
            }

            if (renderers == null || renderers.Length == 0)
            {
                return true;
            }

            if (camera == null || !camera.enabled)
            {
                return true;
            }

            var planes = GetCachedFrustumPlanes(camera);
            if (planes == null)
            {
                return true;
            }

            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (GeometryUtility.TestPlanesAABB(planes, renderer.bounds))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsVisibleToCamera(Camera camera, Renderer[] renderers)
        {
            return IsVisibleToCamera(camera, renderers, camera != null ? camera.transform.position : Vector3.zero);
        }

        private static Plane[] GetCachedFrustumPlanes(Camera camera)
        {
            if (camera == null)
            {
                return null;
            }

            var frame = Time.frameCount;
            if (cachedFrustumCamera == camera && cachedFrustumFrame == frame)
            {
                return FrustumPlaneScratch;
            }

            GeometryUtility.CalculateFrustumPlanes(camera, FrustumPlaneScratch);
            cachedFrustumCamera = camera;
            cachedFrustumFrame = frame;
            return FrustumPlaneScratch;
        }

        private static void ConfigureEnemyAnimators(GameObject root)
        {
            var animators = root.GetComponentsInChildren<Animator>(true);
            for (var i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator == null)
                {
                    continue;
                }

                animator.enabled = true;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullCompletely;
            }
        }

        private static void ConfigureEnemyRenderers(GameObject root)
        {
            var skinnedMeshes = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (var i = 0; i < skinnedMeshes.Length; i++)
            {
                var skinned = skinnedMeshes[i];
                if (skinned == null)
                {
                    continue;
                }

                skinned.updateWhenOffscreen = false;
                skinned.skinnedMotionVectors = false;
            }
        }
    }
}
