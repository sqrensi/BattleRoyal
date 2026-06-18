using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    public static class AchievementToastController
    {
        private const float LeftEdgeOffset = 16f;
        private const float DisplaySeconds = 5f;
        private const float SlideInDuration = 0.45f;
        private const float SlideOutDuration = 0.38f;
        private const float PanelWidth = 392f;
        private const float HiddenOffsetX = LeftEdgeOffset - PanelWidth - 24f;
        private const float VisibleOffsetX = LeftEdgeOffset;
        private const float VerticalOffsetY = 72f;

        private static Sprite whiteSprite;
        private static Canvas toastCanvas;
        private static RectTransform toastPanel;
        private static CanvasGroup toastGroup;
        private static TextMeshProUGUI headerLabel;
        private static TextMeshProUGUI titleLabel;
        private static TextMeshProUGUI descriptionLabel;
        private static Coroutine activeRoutine;
        private static MonoBehaviour runner;

        public static void Show(string title, string description)
        {
            EnsureToastExists();
            if (toastPanel == null || titleLabel == null || toastGroup == null)
            {
                return;
            }

            headerLabel.text = "Достижение выполнено";
            titleLabel.text = string.IsNullOrWhiteSpace(title) ? "Новое достижение" : title;
            descriptionLabel.text = string.IsNullOrWhiteSpace(description)
                ? "Заберите награду в главном меню"
                : description;

            toastPanel.gameObject.SetActive(true);

            if (runner != null && activeRoutine != null)
            {
                runner.StopCoroutine(activeRoutine);
            }

            if (runner != null)
            {
                activeRoutine = runner.StartCoroutine(ShowRoutine());
            }
        }

        private static IEnumerator ShowRoutine()
        {
            toastGroup.alpha = 0f;
            toastPanel.anchoredPosition = new Vector2(HiddenOffsetX, VerticalOffsetY);

            var elapsed = 0f;
            while (elapsed < SlideInDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = SlideInDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / SlideInDuration);
                var eased = EaseOutCubic(t);
                toastGroup.alpha = eased;
                toastPanel.anchoredPosition = new Vector2(
                    Mathf.Lerp(HiddenOffsetX, VisibleOffsetX, eased),
                    VerticalOffsetY);
                yield return null;
            }

            toastGroup.alpha = 1f;
            toastPanel.anchoredPosition = new Vector2(VisibleOffsetX, VerticalOffsetY);

            yield return new WaitForSecondsRealtime(DisplaySeconds);

            elapsed = 0f;
            while (elapsed < SlideOutDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = SlideOutDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / SlideOutDuration);
                var eased = EaseInCubic(t);
                toastGroup.alpha = 1f - eased;
                toastPanel.anchoredPosition = new Vector2(
                    Mathf.Lerp(VisibleOffsetX, HiddenOffsetX, eased),
                    VerticalOffsetY);
                yield return null;
            }

            toastGroup.alpha = 0f;
            toastPanel.anchoredPosition = new Vector2(HiddenOffsetX, VerticalOffsetY);
            toastPanel.gameObject.SetActive(false);
            activeRoutine = null;
        }

        private static void EnsureToastExists()
        {
            if (toastCanvas != null)
            {
                return;
            }

            var canvasObject = new GameObject("AchievementToastCanvas");
            toastCanvas = canvasObject.AddComponent<Canvas>();
            toastCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            toastCanvas.sortingOrder = 600;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GraphicRaycaster>();

            var canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = Vector2.zero;
            canvasRect.offsetMax = Vector2.zero;

            runner = canvasObject.AddComponent<AchievementToastRunner>();

            var panelObject = new GameObject("AchievementToastPanel");
            panelObject.transform.SetParent(canvasObject.transform, false);
            toastPanel = panelObject.AddComponent<RectTransform>();
            toastPanel.anchorMin = new Vector2(0f, 0.5f);
            toastPanel.anchorMax = new Vector2(0f, 0.5f);
            toastPanel.pivot = new Vector2(0f, 0.5f);
            toastPanel.anchoredPosition = new Vector2(HiddenOffsetX, VerticalOffsetY);
            toastPanel.sizeDelta = new Vector2(PanelWidth, 108f);

            toastGroup = panelObject.AddComponent<CanvasGroup>();
            toastGroup.alpha = 0f;
            toastGroup.interactable = false;
            toastGroup.blocksRaycasts = false;

            var background = panelObject.AddComponent<Image>();
            background.sprite = GetWhiteSprite();
            background.color = new Color(0.07f, 0.09f, 0.11f, 0.96f);
            background.raycastTarget = false;

            var accentObject = new GameObject("Accent", typeof(RectTransform));
            accentObject.transform.SetParent(panelObject.transform, false);
            var accentRect = accentObject.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.pivot = new Vector2(0f, 0.5f);
            accentRect.anchoredPosition = Vector2.zero;
            accentRect.sizeDelta = new Vector2(5f, 0f);
            var accentImage = accentObject.AddComponent<Image>();
            accentImage.sprite = GetWhiteSprite();
            accentImage.color = new Color(0.22f, 0.58f, 0.5f, 1f);
            accentImage.raycastTarget = false;

            var headerObject = new GameObject("Header", typeof(RectTransform));
            headerObject.transform.SetParent(panelObject.transform, false);
            var headerRect = headerObject.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(18f, -30f);
            headerRect.offsetMax = new Vector2(-14f, -8f);
            headerLabel = headerObject.AddComponent<TextMeshProUGUI>();
            headerLabel.text = "Достижение выполнено";
            headerLabel.fontSize = 14f;
            headerLabel.fontStyle = FontStyles.Bold;
            headerLabel.characterSpacing = 4f;
            headerLabel.color = new Color(0.22f, 0.58f, 0.5f, 1f);
            headerLabel.alignment = TextAlignmentOptions.TopLeft;

            var titleObject = new GameObject("Title", typeof(RectTransform));
            titleObject.transform.SetParent(panelObject.transform, false);
            var titleRect = titleObject.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0.5f);
            titleRect.anchorMax = new Vector2(1f, 0.5f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.offsetMin = new Vector2(18f, -8f);
            titleRect.offsetMax = new Vector2(-14f, 28f);
            titleLabel = titleObject.AddComponent<TextMeshProUGUI>();
            titleLabel.fontSize = 24f;
            titleLabel.fontStyle = FontStyles.Bold;
            titleLabel.color = new Color(0.95f, 0.82f, 0.35f, 1f);
            titleLabel.alignment = TextAlignmentOptions.TopLeft;

            var descriptionObject = new GameObject("Description", typeof(RectTransform));
            descriptionObject.transform.SetParent(panelObject.transform, false);
            var descriptionRect = descriptionObject.GetComponent<RectTransform>();
            descriptionRect.anchorMin = new Vector2(0f, 0f);
            descriptionRect.anchorMax = new Vector2(1f, 0f);
            descriptionRect.pivot = new Vector2(0.5f, 0f);
            descriptionRect.offsetMin = new Vector2(18f, 12f);
            descriptionRect.offsetMax = new Vector2(-14f, 44f);
            descriptionLabel = descriptionObject.AddComponent<TextMeshProUGUI>();
            descriptionLabel.fontSize = 17f;
            descriptionLabel.color = new Color(0.88f, 0.92f, 0.96f, 0.96f);
            descriptionLabel.alignment = TextAlignmentOptions.TopLeft;

            panelObject.SetActive(false);
        }

        private static float EaseOutCubic(float t)
        {
            var inv = 1f - t;
            return 1f - inv * inv * inv;
        }

        private static float EaseInCubic(float t)
        {
            return t * t * t;
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

        private sealed class AchievementToastRunner : MonoBehaviour
        {
        }
    }
}
