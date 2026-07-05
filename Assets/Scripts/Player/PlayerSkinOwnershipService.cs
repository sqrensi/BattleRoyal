using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class PlayerSkinOwnershipService
    {
        private const string OwnedPrefPrefix = "player_skin_owned_";
        private const string OwnedCountPrefPrefix = "player_skin_count_";
        private const string OwnershipInitKey = "player_skin_ownership_initialized_v1";
        public static event Action OwnershipChanged;
        public static event Action EquipmentChanged;

        public static void EnsureInitialized()
        {
            ShopCatalogService.EnsureLoaded();
            EnsureDefaultWeaponOwnership();

            if (UserScopedPlayerPrefs.HasKey(OwnershipInitKey))
            {
                return;
            }

            var defaultOwned = ShopCatalogService.GetDefaultOwnedSkinIds();
            for (var i = 0; i < defaultOwned.Count; i++)
            {
                MarkOwned(defaultOwned[i], persist: false);
            }

            EnsureDefaultEquipped();
            UserScopedPlayerPrefs.SetInt(OwnershipInitKey, 1);
            PlayerPrefs.Save();
            OwnershipChanged?.Invoke();
        }

        public static bool IsOwned(string skinId)
        {
            if (string.IsNullOrWhiteSpace(skinId))
            {
                return false;
            }

            if (PlayerProfileService.IsServerSynced)
            {
                return PlayerProfileService.IsOwned(skinId);
            }

            return UserScopedPlayerPrefs.GetInt(OwnedPrefPrefix + skinId, 0) == 1 ||
                   GetOwnedCount(skinId) > 0;
        }

        public static int GetOwnedCount(string skinId)
        {
            if (string.IsNullOrWhiteSpace(skinId))
            {
                return 0;
            }

            if (PlayerProfileService.IsServerSynced)
            {
                return PlayerProfileService.GetOwnedQuantity(skinId);
            }

            var count = UserScopedPlayerPrefs.GetInt(OwnedCountPrefPrefix + skinId, 0);
            if (count > 0)
            {
                return count;
            }

            return UserScopedPlayerPrefs.GetInt(OwnedPrefPrefix + skinId, 0) == 1 ? 1 : 0;
        }

        public static void GrantOwnedSkin(string skinId, bool allowDuplicate = false)
        {
            if (string.IsNullOrWhiteSpace(skinId))
            {
                return;
            }

            if (PlayerProfileService.IsServerSynced)
            {
                return;
            }

            GrantOwnedSkinLocal(skinId, allowDuplicate);
        }

        public static void GrantDailyRewardSkin(string skinId, bool allowDuplicate = true)
        {
            if (string.IsNullOrWhiteSpace(skinId))
            {
                return;
            }

            EnsureInitialized();
            GrantOwnedSkinLocal(skinId, allowDuplicate);
            if (PlayerProfileService.IsServerSynced)
            {
                PlayerProfileService.ApplyLocalOwnedSkinGrant(skinId, allowDuplicate);
            }
        }

        private static void GrantOwnedSkinLocal(string skinId, bool allowDuplicate)
        {
            var normalized = skinId.Trim();
            var currentCount = GetOwnedCount(normalized);
            if (currentCount <= 0)
            {
                MarkOwned(normalized, persist: true, quantity: 1);
                return;
            }

            if (!allowDuplicate)
            {
                return;
            }

            MarkOwned(normalized, persist: true, quantity: currentCount + 1);
        }

        public static void ApplyOwnedQuantityFromServer(string skinId, int quantity)
        {
            if (string.IsNullOrWhiteSpace(skinId))
            {
                return;
            }

            var normalized = skinId.Trim();
            var safeQuantity = Mathf.Max(1, quantity);
            UserScopedPlayerPrefs.SetInt(OwnedPrefPrefix + normalized, 1);
            UserScopedPlayerPrefs.SetInt(OwnedCountPrefPrefix + normalized, safeQuantity);
        }

        public static bool IsOwned(PlayerSkinDefinition item)
        {
            return item.IsValid && IsOwned(item.Id);
        }

        public static IReadOnlyList<PlayerSkinDefinition> GetOwnedCatalogItems()
        {
            var owned = new List<PlayerSkinDefinition>(32);
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AppendOwnedFromSlots(owned);
            for (var i = 0; i < owned.Count; i++)
            {
                if (owned[i].IsValid)
                {
                    seenIds.Add(owned[i].Id);
                }
            }

            AppendMissingOwnedDefinitions(owned, seenIds);
            for (var i = owned.Count - 1; i >= 0; i--)
            {
                if (!ShouldShowInInventory(owned[i]))
                {
                    owned.RemoveAt(i);
                }
            }

            return owned;
        }

        public static bool IsShopCatalogItem(PlayerSkinDefinition item)
        {
            return ShopCatalogService.IsShopItem(item);
        }

        public static IReadOnlyList<PlayerSkinDefinition> GetShopCatalogItems()
        {
            return ShopCatalogService.GetShopDefinitions();
        }

        public static int GetShopPrice(PlayerSkinDefinition item)
        {
            return ShopCatalogService.GetPrice(item);
        }

        public static bool TryPurchase(PlayerSkinDefinition item)
        {
            if (!item.IsValid || IsOwned(item))
            {
                return false;
            }

            if (PlayerProfileService.IsServerSynced)
            {
                return false;
            }

            var price = GetShopPrice(item);
            if (!PlayerCurrencyService.TrySpend(price))
            {
                return false;
            }

            MarkOwned(item.Id);
            return true;
        }

        public static void MarkOwnedFromServer(string skinId)
        {
            MarkOwned(skinId, persist: false);
        }

        public static void NotifyOwnershipChanged()
        {
            OwnershipChanged?.Invoke();
        }

        public static void NotifyEquipmentChanged()
        {
            EquipmentChanged?.Invoke();
        }

        public static IReadOnlyList<PlayerSkinDefinition> GetOwnedOptions(PlayerSkinSlot slot)
        {
            var owned = new List<PlayerSkinDefinition>(8);
            var options = PlayerSkinSelectionService.GetCatalogOptions(slot);
            for (var i = 0; i < options.Count; i++)
            {
                if (IsOwned(options[i]))
                {
                    owned.Add(options[i]);
                }
            }

            return owned;
        }

        public static bool TryGetDefaultEquipped(PlayerSkinSlot slot, out PlayerSkinDefinition definition)
        {
            if (ShopCatalogService.TryGetDefaultEquippedSkinId(slot, out var skinId) &&
                PlayerSkinSelectionService.TryGetDefinitionById(skinId, out definition) &&
                IsOwned(definition))
            {
                return true;
            }

            var owned = GetOwnedOptions(slot);
            if (owned.Count > 0)
            {
                definition = owned[0];
                return true;
            }

            definition = default;
            return false;
        }

        private static void AppendOwnedFromSlots(PlayerSkinSlot[] order, List<PlayerSkinDefinition> owned)
        {
            for (var i = 0; i < order.Length; i++)
            {
                var options = PlayerSkinSelectionService.GetCatalogOptions(order[i]);
                for (var j = 0; j < options.Count; j++)
                {
                    if (!IsOwned(options[j]))
                    {
                        continue;
                    }

                    if (options[j].IsWeaponSkin && IsDefaultOwnedSkin(options[j].Id))
                    {
                        continue;
                    }

                    owned.Add(options[j]);
                }
            }
        }

        private static void AppendOwnedFromSlots(List<PlayerSkinDefinition> owned)
        {
            AppendOwnedFromSlots(PlayerSkinSelectionService.GetDisplaySlotOrder(), owned);
        }

        private static void AppendMissingOwnedDefinitions(
            List<PlayerSkinDefinition> owned,
            HashSet<string> seenIds)
        {
            if (PlayerProfileService.IsServerSynced)
            {
                var ownedSkinIds = PlayerProfileService.GetOwnedSkinIds();
                for (var i = 0; i < ownedSkinIds.Count; i++)
                {
                    TryAppendOwnedDefinition(owned, seenIds, ownedSkinIds[i]);
                }

                return;
            }

            var shopItems = ShopCatalogService.GetShopDefinitions();
            for (var i = 0; i < shopItems.Count; i++)
            {
                var item = shopItems[i];
                if (item.IsValid && IsOwned(item.Id))
                {
                    TryAppendOwnedDefinition(owned, seenIds, item.Id);
                }
            }
        }

        private static void TryAppendOwnedDefinition(
            List<PlayerSkinDefinition> owned,
            HashSet<string> seenIds,
            string skinId)
        {
            if (string.IsNullOrWhiteSpace(skinId) || seenIds.Contains(skinId) || !IsOwned(skinId))
            {
                return;
            }

            if (PlayerSkinSelectionService.TryGetDefinitionById(skinId, out var definition) &&
                definition.IsValid)
            {
                if (!ShouldShowInInventory(definition))
                {
                    return;
                }

                owned.Add(definition);
                seenIds.Add(skinId);
                return;
            }

            if (ShopCatalogService.TryGetSkinDefinition(skinId, out definition) &&
                definition.IsValid)
            {
                if (!ShouldShowInInventory(definition))
                {
                    return;
                }

                owned.Add(definition);
                seenIds.Add(skinId);
            }
        }

        public static bool ShouldShowInInventory(PlayerSkinDefinition item)
        {
            if (!item.IsValid || !IsOwned(item.Id))
            {
                return false;
            }

            return !(item.IsWeaponSkin && IsDefaultOwnedSkin(item.Id));
        }

        private static bool IsDefaultOwnedSkin(string skinId)
        {
            return ShopCatalogService.IsDefaultOwnedSkin(skinId);
        }

        private static void MarkOwned(string skinId, bool persist = true, int quantity = 1)
        {
            if (string.IsNullOrWhiteSpace(skinId))
            {
                return;
            }

            var normalized = skinId.Trim();
            var safeQuantity = Mathf.Max(1, quantity);
            UserScopedPlayerPrefs.SetInt(OwnedPrefPrefix + normalized, 1);
            UserScopedPlayerPrefs.SetInt(OwnedCountPrefPrefix + normalized, safeQuantity);
            if (persist)
            {
                PlayerPrefs.Save();
                OwnershipChanged?.Invoke();
            }
        }

        private static void EnsureDefaultEquipped()
        {
            foreach (PlayerSkinSlot slot in Enum.GetValues(typeof(PlayerSkinSlot)))
            {
                if (!TryGetDefaultEquipped(slot, out var definition))
                {
                    continue;
                }

                PlayerSkinSelectionService.SaveSelected(slot, definition.Id);
            }
        }

        private static void EnsureDefaultWeaponOwnership()
        {
            var weaponDefaults = ShopCatalogService.GetDefaultOwnedSkinIds();
            for (var i = 0; i < weaponDefaults.Count; i++)
            {
                var skinId = weaponDefaults[i];
                if (string.IsNullOrWhiteSpace(skinId) ||
                    skinId.IndexOf("weapon_", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                MarkOwned(skinId, persist: false);
            }

            foreach (PlayerSkinSlot slot in Enum.GetValues(typeof(PlayerSkinSlot)))
            {
                if (!WeaponSkinResourcePaths.IsWeaponSkinSlot(slot))
                {
                    continue;
                }

                if (UserScopedPlayerPrefs.HasKey(BuildEquippedPrefKey(slot)))
                {
                    continue;
                }

                if (TryGetDefaultEquipped(slot, out var definition))
                {
                    PlayerSkinSelectionService.SaveSelected(slot, definition.Id);
                }
            }

            PlayerPrefs.Save();
        }

        private static string BuildEquippedPrefKey(PlayerSkinSlot slot)
        {
            return "player_skin_" + slot.ToString().ToLowerInvariant();
        }
    }
}
