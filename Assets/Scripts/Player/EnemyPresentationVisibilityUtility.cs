using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Shared animator / mesh settings and visibility gate wiring for remotes and bots.
    /// </summary>
    public static class EnemyPresentationVisibilityUtility
    {
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

            var gate = root.GetComponent<EnemyPresentationVisibilityGate>();
            return gate == null || gate.PresentationActive;
        }

        public static Camera ResolveLocalPlayerCamera()
        {
            return GameplayRuntimeCache.LocalPlayerCamera;
        }

        public static bool IsVisibleToCamera(Camera camera, Renderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0)
            {
                return true;
            }

            if (camera == null || !camera.enabled)
            {
                return true;
            }

            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
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
