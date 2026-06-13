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
        private const string AssaultMuzzleFlashPath =
            "Assets/JMO Assets/WarFX/_Effects/MuzzleFlashes/FPS/WFX_MF FPS RIFLE1.prefab";
        private const string SniperMuzzleFlashPath =
            "Assets/JMO Assets/WarFX/_Effects/MuzzleFlashes/FPS/WFX_MF FPS RIFLE3.prefab";
        private const string AssaultShotClipPath = "Assets/Scifi Guns SFX Pack/Gun4_2.wav";
        private const string SniperShotClipPath = "Assets/Scifi Guns SFX Pack/Gun5_2.wav";
        private const string AssaultReloadPullPath = "Assets/Scifi Guns SFX Pack/Gun4_load.wav";
        private const string SniperReloadPullPath = "Assets/Scifi Guns SFX Pack/Gun5_Load.wav";
        private const string AssaultReloadInsertPath =
            "Assets/Weapons of Choice FREE - Komposite Sound/GUN/Handling_Gun_01_Clip_In_SFX.wav";
        private const string SniperReloadInsertPath = "Assets/Scifi Guns SFX Pack/Gun4_load.wav";

        private static GameObject assaultPrefabCache;
        private static GameObject sniperPrefabCache;
        private static GameObject assaultMuzzleFlashCache;
        private static GameObject sniperMuzzleFlashCache;
        private static AudioClip assaultShotCache;
        private static AudioClip sniperShotCache;
        private static AudioClip assaultReloadPullCache;
        private static AudioClip sniperReloadPullCache;
        private static AudioClip assaultReloadInsertCache;
        private static AudioClip sniperReloadInsertCache;

        public static WeaponKind ResolveKindFromItemId(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return WeaponKind.AssaultRifle;
            }

            return itemId.IndexOf("sniper", System.StringComparison.OrdinalIgnoreCase) >= 0
                ? WeaponKind.SniperRifle
                : WeaponKind.AssaultRifle;
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
            return kind == WeaponKind.SniperRifle ? "sniper_rifle" : "assault_rifle";
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
