using System;
using System.Collections;
using System.Collections.Generic;
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

        private static readonly HashSet<string> OwnedSkinCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> OwnedSkinQuantityCache =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> OwnedCaseQuantityCache =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public static bool IsServerSynced { get; private set; }
        public static string Nickname { get; private set; } = string.Empty;
        public static int Rating => CurrentProfile?.rating ?? MatchRatingUtility.DefaultRating;
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
                apiClient.Configure(config.QueueApiBaseUrl, config.QueueRequestTimeoutSeconds);
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
            Nickname = profile.nickname ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(Nickname))
            {
                PlayerPrefs.SetString(NicknamePrefKey, Nickname);
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
                PlayerPrefs.SetInt(CurrencyGrantKey, 1);
                PlayerPrefs.SetInt(OwnershipInitKey, 1);
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
            if (!IsServerSynced || CurrentProfile?.achievements == null)
            {
                return Array.Empty<PlayerAchievementEntry>();
            }

            return CurrentProfile.achievements;
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

            yield return apiClient.EnsureProfile(playerId, (ok, responseProfile, _) =>
            {
                completed = true;
                success = ok;
                profile = responseProfile;
            });

            if (!completed)
            {
                yield break;
            }

            if (success && profile != null)
            {
                ApplyProfile(profile);
                yield break;
            }

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
    }
}
