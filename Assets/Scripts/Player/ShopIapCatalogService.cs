using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public readonly struct ShopIapProductDefinition
    {
        public ShopIapProductDefinition(
            string productId,
            string displayName,
            string description,
            int priceRubles,
            string rewardType,
            int amount,
            string caseId)
        {
            ProductId = productId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Description = description ?? string.Empty;
            PriceRubles = Mathf.Max(0, priceRubles);
            RewardType = rewardType ?? string.Empty;
            Amount = Mathf.Max(0, amount);
            CaseId = caseId ?? string.Empty;
        }

        public string ProductId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public int PriceRubles { get; }
        public string RewardType { get; }
        public int Amount { get; }
        public string CaseId { get; }

        public bool IsValid => !string.IsNullOrWhiteSpace(ProductId);
        public bool IsCurrencyReward =>
            string.Equals(RewardType, "currency", StringComparison.OrdinalIgnoreCase);
        public bool IsCaseReward =>
            string.Equals(RewardType, "case", StringComparison.OrdinalIgnoreCase);
        public bool IsVipPrefixReward =>
            string.Equals(RewardType, "vip_prefix", StringComparison.OrdinalIgnoreCase);
    }

    public static class ShopIapCatalogService
    {
        private const string CatalogResourcePath = "Shop/shop-iap-catalog";

        private static ShopIapCatalogData cachedCatalog;
        private static Dictionary<string, ShopIapProductDefinition> productsById;

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
                    $"[ShopIapCatalogService] Missing catalog at Resources/{CatalogResourcePath}.json");
                cachedCatalog = new ShopIapCatalogData { products = Array.Empty<ShopIapCatalogEntry>() };
            }
            else
            {
                cachedCatalog = JsonUtility.FromJson<ShopIapCatalogData>(asset.text);
                if (cachedCatalog == null)
                {
                    cachedCatalog = new ShopIapCatalogData { products = Array.Empty<ShopIapCatalogEntry>() };
                }
            }

            productsById = new Dictionary<string, ShopIapProductDefinition>(StringComparer.OrdinalIgnoreCase);
            if (cachedCatalog.products == null)
            {
                return;
            }

            for (var i = 0; i < cachedCatalog.products.Length; i++)
            {
                var entry = cachedCatalog.products[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.productId))
                {
                    continue;
                }

                var definition = new ShopIapProductDefinition(
                    entry.productId.Trim(),
                    entry.displayName,
                    entry.description,
                    entry.priceRubles,
                    entry.rewardType,
                    entry.amount,
                    entry.caseId);
                productsById[definition.ProductId] = definition;
            }
        }

        public static IReadOnlyList<ShopIapProductDefinition> GetCurrencyProducts()
        {
            EnsureLoaded();
            var results = new List<ShopIapProductDefinition>(4);
            if (cachedCatalog.products == null)
            {
                return results;
            }

            for (var i = 0; i < cachedCatalog.products.Length; i++)
            {
                var entry = cachedCatalog.products[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.productId))
                {
                    continue;
                }

                if (!productsById.TryGetValue(entry.productId.Trim(), out var definition) ||
                    !definition.IsCurrencyReward)
                {
                    continue;
                }

                results.Add(definition);
            }

            return results;
        }

        public static IReadOnlyList<ShopIapProductDefinition> GetPremiumProducts()
        {
            EnsureLoaded();
            var results = new List<ShopIapProductDefinition>(4);
            if (cachedCatalog.products == null)
            {
                return results;
            }

            for (var i = 0; i < cachedCatalog.products.Length; i++)
            {
                var entry = cachedCatalog.products[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.productId))
                {
                    continue;
                }

                if (!productsById.TryGetValue(entry.productId.Trim(), out var definition) ||
                    definition.IsCurrencyReward)
                {
                    continue;
                }

                results.Add(definition);
            }

            return results;
        }

        public static bool TryGetProduct(string productId, out ShopIapProductDefinition definition)
        {
            EnsureLoaded();
            definition = default;
            if (string.IsNullOrWhiteSpace(productId))
            {
                return false;
            }

            return productsById.TryGetValue(productId.Trim(), out definition) && definition.IsValid;
        }

        public static bool TryApplyLocalReward(string productId)
        {
            if (!TryGetProduct(productId, out var product))
            {
                return false;
            }

            if (product.IsCurrencyReward)
            {
                PlayerCurrencyService.AddCurrency(product.Amount);
                return true;
            }

            if (product.IsCaseReward && !string.IsNullOrWhiteSpace(product.CaseId))
            {
                CaseOpeningService.GrantLocalCase(product.CaseId.Trim(), Mathf.Max(1, product.Amount));
                return true;
            }

            if (product.IsVipPrefixReward)
            {
                return PlayerProfileService.GrantLocalVipPrefix();
            }

            return false;
        }

        public static string FormatRubles(int rubles)
        {
            return $"{rubles.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))} руб.";
        }

        [Serializable]
        private sealed class ShopIapCatalogData
        {
            public ShopIapCatalogEntry[] products;
        }

        [Serializable]
        private sealed class ShopIapCatalogEntry
        {
            public string productId;
            public string displayName;
            public string description;
            public int priceRubles;
            public string rewardType;
            public int amount;
            public string caseId;
        }
    }
}
