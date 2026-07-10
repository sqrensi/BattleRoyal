using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShooterPrototype.Player
{
    [Serializable]
    public sealed class PlayerClientStateDto
    {
        public int dailyRewardPage;
        public int dailyRewardClaimsOnPage;
        public string dailyRewardLastClaimDate;
        public string dailyRewardLoginPromptDate;
        public string dailyRewardCurrentVisitDate;
        public string settingsJson;
    }

    /// <summary>
    /// Single source of truth for player-local progress that must survive new builds:
    /// daily rewards, settings cache, and server-backed currency grants.
    /// </summary>
    public static class PlayerProgressSyncService
    {
        public const string RewardedAdGrantType = "rewarded_ad";

        private static PlayerClientStateDto serverState;
        private static bool hasServerState;
        private static string pendingSettingsJson;
        private static bool settingsSaveQueued;
        private static bool dailyRewardStateSaveQueued;
        private static PlayerProfileDailyRewardStateRequest pendingDailyRewardState;

        public static bool HasServerState => hasServerState && PlayerProfileService.IsServerSynced;

        public static void Reset()
        {
            serverState = null;
            hasServerState = false;
            pendingSettingsJson = null;
            settingsSaveQueued = false;
            dailyRewardStateSaveQueued = false;
            pendingDailyRewardState = null;
        }

        public static void ApplyFromProfile(PlayerProfileDto profile)
        {
            if (profile == null || !PlayerProfileService.IsServerSynced)
            {
                hasServerState = false;
                serverState = null;
                return;
            }

            serverState = ResolveClientState(profile);
            hasServerState = true;
            DailyRewardService.ApplyServerState(serverState);

            if (IsGameplaySceneActive())
            {
                return;
            }

            var settingsJson = serverState.settingsJson;
            if (ClientSettingsService.IsServerSettingsPayloadEmpty(settingsJson))
            {
                ClientSettingsService.ReloadForCurrentPlayer();
                if (ClientSettingsService.HasAnySavedSettings())
                {
                    QueueSettingsSave();
                }
            }
            else
            {
                ClientSettingsService.ImportSettingsJson(settingsJson, saveLocally: true);
                ClientSettingsService.ApplyImportedSettingsSideEffects();
            }
        }

        public static IEnumerator ClaimDailyRewardRoutine(
            MonoBehaviour runner,
            PlayerProfileApiClient apiClient,
            string playerId,
            Action<bool, DailyRewardEntry, string> onCompleted)
        {
            apiClient = ResolveProfileApiClient(apiClient);

            if (runner == null || string.IsNullOrWhiteSpace(playerId))
            {
                onCompleted?.Invoke(false, null, "Не удалось получить награду.");
                yield break;
            }

            var canUseServer = PlayerProfileService.IsServerSynced && apiClient != null;
            if (!canUseServer)
            {
                if (DailyRewardService.TryClaimTodayLocal(out var localReward, out var localError))
                {
                    onCompleted?.Invoke(true, localReward, string.Empty);
                }
                else
                {
                    onCompleted?.Invoke(false, null, localError);
                }

                yield break;
            }

            if (!DailyRewardService.CanClaimToday())
            {
                onCompleted?.Invoke(false, null, "Награда уже получена сегодня.");
                yield break;
            }

            var claimedIndex = DailyRewardService.GetNextClaimGlobalIndex();
            var completed = false;
            var success = false;
            var error = string.Empty;
            PlayerProfileDto profile = null;

            yield return apiClient.ClaimDailyReward(playerId, (ok, responseProfile, responseError) =>
            {
                completed = true;
                success = ok;
                profile = responseProfile;
                error = responseError;
            });

            if (!completed)
            {
                if (TryLocalClaimFallback(onCompleted))
                {
                    yield break;
                }

                onCompleted?.Invoke(false, null, "Не удалось связаться с сервером.");
                yield break;
            }

            if (!success || profile == null)
            {
                if (IsAlreadyClaimedError(error))
                {
                    DailyRewardService.MarkClaimedToday();
                    onCompleted?.Invoke(false, null, "Награда уже получена сегодня.");
                    yield break;
                }

                if (ShouldFallbackToLocalClaim(error) && TryLocalClaimFallback(onCompleted))
                {
                    yield break;
                }

                onCompleted?.Invoke(false, null, string.IsNullOrWhiteSpace(error)
                    ? "Не удалось получить награду."
                    : error);
                yield break;
            }

            PlayerProfileService.ApplyProfile(profile, markSynced: true);
            DailyRewardCatalogService.TryGetReward(claimedIndex, out var claimedReward);
            onCompleted?.Invoke(true, claimedReward, string.Empty);
        }

        private static bool TryLocalClaimFallback(Action<bool, DailyRewardEntry, string> onCompleted)
        {
            if (PlayerProfileService.IsServerSynced)
            {
                return false;
            }

            if (!DailyRewardService.TryClaimTodayLocal(out var localReward, out _))
            {
                return false;
            }

            onCompleted?.Invoke(true, localReward, string.Empty);
            return true;
        }

        public static void QueueDailyRewardPromptDismiss()
        {
            QueueDailyRewardState(fullSync: false);
        }

        public static void QueueDailyRewardClaimSync()
        {
            QueueDailyRewardState(fullSync: true);
        }

        private static void QueueDailyRewardState(bool fullSync)
        {
            if (!PlayerProfileService.IsServerSynced)
            {
                return;
            }

            pendingDailyRewardState = BuildDailyRewardStateRequest(fullSync);
            dailyRewardStateSaveQueued = true;
        }

        public static IEnumerator FlushDailyRewardStateIfNeeded(
            MonoBehaviour runner,
            PlayerProfileApiClient apiClient,
            string playerId)
        {
            if (!dailyRewardStateSaveQueued || runner == null ||
                string.IsNullOrWhiteSpace(playerId) || pendingDailyRewardState == null)
            {
                yield break;
            }

            apiClient = ResolveProfileApiClient(apiClient);
            if (apiClient == null)
            {
                yield break;
            }

            dailyRewardStateSaveQueued = false;
            var request = pendingDailyRewardState;
            pendingDailyRewardState = null;

            var completed = false;
            var success = false;
            PlayerProfileDto profile = null;

            yield return apiClient.SaveDailyRewardState(playerId, request, (ok, responseProfile, _) =>
            {
                completed = true;
                success = ok;
                profile = responseProfile;
            });

            if (completed && success && profile != null)
            {
                PlayerProfileService.ApplyProfile(profile, markSynced: true);
            }
            else
            {
                dailyRewardStateSaveQueued = true;
                pendingDailyRewardState = request;
            }
        }

        public static IEnumerator GrantCurrencyRoutine(
            MonoBehaviour runner,
            PlayerProfileApiClient apiClient,
            string playerId,
            int amount,
            string grantType,
            string sourceId,
            Action<bool, string> onCompleted)
        {
            if (amount <= 0)
            {
                onCompleted?.Invoke(true, string.Empty);
                yield break;
            }

            apiClient = ResolveProfileApiClient(apiClient);

            if (!PlayerProfileService.IsServerSynced || runner == null || apiClient == null ||
                string.IsNullOrWhiteSpace(playerId))
            {
                PlayerCurrencyService.AddCurrency(amount);
                onCompleted?.Invoke(true, string.Empty);
                yield break;
            }

            var completed = false;
            var success = false;
            var error = string.Empty;
            PlayerProfileDto profile = null;

            yield return apiClient.GrantCurrency(
                playerId,
                amount,
                grantType,
                sourceId,
                (ok, responseProfile, responseError) =>
                {
                    completed = true;
                    success = ok;
                    profile = responseProfile;
                    error = responseError;
                });

            if (!completed)
            {
                onCompleted?.Invoke(false, "Currency grant request did not complete.");
                yield break;
            }

            if (success && profile != null)
            {
                PlayerProfileService.ApplyProfile(profile, markSynced: true);
                onCompleted?.Invoke(true, string.Empty);
                yield break;
            }

            onCompleted?.Invoke(false, string.IsNullOrWhiteSpace(error) ? "Currency grant failed." : error);
        }

        public static void QueueSettingsSave()
        {
            pendingSettingsJson = ClientSettingsService.ExportSettingsJson();
            settingsSaveQueued = true;
        }

        public static IEnumerator FlushSettingsIfNeeded(
            MonoBehaviour runner,
            PlayerProfileApiClient apiClient,
            string playerId)
        {
            if (!settingsSaveQueued || runner == null || string.IsNullOrWhiteSpace(playerId) ||
                string.IsNullOrWhiteSpace(pendingSettingsJson))
            {
                yield break;
            }

            if (!PlayerProfileService.IsServerSynced)
            {
                yield break;
            }

            apiClient = ResolveProfileApiClient(apiClient);
            if (apiClient == null)
            {
                yield break;
            }

            settingsSaveQueued = false;
            var settingsJson = pendingSettingsJson;
            pendingSettingsJson = null;

            var completed = false;
            var success = false;
            PlayerProfileDto profile = null;

            yield return apiClient.SaveClientSettings(playerId, settingsJson, (ok, responseProfile, _) =>
            {
                completed = true;
                success = ok;
                profile = responseProfile;
            });

            if (completed && success && profile != null)
            {
                PlayerProfileService.ApplyProfile(profile, markSynced: true);
            }
            else
            {
                settingsSaveQueued = true;
                pendingSettingsJson = settingsJson;
            }
        }

        private static PlayerProfileApiClient ResolveProfileApiClient(PlayerProfileApiClient apiClient)
        {
            if (apiClient != null)
            {
                return apiClient;
            }

            return PlayerProfileService.RegisteredProfileApiClient != null
                ? PlayerProfileService.RegisteredProfileApiClient
                : UnityEngine.Object.FindFirstObjectByType<PlayerProfileApiClient>();
        }

        private static PlayerClientStateDto ResolveClientState(PlayerProfileDto profile)
        {
            var state = profile.clientState ?? new PlayerClientStateDto();

            if (profile.dailyRewardPage > 0 || profile.dailyRewardClaimsOnPage > 0 ||
                !string.IsNullOrWhiteSpace(profile.dailyRewardLastClaimDate) ||
                !string.IsNullOrWhiteSpace(profile.dailyRewardLoginPromptDate) ||
                profile.clientState != null)
            {
                if (profile.clientState != null)
                {
                    state = profile.clientState;
                }

                state.dailyRewardPage = profile.dailyRewardPage;
                state.dailyRewardClaimsOnPage = profile.dailyRewardClaimsOnPage;
                state.dailyRewardLastClaimDate = profile.dailyRewardLastClaimDate ?? string.Empty;
                state.dailyRewardLoginPromptDate = profile.dailyRewardLoginPromptDate ?? string.Empty;
                state.dailyRewardCurrentVisitDate = profile.dailyRewardCurrentVisitDate ?? string.Empty;
            }

            return state;
        }

        private static PlayerProfileDailyRewardStateRequest BuildDailyRewardStateRequest(bool fullSync)
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (!fullSync)
            {
                return new PlayerProfileDailyRewardStateRequest
                {
                    loginPromptDate = today,
                    currentVisitDate = today
                };
            }

            return new PlayerProfileDailyRewardStateRequest
            {
                loginPromptDate = today,
                lastClaimDate = DailyRewardService.GetLastClaimDateForSync(),
                currentVisitDate = today,
                page = DailyRewardService.CurrentPage,
                claimsOnPage = DailyRewardService.ClaimsOnCurrentPage
            };
        }

        private static bool IsAlreadyClaimedError(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                return false;
            }

            return error.IndexOf("AlreadyClaimed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("уже получена", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("already claimed", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ShouldFallbackToLocalClaim(string error)
        {
            if (string.IsNullOrWhiteSpace(error) || IsAlreadyClaimedError(error))
            {
                return false;
            }

            return error.IndexOf("HTTP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("404", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("500", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("502", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("503", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("network", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("Cannot connect", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsGameplaySceneActive()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return false;
            }

            return !string.Equals(scene.name, "MainMenu", StringComparison.OrdinalIgnoreCase);
        }
    }
}
