#if UNITY_EDITOR
using ShooterPrototype.Player;
using UnityEditor;
using UnityEngine;

namespace ShooterPrototype.EditorTools
{
    public static class DamageZoneSetupMenu
    {
        private const string DamageZonePrefabPath =
            "Assets/Synty/PolygonBattleRoyale/Prefabs/FX/DamageZone.prefab";

        [MenuItem("ShooterPrototype/Setup Match Damage Zone")]
        public static void CreateDamageZoneInScene()
        {
            var existing = Object.FindFirstObjectByType<MatchDamageZoneController>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[DamageZoneSetup] MatchDamageZoneController already exists in scene.");
                return;
            }

            var root = new GameObject("MatchDamageZone");
            var center = new GameObject("MapCenter").transform;
            center.SetParent(root.transform, false);

            var controller = root.AddComponent<MatchDamageZoneController>();
            var visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DamageZonePrefabPath);
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("zoneCenter").objectReferenceValue = center;
            serialized.FindProperty("damageZoneVisualPrefab").objectReferenceValue = visualPrefab;
            serialized.FindProperty("phase1CenterOffset").floatValue = 10f;
            serialized.FindProperty("phase2CenterOffset").floatValue = 40f;
            serialized.FindProperty("phase1EndRadius").floatValue = 100f;
            serialized.FindProperty("totalShrinkDurationSeconds").floatValue = 180f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Undo.RegisterCreatedObjectUndo(root, "Create Match Damage Zone");
            Selection.activeGameObject = root;
            Debug.Log("[DamageZoneSetup] Created MatchDamageZone: 2 phases, 220->0 in 180s.");
        }
    }
}
#endif
