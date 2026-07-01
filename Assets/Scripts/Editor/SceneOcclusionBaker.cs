#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShooterPrototype.EditorTools
{
    public static class SceneOcclusionBaker
    {
        private static readonly string[] GameplayScenePaths =
        {
            "Assets/Scenes/dm.unity",
            "Assets/Scenes/training.unity",
            "Assets/Scenes/1x1.unity",
            "Assets/Scenes/Game.unity",
            "Assets/Scenes/challenge.unity",
        };

        [MenuItem("Shooter Prototype/Performance/Bake Occlusion Culling (All Gameplay Scenes)")]
        public static void BakeOcclusionForAllGameplayScenes()
        {
            var originalScenePath = SceneManager.GetActiveScene().path;
            var bakedCount = 0;

            try
            {
                for (var i = 0; i < GameplayScenePaths.Length; i++)
                {
                    var scenePath = GameplayScenePaths[i];
                    if (!System.IO.File.Exists(scenePath))
                    {
                        Debug.LogWarning($"[SceneOcclusionBaker] Scene not found: {scenePath}");
                        continue;
                    }

                    var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    var marked = PrepareSceneForOcclusionBake(scene);
                    if (marked <= 0)
                    {
                        Debug.LogWarning($"[SceneOcclusionBaker] No static occluders marked in {scenePath}");
                    }

                    if (!StaticOcclusionCulling.Compute())
                    {
                        Debug.LogWarning($"[SceneOcclusionBaker] Occlusion bake failed for {scenePath}");
                        continue;
                    }

                    EditorSceneManager.SaveScene(scene);
                    bakedCount++;
                    Debug.Log($"[SceneOcclusionBaker] Baked occlusion for {scenePath} ({marked} objects marked).");
                }
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(originalScenePath) && System.IO.File.Exists(originalScenePath))
                {
                    EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[SceneOcclusionBaker] Finished. Baked {bakedCount}/{GameplayScenePaths.Length} gameplay scenes.");
        }

        private static int PrepareSceneForOcclusionBake(Scene scene)
        {
            var marked = 0;
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                marked += PrepareHierarchy(roots[i].transform);
            }

            return marked;
        }

        private static int PrepareHierarchy(Transform root)
        {
            if (root == null)
            {
                return 0;
            }

            var marked = 0;
            if (ShouldMarkAsOccluder(root.gameObject))
            {
                var flags = GameObjectUtility.GetStaticEditorFlags(root.gameObject);
                flags |= StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;
                GameObjectUtility.SetStaticEditorFlags(root.gameObject, flags);
                marked++;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                if (IsDynamicGameplayBranch(root.GetChild(i).gameObject))
                {
                    continue;
                }

                marked += PrepareHierarchy(root.GetChild(i));
            }

            return marked;
        }

        private static bool ShouldMarkAsOccluder(GameObject gameObject)
        {
            if (gameObject == null || IsDynamicGameplayBranch(gameObject))
            {
                return false;
            }

            if (gameObject.GetComponent<Terrain>() != null)
            {
                return true;
            }

            return gameObject.GetComponent<MeshRenderer>() != null &&
                   gameObject.GetComponent<MeshFilter>() != null;
        }

        private static bool IsDynamicGameplayBranch(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return true;
            }

            var name = gameObject.name ?? string.Empty;
            if (name.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("bot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("spawn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("pickup", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (string.Equals(gameObject.tag, "Player", StringComparison.Ordinal))
            {
                return true;
            }

            return gameObject.GetComponent<CharacterController>() != null;
        }
    }
}
#endif
