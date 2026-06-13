using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ShooterPrototype.Player
{
    public static class WeaponCatalog
    {
        private const string AssaultPrefabPath = "Assets/Prefabs/AK-47/rifle_001.prefab";
        private const string SniperPrefabPath = "Assets/Prefabs/Sniper/sniper_rifle_001.prefab";
        private const string PistolPrefabPath = "Assets/Prefabs/Pistol/pistol_001.prefab";
        private const string Mp7PrefabPath = "Assets/Prefabs/mp7/mp7.prefab";
        private const string AssaultMuzzleFlashPath =
            "Assets/JMO Assets/WarFX/_Effects/MuzzleFlashes/FPS/WFX_MF FPS RIFLE1.prefab";
        private const string SniperMuzzleFlashPath =
            "Assets/JMO Assets/WarFX/_Effects/MuzzleFlashes/FPS/WFX_MF FPS RIFLE3.prefab";
        private const string PistolMuzzleFlashPath =
            "Assets/JMO Assets/WarFX/_Effects/MuzzleFlashes/FPS/WFX_MF FPS RIFLE1.prefab";
        private const string Mp7MuzzleFlashPath =
            "Assets/JMO Assets/WarFX/_Effects/MuzzleFlashes/FPS/WFX_MF FPS RIFLE1.prefab";
        private const string AssaultShotClipPath = "Assets/Scifi Guns SFX Pack/Gun4_2.wav";
        private const string SniperShotClipPath = "Assets/Scifi Guns SFX Pack/Gun5_2.wav";
        private const string PistolShotClipPath = "Assets/Scifi Guns SFX Pack/Gun4_2.wav";
        private const string Mp7ShotClipPath = "Assets/Scifi Guns SFX Pack/Gun4_2.wav";
        private const string AssaultReloadPullPath = "Assets/Scifi Guns SFX Pack/Gun4_load.wav";
        private const string SniperReloadPullPath = "Assets/Scifi Guns SFX Pack/Gun5_Load.wav";
        private const string PistolReloadPullPath = "Assets/Scifi Guns SFX Pack/Gun4_load.wav";
        private const string Mp7ReloadPullPath = "Assets/Scifi Guns SFX Pack/Gun4_load.wav";
        private const string AssaultReloadInsertPath =
            "Assets/Weapons of Choice FREE - Komposite Sound/GUN/Handling_Gun_01_Clip_In_SFX.wav";
        private const string SniperReloadInsertPath = "Assets/Scifi Guns SFX Pack/Gun4_load.wav";
        private const string PistolReloadInsertPath =
            "Assets/Weapons of Choice FREE - Komposite Sound/GUN/Handling_Gun_01_Clip_In_SFX.wav";
        private const string Mp7ReloadInsertPath =
            "Assets/Weapons of Choice FREE - Komposite Sound/GUN/Handling_Gun_01_Clip_In_SFX.wav";

        private static GameObject assaultPrefabCache;
        private static GameObject sniperPrefabCache;
        private static GameObject pistolPrefabCache;
        private static GameObject mp7PrefabCache;
        private static GameObject assaultMuzzleFlashCache;
        private static GameObject sniperMuzzleFlashCache;
        private static GameObject pistolMuzzleFlashCache;
        private static GameObject mp7MuzzleFlashCache;
        private static AudioClip assaultShotCache;
        private static AudioClip sniperShotCache;
        private static AudioClip pistolShotCache;
        private static AudioClip mp7ShotCache;
        private static AudioClip assaultReloadPullCache;
        private static AudioClip sniperReloadPullCache;
        private static AudioClip pistolReloadPullCache;
        private static AudioClip mp7ReloadPullCache;
        private static AudioClip assaultReloadInsertCache;
        private static AudioClip sniperReloadInsertCache;
        private static AudioClip pistolReloadInsertCache;
        private static AudioClip mp7ReloadInsertCache;

        public static WeaponKind ResolveKindFromItemId(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return WeaponKind.AssaultRifle;
            }

            if (itemId.IndexOf("sniper", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return WeaponKind.SniperRifle;
            }

            if (itemId.IndexOf("pistol", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return WeaponKind.Pistol;
            }

            if (itemId.IndexOf("mp7", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                itemId.IndexOf("ppsh", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return WeaponKind.Mp7;
            }

            return WeaponKind.AssaultRifle;
        }

        public static bool TryGetProfile(GameObject prefab, out WeaponProfile profile)
        {
            profile = null;
            if (prefab == null)
            {
                return false;
            }

            profile = prefab.GetComponent<WeaponProfile>() ??
                      prefab.GetComponentInChildren<WeaponProfile>(true);
            return profile != null;
        }

        public static bool IsFullWeaponPrefab(GameObject prefab) => TryGetProfile(prefab, out _);

        public static WeaponKind ResolveKindFromPrefab(GameObject prefab, string itemId = null)
        {
            if (TryGetProfile(prefab, out var profile))
            {
                return profile.Kind;
            }

            if (!string.IsNullOrWhiteSpace(itemId))
            {
                return ResolveKindFromItemId(itemId);
            }

            return prefab != null
                ? ResolveKindFromItemId(prefab.name)
                : WeaponKind.AssaultRifle;
        }

        public static GameObject ResolveEquipPrefab(string itemId)
        {
            return GetWeaponPrefab(ResolveKindFromItemId(itemId));
        }

        public static void RegisterWeaponPrefab(WeaponKind kind, GameObject prefab)
        {
            if (!IsValidEquipPrefab(prefab, kind))
            {
                return;
            }

            switch (kind)
            {
                case WeaponKind.SniperRifle:
                    sniperPrefabCache = prefab;
                    break;
                case WeaponKind.Pistol:
                    pistolPrefabCache = prefab;
                    break;
                case WeaponKind.Mp7:
                    mp7PrefabCache = prefab;
                    break;
                default:
                    assaultPrefabCache = prefab;
                    break;
            }
        }

        public static GameObject GetWeaponPrefab(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.SniperRifle:
                    if (IsValidEquipPrefab(sniperPrefabCache, WeaponKind.SniperRifle))
                    {
                        return sniperPrefabCache;
                    }

                    sniperPrefabCache = LoadAsset<GameObject>(SniperPrefabPath);
                    return sniperPrefabCache;
                case WeaponKind.Pistol:
                    if (IsValidEquipPrefab(pistolPrefabCache, WeaponKind.Pistol))
                    {
                        return pistolPrefabCache;
                    }

                    pistolPrefabCache = LoadAsset<GameObject>(PistolPrefabPath);
                    return pistolPrefabCache;
                case WeaponKind.Mp7:
                    if (IsValidEquipPrefab(mp7PrefabCache, WeaponKind.Mp7))
                    {
                        return mp7PrefabCache;
                    }

                    mp7PrefabCache = LoadAsset<GameObject>(Mp7PrefabPath);
                    return mp7PrefabCache;
                default:
                    if (IsValidEquipPrefab(assaultPrefabCache, WeaponKind.AssaultRifle))
                    {
                        return assaultPrefabCache;
                    }

                    assaultPrefabCache = LoadAsset<GameObject>(AssaultPrefabPath);
                    return assaultPrefabCache;
            }
        }

        private static bool IsValidEquipPrefab(GameObject prefab, WeaponKind expectedKind)
        {
            if (!TryGetProfile(prefab, out var profile))
            {
                return false;
            }

            return profile.Kind == expectedKind;
        }

        public static string GetDefaultItemId(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.SniperRifle:
                    return "sniper_rifle";
                case WeaponKind.Pistol:
                    return "pistol";
                case WeaponKind.Mp7:
                    return "mp7";
                default:
                    return "assault_rifle";
            }
        }

        public static WeaponProfile GetProfileTemplate(WeaponKind kind)
        {
            var prefab = GetWeaponPrefab(kind);
            return prefab != null ? prefab.GetComponent<WeaponProfile>() : null;
        }

        public static GameObject LoadMuzzleFlash(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.SniperRifle:
                    return sniperMuzzleFlashCache != null
                        ? sniperMuzzleFlashCache
                        : sniperMuzzleFlashCache = LoadAsset<GameObject>(SniperMuzzleFlashPath);
                case WeaponKind.Pistol:
                    return pistolMuzzleFlashCache != null
                        ? pistolMuzzleFlashCache
                        : pistolMuzzleFlashCache = LoadAsset<GameObject>(PistolMuzzleFlashPath);
                case WeaponKind.Mp7:
                    return mp7MuzzleFlashCache != null
                        ? mp7MuzzleFlashCache
                        : mp7MuzzleFlashCache = LoadAsset<GameObject>(Mp7MuzzleFlashPath);
                default:
                    return assaultMuzzleFlashCache != null
                        ? assaultMuzzleFlashCache
                        : assaultMuzzleFlashCache = LoadAsset<GameObject>(AssaultMuzzleFlashPath);
            }
        }

        public static AudioClip LoadShotClip(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.SniperRifle:
                    return sniperShotCache != null
                        ? sniperShotCache
                        : sniperShotCache = LoadAsset<AudioClip>(SniperShotClipPath);
                case WeaponKind.Pistol:
                    return pistolShotCache != null
                        ? pistolShotCache
                        : pistolShotCache = LoadAsset<AudioClip>(PistolShotClipPath);
                case WeaponKind.Mp7:
                    return mp7ShotCache != null
                        ? mp7ShotCache
                        : mp7ShotCache = LoadAsset<AudioClip>(Mp7ShotClipPath);
                default:
                    return assaultShotCache != null
                        ? assaultShotCache
                        : assaultShotCache = LoadAsset<AudioClip>(AssaultShotClipPath);
            }
        }

        public static AudioClip LoadReloadPullClip(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.SniperRifle:
                    return sniperReloadPullCache != null
                        ? sniperReloadPullCache
                        : sniperReloadPullCache = LoadAsset<AudioClip>(SniperReloadPullPath);
                case WeaponKind.Pistol:
                    return pistolReloadPullCache != null
                        ? pistolReloadPullCache
                        : pistolReloadPullCache = LoadAsset<AudioClip>(PistolReloadPullPath);
                case WeaponKind.Mp7:
                    return mp7ReloadPullCache != null
                        ? mp7ReloadPullCache
                        : mp7ReloadPullCache = LoadAsset<AudioClip>(Mp7ReloadPullPath);
                default:
                    return assaultReloadPullCache != null
                        ? assaultReloadPullCache
                        : assaultReloadPullCache = LoadAsset<AudioClip>(AssaultReloadPullPath);
            }
        }

        public static AudioClip LoadReloadInsertClip(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.SniperRifle:
                    return sniperReloadInsertCache != null
                        ? sniperReloadInsertCache
                        : sniperReloadInsertCache = LoadAsset<AudioClip>(SniperReloadInsertPath);
                case WeaponKind.Pistol:
                    return pistolReloadInsertCache != null
                        ? pistolReloadInsertCache
                        : pistolReloadInsertCache = LoadAsset<AudioClip>(PistolReloadInsertPath);
                case WeaponKind.Mp7:
                    return mp7ReloadInsertCache != null
                        ? mp7ReloadInsertCache
                        : mp7ReloadInsertCache = LoadAsset<AudioClip>(Mp7ReloadInsertPath);
                default:
                    return assaultReloadInsertCache != null
                        ? assaultReloadInsertCache
                        : assaultReloadInsertCache = LoadAsset<AudioClip>(AssaultReloadInsertPath);
            }
        }

        private static T LoadAsset<T>(string assetPath) where T : Object
        {
#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<T>(assetPath);
#else
            return Resources.Load<T>(assetPath);
#endif
        }
    }
}
