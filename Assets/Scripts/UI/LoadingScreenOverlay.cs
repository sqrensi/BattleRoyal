using ShooterPrototype.Platform;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    public static class LoadingScreenOverlay
    {
        private const int SortingOrder = 9000;

        private static GameObject rootObject;
        private static CanvasGroup canvasGroup;
        private static TMP_Text messageText;
        private static TMP_Text progressText;
        private static int showCount;

        public static bool IsVisible => showCount > 0;

        public static void Show(string message)
        {
            EnsureBuilt();
            showCount++;
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = true;
            rootObject.SetActive(true);
            SetMessage(message);
            SetProgress(-1f);
        }

        public static void Hide()
        {
            if (showCount <= 0)
            {
                return;
            }

            showCount--;
            if (showCount > 0)
            {
                return;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.blocksRaycasts = false;
            }

            if (rootObject != null)
            {
                rootObject.SetActive(false);
            }
        }

        public static void SetMessage(string message)
        {
            if (messageText == null)
            {
                return;
            }

            messageText.text = string.IsNullOrWhiteSpace(message) ? "Загрузка..." : message;
        }

        public static void SetProgress(float normalizedProgress)
        {
            if (progressText == null)
            {
                return;
            }

            if (normalizedProgress < 0f)
            {
                progressText.text = string.Empty;
                return;
            }

            progressText.text = $"{Mathf.Clamp01(normalizedProgress) * 100f:0}%";
        }

        private static void EnsureBuilt()
        {
            if (rootObject != null)
            {
                return;
            }

            rootObject = new GameObject("LoadingScreenOverlay");
            Object.DontDestroyOnLoad(rootObject);

            var canvas = rootObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            rootObject.AddComponent<CanvasScaler>();
            rootObject.AddComponent<GraphicRaycaster>();

            canvasGroup = rootObject.AddComponent<CanvasGroup>();

            var backgroundObject = new GameObject("Background");
            backgroundObject.transform.SetParent(rootObject.transform, false);
            var backgroundRect = backgroundObject.AddComponent<RectTransform>();
            StretchFull(backgroundRect);
            var backgroundImage = backgroundObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(backgroundImage, UiTheme.CanvasDim);
            backgroundImage.raycastTarget = true;

            var panelObject = new GameObject("Panel");
            panelObject.transform.SetParent(rootObject.transform, false);
            var panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(560f, 220f);

            var panelBackground = panelObject.AddComponent<Image>();
            UiTheme.ApplyPanel(panelBackground, UiPanelStyle.Overlay);

            var titleObject = new GameObject("Title");
            titleObject.transform.SetParent(panelObject.transform, false);
            var titleRect = titleObject.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -28f);
            titleRect.sizeDelta = new Vector2(500f, 44f);
            var titleText = titleObject.AddComponent<TextMeshProUGUI>();
            titleText.text = GameBranding.GameTitleUpper;
            titleText.fontSize = 32f;
            titleText.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyMilitaryHeader(titleText, UiTextRole.Title);

            var messageObject = new GameObject("Message");
            messageObject.transform.SetParent(panelObject.transform, false);
            var messageRect = messageObject.AddComponent<RectTransform>();
            messageRect.anchorMin = new Vector2(0.5f, 0.5f);
            messageRect.anchorMax = new Vector2(0.5f, 0.5f);
            messageRect.pivot = new Vector2(0.5f, 0.5f);
            messageRect.anchoredPosition = new Vector2(0f, 8f);
            messageRect.sizeDelta = new Vector2(500f, 72f);
            messageText = messageObject.AddComponent<TextMeshProUGUI>();
            messageText.fontSize = 24f;
            messageText.alignment = TextAlignmentOptions.Center;
            messageText.enableWordWrapping = true;
            UiTheme.ApplyTmp(messageText, UiTextRole.Body);

            var progressObject = new GameObject("Progress");
            progressObject.transform.SetParent(panelObject.transform, false);
            var progressRect = progressObject.AddComponent<RectTransform>();
            progressRect.anchorMin = new Vector2(0.5f, 0f);
            progressRect.anchorMax = new Vector2(0.5f, 0f);
            progressRect.pivot = new Vector2(0.5f, 0f);
            progressRect.anchoredPosition = new Vector2(0f, 28f);
            progressRect.sizeDelta = new Vector2(500f, 36f);
            progressText = progressObject.AddComponent<TextMeshProUGUI>();
            progressText.fontSize = 20f;
            progressText.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyTmp(progressText, UiTextRole.Muted);

            rootObject.SetActive(false);
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
