using System.Collections;
using System.Collections.Generic;
using ShooterPrototype.Player;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    public enum MainMenuPanelMode
    {
        None = 0,
        Inventory = 1,
        Shop = 2,
        Achievements = 3,
        Stats = 4,
        Settings = 5,
        DailyRewards = 6
    }

    [DisallowMultipleComponent]
    public sealed class MainMenuSectionController : MonoBehaviour
    {
        [SerializeField] private float fadeDuration = 0.38f;

        private readonly List<CanvasGroup> mainMenuGroups = new List<CanvasGroup>();
        private MainMenuController menuController;
        private MainMenuCameraMotion cameraMotion;
        private MainMenuUiSoundController uiSound;
        private CanvasGroup backButtonGroup;
        private Button backButton;
        private MainMenuInventoryPanel inventoryPanel;
        private MainMenuShopPanel shopPanel;
        private MainMenuAchievementsPanel achievementsPanel;
        private MainMenuStatsPanel statsPanel;
        private MainMenuSettingsPanel settingsPanel;
        private MainMenuDailyRewardsPanel dailyRewardsPanel;
        private Button inventoryButton;
        private Button shopButton;
        private Button achievementsButton;
        private Button dailyRewardsButton;
        private Button statsButton;
        private Button settingsButton;
        private MainMenuPanelMode activePanel = MainMenuPanelMode.None;
        private Coroutine transitionCoroutine;

        public bool IsPanelOpen => activePanel != MainMenuPanelMode.None;
        public bool IsInventoryOpen => activePanel == MainMenuPanelMode.Inventory;
        public bool IsShopOpen => activePanel == MainMenuPanelMode.Shop;
        public bool IsAchievementsOpen => activePanel == MainMenuPanelMode.Achievements;
        public bool IsStatsOpen => activePanel == MainMenuPanelMode.Stats;
        public bool IsSettingsOpen => activePanel == MainMenuPanelMode.Settings;
        public bool IsDailyRewardsOpen => activePanel == MainMenuPanelMode.DailyRewards;

        public void Configure(
            MainMenuController menu,
            MainMenuCameraMotion camera,
            MainMenuUiSoundController sound,
            CanvasGroup topNavGroup,
            CanvasGroup startButtonGroup,
            CanvasGroup changeCharacterGroup,
            CanvasGroup gameModeGroup,
            Button inventoryButton,
            Button shopButton,
            Button achievementsButton,
            Button dailyRewardsButton,
            Button statsButton,
            Button settingsButton,
            Button back,
            CanvasGroup backGroup,
            MainMenuInventoryPanel inventory,
            MainMenuShopPanel shop,
            MainMenuAchievementsPanel achievements,
            MainMenuStatsPanel stats,
            MainMenuSettingsPanel settings,
            MainMenuDailyRewardsPanel dailyRewards,
            CanvasGroup nicknameGroup = null)
        {
            menuController = menu;
            cameraMotion = camera;
            uiSound = sound;
            backButton = back;
            backButtonGroup = backGroup;
            inventoryPanel = inventory;
            shopPanel = shop;
            achievementsPanel = achievements;
            statsPanel = stats;
            settingsPanel = settings;
            dailyRewardsPanel = dailyRewards;
            this.inventoryButton = inventoryButton;
            this.shopButton = shopButton;
            this.achievementsButton = achievementsButton;
            this.dailyRewardsButton = dailyRewardsButton;
            this.statsButton = statsButton;
            this.settingsButton = settingsButton;

            mainMenuGroups.Clear();
            if (topNavGroup != null)
            {
                mainMenuGroups.Add(topNavGroup);
            }

            if (startButtonGroup != null)
            {
                mainMenuGroups.Add(startButtonGroup);
            }

            if (changeCharacterGroup != null)
            {
                mainMenuGroups.Add(changeCharacterGroup);
            }

            if (gameModeGroup != null)
            {
                mainMenuGroups.Add(gameModeGroup);
            }

            if (nicknameGroup != null)
            {
                mainMenuGroups.Add(nicknameGroup);
            }

            if (inventoryButton != null)
            {
                inventoryButton.onClick.AddListener(EnterInventory);
            }

            if (shopButton != null)
            {
                shopButton.onClick.AddListener(EnterShop);
            }

            if (achievementsButton != null)
            {
                achievementsButton.onClick.AddListener(EnterAchievements);
            }

            if (dailyRewardsButton != null)
            {
                dailyRewardsButton.onClick.AddListener(EnterDailyRewards);
            }

            if (statsButton != null)
            {
                statsButton.onClick.AddListener(EnterStats);
            }

            if (settingsButton != null)
            {
                settingsButton.onClick.AddListener(EnterSettings);
            }

            if (backButton != null)
            {
                backButton.onClick.RemoveListener(ExitActivePanel);
                backButton.onClick.AddListener(ExitActivePanel);
                if (uiSound != null)
                {
                    backButton.onClick.AddListener(uiSound.PlayButton);
                }
            }

            SetGroupImmediate(backButtonGroup, 0f, interactable: false);
            SetGroupsImmediate(mainMenuGroups, 1f, interactable: true);
        }

        public void EnterShop()
        {
            if (!TryOpenServerSyncedPanel(MainMenuPanelMode.Shop))
            {
                return;
            }

            OpenPanel(MainMenuPanelMode.Shop);
        }

        public void EnterInventory()
        {
            if (!TryOpenServerSyncedPanel(MainMenuPanelMode.Inventory))
            {
                return;
            }

            OpenPanel(MainMenuPanelMode.Inventory);
        }

        public void EnterAchievements()
        {
            OpenPanel(MainMenuPanelMode.Achievements);
        }

        public void EnterDailyRewards()
        {
            OpenPanel(MainMenuPanelMode.DailyRewards);
        }

        public void EnterStats()
        {
            if (!TryOpenServerSyncedPanel(MainMenuPanelMode.Stats))
            {
                return;
            }

            OpenPanel(MainMenuPanelMode.Stats);
        }

        public void SetServerSyncRestrictions(bool serverSynced)
        {
            // Кнопки остаются кликабельными — при отсутствии синка показывается запрос авторизации.
        }

        public void OpenPanelDirect(MainMenuPanelMode panelMode)
        {
            OpenPanel(panelMode);
        }

        private bool TryOpenServerSyncedPanel(MainMenuPanelMode panelMode)
        {
            if (PlayerProfileService.IsServerSynced)
            {
                return true;
            }

            menuController?.NotifyServerConnectionRequired();
            return false;
        }

        public void EnterSettings()
        {
            OpenPanel(MainMenuPanelMode.Settings);
        }

        public void ExitInventory()
        {
            if (activePanel == MainMenuPanelMode.Inventory)
            {
                ExitActivePanel();
            }
        }

        public void ExitActivePanel()
        {
            if (activePanel == MainMenuPanelMode.None)
            {
                return;
            }

            var wasInventory = activePanel == MainMenuPanelMode.Inventory;

            if (activePanel == MainMenuPanelMode.Inventory)
            {
                inventoryPanel?.Hide();
            }
            else if (activePanel == MainMenuPanelMode.Shop)
            {
                shopPanel?.Hide();
            }
            else if (activePanel == MainMenuPanelMode.Achievements)
            {
                achievementsPanel?.Hide();
            }
            else if (activePanel == MainMenuPanelMode.Stats)
            {
                statsPanel?.Hide();
            }
            else if (activePanel == MainMenuPanelMode.Settings)
            {
                settingsPanel?.Hide();
            }
            else if (activePanel == MainMenuPanelMode.DailyRewards)
            {
                dailyRewardsPanel?.Hide();
            }

            activePanel = MainMenuPanelMode.None;

            if (wasInventory)
            {
                ResolveCameraMotion()?.ExitInventoryView();
            }

            StartTransition(showBackButton: false);
        }

        public void SetBackNavigationVisible(bool visible)
        {
            SetGroupImmediate(backButtonGroup, visible ? 1f : 0f, interactable: visible);
        }

        public void SetCaseOpeningMode(bool active)
        {
            if (transitionCoroutine != null)
            {
                StopCoroutine(transitionCoroutine);
                transitionCoroutine = null;
            }

            if (active)
            {
                SetGroupsImmediate(mainMenuGroups, 0f, interactable: false);
                SetGroupImmediate(backButtonGroup, 0f, interactable: false);
                inventoryPanel?.SetHiddenForOverlay(true);
                return;
            }

            inventoryPanel?.SetHiddenForOverlay(false);

            if (activePanel != MainMenuPanelMode.None)
            {
                SetGroupsImmediate(mainMenuGroups, 0f, interactable: false);
                SetGroupImmediate(backButtonGroup, 1f, interactable: true);
            }
            else
            {
                SetGroupsImmediate(mainMenuGroups, 1f, interactable: true);
                SetGroupImmediate(backButtonGroup, 0f, interactable: false);
            }
        }

        private MainMenuCameraMotion ResolveCameraMotion()
        {
            if (cameraMotion == null)
            {
                cameraMotion = MainMenuCameraMotion.Resolve();
            }

            return cameraMotion;
        }

        private void OpenPanel(MainMenuPanelMode panelMode)
        {
            if (activePanel == panelMode)
            {
                if (panelMode == MainMenuPanelMode.Inventory)
                {
                    inventoryPanel?.Show();
                }
                else if (panelMode == MainMenuPanelMode.Shop)
                {
                    shopPanel?.Show();
                }
                else if (panelMode == MainMenuPanelMode.Achievements)
                {
                    achievementsPanel?.Show();
                }
                else if (panelMode == MainMenuPanelMode.Stats)
                {
                    statsPanel?.Show();
                }
                else if (panelMode == MainMenuPanelMode.Settings)
                {
                    settingsPanel?.Show();
                }
                else if (panelMode == MainMenuPanelMode.DailyRewards)
                {
                    dailyRewardsPanel?.Show();
                }

                return;
            }

            var wasInventory = activePanel == MainMenuPanelMode.Inventory;

            if (activePanel == MainMenuPanelMode.Inventory)
            {
                inventoryPanel?.Hide();
            }
            else if (activePanel == MainMenuPanelMode.Shop)
            {
                shopPanel?.Hide();
            }
            else if (activePanel == MainMenuPanelMode.Achievements)
            {
                achievementsPanel?.Hide();
            }
            else if (activePanel == MainMenuPanelMode.Stats)
            {
                statsPanel?.Hide();
            }
            else if (activePanel == MainMenuPanelMode.Settings)
            {
                settingsPanel?.Hide();
            }
            else if (activePanel == MainMenuPanelMode.DailyRewards)
            {
                dailyRewardsPanel?.Hide();
            }

            if (wasInventory && panelMode != MainMenuPanelMode.Inventory)
            {
                ResolveCameraMotion()?.ExitInventoryView();
            }

            activePanel = panelMode;

            if (panelMode == MainMenuPanelMode.Inventory)
            {
                ResolveCameraMotion()?.EnterInventoryView();
                inventoryPanel?.Show();
            }
            else if (panelMode == MainMenuPanelMode.Shop)
            {
                shopPanel?.Show();
            }
            else if (panelMode == MainMenuPanelMode.Achievements)
            {
                achievementsPanel?.Show();
            }
            else if (panelMode == MainMenuPanelMode.Stats)
            {
                statsPanel?.Show();
            }
            else if (panelMode == MainMenuPanelMode.Settings)
            {
                settingsPanel?.Show();
            }
            else if (panelMode == MainMenuPanelMode.DailyRewards)
            {
                dailyRewardsPanel?.Show();
            }

            StartTransition(showBackButton: true);
        }

        private void StartTransition(bool showBackButton)
        {
            if (transitionCoroutine != null)
            {
                StopCoroutine(transitionCoroutine);
            }

            transitionCoroutine = StartCoroutine(TransitionRoutine(showBackButton));
        }

        private IEnumerator TransitionRoutine(bool showBackButton)
        {
            var fromMain = GetGroupsAlpha(mainMenuGroups);
            var toMain = showBackButton ? 0f : 1f;
            var fromBack = backButtonGroup != null ? backButtonGroup.alpha : 0f;
            var toBack = showBackButton ? 1f : 0f;

            if (showBackButton)
            {
                SetGroupsInteractable(mainMenuGroups, false);
            }
            else
            {
                SetGroupInteractable(backButtonGroup, false);
            }

            var elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = fadeDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / fadeDuration);
                var eased = EaseInOut(t);
                SetGroupsAlpha(mainMenuGroups, Mathf.Lerp(fromMain, toMain, eased));
                SetGroupAlpha(backButtonGroup, Mathf.Lerp(fromBack, toBack, eased));
                yield return null;
            }

            SetGroupsAlpha(mainMenuGroups, toMain);
            SetGroupAlpha(backButtonGroup, toBack);
            SetGroupsInteractable(mainMenuGroups, toMain > 0.5f);
            SetGroupInteractable(backButtonGroup, toBack > 0.5f);
            transitionCoroutine = null;
        }

        private static void SetGroupsImmediate(List<CanvasGroup> groups, float alpha, bool interactable)
        {
            for (var i = 0; i < groups.Count; i++)
            {
                SetGroupImmediate(groups[i], alpha, interactable);
            }
        }

        private static void SetGroupImmediate(CanvasGroup group, float alpha, bool interactable)
        {
            if (group == null)
            {
                return;
            }

            group.alpha = alpha;
            group.interactable = interactable;
            group.blocksRaycasts = interactable;
        }

        private static void SetGroupsAlpha(List<CanvasGroup> groups, float alpha)
        {
            for (var i = 0; i < groups.Count; i++)
            {
                if (groups[i] != null)
                {
                    groups[i].alpha = alpha;
                }
            }
        }

        private static void SetGroupAlpha(CanvasGroup group, float alpha)
        {
            if (group != null)
            {
                group.alpha = alpha;
            }
        }

        private static float GetGroupsAlpha(List<CanvasGroup> groups)
        {
            if (groups == null)
            {
                return 1f;
            }

            for (var i = 0; i < groups.Count; i++)
            {
                if (groups[i] != null)
                {
                    return groups[i].alpha;
                }
            }

            return 1f;
        }

        private static void SetGroupsInteractable(List<CanvasGroup> groups, bool interactable)
        {
            for (var i = 0; i < groups.Count; i++)
            {
                SetGroupInteractable(groups[i], interactable);
            }
        }

        private static void SetGroupInteractable(CanvasGroup group, bool interactable)
        {
            if (group == null)
            {
                return;
            }

            group.interactable = interactable;
            group.blocksRaycasts = interactable;
        }

        private static float EaseInOut(float t)
        {
            return t * t * (3f - 2f * t);
        }
    }
}
