using ShooterPrototype.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class MainMenuUiLayout : MonoBehaviour
    {
        private static Sprite whiteSprite;

        private static readonly Color PanelColor = new Color(0.06f, 0.08f, 0.1f, 0.58f);
        private static readonly Color ButtonNormalColor = new Color(0.12f, 0.14f, 0.17f, 0.9f);
        private static readonly Color ButtonHighlightedColor = new Color(0.18f, 0.22f, 0.26f, 0.96f);
        private static readonly Color ButtonPressedColor = new Color(0.1f, 0.12f, 0.14f, 0.98f);
        private static readonly Color ButtonSelectedColor = new Color(0.16f, 0.34f, 0.38f, 0.96f);
        private static readonly Color PrimaryButtonNormalColor = new Color(0.18f, 0.48f, 0.42f, 0.96f);
        private static readonly Color PrimaryButtonHighlightedColor = new Color(0.22f, 0.58f, 0.5f, 1f);
        private static readonly Color PrimaryButtonPressedColor = new Color(0.14f, 0.38f, 0.34f, 1f);
        private static readonly Color LabelColor = new Color(0.94f, 0.96f, 0.98f, 0.98f);
        private static readonly Color StatusColor = new Color(0.82f, 0.88f, 0.92f, 0.92f);

        [SerializeField] private float edgeMargin = 28f;
        [SerializeField] private float startButtonWidth = 300f;
        [SerializeField] private float navButtonWidth = 192f;
        [SerializeField] private float navButtonHeight = 54f;
        [SerializeField] private float startButtonHeight = 62f;
        [SerializeField] private float buttonSpacing = 10f;
        [SerializeField] private float statusHeight = 42f;
        [SerializeField] private float navFontSize = 22f;
        [SerializeField] private float startFontSize = 26f;
        [SerializeField] private float statusFontSize = 21f;

        private bool layoutApplied;
        private GameObject topNavBarObject;
        private Button inventoryButton;
        private Button backButton;
        private CanvasGroup backButtonGroup;
        private CanvasGroup startButtonGroup;

        public void ApplyLayout(MainMenuController controller)
        {
            if (layoutApplied || controller == null)
            {
                return;
            }

            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                return;
            }

            EnsureCanvasScaler(canvas);
            EnsureEventSystem();

            var canvasRect = canvas.GetComponent<RectTransform>();
            var uiSound = EnsureUiSound(controller);
            BuildTopNavBar(canvasRect, uiSound);
            CreateBackButton(canvasRect);

            if (controller.StartButton != null)
            {
                SetupStartButton(controller.StartButton, canvasRect);
                controller.BindStartButtonText(ResolveButtonLabel(controller.StartButton));
            }

            if (controller.StatusText != null)
            {
                LayoutStatusText(controller.StatusText, canvasRect);
            }

            layoutApplied = true;
        }

        public void ConfigureSections(MainMenuController controller)
        {
            if (!layoutApplied || controller == null)
            {
                return;
            }

            var sectionController = controller.GetComponent<MainMenuSectionController>();
            if (sectionController == null)
            {
                sectionController = controller.gameObject.AddComponent<MainMenuSectionController>();
            }

            var inventoryPanel = controller.GetComponent<MainMenuInventoryPanel>();
            if (inventoryPanel == null)
            {
                inventoryPanel = controller.gameObject.AddComponent<MainMenuInventoryPanel>();
            }

            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas != null)
            {
                inventoryPanel.Build(canvas.GetComponent<RectTransform>());
            }

            inventoryPanel.Configure(
                controller.GetComponent<MainMenuPlayerPreview>(),
                controller.GetComponent<MainMenuUiSoundController>());

            var changeCharacterGroup = controller.ChangeCharacterButton != null
                ? EnsureCanvasGroup(controller.ChangeCharacterButton.gameObject)
                : null;

            sectionController.Configure(
                controller.GetComponent<MainMenuCameraMotion>(),
                controller.GetComponent<MainMenuUiSoundController>(),
                topNavBarObject != null ? EnsureCanvasGroup(topNavBarObject) : null,
                startButtonGroup,
                changeCharacterGroup,
                inventoryButton,
                backButton,
                backButtonGroup,
                inventoryPanel);
        }

        private void BuildTopNavBar(RectTransform canvasRect, MainMenuUiSoundController uiSound)
        {
            topNavBarObject = CreatePanel("TopNavBar", canvasRect, false);
            var topRect = topNavBarObject.GetComponent<RectTransform>();
            topRect.anchorMin = new Vector2(0f, 1f);
            topRect.anchorMax = new Vector2(0f, 1f);
            topRect.pivot = new Vector2(0f, 1f);
            topRect.anchoredPosition = new Vector2(edgeMargin, -edgeMargin);

            var background = topNavBarObject.AddComponent<Image>();
            background.sprite = GetWhiteSprite();
            background.type = Image.Type.Simple;
            background.color = PanelColor;
            background.raycastTarget = false;

            var layout = topNavBarObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.spacing = buttonSpacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(12, 12, 10, 10);

            var fitter = topNavBarObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            CreateNavButton(topNavBarObject.transform, "Меню", uiSound, selected: true);
            inventoryButton = CreateNavButton(topNavBarObject.transform, "Инвентарь", uiSound);
            CreateNavButton(topNavBarObject.transform, "Магазин", uiSound);
            CreateNavButton(topNavBarObject.transform, "Достижения", uiSound);
            CreateNavButton(topNavBarObject.transform, "Настройки", uiSound);

            EnsureCanvasGroup(topNavBarObject);
        }

        private void CreateBackButton(RectTransform canvasRect)
        {
            var backObject = new GameObject("BackButton");
            backObject.transform.SetParent(canvasRect, false);

            var rect = backObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(edgeMargin, -edgeMargin);
            rect.sizeDelta = new Vector2(navButtonWidth, navButtonHeight);

            var image = backObject.AddComponent<Image>();
            image.sprite = GetWhiteSprite();
            image.type = Image.Type.Simple;

            backButton = backObject.AddComponent<Button>();
            StyleButton(backButton, primary: false);

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(backObject.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            StretchFull(labelRect);

            var text = labelObject.AddComponent<TextMeshProUGUI>();
            text.text = "Назад";
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = navFontSize;
            text.fontStyle = FontStyles.Bold;
            text.color = LabelColor;
            text.raycastTarget = false;

            backButtonGroup = EnsureCanvasGroup(backObject);
            backButtonGroup.alpha = 0f;
            backButtonGroup.interactable = false;
            backButtonGroup.blocksRaycasts = false;
        }

        private static MainMenuUiSoundController EnsureUiSound(MainMenuController controller)
        {
            if (controller == null)
            {
                return null;
            }

            var uiSound = controller.GetComponent<MainMenuUiSoundController>();
            if (uiSound == null)
            {
                uiSound = controller.gameObject.AddComponent<MainMenuUiSoundController>();
            }

            return uiSound;
        }

        private void SetupStartButton(Button startButton, RectTransform canvasRect)
        {
            var rect = startButton.GetComponent<RectTransform>();
            rect.SetParent(canvasRect, false);
            rect.localScale = Vector3.one;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(edgeMargin, edgeMargin);
            rect.sizeDelta = new Vector2(startButtonWidth, startButtonHeight);

            var image = startButton.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = GetWhiteSprite();
                image.type = Image.Type.Simple;
            }

            StyleButton(startButton, primary: true);
            SetButtonLabel(startButton, "Играть");
            startButtonGroup = EnsureCanvasGroup(startButton.gameObject);
        }

        private void LayoutStatusText(TMP_Text statusText, RectTransform canvasRect)
        {
            var rect = statusText.rectTransform;
            rect.SetParent(canvasRect, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(edgeMargin, edgeMargin);
            rect.offsetMax = new Vector2(-edgeMargin, edgeMargin + statusHeight);

            statusText.alignment = TextAlignmentOptions.Center;
            statusText.fontSize = statusFontSize;
            statusText.color = StatusColor;
            statusText.enableWordWrapping = true;
            statusText.overflowMode = TextOverflowModes.Ellipsis;
            statusText.raycastTarget = false;
        }

        private Button CreateNavButton(Transform parent, string label, MainMenuUiSoundController uiSound, bool selected = false)
        {
            var buttonObject = new GameObject("Nav_" + label);
            buttonObject.transform.SetParent(parent, false);

            var layoutElement = buttonObject.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = navButtonWidth;
            layoutElement.preferredHeight = navButtonHeight;
            layoutElement.minWidth = navButtonWidth;

            var image = buttonObject.AddComponent<Image>();
            image.sprite = GetWhiteSprite();
            image.type = Image.Type.Simple;

            var button = buttonObject.AddComponent<Button>();
            StyleButton(button, primary: false, selected: selected);
            button.interactable = !selected;

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(buttonObject.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            StretchFull(labelRect);

            var text = labelObject.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = navFontSize;
            text.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
            text.color = LabelColor;
            text.raycastTarget = false;

            if (uiSound != null && !selected)
            {
                button.onClick.AddListener(uiSound.PlayButton);
            }

            return button;
        }

        private static GameObject CreatePanel(string name, Transform parent, bool stretch)
        {
            var panelObject = new GameObject(name);
            panelObject.transform.SetParent(parent, false);
            var rect = panelObject.AddComponent<RectTransform>();
            if (stretch)
            {
                StretchFull(rect);
            }

            return panelObject;
        }

        private static void StyleButton(Button button, bool primary, bool selected = false)
        {
            if (button == null)
            {
                return;
            }

            var colors = button.colors;
            if (primary)
            {
                colors.normalColor = PrimaryButtonNormalColor;
                colors.highlightedColor = PrimaryButtonHighlightedColor;
                colors.pressedColor = PrimaryButtonPressedColor;
                colors.selectedColor = PrimaryButtonHighlightedColor;
            }
            else if (selected)
            {
                colors.normalColor = ButtonSelectedColor;
                colors.highlightedColor = ButtonSelectedColor;
                colors.pressedColor = ButtonSelectedColor;
                colors.selectedColor = ButtonSelectedColor;
            }
            else
            {
                colors.normalColor = ButtonNormalColor;
                colors.highlightedColor = ButtonHighlightedColor;
                colors.pressedColor = ButtonPressedColor;
                colors.selectedColor = ButtonHighlightedColor;
            }

            colors.disabledColor = new Color(0.12f, 0.14f, 0.16f, 0.55f);
            colors.fadeDuration = 0.12f;
            button.colors = colors;

            var targetGraphic = button.targetGraphic as Image;
            if (targetGraphic != null && targetGraphic.sprite == null)
            {
                targetGraphic.sprite = GetWhiteSprite();
                targetGraphic.type = Image.Type.Simple;
            }
        }

        private static TMP_Text ResolveButtonLabel(Button button)
        {
            return button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
        }

        private void SetButtonLabel(Button button, string label)
        {
            var text = ResolveButtonLabel(button);
            if (text != null)
            {
                text.text = label;
                text.alignment = TextAlignmentOptions.Center;
                text.fontSize = startFontSize;
                text.fontStyle = FontStyles.Bold;
                text.color = LabelColor;
                text.raycastTarget = false;
            }
        }

        private static CanvasGroup EnsureCanvasGroup(GameObject target)
        {
            var group = target.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = target.AddComponent<CanvasGroup>();
            }

            return group;
        }

        private static void EnsureCanvasScaler(Canvas canvas)
        {
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null)
            {
                return;
            }

            var eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<UnityEngine.EventSystems.EventSystem>();
#if ENABLE_INPUT_SYSTEM
            eventSystemObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            eventSystemObject.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
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
    }
}
