using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuReconnectButton : MonoBehaviour
    {
        [SerializeField] private float edgeMargin = 28f;
        [SerializeField] private float centerYOffset = 24f;
        [SerializeField] private float buttonWidth = 220f;
        [SerializeField] private float buttonHeight = 52f;
        [SerializeField] private float labelFontSize = 17f;

        private MainMenuController menuController;
        private MainMenuUiSoundController uiSound;
        private CanvasGroup canvasGroup;
        private Button button;
        private bool built;

        public void Configure(MainMenuController controller, MainMenuUiSoundController sound)
        {
            menuController = controller;
            uiSound = sound;
        }

        public void Build(RectTransform canvasRect)
        {
            if (built || canvasRect == null)
            {
                return;
            }

            var rootObject = new GameObject("MainMenuReconnectButton");
            rootObject.transform.SetParent(canvasRect, false);

            var rootRect = rootObject.AddComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0f, 0.5f);
            rootRect.anchorMax = new Vector2(0f, 0.5f);
            rootRect.pivot = new Vector2(0f, 0.5f);
            rootRect.anchoredPosition = new Vector2(edgeMargin, centerYOffset);
            rootRect.sizeDelta = new Vector2(buttonWidth, buttonHeight);

            rootObject.AddComponent<Image>();

            button = rootObject.AddComponent<Button>();
            UiTheme.StyleButton(button, UiButtonStyle.Primary);
            UiMotion.AttachButtonMotion(button);
            button.onClick.AddListener(HandlePressed);
            if (uiSound != null)
            {
                button.onClick.AddListener(uiSound.PlayButton);
            }

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(rootObject.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            StretchFull(labelRect);

            var label = labelObject.AddComponent<TextMeshProUGUI>();
            label.text = "Переподключиться";
            label.fontSize = labelFontSize;
            label.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyTmp(label, UiTextRole.PrimaryButton);

            canvasGroup = rootObject.AddComponent<CanvasGroup>();
            built = true;
            SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
        }

        public void SetInteractable(bool interactable)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }

        private void HandlePressed()
        {
            var controller = menuController != null
                ? menuController
                : FindFirstObjectByType<MainMenuController>();
            controller?.RetryServerConnection();
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
