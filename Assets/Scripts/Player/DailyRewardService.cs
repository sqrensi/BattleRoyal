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

        public static event Action StateChanged;

        public static int CurrentPage
        {
            get
            {
                DailyRewardCatalogService.EnsureLoaded();
                var maxPage = Mathf.Max(0, DailyRewardCatalogService.TotalPages - 1);
                return Mathf.Clamp(UserScopedPlayerPrefs.GetInt(PageKey, 0), 0, maxPage);
            }
        }

        public static int ClaimsOnCurrentPage =>
            Mathf.Clamp(UserScopedPlayerPrefs.GetInt(ClaimsOnPageKey, 0), 0, DailyRewardCatalogService.RewardsPerPage);

        public static bool IsPageComplete => ClaimsOnCurrentPage >= DailyRewardCatalogService.RewardsPerPage;

        public static bool CanClaimToday()
        {
            if (IsAllRewardsComplete())
            {
                return false;
            }

            return !string.Equals(GetLastClaimDate(), GetTodayDateKey(), StringComparison.Ordinal);
        }

        public static int GetNextClaimGlobalIndex() => CurrentPage * DailyRewardCatalogService.RewardsPerPage + ClaimsOnCurrentPage;

        public static bool IsAllRewardsComplete()
        {
            DailyRewardCatalogService.EnsureLoaded();
            return GetNextClaimGlobalIndex() >= DailyRewardCatalogService.TotalRewardCount;
        }

        public static bool ShouldShowLoginPrompt()
        {
            if (IsAllRewardsComplete() || !CanClaimToday())
            {
                return false;
            }

            return !string.Equals(GetLoginPromptDate(), GetTodayDateKey(), StringComparison.Ordinal);
        }

        public static void MarkLoginPromptShown()
        {
            UserScopedPlayerPrefs.SetString(LoginPromptDateKey, GetTodayDateKey());
            PlayerPrefs.Save();
        }

        private static string GetLoginPromptDate()
        {
            return UserScopedPlayerPrefs.GetString(LoginPromptDateKey, string.Empty);
        }

        public static bool TryClaimToday(out DailyRewardEntry claimedReward, out string error)
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

            var claims = ClaimsOnCurrentPage + 1;
            if (claims >= DailyRewardCatalogService.RewardsPerPage)
            {
                UserScopedPlayerPrefs.SetInt(PageKey, Mathf.Min(CurrentPage + 1, DailyRewardCatalogService.TotalPages - 1));
                UserScopedPlayerPrefs.SetInt(ClaimsOnPageKey, 0);
            }
            else
            {
                UserScopedPlayerPrefs.SetInt(ClaimsOnPageKey, claims);
            }

            UserScopedPlayerPrefs.SetString(LastClaimDateKey, GetTodayDateKey());
            PlayerPrefs.Save();
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
            return UserScopedPlayerPrefs.GetString(LastClaimDateKey, string.Empty);
        }

        private static string GetTodayDateKey()
        {
            return DateTime.UtcNow.ToString("yyyy-MM-dd");
        }
    }
}
