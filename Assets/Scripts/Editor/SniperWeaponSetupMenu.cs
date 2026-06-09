#if UNITY_EDITOR
using ShooterPrototype.Player;
using UnityEditor;
using UnityEngine;

namespace ShooterPrototype.EditorTools
{
    public static class SniperWeaponSetupMenu
    {
        private const string SourcePrefabPath = "Assets/ithappy/Weapons_FREE/Prefabs/sniper_rifle_001.prefab";
        private const string OutputPrefabPath = "Assets/Prefabs/Sniper/sniper_rifle_001.prefab";

        [MenuItem("ShooterPrototype/Setup Sniper Rifle Prefab")]
        public static void SetupSniperPrefab()
        {
            EnsureFolder("Assets/Prefabs", "Sniper");

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
            if (source == null)
            {
                Debug.LogError($"[SniperSetup] Source prefab not found at {SourcePrefabPath}");
                return;
            }

            var existingOutput = AssetDatabase.LoadAssetAtPath<GameObject>(OutputPrefabPath);
            var root = existingOutput != null
                ? PrefabUtility.LoadPrefabContents(OutputPrefabPath)
                : Object.Instantiate(source);

            try
            {
                root.name = "sniper_rifle_001";
                EnsureChild(root.transform, "AimPoint", new Vector3(0f, 0.08f, 0.52f));
                EnsureChild(root.transform, "Muzzle", new Vector3(0f, 0.06f, 1.05f));
                EnsureChild(root.transform, "LeftHandTarget", new Vector3(-0.04f, -0.02f, 0.18f));
                EnsureChild(root.transform, "RightHandTarget", new Vector3(0.02f, -0.01f, 0.08f));
                EnsureChild(root.transform, "RemoteLeftHandTarget", new Vector3(-0.03f, -0.02f, 0.2f));
                EnsureChild(root.transform, "RemoteRightHandTarget", new Vector3(0.03f, -0.01f, 0.1f));

                var profile = root.GetComponent<WeaponProfile>();
                if (profile == null)
                {
                    profile = root.AddComponent<WeaponProfile>();
                }

                ApplySniperProfile(profile);

                if (existingOutput != null)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, OutputPrefabPath);
                }
                else
                {
                    PrefabUtility.SaveAsPrefabAsset(root, OutputPrefabPath);
                }

