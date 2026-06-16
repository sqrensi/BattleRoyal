using ShooterPrototype.Player;
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
        private const int DamageOverlayVersion = 2;

        private static Sprite whiteSprite;

        private Canvas canvas;
        private RectTransform hpFillRect;
        private RectTransform hpTrailRect;
        private Image hpFillImage;
        private Image hpTrailImage;
        private Image hpBackgroundImage;
        private Image leftDamageArc;
        private Image rightDamageArc;

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
            damageOverlayAlpha = 0f;
            damageOverlayTarget = 0f;
        }

        public void SetActiveForScene(bool active)
        {
            if (canvas == null)
            {
                return;
            }

            if (!active)
            {
                UnbindHealth();
                displayedHealthRatio = 1f;
                trailHealthRatio = 1f;
                lastObservedHealth = -1f;
                damageOverlayAlpha = 0f;
                damageOverlayTarget = 0f;
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
        }

        private void OnDestroy()
        {
            UnbindHealth();
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
                ApplyBarVisuals(new Color(0.72f, 0.18f, 0.28f, 0.95f));
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
            hpTrailImage.color = new Color(0.82f, 0.16f, 0.2f, 0.82f);
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
                return new Color(0.92f, 0.24f, 0.42f, 0.98f);
            }

            if (ratio <= LowHealthYellowThreshold)
            {
                var t = (ratio - CriticalHealthThreshold) /
                        Mathf.Max(0.001f, LowHealthYellowThreshold - CriticalHealthThreshold);
                return Color.Lerp(
                    new Color(0.92f, 0.24f, 0.42f, 0.98f),
                    new Color(0.95f, 0.78f, 0.18f, 0.98f),
                    t);
            }

            return new Color(0.22f, 0.78f, 0.34f, 0.98f);
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
            hpBackgroundImage.sprite = GetWhiteSprite();
            hpBackgroundImage.type = Image.Type.Simple;
            hpBackgroundImage.color = new Color(0.08f, 0.08f, 0.1f, 0.72f);
            hpBackgroundImage.raycastTarget = false;

            var trailObject = CreateRect("HealthBarTrail", barRoot.transform);
            hpTrailRect = trailObject.GetComponent<RectTransform>();
            hpTrailImage = trailObject.AddComponent<Image>();
            hpTrailImage.sprite = GetWhiteSprite();
            hpTrailImage.type = Image.Type.Simple;
            hpTrailImage.color = new Color(0.82f, 0.16f, 0.2f, 0.82f);
            hpTrailImage.raycastTarget = false;
            SetHorizontalFill(hpTrailRect, 1f);

            var fillObject = CreateRect("HealthBarFill", barRoot.transform);
            hpFillRect = fillObject.GetComponent<RectTransform>();
            hpFillImage = fillObject.AddComponent<Image>();
            hpFillImage.sprite = GetWhiteSprite();
            hpFillImage.type = Image.Type.Simple;
            hpFillImage.color = new Color(0.22f, 0.78f, 0.34f, 0.98f);
            hpFillImage.raycastTarget = false;
            SetHorizontalFill(hpFillRect, 1f);
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
            image.color = new Color(0.98f, 0.05f, 0.1f, 0f);
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

        private static Sprite GetWhiteSprite()
        {
            if (whiteSprite != null)
            {
                return whiteSprite;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, Color.white);
            texture.SetPixel(1, 0, Color.white);
            texture.SetPixel(0, 1, Color.white);
            texture.SetPixel(1, 1, Color.white);
            texture.Apply(false, false);
            whiteSprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f), 100f);
            return whiteSprite;
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
