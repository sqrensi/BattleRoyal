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
    }
}
