using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    public enum MainMenuServerConnectionState
    {
        Loading = 0,
        Connected = 1,
        Unavailable = 2
    }

    [DisallowMultipleComponent]
    public sealed class MainMenuServerConnectionGate : MonoBehaviour
    {
        private readonly List<CanvasGroup> menuGroups = new List<CanvasGroup>();

        private MainMenuController controller;
        private CanvasGroup overlayGroup;
        private TMP_Text messageText;
        private Button retryButton;
        private TMP_Text statusText;
        private bool built;
        private MainMenuServerConnectionState currentState = MainMenuServerConnectionState.Loading;

        public MainMenuServerConnectionState CurrentState => currentState;

        public void Configure(MainMenuController menuController, TMP_Text bottomStatusText)
        {
            controller = menuController;
            statusText = bottomStatusText;
        }

        public void RegisterMenuGroup(CanvasGroup group)
        {
            if (group == null || menuGroups.Contains(group))
            {
                return;
            }

            menuGroups.Add(group);
            if (currentState != MainMenuServerConnectionState.Connected)
            {
                SetGroupVisible(group, false);
            }
        }

        public void Build(RectTransform canvasRect, MainMenuUiSoundController uiSound)
        {
            if (built || canvasRect == null)
            {
                return;
            }

            var overlayObject = new GameObject("ServerConnectionOverlay");
            overlayObject.transform.SetParent(canvasRect, false);

            var overlayRect = overlayObject.AddComponent<RectTransform>();
            StretchFull(overlayRect);

            var blocker = overlayObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(blocker, UiTheme.CanvasDim);
            blocker.raycastTarget = true;

            overlayGroup = overlayObject.AddComponent<CanvasGroup>();

            var panelObject = new GameObject("Panel");
            panelObject.transform.SetParent(overlayObject.transform, false);
            var panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(520f, 220f);

            var panelBackground = panelObject.AddComponent<Image>();
            UiTheme.ApplyPanel(panelBackground, UiPanelStyle.Overlay);

            var messageObject = new GameObject("Message");
            messageObject.transform.SetParent(panelObject.transform, false);
            var messageRect = messageObject.AddComponent<RectTransform>();
            messageRect.anchorMin = new Vector2(0.5f, 1f);
            messageRect.anchorMax = new Vector2(0.5f, 1f);
            messageRect.pivot = new Vector2(0.5f, 1f);
            messageRect.anchoredPosition = new Vector2(0f, -36f);
            messageRect.sizeDelta = new Vector2(460f, 96f);
            messageText = messageObject.AddComponent<TextMeshProUGUI>();
            messageText.alignment = TextAlignmentOptions.Center;
            messageText.fontSize = 28f;
            messageText.enableWordWrapping = true;
            UiTheme.ApplyTmp(messageText, UiTextRole.Heading);

            retryButton = CreateButton(panelObject.transform, "Перезагрузить", new Vector2(0f, -148f), 220f, 52f);
            retryButton.onClick.AddListener(HandleRetryPressed);
            if (uiSound != null)
            {
                retryButton.onClick.AddListener(uiSound.PlayButton);
            }

            built = true;
            ApplyState(currentState, force: true);
        }

        public void SetState(MainMenuServerConnectionState state, string message = null)
        {
            currentState = state;
            if (messageText != null)
            {
                messageText.text = !string.IsNullOrWhiteSpace(message)
                    ? message
                    : ResolveDefaultMessage(state);
            }

            ApplyState(state, force: false);
        }

        private void ApplyState(MainMenuServerConnectionState state, bool force)
        {
            if (!built && !force)
            {
                return;
            }

            var connected = state == MainMenuServerConnectionState.Connected;
            for (var i = 0; i < menuGroups.Count; i++)
            {
                SetGroupVisible(menuGroups[i], connected);
            }

            if (statusText != null)
            {
                statusText.gameObject.SetActive(connected);
            }

            if (overlayGroup != null)
            {
                overlayGroup.alpha = connected ? 0f : 1f;
                overlayGroup.interactable = !connected;
                overlayGroup.blocksRaycasts = !connected;
            }

            if (messageText != null)
            {
                if (string.IsNullOrWhiteSpace(messageText.text))
                {
                    messageText.text = ResolveDefaultMessage(state);
                }
            }

            if (retryButton != null)
            {
                retryButton.gameObject.SetActive(state == MainMenuServerConnectionState.Unavailable);
            }
        }

        private void HandleRetryPressed()
        {
            if (controller == null)
            {
                return;
            }

            controller.RetryServerConnection();
        }

        private static string ResolveDefaultMessage(MainMenuServerConnectionState state)
        {
            switch (state)
            {
                case MainMenuServerConnectionState.Loading:
                    return "Подключение к серверу...";
                case MainMenuServerConnectionState.Unavailable:
                    return "Сервер недоступен";
                default:
                    return string.Empty;
            }
        }

        private static void SetGroupVisible(CanvasGroup group, bool visible)
        {
            if (group == null)
            {
                return;
            }

            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }

        private Button CreateButton(Transform parent, string label, Vector2 anchoredPosition, float width, float height)
        {
            var buttonObject = new GameObject("RetryButton");
            buttonObject.transform.SetParent(parent, false);

            var rect = buttonObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(width, height);

            buttonObject.AddComponent<Image>();

            var button = buttonObject.AddComponent<Button>();
            UiTheme.StyleButton(button, UiButtonStyle.Primary);

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(buttonObject.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            StretchFull(labelRect);
            var labelText = labelObject.AddComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.fontSize = 22f;
            labelText.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyTmp(labelText, UiTextRole.PrimaryButton);

            return button;
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
