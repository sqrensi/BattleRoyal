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

        private const string PrefabRegistryResourcePath = "Weapons/WeaponPrefabRegistry";
        private const string EquipResourceRoot = "Weapons/Equip";

        private static GameObject[] equipPrefabsByKind;
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
        private static bool equipPrefabsInitialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            equipPrefabsByKind = null;
            equipPrefabsInitialized = false;
        }

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
            if (!IsAlive(prefab))
            {
                return false;
            }

            profile = prefab.GetComponent<WeaponProfile>() ??
                      prefab.GetComponentInChildren<WeaponProfile>(true);
            return profile != null;
        }

        public static bool IsFullWeaponPrefab(GameObject prefab) => TryGetProfile(prefab, out _);

        public static bool IsAlive(UnityEngine.Object unityObject)
        {
            if (ReferenceEquals(unityObject, null))
            {
                return false;
            }

            try
            {
                _ = unityObject.name;
                return true;
            }
            catch (MissingReferenceException)
            {
                return false;
            }
        }

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
            EnsureEquipPrefabsInitialized();
            if (!TryResolveEquipPrefab(kind, prefab, out var resolved))
            {
                return;
            }

            equipPrefabsByKind[(int)kind] = resolved;
        }

        public static void EnsureWeaponPrefabRegistered(WeaponKind kind)
        {
            EnsureEquipPrefabsInitialized();
            ResolveEquipPrefab(kind);
        }

        public static bool TryGetWeaponPrefab(WeaponKind kind, out GameObject prefab)
        {
            prefab = GetWeaponPrefab(kind);
            return prefab != null;
        }

        public static GameObject GetWeaponPrefab(WeaponKind kind)
        {
            EnsureEquipPrefabsInitialized();
            return ResolveEquipPrefab(kind);
        }

        public static void PrewarmAllWeaponPrefabs()
        {
            EnsureEquipPrefabsInitialized();
            for (var kindValue = 0; kindValue <= WeaponKindUtility.MaxKindId; kindValue++)
            {
                ResolveEquipPrefab((WeaponKind)kindValue);
            }
        }

#if UNITY_EDITOR
        public static void ResetEquipPrefabsForEditor()
        {
            equipPrefabsByKind = null;
            equipPrefabsInitialized = false;
        }
#endif

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

        public static string ResolveItemId(WeaponKind kind) => GetDefaultItemId(kind);

        public static WeaponProfile GetProfileTemplate(WeaponKind kind)
        {
            var prefab = GetWeaponPrefab(kind);
            return prefab != null && TryGetProfile(prefab, out var profile) ? profile : null;
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

        private static void EnsureEquipPrefabsInitialized()
        {
            if (equipPrefabsInitialized)
            {
                return;
            }

            equipPrefabsInitialized = true;
            equipPrefabsByKind = new GameObject[WeaponKindUtility.MaxKindId + 1];

            for (var kindValue = 0; kindValue <= WeaponKindUtility.MaxKindId; kindValue++)
            {
                var kind = (WeaponKind)kindValue;
                equipPrefabsByKind[kindValue] = LoadEquipPrefab(kind);
            }

            var loadedKinds = string.Empty;
            for (var kindValue = 0; kindValue <= WeaponKindUtility.MaxKindId; kindValue++)
            {
                var kind = (WeaponKind)kindValue;
                if (equipPrefabsByKind[kindValue] != null)
                {
                    loadedKinds += kind + "=" + equipPrefabsByKind[kindValue].name + "; ";
                    continue;
                }

                Debug.LogError(
                    $"[WeaponCatalog] Missing equip prefab for {kind}. " +
                    $"Run 'Shooter Prototype/Weapons/Setup Weapon Prefab Registry' or add " +
                    $"Resources/{EquipResourceRoot}/{GetDefaultItemId(kind)}.prefab");
            }

            if (!string.IsNullOrEmpty(loadedKinds))
            {
                Debug.Log($"[WeaponCatalog] Equip prefabs loaded: {loadedKinds}");
            }
        }

        private static GameObject LoadEquipPrefab(WeaponKind kind)
        {
#if UNITY_EDITOR
            var editorPrefab = LoadAsset<GameObject>(GetPrefabPath(kind));
            if (TryResolveEquipPrefab(kind, editorPrefab, out var editorResolved))
            {
                return editorResolved;
            }
#endif

            var resourcePrefab = Resources.Load<GameObject>($"{EquipResourceRoot}/{GetDefaultItemId(kind)}");
            if (TryResolveEquipPrefab(kind, resourcePrefab, out var resourceResolved))
            {
                return resourceResolved;
            }

            var registry = LoadRegistryAsset();
            if (registry != null &&
                TryResolveEquipPrefab(kind, registry.GetPrefab(kind), out var registryResolved))
            {
                return registryResolved;
            }

#if UNITY_EDITOR
            var legacyPrefab = LoadAsset<GameObject>(GetPrefabPath(kind));
            if (TryResolveEquipPrefab(kind, legacyPrefab, out var legacyResolved))
            {
                return legacyResolved;
            }
#endif

            return null;
        }

        private static WeaponPrefabRegistry LoadRegistryAsset()
        {
#if UNITY_EDITOR
            var editorRegistry = LoadAsset<WeaponPrefabRegistry>(
                $"Assets/Resources/{PrefabRegistryResourcePath}.asset");
            if (editorRegistry != null)
            {
                return editorRegistry;
            }
#endif

            return Resources.Load<WeaponPrefabRegistry>(PrefabRegistryResourcePath);
        }

        private static GameObject ResolveEquipPrefab(WeaponKind kind)
        {
            var kindIndex = (int)WeaponKindUtility.ClampKind((int)kind);
            return equipPrefabsByKind != null ? equipPrefabsByKind[kindIndex] : null;
        }

        private static bool TryResolveEquipPrefab(WeaponKind kind, GameObject prefab, out GameObject resolved)
        {
            resolved = null;
            if (prefab == null || !TryGetProfile(prefab, out var profile))
            {
                return false;
            }

            if (profile.Kind != kind)
            {
                Debug.LogWarning(
                    $"[WeaponCatalog] Prefab '{prefab.name}' profile kind {profile.Kind} " +
                    $"does not match requested {kind}.");
            }

            resolved = prefab;
            return true;
        }

        private static string GetPrefabPath(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.SniperRifle:
                    return SniperPrefabPath;
                case WeaponKind.Pistol:
                    return PistolPrefabPath;
                case WeaponKind.Mp7:
                    return Mp7PrefabPath;
                default:
                    return AssaultPrefabPath;
            }
        }

        private static T LoadAsset<T>(string assetPath) where T : Object
        {
#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<T>(assetPath);
#else
            return null;
#endif
        }
    }
}
