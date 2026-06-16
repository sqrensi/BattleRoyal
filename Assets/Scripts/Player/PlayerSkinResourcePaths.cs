namespace ShooterPrototype.Player
{
    public static class PlayerSkinResourcePaths
    {
        public const string SkinsRoot = "Skins";

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
