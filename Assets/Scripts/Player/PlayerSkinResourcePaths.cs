using System;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class PlayerSkinResourcePaths
    {
        public const string SkinsRoot = "Skins";
        public const string AttachmentsRoot = "Attachments";

        public static bool IsAttachmentSlot(PlayerSkinSlot slot)
        {
            return slot == PlayerSkinSlot.Face || slot == PlayerSkinSlot.Hair;
        }

        public static string GetCategoryFolder(PlayerSkinSlot slot)
        {
            switch (slot)
            {
                case PlayerSkinSlot.Shirt:
                    return "tshirts";
                case PlayerSkinSlot.Pants:
                    return "pants";
                case PlayerSkinSlot.Boots:
                    return "shoes";
                case PlayerSkinSlot.Gloves:
                    return "gloves";
                default:
                    return string.Empty;
            }
        }

        public static string GetAttachmentCategory(PlayerSkinSlot slot)
        {
            switch (slot)
            {
                case PlayerSkinSlot.Face:
                    return "face";
                case PlayerSkinSlot.Hair:
                    return "hair";
                default:
                    return string.Empty;
            }
        }

        public static string BuildAttachmentSkinId(string categoryFolder, string variantId)
        {
            return $"attachment_{categoryFolder}_{variantId}";
        }

        public static string BuildAttachmentPrefabPath(string categoryFolder, string variantId)
        {
            return $"{AttachmentsRoot}/{categoryFolder}/{variantId}";
        }

        public static string BuildAttachmentPicturePath(string categoryFolder, string variantId)
        {
            return $"{AttachmentsRoot}/{categoryFolder}/{variantId}";
        }

        public static string BuildSkinId(string categoryFolder, string variantId)
        {
            return $"{categoryFolder}_{variantId}";
        }

        public static string BuildPrefabPath(string categoryFolder, string variantId)
        {
            return $"{SkinsRoot}/{categoryFolder}/{variantId}/prefab";
        }

        public static string BuildMaterialPath(string categoryFolder, string variantId)
        {
            return $"{SkinsRoot}/{categoryFolder}/{variantId}/material";
        }

        public static string BuildPicturePath(string categoryFolder, string variantId)
        {
            return $"{SkinsRoot}/{categoryFolder}/{variantId}/picture";
        }

        public static bool TryResolvePictureResourcePath(string skinId, out string pictureResourcePath)
        {
            pictureResourcePath = string.Empty;
            if (string.IsNullOrWhiteSpace(skinId))
            {
                return false;
            }

            var normalized = skinId.Trim();
            if (WeaponSkinResourcePaths.TryParseSkinId(normalized, out var weaponKind, out var weaponVariant))
            {
                pictureResourcePath = WeaponSkinResourcePaths.BuildPicturePath(weaponKind, weaponVariant);
                return true;
            }

            if (normalized.StartsWith("attachment_", StringComparison.OrdinalIgnoreCase))
            {
                var parts = normalized.Split('_');
                if (parts.Length < 3)
                {
                    return false;
                }

                pictureResourcePath = BuildAttachmentPicturePath(parts[1], parts[2]);
                return true;
            }

            var separatorIndex = normalized.LastIndexOf('_');
            if (separatorIndex <= 0 || separatorIndex >= normalized.Length - 1)
            {
                return false;
            }

            pictureResourcePath = BuildPicturePath(
                normalized.Substring(0, separatorIndex),
                normalized.Substring(separatorIndex + 1));
            return true;
        }

        public static string ResolveClothingPrefabPath(string categoryFolder, string variantId)
        {
            if (string.IsNullOrWhiteSpace(categoryFolder) || string.IsNullOrWhiteSpace(variantId))
            {
                return string.Empty;
            }

            var variantPath = BuildPrefabPath(categoryFolder, variantId);
            if (Resources.Load<GameObject>(variantPath) != null)
            {
                return variantPath;
            }

            if (string.Equals(variantId, "001", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            var fallbackPath = BuildPrefabPath(categoryFolder, "001");
            return Resources.Load<GameObject>(fallbackPath) != null ? fallbackPath : string.Empty;
        }

        public static string ResolveClothingMaterialPath(string categoryFolder, string variantId)
        {
            if (string.IsNullOrWhiteSpace(categoryFolder) || string.IsNullOrWhiteSpace(variantId))
            {
                return string.Empty;
            }

            var materialPath = BuildMaterialPath(categoryFolder, variantId);
            if (Resources.Load<Material>(materialPath) != null)
            {
                return materialPath;
            }

            var alternatePath = $"{SkinsRoot}/{categoryFolder}/{variantId}/wow";
            return Resources.Load<Material>(alternatePath) != null ? alternatePath : materialPath;
        }
    }
}
