using System;
using System.Collections.Generic;
using System.Text;
using ShooterPrototype.Player;
using UnityEngine;

namespace ShooterPrototype.UI
{
    public static class MainMenuNotificationState
    {
        private const string InitializedPrefKey = "menu_notification_initialized_v1";
        private const string ViewedSkinsPrefKey = "menu_notification_viewed_skins_v1";
        private const string ViewedCasesPrefKey = "menu_notification_viewed_cases_v1";
        private const string PendingSkinsPrefKey = "menu_notification_pending_skins_v1";
        private const string PendingCasesPrefKey = "menu_notification_pending_cases_v1";

        private static readonly HashSet<string> PendingNewSkinIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> PendingNewCaseIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> ViewedSkinIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> ViewedCaseIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static bool loaded;

        public static event Action Changed;

        public static bool IsSkinUnviewed(string skinId)
        {
            if (string.IsNullOrWhiteSpace(skinId))
            {
                return false;
            }

            EnsureLoaded();
            var normalized = skinId.Trim();
            if (!PendingNewSkinIds.Contains(normalized))
            {
                return false;
            }

            return ShouldShowSkinNotification(normalized);
        }

        public static bool IsCaseUnviewed(string caseId)
        {
            if (string.IsNullOrWhiteSpace(caseId))
            {
                return false;
            }

            EnsureLoaded();
            var normalized = caseId.Trim();
            if (!PendingNewCaseIds.Contains(normalized))
            {
                return false;
            }

            return ShouldShowCaseNotification(normalized);
        }

        public static bool HasInventoryNotifications
        {
            get
            {
                EnsureLoaded();
                return HasVisibleUnviewedSkins() || HasVisibleUnviewedCases();
            }
        }

        public static void SyncInventoryNotifications()
        {
            EnsureLoaded();
            PruneStalePendingNotifications(saveChanges: true);
        }

