using System;
using ShooterPrototype.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace ShooterPrototype.Platform
{
    public static class YandexGamesAuthPrompt
    {
        private static GameObject overlayRoot;
        private static Action<bool> pendingCallback;
        private static Button acceptButton;
        private static Button declineButton;

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
            if (overlayRoot == null)
            {
                onComplete?.Invoke(false);
                pendingCallback = null;
                return;
            }

            overlayRoot.transform.SetAsLastSibling();
            overlayRoot.SetActive(true);
            UpdateButtons(interactable: true);

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
                if (overlayRoot.GetComponent<Canvas>() == null)
                {
                    UnityEngine.Object.Destroy(overlayRoot);
                    overlayRoot = null;
                    acceptButton = null;
                    declineButton = null;
                }
                else
                {
                    return;
                }
            }

            if (overlayRoot != null)
            {
                return;
            }

            overlayRoot = new GameObject("YandexAuthPromptOverlay");
            var canvas = overlayRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = short.MaxValue;
            overlayRoot.AddComponent<GraphicRaycaster>();

            var scaler = overlayRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                UnityEngine.Object.DontDestroyOnLoad(eventSystemObject);
            }

            UnityEngine.Object.DontDestroyOnLoad(overlayRoot);
            overlayRoot.hideFlags = HideFlags.DontSave;

            var rootRect = overlayRoot.GetComponent<RectTransform>();
            if (rootRect == null)
            {
                UnityEngine.Object.Destroy(overlayRoot);
                overlayRoot = null;
                acceptButton = null;
                declineButton = null;
                return;
            }

            StretchFull(rootRect);

            var dimmerObject = new GameObject("Dimmer");
            dimmerObject.transform.SetParent(overlayRoot.transform, false);
            var dimmerRect = dimmerObject.AddComponent<RectTransform>();
            StretchFull(dimmerRect);

            var dimmer = dimmerObject.AddComponent<Image>();
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

            acceptButton = CreateButton(
                panelObject.transform,
                "AcceptButton",
                "Войти через Яндекс",
                new Vector2(-130f, 28f),
                UiButtonStyle.Primary,
                UiTextRole.PrimaryButton,
                () => Hide(true));
            declineButton = CreateButton(
                panelObject.transform,
                "DeclineButton",
                "Продолжить без входа",
                new Vector2(130f, 28f),
                UiButtonStyle.Standard,
                UiTextRole.Body,
                () => Hide(false));

            overlayRoot.SetActive(false);
        }

        private static Button CreateButton(
            Transform parent,
            string name,
            string label,
            Vector2 anchoredPosition,
            UiButtonStyle buttonStyle,
            UiTextRole textRole,
            Action onClick)
        {
            var buttonObject = new GameObject(name);
            buttonObject.transform.SetParent(parent, false);
            var buttonRect = buttonObject.AddComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.5f, 0f);
            buttonRect.anchorMax = new Vector2(0.5f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.sizeDelta = new Vector2(210f, 44f);
            buttonRect.anchoredPosition = anchoredPosition;

            var image = buttonObject.AddComponent<Image>();
            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            UiTheme.StyleButton(button, buttonStyle);

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(buttonObject.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            StretchFull(labelRect);
            var labelText = labelObject.AddComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.fontSize = 16f;
            labelText.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyTmp(labelText, textRole);

            button.onClick.AddListener(() => onClick?.Invoke());
            return button;
        }

        private static void UpdateButtons(bool interactable)
        {
            if (acceptButton != null)
            {
                acceptButton.interactable = interactable;
            }

            if (declineButton != null)
            {
                declineButton.interactable = interactable;
            }
        }

        private static void StretchFull(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
