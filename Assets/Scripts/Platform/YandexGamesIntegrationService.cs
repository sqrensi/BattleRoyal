using System;
using System.Collections;
using ShooterPrototype.Player;
using UnityEngine;
using YG;

namespace ShooterPrototype.Platform
{
    public static class YandexGamesIntegrationService
    {
        private const float SdkTimeoutSeconds = 15f;
        private const float AuthTimeoutSeconds = 45f;
        private const float PaymentsCatalogTimeoutSeconds = 15f;

        public const string ProfileSyncAuthReason =
            "Вход через Яндекс ID сохраняет прогресс в облаке.\n\n" +
            "Преимущества:\n" +
            "• рейтинг, инвентарь и покупки на любом устройстве;\n" +
            "• прогресс не привязан к одному браузеру.\n\n" +
            "Онлайн-матчи доступны и без входа.";

        public const string DefaultAuthReason =
            "Для этой функции нужен вход через Яндекс ID.\n\n" +
            "Преимущества:\n" +
            "• сохранение прогресса в облаке;\n" +
            "• покупки и инвентарь на любом устройстве.";

        private static bool gameReadySent;

        public static IEnumerator PrepareAccountAndBindProfile(MonoBehaviour runner, Action<string> onPlayerIdReady)
        {
            if (runner == null)
            {
                onPlayerIdReady?.Invoke(PlayerIdentityService.GetOrCreatePlayerId());
                yield break;
            }

            if (ShouldUseYandexIntegration())
            {
                yield return runner.StartCoroutine(EnsureSdkReadyRoutine());
                if (IsYandexPlayerAuthorized())
                {
                    TryApplyCurrentYandexPlayerId();
                }
                else
                {
                    TryApplySavedYandexPlayerId();
                }
            }

            onPlayerIdReady?.Invoke(PlayerIdentityService.GetOrCreatePlayerId());
        }

        public static IEnumerator RequestAuthorizationIfNeeded(
            MonoBehaviour runner,
            string reason,
            Action<bool> onComplete)
        {
            if (runner == null)
            {
                onComplete?.Invoke(false);
                yield break;
            }

            if (!ShouldUseYandexIntegration())
            {
                onComplete?.Invoke(true);
                yield break;
            }

            yield return runner.StartCoroutine(EnsureSdkReadyRoutine());

            if (IsYandexPlayerAuthorized())
            {
                TryApplyCurrentYandexPlayerId();
                onComplete?.Invoke(true);
                yield break;
            }

            if (PlayerIdentityService.HasAuthorizedYandexLink())
            {
                TryApplySavedYandexPlayerId();
                YG2.GetAuth();
                yield return WaitForYandexPlayerDataRoutine(3f);
                if (IsYandexPlayerAuthorized())
                {
                    TryApplyCurrentYandexPlayerId();
                    onComplete?.Invoke(true);
                    yield break;
                }
            }

            var answered = false;
            var accepted = false;
            YandexGamesAuthPrompt.Show(reason, granted =>
            {
                accepted = granted;
                answered = true;
            });

            while (!answered)
            {
                yield return null;
            }

            if (!accepted)
            {
                onComplete?.Invoke(false);
                yield break;
            }

            yield return runner.StartCoroutine(AuthorizeAndBindPlayerIdRoutine());
            onComplete?.Invoke(IsYandexPlayerAuthorized());
        }

        public static IEnumerator WaitForPaymentsCatalog(MonoBehaviour runner)
        {
            if (runner == null || !ShouldUseYandexIntegration())
            {
                yield break;
            }

            yield return runner.StartCoroutine(WaitForPaymentsCatalogRoutine());
        }

        public static bool IsProductInPaymentsCatalog(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId))
            {
                return false;
            }

