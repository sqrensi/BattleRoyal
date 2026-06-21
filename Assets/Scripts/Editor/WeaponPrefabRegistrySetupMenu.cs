using System.IO;
using ShooterPrototype.Player;
using UnityEditor;
using UnityEngine;

namespace ShooterPrototype.EditorTools
{
    public static class WeaponPrefabRegistrySetupMenu
    {
        private const string RegistryAssetPath = "Assets/Resources/Weapons/WeaponPrefabRegistry.asset";
        private const string EquipResourceFolder = "Assets/Resources/Weapons/Equip";
        private const string AssaultPrefabPath = "Assets/Prefabs/AK-47/rifle_001.prefab";
        private const string SniperPrefabPath = "Assets/Prefabs/Sniper/sniper_rifle_001.prefab";
        private const string PistolPrefabPath = "Assets/Prefabs/Pistol/pistol_001.prefab";
        private const string Mp7PrefabPath = "Assets/Prefabs/mp7/mp7.prefab";

        [MenuItem("Shooter Prototype/Weapons/Setup Weapon Prefab Registry")]
        public static void SetupWeaponPrefabRegistry()
        {
            EnsureResourcesFolder();
            SyncEquipResourcePrefab(AssaultPrefabPath, $"{EquipResourceFolder}/assault_rifle.prefab");
            SyncEquipResourcePrefab(SniperPrefabPath, $"{EquipResourceFolder}/sniper_rifle.prefab");
            SyncEquipResourcePrefab(PistolPrefabPath, $"{EquipResourceFolder}/pistol.prefab");
            SyncEquipResourcePrefab(Mp7PrefabPath, $"{EquipResourceFolder}/mp7.prefab");

            var registry = AssetDatabase.LoadAssetAtPath<WeaponPrefabRegistry>(RegistryAssetPath);
            if (registry == null)
            {
                registry = ScriptableObject.CreateInstance<WeaponPrefabRegistry>();
                AssetDatabase.CreateAsset(registry, RegistryAssetPath);
            }

            var serialized = new SerializedObject(registry);
            serialized.FindProperty("assaultRiflePrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(AssaultPrefabPath);
            serialized.FindProperty("sniperRiflePrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(SniperPrefabPath);
            serialized.FindProperty("pistolPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(PistolPrefabPath);
            serialized.FindProperty("mp7Prefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(Mp7PrefabPath);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (!registry.TryValidate(out var error))
            {
                Debug.LogError($"[WeaponCatalog] WeaponPrefabRegistry validation failed: {error}");
                return;
            }

            WeaponCatalog.ResetEquipPrefabsForEditor();
            WeaponCatalog.PrewarmAllWeaponPrefabs();
            Debug.Log("[WeaponCatalog] WeaponPrefabRegistry + Resources/Weapons/Equip configured and validated.");
        }

        private static void SyncEquipResourcePrefab(string sourcePath, string destinationPath)
        {
            if (!File.Exists(sourcePath))
            {
                Debug.LogError($"[WeaponCatalog] Source prefab missing: {sourcePath}");
                return;
            }

            if (File.Exists(destinationPath))
            {
                AssetDatabase.DeleteAsset(destinationPath);
            }

            if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
            {
                Debug.LogError($"[WeaponCatalog] Failed to copy {sourcePath} -> {destinationPath}");
            }
        }

        private static void EnsureResourcesFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            if (!AssetDatabase.IsValidFolder("Assets/Resources/Weapons"))
            {
                AssetDatabase.CreateFolder("Assets/Resources", "Weapons");
            }

            if (!AssetDatabase.IsValidFolder(EquipResourceFolder))
            {
                AssetDatabase.CreateFolder("Assets/Resources/Weapons", "Equip");
            }
        }
    }
}
