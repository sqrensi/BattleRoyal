using System;
using System.Collections;
using System.Collections.Generic;
using ShooterPrototype.Matchmaking;
using ShooterPrototype.Network;
using ShooterPrototype.UI;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class PlayerProfileService
    {
        private const string OwnershipInitKey = "player_skin_ownership_initialized_v1";
        private const string CurrencyGrantKey = "player_currency_grant_100k_v1";
        private const string NicknamePrefKey = "player_nickname_v1";
        private const string VipPrefixExpiresPrefKey = "player_vip_prefix_expires_v1";
        private const long VipDurationMs = 30L * 24L * 60L * 60L * 1000L;

        private static readonly HashSet<string> OwnedSkinCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> OwnedSkinQuantityCache =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> OwnedCaseQuantityCache =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public static bool IsServerSynced { get; private set; }
        public static bool IsOfflineMode { get; private set; }
        public static string LastSyncError { get; private set; } = string.Empty;
        public static string Nickname { get; private set; } = string.Empty;
        public static int Rating => CurrentProfile?.rating ?? MatchRatingUtility.DefaultRating;

        public static int DuelRating => CurrentProfile?.duelRating ?? MatchRatingUtility.DefaultRating;

        public static bool HasVipPrefix => IsVipPrefixActive(VipPrefixExpiresAtMs);

        public static long VipPrefixExpiresAtMs
        {
            get
            {
                if (CurrentProfile != null && CurrentProfile.vipPrefixExpiresAtMs > 0)
                {
                    return CurrentProfile.vipPrefixExpiresAtMs;
                }

                var stored = PlayerPrefs.GetString(VipPrefixExpiresPrefKey, "0");
                return long.TryParse(stored, out var expiresAtMs) ? expiresAtMs : 0L;
            }
        }

        public static string FormatVipExpiryShopLabel()
        {
            if (!HasVipPrefix)
            {
                return string.Empty;
            }

            var expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(VipPrefixExpiresAtMs).ToLocalTime();
            return $"до {expiresAt:dd.MM.yy}";
        }

        public static string NicknamePrefix
        {
            get
            {
                if (CurrentProfile != null)
                {
                    var fromServer = NicknamePrefixUtility.Normalize(CurrentProfile.nicknamePrefix);
                    if (!string.IsNullOrEmpty(fromServer))
                    {
                        return fromServer;
                    }
                }

                return HasVipPrefix ? NicknamePrefixUtility.Vip : string.Empty;
            }
        }

        public static string LocalDisplayNickname =>
            NicknamePrefixUtility.FormatPlain(NicknamePrefix, Nickname);

        public static int ChallengeBestTimeMs => ResolveChallengeBestTimeMs();

        public static PlayerProfileDto CurrentProfile { get; private set; }

        public static int GetSpendableBalance()
        {
            if (IsServerSynced && CurrentProfile != null)
            {
                return Mathf.Max(0, CurrentProfile.currencyBalance);
            }

            return PlayerCurrencyService.Balance;
        }

        public static bool TryResolveApiClient(out PlayerProfileApiClient apiClient)
        {
            apiClient = UnityEngine.Object.FindFirstObjectByType<PlayerProfileApiClient>();
            if (apiClient == null)
            {
                var launcher = UnityEngine.Object.FindFirstObjectByType<NetworkLauncher>();
                if (launcher != null)
                {
                    apiClient = launcher.GetComponent<PlayerProfileApiClient>();
                    if (apiClient == null)
                    {
                        apiClient = launcher.gameObject.AddComponent<PlayerProfileApiClient>();
                    }
                }
            }

            if (apiClient == null)
            {
                var menuController = UnityEngine.Object.FindFirstObjectByType<MainMenuController>();
                if (menuController != null)
                {
                    apiClient = menuController.GetComponent<PlayerProfileApiClient>();
                    if (apiClient == null)
                    {
                        apiClient = menuController.gameObject.AddComponent<PlayerProfileApiClient>();
                    }
                }
            }

            if (apiClient == null)
            {
                return false;
            }

            var config = UnityEngine.Object.FindFirstObjectByType<NetworkLauncher>()?.Config;
            if (config != null)
            {
                apiClient.Configure(config.ResolveQueueApiBaseUrl(), config.QueueRequestTimeoutSeconds);
            }
            else
            {
                apiClient.Configure("http://127.0.0.1:5050", 5f);
            }

            return true;
        }

        public static event Action ProfileSynced;
        public static event Action ProfileSyncFailed;

        public static void ApplyLocalFallback()
        {
            IsServerSynced = false;
            CurrentProfile = null;
            OwnedSkinCache.Clear();
            OwnedSkinQuantityCache.Clear();
            OwnedCaseQuantityCache.Clear();
            Nickname = PlayerPrefs.GetString(NicknamePrefKey, string.Empty);
            PlayerSkinOwnershipService.EnsureInitialized();
        }

        public static bool GrantLocalVipPrefix()
        {
            if (HasVipPrefix)
            {
                return false;
            }

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var expiresAtMs = nowMs + VipDurationMs;
            PlayerPrefs.SetString(VipPrefixExpiresPrefKey, expiresAtMs.ToString());
            PlayerPrefs.Save();

            if (CurrentProfile != null)
            {
                CurrentProfile.hasVipPrefix = true;
                CurrentProfile.vipPrefixExpiresAtMs = expiresAtMs;
                CurrentProfile.nicknamePrefix = NicknamePrefixUtility.Vip;
            }

            ProfileSynced?.Invoke();
            return true;
        }

        private static bool IsVipPrefixActive(long expiresAtMs)
        {
            if (expiresAtMs <= 0)
            {
                return false;
            }

            return expiresAtMs > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public static void EnterOfflineMode()
        {
            IsOfflineMode = true;
            ApplyLocalFallback();
        }

        public static void ExitOfflineMode()
        {
            IsOfflineMode = false;
        }

        public static bool UsesLocalProgressOnly => !IsServerSynced;

        public static bool UsesOfflineAchievements => !IsServerSynced;

        public static bool CanReportMatchStatsToServer => IsServerSynced;

        public static void ApplyLiveRatings(int duelRating, int rating)
        {
            if (CurrentProfile == null)
            {
                CurrentProfile = new PlayerProfileDto
                {
                    duelRating = Mathf.Max(0, duelRating),
                    rating = Mathf.Max(0, rating),
                };
            }
            else
            {
                CurrentProfile.duelRating = Mathf.Max(0, duelRating);
                CurrentProfile.rating = Mathf.Max(0, rating);
            }

            ProfileSynced?.Invoke();
        }

        public static void ApplyProfile(PlayerProfileDto profile, bool markSynced = true)
        {
            if (profile == null)
            {
                return;
            }

            var previousOwned = new HashSet<string>(OwnedSkinCache, StringComparer.OrdinalIgnoreCase);
            var previousQuantities = new Dictionary<string, int>(OwnedSkinQuantityCache);
            var previousCaseQuantities = new Dictionary<string, int>(OwnedCaseQuantityCache);

            CurrentProfile = profile;
            IsServerSynced = markSynced;
            offlineAchievementCache = null;
            serverAchievementCatalogCache = null;
            if (markSynced)
            {
                ExitOfflineMode();
            }
            Nickname = profile.nickname ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(Nickname))
            {
                PlayerPrefs.SetString(NicknamePrefKey, Nickname);
            }

            if (IsVipPrefixActive(profile.vipPrefixExpiresAtMs))
            {
                PlayerPrefs.SetString(VipPrefixExpiresPrefKey, profile.vipPrefixExpiresAtMs.ToString());
            }
            else if (markSynced)
            {
                PlayerPrefs.DeleteKey(VipPrefixExpiresPrefKey);
            }

            PlayerCurrencyService.ApplyFromServer(profile.currencyBalance);

            OwnedSkinCache.Clear();
            OwnedSkinQuantityCache.Clear();
            OwnedCaseQuantityCache.Clear();
            ApplyOwnedQuantities(profile.ownedSkinQuantities, profile.ownedSkins);
            ApplyOwnedCaseQuantities(profile.ownedCaseQuantities);

            var ownedChanged = HasOwnedInventoryChanged(previousOwned, previousQuantities);

            ApplyOwnedSkinsToLocal(profile.ownedSkins, profile.ownedSkinQuantities);
            ApplyEquippedToLocal(profile.equipped);

            if (profile.starterPackGranted)
            {
                UserScopedPlayerPrefs.SetInt(CurrencyGrantKey, 1);
                UserScopedPlayerPrefs.SetInt(OwnershipInitKey, 1);
            }

            PlayerPrefs.Save();
            if (ownedChanged)
            {
                PlayerSkinOwnershipService.NotifyOwnershipChanged();
            }

            PlayerSkinOwnershipService.NotifyEquipmentChanged();
            MainMenuNotificationState.HandleProfileApplied(
                previousOwned,
                previousQuantities,
                previousCaseQuantities,
                profile);
            ProfileSynced?.Invoke();
        }

        public static bool IsOwned(string skinId)
        {
            if (!IsServerSynced || string.IsNullOrWhiteSpace(skinId))
            {
                return false;
            }

            return OwnedSkinCache.Contains(skinId.Trim());
        }

        public static IReadOnlyList<string> GetOwnedSkinIds()
        {
            if (!IsServerSynced || OwnedSkinCache.Count == 0)
            {
                return Array.Empty<string>();
            }

            var results = new string[OwnedSkinCache.Count];
            OwnedSkinCache.CopyTo(results);
            return results;
        }

        public static int GetOwnedQuantity(string skinId)
        {
            if (string.IsNullOrWhiteSpace(skinId))
            {
                return 0;
            }

            if (IsServerSynced)
            {
                return OwnedSkinQuantityCache.TryGetValue(skinId.Trim(), out var quantity) ? quantity : 0;
            }

            return PlayerSkinOwnershipService.GetOwnedCount(skinId);
        }

        public static int GetOwnedCaseQuantity(string caseId)
        {
            if (string.IsNullOrWhiteSpace(caseId))
            {
                return 0;
            }

            if (IsServerSynced)
            {
                return OwnedCaseQuantityCache.TryGetValue(caseId.Trim(), out var quantity) ? quantity : 0;
            }

            return CaseOpeningService.GetLocalOwnedQuantity(caseId);
        }

        public static IReadOnlyList<PlayerAchievementEntry> GetActiveAchievements()
        {
            if (!IsServerSynced)
            {
                return LoadOfflineAchievementsFromCatalog();
            }

            var serverAchievements = CurrentProfile?.achievements;
            if (serverAchievements != null && serverAchievements.Length > 0)
            {
                return serverAchievements;
            }

            return LoadServerAchievementCatalogFallback();
        }

        public static bool CanUseOfflineAchievementClaim(string achievementId)
        {
            if (string.IsNullOrWhiteSpace(achievementId))
            {
                return false;
            }

            if (!IsServerSynced)
            {
                return true;
            }

            return IsOfflineAchievementCompletedLocally(achievementId) &&
                   !IsOfflineAchievementRewardClaimed(achievementId);
        }

        [Serializable]
        private sealed class OfflineAchievementCatalogFile
        {
            public OfflineAchievementCatalogEntry[] achievements;
        }

        [Serializable]
        private sealed class OfflineAchievementRewardEntry
        {
            public string type;
            public int amount;
            public string caseId;
        }

        [Serializable]
        private sealed class OfflineAchievementCatalogEntry
        {
            public string achievementId;
            public string code;
            public string title;
            public string description;
            public int target;
            public int sortOrder;
            public string eventType;
            public OfflineAchievementRewardEntry reward;
        }

        private static PlayerAchievementEntry[] offlineAchievementCache;
        private static PlayerAchievementEntry[] serverAchievementCatalogCache;

        public static bool IsOfflineAchievementRewardClaimed(string achievementId)
        {
            if (string.IsNullOrWhiteSpace(achievementId))
            {
                return false;
            }

            var claimedKey = $"offline_achievement_reward_claimed_{achievementId.Trim()}";
            return UserScopedPlayerPrefs.GetInt(claimedKey, 0) == 1;
        }

        public static bool TryClaimOfflineAchievement(string achievementId, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(achievementId))
            {
                error = "Достижение не найдено.";
                return false;
            }

            if (!CanUseOfflineAchievementClaim(achievementId))
            {
                error = "Награда доступна только в оффлайн-режиме.";
                return false;
            }

            if (IsOfflineAchievementRewardClaimed(achievementId))
            {
                error = "Награда уже получена.";
                return false;
            }

            var asset = Resources.Load<TextAsset>("Shop/achievement-catalog");
            if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            {
                error = "Каталог достижений недоступен.";
                return false;
            }

            var catalog = JsonUtility.FromJson<OfflineAchievementCatalogFile>(asset.text);
            if (catalog?.achievements == null)
            {
                error = "Каталог достижений пуст.";
                return false;
            }

            OfflineAchievementCatalogEntry matched = null;
            for (var i = 0; i < catalog.achievements.Length; i++)
            {
                var entry = catalog.achievements[i];
                if (entry != null &&
                    string.Equals(entry.achievementId, achievementId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    matched = entry;
                    break;
                }
            }

            if (matched == null)
            {
                error = "Достижение не найдено.";
                return false;
            }

            var target = Mathf.Max(1, matched.target);
            var progressKey = $"offline_achievement_progress_{matched.achievementId}";
            var progress = Mathf.Clamp(UserScopedPlayerPrefs.GetInt(progressKey, 0), 0, target);
            if (progress < target)
            {
                error = "Достижение ещё не выполнено.";
                return false;
            }

            GrantOfflineAchievementReward(matched);
            UserScopedPlayerPrefs.SetInt($"offline_achievement_reward_claimed_{matched.achievementId}", 1);
            offlineAchievementCache = null;
            PlayerPrefs.Save();
            ProfileSynced?.Invoke();
            return true;
        }

        public static void ReportOfflineAchievementEvent(string eventType, int amount = 1)
        {
            if (string.IsNullOrWhiteSpace(eventType) || amount <= 0)
            {
                return;
            }

            if (IsServerSynced && !IsOfflineAchievementSession())
            {
                return;
            }

            var asset = Resources.Load<TextAsset>("Shop/achievement-catalog");
            if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            {
                return;
            }

            var catalog = JsonUtility.FromJson<OfflineAchievementCatalogFile>(asset.text);
            if (catalog?.achievements == null || catalog.achievements.Length == 0)
            {
                return;
            }

            var changed = false;
            for (var i = 0; i < catalog.achievements.Length; i++)
            {
                var entry = catalog.achievements[i];
                if (entry == null ||
                    string.IsNullOrWhiteSpace(entry.achievementId) ||
                    !string.Equals(entry.eventType, eventType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var target = Mathf.Max(1, entry.target);
                var progressKey = $"offline_achievement_progress_{entry.achievementId}";
                var claimedKey = $"offline_achievement_reward_claimed_{entry.achievementId}";
                if (UserScopedPlayerPrefs.GetInt(claimedKey, 0) == 1)
                {
                    continue;
                }

                var progress = Mathf.Clamp(UserScopedPlayerPrefs.GetInt(progressKey, 0), 0, target);
                var next = Mathf.Clamp(progress + amount, 0, target);
                if (next == progress)
                {
                    continue;
                }

                UserScopedPlayerPrefs.SetInt(progressKey, next);
                changed = true;

                if (next < target)
                {
                    continue;
                }
            }

            if (!changed)
            {
                return;
            }

            offlineAchievementCache = null;
            PlayerPrefs.Save();
            ProfileSynced?.Invoke();
        }

        public static void ResetOfflineAchievementEventProgress(string eventType)
        {
            if (string.IsNullOrWhiteSpace(eventType))
            {
                return;
            }

            var asset = Resources.Load<TextAsset>("Shop/achievement-catalog");
            if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            {
                return;
            }

            var catalog = JsonUtility.FromJson<OfflineAchievementCatalogFile>(asset.text);
            if (catalog?.achievements == null || catalog.achievements.Length == 0)
            {
                return;
            }

            var changed = false;
            for (var i = 0; i < catalog.achievements.Length; i++)
            {
                var entry = catalog.achievements[i];
                if (entry == null ||
                    string.IsNullOrWhiteSpace(entry.achievementId) ||
                    !string.Equals(entry.eventType, eventType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var progressKey = $"offline_achievement_progress_{entry.achievementId}";
                var claimedKey = $"offline_achievement_reward_claimed_{entry.achievementId}";
                if (UserScopedPlayerPrefs.GetInt(claimedKey, 0) == 1)
                {
                    continue;
                }

                if (UserScopedPlayerPrefs.GetInt(progressKey, 0) == 0)
                {
                    continue;
                }

                UserScopedPlayerPrefs.SetInt(progressKey, 0);
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            offlineAchievementCache = null;
            PlayerPrefs.Save();
            ProfileSynced?.Invoke();
        }

        private static void GrantOfflineAchievementReward(OfflineAchievementCatalogEntry entry)
        {
            if (entry?.reward == null)
            {
                return;
            }

            var type = entry.reward.type?.Trim().ToLowerInvariant() ?? string.Empty;
            switch (type)
            {
                case "currency":
                    PlayerCurrencyService.AddCurrency(Mathf.Max(0, entry.reward.amount));
                    break;
                case "case":
                    if (!string.IsNullOrWhiteSpace(entry.reward.caseId) &&
                        CaseCatalogService.IsRewardEligibleCase(entry.reward.caseId))
                    {
                        CaseOpeningService.GrantLocalCase(entry.reward.caseId.Trim(), Mathf.Max(1, entry.reward.amount));
                    }

                    break;
                default:
                    break;
            }

            if (!string.IsNullOrWhiteSpace(entry.title))
            {
                AchievementToastController.Show(entry.title, entry.description);
            }
        }

        private static bool IsOfflineAchievementSession()
        {
            return ActiveMatchContext.IsOfflineDuelSession ||
                   ActiveMatchContext.IsOfflineDeathmatchSession ||
                   ActiveMatchContext.IsOfflineTrainingSession ||
                   ActiveMatchContext.IsOfflineChallengeSession;
        }

        private static bool ShouldPreferOfflineAchievements()
        {
            return !IsServerSynced;
        }

        public static bool IsOfflineAchievementCompletedLocally(string achievementId)
        {
            if (string.IsNullOrWhiteSpace(achievementId))
            {
                return false;
            }

            var target = ResolveOfflineAchievementTarget(achievementId);
            if (target <= 0)
            {
                return false;
            }

            var progressKey = $"offline_achievement_progress_{achievementId.Trim()}";
            return UserScopedPlayerPrefs.GetInt(progressKey, 0) >= target;
        }

        private static int ResolveOfflineAchievementTarget(string achievementId)
        {
            if (string.IsNullOrWhiteSpace(achievementId))
            {
                return 0;
            }

            var asset = Resources.Load<TextAsset>("Shop/achievement-catalog");
            if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            {
                return 0;
            }

            var catalog = JsonUtility.FromJson<OfflineAchievementCatalogFile>(asset.text);
            if (catalog?.achievements == null)
            {
                return 0;
            }

            for (var i = 0; i < catalog.achievements.Length; i++)
            {
                var entry = catalog.achievements[i];
                if (entry != null &&
                    string.Equals(entry.achievementId, achievementId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return Mathf.Max(1, entry.target);
                }
            }

            return 0;
        }

        private static IReadOnlyList<PlayerAchievementEntry> LoadOfflineAchievementsFromCatalog()
        {
            if (offlineAchievementCache != null)
            {
                return offlineAchievementCache;
            }

            var asset = Resources.Load<TextAsset>("Shop/achievement-catalog");
            if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            {
                offlineAchievementCache = Array.Empty<PlayerAchievementEntry>();
                return offlineAchievementCache;
            }

            var catalog = JsonUtility.FromJson<OfflineAchievementCatalogFile>(asset.text);
            if (catalog?.achievements == null || catalog.achievements.Length == 0)
            {
                offlineAchievementCache = Array.Empty<PlayerAchievementEntry>();
                return offlineAchievementCache;
            }

            var list = new List<PlayerAchievementEntry>(catalog.achievements.Length);
            for (var i = 0; i < catalog.achievements.Length; i++)
            {
                var source = catalog.achievements[i];
                if (source == null || string.IsNullOrWhiteSpace(source.achievementId))
                {
                    continue;
                }

                if (IsOfflineAchievementRewardClaimed(source.achievementId))
                {
                    continue;
                }

                var target = Mathf.Max(1, source.target);
                var progressKey = $"offline_achievement_progress_{source.achievementId}";
                var progress = Mathf.Clamp(UserScopedPlayerPrefs.GetInt(progressKey, 0), 0, target);
                var completed = progress >= target;
                list.Add(new PlayerAchievementEntry
                {
                    achievementId = source.achievementId,
                    code = source.code,
                    title = source.title,
                    description = source.description,
                    progress = progress,
                    target = target,
                    completed = completed,
                    completedAt = 0,
                    rewardType = source.reward != null ? source.reward.type ?? string.Empty : string.Empty,
                    rewardAmount = source.reward != null ? source.reward.amount : 0,
                    rewardCaseId = source.reward != null ? source.reward.caseId ?? string.Empty : string.Empty
                });
            }

            offlineAchievementCache = list.ToArray();
            return offlineAchievementCache;
        }

        private static IReadOnlyList<PlayerAchievementEntry> LoadServerAchievementCatalogFallback()
        {
            if (serverAchievementCatalogCache != null)
            {
                return serverAchievementCatalogCache;
            }

            var asset = Resources.Load<TextAsset>("Shop/achievement-catalog");
            if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            {
                serverAchievementCatalogCache = Array.Empty<PlayerAchievementEntry>();
                return serverAchievementCatalogCache;
            }

            var catalog = JsonUtility.FromJson<OfflineAchievementCatalogFile>(asset.text);
            if (catalog?.achievements == null || catalog.achievements.Length == 0)
            {
                serverAchievementCatalogCache = Array.Empty<PlayerAchievementEntry>();
                return serverAchievementCatalogCache;
            }

            var list = new List<PlayerAchievementEntry>(catalog.achievements.Length);
            for (var i = 0; i < catalog.achievements.Length; i++)
            {
                var source = catalog.achievements[i];
                if (source == null || string.IsNullOrWhiteSpace(source.achievementId))
                {
                    continue;
                }

                var target = Mathf.Max(1, source.target);
                list.Add(new PlayerAchievementEntry
                {
                    achievementId = source.achievementId,
                    code = source.code,
                    title = source.title,
                    description = source.description,
                    progress = 0,
                    target = target,
                    completed = false,
                    completedAt = 0,
                    rewardType = source.reward != null ? source.reward.type ?? string.Empty : string.Empty,
                    rewardAmount = source.reward != null ? source.reward.amount : 0,
                    rewardCaseId = source.reward != null ? source.reward.caseId ?? string.Empty : string.Empty
                });
            }

            serverAchievementCatalogCache = list.ToArray();
            return serverAchievementCatalogCache;
        }

        public static PlayerMatchStatsDto GetMatchStats()
        {
            if (!IsServerSynced || CurrentProfile?.stats == null)
            {
                return new PlayerMatchStatsDto();
            }

            return CurrentProfile.stats;
        }

        public static IEnumerator PurchaseCase(
            MonoBehaviour runner,
            PlayerProfileApiClient apiClient,
            string playerId,
            string caseId,
            Action<bool, string> onCompleted)
        {
            if (!IsServerSynced || runner == null || apiClient == null)
            {
                onCompleted?.Invoke(false, "Profile is not synced with server.");
                yield break;
            }

            var completed = false;
            var success = false;
            var error = string.Empty;
            PlayerProfileDto profile = null;

            yield return apiClient.PurchaseCase(playerId, caseId, (ok, responseProfile, responseError) =>
            {
                completed = true;
                success = ok;
                profile = responseProfile;
                error = responseError;
            });

            if (!completed)
            {
                onCompleted?.Invoke(false, "Case purchase request did not complete.");
                yield break;
            }

            if (success && profile != null)
            {
                ApplyProfile(profile);
                onCompleted?.Invoke(true, string.Empty);
                yield break;
            }

            onCompleted?.Invoke(false, string.IsNullOrWhiteSpace(error) ? "Case purchase failed." : error);
        }

        public static IEnumerator GrantIapProduct(
            MonoBehaviour runner,
            PlayerProfileApiClient apiClient,
            string playerId,
            string productId,
            Action<bool, string> onCompleted)
        {
            if (!IsServerSynced || runner == null || apiClient == null)
            {
                onCompleted?.Invoke(false, "Profile is not synced with server.");
                yield break;
            }

            var completed = false;
            var success = false;
            var error = string.Empty;
            PlayerProfileDto profile = null;

            yield return apiClient.GrantIapProduct(playerId, productId, (ok, responseProfile, responseError) =>
            {
                completed = true;
                success = ok;
                profile = responseProfile;
                error = responseError;
            });

            if (!completed)
            {
                onCompleted?.Invoke(false, "IAP grant request did not complete.");
                yield break;
            }

            if (success && profile != null)
            {
                ApplyProfile(profile);
                onCompleted?.Invoke(true, string.Empty);
                yield break;
            }

            onCompleted?.Invoke(false, string.IsNullOrWhiteSpace(error) ? "IAP grant failed." : error);
        }

        public static IEnumerator OpenCase(
            MonoBehaviour runner,
            PlayerProfileApiClient apiClient,
            string playerId,
            string caseId,
            Action<bool, string, string> onCompleted)
        {
            if (!IsServerSynced || runner == null || apiClient == null)
            {
                onCompleted?.Invoke(false, "Profile is not synced with server.", string.Empty);
                yield break;
            }

            var completed = false;
            var success = false;
            var error = string.Empty;
            var rolledSkinId = string.Empty;
            PlayerProfileDto profile = null;

            yield return apiClient.OpenCase(playerId, caseId, (ok, responseProfile, responseError, rolled) =>
            {
                completed = true;
                success = ok;
                profile = responseProfile;
                error = responseError;
                rolledSkinId = rolled;
            });

            if (!completed)
            {
                onCompleted?.Invoke(false, "Case open request did not complete.", string.Empty);
                yield break;
            }

            if (success && profile != null)
            {
                ApplyProfile(profile);
                onCompleted?.Invoke(true, string.Empty, rolledSkinId);
                yield break;
            }

            onCompleted?.Invoke(false, string.IsNullOrWhiteSpace(error) ? "Case open failed." : error, string.Empty);
        }

        public static IEnumerator ReportAchievementEvent(
            MonoBehaviour runner,
            PlayerProfileApiClient apiClient,
            string playerId,
            string eventType,
            int amount,
            Action<bool, AchievementCompletedEntry[], string> onCompleted)
        {
            if (runner == null || apiClient == null || string.IsNullOrWhiteSpace(playerId))
            {
                onCompleted?.Invoke(false, null, "Profile is not synced with server.");
                yield break;
            }

            if (string.IsNullOrWhiteSpace(eventType))
            {
                onCompleted?.Invoke(false, null, "Missing event type.");
                yield break;
            }

            var completed = false;
            var success = false;
            var error = string.Empty;
            AchievementCompletedEntry[] newlyCompleted = null;

            yield return apiClient.ReportAchievementEvent(
                playerId,
                eventType,
                amount,
                (ok, profile, completedEntries, responseError) =>
                {
                    completed = true;
                    success = ok;
                    error = responseError;
                    newlyCompleted = completedEntries;
                    if (ok && profile != null)
                    {
                        ApplyProfile(profile, markSynced: true);
                    }
                });

            if (!completed)
            {
                onCompleted?.Invoke(false, null, "Achievement event request did not complete.");
                yield break;
            }

            if (success)
            {
                onCompleted?.Invoke(true, newlyCompleted ?? Array.Empty<AchievementCompletedEntry>(), string.Empty);
                yield break;
            }

            onCompleted?.Invoke(false, null, string.IsNullOrWhiteSpace(error) ? "Achievement event failed." : error);
        }

        public static IEnumerator ClaimAchievement(
            MonoBehaviour runner,
            PlayerProfileApiClient apiClient,
            string playerId,
            string achievementId,
            Action<bool, string> onCompleted)
        {
            if (!IsServerSynced || runner == null || apiClient == null)
            {
                onCompleted?.Invoke(false, "Profile is not synced with server.");
                yield break;
            }

            var completed = false;
            var success = false;
            var error = string.Empty;
            PlayerProfileDto profile = null;

            yield return apiClient.ClaimAchievement(playerId, achievementId, (ok, responseProfile, responseError) =>
            {
                completed = true;
                success = ok;
                profile = responseProfile;
                error = responseError;
            });

            if (!completed)
            {
                onCompleted?.Invoke(false, "Claim achievement request did not complete.");
                yield break;
            }

            if (success && profile != null)
            {
                ApplyProfile(profile);
                onCompleted?.Invoke(true, string.Empty);
                yield break;
            }

            onCompleted?.Invoke(false, string.IsNullOrWhiteSpace(error) ? "Claim achievement failed." : error);
        }

        public static IEnumerator SyncProfile(
            MonoBehaviour runner,
            PlayerProfileApiClient apiClient,
            string playerId,
            bool fallbackToLocalOnFailure = true)
        {
            if (runner == null || apiClient == null || string.IsNullOrWhiteSpace(playerId))
            {
                if (fallbackToLocalOnFailure)
                {
                    ApplyLocalFallback();
                }

                ProfileSyncFailed?.Invoke();
                yield break;
            }

            var completed = false;
            var success = false;
            PlayerProfileDto profile = null;
            var syncError = string.Empty;

            yield return apiClient.EnsureProfile(playerId, (ok, responseProfile, error) =>
            {
                completed = true;
                success = ok;
                profile = responseProfile;
                syncError = error;
            });

            if (!completed)
            {
                LastSyncError = "Profile request did not complete.";
                yield break;
            }

            if (success && profile != null)
            {
                LastSyncError = string.Empty;
                ApplyProfile(profile);
                yield break;
            }

            LastSyncError = string.IsNullOrWhiteSpace(syncError)
                ? "Profile sync failed."
                : syncError;
            Debug.LogWarning($"[PlayerProfileService] SyncProfile failed: {LastSyncError}");

            if (fallbackToLocalOnFailure)
            {
                ApplyLocalFallback();
            }

            ProfileSyncFailed?.Invoke();
        }

        public static IEnumerator PurchaseSkin(
            MonoBehaviour runner,
            PlayerProfileApiClient apiClient,
            string playerId,
            string skinId,
            Action<bool, string> onCompleted)
        {
            if (!IsServerSynced || runner == null || apiClient == null)
            {
                onCompleted?.Invoke(false, "Profile is not synced with server.");
                yield break;
            }

            var completed = false;
            var success = false;
            var error = string.Empty;
            PlayerProfileDto profile = null;

            yield return apiClient.PurchaseSkin(playerId, skinId, (ok, responseProfile, responseError) =>
            {
                completed = true;
                success = ok;
                profile = responseProfile;
                error = responseError;
            });

            if (!completed)
            {
                onCompleted?.Invoke(false, "Purchase request did not complete.");
                yield break;
            }

            if (success && profile != null)
            {
                ApplyProfile(profile);
                onCompleted?.Invoke(true, string.Empty);
                yield break;
            }

            onCompleted?.Invoke(false, string.IsNullOrWhiteSpace(error) ? "Purchase failed." : error);
        }

        public static IEnumerator SetNickname(
            MonoBehaviour runner,
            PlayerProfileApiClient apiClient,
            string playerId,
            string nickname,
            Action<bool, string> onCompleted)
        {
            if (runner == null || apiClient == null || string.IsNullOrWhiteSpace(playerId))
            {
                onCompleted?.Invoke(false, "Профиль не синхронизирован с сервером.");
                yield break;
            }

            if (!IsServerSynced)
            {
                var ensured = false;
                var ensureSuccess = false;
                PlayerProfileDto ensuredProfile = null;

                yield return apiClient.EnsureProfile(playerId, (ok, responseProfile, _) =>
                {
                    ensured = true;
                    ensureSuccess = ok;
                    ensuredProfile = responseProfile;
                });

                if (!ensured || !ensureSuccess || ensuredProfile == null)
                {
                    onCompleted?.Invoke(false, "Не удалось подключиться к серверу профиля.");
                    yield break;
                }

                ApplyProfile(ensuredProfile);
            }

            var completed = false;
            var success = false;
            var error = string.Empty;
            PlayerProfileDto profile = null;

            yield return apiClient.SetNickname(playerId, nickname, (ok, responseProfile, responseError) =>
            {
                completed = true;
                success = ok;
                profile = responseProfile;
                error = responseError;
            });

            if (!completed)
            {
                onCompleted?.Invoke(false, "Запрос смены никнейма не завершился.");
                yield break;
            }

            if (success && profile != null)
            {
                ApplyProfile(profile);
                onCompleted?.Invoke(true, string.Empty);
                yield break;
            }

            var message = ExtractNicknameErrorMessage(error);
            onCompleted?.Invoke(false, string.IsNullOrWhiteSpace(message) ? "Не удалось сменить никнейм." : message);
        }

        public static IEnumerator EquipSkin(
            MonoBehaviour runner,
            PlayerProfileApiClient apiClient,
            string playerId,
            PlayerSkinSlot slot,
            string skinId,
            Action<bool, string> onCompleted)
        {
            if (!IsServerSynced || runner == null || apiClient == null)
            {
                onCompleted?.Invoke(false, "Profile is not synced with server.");
                yield break;
            }

            if (!TryMapSlot(slot, out var slotKey))
            {
                onCompleted?.Invoke(false, "Unknown slot.");
                yield break;
            }

            var completed = false;
            var success = false;
            var error = string.Empty;
            PlayerProfileDto profile = null;

            yield return apiClient.EquipSkin(playerId, slotKey, skinId, (ok, responseProfile, responseError) =>
            {
                completed = true;
                success = ok;
                profile = responseProfile;
                error = responseError;
            });

            if (!completed)
            {
                onCompleted?.Invoke(false, "Equip request did not complete.");
                yield break;
            }

            if (success && profile != null)
            {
                ApplyProfile(profile);
                onCompleted?.Invoke(true, string.Empty);
                yield break;
            }

            onCompleted?.Invoke(false, string.IsNullOrWhiteSpace(error) ? "Equip failed." : error);
        }

        public static bool TryApplyEquippedOptimistic(PlayerSkinSlot slot, string skinId)
        {
            if (!IsServerSynced || CurrentProfile == null)
            {
                return false;
            }

            if (CurrentProfile.equipped == null)
            {
                CurrentProfile.equipped = new PlayerProfileEquippedDto();
            }

            var normalizedSkinId = skinId ?? string.Empty;
            WriteEquippedDtoField(CurrentProfile.equipped, slot, normalizedSkinId);
            ApplyEquippedSlot(slot, normalizedSkinId);
            PlayerSkinOwnershipService.NotifyEquipmentChanged();
            return true;
        }

        public static IEnumerator GrantMatchReward(
            MonoBehaviour runner,
            PlayerProfileApiClient apiClient,
            string playerId,
            int amount,
            string sourceId,
            Action<bool, string> onCompleted)
        {
            if (amount <= 0)
            {
                onCompleted?.Invoke(true, string.Empty);
                yield break;
            }

            if (runner == null || apiClient == null || string.IsNullOrWhiteSpace(playerId))
            {
                onCompleted?.Invoke(false, "Profile is not synced with server.");
                yield break;
            }

            if (string.IsNullOrWhiteSpace(sourceId))
            {
                onCompleted?.Invoke(false, "Missing match source id.");
                yield break;
            }

            var completed = false;
            var success = false;
            var error = string.Empty;
            PlayerProfileDto profile = null;

            yield return apiClient.GrantMatchReward(playerId, amount, sourceId, (ok, responseProfile, responseError) =>
            {
                completed = true;
                success = ok;
                profile = responseProfile;
                error = responseError;
            });

            if (!completed)
            {
                onCompleted?.Invoke(false, "Match reward request did not complete.");
                yield break;
            }

            if (success && profile != null)
            {
                ApplyProfile(profile);
                onCompleted?.Invoke(true, string.Empty);
                yield break;
            }

            onCompleted?.Invoke(false, string.IsNullOrWhiteSpace(error) ? "Match reward failed." : error);
        }

        public static IEnumerator RecordMatchStats(
            MonoBehaviour runner,
            PlayerProfileApiClient apiClient,
            string playerId,
            string sourceId,
            int kills,
            int deaths,
            int placement,
            bool won,
            int damageDealt,
            Action<bool, int, string> onCompleted)
        {
            yield return RecordMatchStats(
                runner,
                apiClient,
                playerId,
                sourceId,
                kills,
                deaths,
                placement,
                won,
                damageDealt,
                null,
                onCompleted);
        }

        public static IEnumerator RecordMatchStats(
            MonoBehaviour runner,
            PlayerProfileApiClient apiClient,
            string playerId,
            string sourceId,
            int kills,
            int deaths,
            int placement,
            bool won,
            int damageDealt,
            string matchMode,
            Action<bool, int, string> onCompleted)
        {
            if (runner == null || apiClient == null || string.IsNullOrWhiteSpace(playerId))
            {
                onCompleted?.Invoke(false, 0, "Profile is not synced with server.");
                yield break;
            }

            if (string.IsNullOrWhiteSpace(sourceId))
            {
                onCompleted?.Invoke(false, 0, "Missing match source id.");
                yield break;
            }

            var completed = false;
            var success = false;
            var error = string.Empty;
            var ratingDelta = 0;
            PlayerProfileDto profile = null;

            var request = new PlayerProfileMatchStatsRequest
            {
                sourceId = sourceId,
                kills = Mathf.Max(0, kills),
                deaths = Mathf.Max(0, deaths),
                placement = Mathf.Max(1, placement),
                won = won,
                damageDealt = Mathf.Max(0, damageDealt),
                matchMode = matchMode ?? string.Empty
            };

            yield return RecordMatchStats(runner, apiClient, playerId, request, onCompleted);
        }

        public static IEnumerator RecordMatchStats(
            MonoBehaviour runner,
            PlayerProfileApiClient apiClient,
            string playerId,
            string sourceId,
            int kills,
            int deaths,
            int placement,
            bool won,
            int damageDealt,
            string matchMode,
            int roundWins,
            int roundLosses,
            int opponentRating,
            Action<bool, int, string> onCompleted)
        {
            if (runner == null || apiClient == null || string.IsNullOrWhiteSpace(playerId))
            {
                onCompleted?.Invoke(false, 0, "Profile is not synced with server.");
                yield break;
            }

            if (string.IsNullOrWhiteSpace(sourceId))
            {
                onCompleted?.Invoke(false, 0, "Missing match source id.");
                yield break;
            }

            var request = new PlayerProfileMatchStatsRequest
            {
                sourceId = sourceId,
                kills = Mathf.Max(0, kills),
                deaths = Mathf.Max(0, deaths),
                placement = Mathf.Max(1, placement),
                won = won,
                damageDealt = Mathf.Max(0, damageDealt),
                matchMode = matchMode ?? string.Empty,
                roundWins = Mathf.Max(0, roundWins),
                roundLosses = Mathf.Max(0, roundLosses),
                opponentRating = Mathf.Max(0, opponentRating)
            };

            yield return RecordMatchStats(runner, apiClient, playerId, request, onCompleted);
        }

        private static IEnumerator RecordMatchStats(
            MonoBehaviour runner,
            PlayerProfileApiClient apiClient,
            string playerId,
            PlayerProfileMatchStatsRequest request,
            Action<bool, int, string> onCompleted)
        {
            if (runner == null || apiClient == null || string.IsNullOrWhiteSpace(playerId))
            {
                onCompleted?.Invoke(false, 0, "Profile is not synced with server.");
                yield break;
            }

            if (request == null || string.IsNullOrWhiteSpace(request.sourceId))
            {
                onCompleted?.Invoke(false, 0, "Missing match source id.");
                yield break;
            }

            var completed = false;
            var success = false;
            var error = string.Empty;
            var ratingDelta = 0;
            PlayerProfileDto profile = null;

            yield return apiClient.RecordMatchStats(playerId, request, (ok, responseDelta, responseProfile, responseError) =>
            {
                completed = true;
                success = ok;
                ratingDelta = responseDelta;
                profile = responseProfile;
                error = responseError;
            });

            if (!completed)
            {
                onCompleted?.Invoke(false, 0, "Match stats request did not complete.");
                yield break;
            }

            if (success && profile != null)
            {
                ApplyProfile(profile);
                onCompleted?.Invoke(true, ratingDelta, string.Empty);
                yield break;
            }

            onCompleted?.Invoke(false, 0, string.IsNullOrWhiteSpace(error) ? "Match stats failed." : error);
        }

        private static void ApplyOwnedSkinsToLocal(string[] ownedSkins, SkinQuantityEntry[] ownedSkinQuantities)
        {
            if (ownedSkinQuantities != null && ownedSkinQuantities.Length > 0)
            {
                for (var i = 0; i < ownedSkinQuantities.Length; i++)
                {
                    var entry = ownedSkinQuantities[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.skinId))
                    {
                        continue;
                    }

                    PlayerSkinOwnershipService.ApplyOwnedQuantityFromServer(entry.skinId, entry.quantity);
                }

                return;
            }

            if (ownedSkins == null)
            {
                return;
            }

            for (var i = 0; i < ownedSkins.Length; i++)
            {
                var skinId = ownedSkins[i];
                if (!string.IsNullOrWhiteSpace(skinId))
                {
                    PlayerSkinOwnershipService.MarkOwnedFromServer(skinId);
                }
            }
        }

        private static void ApplyOwnedQuantities(
            SkinQuantityEntry[] ownedSkinQuantities,
            string[] ownedSkins)
        {
            if (ownedSkinQuantities != null && ownedSkinQuantities.Length > 0)
            {
                for (var i = 0; i < ownedSkinQuantities.Length; i++)
                {
                    var entry = ownedSkinQuantities[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.skinId))
                    {
                        continue;
                    }

                    var skinId = entry.skinId.Trim();
                    var quantity = entry.quantity > 0 ? entry.quantity : 1;
                    OwnedSkinCache.Add(skinId);
                    OwnedSkinQuantityCache[skinId] = quantity;
                }

                return;
            }

            if (ownedSkins == null)
            {
                return;
            }

            for (var i = 0; i < ownedSkins.Length; i++)
            {
                var skinId = ownedSkins[i];
                if (string.IsNullOrWhiteSpace(skinId))
                {
                    continue;
                }

                var normalized = skinId.Trim();
                OwnedSkinCache.Add(normalized);
                OwnedSkinQuantityCache[normalized] = 1;
            }
        }

        private static void ApplyOwnedCaseQuantities(CaseQuantityEntry[] ownedCaseQuantities)
        {
            if (ownedCaseQuantities == null)
            {
                return;
            }

            for (var i = 0; i < ownedCaseQuantities.Length; i++)
            {
                var entry = ownedCaseQuantities[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.caseId))
                {
                    continue;
                }

                var caseId = entry.caseId.Trim();
                var quantity = entry.quantity > 0 ? entry.quantity : 1;
                OwnedCaseQuantityCache[caseId] = quantity;
            }
        }

        private static bool HasOwnedInventoryChanged(
            HashSet<string> previousOwned,
            Dictionary<string, int> previousQuantities)
        {
            if (previousOwned.Count != OwnedSkinCache.Count)
            {
                return true;
            }

            foreach (var skinId in OwnedSkinCache)
            {
                if (!previousOwned.Contains(skinId))
                {
                    return true;
                }
            }

            if (previousQuantities.Count != OwnedSkinQuantityCache.Count)
            {
                return true;
            }

            foreach (var pair in OwnedSkinQuantityCache)
            {
                if (!previousQuantities.TryGetValue(pair.Key, out var previousQuantity) ||
                    previousQuantity != pair.Value)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplyEquippedToLocal(PlayerProfileEquippedDto equipped)
        {
            if (equipped == null)
            {
                return;
            }

            ApplyEquippedSlot(PlayerSkinSlot.Shirt, equipped.shirt);
            ApplyEquippedSlot(PlayerSkinSlot.Pants, equipped.pants);
            ApplyEquippedSlot(PlayerSkinSlot.Boots, equipped.boots);
            ApplyEquippedSlot(PlayerSkinSlot.Gloves, equipped.gloves);
            ApplyEquippedSlot(PlayerSkinSlot.Face, equipped.face);
            ApplyEquippedSlot(PlayerSkinSlot.Hair, equipped.hair);
            ApplyEquippedSlot(PlayerSkinSlot.WeaponAssaultRifle, equipped.weaponAssault);
            ApplyEquippedSlot(PlayerSkinSlot.WeaponSniperRifle, equipped.weaponSniper);
            ApplyEquippedSlot(PlayerSkinSlot.WeaponPistol, equipped.weaponPistol);
            ApplyEquippedSlot(PlayerSkinSlot.WeaponMp7, equipped.weaponMp7);
        }

        private static void WriteEquippedDtoField(
            PlayerProfileEquippedDto equipped,
            PlayerSkinSlot slot,
            string skinId)
        {
            if (equipped == null)
            {
                return;
            }

            switch (slot)
            {
                case PlayerSkinSlot.Shirt:
                    equipped.shirt = skinId;
                    return;
                case PlayerSkinSlot.Pants:
                    equipped.pants = skinId;
                    return;
                case PlayerSkinSlot.Boots:
                    equipped.boots = skinId;
                    return;
                case PlayerSkinSlot.Gloves:
                    equipped.gloves = skinId;
                    return;
                case PlayerSkinSlot.Face:
                    equipped.face = skinId;
                    return;
                case PlayerSkinSlot.Hair:
                    equipped.hair = skinId;
                    return;
                case PlayerSkinSlot.WeaponAssaultRifle:
                    equipped.weaponAssault = skinId;
                    return;
                case PlayerSkinSlot.WeaponSniperRifle:
                    equipped.weaponSniper = skinId;
                    return;
                case PlayerSkinSlot.WeaponPistol:
                    equipped.weaponPistol = skinId;
                    return;
                case PlayerSkinSlot.WeaponMp7:
                    equipped.weaponMp7 = skinId;
                    return;
            }
        }

        private static void ApplyEquippedSlot(PlayerSkinSlot slot, string skinId)
        {
            if (string.IsNullOrWhiteSpace(skinId))
            {
                if (WeaponSkinResourcePaths.IsWeaponSkinSlot(slot) &&
                    WeaponSkinResourcePaths.TryGetWeaponKind(slot, out var kind))
                {
                    PlayerSkinSelectionService.SaveSelected(
                        slot,
                        WeaponSkinResourcePaths.BuildSkinId(kind, "000"));
                }
                else if (PlayerSkinSelectionService.SupportsUnequip(slot))
                {
                    PlayerSkinSelectionService.SaveSelected(slot, "__none__");
                }

                return;
            }

            PlayerSkinSelectionService.SaveSelected(slot, skinId);
        }

        private static string ExtractNicknameErrorMessage(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                return string.Empty;
            }

            if (error.IndexOf("NicknameTaken", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Этот никнейм уже занят.";
            }

            if (error.IndexOf("InvalidNickname", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Никнейм: 3–16 символов, буквы, цифры, _ или -.";
            }

            return error;
        }

        private static bool TryMapSlot(PlayerSkinSlot slot, out string slotKey)
        {
            switch (slot)
            {
                case PlayerSkinSlot.Shirt:
                    slotKey = "shirt";
                    return true;
                case PlayerSkinSlot.Pants:
                    slotKey = "pants";
                    return true;
                case PlayerSkinSlot.Boots:
                    slotKey = "boots";
                    return true;
                case PlayerSkinSlot.Gloves:
                    slotKey = "gloves";
                    return true;
                case PlayerSkinSlot.Face:
                    slotKey = "face";
                    return true;
                case PlayerSkinSlot.Hair:
                    slotKey = "hair";
                    return true;
                case PlayerSkinSlot.WeaponAssaultRifle:
                    slotKey = "weapon_assault";
                    return true;
                case PlayerSkinSlot.WeaponSniperRifle:
                    slotKey = "weapon_sniper";
                    return true;
                case PlayerSkinSlot.WeaponPistol:
                    slotKey = "weapon_pistol";
                    return true;
                case PlayerSkinSlot.WeaponMp7:
                    slotKey = "weapon_mp7";
                    return true;
                default:
                    slotKey = string.Empty;
                    return false;
            }
        }

        private const string OfflineChallengeBestTimeKey = "offline_challenge_best_time_ms";

        public static int ResolveChallengeBestTimeMs()
        {
            if (CurrentProfile != null && CurrentProfile.challengeBestTimeMs > 0)
            {
                return CurrentProfile.challengeBestTimeMs;
            }

            return PlayerPrefs.GetInt(OfflineChallengeBestTimeKey, -1);
        }

        public static IEnumerator RecordChallengeCompletion(
            MonoBehaviour runner,
            float elapsedSeconds,
            Action<bool, bool> onCompleted)
        {
            var timeMs = Mathf.Max(0, Mathf.RoundToInt(elapsedSeconds * 1000f));
            var improved = false;
            var previousBest = ResolveChallengeBestTimeMs();
            if (previousBest < 0 || timeMs < previousBest)
            {
                improved = true;
                PlayerPrefs.SetInt(OfflineChallengeBestTimeKey, timeMs);
            }

            if (UsesLocalProgressOnly || runner == null || !TryResolveApiClient(out var apiClient))
            {
                onCompleted?.Invoke(true, improved);
                yield break;
            }

            var playerId = PlayerIdentityService.GetOrCreatePlayerId();
            var sourceId = $"challenge_{playerId}_{DateTime.UtcNow.Ticks}";
            var completed = false;
            var success = false;
            var error = string.Empty;
            PlayerProfileDto profile = null;

            var request = new PlayerProfileMatchStatsRequest
            {
                sourceId = sourceId,
                kills = 0,
                deaths = 0,
                placement = 1,
                won = true,
                damageDealt = 0,
                matchMode = "challenge",
                completionTimeMs = timeMs
            };

            yield return apiClient.RecordMatchStats(playerId, request, (ok, _, responseProfile, responseError) =>
            {
                completed = true;
                success = ok;
                profile = responseProfile;
                error = responseError;
            });

            if (!completed)
            {
                onCompleted?.Invoke(false, improved);
                yield break;
            }

            if (success && profile != null)
            {
                ApplyProfile(profile);
                var serverBest = profile.challengeBestTimeMs;
                improved = serverBest > 0 && (previousBest < 0 || serverBest < previousBest || timeMs <= serverBest);
                onCompleted?.Invoke(true, improved);
                yield break;
            }

            onCompleted?.Invoke(false, improved);
        }

        public static IEnumerator RecordTrainingSession(
            MonoBehaviour runner,
            int elapsedSeconds,
            Action<bool> onCompleted)
        {
            var trainingTimeSeconds = Mathf.Max(1, elapsedSeconds);
            if (UsesLocalProgressOnly || runner == null || !TryResolveApiClient(out var apiClient))
            {
                onCompleted?.Invoke(true);
                yield break;
            }

            var playerId = PlayerIdentityService.GetOrCreatePlayerId();
            var sourceId = $"training_{playerId}_{DateTime.UtcNow.Ticks}";
            var completed = false;
            var success = false;

            var request = new PlayerProfileMatchStatsRequest
            {
                sourceId = sourceId,
                kills = 0,
                deaths = 0,
                placement = 1,
                won = false,
                damageDealt = 0,
                matchMode = "training",
                trainingTimeSeconds = trainingTimeSeconds
            };

            yield return apiClient.RecordMatchStats(playerId, request, (ok, _, __, ___) =>
            {
                completed = true;
                success = ok;
            });

            onCompleted?.Invoke(completed && success);
        }
    }
}
