namespace ShooterPrototype.Player
{
    public static class WeaponSkinResourcePaths
    {
        public const string Root = "WeaponSkins";

        public static bool IsWeaponSkinSlot(PlayerSkinSlot slot)
        {
            return slot == PlayerSkinSlot.WeaponAssaultRifle ||
                   slot == PlayerSkinSlot.WeaponSniperRifle ||
                   slot == PlayerSkinSlot.WeaponPistol ||
                   slot == PlayerSkinSlot.WeaponMp7;
        }

        public static bool TryGetWeaponKind(PlayerSkinSlot slot, out WeaponKind kind)
        {
            switch (slot)
            {
                case PlayerSkinSlot.WeaponAssaultRifle:
                    kind = WeaponKind.AssaultRifle;
                    return true;
                case PlayerSkinSlot.WeaponSniperRifle:
                    kind = WeaponKind.SniperRifle;
                    return true;
                case PlayerSkinSlot.WeaponPistol:
                    kind = WeaponKind.Pistol;
                    return true;
                case PlayerSkinSlot.WeaponMp7:
                    kind = WeaponKind.Mp7;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }

        public static bool TryGetSlotForKind(WeaponKind kind, out PlayerSkinSlot slot)
        {
            switch (kind)
            {
                case WeaponKind.AssaultRifle:
                    slot = PlayerSkinSlot.WeaponAssaultRifle;
                    return true;
                case WeaponKind.SniperRifle:
                    slot = PlayerSkinSlot.WeaponSniperRifle;
                    return true;
                case WeaponKind.Pistol:
                    slot = PlayerSkinSlot.WeaponPistol;
                    return true;
                case WeaponKind.Mp7:
                    slot = PlayerSkinSlot.WeaponMp7;
                    return true;
                default:
                    slot = default;
                    return false;
            }
        }

        public static string GetCategoryFolder(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.AssaultRifle:
                    return "Ak-47";
                case WeaponKind.SniperRifle:
                    return "sniper";
                case WeaponKind.Pistol:
                    return "pistol";
                case WeaponKind.Mp7:
                    return "mp7";
                default:
                    return string.Empty;
            }
        }

        public static string GetCategoryKey(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.AssaultRifle:
                    return "ak47";
                case WeaponKind.SniperRifle:
                    return "sniper";
                case WeaponKind.Pistol:
                    return "pistol";
                case WeaponKind.Mp7:
                    return "mp7";
                default:
                    return string.Empty;
            }
        }

        public static string BuildSkinId(WeaponKind kind, string variantId)
        {
            return $"weapon_{GetCategoryKey(kind)}_{variantId}";
        }

        public static string BuildPicturePath(WeaponKind kind, string variantId)
        {
            return $"{Root}/{GetCategoryFolder(kind)}/{variantId}/picture";
        }

        public static string BuildMaterialPath(WeaponKind kind, string variantId)
        {
            return $"{Root}/{GetCategoryFolder(kind)}/{variantId}/material";
        }

        public static string GetEquipmentSlotKey(PlayerSkinSlot slot)
        {
            switch (slot)
            {
                case PlayerSkinSlot.WeaponAssaultRifle:
                    return "weapon_assault";
                case PlayerSkinSlot.WeaponSniperRifle:
                    return "weapon_sniper";
                case PlayerSkinSlot.WeaponPistol:
                    return "weapon_pistol";
                case PlayerSkinSlot.WeaponMp7:
                    return "weapon_mp7";
                default:
                    return string.Empty;
            }
        }

        public static bool TryParseSkinId(string skinId, out WeaponKind kind, out string variantId)
        {
            kind = default;
            variantId = string.Empty;
            if (string.IsNullOrWhiteSpace(skinId))
            {
                return false;
            }

            var normalized = skinId.Trim().ToLowerInvariant();
            if (!normalized.StartsWith("weapon_"))
            {
                return false;
            }

            var parts = normalized.Split('_');
            if (parts.Length != 3)
            {
                return false;
            }

            variantId = parts[2];
            switch (parts[1])
            {
                case "ak47":
                    kind = WeaponKind.AssaultRifle;
                    return true;
                case "sniper":
                    kind = WeaponKind.SniperRifle;
                    return true;
                case "pistol":
                    kind = WeaponKind.Pistol;
                    return true;
                case "mp7":
                    kind = WeaponKind.Mp7;
                    return true;
                default:
                    return false;
            }
        }
    }
}
