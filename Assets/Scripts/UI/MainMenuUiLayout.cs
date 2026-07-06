using ShooterPrototype.Player;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class MainMenuUiLayout : MonoBehaviour
    {
        [SerializeField] private float edgeMargin = 28f;
        [SerializeField] private float startButtonWidth = 300f;
        [SerializeField] private float navButtonWidth = 200f;
        [SerializeField] private float navButtonHeight = 58f;
        [SerializeField] private float startButtonHeight = 62f;
        [SerializeField] private float buttonSpacing = 10f;
        [SerializeField] private float statusHeight = 42f;
        [SerializeField] private float navFontSize = 18f;
        [SerializeField] private float startFontSize = 26f;
        [SerializeField] private float statusFontSize = 21f;
        [SerializeField] private float modeSelectorGap = 10f;

        private bool layoutApplied;
        private GameObject topNavBarObject;
        private Button inventoryButton;
        private Button shopButton;
        private Button achievementsButton;
        private Button dailyRewardsButton;
        private Button statsButton;
        private Button settingsButton;
        private Button backButton;
        private CanvasGroup backButtonGroup;
        private CanvasGroup startButtonGroup;
        private MainMenuGameModeSelector gameModeSelector;
        private MainMenuCurrencyDisplay currencyDisplay;
        private MainMenuNicknameEditor nicknameEditor;
        private MainMenuLeaderboardPanel leaderboardPanel;
        private RectTransform bottomRightStackRect;
        private CanvasGroup bottomRightStackGroup;
        private MainMenuNavNotificationBadges navNotificationBadges;
        private MainMenuServerConnectionGate connectionGate;
        private MainMenuReconnectButton reconnectButton;
        private MainMenuDailyRewardPrompt dailyRewardPrompt;
        private RectTransform menuCanvasRect;

        public void TryShowDailyRewardLoginPrompt()
        {
            if (dailyRewardPrompt == null || menuCanvasRect == null)
            {
                return;
            }

            dailyRewardPrompt.TryShow(menuCanvasRect);
        }

        public void ApplyLayout(MainMenuController controller)
        {
            if (layoutApplied || controller == null)
            {
                return;
            }

            var canvas = ResolveMenuCanvas(controller);
            if (canvas == null)
            {
                return;
            }

            EnsureCanvasScaler(canvas);
            EnsureEventSystem();

            var canvasRect = canvas.GetComponent<RectTransform>();
            var uiSound = EnsureUiSound(controller);
            UiDecor.CreateVignette(canvasRect);
            UiTooltipController.Ensure(canvas);
            BuildTopNavBar(canvasRect, uiSound);
            CreateBackButton(canvasRect);
            BuildCurrencyDisplay(controller, canvasRect);
            BuildBottomRightProfileArea(controller, canvasRect, uiSound);

            if (controller.StartButton != null)
            {
                SetupStartButton(controller.StartButton, canvasRect);
                controller.BindStartButtonText(ResolveButtonLabel(controller.StartButton));
                BuildGameModeSelector(controller, canvasRect, uiSound);
            }

            if (controller.StatusText != null)
            {
                var startRect = controller.StartButton != null
                    ? controller.StartButton.GetComponent<RectTransform>()
                    : null;
                LayoutStatusText(controller.StatusText, canvasRect, startRect);
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

            var shopPanel = controller.GetComponent<MainMenuShopPanel>();
            if (shopPanel == null)
            {
                shopPanel = controller.gameObject.AddComponent<MainMenuShopPanel>();
            }

            var achievementsPanel = controller.GetComponent<MainMenuAchievementsPanel>();
            if (achievementsPanel == null)
            {
                achievementsPanel = controller.gameObject.AddComponent<MainMenuAchievementsPanel>();
            }

            var statsPanel = controller.GetComponent<MainMenuStatsPanel>();
            if (statsPanel == null)
            {
                statsPanel = controller.gameObject.AddComponent<MainMenuStatsPanel>();
            }

            var settingsPanel = controller.GetComponent<MainMenuSettingsPanel>();
            if (settingsPanel == null)
            {
                settingsPanel = controller.gameObject.AddComponent<MainMenuSettingsPanel>();
            }

            var dailyRewardsPanel = controller.GetComponent<MainMenuDailyRewardsPanel>();
            if (dailyRewardsPanel == null)
            {
                dailyRewardsPanel = controller.gameObject.AddComponent<MainMenuDailyRewardsPanel>();
            }

            var canvas = ResolveMenuCanvas(controller);
            if (canvas != null)
            {
                var canvasRect = canvas.GetComponent<RectTransform>();
                menuCanvasRect = canvasRect;
                inventoryPanel.Build(canvasRect);
                shopPanel.Build(canvasRect);
                achievementsPanel.Build(canvasRect);
                statsPanel.Build(canvasRect);
                settingsPanel.Build(canvasRect);
                dailyRewardsPanel.Build(canvasRect);
                currencyDisplay?.Build(canvasRect);
                if (bottomRightStackRect != null)
                {
                    nicknameEditor?.Build(canvasRect, bottomRightStackRect);
                    leaderboardPanel?.Build(bottomRightStackRect);
                }
                else
                {
                    nicknameEditor?.Build(canvasRect);
                }

                nicknameEditor?.Configure(controller, controller.ProfileApiClient, EnsureUiSound(controller));
                leaderboardPanel?.Configure(controller, controller.ProfileApiClient);

                connectionGate = EnsureConnectionGate(controller);
                connectionGate.Configure(controller, controller.StatusText, sectionController);
                connectionGate.Build(canvasRect, EnsureUiSound(controller));

                reconnectButton = EnsureReconnectButton(controller);
                reconnectButton.Configure(controller, EnsureUiSound(controller));
                reconnectButton.Build(canvasRect);

                connectionGate.RegisterMenuGroup(currencyDisplay != null ? currencyDisplay.CanvasGroup : null);
                controller.BindConnectionGate(connectionGate);
            }

            inventoryPanel.Configure(
                controller.GetComponent<MainMenuPlayerPreview>(),
                controller.GetComponent<MainMenuUiSoundController>(),
                MainMenuCameraMotion.Resolve());

            shopPanel.Configure(
                controller.GetComponent<MainMenuPlayerPreview>(),
                controller.GetComponent<MainMenuUiSoundController>());

            achievementsPanel.Configure(controller.GetComponent<MainMenuUiSoundController>());

            statsPanel.Configure(controller.GetComponent<MainMenuUiSoundController>());
            settingsPanel.Configure(controller.GetComponent<MainMenuUiSoundController>());
            dailyRewardsPanel.Configure(controller.GetComponent<MainMenuUiSoundController>());

            dailyRewardPrompt = controller.GetComponent<MainMenuDailyRewardPrompt>();
            if (dailyRewardPrompt == null)
            {
                dailyRewardPrompt = controller.gameObject.AddComponent<MainMenuDailyRewardPrompt>();
            }

            dailyRewardPrompt.Configure(controller.GetComponent<MainMenuUiSoundController>());
            StartCoroutine(ShowDailyRewardPromptWhenReady());

            ClientSettingsService.EnsureLoaded();
            ClientSettingsService.ApplyMasterVolume();
            ClientSettingsService.ApplyGraphicsPreset();

            var changeCharacterGroup = controller.ChangeCharacterButton != null
                ? EnsureCanvasGroup(controller.ChangeCharacterButton.gameObject)
                : null;

            sectionController.Configure(
                controller,
                MainMenuCameraMotion.Resolve(),
                controller.GetComponent<MainMenuUiSoundController>(),
                topNavBarObject != null ? EnsureCanvasGroup(topNavBarObject) : null,
                startButtonGroup,
                changeCharacterGroup,
                gameModeSelector != null ? gameModeSelector.CanvasGroup : null,
                inventoryButton,
                shopButton,
                achievementsButton,
                dailyRewardsButton,
                statsButton,
                settingsButton,
                backButton,
                backButtonGroup,
                inventoryPanel,
                shopPanel,
                achievementsPanel,
                statsPanel,
                settingsPanel,
                dailyRewardsPanel,
                bottomRightStackGroup != null
                    ? bottomRightStackGroup
                    : nicknameEditor != null ? nicknameEditor.CanvasGroup : null);
        }

        private void BuildBottomRightProfileArea(
            MainMenuController controller,
            RectTransform canvasRect,
            MainMenuUiSoundController uiSound)
        {
            if (controller == null || canvasRect == null)
            {
                return;
            }

            nicknameEditor = controller.GetComponent<MainMenuNicknameEditor>();
            if (nicknameEditor == null)
            {
                nicknameEditor = controller.gameObject.AddComponent<MainMenuNicknameEditor>();
            }

            leaderboardPanel = controller.GetComponent<MainMenuLeaderboardPanel>();
            if (leaderboardPanel == null)
            {
                leaderboardPanel = controller.gameObject.AddComponent<MainMenuLeaderboardPanel>();
            }

            nicknameEditor.Configure(controller, controller.ProfileApiClient, uiSound);
            leaderboardPanel.Configure(controller, controller.ProfileApiClient);

            if (bottomRightStackRect != null)
            {
                return;
            }

            var stackObject = new GameObject("MainMenuBottomRightStack");
            stackObject.transform.SetParent(canvasRect, false);

            bottomRightStackRect = stackObject.AddComponent<RectTransform>();
            bottomRightStackRect.anchorMin = new Vector2(1f, 0f);
            bottomRightStackRect.anchorMax = new Vector2(1f, 0f);
            bottomRightStackRect.pivot = new Vector2(1f, 0f);
            bottomRightStackRect.anchoredPosition = new Vector2(-edgeMargin, edgeMargin);
            bottomRightStackRect.sizeDelta = new Vector2(340f, 0f);

            var stackLayout = stackObject.AddComponent<VerticalLayoutGroup>();
            stackLayout.childAlignment = TextAnchor.LowerRight;
            stackLayout.spacing = 12f;
            stackLayout.childControlWidth = true;
            stackLayout.childControlHeight = true;
            stackLayout.childForceExpandWidth = false;
            stackLayout.childForceExpandHeight = false;

            var stackFitter = stackObject.AddComponent<ContentSizeFitter>();
            stackFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            stackFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            bottomRightStackGroup = stackObject.AddComponent<CanvasGroup>();

            nicknameEditor.Build(canvasRect, bottomRightStackRect);
            leaderboardPanel.Build(bottomRightStackRect);
            if (nicknameEditor.RootRect != null)
            {
                nicknameEditor.RootRect.SetSiblingIndex(0);
            }

            if (leaderboardPanel.RootRect != null)
            {
                leaderboardPanel.RootRect.SetSiblingIndex(1);
            }
        }

        private void BuildNicknameEditor(MainMenuController controller, RectTransform canvasRect, MainMenuUiSoundController uiSound)
        {
            BuildBottomRightProfileArea(controller, canvasRect, uiSound);
        }

        private static MainMenuServerConnectionGate EnsureConnectionGate(MainMenuController controller)
        {
            if (controller == null)
            {
                return null;
            }

            var gate = controller.GetComponent<MainMenuServerConnectionGate>();
            if (gate == null)
            {
                gate = controller.gameObject.AddComponent<MainMenuServerConnectionGate>();
            }

            return gate;
        }

        private static MainMenuReconnectButton EnsureReconnectButton(MainMenuController controller)
        {
            if (controller == null)
            {
                return null;
            }

            var reconnect = controller.GetComponent<MainMenuReconnectButton>();
            if (reconnect == null)
            {
                reconnect = controller.gameObject.AddComponent<MainMenuReconnectButton>();
            }

            return reconnect;
        }

        public MainMenuReconnectButton ReconnectButton => reconnectButton;

        private void BuildCurrencyDisplay(MainMenuController controller, RectTransform canvasRect)
        {
            if (controller == null || canvasRect == null)
            {
                return;
            }

            currencyDisplay = controller.GetComponent<MainMenuCurrencyDisplay>();
            if (currencyDisplay == null)
            {
                currencyDisplay = controller.gameObject.AddComponent<MainMenuCurrencyDisplay>();
            }
        }

        private void BuildTopNavBar(RectTransform canvasRect, MainMenuUiSoundController uiSound)
        {
            topNavBarObject = CreatePanel("TopNavBar", canvasRect, false);
            var topRect = topNavBarObject.GetComponent<RectTransform>();
            topRect.anchorMin = new Vector2(0f, 1f);
            topRect.anchorMax = new Vector2(0f, 1f);
            topRect.pivot = new Vector2(0f, 1f);
            topRect.anchoredPosition = new Vector2(edgeMargin, -edgeMargin);

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
            shopButton = CreateNavButton(topNavBarObject.transform, "Магазин", uiSound);
            achievementsButton = CreateNavButton(topNavBarObject.transform, "Достижения", uiSound);
            dailyRewardsButton = CreateNavButton(topNavBarObject.transform, "Награды", uiSound);
            statsButton = CreateNavButton(topNavBarObject.transform, "Статистика", uiSound);
            settingsButton = CreateNavButton(topNavBarObject.transform, "Настройки", uiSound);

            navNotificationBadges = GetComponent<MainMenuNavNotificationBadges>();
            if (navNotificationBadges == null)
            {
                navNotificationBadges = gameObject.AddComponent<MainMenuNavNotificationBadges>();
            }

            navNotificationBadges.Configure(inventoryButton, achievementsButton);

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
            backButton = backObject.AddComponent<Button>();
            UiTheme.StyleButton(backButton, UiButtonStyle.Standard);
            UiMotion.AttachButtonMotion(backButton);

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(backObject.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            StretchFull(labelRect);
            labelRect.offsetMin = new Vector2(34f, 0f);

            var text = labelObject.AddComponent<TextMeshProUGUI>();
            text.text = "НАЗАД";
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.fontSize = navFontSize;
            UiTheme.ApplyMilitaryHeader(text, UiTextRole.Heading);
            UiIconCatalog.AttachIcon(rect, UiIconCatalog.IconKind.Back, 18f, new Vector2(12f, 0f), TextAnchor.MiddleLeft);

            backButtonGroup = EnsureCanvasGroup(backObject);
            backButtonGroup.alpha = 0f;
            backButtonGroup.interactable = false;
            backButtonGroup.blocksRaycasts = false;
        }

        private static Canvas ResolveMenuCanvas(MainMenuController controller)
        {
            if (controller == null)
            {
                return null;
            }

            if (controller.StartButton != null)
            {
                var startCanvas = controller.StartButton.GetComponentInParent<Canvas>();
                if (IsMenuCanvas(startCanvas))
                {
                    return startCanvas;
                }
            }

            if (controller.StatusText != null)
            {
                var statusCanvas = controller.StatusText.GetComponentInParent<Canvas>();
                if (IsMenuCanvas(statusCanvas))
                {
                    return statusCanvas;
                }
            }

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                return null;
            }

            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var canvases = roots[i].GetComponentsInChildren<Canvas>(true);
                for (var j = 0; j < canvases.Length; j++)
                {
                    if (IsMenuCanvas(canvases[j]))
                    {
                        return canvases[j];
                    }
                }
            }

            return null;
        }

        private static bool IsMenuCanvas(Canvas canvas)
        {
            return canvas != null &&
                   !string.Equals(
                       canvas.gameObject.name,
                       GameHudController.RuntimeCanvasObjectName,
                       System.StringComparison.Ordinal);
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
                image.sprite = UiTheme.PrimaryButtonSprite;
                image.type = Image.Type.Sliced;
                image.color = Color.white;
            }

            UiTheme.StyleButton(startButton, UiButtonStyle.Primary);
            SetButtonLabel(startButton, "Поиск матча");
            UiMotion.AttachButtonMotion(startButton, primary: true);
            startButtonGroup = EnsureCanvasGroup(startButton.gameObject);
        }

        private void BuildGameModeSelector(
            MainMenuController controller,
            RectTransform canvasRect,
            MainMenuUiSoundController uiSound)
        {
            if (controller == null || canvasRect == null || controller.StartButton == null)
            {
                return;
            }

            gameModeSelector = controller.GetComponent<MainMenuGameModeSelector>();
            if (gameModeSelector == null)
            {
                gameModeSelector = controller.gameObject.AddComponent<MainMenuGameModeSelector>();
            }

            var startRect = controller.StartButton.GetComponent<RectTransform>();
            var modeLeft = startRect != null ? startRect.anchoredPosition.x : edgeMargin;
            var modeWidth = startRect != null ? startRect.sizeDelta.x : startButtonWidth;
            var modeBottom = startRect != null
                ? startRect.anchoredPosition.y + startRect.sizeDelta.y + modeSelectorGap
                : edgeMargin + startButtonHeight + modeSelectorGap;
            gameModeSelector.Build(canvasRect, modeLeft, modeBottom, modeWidth, uiSound);
        }

        private void LayoutStatusText(TMP_Text statusText, RectTransform canvasRect, RectTransform startButtonRect)
        {
            if (statusText == null || canvasRect == null)
            {
                return;
            }

            var rect = statusText.rectTransform;
            rect.SetParent(canvasRect, false);
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            var buttonCenterY = edgeMargin + startButtonHeight * 0.5f;
            if (startButtonRect != null)
            {
                buttonCenterY = startButtonRect.anchoredPosition.y + startButtonRect.sizeDelta.y * 0.5f;
            }

            rect.anchoredPosition = new Vector2(0f, buttonCenterY);
            rect.sizeDelta = new Vector2(760f, statusHeight + 12f);

            statusText.alignment = TextAlignmentOptions.Center;
            statusText.fontSize = statusFontSize;
            UiTheme.ApplyTmp(statusText, UiTextRole.Accent);
            statusText.enableWordWrapping = true;
            statusText.overflowMode = TextOverflowModes.Ellipsis;
            statusText.raycastTarget = false;
            statusText.gameObject.SetActive(false);
        }

        private Button CreateNavButton(
            Transform parent,
            string label,
            MainMenuUiSoundController uiSound,
            bool selected = false)
        {
            var tab = UiPrefabLibrary.CreateNavTab(parent, label, selected, navButtonWidth, navButtonHeight);
            var button = tab.Button;
            if (tab.Label != null)
            {
                tab.Label.text = label.ToUpperInvariant();
                tab.Label.fontSize = navFontSize;
                tab.Label.alignment = TextAlignmentOptions.Center;
                tab.Label.enableWordWrapping = false;
                tab.Label.overflowMode = TextOverflowModes.Overflow;
                tab.Label.characterSpacing = 1.2f;
                UiTheme.ApplyMilitaryHeader(tab.Label, selected ? UiTextRole.Accent : UiTextRole.Heading);

                var labelRect = tab.Label.rectTransform;
                labelRect.offsetMin = new Vector2(10f, 0f);
                labelRect.offsetMax = new Vector2(-10f, 0f);
            }

            if (uiSound != null && !selected && button != null)
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
                UiTheme.ApplyTmp(text, UiTextRole.PrimaryButton);
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

        private IEnumerator ShowDailyRewardPromptWhenReady()
        {
            var controller = GetComponent<MainMenuController>();
            while (controller != null && !controller.HasCompletedInitialProfileBootstrap)
            {
                yield return null;
            }

            yield return null;
            TryShowDailyRewardLoginPrompt();
        }
    }
}
