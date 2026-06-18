using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class CaseOpeningService
    {
        private const string LocalOwnedCasesPrefKey = "local_owned_cases_v1";

        private static readonly Dictionary<string, int> LocalOwnedCases =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static bool localCasesLoaded;

        public static bool TryPurchaseLocal(CaseDefinition caseDefinition)
        {
            if (!caseDefinition.IsValid)
            {
                return false;
            }

            if (PlayerProfileService.IsServerSynced)
            {
                return false;
            }

            var price = CaseCatalogService.GetPrice(caseDefinition);
            if (!PlayerCurrencyService.TrySpend(price))
            {
                return false;
            }

            EnsureLocalCasesLoaded();
            if (LocalOwnedCases.TryGetValue(caseDefinition.Id, out var quantity))
            {
                LocalOwnedCases[caseDefinition.Id] = quantity + 1;
            }
            else
            {
                LocalOwnedCases[caseDefinition.Id] = 1;
            }

            SaveLocalOwnedCases();
            return true;
        }

        public static bool TryOpenLocal(CaseDefinition caseDefinition, out string rolledSkinId)
        {
            rolledSkinId = string.Empty;
            if (!caseDefinition.IsValid)
            {
                return false;
            }

            if (PlayerProfileService.IsServerSynced)
            {
                return false;
            }

            EnsureLocalCasesLoaded();
            if (!LocalOwnedCases.TryGetValue(caseDefinition.Id, out var quantity) || quantity <= 0)
            {
                return false;
            }

            if (!CaseCatalogService.TryRollLoot(caseDefinition, out rolledSkinId))
            {
                return false;
            }

            if (quantity <= 1)
            {
                LocalOwnedCases.Remove(caseDefinition.Id);
            }
            else
            {
                LocalOwnedCases[caseDefinition.Id] = quantity - 1;
            }

            SaveLocalOwnedCases();
            PlayerSkinOwnershipService.GrantOwnedSkin(rolledSkinId, allowDuplicate: true);
            return true;
        }

        public static int GetLocalOwnedQuantity(string caseId)
        {
            if (string.IsNullOrWhiteSpace(caseId))
            {
                return 0;
            }

            EnsureLocalCasesLoaded();
            return LocalOwnedCases.TryGetValue(caseId.Trim(), out var quantity) ? quantity : 0;
        }

        private static void EnsureLocalCasesLoaded()
        {
            if (localCasesLoaded)
            {
                return;
            }

            localCasesLoaded = true;
            LocalOwnedCases.Clear();

            var raw = PlayerPrefs.GetString(LocalOwnedCasesPrefKey, string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            var pairs = raw.Split('|');
            for (var i = 0; i < pairs.Length; i++)
            {
                var pair = pairs[i];
                if (string.IsNullOrWhiteSpace(pair))
                {
                    continue;
                }

                var separatorIndex = pair.IndexOf(':');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var caseId = pair.Substring(0, separatorIndex).Trim();
                if (!int.TryParse(pair.Substring(separatorIndex + 1), out var quantity) || quantity <= 0)
                {
                    continue;
                }

                LocalOwnedCases[caseId] = quantity;
            }
        }

        private static void SaveLocalOwnedCases()
        {
            if (LocalOwnedCases.Count == 0)
            {
                PlayerPrefs.DeleteKey(LocalOwnedCasesPrefKey);
                PlayerPrefs.Save();
                return;
            }

            var parts = new List<string>(LocalOwnedCases.Count);
            foreach (var pair in LocalOwnedCases)
            {
                parts.Add($"{pair.Key}:{pair.Value}");
            }

            PlayerPrefs.SetString(LocalOwnedCasesPrefKey, string.Join("|", parts));
            PlayerPrefs.Save();
        }
    }
}