        public static bool HasAchievementNotifications
        {
            get
            {
                var achievements = PlayerProfileService.GetActiveAchievements();
                if (achievements == null || achievements.Count == 0)
                {
                    return false;
                }

                for (var i = 0; i < achievements.Count; i++)
                {
                    var entry = achievements[i];
                    if (entry != null && entry.completed)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public static void HandleProfileApplied(
            HashSet<string> previousOwned,
            Dictionary<string, int> previousQuantities,
            Dictionary<string, int> previousCaseQuantities,
            PlayerProfileDto profile)
        {
            if (profile == null)
            {
                return;
            }

            EnsureLoaded();

            if (PlayerPrefs.GetInt(InitializedPrefKey, 0) != 1)
            {
                MarkAllCurrentAsViewed(profile);
                PlayerPrefs.SetInt(InitializedPrefKey, 1);
                SaveState();
                NotifyChanged();
                return;
            }

            var anyNew = false;
            anyNew |= TrackNewSkins(previousOwned, previousQuantities, profile);
            anyNew |= TrackNewCases(previousCaseQuantities, profile);
            if (anyNew)
            {
                SaveState();
                NotifyChanged();
            }
        }

        public static void MarkSkinViewed(string skinId)
        {
            if (string.IsNullOrWhiteSpace(skinId))
            {
                return;
            }

            EnsureLoaded();
            var normalized = skinId.Trim();
            if (!PendingNewSkinIds.Remove(normalized))
            {
                return;
            }

            ViewedSkinIds.Add(normalized);
            PruneStalePendingNotifications(saveChanges: false);
            SaveState();
            NotifyChanged();
        }

        public static void MarkSkinAsNew(string skinId)
        {
            if (string.IsNullOrWhiteSpace(skinId))
            {
                return;
            }

            EnsureLoaded();
            if (!MarkSkinAsPending(skinId))
            {
                return;
            }

            SaveState();
            NotifyChanged();
        }

        public static void MarkCaseAsNew(string caseId)
        {
            if (string.IsNullOrWhiteSpace(caseId))
            {
                return;
            }

            EnsureLoaded();
            if (!MarkCaseAsPending(caseId))
            {
                return;
            }

            SaveState();
            NotifyChanged();
        }

        public static void MarkCaseViewed(string caseId)
        {
            if (string.IsNullOrWhiteSpace(caseId))
            {
                return;
            }

            EnsureLoaded();
            var normalized = caseId.Trim();
            if (!PendingNewCaseIds.Remove(normalized))
            {
                return;
            }

            ViewedCaseIds.Add(normalized);
            PruneStalePendingNotifications(saveChanges: false);
            SaveState();
            NotifyChanged();
        }

        private static void EnsureLoaded()
        {
            if (loaded)
            {
                return;
            }

            LoadSet(ViewedSkinsPrefKey, ViewedSkinIds);
            LoadSet(ViewedCasesPrefKey, ViewedCaseIds);
            LoadSet(PendingSkinsPrefKey, PendingNewSkinIds);
            LoadSet(PendingCasesPrefKey, PendingNewCaseIds);
            loaded = true;
            PruneStalePendingNotifications(saveChanges: true);
        }

        private static bool TrackNewSkins(
            HashSet<string> previousOwned,
            Dictionary<string, int> previousQuantities,
            PlayerProfileDto profile)
        {
            var anyNew = false;

            if (profile.ownedSkinQuantities != null && profile.ownedSkinQuantities.Length > 0)
            {
                for (var i = 0; i < profile.ownedSkinQuantities.Length; i++)
                {
                    var entry = profile.ownedSkinQuantities[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.skinId))
                    {
                        continue;
                    }

                    var skinId = entry.skinId.Trim();
                    var quantity = Mathf.Max(0, entry.quantity);
                    previousQuantities.TryGetValue(skinId, out var previousQuantity);
                    if (quantity <= previousQuantity)
                    {
                        continue;
                    }

                    if (MarkSkinAsPending(skinId))
                    {
                        anyNew = true;
                    }
                }

                return anyNew;
            }

            if (profile.ownedSkins == null)
            {
                return false;
            }

            for (var i = 0; i < profile.ownedSkins.Length; i++)
            {
                var skinId = profile.ownedSkins[i];
                if (string.IsNullOrWhiteSpace(skinId))
                {
                    continue;
                }

                var normalized = skinId.Trim();
                if (previousOwned.Contains(normalized))
                {
                    continue;
                }

                if (MarkSkinAsPending(normalized))
                {
                    anyNew = true;
                }
            }

            return anyNew;
        }

        private static bool TrackNewCases(
            Dictionary<string, int> previousCaseQuantities,
            PlayerProfileDto profile)
        {
            if (profile.ownedCaseQuantities == null)
            {
                return false;
            }

            var anyNew = false;
            for (var i = 0; i < profile.ownedCaseQuantities.Length; i++)
            {
                var entry = profile.ownedCaseQuantities[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.caseId))
                {
                    continue;
                }

                var caseId = entry.caseId.Trim();
                var quantity = Mathf.Max(0, entry.quantity);
                previousCaseQuantities.TryGetValue(caseId, out var previousQuantity);
                if (quantity <= previousQuantity)
                {
                    continue;
                }

                if (MarkCaseAsPending(caseId))
                {
                    anyNew = true;
                }
            }

            return anyNew;
        }

        private static bool MarkSkinAsPending(string skinId)
        {
            var normalized = skinId.Trim();
            ViewedSkinIds.Remove(normalized);
            return PendingNewSkinIds.Add(normalized);
        }

        private static bool MarkCaseAsPending(string caseId)
        {
            var normalized = caseId.Trim();
            ViewedCaseIds.Remove(normalized);
            return PendingNewCaseIds.Add(normalized);
        }

        private static void MarkAllCurrentAsViewed(PlayerProfileDto profile)
        {
            PendingNewSkinIds.Clear();
            PendingNewCaseIds.Clear();
            ViewedSkinIds.Clear();
            ViewedCaseIds.Clear();

            if (profile.ownedSkinQuantities != null)
            {
                for (var i = 0; i < profile.ownedSkinQuantities.Length; i++)
                {
                    var entry = profile.ownedSkinQuantities[i];
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.skinId) && entry.quantity > 0)
                    {
                        ViewedSkinIds.Add(entry.skinId.Trim());
                    }
                }
            }
            else if (profile.ownedSkins != null)
            {
                for (var i = 0; i < profile.ownedSkins.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(profile.ownedSkins[i]))
                    {
                        ViewedSkinIds.Add(profile.ownedSkins[i].Trim());
                    }
                }
            }

