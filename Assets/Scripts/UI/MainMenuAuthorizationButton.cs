using ShooterPrototype.Platform;
using ShooterPrototype.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuAuthorizationButton : MonoBehaviour
    {
        [SerializeField] private float buttonWidth = 240f;
        [SerializeField] private float buttonHeight = 56f;
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

        public RectTransform RootRect { get; private set; }

        public void Build(RectTransform canvasRect, RectTransform stackParent = null)
        {
            if (built || canvasRect == null)
            {
                return;
            }

            var parent = stackParent != null ? stackParent : canvasRect;
            var rootObject = new GameObject("MainMenuAuthorizationButton");
            rootObject.transform.SetParent(parent, false);

            RootRect = rootObject.AddComponent<RectTransform>();
            var rootRect = RootRect;
            if (stackParent != null)
            {
                var layoutElement = rootObject.AddComponent<LayoutElement>();
                layoutElement.preferredWidth = buttonWidth;
                layoutElement.preferredHeight = buttonHeight;
                layoutElement.minWidth = buttonWidth;
                layoutElement.minHeight = buttonHeight;
                rootRect.anchorMin = new Vector2(0f, 1f);
                rootRect.anchorMax = new Vector2(1f, 1f);
                rootRect.pivot = new Vector2(0.5f, 1f);
                rootRect.sizeDelta = Vector2.zero;
            }
            else
            {
                rootRect.anchorMin = new Vector2(1f, 0.5f);
                rootRect.anchorMax = new Vector2(1f, 0.5f);
                rootRect.pivot = new Vector2(1f, 0.5f);
                rootRect.anchoredPosition = new Vector2(-28f, 24f);
                rootRect.sizeDelta = new Vector2(buttonWidth, buttonHeight);
            }

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
            label.text = "Войти через Яндекс";
            label.fontSize = labelFontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = true;
            UiTheme.ApplyTmp(label, UiTextRole.PrimaryButton);

            canvasGroup = rootObject.AddComponent<CanvasGroup>();
            built = true;
            Refresh();
        }

        public void Refresh()
        {
            var visible = YandexGamesIntegrationService.IsYandexGamesRuntime() &&
                          !PlayerIdentityService.HasAuthorizedYandexLink();
            if (canvasGroup != null)
            {
                canvasGroup.alpha = visible ? 1f : 0f;
                canvasGroup.interactable = visible;
                canvasGroup.blocksRaycasts = visible;
            }
        }

        private void HandlePressed()
        {
            var controller = menuController != null
                ? menuController
                : FindFirstObjectByType<MainMenuController>();
            controller?.BeginYandexProfileLink();
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
