using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public readonly struct CaseDefinition
    {
        public CaseDefinition(
            string id,
            string displayName,
            string pictureResourcePath,
            int price,
            IReadOnlyList<string> lootPool)
        {
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            PictureResourcePath = pictureResourcePath ?? string.Empty;
            Price = Mathf.Max(0, price);
            LootPool = lootPool ?? Array.Empty<string>();
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string PictureResourcePath { get; }
        public int Price { get; }
        public IReadOnlyList<string> LootPool { get; }

        public bool IsValid => !string.IsNullOrWhiteSpace(Id) && LootPool.Count > 0;
    }

    /// <summary>
    /// Data-driven case catalog from Resources/Shop/case-catalog.json.
    /// </summary>
    public static class CaseCatalogService
    {
        private const string CatalogResourcePath = "Shop/case-catalog";

        private static CaseCatalogData cachedCatalog;
        private static Dictionary<string, CaseDefinition> casesById;

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
                    lootPool);
                casesById[definition.Id] = definition;
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

        public static bool TryRollLoot(CaseDefinition caseDefinition, out string skinId)
        {
            skinId = string.Empty;
            if (!caseDefinition.IsValid || caseDefinition.LootPool.Count == 0)
            {
                return false;
            }

            skinId = caseDefinition.LootPool[UnityEngine.Random.Range(0, caseDefinition.LootPool.Count)];
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
                if (PlayerSkinSelectionService.TryGetDefinitionById(
                        caseDefinition.LootPool[i],
                        out var definition) &&
                    definition.IsValid)
                {
                    results.Add(definition);
                }
            }

            return results.Count > 0;
        }

        private static List<string> NormalizeLootPool(string[] lootPool)
        {
            var results = new List<string>(lootPool?.Length ?? 0);
            if (lootPool == null)
            {
                return results;
            }

            for (var i = 0; i < lootPool.Length; i++)
            {
                var skinId = lootPool[i];
                if (!string.IsNullOrWhiteSpace(skinId))
                {
                    results.Add(skinId.Trim());
                }
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
            public string[] lootPool;
        }
    }
}
