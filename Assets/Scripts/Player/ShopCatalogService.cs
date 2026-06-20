using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Data-driven shop catalog loaded from Resources/Shop/skin-catalog.json.
    /// Add new skins to Resources folders, then append an entry to the JSON with price.
    /// </summary>
    public static class ShopCatalogService
    {
        private const string CatalogResourcePath = "Shop/skin-catalog";

        private static ShopCatalogData cachedCatalog;
        private static Dictionary<string, int> priceBySkinId;
        private static Dictionary<string, SkinRarity> rarityBySkinId;
        private static HashSet<string> shopSkinIds;
        private static HashSet<string> defaultOwnedSkinIds;

        public static void EnsureLoaded()
        {
            if (cachedCatalog != null)
            {
                return;
            }

            var asset = Resources.Load<TextAsset>(CatalogResourcePath);
            if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            {
                Debug.LogError(
                    $"[ShopCatalogService] Missing catalog at Resources/{CatalogResourcePath}.json");
                cachedCatalog = new ShopCatalogData
                {
                    defaultOwnedSkinIds = Array.Empty<string>(),
                    shopItems = Array.Empty<ShopCatalogItemEntry>()
                };
            }
            else
            {
                cachedCatalog = JsonUtility.FromJson<ShopCatalogData>(asset.text);
                if (cachedCatalog == null)
                {
                    Debug.LogError("[ShopCatalogService] Failed to parse skin catalog JSON.");
                    cachedCatalog = new ShopCatalogData
                    {
                        defaultOwnedSkinIds = Array.Empty<string>(),
                        shopItems = Array.Empty<ShopCatalogItemEntry>()
                    };
                }
            }

            priceBySkinId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            rarityBySkinId = new Dictionary<string, SkinRarity>(StringComparer.OrdinalIgnoreCase);
            shopSkinIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            defaultOwnedSkinIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (cachedCatalog.defaultOwnedSkinIds != null)
            {
                for (var i = 0; i < cachedCatalog.defaultOwnedSkinIds.Length; i++)
                {
                    var skinId = cachedCatalog.defaultOwnedSkinIds[i];
                    if (!string.IsNullOrWhiteSpace(skinId))
                    {
                        defaultOwnedSkinIds.Add(skinId.Trim());
                    }
                }
            }

            if (cachedCatalog.shopItems != null)
            {
                for (var i = 0; i < cachedCatalog.shopItems.Length; i++)
                {
                    var entry = cachedCatalog.shopItems[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.skinId))
                    {
                        continue;
                    }

                    var skinId = entry.skinId.Trim();
                    shopSkinIds.Add(skinId);
                    priceBySkinId[skinId] = Mathf.Max(0, entry.price);
                }
            }

            if (cachedCatalog.skinRarities != null)
            {
                for (var i = 0; i < cachedCatalog.skinRarities.Length; i++)
                {
                    var entry = cachedCatalog.skinRarities[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.skinId))
                    {
                        continue;
                    }

                    rarityBySkinId[entry.skinId.Trim()] = SkinRarityUtility.ParseOrDefault(entry.rarity);
                }
            }
        }

        public static SkinRarity GetRarity(string skinId)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(skinId))
            {
                return SkinRarity.Common;
            }

            return rarityBySkinId.TryGetValue(skinId.Trim(), out var rarity)
                ? rarity
                : SkinRarity.Common;
        }

        public static Color GetRarityCardColor(string skinId)
        {
            return SkinRarityUtility.GetCardBackgroundColor(GetRarity(skinId));
        }

        public static IReadOnlyList<string> GetDefaultOwnedSkinIds()
        {
            EnsureLoaded();
            if (cachedCatalog.defaultOwnedSkinIds == null || cachedCatalog.defaultOwnedSkinIds.Length == 0)
            {
                return Array.Empty<string>();
            }

            return cachedCatalog.defaultOwnedSkinIds;
        }

        public static bool IsDefaultOwnedSkin(string skinId)
        {
            EnsureLoaded();
            return !string.IsNullOrWhiteSpace(skinId) && defaultOwnedSkinIds.Contains(skinId.Trim());
        }

        public static bool IsShopSkin(string skinId)
        {
            EnsureLoaded();
            return !string.IsNullOrWhiteSpace(skinId) && shopSkinIds.Contains(skinId.Trim());
        }

        public static bool IsShopItem(PlayerSkinDefinition item)
        {
            return item.IsValid && IsShopSkin(item.Id);
        }

        public static int GetPrice(string skinId)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(skinId))
            {
                return 0;
            }

            return priceBySkinId.TryGetValue(skinId.Trim(), out var price) ? price : 0;
        }

        public static int GetPrice(PlayerSkinDefinition item)
        {
            return item.IsValid ? GetPrice(item.Id) : 0;
        }

        public static IReadOnlyList<PlayerSkinDefinition> GetShopDefinitions()
        {
            EnsureLoaded();
            var results = new List<PlayerSkinDefinition>(cachedCatalog.shopItems?.Length ?? 0);
            if (cachedCatalog.shopItems == null)
            {
                return results;
            }

            for (var i = 0; i < cachedCatalog.shopItems.Length; i++)
            {
                var entry = cachedCatalog.shopItems[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.skinId))
                {
                    continue;
                }

                var skinId = entry.skinId.Trim();
                if (IsDefaultOwnedSkin(skinId))
                {
                    continue;
                }

                if (!TryResolveShopDefinition(skinId, out var definition))
                {
                    continue;
                }

                if (!HasResourcePicture(definition.PictureResourcePath))
                {
                    continue;
                }

                results.Add(definition);
            }

            results.Sort(CompareShopDefinitions);
            return results;
        }

        public static bool TryGetSkinDefinition(string skinId, out PlayerSkinDefinition definition)
        {
            EnsureLoaded();
            return TryResolveShopDefinition(skinId, out definition) && definition.IsValid;
        }

        private static bool TryResolveShopDefinition(string skinId, out PlayerSkinDefinition definition)
        {
            if (PlayerSkinSelectionService.TryGetDefinitionById(skinId, out definition) &&
                definition.IsValid)
            {
                return true;
            }

            return TryBuildSyntheticShopDefinition(skinId, out definition);
        }

        private static bool HasResourcePicture(string pictureResourcePath)
        {
            if (string.IsNullOrWhiteSpace(pictureResourcePath))
            {
                return false;
            }

            return Resources.Load<Sprite>(pictureResourcePath) != null ||
                   Resources.Load<Texture2D>(pictureResourcePath) != null;
        }

        private static bool TryBuildSyntheticShopDefinition(string skinId, out PlayerSkinDefinition definition)
        {
            definition = default;
            var normalized = (skinId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            if (normalized.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase) &&
                WeaponSkinResourcePaths.TryParseSkinId(normalized, out var weaponKind, out var weaponVariant))
            {
                if (weaponVariant == "000" || IsDefaultOwnedSkin(normalized))
                {
                    return false;
                }

                var picturePath = WeaponSkinResourcePaths.BuildPicturePath(weaponKind, weaponVariant);
                if (!HasResourcePicture(picturePath))
                {
                    return false;
                }

                var materialPath = WeaponSkinResourcePaths.BuildMaterialPath(weaponKind, weaponVariant);
                definition = new PlayerSkinDefinition(
                    normalized,
                    BuildShopDisplayName(normalized),
                    string.Empty,
                    materialPath,
                    picturePath,
                    weaponKind);
                return true;
            }

            if (normalized.StartsWith("attachment_", StringComparison.OrdinalIgnoreCase))
            {
                var parts = normalized.Split('_');
                if (parts.Length >= 3)
                {
                    var category = parts[1];
                    var variant = parts[2];
                    var picturePath = PlayerSkinResourcePaths.BuildAttachmentPicturePath(category, variant);
                    if (!HasResourcePicture(picturePath))
                    {
                        return false;
                    }

                    definition = new PlayerSkinDefinition(
                        normalized,
                        BuildShopDisplayName(normalized),
                        PlayerSkinResourcePaths.BuildAttachmentPrefabPath(category, variant),
                        string.Empty,
                        picturePath);
                    return true;
                }

                return false;
            }

            var separatorIndex = normalized.LastIndexOf('_');
            if (separatorIndex <= 0 || separatorIndex >= normalized.Length - 1)
            {
                return false;
            }

            var categoryFolder = normalized.Substring(0, separatorIndex);
            var variantId = normalized.Substring(separatorIndex + 1);
            var clothingPicturePath = PlayerSkinResourcePaths.BuildPicturePath(categoryFolder, variantId);
            if (!HasResourcePicture(clothingPicturePath))
            {
                return false;
            }

            var prefabPath = PlayerSkinResourcePaths.ResolveClothingPrefabPath(categoryFolder, variantId);
            if (string.IsNullOrWhiteSpace(prefabPath))
            {
                return false;
            }

            definition = new PlayerSkinDefinition(
                normalized,
                BuildShopDisplayName(normalized),
                prefabPath,
                PlayerSkinResourcePaths.ResolveClothingMaterialPath(categoryFolder, variantId),
                clothingPicturePath);
            return true;
        }

        private static string BuildShopDisplayName(string skinId)
        {
            return skinId.Replace('_', ' ');
        }

        public static bool TryGetDefaultEquippedSkinId(PlayerSkinSlot slot, out string skinId)
        {
            EnsureLoaded();
            skinId = string.Empty;
            if (cachedCatalog.defaultEquipped == null)
            {
                return false;
            }

            switch (slot)
            {
                case PlayerSkinSlot.Shirt:
                    skinId = cachedCatalog.defaultEquipped.shirt;
                    break;
                case PlayerSkinSlot.Pants:
                    skinId = cachedCatalog.defaultEquipped.pants;
                    break;
                case PlayerSkinSlot.Boots:
                    skinId = cachedCatalog.defaultEquipped.boots;
                    break;
                case PlayerSkinSlot.Gloves:
                    skinId = cachedCatalog.defaultEquipped.gloves;
                    break;
                case PlayerSkinSlot.Face:
                    skinId = cachedCatalog.defaultEquipped.face;
                    break;
                case PlayerSkinSlot.Hair:
                    skinId = cachedCatalog.defaultEquipped.hair;
                    break;
                case PlayerSkinSlot.WeaponAssaultRifle:
                    skinId = cachedCatalog.defaultEquipped.weaponAssault;
                    break;
                case PlayerSkinSlot.WeaponSniperRifle:
                    skinId = cachedCatalog.defaultEquipped.weaponSniper;
                    break;
                case PlayerSkinSlot.WeaponPistol:
                    skinId = cachedCatalog.defaultEquipped.weaponPistol;
                    break;
                case PlayerSkinSlot.WeaponMp7:
                    skinId = cachedCatalog.defaultEquipped.weaponMp7;
                    break;
                default:
                    return false;
            }

            return !string.IsNullOrWhiteSpace(skinId);
        }

        private static int CompareShopDefinitions(PlayerSkinDefinition a, PlayerSkinDefinition b)
        {
            var ownedCompare = PlayerSkinOwnershipService.IsOwned(a).CompareTo(PlayerSkinOwnershipService.IsOwned(b));
            if (ownedCompare != 0)
            {
                return ownedCompare;
            }

            var priceCompare = GetPrice(a).CompareTo(GetPrice(b));
            if (priceCompare != 0)
            {
                return priceCompare;
            }

            return string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
        }

        [Serializable]
        private sealed class ShopCatalogData
        {
            public string[] defaultOwnedSkinIds;
            public ShopCatalogDefaultEquipped defaultEquipped;
            public ShopCatalogItemEntry[] shopItems;
            public ShopCatalogRarityEntry[] skinRarities;
        }

        [Serializable]
        private sealed class ShopCatalogRarityEntry
        {
            public string skinId;
            public string rarity;
        }

        [Serializable]
        private sealed class ShopCatalogDefaultEquipped
        {
            public string shirt;
            public string pants;
            public string boots;
            public string gloves;
            public string face;
            public string hair;
            public string weaponAssault;
            public string weaponSniper;
            public string weaponPistol;
            public string weaponMp7;
        }

        [Serializable]
        private sealed class ShopCatalogItemEntry
        {
            public string skinId;
            public int price;
        }
    }
}
