using System;
using System.Collections;
using ShooterPrototype.Player;
using ShooterPrototype.UI;
using UnityEngine;
#if InterstitialAdv_yg
using YG;
#endif
#if RewardedAdv_yg
using YG;
#endif

namespace ShooterPrototype.Platform
{
    public static class GameAdsService
    {
        public const string RewardedCoinsPlacementId = "reward_coins_100";
        public const int RewardedCoinsAmount = 100;
        public const string RewardedCoinsDescription =
            "Посмотрите рекламу до конца и получите 100 монет. Можно смотреть без ограничений.";

        public static bool ShouldShowUiInterstitial()
        {
            return YandexGamesIntegrationService.IsYandexGamesRuntime() &&
                   !PlayerProfileService.HasNoAdsPass;
        }

        public static void RunUiInterstitialGate(MonoBehaviour runner, Action onContinue)
        {
            if (runner == null)
            {
                onContinue?.Invoke();
                return;
            }

            if (!ShouldShowUiInterstitial())
            {
                onContinue?.Invoke();
                return;
            }

            runner.StartCoroutine(UiInterstitialRoutine(onContinue));
        }

        public static void RunRewardedCoins(MonoBehaviour runner, Action<bool> onCompleted)
        {
            if (runner == null)
            {
                onCompleted?.Invoke(false);
                return;
            }

            if (!YandexGamesIntegrationService.IsYandexGamesRuntime())
            {
                onCompleted?.Invoke(false);
                return;
            }

            runner.StartCoroutine(RewardedCoinsRoutine(runner, onCompleted));
        }

        private static IEnumerator UiInterstitialRoutine(Action onContinue)
        {
#if InterstitialAdv_yg
            var closed = false;
            void HandleClose()
            {
                closed = true;
            }

            YG2.onCloseInterAdv += HandleClose;
            YG2.InterstitialAdvShow();

            yield return null;

            if (!YG2.nowInterAdv)
            {
                YG2.onCloseInterAdv -= HandleClose;
                MenuCursorUtility.UnlockForMenu();
                onContinue?.Invoke();
                yield break;
            }

            while (YG2.nowInterAdv)
            {
                yield return null;
            }

            if (!closed)
            {
                yield return null;
            }

            YG2.onCloseInterAdv -= HandleClose;
            MenuCursorUtility.UnlockForMenu();
            onContinue?.Invoke();
#else
            onContinue?.Invoke();
            yield break;
#endif
        }

        private static IEnumerator RewardedCoinsRoutine(MonoBehaviour runner, Action<bool> onCompleted)
        {
#if RewardedAdv_yg
            if (YG2.nowInterAdv || YG2.nowRewardAdv)
            {
                onCompleted?.Invoke(false);
                yield break;
            }

            var completed = false;
            var rewarded = false;

            void HandleClose()
            {
                completed = true;
            }

            void HandleReward(string rewardId)
            {
                if (string.IsNullOrWhiteSpace(rewardId) ||
                    string.Equals(rewardId, RewardedCoinsPlacementId, StringComparison.Ordinal))
                {
                    rewarded = true;
                }
            }

            YG2.onCloseRewardedAdv += HandleClose;
            YG2.onRewardAdv += HandleReward;
            YG2.RewardedAdvShow(RewardedCoinsPlacementId, () => rewarded = true);

            var timeoutAt = Time.unscaledTime + 120f;
            while (!completed && Time.unscaledTime < timeoutAt)
            {
                yield return null;
            }

            YG2.onCloseRewardedAdv -= HandleClose;
            YG2.onRewardAdv -= HandleReward;

            if (!rewarded)
            {
                onCompleted?.Invoke(false);
                yield break;
            }

            var grantCompleted = false;
            var grantSuccess = false;
            var sourceId = RewardedCoinsPlacementId + "_" + Guid.NewGuid().ToString("N");
            var playerId = PlayerIdentityService.GetOrCreatePlayerId();
            var apiClient = UnityEngine.Object.FindFirstObjectByType<PlayerProfileApiClient>();

            yield return PlayerProgressSyncService.GrantCurrencyRoutine(
                runner,
                apiClient,
                playerId,
                RewardedCoinsAmount,
                PlayerProgressSyncService.RewardedAdGrantType,
                sourceId,
                (ok, _) =>
                {
                    grantCompleted = true;
                    grantSuccess = ok;
                });

            if (!grantCompleted)
            {
                onCompleted?.Invoke(false);
                yield break;
            }

            onCompleted?.Invoke(grantSuccess);
#else
            onCompleted?.Invoke(false);
            yield break;
#endif
        }
    }
}
