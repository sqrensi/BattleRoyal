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
                yield return runner.StartCoroutine(AuthorizeAndBindPlayerIdRoutine());
            }

            onPlayerIdReady?.Invoke(PlayerIdentityService.GetOrCreatePlayerId());
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

        private static IEnumerator WaitForYandexSdkRoutine()
        {
            var deadline = Time.unscaledTime + SdkTimeoutSeconds;
            while (!YG2.isSDKEnabled && Time.unscaledTime < deadline)
            {
                yield return null;
            }
        }

        private static IEnumerator WaitForYandexPlayerDataRoutine(float timeoutSeconds)
        {
            if (HasValidYandexPlayerId())
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
                if (HasValidYandexPlayerId())
                {
                    break;
                }

                if (completed && HasValidYandexPlayerId())
                {
                    break;
                }

                yield return null;
            }

            YG2.onGetSDKData -= HandleSdkData;
        }

        private static bool TryApplyCurrentYandexPlayerId()
        {
            if (!HasValidYandexPlayerId())
            {
                return false;
            }

            return PlayerIdentityService.TryApplyYandexUniqueId(YG2.player.id);
        }

        private static void TryApplySavedYandexPlayerId()
        {
            if (PlayerIdentityService.TryGetSavedYandexUniqueId(out var savedUniqueId))
            {
                PlayerIdentityService.TryApplyYandexUniqueId(savedUniqueId);
            }
        }

        private static bool HasValidYandexPlayerId()
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
