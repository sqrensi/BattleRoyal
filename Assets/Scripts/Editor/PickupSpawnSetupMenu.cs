#if UNITY_EDITOR
using ShooterPrototype.Player;
using UnityEditor;
using UnityEngine;

namespace ShooterPrototype.EditorTools
{
    public static class PickupSpawnSetupMenu
    {
        private const string AssaultWeaponPath = "Assets/Prefabs/AK-47/rifle_001.prefab";
        private const string SniperWeaponPath = "Assets/Prefabs/Sniper/sniper_rifle_001.prefab";

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

            var spawnZonesRoot = new GameObject("SpawnZones").transform;
            spawnZonesRoot.SetParent(root.transform, false);
            CreateExampleZone(spawnZonesRoot, "BuildingFloor_A", new Vector3(0f, 0.05f, 0f), new Vector3(8f, 0.1f, 6f));
            CreateExampleZone(spawnZonesRoot, "BuildingFloor_B", new Vector3(12f, 0.05f, 4f), new Vector3(6f, 0.1f, 5f));

            var assaultPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssaultWeaponPath);
            var sniperPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SniperWeaponPath);
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("spawnZonesRoot").objectReferenceValue = spawnZonesRoot;
            serialized.FindProperty("autoCollectSpawnZonesFromScene").boolValue = true;
            serialized.FindProperty("spawnCollectionMode").enumValueIndex = (int)0;
            serialized.FindProperty("defaultVisualPrefab").objectReferenceValue = assaultPrefab;
            serialized.FindProperty("defaultPickupKind").enumValueIndex = (int)PickupKind.Weapon;
            serialized.FindProperty("spawnOnStart").boolValue = true;

            var randomPool = serialized.FindProperty("randomPickupPool");
            randomPool.FindPropertyRelative("enabled").boolValue = true;
            var entries = randomPool.FindPropertyRelative("entries");
            entries.arraySize = 3;
            SetRandomPoolEntry(entries.GetArrayElementAtIndex(0), PickupKind.Weapon, WeaponKind.AssaultRifle, assaultPrefab, "assault_rifle", 1, 1f);
            SetRandomPoolEntry(entries.GetArrayElementAtIndex(1), PickupKind.Weapon, WeaponKind.SniperRifle, sniperPrefab, "sniper_rifle", 1, 1f);
            SetRandomPoolEntry(entries.GetArrayElementAtIndex(2), PickupKind.Ammo, WeaponKind.AssaultRifle, null, "ammo_pack", 30, 1f);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            manager.RebuildSpawnZonesFromRoot();
            Undo.RegisterCreatedObjectUndo(root, "Create Pickup Spawns");
            Selection.activeGameObject = root;
            Debug.Log("[PickupSpawnSetup] Created PickupSpawns with floor spawn zones.");
        }

        [MenuItem("ShooterPrototype/Add Pickup Spawn Zone")]
        public static void AddPickupSpawnZone()
        {
            var zonesRoot = GameObject.Find("PickupSpawns/SpawnZones")?.transform;
            if (zonesRoot == null)
            {
                var manager = Object.FindFirstObjectByType<PickupSpawnManager>();
                if (manager != null)
                {
                    var serialized = new SerializedObject(manager);
                    zonesRoot = serialized.FindProperty("spawnZonesRoot").objectReferenceValue as Transform;
                }
            }

            if (zonesRoot == null)
            {
                Debug.LogWarning("[PickupSpawnSetup] Create PickupSpawns first or assign Spawn Zones Root.");
                return;
            }

            var zoneCount = zonesRoot.childCount + 1;
            var zone = CreateExampleZone(
                zonesRoot,
                $"BuildingFloor_{zoneCount:00}",
                Vector3.zero,
                new Vector3(6f, 0.1f, 5f));
            Selection.activeGameObject = zone;
            var managerComponent = Object.FindFirstObjectByType<PickupSpawnManager>();
            managerComponent?.RebuildSpawnZonesFromRoot();
        }

        private static GameObject CreateExampleZone(
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 boxSize)
        {
            var zoneObject = new GameObject(name);
            zoneObject.transform.SetParent(parent, false);
            zoneObject.transform.localPosition = localPosition;

            var collider = zoneObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = boxSize;
            collider.center = Vector3.zero;

            var zone = zoneObject.AddComponent<PickupSpawnZone>();
            var serialized = new SerializedObject(zone);
            serialized.FindProperty("pickupCount").intValue = 2;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return zoneObject;
        }

        private static void SetRandomPoolEntry(
            SerializedProperty entry,
            PickupKind kind,
            WeaponKind weaponKind,
            GameObject visualPrefab,
            string itemId,
            int amount,
            float weight)
        {
            entry.FindPropertyRelative("kind").enumValueIndex = (int)kind;
            entry.FindPropertyRelative("weaponKind").enumValueIndex = (int)weaponKind;
            entry.FindPropertyRelative("visualPrefab").objectReferenceValue = visualPrefab;
            entry.FindPropertyRelative("itemId").stringValue = itemId;
            entry.FindPropertyRelative("amount").intValue = amount;
            entry.FindPropertyRelative("weight").floatValue = weight;
        }
    }
}
#endif
