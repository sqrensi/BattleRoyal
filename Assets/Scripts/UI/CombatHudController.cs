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
        private const float DamageFadeInSeconds = 0.08f;
        private const float DamageHoldSeconds = 0.12f;
        private const float DamageFadeOutSeconds = 0.38f;
        private const float TrailCatchUpSpeed = 2.4f;

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

            if (hpFillRect != null)
            {
                return;
            }

            if (hpFillImage != null)
            {
                var oldRoot = hostCanvas.transform.Find("HealthBarRoot");
                if (oldRoot != null)
                {
                    Destroy(oldRoot.gameObject);
                }

                var oldLeft = hostCanvas.transform.Find("LeftDamageArc");
                if (oldLeft != null)
                {
                    Destroy(oldLeft.gameObject);
                }

                var oldRight = hostCanvas.transform.Find("RightDamageArc");
                if (oldRight != null)
                {
                    Destroy(oldRight.gameObject);
                }

                hpFillImage = null;
                hpTrailImage = null;
                hpBackgroundImage = null;
                leftDamageArc = null;
                rightDamageArc = null;
            }

            canvas = hostCanvas;
            BuildLayout(hostCanvas.transform);
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
            damageOverlayTarget = Mathf.Max(damageOverlayTarget, 0.45f + intensity * 0.5f);
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
            arc.enabled = alpha > 0.01f;
        }

        private void BuildLayout(Transform root)
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

            leftDamageArc = CreateDamageArc(root, "LeftDamageArc", true);
            rightDamageArc = CreateDamageArc(root, "RightDamageArc", false);
            leftDamageArc.transform.SetAsLastSibling();
            rightDamageArc.transform.SetAsLastSibling();
        }

        private static Image CreateDamageArc(Transform root, string name, bool leftSide)
        {
            var arcObject = CreateRect(name, root);
            var rect = arcObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(leftSide ? 0f : 1f, 0f);
            rect.anchorMax = new Vector2(leftSide ? 0f : 1f, 1f);
            rect.pivot = new Vector2(leftSide ? 0f : 1f, 0.5f);
            rect.sizeDelta = new Vector2(180f, 0f);
            rect.anchoredPosition = Vector2.zero;

            var image = arcObject.AddComponent<Image>();
            image.raycastTarget = false;
            image.sprite = CreateSideArcSprite(leftSide);
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.color = new Color(0.98f, 0.08f, 0.14f, 0f);
            image.enabled = false;
            return image;
        }

        private static Sprite CreateSideArcSprite(bool leftSide)
        {
            const int width = 128;
            const int height = 512;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            var centerX = leftSide ? 0f : width - 1f;
            var centerY = height * 0.5f;
            var radius = height * 0.46f;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var dx = x - centerX;
                    var dy = y - centerY;
                    var dist = Mathf.Sqrt(dx * dx + dy * dy);
                    var ring = 1f - Mathf.Clamp01(Mathf.Abs(dist - radius) / (radius * 0.16f));
                    var edgeFade = leftSide
                        ? Mathf.Clamp01(1f - x / (width * 0.72f))
                        : Mathf.Clamp01(1f - (width - 1f - x) / (width * 0.72f));
                    var verticalFade = 1f - Mathf.Abs(y - centerY) / (height * 0.44f);
                    var alpha = ring * edgeFade * verticalFade;
                    alpha = Mathf.Pow(alpha, 0.75f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply(false, true);
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(leftSide ? 0f : 1f, 0.5f),
                100f);
        }

        private static Sprite GetWhiteSprite()
        {
            if (whiteSprite != null)
            {
                return whiteSprite;
            }

            whiteSprite = Resources.GetBuiltinResource<Sprite>("UISprite.psd");
            if (whiteSprite != null)
            {
                return whiteSprite;
            }

            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply(false, true);
            whiteSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
            return whiteSprite;
        }

        private static GameObject CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go;
        }
    }
}
