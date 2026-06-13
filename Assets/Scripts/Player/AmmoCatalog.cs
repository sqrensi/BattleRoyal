using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class AmmoCatalog
    {
        public const string AssaultAmmoItemId = "ammo_assault";
        public const string SniperAmmoItemId = "ammo_sniper";
        public const string PistolAmmoItemId = "ammo_pistol";
        public const string Mp7AmmoItemId = "ammo_mp7";
        public const int MaxPairedAmmoBoxesPerWeapon = 2;
        private const string PairedAmmoMarker = "_ammo_";

        public static bool TryResolveKindFromItemId(string itemId, out WeaponKind kind)
        {
            kind = WeaponKind.AssaultRifle;
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return false;
            }

            var id = itemId.Trim();
            if (id.IndexOf("sniper", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = WeaponKind.SniperRifle;
                return true;
            }

            if (id.IndexOf("pistol", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = WeaponKind.Pistol;
                return true;
            }

            if (id.IndexOf("mp7", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.IndexOf("ppsh", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = WeaponKind.Mp7;
                return true;
            }

            if (id.IndexOf("assault", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.IndexOf("ammo_pack", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.IndexOf("rifle", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = WeaponKind.AssaultRifle;
                return true;
            }

            return false;
        }

        public static string GetAmmoItemId(WeaponKind weaponKind)
        {
            switch (weaponKind)
            {
                case WeaponKind.SniperRifle:
                    return SniperAmmoItemId;
                case WeaponKind.Pistol:
                    return PistolAmmoItemId;
                case WeaponKind.Mp7:
                    return Mp7AmmoItemId;
                default:
                    return AssaultAmmoItemId;
            }
        }

        public static int GetDefaultPickupAmount(WeaponKind weaponKind)
        {
            switch (weaponKind)
            {
                case WeaponKind.SniperRifle:
                    return 7;
                case WeaponKind.Pistol:
                    return 12;
                case WeaponKind.Mp7:
                    return 30;
                default:
                    return 30;
            }
        }

        public static Color GetAmmoColor(WeaponKind weaponKind)
        {
            switch (weaponKind)
            {
                case WeaponKind.SniperRifle:
                    return new Color(0.45f, 0.58f, 0.82f, 1f);
                case WeaponKind.Pistol:
                    return new Color(0.86f, 0.54f, 0.24f, 1f);
                case WeaponKind.Mp7:
                    return new Color(0.34f, 0.78f, 0.42f, 1f);
                default:
                    return new Color(0.82f, 0.68f, 0.18f, 1f);
            }
        }

        public static string GetPairedAmmoSpawnId(string weaponSpawnId, int boxIndex = 0)
        {
            if (string.IsNullOrWhiteSpace(weaponSpawnId))
            {
                return string.Empty;
            }

            return $"{weaponSpawnId}{PairedAmmoMarker}{Mathf.Clamp(boxIndex, 0, MaxPairedAmmoBoxesPerWeapon - 1):00}";
        }

        public static bool IsPairedAmmoSpawnId(string spawnId)
        {
            if (string.IsNullOrWhiteSpace(spawnId) || PickupSpawnZone.IsStandaloneAmmoSpawnId(spawnId))
            {
                return false;
            }

            return spawnId.IndexOf(PairedAmmoMarker, System.StringComparison.Ordinal) >= 0;
        }

        public static bool TryParsePairedAmmoSpawnId(string ammoSpawnId, out string weaponSpawnId, out int boxIndex)
        {
            weaponSpawnId = string.Empty;
            boxIndex = 0;
            if (!IsPairedAmmoSpawnId(ammoSpawnId))
            {
                return false;
            }

            var markerIndex = ammoSpawnId.LastIndexOf(PairedAmmoMarker, System.StringComparison.Ordinal);
            if (markerIndex <= 0)
            {
                return false;
            }

            weaponSpawnId = ammoSpawnId.Substring(0, markerIndex);
            var suffix = ammoSpawnId.Substring(markerIndex + PairedAmmoMarker.Length);
            if (!int.TryParse(suffix, out boxIndex))
            {
                boxIndex = 0;
            }

            return !string.IsNullOrWhiteSpace(weaponSpawnId);
        }

        public static bool TryResolveWeaponSpawnIdFromPairedAmmo(string ammoSpawnId, out string weaponSpawnId)
        {
            return TryParsePairedAmmoSpawnId(ammoSpawnId, out weaponSpawnId, out _);
        }
    }
}
