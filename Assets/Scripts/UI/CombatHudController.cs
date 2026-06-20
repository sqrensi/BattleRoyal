using ShooterPrototype.Network;
using ShooterPrototype.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class CombatHudController : MonoBehaviour
    {
        private const float BarWidth = 280f;
        private const float BarHeight = 14f;
        private const float BarBottomOffset = 28f;
        private const float LowHealthYellowThreshold = 0.55f;
        private const float CriticalHealthThreshold = 0.3f;
        private const float DamageFadeInSeconds = 0.07f;
        private const float DamageHoldSeconds = 0.14f;
        private const float DamageFadeOutSeconds = 0.42f;
        private const float TrailCatchUpSpeed = 2.4f;
        private const int DamageOverlayVersion = 3;
        private const float KillBannerFadeInSeconds = 0.28f;
        private const float KillBannerHoldSeconds = 2.2f;
        private const float DeathBannerHoldSeconds = 4f;
        private const float KillBannerFadeOutSeconds = 0.45f;
        private const float KillBannerBottomOffset = 58f;

        private Canvas canvas;
        private RectTransform hpFillRect;
        private RectTransform hpTrailRect;
        private Image hpFillImage;
        private Image hpTrailImage;
        private Image hpBackgroundImage;
        private Image leftDamageArc;
        private Image rightDamageArc;
        private RectTransform killBannerRoot;
        private CanvasGroup killBannerGroup;
        private TMP_Text killBannerText;
        private RealtimeTransportClient transportClient;
        private float killBannerAlpha;
        private float killBannerTargetAlpha;
        private float killBannerHideAt;
        private bool sceneActive;

        private PlayerHealth trackedHealth;
        private float displayedHealthRatio = 1f;
        private float trailHealthRatio = 1f;
        private float lastObservedHealth = -1f;
        private float damageOverlayAlpha;
        private float damageOverlayTarget;
        private float damagePulseUntil;

        public void EnsureOnCanvas(Canvas hostCanvas)
        {
            if (hostCanvas == null)
            {
                return;
            }

            canvas = hostCanvas;

            if (hpFillRect == null)
            {
                if (hpFillImage != null)
                {
                    DestroyUiChild(hostCanvas.transform, "HealthBarRoot");
                    hpFillImage = null;
                    hpTrailImage = null;
                    hpBackgroundImage = null;
                }

                BuildHealthBar(hostCanvas.transform);
            }

            EnsureDamageOverlay(hostCanvas.transform);
            EnsureKillBanner(hostCanvas.transform);
            damageOverlayAlpha = 0f;
            damageOverlayTarget = 0f;
        }

        public void SetActiveForScene(bool active)
        {
            sceneActive = active;
            if (canvas == null)
            {
                return;
            }

            if (!active)
            {
                UnbindHealth();
                UnbindKillFeed();
                displayedHealthRatio = 1f;
                trailHealthRatio = 1f;
                lastObservedHealth = -1f;
                damageOverlayAlpha = 0f;
                damageOverlayTarget = 0f;
                killBannerAlpha = 0f;
                killBannerTargetAlpha = 0f;
                if (killBannerRoot != null)
                {
                    killBannerRoot.gameObject.SetActive(false);
                }
            }
            else
            {
                BindKillFeed();
            }
        }

        private void Update()
        {
            if (hpFillImage == null)
            {
                return;
            }

            EnsureHealthBinding();
            RefreshHealthBar();
            TickDamageOverlay();
            TickKillBanner();
        }

        private void OnDestroy()
        {
            UnbindHealth();
            UnbindKillFeed();
        }

        private void BindKillFeed()
        {
            if (transportClient == null)
            {
                transportClient = RealtimeTransportClient.Active != null
                    ? RealtimeTransportClient.Active
                    : FindFirstObjectByType<RealtimeTransportClient>();
            }

            if (transportClient == null)
            {
                return;
            }

            transportClient.KillFeedReceived -= HandleKillFeed;
            transportClient.KillFeedReceived += HandleKillFeed;
        }

        private void UnbindKillFeed()
        {
            if (transportClient != null)
            {
                transportClient.KillFeedReceived -= HandleKillFeed;
            }
        }

        private void HandleKillFeed(RealtimeTransportClient.KillFeedMessage message)
        {
            if (!sceneActive || message == null)
            {
                return;
            }

            var localTicketId = ResolveLocalTicketId();
            if (string.IsNullOrWhiteSpace(localTicketId))
            {
                return;
            }

            if (string.Equals(message.victimTicketId, localTicketId, System.StringComparison.Ordinal))
            {
                if (string.Equals(message.cause, "player", System.StringComparison.OrdinalIgnoreCase))
                {
                    ShowDeathBanner(message.killerNickname);
                }
                else if (string.Equals(message.cause, "zone", System.StringComparison.OrdinalIgnoreCase))
                {
                    ShowDeathBanner(null, fromZone: true);
                }

                return;
            }

            if (!string.Equals(message.cause, "player", System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!string.Equals(message.killerTicketId, localTicketId, System.StringComparison.Ordinal))
            {
                return;
            }

            ShowKillBanner(message.victimNickname);
        }

        private static string ResolveLocalTicketId()
        {
            var launcher = FindFirstObjectByType<NetworkLauncher>();
            if (launcher != null && !string.IsNullOrWhiteSpace(launcher.CurrentTicketId))
            {
                return launcher.CurrentTicketId.Trim();
            }

            var transport = RealtimeTransportClient.Active != null
                ? RealtimeTransportClient.Active
                : FindFirstObjectByType<RealtimeTransportClient>();
            return string.IsNullOrWhiteSpace(transport?.ConnectedTicketId)
                ? string.Empty
                : transport.ConnectedTicketId.Trim();
        }

        private void ShowKillBanner(string victimNickname)
        {
            if (killBannerRoot == null || killBannerText == null || killBannerGroup == null)
            {
                return;
            }

            var victim = string.IsNullOrWhiteSpace(victimNickname) ? "Игрок" : victimNickname.Trim();
            killBannerText.text = $"Вы убили {victim}";
            killBannerText.color = UiTheme.TextAccent;
            ActivateBanner(KillBannerFadeInSeconds + KillBannerHoldSeconds);
        }

        private void ShowDeathBanner(string killerNickname, bool fromZone = false)
        {
            if (killBannerRoot == null || killBannerText == null || killBannerGroup == null)
            {
                return;
            }

            if (fromZone)
            {
                killBannerText.text = "Зона вас убила";
            }
            else
            {
                var killer = string.IsNullOrWhiteSpace(killerNickname) ? "Игрок" : killerNickname.Trim();
                killBannerText.text = $"{killer} вас убил";
            }

            killBannerText.color = UiTheme.Danger;
            ActivateBanner(KillBannerFadeInSeconds + DeathBannerHoldSeconds);
        }

        private void ActivateBanner(float visibleSeconds)
        {
            killBannerRoot.gameObject.SetActive(true);
            killBannerAlpha = 0f;
            killBannerTargetAlpha = 1f;
            killBannerHideAt = Time.unscaledTime + visibleSeconds;
        }

        private void TickKillBanner()
        {
            if (killBannerRoot == null || killBannerGroup == null || !killBannerRoot.gameObject.activeSelf)
            {
                return;
            }

            if (Time.unscaledTime >= killBannerHideAt)
            {
                killBannerTargetAlpha = 0f;
            }

            var fadeSpeed = killBannerTargetAlpha > killBannerAlpha
                ? 1f / Mathf.Max(0.01f, KillBannerFadeInSeconds)
                : 1f / Mathf.Max(0.01f, KillBannerFadeOutSeconds);
            killBannerAlpha = Mathf.MoveTowards(
                killBannerAlpha,
                killBannerTargetAlpha,
                fadeSpeed * Time.unscaledDeltaTime);
            killBannerGroup.alpha = killBannerAlpha;

            if (killBannerAlpha <= 0.001f && killBannerTargetAlpha <= 0.001f)
            {
                killBannerRoot.gameObject.SetActive(false);
            }
        }

        private void EnsureHealthBinding()
        {
            var local = FindFirstObjectByType<LocalPlayerMarker>();
            var health = local != null ? local.GetComponent<PlayerHealth>() : null;
            if (health == trackedHealth)
            {
                return;
            }

            UnbindHealth();
            trackedHealth = health;
            lastObservedHealth = trackedHealth != null ? trackedHealth.CurrentHealth : -1f;
            displayedHealthRatio = trackedHealth != null && trackedHealth.MaxHealth > 0.001f
                ? trackedHealth.CurrentHealth / trackedHealth.MaxHealth
                : 1f;
            trailHealthRatio = displayedHealthRatio;
            if (trackedHealth != null)
            {
                trackedHealth.LocalDamageTaken += HandleLocalDamageTaken;
            }
        }

        private void UnbindHealth()
        {
            if (trackedHealth != null)
            {
                trackedHealth.LocalDamageTaken -= HandleLocalDamageTaken;
                trackedHealth = null;
            }
        }

        private void HandleLocalDamageTaken(float damageAmount)
        {
            if (damageAmount <= 0f)
            {
                return;
            }

            TriggerDamageFeedback(damageAmount);
        }

        private void TriggerDamageFeedback(float damageAmount)
        {
            var intensity = Mathf.Clamp01(damageAmount / 35f);
            damageOverlayTarget = Mathf.Max(damageOverlayTarget, 0.55f + intensity * 0.4f);
            damagePulseUntil = Time.unscaledTime + DamageHoldSeconds;
        }

        private void RefreshHealthBar()
        {
            if (trackedHealth == null || trackedHealth.IsDead)
            {
                displayedHealthRatio = 0f;
                trailHealthRatio = Mathf.MoveTowards(trailHealthRatio, 0f, TrailCatchUpSpeed * Time.unscaledDeltaTime);
                ApplyBarVisuals(UiTheme.HealthLow);
                return;
            }

            var currentHealth = trackedHealth.CurrentHealth;
            if (lastObservedHealth > 0f && currentHealth < lastObservedHealth - 0.01f)
            {
                TriggerDamageFeedback(lastObservedHealth - currentHealth);
            }

            lastObservedHealth = currentHealth;

            var targetRatio = trackedHealth.MaxHealth > 0.001f
                ? Mathf.Clamp01(currentHealth / trackedHealth.MaxHealth)
                : 0f;
            displayedHealthRatio = targetRatio;
            trailHealthRatio = Mathf.MoveTowards(
                trailHealthRatio,
                targetRatio,
                TrailCatchUpSpeed * Time.unscaledDeltaTime);
            ApplyBarVisuals(ResolveHealthColor(targetRatio));
        }

        private void ApplyBarVisuals(Color fillColor)
        {
            SetHorizontalFill(hpFillRect, displayedHealthRatio);
            SetHorizontalFill(hpTrailRect, trailHealthRatio);
            hpFillImage.color = fillColor;
            hpTrailImage.color = new Color(UiTheme.HealthLow.r, UiTheme.HealthLow.g, UiTheme.HealthLow.b, 0.82f);
        }

        private static void SetHorizontalFill(RectTransform rect, float ratio)
        {
            if (rect == null)
            {
                return;
            }

            ratio = Mathf.Clamp01(ratio);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(ratio, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Color ResolveHealthColor(float ratio)
        {
            if (ratio <= CriticalHealthThreshold)
            {
                return UiTheme.HealthLow;
            }

            if (ratio <= LowHealthYellowThreshold)
            {
                var t = (ratio - CriticalHealthThreshold) /
                        Mathf.Max(0.001f, LowHealthYellowThreshold - CriticalHealthThreshold);
                return Color.Lerp(UiTheme.HealthLow, UiTheme.HealthMid, t);
            }

            return UiTheme.HealthHigh;
        }

        private void TickDamageOverlay()
        {
            if (Time.unscaledTime >= damagePulseUntil)
            {
                damageOverlayTarget = 0f;
            }

            var fadeSpeed = damageOverlayTarget > damageOverlayAlpha
                ? 1f / Mathf.Max(0.01f, DamageFadeInSeconds)
                : 1f / Mathf.Max(0.01f, DamageFadeOutSeconds);
            damageOverlayAlpha = Mathf.MoveTowards(
                damageOverlayAlpha,
                damageOverlayTarget,
                fadeSpeed * Time.unscaledDeltaTime);

            ApplyArcAlpha(leftDamageArc, damageOverlayAlpha);
            ApplyArcAlpha(rightDamageArc, damageOverlayAlpha);
        }

        private static void ApplyArcAlpha(Image arc, float alpha)
        {
            if (arc == null)
            {
                return;
            }

            var color = arc.color;
            color.a = alpha;
            arc.color = color;
            arc.enabled = alpha > 0.005f;
        }

        private void BuildHealthBar(Transform root)
        {
            var barRoot = CreateRect("HealthBarRoot", root);
            var barRect = barRoot.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0.5f, 0f);
            barRect.anchorMax = new Vector2(0.5f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.sizeDelta = new Vector2(BarWidth, BarHeight);
            barRect.anchoredPosition = new Vector2(0f, BarBottomOffset);

            hpBackgroundImage = barRoot.AddComponent<Image>();
            UiTheme.ApplyPanel(hpBackgroundImage, UiPanelStyle.Hud);
            hpBackgroundImage.raycastTarget = false;

            var trailObject = CreateRect("HealthBarTrail", barRoot.transform);
            hpTrailRect = trailObject.GetComponent<RectTransform>();
            hpTrailImage = trailObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(hpTrailImage, UiTheme.HealthLow);
            hpTrailImage.color = new Color(UiTheme.HealthLow.r, UiTheme.HealthLow.g, UiTheme.HealthLow.b, 0.82f);
            hpTrailImage.raycastTarget = false;
            SetHorizontalFill(hpTrailRect, 1f);

            var fillObject = CreateRect("HealthBarFill", barRoot.transform);
            hpFillRect = fillObject.GetComponent<RectTransform>();
            hpFillImage = fillObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(hpFillImage, UiTheme.HealthHigh);
            hpFillImage.raycastTarget = false;
            SetHorizontalFill(hpFillRect, 1f);

            CreateHealthSegmentDivider(barRoot.transform, 0.25f);
            CreateHealthSegmentDivider(barRoot.transform, 0.5f);
            CreateHealthSegmentDivider(barRoot.transform, 0.75f);
        }

        private static void CreateHealthSegmentDivider(Transform parent, float normalizedX)
        {
            var dividerObject = CreateRect("HealthSegment_" + normalizedX, parent);
            var rect = dividerObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(normalizedX, 0f);
            rect.anchorMax = new Vector2(normalizedX, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1f, 0f);
            var image = dividerObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(image, new Color(0f, 0f, 0f, 0.35f));
            image.raycastTarget = false;
        }

        private void EnsureKillBanner(Transform root)
        {
            if (killBannerRoot != null)
            {
                return;
            }

            var bannerObject = CreateRect("KillBannerRoot", root);
            killBannerRoot = bannerObject.GetComponent<RectTransform>();
            killBannerRoot.anchorMin = new Vector2(0.5f, 0f);
            killBannerRoot.anchorMax = new Vector2(0.5f, 0f);
            killBannerRoot.pivot = new Vector2(0.5f, 0f);
            killBannerRoot.anchoredPosition = new Vector2(0f, KillBannerBottomOffset);
            killBannerRoot.sizeDelta = new Vector2(520f, 42f);

            var background = bannerObject.AddComponent<Image>();
            UiTheme.ApplyPanel(background, UiPanelStyle.Hud);
            background.raycastTarget = false;

            killBannerGroup = bannerObject.AddComponent<CanvasGroup>();
            killBannerGroup.alpha = 0f;

            var labelObject = CreateRect("Label", bannerObject.transform);
            var labelRect = labelObject.GetComponent<RectTransform>();
            StretchFull(labelRect);
            labelRect.offsetMin = new Vector2(16f, 6f);
            labelRect.offsetMax = new Vector2(-16f, -6f);

            killBannerText = labelObject.AddComponent<TextMeshProUGUI>();
            UiTheme.ApplyTmp(killBannerText, UiTextRole.Accent);
            killBannerText.fontSize = 24f;
            killBannerText.alignment = TextAlignmentOptions.Center;
            killBannerText.raycastTarget = false;

            bannerObject.SetActive(false);
        }

        private void EnsureDamageOverlay(Transform root)
        {
            var overlayRoot = root.Find("DamageOverlayRoot");
            if (overlayRoot != null)
            {
                var versionMarker = overlayRoot.GetComponent<DamageOverlayVersionMarker>();
                if (versionMarker != null && versionMarker.Version >= DamageOverlayVersion)
                {
                    leftDamageArc = overlayRoot.Find("LeftDamageArc")?.GetComponent<Image>();
                    rightDamageArc = overlayRoot.Find("RightDamageArc")?.GetComponent<Image>();
                    overlayRoot.SetAsLastSibling();
                    return;
                }

                Destroy(overlayRoot.gameObject);
            }

            DestroyUiChild(root, "LeftDamageArc");
            DestroyUiChild(root, "RightDamageArc");

            var overlayObject = CreateRect("DamageOverlayRoot", root);
            overlayObject.AddComponent<DamageOverlayVersionMarker>().Version = DamageOverlayVersion;
            var overlayRect = overlayObject.GetComponent<RectTransform>();
            StretchFull(overlayRect);

            leftDamageArc = CreateDamageArc(overlayObject.transform, "LeftDamageArc", true);
            rightDamageArc = CreateDamageArc(overlayObject.transform, "RightDamageArc", false);
            overlayObject.transform.SetAsLastSibling();
        }

        private static Image CreateDamageArc(Transform root, string name, bool leftSide)
        {
            var arcObject = CreateRect(name, root);
            var rect = arcObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(leftSide ? 0f : 1f, 0f);
            rect.anchorMax = new Vector2(leftSide ? 0f : 1f, 1f);
            rect.pivot = new Vector2(leftSide ? 0f : 1f, 0.5f);
            rect.sizeDelta = new Vector2(260f, 0f);
            rect.anchoredPosition = Vector2.zero;

            var image = arcObject.AddComponent<Image>();
            image.raycastTarget = false;
            image.sprite = CreateSideArcSprite(leftSide);
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.color = new Color(UiTheme.Danger.r, UiTheme.Danger.g, UiTheme.Danger.b, 0f);
            image.enabled = false;
            return image;
        }

        private static Sprite CreateSideArcSprite(bool leftSide)
        {
            const int width = 160;
            const int height = 512;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            var centerY = height * 0.5f;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var edgeDistance = leftSide ? x : (width - 1 - x);
                    var edgeFade = 1f - (edgeDistance / (width - 1f));
                    edgeFade = Mathf.Pow(Mathf.Clamp01(edgeFade), 1.35f);

                    var verticalFade = 1f - Mathf.Abs(y - centerY) / (height * 0.46f);
                    verticalFade = Mathf.Clamp01(verticalFade);
                    verticalFade = Mathf.Pow(verticalFade, 0.85f);

                    var innerX = leftSide ? x : (width - 1 - x);
                    var arcRadius = height * 0.34f;
                    var arcCenterX = arcRadius;
                    var arcCenterY = centerY;
                    var arcDx = innerX - arcCenterX;
                    var arcDy = y - arcCenterY;
                    var arcDist = Mathf.Sqrt(arcDx * arcDx + arcDy * arcDy);
                    var arcBand = 1f - Mathf.Clamp01(Mathf.Abs(arcDist - arcRadius) / (arcRadius * 0.11f));
                    arcBand = Mathf.Pow(arcBand, 1.2f);

                    var alpha = Mathf.Clamp01(edgeFade * 0.55f + arcBand * edgeFade * 0.75f) * verticalFade;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply(false, false);
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(leftSide ? 0f : 1f, 0.5f),
                100f);
        }

        private static void DestroyUiChild(Transform root, string childName)
        {
            var child = root.Find(childName);
            if (child != null)
            {
                Object.Destroy(child.gameObject);
            }
        }

        private static GameObject CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private sealed class DamageOverlayVersionMarker : MonoBehaviour
        {
            public int Version;
        }
    }
}
