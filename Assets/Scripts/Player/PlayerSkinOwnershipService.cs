using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class PlayerSkinOwnershipService
    {
        private const string OwnedPrefPrefix = "player_skin_owned_";
        private const string OwnershipInitKey = "player_skin_ownership_initialized_v1";
        private const string CurrencyGrantKey = "player_currency_grant_100k_v1";
        private const int ShopBasePrice = 500;
        private const int ShopPriceStep = 250;

        private static readonly string[] DefaultOwnedSkinIds =
        {
            PlayerSkinResourcePaths.BuildSkinId("tshirts", "001"),
            PlayerSkinResourcePaths.BuildSkinId("pants", "001"),
            PlayerSkinResourcePaths.BuildSkinId("shoes", "001"),
            PlayerSkinResourcePaths.BuildSkinId("gloves", "001"),
            PlayerSkinResourcePaths.BuildAttachmentSkinId("face", "001"),
            PlayerSkinResourcePaths.BuildAttachmentSkinId("hair", "003")
        };

        private static readonly Dictionary<PlayerSkinSlot, string> DefaultEquippedSkinIds =
            new Dictionary<PlayerSkinSlot, string>
            {
                { PlayerSkinSlot.Shirt, PlayerSkinResourcePaths.BuildSkinId("tshirts", "001") },
                { PlayerSkinSlot.Pants, PlayerSkinResourcePaths.BuildSkinId("pants", "001") },
                { PlayerSkinSlot.Boots, PlayerSkinResourcePaths.BuildSkinId("shoes", "001") },
                { PlayerSkinSlot.Gloves, PlayerSkinResourcePaths.BuildSkinId("gloves", "001") },
                { PlayerSkinSlot.Face, PlayerSkinResourcePaths.BuildAttachmentSkinId("face", "001") },
                { PlayerSkinSlot.Hair, PlayerSkinResourcePaths.BuildAttachmentSkinId("hair", "003") }
            };

        private static readonly Dictionary<string, int> ShopPriceBySkinId = BuildShopPrices();

        public static event Action OwnershipChanged;

        public static void EnsureInitialized()
        {
            if (!PlayerPrefs.HasKey(CurrencyGrantKey))
            {
                PlayerCurrencyService.AddCurrency(100000);
                PlayerPrefs.SetInt(CurrencyGrantKey, 1);
                PlayerPrefs.Save();
            }

            if (PlayerPrefs.HasKey(OwnershipInitKey))
            {
                return;
            }

            for (var i = 0; i < DefaultOwnedSkinIds.Length; i++)
            {
                MarkOwned(DefaultOwnedSkinIds[i], persist: false);
            }

            EnsureDefaultEquipped();
            PlayerPrefs.SetInt(OwnershipInitKey, 1);
            PlayerPrefs.Save();
            OwnershipChanged?.Invoke();
        }

        public static bool IsOwned(string skinId)
        {
            if (string.IsNullOrWhiteSpace(skinId))
            {
                return false;
            }

            return PlayerPrefs.GetInt(OwnedPrefPrefix + skinId, 0) == 1;
        }

        public static bool IsOwned(PlayerSkinDefinition item)
        {
            return item.IsValid && IsOwned(item.Id);
        }

        public static IReadOnlyList<PlayerSkinDefinition> GetOwnedCatalogItems()
        {
            var owned = new List<PlayerSkinDefinition>(32);
            AppendOwnedFromSlots(owned);
            return owned;
        }

        public static bool IsShopCatalogItem(PlayerSkinDefinition item)
        {
            return item.IsValid && !IsDefaultOwnedSkin(item.Id);
        }

        public static IReadOnlyList<PlayerSkinDefinition> GetShopCatalogItems()
        {
            var shopItems = new List<PlayerSkinDefinition>(64);
            AppendShopFromSlots(shopItems);
            shopItems.Sort(CompareShopItems);
            return shopItems;
        }

        public static int GetShopPrice(PlayerSkinDefinition item)
        {
            if (!item.IsValid)
            {
                return 0;
            }

            return ShopPriceBySkinId.TryGetValue(item.Id, out var price) ? price : ShopBasePrice;
        }

        public static bool TryPurchase(PlayerSkinDefinition item)
        {
            if (!item.IsValid || IsOwned(item))
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
            if (DefaultEquippedSkinIds.TryGetValue(slot, out var skinId) &&
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
                    if (IsOwned(options[j]))
                    {
                        owned.Add(options[j]);
                    }
                }
            }
        }

        private static void AppendOwnedFromSlots(List<PlayerSkinDefinition> owned)
        {
            AppendOwnedFromSlots(PlayerSkinSelectionService.GetDisplaySlotOrder(), owned);
        }

        private static void AppendShopFromSlots(List<PlayerSkinDefinition> shopItems)
        {
            var order = PlayerSkinSelectionService.GetDisplaySlotOrder();
            for (var i = 0; i < order.Length; i++)
            {
                var options = PlayerSkinSelectionService.GetCatalogOptions(order[i]);
                for (var j = 0; j < options.Count; j++)
                {
                    if (IsShopCatalogItem(options[j]))
                    {
                        shopItems.Add(options[j]);
                    }
                }
            }
        }

        private static int CompareShopItems(PlayerSkinDefinition a, PlayerSkinDefinition b)
        {
            var ownedCompare = IsOwned(a).CompareTo(IsOwned(b));
            if (ownedCompare != 0)
            {
                return ownedCompare;
            }

            var priceCompare = GetShopPrice(a).CompareTo(GetShopPrice(b));
            if (priceCompare != 0)
            {
                return priceCompare;
            }

            return string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, int> BuildShopPrices()
        {
            var prices = new Dictionary<string, int>(64);
            var index = 0;
            var order = PlayerSkinSelectionService.GetDisplaySlotOrder();
            for (var i = 0; i < order.Length; i++)
            {
                var options = PlayerSkinSelectionService.GetCatalogOptions(order[i]);
                for (var j = 0; j < options.Count; j++)
                {
                    var item = options[j];
                    if (IsDefaultOwnedSkin(item.Id))
                    {
                        continue;
                    }

                    prices[item.Id] = ShopBasePrice + index * ShopPriceStep;
                    index++;
                }
            }

            return prices;
        }

        private static bool IsDefaultOwnedSkin(string skinId)
        {
            for (var i = 0; i < DefaultOwnedSkinIds.Length; i++)
            {
                if (string.Equals(DefaultOwnedSkinIds[i], skinId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void MarkOwned(string skinId, bool persist = true)
        {
            if (string.IsNullOrWhiteSpace(skinId))
            {
                return;
            }

            PlayerPrefs.SetInt(OwnedPrefPrefix + skinId, 1);
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
    }
}
