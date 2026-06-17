using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    public enum MainMenuPanelMode
    {
        None = 0,
        Inventory = 1,
        Shop = 2
    }

    [DisallowMultipleComponent]
    public sealed class MainMenuSectionController : MonoBehaviour
    {
        [SerializeField] private float fadeDuration = 0.38f;

        private readonly List<CanvasGroup> mainMenuGroups = new List<CanvasGroup>();
        private MainMenuCameraMotion cameraMotion;
        private MainMenuUiSoundController uiSound;
        private CanvasGroup backButtonGroup;
        private Button backButton;
        private MainMenuInventoryPanel inventoryPanel;
        private MainMenuShopPanel shopPanel;
        private MainMenuPanelMode activePanel = MainMenuPanelMode.None;
        private Coroutine transitionCoroutine;

        public bool IsPanelOpen => activePanel != MainMenuPanelMode.None;
        public bool IsInventoryOpen => activePanel == MainMenuPanelMode.Inventory;
        public bool IsShopOpen => activePanel == MainMenuPanelMode.Shop;

        public void Configure(
            MainMenuCameraMotion camera,
            MainMenuUiSoundController sound,
            CanvasGroup topNavGroup,
            CanvasGroup startButtonGroup,
            CanvasGroup changeCharacterGroup,
            Button inventoryButton,
            Button shopButton,
            Button back,
            CanvasGroup backGroup,
            MainMenuInventoryPanel inventory,
            MainMenuShopPanel shop)
        {
            cameraMotion = camera;
            uiSound = sound;
            backButton = back;
            backButtonGroup = backGroup;
            inventoryPanel = inventory;
            shopPanel = shop;

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

            if (inventoryButton != null)
            {
                inventoryButton.onClick.AddListener(EnterInventory);
            }

            if (shopButton != null)
            {
                shopButton.onClick.AddListener(EnterShop);
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

        public void EnterInventory()
        {
            OpenPanel(MainMenuPanelMode.Inventory);
        }

        public void EnterShop()
        {
            OpenPanel(MainMenuPanelMode.Shop);
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

            activePanel = MainMenuPanelMode.None;

            if (wasInventory)
            {
                cameraMotion?.ExitInventoryView();
            }

            StartTransition(showBackButton: false);
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

            if (wasInventory && panelMode != MainMenuPanelMode.Inventory)
            {
                cameraMotion?.ExitInventoryView();
            }

            activePanel = panelMode;

            if (panelMode == MainMenuPanelMode.Inventory)
            {
                cameraMotion?.EnterInventoryView();
                inventoryPanel?.Show();
            }
            else if (panelMode == MainMenuPanelMode.Shop)
            {
                shopPanel?.Show();
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
            var fromMain = showBackButton ? 1f : 0f;
            var toMain = showBackButton ? 0f : 1f;
            var fromBack = showBackButton ? 0f : 1f;
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
