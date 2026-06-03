#if UNITY_EDITOR
using ShooterPrototype.Player;
using UnityEditor;
using UnityEngine;

namespace ShooterPrototype.EditorTools
{
    public static class PickupSpawnSetupMenu
    {
        private const string DefaultWeaponPath = "Assets/Prefabs/AK-47/rifle_001.prefab";

        [MenuItem("ShooterPrototype/Setup Weapon Pickup Spawns")]
        public static void CreatePickupSpawnsInScene()
        {
            var existing = Object.FindFirstObjectByType<PickupSpawnManager>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[PickupSpawnSetup] PickupSpawnManager already exists in scene.");
                return;
            }

            var root = new GameObject("PickupSpawns");
            var manager = root.AddComponent<PickupSpawnManager>();
            root.AddComponent<MatchPickupSync>();
            var spawnPointsRoot = new GameObject("SpawnPoints").transform;
            spawnPointsRoot.SetParent(root.transform, false);

            CreateSpawnPoint(spawnPointsRoot, "PickupSpawn_01", new Vector3(2f, 0f, 3f));
            CreateSpawnPoint(spawnPointsRoot, "PickupSpawn_02", new Vector3(-2f, 0f, 5f));
            CreateSpawnPoint(spawnPointsRoot, "PickupSpawn_03", new Vector3(0f, 0f, 8f));

            var weaponPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultWeaponPath);
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("spawnPointsRoot").objectReferenceValue = spawnPointsRoot;
            serialized.FindProperty("autoCollectSpawnPointsFromRoot").boolValue = true;
            serialized.FindProperty("defaultVisualPrefab").objectReferenceValue = weaponPrefab;
            serialized.FindProperty("defaultPickupKind").enumValueIndex = (int)PickupKind.Weapon;
            serialized.FindProperty("spawnOnStart").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            manager.RebuildSpawnPointsFromRoot();
            Undo.RegisterCreatedObjectUndo(root, "Create Pickup Spawns");
            Selection.activeGameObject = root;
            Debug.Log("[PickupSpawnSetup] Created PickupSpawns with 3 spawn points.");
        }

        private static void CreateSpawnPoint(Transform parent, string name, Vector3 localPosition)
        {
            var point = new GameObject(name).transform;
            point.SetParent(parent, false);
            point.localPosition = localPosition;
        }
    }
}
#endif
