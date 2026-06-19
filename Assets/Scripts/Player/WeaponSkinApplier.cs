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
                return;
            }

            ApplyEquippedSkin(weaponRoot, kind);
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

            if (!TryApplyMainBodyMaterial(weaponRoot, material))
            {
                var profileTransform = ResolveWeaponProfileTransform(weaponRoot);
                if (profileTransform != null && profileTransform != weaponRoot)
                {
                    TryApplyMainBodyMaterial(profileTransform, material);
                }
            }

            if (kind == WeaponKind.Pistol)
            {
                ApplyPistolSecondaryPart(weaponRoot, material);
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

        private static Transform ResolveWeaponProfileTransform(Transform weaponRoot)
        {
            var profile = weaponRoot.GetComponent<WeaponProfile>() ??
                          weaponRoot.GetComponentInChildren<WeaponProfile>(true);
            return profile != null ? profile.transform : null;
        }

        private static bool TryApplyMainBodyMaterial(Transform bodyTransform, Material material)
        {
            var renderer = bodyTransform.GetComponent<Renderer>();
            if (renderer == null || !ShouldReceiveWeaponSkin(renderer))
            {
                return false;
            }

            ApplyMaterialToRenderer(renderer, material);
            return true;
        }

        private static bool ShouldReceiveWeaponSkin(Renderer renderer)
        {
            return renderer != null &&
                   !IsExcludedRenderer(renderer) &&
                   !IsUnderExcludedAttachmentHierarchy(renderer.transform);
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

        private static bool IsExcludedRenderer(Renderer renderer)
        {
            if (renderer == null)
            {
                return true;
            }

            return ContainsExcludedNameToken(renderer.gameObject.name);
        }

        private static bool IsUnderExcludedAttachmentHierarchy(Transform transform)
        {
            var current = transform != null ? transform.parent : null;
            while (current != null)
            {
                if (ContainsExcludedNameToken(current.gameObject.name))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static bool ContainsExcludedNameToken(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return false;
            }

            return objectName.IndexOf("Target", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("IkAnchor", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("AimPoint", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Muzzle", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Optic", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Scope", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Sight", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Lens", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Part2", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Attachment", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Trigger", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Slide", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Hammer", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Stock", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
