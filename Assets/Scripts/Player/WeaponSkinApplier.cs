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

            ApplyMaterialToRenderers(weaponRoot, material);

            if (kind == WeaponKind.Pistol)
            {
                var part2 = weaponRoot.Find(PistolSecondaryPartName);
                if (part2 != null)
                {
                    ApplyMaterialToRenderers(part2, material);
                }
            }
        }

        private static void ApplyMaterialToRenderers(Transform root, Material material)
        {
            if (root == null || material == null)
            {
                return;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || IsGripOrTargetRenderer(renderer))
                {
                    continue;
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

        private static bool IsGripOrTargetRenderer(Renderer renderer)
        {
            var objectName = renderer.gameObject.name;
            return objectName.IndexOf("Target", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("IkAnchor", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("AimPoint", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("Muzzle", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
