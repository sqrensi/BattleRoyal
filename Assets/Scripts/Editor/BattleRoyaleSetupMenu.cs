using ShooterPrototype.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ShooterPrototype.Editor
{
    public static class BattleRoyaleSetupMenu
    {
        private const string PlanePrefabPath = "Assets/Prefabs/Plane/Plane.prefab";

        [MenuItem("ShooterPrototype/Setup Battle Royale Flow")]
        public static void SetupBattleRoyaleFlow()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.name != "Game")
            {
                EditorUtility.DisplayDialog(
                    "Battle Royale Setup",
                    "Open the Game scene before running this setup.",
                    "OK");
                return;
            }

            var controller = Object.FindFirstObjectByType<MatchBattleRoyaleController>();
            if (controller == null)
            {
                var controllerObject = new GameObject("MatchBattleRoyale");
                controller = controllerObject.AddComponent<MatchBattleRoyaleController>();
            }

            var planePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlanePrefabPath);
            if (planePrefab != null)
            {
                var serialized = new SerializedObject(controller);
                serialized.FindProperty("planePrefab").objectReferenceValue = planePrefab;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = controller.gameObject;
            Debug.Log("[BattleRoyaleSetup] MatchBattleRoyaleController configured in Game scene.");
        }
    }
}
