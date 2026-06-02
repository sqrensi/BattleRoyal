#if UNITY_EDITOR
using ShooterPrototype.Player;
using UnityEditor;
using UnityEngine;

namespace ShooterPrototype.EditorTools
{
    internal static class PlayerPrefabOptimization
    {
        public static void ApplyLocalHolsterDefaults(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var holster = root.GetComponent<PlayerWeaponHolsterController>();
            if (holster == null)
            {
                holster = root.AddComponent<PlayerWeaponHolsterController>();
            }

            var serialized = new SerializedObject(holster);
            SetSerializedVector3(serialized, "loweredLocalPosition", new Vector3(0.14f, -0.4f, -0.05f));
            SetSerializedVector3(serialized, "loweredLocalEulerAngles", new Vector3(58f, -18f, 14f));
            SetSerializedVector3(serialized, "holsterSwingOffset", new Vector3(0.05f, 0.04f, -0.16f));
            SetSerializedProperty(serialized, "holsterLowerDuration", 0.38f);
            SetSerializedProperty(serialized, "drawRaiseDuration", 0.32f);
            SetSerializedProperty(serialized, "holsteredScaleFactor", 0.94f);
            SetSerializedProperty(serialized, "armsHideHolsterThreshold", 0.68f);
            SetSerializedProperty(serialized, "armsShowDrawThreshold", 0.22f);
            SetSerializedProperty(serialized, "scaleLoweredPoseByFieldOfView", true);
            SetSerializedProperty(serialized, "referenceFieldOfView", 75f);
            SetSerializedProperty(serialized, "extraLowerPerFovRatio", 0.14f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void StripLocalFirstPersonPrefab(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            DestroyComponent<PlayerHeadMaskSelector>(root);
            DestroyComponent<SyntyHandAttachedWeaponMount>(root);

            var thirdPersonBody = root.transform.Find("ThirdPersonBody");
            if (thirdPersonBody != null)
            {
                var legacyWeaponAnchor = thirdPersonBody.Find("WeaponAnchor");
                if (legacyWeaponAnchor != null)
                {
                    Object.DestroyImmediate(legacyWeaponAnchor.gameObject);
                }
            }
        }

        public static void StripRemoteThirdPersonPrefab(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            PlayerHitboxCleanup.RemoveLegacyLineHitboxes(root);

            var cameraPivot = root.transform.Find("CameraPivot");
            if (cameraPivot != null)
            {
                Object.DestroyImmediate(cameraPivot.gameObject);
            }

            DestroyComponent<FpsCharacterController>(root);
            DestroyComponent<PlayerWeaponMount>(root);
            DestroyComponent<PlayerWeaponController>(root);
            DestroyComponent<SyntyWeaponHandBinder>(root);
            DestroyComponent<SyntyFirstPersonArmsPresenter>(root);
            DestroyComponent<SyntyFirstPersonArmLocomotionGate>(root);
            DestroyComponent<PlayerWeaponHolsterController>(root);
            DestroyComponent<SyntyHandAttachedWeaponMount>(root);
            DestroyComponent<PlayerHeadMaskSelector>(root);
            DestroyComponent<PlayerViewPresentation>(root);
            DestroyComponent<SyntySplitBodyPresentation>(root);
            DestroyComponent<SyntyBoneAnchorSync>(root);
            DestroyComponent<SyntyCrouchPosture>(root);

            var thirdPersonBody = root.transform.Find("ThirdPersonBody");
            if (thirdPersonBody != null)
            {
                var generatedArms = FindChildRecursive(thirdPersonBody, "GeneratedFirstPersonArms");
                if (generatedArms != null)
                {
                    Object.DestroyImmediate(generatedArms.gameObject);
                }
            }
        }

        public static void EnsureRemoteShotEffects(GameObject root, PlayerWeaponController sourceWeaponController)
        {
            if (root == null)
            {
                return;
            }

            var shotEffects = root.GetComponent<RemotePlayerShotEffects>();
            if (shotEffects == null)
            {
                shotEffects = root.AddComponent<RemotePlayerShotEffects>();
            }

            if (sourceWeaponController == null)
            {
                return;
            }

            shotEffects.ApplyVisualSettings(
                sourceWeaponController.MuzzleFlashVfx,
                sourceWeaponController.WorldHitVfx,
                sourceWeaponController.PlayerHitVfx,
                sourceWeaponController.ShotMaxDistance);

            var serialized = new SerializedObject(shotEffects);
            SetSerializedProperty(serialized, "showTracer", ReadBool(sourceWeaponController, "showTracer", true));
            SetSerializedProperty(serialized, "tracerDuration", ReadFloat(sourceWeaponController, "tracerDuration", 0.05f));
            SetSerializedProperty(serialized, "tracerWidth", ReadFloat(sourceWeaponController, "tracerWidth", 0.01f));
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void EnsureRemoteAudio(GameObject root, PlayerAudioController sourceAudioController)
        {
            if (root == null || sourceAudioController == null)
            {
                return;
            }

            var remoteAudio = root.GetComponent<PlayerAudioController>();
            if (remoteAudio == null)
            {
                remoteAudio = root.AddComponent<PlayerAudioController>();
            }

            remoteAudio.InheritFrom(sourceAudioController);
        }

        private static void CopyGameObjectReference(
            Object source,
            SerializedObject target,
            string sourceProperty,
            string targetProperty)
        {
            var sourceSerialized = new SerializedObject(source);
            var sourceValue = sourceSerialized.FindProperty(sourceProperty);
            var targetValue = target.FindProperty(targetProperty);
            if (sourceValue != null && targetValue != null)
            {
                targetValue.objectReferenceValue = sourceValue.objectReferenceValue;
            }
        }

        private static float ReadFloat(Object source, string propertyName, float fallback)
        {
            var serialized = new SerializedObject(source);
            var property = serialized.FindProperty(propertyName);
            return property != null ? property.floatValue : fallback;
        }

        private static bool ReadBool(Object source, string propertyName, bool fallback)
        {
            var serialized = new SerializedObject(source);
            var property = serialized.FindProperty(propertyName);
            return property != null ? property.boolValue : fallback;
        }

        private static void SetSerializedProperty(SerializedObject serialized, string propertyName, float value)
        {
            var property = serialized.FindProperty(propertyName);
            if (property != null)
            {
                property.floatValue = value;
            }
        }

        private static void SetSerializedProperty(SerializedObject serialized, string propertyName, bool value)
        {
            var property = serialized.FindProperty(propertyName);
            if (property != null)
            {
                property.boolValue = value;
            }
        }

        private static void SetSerializedVector3(SerializedObject serialized, string propertyName, Vector3 value)
        {
            var property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                return;
            }

            property.vector3Value = value;
        }

        private static void DestroyComponent<T>(GameObject root) where T : Component
        {
            var component = root.GetComponent<T>();
            if (component != null)
            {
                Object.DestroyImmediate(component);
            }
        }

        private static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null)
            {
                return null;
            }

            var all = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < all.Length; i++)
            {
                var current = all[i];
                if (current != null && string.Equals(current.name, childName, System.StringComparison.Ordinal))
                {
                    return current;
                }
            }

            return null;
        }
    }
}
#endif
