using System;
using UnityEngine;

namespace ShooterPrototype.Player
{
    [Serializable]
    public sealed class DailyRewardEntry
    {
        public string rewardId;
        public string type;
        public int amount;
        public string skinId;
        public string caseId;
        public string title;
    }

    [Serializable]
    public sealed class DailyRewardCatalogFile
    {
        public DailyRewardEntry[] rewards;
    }

    public static class DailyRewardCatalogService
    {
        private const string CatalogResourcePath = "Shop/daily-rewards-catalog";
        public const int RewardsPerPage = 7;
        public const int TotalPages = 7;

        private static DailyRewardEntry[] cachedRewards;

        public static void EnsureLoaded()
        {
            if (cachedRewards != null)
            {
                return;
            }

            var asset = Resources.Load<TextAsset>(CatalogResourcePath);
            if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            {
                cachedRewards = Array.Empty<DailyRewardEntry>();
                return;
            }

            var catalog = JsonUtility.FromJson<DailyRewardCatalogFile>(asset.text);
            cachedRewards = catalog?.rewards ?? Array.Empty<DailyRewardEntry>();
        }

        public static int TotalRewardCount
        {
            get
            {
                EnsureLoaded();
                return cachedRewards.Length;
            }
        }

        public static bool TryGetReward(int globalIndex, out DailyRewardEntry entry)
        {
            EnsureLoaded();
            if (globalIndex < 0 || globalIndex >= cachedRewards.Length)
            {
                entry = null;
                return false;
            }

            entry = cachedRewards[globalIndex];
            return entry != null;
        }

        public static string FormatRewardTitle(DailyRewardEntry entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(entry.title))
            {
                return entry.title;
            }

            var type = entry.type?.Trim().ToLowerInvariant() ?? string.Empty;
            return type switch
            {
                "currency" => $"{Mathf.Max(0, entry.amount)} монет",
                "case" => "Кейс",
                "skin" => "Скин",
                _ => "Награда",
            };
        }
    }
}
