using System;
using ShooterPrototype.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.Platform
{
    public static class YandexGamesAuthPrompt
    {
        private static GameObject overlayRoot;
        private static Action<bool> pendingCallback;

        public static void Show(string reason, Action<bool> onComplete)
        {
            if (pendingCallback != null)
            {
                var previous = pendingCallback;
                pendingCallback = null;
                previous.Invoke(false);
            }

            pendingCallback = onComplete;
            EnsureOverlay();
            overlayRoot.SetActive(true);

            var reasonText = overlayRoot.transform.Find("Panel/ReasonText")?.GetComponent<TextMeshProUGUI>();
            if (reasonText != null)
            {
                reasonText.text = string.IsNullOrWhiteSpace(reason)
                    ? YandexGamesIntegrationService.DefaultAuthReason
                    : reason.Trim();
            }
        }

        public static void Hide(bool result)
        {
            if (overlayRoot != null)
            {
                overlayRoot.SetActive(false);
            }

            var callback = pendingCallback;
            pendingCallback = null;
            callback?.Invoke(result);
        }

        private static void EnsureOverlay()
        {
            if (overlayRoot != null)
            {
                return;
            }

            var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                return;
            }

            overlayRoot = new GameObject("YandexAuthPromptOverlay");
            overlayRoot.transform.SetParent(canvas.transform, false);

            var rootRect = overlayRoot.AddComponent<RectTransform>();
            StretchFull(rootRect);

            var dimmer = overlayRoot.AddComponent<Image>();
            dimmer.color = new Color(0f, 0f, 0f, 0.72f);
            dimmer.raycastTarget = true;

            var panelObject = new GameObject("Panel");
            panelObject.transform.SetParent(overlayRoot.transform, false);
            var panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(560f, 320f);

            var panelImage = panelObject.AddComponent<Image>();
            UiTheme.ApplyPanel(panelImage, UiPanelStyle.Heavy);

            var titleObject = new GameObject("TitleText");
            titleObject.transform.SetParent(panelObject.transform, false);
            var titleRect = titleObject.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.sizeDelta = new Vector2(-32f, 40f);
            titleRect.anchoredPosition = new Vector2(0f, -16f);
            var titleText = titleObject.AddComponent<TextMeshProUGUI>();
            titleText.text = "Вход через Яндекс ID";
            titleText.fontSize = 22f;
            titleText.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyTmp(titleText, UiTextRole.Title);

            var reasonObject = new GameObject("ReasonText");
            reasonObject.transform.SetParent(panelObject.transform, false);
            var reasonRect = reasonObject.AddComponent<RectTransform>();
            reasonRect.anchorMin = new Vector2(0f, 0.35f);
            reasonRect.anchorMax = new Vector2(1f, 0.85f);
            reasonRect.offsetMin = new Vector2(20f, 0f);
            reasonRect.offsetMax = new Vector2(-20f, 0f);
            var reasonLabel = reasonObject.AddComponent<TextMeshProUGUI>();
            reasonLabel.text = string.Empty;
            reasonLabel.fontSize = 16f;
            reasonLabel.alignment = TextAlignmentOptions.TopLeft;
            reasonLabel.enableWordWrapping = true;
            UiTheme.ApplyTmp(reasonLabel, UiTextRole.Body);

            CreateButton(panelObject.transform, "AcceptButton", "Войти через Яндекс", new Vector2(-130f, 28f), () => Hide(true));
            CreateButton(panelObject.transform, "DeclineButton", "Продолжить без входа", new Vector2(130f, 28f), () => Hide(false));

            overlayRoot.SetActive(false);
        }

        private static void CreateButton(Transform parent, string name, string label, Vector2 anchoredPosition, Action onClick)
        {
            var buttonObject = new GameObject(name);
            buttonObject.transform.SetParent(parent, false);
            var buttonRect = buttonObject.AddComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.5f, 0f);
            buttonRect.anchorMax = new Vector2(0.5f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.sizeDelta = new Vector2(210f, 44f);
            buttonRect.anchoredPosition = anchoredPosition;

            var button = buttonObject.AddComponent<Button>();
            UiTheme.StyleButton(button, UiButtonStyle.Primary);

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(buttonObject.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            StretchFull(labelRect);
            var labelText = labelObject.AddComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.fontSize = 16f;
            labelText.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyTmp(labelText, UiTextRole.PrimaryButton);

            button.onClick.AddListener(() => onClick?.Invoke());
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