            return YG2.PurchaseByID(productId.Trim()) != null;
        }

        public static void NotifyMainMenuLoadingComplete()
        {
            if (gameReadySent || !ShouldUseYandexIntegration())
            {
                return;
            }

            gameReadySent = true;

            if (YG2.isSDKEnabled)
            {
                YG2.GameReadyAPI();
                return;
            }

            YG2.onGetSDKData += HandleSdkReadyForGameReady;
        }

        public static bool IsYandexPlayerAuthorized()
        {
            return YG2.player.auth && HasResolvableYandexPlayerId();
        }

        private static IEnumerator AuthorizeAndBindPlayerIdRoutine()
        {
            yield return WaitForYandexSdkRoutine();

            if (!YG2.isSDKEnabled)
            {
                TryApplySavedYandexPlayerId();
                yield break;
            }

            YG2.GetAuth();
            yield return WaitForYandexPlayerDataRoutine(AuthTimeoutSeconds);

            if (!YG2.player.auth)
            {
                YG2.OpenAuthDialog();
                yield return WaitForYandexPlayerDataRoutine(AuthTimeoutSeconds);
            }

            if (TryApplyCurrentYandexPlayerId())
            {
                yield break;
            }

            TryApplySavedYandexPlayerId();
        }

        private static IEnumerator EnsureSdkReadyRoutine()
        {
            yield return WaitForYandexSdkRoutine();
        }

        private static IEnumerator WaitForYandexSdkRoutine()
        {
            var deadline = Time.unscaledTime + SdkTimeoutSeconds;
            while (!YG2.isSDKEnabled && Time.unscaledTime < deadline)
            {
                yield return null;
            }
        }

        private static IEnumerator WaitForPaymentsCatalogRoutine()
        {
            yield return WaitForYandexSdkRoutine();

            var deadline = Time.unscaledTime + PaymentsCatalogTimeoutSeconds;
            while (Time.unscaledTime < deadline)
            {
                if (YG2.purchases != null && YG2.purchases.Length > 0)
                {
                    yield break;
                }

                yield return null;
            }
        }

        private static IEnumerator WaitForYandexPlayerDataRoutine(float timeoutSeconds)
        {
            if (IsYandexPlayerAuthorized())
            {
                yield break;
            }

            var completed = false;
            void HandleSdkData()
            {
                completed = true;
            }

            YG2.onGetSDKData += HandleSdkData;

            var deadline = Time.unscaledTime + Mathf.Max(1f, timeoutSeconds);
            while (Time.unscaledTime < deadline)
            {
                if (IsYandexPlayerAuthorized())
                {
                    break;
                }

                if (completed && IsYandexPlayerAuthorized())
                {
                    break;
                }

                yield return null;
            }

            YG2.onGetSDKData -= HandleSdkData;
        }

        private static bool TryApplyCurrentYandexPlayerId()
        {
            if (!IsYandexPlayerAuthorized())
            {
                return false;
            }

            return PlayerIdentityService.TryApplyAuthorizedYandexUniqueId(YG2.player.id);
        }

        private static void TryApplySavedYandexPlayerId()
        {
            if (!PlayerIdentityService.HasAuthorizedYandexLink() ||
                !PlayerIdentityService.TryGetSavedYandexUniqueId(out var savedUniqueId))
            {
                return;
            }

            PlayerIdentityService.TryApplyAuthorizedYandexUniqueId(savedUniqueId);
        }

        private static bool HasResolvableYandexPlayerId()
        {
            var playerId = YG2.player.id;
            if (string.IsNullOrWhiteSpace(playerId))
            {
                return false;
            }

            return !string.Equals(playerId, "unauthorized", StringComparison.OrdinalIgnoreCase);
        }

        private static void HandleSdkReadyForGameReady()
        {
            YG2.onGetSDKData -= HandleSdkReadyForGameReady;
            YG2.GameReadyAPI();
        }

        public static bool IsYandexGamesRuntime()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var pageUrl = Application.absoluteURL;
            return !string.IsNullOrWhiteSpace(pageUrl) &&
                   pageUrl.IndexOf("yandex", StringComparison.OrdinalIgnoreCase) >= 0;
#else
            return false;
#endif
        }

        private static bool ShouldUseYandexIntegration()
        {
            return IsYandexGamesRuntime();
        }
    }
}