                Debug.Log($"[SniperSetup] Saved sniper prefab with WeaponProfile at {OutputPrefabPath}");
            }
            finally
            {
                if (existingOutput != null)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
                else
                {
                    Object.DestroyImmediate(root);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(OutputPrefabPath);
        }

        [MenuItem("ShooterPrototype/Add Sniper Pickup Spawn To Scene")]
        public static void AddSniperPickupSpawn()
        {
            SetupSniperPrefab();

            var manager = Object.FindFirstObjectByType<PickupSpawnManager>();
            if (manager == null)
            {
                PickupSpawnSetupMenu.CreatePickupSpawnsInScene();
                manager = Object.FindFirstObjectByType<PickupSpawnManager>();
            }

            if (manager == null)
            {
                Debug.LogError("[SniperSetup] PickupSpawnManager not found in scene.");
                return;
            }

            var sniperPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OutputPrefabPath);
            var spawnPointsRoot = manager.transform.Find("SpawnPoints");
            if (spawnPointsRoot == null)
            {
                Debug.LogError("[SniperSetup] SpawnPoints root not found under PickupSpawnManager.");
                return;
            }

            var spawnPoint = new GameObject("PickupSpawn_Sniper").transform;
            spawnPoint.SetParent(spawnPointsRoot, false);
            spawnPoint.localPosition = new Vector3(6f, 0f, 6f);
            Undo.RegisterCreatedObjectUndo(spawnPoint.gameObject, "Add Sniper Pickup Spawn");

            manager.RebuildSpawnPointsFromRoot();

            var serialized = new SerializedObject(manager);
            var slots = serialized.FindProperty("spawnSlots");
            for (var i = 0; i < slots.arraySize; i++)
            {
                var slot = slots.GetArrayElementAtIndex(i);
                var spawnPointProp = slot.FindPropertyRelative("spawnPoint");
                if (spawnPointProp.objectReferenceValue != spawnPoint)
                {
                    continue;
                }

                slot.FindPropertyRelative("pickupVisualOverride").objectReferenceValue = sniperPrefab;
                slot.FindPropertyRelative("pickupKindOverride").enumValueIndex = (int)PickupKind.Weapon;
                slot.FindPropertyRelative("itemIdOverride").stringValue = "sniper_rifle";
                break;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            Selection.activeGameObject = spawnPoint.gameObject;
            Debug.Log("[SniperSetup] Added sniper weapon pickup spawn to scene.");
        }

        private static void ApplySniperProfile(WeaponProfile profile)
        {
            var serialized = new SerializedObject(profile);
            serialized.FindProperty("weaponKind").enumValueIndex = (int)WeaponKind.SniperRifle;
            serialized.FindProperty("automatic").boolValue = true;
            serialized.FindProperty("fireRate").floatValue = 1.15f;
            serialized.FindProperty("magazineSize").intValue = 7;
            serialized.FindProperty("reloadDuration").floatValue = 2.85f;
            serialized.FindProperty("maxDistance").floatValue = 260f;
            serialized.FindProperty("bulletDropAngleDegrees").floatValue = 0.18f;
            serialized.FindProperty("legDamage").floatValue = 45f;
            serialized.FindProperty("bodyDamage").floatValue = 62f;
            serialized.FindProperty("neckDamage").floatValue = 88f;
            serialized.FindProperty("headDamage").floatValue = 110f;
            serialized.FindProperty("spreadStartDegrees").floatValue = 0.04f;
            serialized.FindProperty("spreadPerShotDegrees").floatValue = 0.08f;
            serialized.FindProperty("spreadMaxDegrees").floatValue = 0.9f;
            serialized.FindProperty("hipFireSpreadMultiplier").floatValue = 2.4f;
            serialized.FindProperty("adsSpreadMultiplier").floatValue = 0.12f;
            serialized.FindProperty("hipFireRecoilMultiplier").floatValue = 1.35f;
            serialized.FindProperty("adsRecoilMultiplier").floatValue = 0.55f;
            serialized.FindProperty("hasScope").boolValue = true;
            serialized.FindProperty("adsCameraFov").floatValue = 22f;
            serialized.FindProperty("adsMaxLookAngle").floatValue = 28f;
            serialized.FindProperty("overrideAdsCameraPose").boolValue = true;
            serialized.FindProperty("adsCameraLocalPosition").vector3Value = new Vector3(0f, -0.1f, 0.33f);
            serialized.FindProperty("adsCameraLocalEuler").vector3Value = new Vector3(1.5f, 0f, 0f);
            serialized.FindProperty("handsScaleMultiplier").floatValue = 1.45f;
            serialized.FindProperty("muzzleFlashVfx").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/JMO Assets/WarFX/_Effects/MuzzleFlashes/FPS/WFX_MF FPS RIFLE3.prefab");
            serialized.FindProperty("shotClip").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Scifi Guns SFX Pack/Gun5_2.wav");
            serialized.FindProperty("reloadPullClip").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Scifi Guns SFX Pack/Gun5_Load.wav");
            serialized.FindProperty("reloadInsertClip").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Scifi Guns SFX Pack/Gun4_load.wav");
            serialized.FindProperty("remoteReloadClip").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Scifi Guns SFX Pack/Gun5_Load.wav");
            serialized.FindProperty("shotVolume").floatValue = 0.92f;
            serialized.FindProperty("reloadPullVolume").floatValue = 0.48f;
            serialized.FindProperty("reloadInsertVolume").floatValue = 0.56f;
            serialized.FindProperty("reloadInsertNormalizedTime").floatValue = 0.72f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureChild(Transform parent, string name, Vector3 localPosition)
        {
            var existing = parent.Find(name);
            if (existing != null)
            {
                existing.localPosition = localPosition;
                return;
            }

            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            child.localPosition = localPosition;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
        }

        private static void EnsureFolder(string parentPath, string folderName)
        {
            if (!AssetDatabase.IsValidFolder(parentPath))
            {
                return;
            }

            var fullPath = $"{parentPath}/{folderName}";
            if (!AssetDatabase.IsValidFolder(fullPath))
            {
                AssetDatabase.CreateFolder(parentPath, folderName);
            }
        }
    }
}
#endif
