using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public readonly struct CaseLootEntry
    {
        public CaseLootEntry(string skinId, int weight)
        {
            SkinId = skinId ?? string.Empty;
            Weight = Mathf.Max(0, weight);
        }

        public string SkinId { get; }
        public int Weight { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(SkinId) && Weight > 0;
    }

    public readonly struct CaseDefinition
    {
        public CaseDefinition(
            string id,
            string displayName,
            string pictureResourcePath,
            int price,
            IReadOnlyList<CaseLootEntry> lootPool,
            string realMoneyProductId = "",
            int priceRubles = 0,
            bool excludeFromRewards = false,
            bool isDonateExclusive = false)
        {
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            PictureResourcePath = pictureResourcePath ?? string.Empty;
            Price = Mathf.Max(0, price);
            LootPool = lootPool ?? Array.Empty<CaseLootEntry>();
            RealMoneyProductId = realMoneyProductId ?? string.Empty;
            PriceRubles = Mathf.Max(0, priceRubles);
            ExcludeFromRewards = excludeFromRewards;
            IsDonateExclusive = isDonateExclusive;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string PictureResourcePath { get; }
        public int Price { get; }
        public IReadOnlyList<CaseLootEntry> LootPool { get; }
        public string RealMoneyProductId { get; }
        public int PriceRubles { get; }
        public bool ExcludeFromRewards { get; }
        public bool IsDonateExclusive { get; }

        public bool IsValid => !string.IsNullOrWhiteSpace(Id) && LootPool.Count > 0;
        public bool IsRealMoneyPurchase => !string.IsNullOrWhiteSpace(RealMoneyProductId);
    }

    /// <summary>
    /// Data-driven case catalog from Resources/Shop/case-catalog.json.
    /// </summary>
    public static class CaseCatalogService
    {
        private const string CatalogResourcePath = "Shop/case-catalog";

        private static CaseCatalogData cachedCatalog;
        private static Dictionary<string, CaseDefinition> casesById;
        private static Dictionary<string, CaseDefinition> casesByProductId;
        private static HashSet<string> donateExclusiveSkinIds;

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
                    $"[CaseCatalogService] Missing catalog at Resources/{CatalogResourcePath}.json");
                cachedCatalog = new CaseCatalogData { shopCases = Array.Empty<CaseCatalogEntry>() };
            }
            else
            {
                cachedCatalog = JsonUtility.FromJson<CaseCatalogData>(asset.text);
                if (cachedCatalog == null)
                {
                    Debug.LogError("[CaseCatalogService] Failed to parse case catalog JSON.");
                    cachedCatalog = new CaseCatalogData { shopCases = Array.Empty<CaseCatalogEntry>() };
                }
            }

            casesById = new Dictionary<string, CaseDefinition>(StringComparer.OrdinalIgnoreCase);
            casesByProductId = new Dictionary<string, CaseDefinition>(StringComparer.OrdinalIgnoreCase);
            donateExclusiveSkinIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (cachedCatalog.shopCases == null)
            {
                return;
            }

            for (var i = 0; i < cachedCatalog.shopCases.Length; i++)
            {
                var entry = cachedCatalog.shopCases[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.caseId))
                {
                    continue;
                }

                var lootPool = NormalizeLootPool(entry.lootPool);
                if (lootPool.Count == 0)
                {
                    Debug.LogWarning(
                        $"[CaseCatalogService] Case '{entry.caseId}' has an empty loot pool.");
                    continue;
                }

                var pictureFolder = string.IsNullOrWhiteSpace(entry.pictureFolder)
                    ? "001"
                    : entry.pictureFolder.Trim();
                var definition = new CaseDefinition(
                    entry.caseId.Trim(),
                    string.IsNullOrWhiteSpace(entry.displayName)
                        ? entry.caseId.Trim()
                        : entry.displayName.Trim(),
                    $"Cases/{pictureFolder}/picture",
                    entry.price,
                    lootPool,
                    entry.realMoneyProductId,
                    entry.priceRubles,
                    entry.excludeFromRewards,
                    entry.isDonateExclusive);
                casesById[definition.Id] = definition;

                if (!string.IsNullOrWhiteSpace(definition.RealMoneyProductId))
                {
                    casesByProductId[definition.RealMoneyProductId.Trim()] = definition;
                }

                if (definition.IsDonateExclusive)
                {
                    for (var lootIndex = 0; lootIndex < lootPool.Count; lootIndex++)
                    {
                        donateExclusiveSkinIds.Add(lootPool[lootIndex].SkinId);
                    }
                }
            }
        }

        public static IReadOnlyList<CaseDefinition> GetShopCases()
        {
            EnsureLoaded();
            var results = new List<CaseDefinition>(casesById.Count);
            if (cachedCatalog.shopCases == null)
            {
                return results;
            }

            for (var i = 0; i < cachedCatalog.shopCases.Length; i++)
            {
                var entry = cachedCatalog.shopCases[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.caseId))
                {
                    continue;
                }

                if (casesById.TryGetValue(entry.caseId.Trim(), out var definition))
                {
                    results.Add(definition);
                }
            }

            return results;
        }

        public static bool TryGetCase(string caseId, out CaseDefinition definition)
        {
            EnsureLoaded();
            definition = default;
            if (string.IsNullOrWhiteSpace(caseId))
            {
                return false;
            }

            return casesById.TryGetValue(caseId.Trim(), out definition) && definition.IsValid;
        }

        public static int GetPrice(CaseDefinition caseDefinition)
        {
            return caseDefinition.IsValid ? caseDefinition.Price : 0;
        }

        public static bool IsRewardEligibleCase(string caseId)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(caseId))
            {
                return false;
            }

            if (!casesById.TryGetValue(caseId.Trim(), out var definition))
            {
                return false;
            }

            return !definition.ExcludeFromRewards;
        }

        public static bool IsDonateExclusiveSkin(string skinId)
        {
            EnsureLoaded();
            return !string.IsNullOrWhiteSpace(skinId) &&
                   donateExclusiveSkinIds != null &&
                   donateExclusiveSkinIds.Contains(skinId.Trim());
        }

        public static bool TryGetCaseByRealMoneyProductId(
            string productId,
            out CaseDefinition definition)
        {
            EnsureLoaded();
            definition = default;
            if (string.IsNullOrWhiteSpace(productId))
            {
                return false;
            }

            return casesByProductId != null &&
                   casesByProductId.TryGetValue(productId.Trim(), out definition) &&
                   definition.IsValid;
        }

        public static bool TryRollLoot(CaseDefinition caseDefinition, out string skinId)
        {
            skinId = string.Empty;
            if (!caseDefinition.IsValid || caseDefinition.LootPool.Count == 0)
            {
                return false;
            }

            var totalWeight = 0;
            for (var i = 0; i < caseDefinition.LootPool.Count; i++)
            {
                totalWeight += caseDefinition.LootPool[i].Weight;
            }

            if (totalWeight <= 0)
            {
                return false;
            }

            var roll = UnityEngine.Random.Range(0, totalWeight);
            var cumulative = 0;
            for (var i = 0; i < caseDefinition.LootPool.Count; i++)
            {
                cumulative += caseDefinition.LootPool[i].Weight;
                if (roll < cumulative)
                {
                    skinId = caseDefinition.LootPool[i].SkinId;
                    return !string.IsNullOrWhiteSpace(skinId);
                }
            }

            skinId = caseDefinition.LootPool[caseDefinition.LootPool.Count - 1].SkinId;
            return !string.IsNullOrWhiteSpace(skinId);
        }

        public static bool TryResolveLootDefinitions(
            CaseDefinition caseDefinition,
            List<PlayerSkinDefinition> results)
        {
            results?.Clear();
            if (results == null || !caseDefinition.IsValid)
            {
                return false;
            }

            for (var i = 0; i < caseDefinition.LootPool.Count; i++)
            {
                var skinId = caseDefinition.LootPool[i].SkinId;
                if (PlayerSkinSelectionService.TryGetDefinitionById(skinId, out var definition) &&
                    definition.IsValid &&
                    !ContainsDefinition(results, definition.Id))
                {
                    results.Add(definition);
                }
            }

            return results.Count > 0;
        }

        public static SkinRarity GetLootRarity(CaseDefinition caseDefinition, string skinId)
        {
            if (string.IsNullOrWhiteSpace(skinId))
            {
                return SkinRarity.Common;
            }

            if (ShopCatalogService.HasExplicitRarity(skinId))
            {
                return ShopCatalogService.GetRarity(skinId);
            }

            if (!caseDefinition.IsValid || !TryGetLootWeight(caseDefinition, skinId, out var weight))
            {
                return DeriveDeterministicRarity(skinId);
            }

            if (AllLootWeightsEqual(caseDefinition))
            {
                return DeriveDeterministicRarity(skinId);
            }

            return WeightToRarity(weight);
        }

        public static Color GetLootRarityStripeColor(CaseDefinition caseDefinition, string skinId)
        {
            return SkinRarityUtility.GetStripeColor(GetLootRarity(caseDefinition, skinId));
        }

        private static bool TryGetLootWeight(CaseDefinition caseDefinition, string skinId, out int weight)
        {
            weight = 0;
            for (var i = 0; i < caseDefinition.LootPool.Count; i++)
            {
                var entry = caseDefinition.LootPool[i];
                if (string.Equals(entry.SkinId, skinId, StringComparison.OrdinalIgnoreCase))
                {
                    weight = entry.Weight;
                    return entry.IsValid;
                }
            }

            return false;
        }

        private static bool AllLootWeightsEqual(CaseDefinition caseDefinition)
        {
            if (!caseDefinition.IsValid || caseDefinition.LootPool.Count <= 1)
            {
                return true;
            }

            var firstWeight = caseDefinition.LootPool[0].Weight;
            for (var i = 1; i < caseDefinition.LootPool.Count; i++)
            {
                if (caseDefinition.LootPool[i].Weight != firstWeight)
                {
                    return false;
                }
            }

            return true;
        }

        private static SkinRarity WeightToRarity(int weight)
        {
            if (weight >= 50)
            {
                return SkinRarity.Common;
            }

            if (weight >= 30)
            {
                return SkinRarity.Uncommon;
            }

            if (weight >= 15)
            {
                return SkinRarity.Rare;
            }

            if (weight >= 8)
            {
                return SkinRarity.Epic;
            }

            if (weight >= 2)
            {
                return SkinRarity.Legendary;
            }

            return SkinRarity.Common;
        }

        private static SkinRarity DeriveDeterministicRarity(string skinId)
        {
            var roll = StableHash(skinId) % 100;
            if (roll < 40)
            {
                return SkinRarity.Common;
            }

            if (roll < 65)
            {
                return SkinRarity.Uncommon;
            }

            if (roll < 82)
            {
                return SkinRarity.Rare;
            }

            if (roll < 94)
            {
                return SkinRarity.Epic;
            }

            return SkinRarity.Legendary;
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                var hash = 17;
                for (var i = 0; i < value.Length; i++)
                {
                    hash = (hash * 31) + value[i];
                }

                return hash & 0x7FFFFFFF;
            }
        }

        private static bool ContainsDefinition(List<PlayerSkinDefinition> results, string skinId)
        {
            for (var i = 0; i < results.Count; i++)
            {
                if (string.Equals(results[i].Id, skinId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<CaseLootEntry> NormalizeLootPool(CaseCatalogLootEntry[] lootPool)
        {
            var results = new List<CaseLootEntry>(lootPool?.Length ?? 0);
            if (lootPool == null)
            {
                return results;
            }

            for (var i = 0; i < lootPool.Length; i++)
            {
                var entry = lootPool[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.skinId))
                {
                    continue;
                }

                var weight = entry.weight > 0 ? entry.weight : 1;
                results.Add(new CaseLootEntry(entry.skinId.Trim(), weight));
            }

            return results;
        }

        [Serializable]
        private sealed class CaseCatalogData
        {
            public CaseCatalogEntry[] shopCases;
        }

        [Serializable]
        private sealed class CaseCatalogEntry
        {
            public string caseId;
            public string displayName;
            public string pictureFolder;
            public int price;
            public string realMoneyProductId;
            public int priceRubles;
            public bool excludeFromRewards;
            public bool isDonateExclusive;
            public CaseCatalogLootEntry[] lootPool;
        }

        [Serializable]
        private sealed class CaseCatalogLootEntry
        {
            public string skinId;
            public int weight;
        }
    }
}
