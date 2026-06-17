using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class PlayerProfileService
    {
        private const string OwnershipInitKey = "player_skin_ownership_initialized_v1";
        private const string CurrencyGrantKey = "player_currency_grant_100k_v1";
        private const string NicknamePrefKey = "player_nickname_v1";

        private static readonly HashSet<string> OwnedSkinCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static bool IsServerSynced { get; private set; }
        public static string Nickname { get; private set; } = string.Empty;
        public static PlayerProfileDto CurrentProfile { get; private set; }

        public static event Action ProfileSynced;
        public static event Action ProfileSyncFailed;

        public static void ApplyLocalFallback()
        {
            IsServerSynced = false;
            CurrentProfile = null;
            OwnedSkinCache.Clear();
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

            CurrentProfile = profile;
            IsServerSynced = markSynced;
            Nickname = profile.nickname ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(Nickname))
            {
                PlayerPrefs.SetString(NicknamePrefKey, Nickname);
            }

            PlayerCurrencyService.ApplyFromServer(profile.currencyBalance);

            OwnedSkinCache.Clear();
            if (profile.ownedSkins != null)
            {
                for (var i = 0; i < profile.ownedSkins.Length; i++)
                {
                    var skinId = profile.ownedSkins[i];
                    if (!string.IsNullOrWhiteSpace(skinId))
                    {
                        OwnedSkinCache.Add(skinId.Trim());
                    }
                }
            }

            var ownedChanged = previousOwned.Count != OwnedSkinCache.Count;
            if (!ownedChanged)
            {
                foreach (var skinId in OwnedSkinCache)
                {
                    if (!previousOwned.Contains(skinId))
                    {
                        ownedChanged = true;
                        break;
                    }
                }
            }

            ApplyOwnedSkinsToLocal(profile.ownedSkins);
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
            else
            {
                PlayerSkinOwnershipService.NotifyEquipmentChanged();
            }

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

        private static void ApplyOwnedSkinsToLocal(string[] ownedSkins)
        {
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
        }

        private static void ApplyEquippedSlot(PlayerSkinSlot slot, string skinId)
        {
            if (string.IsNullOrWhiteSpace(skinId))
            {
                if (PlayerSkinSelectionService.SupportsUnequip(slot))
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
                default:
                    slotKey = string.Empty;
                    return false;
            }
        }
    }
}
