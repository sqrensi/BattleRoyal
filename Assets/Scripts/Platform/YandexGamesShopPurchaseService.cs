using System;
using System.Collections;
using ShooterPrototype.Player;
using UnityEngine;
#if Payments_yg
using YG;
#endif

namespace ShooterPrototype.Platform
{
    public sealed class YandexGamesShopPurchaseService : MonoBehaviour
    {
        public const string PurchaseAuthReason =
            "Для покупок за реальные деньги нужен вход через Яндекс ID.\n\n" +
            "Преимущества:\n" +
            "• купленные предметы сохраняются в облаке;\n" +
            "• покупки доступны в любом браузере.";

        private static YandexGamesShopPurchaseService instance;
        private static Action<bool> pendingPurchaseCallback;
        private static string pendingProductId = string.Empty;
        private Coroutine pendingPurchaseTimeoutRoutine;

        public static bool IsPurchaseInProgress =>
            !string.IsNullOrWhiteSpace(pendingProductId);

        public static void CancelPendingPurchase()
        {
#if Payments_yg
            CompletePending(false);
#endif
        }

        public static void EnsureInitialized(MonoBehaviour host)
        {
            if (instance != null || host == null)
            {
                return;
            }

            var existing = FindFirstObjectByType<YandexGamesShopPurchaseService>();
            if (existing != null)
            {
                instance = existing;
                return;
            }

            var go = new GameObject(nameof(YandexGamesShopPurchaseService));
            go.transform.SetParent(host.transform, false);
            instance = go.AddComponent<YandexGamesShopPurchaseService>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
#if Payments_yg
            YG2.onPurchaseSuccess += HandlePurchaseSuccess;
            YG2.onPurchaseFailed += HandlePurchaseFailed;
#endif
        }

        private void OnDisable()
        {
#if Payments_yg
            YG2.onPurchaseSuccess -= HandlePurchaseSuccess;
            YG2.onPurchaseFailed -= HandlePurchaseFailed;
#endif
        }

        public static void TryPurchase(
            MonoBehaviour runner,
            string productId,
            Action<bool> onCompleted)
        {
            if (runner == null || string.IsNullOrWhiteSpace(productId))
            {
                onCompleted?.Invoke(false);
                return;
            }

            EnsureInitialized(runner);
            runner.StartCoroutine(PurchaseRoutine(productId, onCompleted));
        }

        private static IEnumerator PurchaseRoutine(string productId, Action<bool> onCompleted)
        {
            if (!ShopIapCatalogService.TryGetProduct(productId, out _) &&
                !CaseCatalogService.TryGetCaseByRealMoneyProductId(productId, out _))
            {
                Debug.LogWarning($"[YandexGamesShopPurchase] Unknown product id '{productId}'.");
                onCompleted?.Invoke(false);
                yield break;
            }

            if (!YandexGamesIntegrationService.IsYandexGamesRuntime())
            {
                Debug.LogWarning("[YandexGamesShopPurchase] Real-money purchases are only available in Yandex Games.");
                onCompleted?.Invoke(false);
                yield break;
            }

            var host = instance != null ? instance : FindFirstObjectByType<MonoBehaviour>();
            var authorized = false;
            yield return YandexGamesIntegrationService.RequestAuthorizationIfNeeded(
                host,
                PurchaseAuthReason,
                granted => authorized = granted);

            if (!authorized)
            {
                onCompleted?.Invoke(false);
                yield break;
            }

            yield return YandexGamesIntegrationService.WaitForPaymentsCatalog(host);

#if Payments_yg
            if (!YandexGamesIntegrationService.IsProductInPaymentsCatalog(productId))
            {
                Debug.LogWarning(
                    $"[YandexGamesShopPurchase] Product '{productId}' is missing in Yandex Games payments catalog.");
                onCompleted?.Invoke(false);
                yield break;
            }
#endif

#if Payments_yg
            if (pendingPurchaseCallback != null)
            {
                var previous = pendingPurchaseCallback;
                pendingPurchaseCallback = null;
                previous.Invoke(false);
            }

            pendingProductId = productId.Trim();
            pendingPurchaseCallback = onCompleted;
            if (instance != null)
            {
                instance.StartPendingPurchaseTimeout();
            }

            YG2.BuyPayments(pendingProductId);
#else
            onCompleted?.Invoke(false);
#endif
        }

#if Payments_yg
        private void HandlePurchaseSuccess(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId))
            {
                CompletePending(false);
                return;
            }

            StartCoroutine(ApplyRewardAndComplete(productId.Trim()));
        }

        private void HandlePurchaseFailed(string productId)
        {
            Debug.LogWarning($"[YandexGamesShopPurchase] Purchase failed for '{productId}'.");
            CompletePending(false);
        }

        private IEnumerator ApplyRewardAndComplete(string productId)
        {
            var success = false;
            if (PlayerProfileService.IsServerSynced && PlayerProfileService.TryResolveApiClient(out var apiClient))
            {
                var playerId = PlayerIdentityService.GetOrCreatePlayerId();
                var error = string.Empty;
                yield return PlayerProfileService.GrantIapProduct(
                    this,
                    apiClient,
                    playerId,
                    productId,
                    (ok, responseError) =>
                    {
                        success = ok;
                        error = responseError;
                    });

                if (!success && !string.IsNullOrWhiteSpace(error))
                {
                    Debug.LogWarning($"[YandexGamesShopPurchase] Server grant failed: {error}");
                }
            }
            else
            {
                success = ApplyLocalReward(productId);
            }

            if (success)
            {
                YG2.ConsumePurchaseByID(productId);
            }

            CompletePending(success);
        }

        private void StartPendingPurchaseTimeout()
        {
            if (pendingPurchaseTimeoutRoutine != null)
            {
                StopCoroutine(pendingPurchaseTimeoutRoutine);
            }

            pendingPurchaseTimeoutRoutine = StartCoroutine(WatchPendingPurchaseTimeoutRoutine());
        }

        private void StopPendingPurchaseTimeout()
        {
            if (pendingPurchaseTimeoutRoutine == null)
            {
                return;
            }

            StopCoroutine(pendingPurchaseTimeoutRoutine);
            pendingPurchaseTimeoutRoutine = null;
        }

        private IEnumerator WatchPendingPurchaseTimeoutRoutine()
        {
            var productId = pendingProductId;
            var deadline = Time.unscaledTime + 120f;
            while (!string.IsNullOrWhiteSpace(pendingProductId) &&
                   string.Equals(pendingProductId, productId, StringComparison.Ordinal) &&
                   Time.unscaledTime < deadline)
            {
                yield return null;
            }

            if (!string.IsNullOrWhiteSpace(pendingProductId) &&
                string.Equals(pendingProductId, productId, StringComparison.Ordinal))
            {
                Debug.LogWarning(
                    $"[YandexGamesShopPurchase] Purchase callback timeout for '{productId}'.");
                CompletePending(false);
            }
        }

        private static void CompletePending(bool success)
        {
            if (instance != null)
            {
                instance.StopPendingPurchaseTimeout();
            }

            pendingProductId = string.Empty;
            var callback = pendingPurchaseCallback;
            pendingPurchaseCallback = null;
            callback?.Invoke(success);
        }
#endif

        private static bool ApplyLocalReward(string productId)
        {
            if (ShopIapCatalogService.TryApplyLocalReward(productId))
            {
                return true;
            }

            if (CaseCatalogService.TryGetCaseByRealMoneyProductId(productId, out var caseDefinition))
            {
                CaseOpeningService.GrantLocalCase(caseDefinition.Id, 1);
                return true;
            }

            return false;
        }
    }
}