            if (profile.ownedCaseQuantities != null)
            {
                for (var i = 0; i < profile.ownedCaseQuantities.Length; i++)
                {
                    var entry = profile.ownedCaseQuantities[i];
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.caseId) && entry.quantity > 0)
                    {
                        ViewedCaseIds.Add(entry.caseId.Trim());
                    }
                }
            }
        }

        private static bool HasVisibleUnviewedSkins()
        {
            foreach (var skinId in PendingNewSkinIds)
            {
                if (ShouldShowSkinNotification(skinId))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasVisibleUnviewedCases()
        {
            foreach (var caseId in PendingNewCaseIds)
            {
                if (ShouldShowCaseNotification(caseId))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldShowSkinNotification(string skinId)
        {
            if (string.IsNullOrWhiteSpace(skinId) || !PlayerSkinOwnershipService.IsOwned(skinId))
            {
                return false;
            }

            if (PlayerSkinSelectionService.TryGetDefinitionById(skinId, out var definition) ||
                ShopCatalogService.TryGetSkinDefinition(skinId, out definition))
            {
                return PlayerSkinOwnershipService.ShouldShowInInventory(definition);
            }

            return !ShopCatalogService.IsDefaultOwnedSkin(skinId);
        }

        private static bool ShouldShowCaseNotification(string caseId)
        {
            return !string.IsNullOrWhiteSpace(caseId) &&
                   PlayerProfileService.GetOwnedCaseQuantity(caseId) > 0;
        }

        private static void PruneStalePendingNotifications(bool saveChanges)
        {
            var changed = false;

            if (PendingNewSkinIds.Count > 0)
            {
                var staleSkinIds = new List<string>();
                foreach (var skinId in PendingNewSkinIds)
                {
                    if (!ShouldShowSkinNotification(skinId))
                    {
                        staleSkinIds.Add(skinId);
                    }
                }

                for (var i = 0; i < staleSkinIds.Count; i++)
                {
                    var skinId = staleSkinIds[i];
                    if (PendingNewSkinIds.Remove(skinId))
                    {
                        ViewedSkinIds.Add(skinId);
                        changed = true;
                    }
                }
            }

            if (PendingNewCaseIds.Count > 0)
            {
                var staleCaseIds = new List<string>();
                foreach (var caseId in PendingNewCaseIds)
                {
                    if (!ShouldShowCaseNotification(caseId))
                    {
                        staleCaseIds.Add(caseId);
                    }
                }

                for (var i = 0; i < staleCaseIds.Count; i++)
                {
                    var caseId = staleCaseIds[i];
                    if (PendingNewCaseIds.Remove(caseId))
                    {
                        ViewedCaseIds.Add(caseId);
                        changed = true;
                    }
                }
            }

            if (changed && saveChanges)
            {
                SaveState();
                NotifyChanged();
            }
        }

        private static void LoadSet(string prefKey, HashSet<string> target)
        {
            target.Clear();
            var raw = PlayerPrefs.GetString(prefKey, string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            var parts = raw.Split('|');
            for (var i = 0; i < parts.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(parts[i]))
                {
                    target.Add(parts[i].Trim());
                }
            }
        }

        private static void SaveState()
        {
            PlayerPrefs.SetString(ViewedSkinsPrefKey, JoinSet(ViewedSkinIds));
            PlayerPrefs.SetString(ViewedCasesPrefKey, JoinSet(ViewedCaseIds));
            PlayerPrefs.SetString(PendingSkinsPrefKey, JoinSet(PendingNewSkinIds));
            PlayerPrefs.SetString(PendingCasesPrefKey, JoinSet(PendingNewCaseIds));
            PlayerPrefs.Save();
        }

        private static string JoinSet(HashSet<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append('|');
                }

                builder.Append(value.Trim());
            }

            return builder.ToString();
        }

        private static void NotifyChanged()
        {
            Changed?.Invoke();
        }
    }
}
