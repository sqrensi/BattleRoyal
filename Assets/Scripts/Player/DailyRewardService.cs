using System;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class DailyRewardService
    {
        private const string PageKey = "daily_reward_page_v1";
        private const string ClaimsOnPageKey = "daily_reward_claims_on_page_v1";
        private const string LastClaimDateKey = "daily_reward_last_claim_date_v1";
        private const string LoginPromptDateKey = "daily_reward_login_prompt_date_v1";

        private static bool useServerState;
        private static int serverPage;
        private static int serverClaimsOnPage;
        private static string serverLastClaimDate = string.Empty;
        private static string serverLoginPromptDate = string.Empty;
        private static bool loginPromptHandledThisSession;

        public static event Action StateChanged;

        public static void ApplyServerState(PlayerClientStateDto state)
        {
            if (state == null || !PlayerProfileService.IsServerSynced)
            {
                useServerState = false;
                return;
            }

            useServerState = true;

            var localPage = PlayerSettingsPrefs.GetInt(PageKey, 0);
            var localClaims = PlayerSettingsPrefs.GetInt(ClaimsOnPageKey, 0);
            var localLastClaim = PlayerSettingsPrefs.GetString(LastClaimDateKey, string.Empty);
            var localLoginPrompt = PlayerSettingsPrefs.GetString(LoginPromptDateKey, string.Empty);

            serverPage = Mathf.Max(0, state.dailyRewardPage);
            serverClaimsOnPage = Mathf.Max(0, state.dailyRewardClaimsOnPage);
            if (GetProgressScore(localPage, localClaims) > GetProgressScore(serverPage, serverClaimsOnPage))
            {
                serverPage = localPage;
                serverClaimsOnPage = localClaims;
            }

            serverLastClaimDate = PickLatestDateKey(state.dailyRewardLastClaimDate, localLastClaim);
            serverLoginPromptDate = PickLatestDateKey(state.dailyRewardLoginPromptDate, localLoginPrompt);

            var today = GetTodayDateKey();
            if (string.Equals(serverLastClaimDate, today, StringComparison.Ordinal) ||
                string.Equals(serverLoginPromptDate, today, StringComparison.Ordinal))
            {
                loginPromptHandledThisSession = true;
            }

            PlayerSettingsPrefs.SetInt(PageKey, serverPage);
            PlayerSettingsPrefs.SetInt(ClaimsOnPageKey, serverClaimsOnPage);
            PlayerSettingsPrefs.SetString(LastClaimDateKey, serverLastClaimDate);
            PlayerSettingsPrefs.SetString(LoginPromptDateKey, serverLoginPromptDate);
            PlayerPrefs.Save();
            StateChanged?.Invoke();
        }

        public static int CurrentPage
        {
            get
            {
                DailyRewardCatalogService.EnsureLoaded();
                var maxPage = Mathf.Max(0, DailyRewardCatalogService.TotalPages - 1);
                var page = useServerState && PlayerProfileService.IsServerSynced
                    ? serverPage
                    : PlayerSettingsPrefs.GetInt(PageKey, 0);
                return Mathf.Clamp(page, 0, maxPage);
            }
        }

        public static int ClaimsOnCurrentPage
        {
            get
            {
                var claims = useServerState && PlayerProfileService.IsServerSynced
                    ? serverClaimsOnPage
                    : PlayerSettingsPrefs.GetInt(ClaimsOnPageKey, 0);
                return Mathf.Clamp(claims, 0, DailyRewardCatalogService.RewardsPerPage);
            }
        }

        public static bool IsPageComplete => ClaimsOnCurrentPage >= DailyRewardCatalogService.RewardsPerPage;

        public static bool CanClaimToday()
        {
            if (IsAllRewardsComplete())
            {
                return false;
            }

            return !string.Equals(GetLastClaimDate(), GetTodayDateKey(), StringComparison.Ordinal);
        }

        public static int GetNextClaimGlobalIndex() =>
            CurrentPage * DailyRewardCatalogService.RewardsPerPage + ClaimsOnCurrentPage;

        public static bool IsAllRewardsComplete()
        {
            DailyRewardCatalogService.EnsureLoaded();
            return GetNextClaimGlobalIndex() >= DailyRewardCatalogService.TotalRewardCount;
        }

        public static bool ShouldShowLoginPrompt()
        {
            if (loginPromptHandledThisSession)
            {
                return false;
            }

            if (IsAllRewardsComplete() || !CanClaimToday())
            {
                return false;
            }

            return !string.Equals(GetLoginPromptDate(), GetTodayDateKey(), StringComparison.Ordinal);
        }

        public static void MarkPromptShownThisSession()
        {
            loginPromptHandledThisSession = true;
        }

        public static void MarkLoginPromptShown()
        {
            loginPromptHandledThisSession = true;
            var today = GetTodayDateKey();
            if (useServerState && PlayerProfileService.IsServerSynced)
            {
                serverLoginPromptDate = today;
            }

            PlayerSettingsPrefs.SetString(LoginPromptDateKey, today);
            PlayerPrefs.Save();
            PlayerProgressSyncService.QueueDailyRewardPromptDismiss();
        }

        public static void MarkClaimedToday()
        {
            var today = GetTodayDateKey();
            if (useServerState && PlayerProfileService.IsServerSynced)
            {
                serverLastClaimDate = today;
                serverLoginPromptDate = today;
            }

            PlayerSettingsPrefs.SetString(LastClaimDateKey, today);
            PlayerSettingsPrefs.SetString(LoginPromptDateKey, today);
            PlayerPrefs.Save();
            loginPromptHandledThisSession = true;
            StateChanged?.Invoke();
        }

        public static string GetLastClaimDateForSync()
        {
            return GetLastClaimDate();
        }

        public static bool TryClaimToday(out DailyRewardEntry claimedReward, out string error)
        {
            if (PlayerProfileService.IsServerSynced)
            {
                claimedReward = null;
                error = "Daily reward must be claimed through server sync.";
                return false;
            }

            return TryClaimTodayLocal(out claimedReward, out error);
        }

        public static bool TryClaimTodayLocal(out DailyRewardEntry claimedReward, out string error)
        {
            claimedReward = null;
            error = string.Empty;

            if (!CanClaimToday())
            {
                error = "Награда уже получена сегодня.";
                return false;
            }

            if (IsAllRewardsComplete())
            {
                error = "Все награды уже получены.";
                return false;
            }

            var globalIndex = GetNextClaimGlobalIndex();
            if (!DailyRewardCatalogService.TryGetReward(globalIndex, out claimedReward))
            {
                error = "Награда недоступна.";
                return false;
            }

            if (!ApplyReward(claimedReward))
            {
                error = "Не удалось выдать награду.";
                claimedReward = null;
                return false;
            }

            AdvanceLocalClaimState();
            StateChanged?.Invoke();
            return true;
        }

        public static bool IsSlotClaimed(int slotIndexOnPage)
        {
            return slotIndexOnPage >= 0 && slotIndexOnPage < ClaimsOnCurrentPage;
        }

        public static bool IsSlotClaimableToday(int slotIndexOnPage)
        {
            return CanClaimToday() && slotIndexOnPage == ClaimsOnCurrentPage;
        }

        private static void AdvanceLocalClaimState()
        {
            var claims = ClaimsOnCurrentPage + 1;
            if (claims >= DailyRewardCatalogService.RewardsPerPage)
            {
                var nextPage = Mathf.Min(CurrentPage + 1, DailyRewardCatalogService.TotalPages - 1);
                if (useServerState && PlayerProfileService.IsServerSynced)
                {
                    serverPage = nextPage;
                    serverClaimsOnPage = 0;
                }

                PlayerSettingsPrefs.SetInt(PageKey, nextPage);
                PlayerSettingsPrefs.SetInt(ClaimsOnPageKey, 0);
            }
            else
            {
                if (useServerState && PlayerProfileService.IsServerSynced)
                {
                    serverClaimsOnPage = claims;
                }

                PlayerSettingsPrefs.SetInt(ClaimsOnPageKey, claims);
            }

            var today = GetTodayDateKey();
            if (useServerState && PlayerProfileService.IsServerSynced)
            {
                serverLastClaimDate = today;
                serverLoginPromptDate = today;
            }

            PlayerSettingsPrefs.SetString(LastClaimDateKey, today);
            PlayerSettingsPrefs.SetString(LoginPromptDateKey, today);
            PlayerPrefs.Save();
        }

        private static bool ApplyReward(DailyRewardEntry entry)
        {
            if (entry == null)
            {
                return false;
            }

            PlayerSkinOwnershipService.EnsureInitialized();

            var type = entry.type?.Trim().ToLowerInvariant() ?? string.Empty;
            switch (type)
            {
                case "currency":
                    PlayerCurrencyService.AddCurrency(Mathf.Max(0, entry.amount));
                    return true;
                case "skin":
                    if (string.IsNullOrWhiteSpace(entry.skinId))
                    {
                        return false;
                    }

                    PlayerSkinOwnershipService.GrantDailyRewardSkin(entry.skinId.Trim(), allowDuplicate: true);
                    return true;
                case "case":
                    if (string.IsNullOrWhiteSpace(entry.caseId))
                    {
                        return false;
                    }

                    if (!CaseCatalogService.IsRewardEligibleCase(entry.caseId))
                    {
                        return false;
                    }

                    var caseAmount = Mathf.Max(1, entry.amount);
                    CaseOpeningService.GrantLocalCase(entry.caseId.Trim(), caseAmount);
                    if (PlayerProfileService.IsServerSynced)
                    {
                        PlayerProfileService.ApplyLocalCaseRewardGrant(entry.caseId.Trim(), caseAmount);
                    }

                    return true;
                default:
                    return false;
            }
        }

        private static string GetLastClaimDate()
        {
            if (useServerState && PlayerProfileService.IsServerSynced)
            {
                return serverLastClaimDate ?? string.Empty;
            }

            return PlayerSettingsPrefs.GetString(LastClaimDateKey, string.Empty);
        }

        private static string GetLoginPromptDate()
        {
            if (useServerState && PlayerProfileService.IsServerSynced)
            {
                return serverLoginPromptDate ?? string.Empty;
            }

            return PlayerSettingsPrefs.GetString(LoginPromptDateKey, string.Empty);
        }

        private static string GetTodayDateKey()
        {
            return DateTime.UtcNow.ToString("yyyy-MM-dd");
        }

        private static int GetProgressScore(int page, int claimsOnPage)
        {
            return page * DailyRewardCatalogService.RewardsPerPage + claimsOnPage;
        }

        private static string PickLatestDateKey(string serverDate, string localDate)
        {
            if (string.IsNullOrWhiteSpace(serverDate))
            {
                return localDate ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(localDate))
            {
                return serverDate ?? string.Empty;
            }

            return string.CompareOrdinal(serverDate, localDate) >= 0 ? serverDate : localDate;
        }
    }
}
