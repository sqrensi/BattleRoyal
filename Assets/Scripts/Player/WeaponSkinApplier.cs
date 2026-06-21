using System;
using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Applies weapon cosmetics to player-held weapon models only.
    /// World/map pickups keep prefab default materials and must not use this type.
    /// </summary>
    public static class WeaponSkinApplier
    {
        private const string PistolSecondaryPartName = "Part2";
        /// <summary>
        /// Applies the locally equipped inventory skin when a weapon enters the player's hands.
        /// Default skin variants (no material) leave prefab materials unchanged.
        /// </summary>
        public static void ApplyEquippedSkin(Transform weaponRoot, WeaponKind kind)
        {
            if (weaponRoot == null)
            {
                return;
            }

            var skinState = PlayerSkinSelectionService.CaptureLocalNetworkState();
            if (skinState.TryGetWeaponSkinId(kind, out var skinId))
            {
                ApplySkinById(weaponRoot, kind, skinId);
            }
        }

        public static void ApplyNetworkSkin(
            Transform weaponRoot,
            WeaponKind kind,
            in PlayerSkinNetworkState skinState)
        {
            if (weaponRoot == null)
            {
                return;
            }

            if (skinState.TryGetWeaponSkinId(kind, out var skinId))
            {
                ApplySkinById(weaponRoot, kind, skinId);
            }
        }

        public static void ApplySkinById(Transform weaponRoot, WeaponKind kind, string skinId)
        {
            if (weaponRoot == null || string.IsNullOrWhiteSpace(skinId))
            {
                return;
            }

            if (!PlayerSkinSelectionService.TryGetDefinitionById(skinId, out var skin) || !skin.IsValid)
            {
                return;
            }

            ApplySkin(weaponRoot, kind, skin);
        }

        public static void ApplySkin(Transform weaponRoot, WeaponKind kind, PlayerSkinDefinition skin)
        {
            if (weaponRoot == null || !skin.IsValid)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(skin.MaterialResourcePath))
            {
                return;
            }

            var material = Resources.Load<Material>(skin.MaterialResourcePath);
            if (material == null)
            {
                return;
            }

            ApplyMaterialToRenderers(weaponRoot, kind, material);
        }

        private static void ApplyMaterialToRenderers(Transform weaponRoot, WeaponKind kind, Material material)
        {
            if (weaponRoot == null || material == null)
            {
                return;
            }

            ApplyMaterialToEligibleWeaponRenderers(weaponRoot, material, skipPart2: kind == WeaponKind.Pistol);

            if (kind == WeaponKind.Pistol)
            {
                ApplyPistolSecondaryPart(weaponRoot, material);
            }
        }

        private static void ApplyMaterialToEligibleWeaponRenderers(
            Transform weaponRoot,
            Material material,
            bool skipPart2)
        {
            if (weaponRoot == null || material == null)
            {
                return;
            }

            var renderers = weaponRoot.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !ShouldReceiveWeaponSkin(renderer))
                {
                    continue;
                }

                if (skipPart2 &&
                    string.Equals(renderer.gameObject.name, PistolSecondaryPartName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ApplyMaterialToRenderer(renderer, material);
            }
        }

        private static void ApplyPistolSecondaryPart(Transform weaponRoot, Material material)
        {
            var part2 = FindChildRecursive(weaponRoot, PistolSecondaryPartName);
            if (part2 == null)
            {
                return;
            }

            var renderer = part2.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            ApplyMaterialToRenderer(renderer, material);
        }

        private static Transform FindChildRecursive(Transform parent, string childName)
        {
            if (parent == null || string.IsNullOrWhiteSpace(childName))
            {
                return null;
            }

            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (string.Equals(child.name, childName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }

                var nested = FindChildRecursive(child, childName);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static bool ShouldReceiveWeaponSkin(Renderer renderer)
        {
            return renderer != null &&
                   !IsExcludedRenderer(renderer) &&
                   !IsUnderExcludedAttachmentHierarchy(renderer.transform);
        }

        private static bool IsExcludedRenderer(Renderer renderer)
        {
            if (renderer == null)
            {
                return true;
            }

            return IsExcludedWeaponHelperName(renderer.gameObject.name);
        }

        private static bool IsUnderExcludedAttachmentHierarchy(Transform transform)
        {
            var current = transform != null ? transform.parent : null;
            while (current != null)
            {
                if (IsExcludedWeaponHelperName(current.gameObject.name))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static bool IsExcludedWeaponHelperName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return false;
            }

            if (string.Equals(objectName, "RemoteWeaponTarget", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(objectName, "BackWeaponTarget", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(objectName, "BackWeaponTarget2", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(objectName, "AimPoint", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(objectName, "Muzzle", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(objectName, "MagHandTarget", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(objectName, "SightTarget", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(objectName, PistolSecondaryPartName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (objectName.EndsWith("HandTarget", StringComparison.OrdinalIgnoreCase) ||
                objectName.EndsWith("IkAnchor", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return objectName.IndexOf("Optic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Scope", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Lens", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Attachment", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ApplyMaterialToRenderer(Renderer renderer, Material material)
        {
            if (renderer == null || material == null)
            {
                return;
            }

            var sharedMaterials = renderer.sharedMaterials;
            for (var j = 0; j < sharedMaterials.Length; j++)
            {
                sharedMaterials[j] = material;
            }

            renderer.sharedMaterials = sharedMaterials;

            var instanceMaterials = renderer.materials;
            for (var j = 0; j < instanceMaterials.Length; j++)
            {
                instanceMaterials[j] = material;
            }

            renderer.materials = instanceMaterials;
        }
    }
}
