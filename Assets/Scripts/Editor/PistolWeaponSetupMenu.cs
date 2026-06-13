#if UNITY_EDITOR
using ShooterPrototype.Player;
using UnityEditor;
using UnityEngine;

namespace ShooterPrototype.EditorTools
{
    public static class PistolWeaponSetupMenu
    {
        private const string SourcePrefabPath = "Assets/ithappy/Weapons_FREE/Prefabs/pistol_001.prefab";
        private const string OutputPrefabPath = "Assets/Prefabs/Pistol/pistol_001.prefab";

        [MenuItem("ShooterPrototype/Setup Pistol Prefab")]
        public static void SetupPistolPrefab()
        {
            EnsureFolder("Assets/Prefabs", "Pistol");

            var existingOutput = AssetDatabase.LoadAssetAtPath<GameObject>(OutputPrefabPath);
            var root = existingOutput != null
                ? PrefabUtility.LoadPrefabContents(OutputPrefabPath)
                : Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath));

            if (root == null)
            {
                Debug.LogError($"[PistolSetup] Source prefab not found at {SourcePrefabPath}");
                return;
            }

            try
            {
                root.name = "pistol_001";
                EnsureChild(root.transform, "AimPoint", new Vector3(0f, 0.05f, 0.18f));
                EnsureChild(root.transform, "Muzzle", new Vector3(0f, 0.04f, 0.22f));
                EnsureChild(root.transform, "LeftHandTarget", new Vector3(-0.03f, -0.02f, 0.08f));
                EnsureChild(root.transform, "RightHandTarget", new Vector3(0.02f, -0.01f, 0.04f));
                EnsureChild(root.transform, "RemoteLeftHandTarget", new Vector3(-0.025f, -0.02f, 0.09f));
                EnsureChild(root.transform, "RemoteRightHandTarget", new Vector3(0.025f, -0.01f, 0.05f));

                var profile = root.GetComponent<WeaponProfile>();
                if (profile == null)
                {
                    profile = root.AddComponent<WeaponProfile>();
                }

                ApplyPistolProfile(profile);
                PrefabUtility.SaveAsPrefabAsset(root, OutputPrefabPath);
                Debug.Log($"[PistolSetup] Saved pistol prefab with WeaponProfile at {OutputPrefabPath}");
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

        private static void ApplyPistolProfile(WeaponProfile profile)
        {
            var serialized = new SerializedObject(profile);
            serialized.FindProperty("weaponKind").enumValueIndex = (int)WeaponKind.Pistol;
            serialized.FindProperty("automatic").boolValue = false;
            serialized.FindProperty("fireRate").floatValue = 4.5f;
            serialized.FindProperty("magazineSize").intValue = 12;
            serialized.FindProperty("reloadDuration").floatValue = 1.35f;
            serialized.FindProperty("maxDistance").floatValue = 90f;
            serialized.FindProperty("bulletDropAngleDegrees").floatValue = 0.2f;
            serialized.FindProperty("legDamage").floatValue = 12f;
            serialized.FindProperty("bodyDamage").floatValue = 22f;
            serialized.FindProperty("neckDamage").floatValue = 55f;
            serialized.FindProperty("headDamage").floatValue = 85f;
            serialized.FindProperty("spreadStartDegrees").floatValue = 0.12f;
            serialized.FindProperty("spreadPerShotDegrees").floatValue = 0.28f;
            serialized.FindProperty("spreadMaxDegrees").floatValue = 2.8f;
            serialized.FindProperty("hipFireSpreadMultiplier").floatValue = 2f;
            serialized.FindProperty("adsSpreadMultiplier").floatValue = 0.45f;
            serialized.FindProperty("hipFireRecoilMultiplier").floatValue = 0.85f;
            serialized.FindProperty("adsRecoilMultiplier").floatValue = 0.6f;
            serialized.FindProperty("hasScope").boolValue = false;
            serialized.FindProperty("adsCameraFov").floatValue = 58f;
            serialized.FindProperty("adsMaxLookAngle").floatValue = 38f;
            serialized.FindProperty("adsLookSensitivityMultiplier").floatValue = 1.4f;
            serialized.FindProperty("overrideAdsCameraPose").boolValue = true;
            serialized.FindProperty("adsCameraLocalPosition").vector3Value = new Vector3(0f, -0.12f, 0.28f);
            serialized.FindProperty("adsCameraLocalEuler").vector3Value = new Vector3(0.5f, 0f, 0f);
            serialized.FindProperty("handsScaleMultiplier").floatValue = 1.05f;
            serialized.FindProperty("muzzleFlashVfx").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/JMO Assets/WarFX/_Effects/MuzzleFlashes/FPS/WFX_MF FPS RIFLE1.prefab");
            serialized.FindProperty("shotClip").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Scifi Guns SFX Pack/Gun4_2.wav");
            serialized.FindProperty("reloadPullClip").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Scifi Guns SFX Pack/Gun4_load.wav");
            serialized.FindProperty("reloadInsertClip").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(
                    "Assets/Weapons of Choice FREE - Komposite Sound/GUN/Handling_Gun_01_Clip_In_SFX.wav");
            serialized.FindProperty("remoteReloadClip").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Scifi Guns SFX Pack/Gun4_load.wav");
            serialized.FindProperty("shotVolume").floatValue = 0.72f;
            serialized.FindProperty("reloadPullVolume").floatValue = 0.42f;
            serialized.FindProperty("reloadInsertVolume").floatValue = 0.5f;
            serialized.FindProperty("reloadInsertNormalizedTime").floatValue = 0.74f;
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
