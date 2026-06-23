using System.Collections;
using System.Collections.Generic;
using ShooterPrototype.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuInventoryPanel : MonoBehaviour
    {
        [SerializeField] private float edgeMargin = 44f;
        [SerializeField] private float panelWidth = 628f;
        [SerializeField] private float innerPadding = 28f;
        [SerializeField] private float fadeDuration = 0.38f;
        [SerializeField] private float slideOffset = 56f;
        [SerializeField] private float itemSpacing = 20f;
        [SerializeField] private float headerHeight = 110f;
        [SerializeField] private float scrollbarWidth = 12f;
        [SerializeField] private float scrollbarGap = 8f;
        [SerializeField] private float titleFontSize = 28f;
        [SerializeField] private float slotPadding = 12f;

        private readonly List<ItemSlotVisual> itemSlots = new List<ItemSlotVisual>(32);
        private readonly List<CaseSlotVisual> caseSlots = new List<CaseSlotVisual>(8);

        private enum InventoryTab
        {
            Skins = 0,
            Cases = 1
        }

        private InventoryTab activeTab = InventoryTab.Skins;
        private Button skinsTabButton;
        private Button casesTabButton;
        private GameObject skinsScrollObject;
        private GameObject casesScrollObject;
        private ScrollRect skinsScrollRect;
        private ScrollRect casesScrollRect;
        private RectTransform skinsViewportRect;
        private RectTransform casesViewportRect;
        private RectTransform casesContentRect;
        private bool caseOpenInProgress;

        private CanvasGroup panelGroup;
        private RectTransform panelRect;
        private RectTransform contentRect;
        private Canvas hostCanvas;
        private GameObject emptySkinsState;
        private GameObject emptyCasesState;
        private Vector2 shownAnchoredPosition;
        private Vector2 hiddenAnchoredPosition;
        private const float ViewportClipInset = 6f;
        private const float SlotCardInset = 9f;
        private const float RarityBorderWidth = 7f;
        private const float IconWellPadding = 8f;
        private const int InventoryLayoutVersion = 6;
        private float itemCellWidth;
        private float itemCellHeight;
        private MainMenuPlayerPreview playerPreview;
        private MainMenuUiSoundController uiSound;
        private MainMenuCameraMotion cameraMotion;
        private bool overlayHidden;
        private bool isVisible;
        private bool notificationPulseActive;
        private Coroutine transitionCoroutine;
        private string pendingPulseItemId;

        private sealed class ItemSlotVisual
        {
            public PlayerSkinDefinition Definition;
            public Image Background;
            public Image EquippedFrame;
            public Button Button;
            public TMP_Text QuantityLabel;
            public RectTransform RootRect;
            public GameObject NotificationDot;
        }

        private sealed class CaseSlotVisual
        {
            public CaseDefinition Definition;
            public Image Background;
            public Button OpenButton;
            public TMP_Text QuantityLabel;
            public RectTransform RootRect;
            public GameObject NotificationDot;
        }

        public void Configure(
            MainMenuPlayerPreview preview,
            MainMenuUiSoundController sound,
            MainMenuCameraMotion camera = null)
        {
            playerPreview = preview;
            uiSound = sound;
            if (camera != null)
            {
                cameraMotion = camera;
            }
        }

        public void Build(RectTransform canvasRect)
        {
            if (canvasRect == null)
            {
                return;
            }

            var existing = canvasRect.Find("MainMenuInventoryPanel");
            if (existing != null)
            {
                var marker = existing.GetComponent<InventoryLayoutMarker>();
                if (marker != null && marker.Version >= InventoryLayoutVersion && panelRect != null)
                {
                    return;
                }

                Destroy(existing.gameObject);
                panelRect = null;
                contentRect = null;
                skinsScrollObject = null;
                casesScrollObject = null;
                itemSlots.Clear();
                caseSlots.Clear();
            }

            if (panelRect != null)
            {
                return;
            }

            itemCellWidth = (panelWidth - innerPadding * 2f - itemSpacing - scrollbarWidth - scrollbarGap) * 0.5f;
            itemCellHeight = itemCellWidth;
            hostCanvas = canvasRect.GetComponent<Canvas>();

            var panelObject = new GameObject("MainMenuInventoryPanel");
            panelObject.transform.SetParent(canvasRect, false);

            panelRect = panelObject.AddComponent<RectTransform>();
            panelObject.AddComponent<InventoryLayoutMarker>().Version = InventoryLayoutVersion;
            panelRect.anchorMin = new Vector2(1f, 0f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 0.5f);
            panelRect.anchoredPosition = new Vector2(-edgeMargin, 0f);
            panelRect.offsetMin = new Vector2(-panelWidth, edgeMargin);
            panelRect.offsetMax = new Vector2(0f, -edgeMargin);

            shownAnchoredPosition = panelRect.anchoredPosition;
            hiddenAnchoredPosition = shownAnchoredPosition + new Vector2(slideOffset, 0f);
            panelRect.anchoredPosition = hiddenAnchoredPosition;

            var background = panelObject.AddComponent<Image>();
            UiTheme.ApplyPanel(background, UiPanelStyle.Inventory);
            UiDecor.AttachPanelChrome(panelRect, 12f, 16f);

            panelGroup = panelObject.AddComponent<CanvasGroup>();
            panelGroup.alpha = 0f;
            panelGroup.interactable = false;
            panelGroup.blocksRaycasts = false;

            BuildHeader(panelObject.transform);
            BuildItemsGrid(panelObject.transform);
            BuildCasesGrid(panelObject.transform);
            SetActiveTab(InventoryTab.Skins);
        }

        private void OnEnable()
        {
            PlayerSkinOwnershipService.OwnershipChanged += RebuildItems;
            PlayerSkinOwnershipService.EquipmentChanged += OnEquipmentChanged;
            PlayerProfileService.ProfileSynced += OnProfileSynced;
            MainMenuNotificationState.Changed += OnNotificationStateChanged;
        }

        private void OnDisable()
        {
            StopNotificationPulse();
            PlayerSkinOwnershipService.OwnershipChanged -= RebuildItems;
            PlayerSkinOwnershipService.EquipmentChanged -= OnEquipmentChanged;
            PlayerProfileService.ProfileSynced -= OnProfileSynced;
            MainMenuNotificationState.Changed -= OnNotificationStateChanged;
        }

        private void Update()
        {
            if (notificationPulseActive)
            {
                var scale = 1f + Mathf.Sin(Time.unscaledTime * 5f) * 0.12f;
                ApplyNotificationDotScale(scale);
            }
        }

        private void OnNotificationStateChanged()
        {
            RefreshSlotNotificationDots();
        }

        private void OnProfileSynced()
        {
            RebuildItems();
            if (isVisible && activeTab == InventoryTab.Cases)
            {
                RebuildCases();
            }
            else if (isVisible)
            {
                RefreshSlotNotificationDots();
            }
        }

        private void OnEquipmentChanged()
        {
            if (!string.IsNullOrWhiteSpace(pendingPulseItemId) &&
                PlayerSkinSelectionService.TryGetDefinitionById(pendingPulseItemId, out var definition))
            {
                PreviewInventoryItem(definition);
            }
            else
            {
                ResolvePlayerPreview()?.RefreshSkins();
            }

            if (contentRect == null || itemSlots.Count == 0)
            {
                return;
            }

            RefreshEquippedVisuals();
            pendingPulseItemId = null;
        }

        private void PreviewInventoryItem(PlayerSkinDefinition item)
        {
            if (!item.IsValid)
            {
                return;
            }

            ResolvePlayerPreview()?.PreviewInventoryItem(item);
        }

        private MainMenuPlayerPreview ResolvePlayerPreview()
        {
            if (playerPreview != null)
            {
                return playerPreview;
            }

            var controller = FindObjectOfType<MainMenuController>();
            playerPreview = controller != null ? controller.GetComponent<MainMenuPlayerPreview>() : null;
            return playerPreview;
        }

        private MainMenuCameraMotion ResolveCameraMotion()
        {
            if (cameraMotion == null)
            {
                cameraMotion = MainMenuCameraMotion.Resolve();
            }

            return cameraMotion;
        }

        public void SetHiddenForOverlay(bool hidden)
        {
            overlayHidden = hidden;
            if (panelGroup == null)
            {
                return;
            }

            if (hidden)
            {
                panelGroup.alpha = 0f;
                panelGroup.interactable = false;
                panelGroup.blocksRaycasts = false;
                return;
            }

            if (isVisible)
            {
                panelGroup.alpha = 1f;
                panelGroup.interactable = true;
                panelGroup.blocksRaycasts = true;
            }
        }

        public void Show()
        {
            ResolveCameraMotion()?.EnterInventoryView();

            if (isVisible)
            {
                if (activeTab == InventoryTab.Skins)
                {
                    RebuildItems();
                    RefreshEquippedVisuals();
                }
                else
                {
                    RebuildCases();
                }

                return;
            }

            isVisible = true;
            UiMenuBackdrop.PushOpen(hostCanvas);
            RebuildItems();
            RebuildCases();
            StartTransition(show: true);
        }

        public void Hide()
        {
            if (!isVisible)
            {
                return;
            }

            isVisible = false;
            ResolveCameraMotion()?.ExitInventoryView();
            UiMenuBackdrop.PopClosed();
            StopNotificationPulse();
            MainMenuNotificationState.SyncInventoryNotifications();
            StartTransition(show: false);
        }

        private void BuildHeader(Transform parent)
        {
            var headerObject = new GameObject("Header");
            headerObject.transform.SetParent(parent, false);

            var rect = headerObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(innerPadding, -(headerHeight + innerPadding * 0.5f));
            rect.offsetMax = new Vector2(-innerPadding, -innerPadding * 0.5f);

            var title = headerObject.AddComponent<TextMeshProUGUI>();
            title.text = "Инвентарь";
            title.fontSize = titleFontSize;
            title.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyMilitaryHeader(title, UiTextRole.Heading);

            var tabsObject = new GameObject("Tabs");
            tabsObject.transform.SetParent(headerObject.transform, false);
            var tabsRect = tabsObject.AddComponent<RectTransform>();
            tabsRect.anchorMin = new Vector2(0f, 0f);
            tabsRect.anchorMax = new Vector2(1f, 0f);
            tabsRect.pivot = new Vector2(0.5f, 0f);
            tabsRect.offsetMin = new Vector2(0f, 0f);
            tabsRect.offsetMax = new Vector2(0f, 28f);

            var tabsLayout = tabsObject.AddComponent<HorizontalLayoutGroup>();
            tabsLayout.spacing = 8f;
            tabsLayout.childAlignment = TextAnchor.MiddleCenter;
            tabsLayout.childControlWidth = true;
            tabsLayout.childControlHeight = true;
            tabsLayout.childForceExpandWidth = true;
            tabsLayout.childForceExpandHeight = true;

            skinsTabButton = CreateTabButton(tabsObject.transform, "Скины", () => SetActiveTab(InventoryTab.Skins));
            casesTabButton = CreateTabButton(tabsObject.transform, "Кейсы", () => SetActiveTab(InventoryTab.Cases));
        }

        private Button CreateTabButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var buttonObject = new GameObject(label + "Tab");
            buttonObject.transform.SetParent(parent, false);

            buttonObject.AddComponent<Image>();

            var button = buttonObject.AddComponent<Button>();
            UiTheme.StyleButton(button, UiButtonStyle.Standard);
            button.onClick.AddListener(onClick);
            if (uiSound != null)
            {
                button.onClick.AddListener(uiSound.PlayButton);
            }

            var labelObject = new GameObject("Label", typeof(RectTransform));
            labelObject.transform.SetParent(buttonObject.transform, false);
            var labelRect = labelObject.GetComponent<RectTransform>();
            StretchFull(labelRect);

            var text = labelObject.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 18f;
            text.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyTmp(text, UiTextRole.Heading);

            return button;
        }

        private void SetActiveTab(InventoryTab tab)
        {
            activeTab = tab;
            var skinsActive = tab == InventoryTab.Skins;
            if (skinsScrollObject != null)
            {
                skinsScrollObject.SetActive(skinsActive);
            }

            if (casesScrollObject != null)
            {
                casesScrollObject.SetActive(!skinsActive);
            }

            StyleTabButton(skinsTabButton, skinsActive);
            StyleTabButton(casesTabButton, !skinsActive);

            if (skinsActive)
            {
                RebuildItems();
                RefreshEquippedVisuals();
            }
            else
            {
                RebuildCases();
            }
        }

        private static void StyleTabButton(Button button, bool selected)
        {
            if (button == null)
            {
                return;
            }

            UiTheme.StyleButton(button, selected ? UiButtonStyle.NavSelected : UiButtonStyle.Standard);
        }

        private void BuildItemsGrid(Transform parent)
        {
            skinsScrollObject = new GameObject("ItemsScroll");
            skinsScrollObject.transform.SetParent(parent, false);

            var scrollRectTransform = skinsScrollObject.AddComponent<RectTransform>();
            scrollRectTransform.anchorMin = Vector2.zero;
            scrollRectTransform.anchorMax = Vector2.one;
            scrollRectTransform.offsetMin = new Vector2(innerPadding, innerPadding);
            scrollRectTransform.offsetMax = new Vector2(-innerPadding, -(headerHeight + innerPadding));

            var scroll = skinsScrollObject.AddComponent<ScrollRect>();
            skinsScrollRect = scroll;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;

            var viewportObject = new GameObject("Viewport");
            viewportObject.transform.SetParent(skinsScrollObject.transform, false);
            var viewportRect = viewportObject.AddComponent<RectTransform>();
            skinsViewportRect = viewportRect;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(ViewportClipInset, ViewportClipInset);
            viewportRect.offsetMax = new Vector2(-(scrollbarWidth + scrollbarGap + ViewportClipInset), -ViewportClipInset);
            viewportObject.AddComponent<RectMask2D>();
            EnableViewportScrollCapture(viewportObject);

            var scrollbar = CreateVerticalScrollbar(skinsScrollObject.transform);
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            var contentObject = new GameObject("Content");
            contentObject.transform.SetParent(viewportObject.transform, false);
            contentRect = contentObject.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 0f);

            var contentBackground = contentObject.AddComponent<Image>();
            contentBackground.sprite = UiTheme.WhiteSprite;
            contentBackground.color = Color.clear;
            contentBackground.raycastTarget = true;

            var grid = contentObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(itemCellWidth, itemCellHeight);
            grid.spacing = new Vector2(itemSpacing, itemSpacing);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.padding = new RectOffset(18, 18, 14, 16);

            var fitter = contentObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewportRect;
            scroll.content = contentRect;

            emptySkinsState = UiDecor.CreateEmptyState(
                viewportObject.transform,
                "Пусто",
                "У вас пока нет скинов",
                UiIconCatalog.IconKind.Skin).gameObject;
            emptySkinsState.SetActive(false);

            RebuildItems();
        }

        private void BuildCasesGrid(Transform parent)
        {
            casesScrollObject = new GameObject("CasesScroll");
            casesScrollObject.transform.SetParent(parent, false);

            var scrollRectTransform = casesScrollObject.AddComponent<RectTransform>();
            scrollRectTransform.anchorMin = Vector2.zero;
            scrollRectTransform.anchorMax = Vector2.one;
            scrollRectTransform.offsetMin = new Vector2(innerPadding, innerPadding);
            scrollRectTransform.offsetMax = new Vector2(-innerPadding, -(headerHeight + innerPadding));

            var scroll = casesScrollObject.AddComponent<ScrollRect>();
            casesScrollRect = scroll;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;

            var viewportObject = new GameObject("Viewport");
            viewportObject.transform.SetParent(casesScrollObject.transform, false);
            var viewportRect = viewportObject.AddComponent<RectTransform>();
            casesViewportRect = viewportRect;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(ViewportClipInset, ViewportClipInset);
            viewportRect.offsetMax = new Vector2(-(scrollbarWidth + scrollbarGap + ViewportClipInset), -ViewportClipInset);
            viewportObject.AddComponent<RectMask2D>();
            EnableViewportScrollCapture(viewportObject);

            var scrollbar = CreateVerticalScrollbar(casesScrollObject.transform);
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            var contentObject = new GameObject("Content");
            contentObject.transform.SetParent(viewportObject.transform, false);
            casesContentRect = contentObject.AddComponent<RectTransform>();
            casesContentRect.anchorMin = new Vector2(0f, 1f);
            casesContentRect.anchorMax = new Vector2(1f, 1f);
            casesContentRect.pivot = new Vector2(0.5f, 1f);
            casesContentRect.anchoredPosition = Vector2.zero;
            casesContentRect.sizeDelta = new Vector2(0f, 0f);

            var contentBackground = contentObject.AddComponent<Image>();
            contentBackground.sprite = UiTheme.WhiteSprite;
            contentBackground.color = Color.clear;
            contentBackground.raycastTarget = true;

            var grid = contentObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(itemCellWidth, itemCellHeight);
            grid.spacing = new Vector2(itemSpacing, itemSpacing);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.padding = new RectOffset(18, 18, 14, 16);

            var fitter = contentObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewportRect;
            scroll.content = casesContentRect;

            emptyCasesState = UiDecor.CreateEmptyState(
                viewportObject.transform,
                "Пусто",
                "У вас пока нет кейсов",
                UiIconCatalog.IconKind.Case).gameObject;
            emptyCasesState.SetActive(false);

            casesScrollObject.SetActive(false);
            RebuildCases();
        }

        private void RebuildCases()
        {
            if (casesContentRect == null)
            {
                return;
            }

            for (var i = casesContentRect.childCount - 1; i >= 0; i--)
            {
                var child = casesContentRect.GetChild(i);
                if (child != null)
                {
                    Destroy(child.gameObject);
                }
            }

            caseSlots.Clear();
            CaseCatalogService.EnsureLoaded();

            var ownedCases = new List<(CaseDefinition definition, int quantity)>(8);
            var shopCases = CaseCatalogService.GetShopCases();
            for (var i = 0; i < shopCases.Count; i++)
            {
                var caseDefinition = shopCases[i];
                var quantity = PlayerProfileService.GetOwnedCaseQuantity(caseDefinition.Id);
                if (quantity > 0)
                {
                    ownedCases.Add((caseDefinition, quantity));
                }
            }

            for (var i = 0; i < ownedCases.Count; i++)
            {
                caseSlots.Add(CreateCaseSlot(casesContentRect, ownedCases[i].definition, ownedCases[i].quantity));
            }

            if (emptyCasesState != null)
            {
                emptyCasesState.SetActive(ownedCases.Count == 0);
            }

            RefreshSlotNotificationDots();
        }

        private CaseSlotVisual CreateCaseSlot(Transform parent, CaseDefinition caseDefinition, int quantity)
        {
            var slotObject = new GameObject("Case_" + caseDefinition.Id);
            slotObject.transform.SetParent(parent, false);
            var rootRect = slotObject.GetComponent<RectTransform>();
            if (rootRect == null)
            {
                rootRect = slotObject.AddComponent<RectTransform>();
            }

            var slotLayout = slotObject.AddComponent<LayoutElement>();
            slotLayout.preferredWidth = itemCellWidth;
            slotLayout.preferredHeight = itemCellHeight;
            slotLayout.minWidth = itemCellWidth;
            slotLayout.minHeight = itemCellHeight;

            var background = slotObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(background, UiTheme.InventorySlotFill);

            var iconObject = new GameObject("Icon", typeof(RectTransform));
            iconObject.transform.SetParent(slotObject.transform, false);
            var iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(slotPadding, slotPadding + 34f);
            iconRect.offsetMax = new Vector2(-slotPadding, -slotPadding);

            var iconImage = iconObject.AddComponent<Image>();
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            iconImage.sprite = InventoryIconCatalog.GetCaseIcon(caseDefinition.PictureResourcePath);

            var openButtonObject = new GameObject("OpenButton", typeof(RectTransform));
            openButtonObject.transform.SetParent(slotObject.transform, false);
            var openButtonRect = openButtonObject.GetComponent<RectTransform>();
            openButtonRect.anchorMin = new Vector2(0.5f, 0f);
            openButtonRect.anchorMax = new Vector2(0.5f, 0f);
            openButtonRect.pivot = new Vector2(0.5f, 0f);
            openButtonRect.anchoredPosition = new Vector2(0f, slotPadding);
            openButtonRect.sizeDelta = new Vector2(Mathf.Max(88f, itemCellWidth * 0.72f), 28f);

            openButtonObject.AddComponent<Image>();

            var openButton = openButtonObject.AddComponent<Button>();
            UiTheme.StyleButton(openButton, UiButtonStyle.Primary);
            openButton.onClick.AddListener(() => OnCaseOpenClicked(caseDefinition));

            var openLabelObject = new GameObject("Label", typeof(RectTransform));
            openLabelObject.transform.SetParent(openButtonObject.transform, false);
            var openLabelRect = openLabelObject.GetComponent<RectTransform>();
            StretchFull(openLabelRect);
            var openLabel = openLabelObject.AddComponent<TextMeshProUGUI>();
            openLabel.text = "Открыть";
            openLabel.fontSize = 16f;
            openLabel.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyTmp(openLabel, UiTextRole.PrimaryButton);

            var quantityObject = new GameObject("QuantityBadge", typeof(RectTransform));
            quantityObject.transform.SetParent(slotObject.transform, false);
            var quantityRect = quantityObject.GetComponent<RectTransform>();
            quantityRect.anchorMin = new Vector2(1f, 1f);
            quantityRect.anchorMax = new Vector2(1f, 1f);
            quantityRect.pivot = new Vector2(1f, 1f);
            quantityRect.anchoredPosition = new Vector2(-8f, -8f);
            quantityRect.sizeDelta = new Vector2(42f, 28f);

            var quantityBackground = quantityObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(quantityBackground, UiTheme.SlotEmpty);
            quantityBackground.raycastTarget = false;

            var quantityLabelObject = new GameObject("QuantityLabel", typeof(RectTransform));
            quantityLabelObject.transform.SetParent(quantityObject.transform, false);
            var quantityLabelRect = quantityLabelObject.GetComponent<RectTransform>();
            StretchFull(quantityLabelRect);
            var quantityLabel = quantityLabelObject.AddComponent<TextMeshProUGUI>();
            quantityLabel.fontSize = 16f;
            quantityLabel.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyTmp(quantityLabel, UiTextRole.Heading);
            quantityLabel.text = quantity > 1 ? "x" + quantity : string.Empty;
            quantityObject.SetActive(quantity > 1);

            var notificationDot = CreateNotificationDot(slotObject.transform);
            notificationDot.SetActive(MainMenuNotificationState.IsCaseUnviewed(caseDefinition.Id));

            return new CaseSlotVisual
            {
                Definition = caseDefinition,
                Background = background,
                OpenButton = openButton,
                QuantityLabel = quantityLabel,
                RootRect = rootRect,
                NotificationDot = notificationDot
            };
        }

        private void OnCaseOpenClicked(CaseDefinition caseDefinition)
        {
            if (!caseDefinition.IsValid || caseOpenInProgress)
            {
                return;
            }

            if (PlayerProfileService.GetOwnedCaseQuantity(caseDefinition.Id) <= 0)
            {
                uiSound?.PlayButton();
                return;
            }

            MainMenuNotificationState.MarkCaseViewed(caseDefinition.Id);
            RefreshSlotNotificationDots();

            if (PlayerProfileService.IsServerSynced)
            {
                var menu = FindObjectOfType<MainMenuController>();
                if (menu != null && menu.ProfileApiClient != null)
                {
                    StartCoroutine(OpenCaseFromServerRoutine(menu, caseDefinition));
                    return;
                }
            }

            if (!CaseOpeningService.TryOpenLocal(caseDefinition, out var rolledSkinId))
            {
                uiSound?.PlayButton();
                return;
            }

            uiSound?.PlayButton();
            RebuildCases();
            ShowCaseOpeningOverlay(caseDefinition, rolledSkinId);
        }

        private IEnumerator OpenCaseFromServerRoutine(MainMenuController menu, CaseDefinition caseDefinition)
        {
            caseOpenInProgress = true;
            RefreshCaseOpenButtons(false);

            var success = false;
            var rolledSkinId = string.Empty;
            yield return PlayerProfileService.OpenCase(
                this,
                menu.ProfileApiClient,
                menu.LocalPlayerId,
                caseDefinition.Id,
                (ok, _, rolled) =>
                {
                    success = ok;
                    rolledSkinId = rolled;
                });

            caseOpenInProgress = false;
            uiSound?.PlayButton();
            RebuildCases();
            RefreshCaseOpenButtons(true);

            if (success)
            {
                ShowCaseOpeningOverlay(caseDefinition, rolledSkinId);
            }
        }

        private void RefreshCaseOpenButtons(bool interactable)
        {
            for (var i = 0; i < caseSlots.Count; i++)
            {
                if (caseSlots[i].OpenButton != null)
                {
                    caseSlots[i].OpenButton.interactable = interactable && !caseOpenInProgress;
                }
            }
        }

        private void ShowCaseOpeningOverlay(CaseDefinition caseDefinition, string rolledSkinId)
        {
            var canvasRect = panelRect != null ? panelRect.parent as RectTransform : null;
            if (canvasRect == null)
            {
                var controller = FindObjectOfType<MainMenuController>();
                canvasRect = controller != null
                    ? controller.GetComponentInChildren<Canvas>()?.GetComponent<RectTransform>()
                    : null;
            }

            if (canvasRect == null)
            {
                return;
            }

            var sectionController = FindObjectOfType<MainMenuSectionController>();
            sectionController?.SetCaseOpeningMode(true);

            MainMenuCaseOpeningOverlay.Show(
                canvasRect,
                caseDefinition,
                rolledSkinId,
                () =>
                {
                    sectionController?.SetCaseOpeningMode(false);
                    MainMenuNotificationState.MarkSkinAsNew(rolledSkinId);

                    if (PlayerSkinSelectionService.TryGetDefinitionById(rolledSkinId, out var item))
                    {
                        playerPreview?.PreviewInventoryItem(item);
                    }

                    if (activeTab == InventoryTab.Skins)
                    {
                        RebuildItems();
                    }
                    else
                    {
                        RefreshSlotNotificationDots();
                    }
                });
        }

        private void RebuildItems()
        {
            if (contentRect == null)
            {
                return;
            }

            StopNotificationPulse();
            for (var i = contentRect.childCount - 1; i >= 0; i--)
            {
                var child = contentRect.GetChild(i);
                if (child != null)
                {
                    Destroy(child.gameObject);
                }
            }

            itemSlots.Clear();
            var ownedItems = PlayerSkinSelectionService.GetOwnedItems();
            for (var i = 0; i < ownedItems.Count; i++)
            {
                itemSlots.Add(CreateItemSlot(contentRect, ownedItems[i]));
            }

            if (emptySkinsState != null)
            {
                emptySkinsState.SetActive(ownedItems.Count == 0);
            }

            RefreshEquippedVisuals();
            RefreshSlotNotificationDots();
        }

        private void MarkSkinViewedIfNeeded(string skinId)
        {
            if (string.IsNullOrWhiteSpace(skinId) ||
                !MainMenuNotificationState.IsSkinUnviewed(skinId))
            {
                return;
            }

            MainMenuNotificationState.MarkSkinViewed(skinId);
            RefreshSlotNotificationDots();
        }

        private void StartNotificationPulse()
        {
            notificationPulseActive = isVisible && !overlayHidden;
            if (!notificationPulseActive)
            {
                ApplyNotificationDotScale(1f);
            }
        }

        private void StopNotificationPulse()
        {
            notificationPulseActive = false;
            ApplyNotificationDotScale(1f);
        }

        private void ApplyNotificationDotScale(float scale)
        {
            var pulseScale = new Vector3(scale, scale, 1f);

            for (var i = 0; i < itemSlots.Count; i++)
            {
                var slot = itemSlots[i];
                if (slot?.NotificationDot != null && slot.NotificationDot.activeSelf)
                {
                    slot.NotificationDot.transform.localScale = pulseScale;
                }
            }

            for (var i = 0; i < caseSlots.Count; i++)
            {
                var slot = caseSlots[i];
                if (slot?.NotificationDot != null && slot.NotificationDot.activeSelf)
                {
                    slot.NotificationDot.transform.localScale = pulseScale;
                }
            }
        }

        private GameObject CreateNotificationDot(Transform parent)
        {
            var badgeObject = new GameObject("NotificationDot", typeof(RectTransform));
            badgeObject.transform.SetParent(parent, false);

            var rect = badgeObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(6f, -6f);
            rect.sizeDelta = new Vector2(22f, 22f);

            var ringObject = new GameObject("Ring", typeof(RectTransform));
            ringObject.transform.SetParent(badgeObject.transform, false);
            var ringRect = ringObject.GetComponent<RectTransform>();
            StretchFull(ringRect);
            var ringImage = ringObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(ringImage, UiTheme.PanelFillHeavy);
            ringImage.raycastTarget = false;

            var dotObject = new GameObject("Dot", typeof(RectTransform));
            dotObject.transform.SetParent(badgeObject.transform, false);
            var dotRect = dotObject.GetComponent<RectTransform>();
            dotRect.anchorMin = new Vector2(0.5f, 0.5f);
            dotRect.anchorMax = new Vector2(0.5f, 0.5f);
            dotRect.pivot = new Vector2(0.5f, 0.5f);
            dotRect.anchoredPosition = Vector2.zero;
            dotRect.sizeDelta = new Vector2(14f, 14f);
            var dotImage = dotObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(dotImage, UiTheme.NotificationDot);
            dotImage.raycastTarget = false;

            badgeObject.transform.SetAsLastSibling();
            return badgeObject;
        }

        private void RefreshSlotNotificationDots()
        {
            for (var i = 0; i < itemSlots.Count; i++)
            {
                var slot = itemSlots[i];
                if (slot?.NotificationDot == null || !slot.Definition.IsValid)
                {
                    continue;
                }

                slot.NotificationDot.SetActive(MainMenuNotificationState.IsSkinUnviewed(slot.Definition.Id));
            }

            for (var i = 0; i < caseSlots.Count; i++)
            {
                var slot = caseSlots[i];
                if (slot?.NotificationDot == null || !slot.Definition.IsValid)
                {
                    continue;
                }

                slot.NotificationDot.SetActive(MainMenuNotificationState.IsCaseUnviewed(slot.Definition.Id));
            }

            if (isVisible && !overlayHidden)
            {
                StartNotificationPulse();
            }
        }

        private Scrollbar CreateVerticalScrollbar(Transform parent)
        {
            var scrollbarObject = new GameObject("Scrollbar Vertical");
            scrollbarObject.transform.SetParent(parent, false);

            var rect = scrollbarObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(scrollbarWidth, 0f);
            rect.anchoredPosition = Vector2.zero;

            var trackImage = scrollbarObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(trackImage, UiTheme.ScrollTrack);

            var scrollbar = scrollbarObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            var slidingAreaObject = new GameObject("Sliding Area");
            slidingAreaObject.transform.SetParent(scrollbarObject.transform, false);
            var slidingAreaRect = slidingAreaObject.AddComponent<RectTransform>();
            StretchFull(slidingAreaRect);
            slidingAreaRect.offsetMin = new Vector2(2f, 8f);
            slidingAreaRect.offsetMax = new Vector2(-2f, -8f);

            var handleObject = new GameObject("Handle");
            handleObject.transform.SetParent(slidingAreaObject.transform, false);
            var handleRect = handleObject.AddComponent<RectTransform>();
            StretchFull(handleRect);

            var handleImage = handleObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(handleImage, UiTheme.ScrollHandle);

            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;
            return scrollbar;
        }

        private ItemSlotVisual CreateItemSlot(Transform parent, PlayerSkinDefinition item)
        {
            var slotObject = new GameObject("Item_" + item.Id);
            slotObject.transform.SetParent(parent, false);
            var rootRect = slotObject.GetComponent<RectTransform>();
            if (rootRect == null)
            {
                rootRect = slotObject.AddComponent<RectTransform>();
            }

            var slotLayout = slotObject.AddComponent<LayoutElement>();
            slotLayout.preferredWidth = itemCellWidth;
            slotLayout.preferredHeight = itemCellHeight;
            slotLayout.minWidth = itemCellWidth;
            slotLayout.minHeight = itemCellHeight;
            slotObject.AddComponent<RectMask2D>();

            var bodyObject = new GameObject("Body", typeof(RectTransform));
            bodyObject.transform.SetParent(slotObject.transform, false);
            var bodyRect = bodyObject.GetComponent<RectTransform>();
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = Vector2.one;
            bodyRect.offsetMin = Vector2.zero;
            bodyRect.offsetMax = Vector2.zero;

            var background = bodyObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(background, UiTheme.InventorySlotFill);

            var button = bodyObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => OnItemClicked(item));
            UiMotion.AttachHoverScale(bodyObject, hoverScale: 1.02f, pressedScale: 0.985f);

            var cardSize = Mathf.Min(itemCellWidth, itemCellHeight) - SlotCardInset * 2f;
            var cardObject = new GameObject("Card", typeof(RectTransform));
            cardObject.transform.SetParent(bodyObject.transform, false);
            var cardRect = cardObject.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.anchoredPosition = Vector2.zero;
            cardRect.sizeDelta = new Vector2(cardSize, cardSize);

            var rarityBorderObject = new GameObject("RarityBorder", typeof(RectTransform));
            rarityBorderObject.transform.SetParent(cardObject.transform, false);
            StretchFull(rarityBorderObject.GetComponent<RectTransform>());
            var rarityBorder = rarityBorderObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(rarityBorder, ShopCatalogService.GetRarityStripeColor(item.Id));
            rarityBorder.raycastTarget = false;

            var iconWellObject = new GameObject("IconWell", typeof(RectTransform));
            iconWellObject.transform.SetParent(cardObject.transform, false);
            var iconWellRect = iconWellObject.GetComponent<RectTransform>();
            StretchInset(iconWellRect, RarityBorderWidth);
            var iconWellBackground = iconWellObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(iconWellBackground, new Color(0.11f, 0.105f, 0.095f, 0.96f));
            iconWellBackground.raycastTarget = false;

            var iconObject = new GameObject("Icon", typeof(RectTransform));
            iconObject.transform.SetParent(iconWellObject.transform, false);
            var iconRect = iconObject.GetComponent<RectTransform>();
            StretchInset(iconRect, IconWellPadding);
            var iconImage = iconObject.AddComponent<Image>();
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            iconImage.sprite = InventoryIconCatalog.GetSkinIcon(item);

            var frameObject = new GameObject("EquippedFrame", typeof(RectTransform));
            frameObject.transform.SetParent(cardObject.transform, false);
            StretchFull(frameObject.GetComponent<RectTransform>());
            var equippedFrame = frameObject.AddComponent<Image>();
            UiTheme.ApplyEquippedFrame(equippedFrame, visible: false);

            var quantityObject = new GameObject("QuantityBadge", typeof(RectTransform));
            quantityObject.transform.SetParent(bodyObject.transform, false);
            var quantityRect = quantityObject.GetComponent<RectTransform>();
            quantityRect.anchorMin = new Vector2(1f, 1f);
            quantityRect.anchorMax = new Vector2(1f, 1f);
            quantityRect.pivot = new Vector2(1f, 1f);
            quantityRect.anchoredPosition = new Vector2(-6f, -6f);
            quantityRect.sizeDelta = new Vector2(42f, 28f);

            var quantityBackground = quantityObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(quantityBackground, UiTheme.SlotEmpty);
            quantityBackground.raycastTarget = false;

            var quantityLabelObject = new GameObject("QuantityLabel", typeof(RectTransform));
            quantityLabelObject.transform.SetParent(quantityObject.transform, false);
            var quantityLabelRect = quantityLabelObject.GetComponent<RectTransform>();
            quantityLabelRect.anchorMin = Vector2.zero;
            quantityLabelRect.anchorMax = Vector2.one;
            quantityLabelRect.offsetMin = Vector2.zero;
            quantityLabelRect.offsetMax = Vector2.zero;

            var quantityLabel = quantityLabelObject.AddComponent<TextMeshProUGUI>();
            quantityLabel.fontSize = 16f;
            quantityLabel.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyTmp(quantityLabel, UiTextRole.Heading);

            var notificationDot = CreateNotificationDot(slotObject.transform);
            var showNotification = MainMenuNotificationState.IsSkinUnviewed(item.Id);
            notificationDot.SetActive(showNotification);

            UiTooltipController.AttachSlotTooltip(
                slotObject,
                hostCanvas,
                () => item.DisplayName,
                () =>
                {
                    var rarity = SkinRarityUtility.GetDisplayName(ShopCatalogService.GetRarity(item.Id));
                    return PlayerSkinSelectionService.IsEquipped(item)
                        ? rarity + " · Экипировано"
                        : rarity;
                });

            return new ItemSlotVisual
            {
                Definition = item,
                Background = background,
                EquippedFrame = equippedFrame,
                Button = button,
                QuantityLabel = quantityLabel,
                RootRect = rootRect,
                NotificationDot = notificationDot
            };
        }

        private static void RefreshQuantityBadge(ItemSlotVisual slot)
        {
            if (slot?.QuantityLabel == null)
            {
                return;
            }

            var count = PlayerSkinOwnershipService.GetOwnedCount(slot.Definition.Id);
            if (count > 1)
            {
                slot.QuantityLabel.transform.parent.gameObject.SetActive(true);
                slot.QuantityLabel.text = "x" + count;
            }
            else
            {
                slot.QuantityLabel.transform.parent.gameObject.SetActive(false);
            }
        }

        private void OnItemClicked(PlayerSkinDefinition item)
        {
            if (!PlayerSkinSelectionService.TryResolveSlot(item, out var slot))
            {
                return;
            }

            MarkSkinViewedIfNeeded(item.Id);

            if (PlayerProfileService.IsServerSynced)
            {
                var menu = FindObjectOfType<MainMenuController>();
                if (menu != null && menu.ProfileApiClient != null)
                {
                    pendingPulseItemId = item.Id;
                    uiSound?.PlayButton();
                    StartCoroutine(EquipFromServerRoutine(menu, item, slot));
                    return;
                }
            }

            if (!PlayerSkinSelectionService.TryEquip(item))
            {
                return;
            }

            pendingPulseItemId = item.Id;
            PreviewInventoryItem(item);
            RefreshEquippedVisuals();
            pendingPulseItemId = null;
            uiSound?.PlayButton();
        }

        private IEnumerator EquipFromServerRoutine(
            MainMenuController menu,
            PlayerSkinDefinition item,
            PlayerSkinSlot slot)
        {
            var wasEquipped = PlayerSkinSelectionService.IsEquipped(item);
            if (wasEquipped && !PlayerSkinSelectionService.SupportsUnequip(slot))
            {
                pendingPulseItemId = null;
                yield break;
            }

            var targetSkinId = wasEquipped ? "__none__" : item.Id;
            var previousSkinId = PlayerSkinSelectionService.ResolveEquippedSkinId(slot);
            PlayerProfileService.TryApplyEquippedOptimistic(slot, targetSkinId);

            var success = false;
            yield return PlayerProfileService.EquipSkin(
                this,
                menu.ProfileApiClient,
                menu.LocalPlayerId,
                slot,
                targetSkinId,
                (ok, _) => success = ok);

            if (!success)
            {
                PlayerProfileService.TryApplyEquippedOptimistic(slot, previousSkinId);
                pendingPulseItemId = null;
                yield break;
            }

            pendingPulseItemId = null;
        }

        private void RefreshEquippedVisuals()
        {
            for (var i = 0; i < itemSlots.Count; i++)
            {
                var slot = itemSlots[i];
                var equipped = PlayerSkinSelectionService.IsEquipped(slot.Definition);

                if (slot.Background != null)
                {
                    slot.Background.color = UiTheme.InventorySlotFill;
                }

                if (slot.EquippedFrame != null)
                {
                    UiTheme.ApplyEquippedFrame(slot.EquippedFrame, equipped);
                }

                if (slot.Button != null)
                {
                    var colors = slot.Button.colors;
                    colors.normalColor = UiTheme.InventorySlotFill;
                    colors.highlightedColor = UiTheme.ButtonHighlight;
                    colors.pressedColor = UiTheme.ButtonPressed;
                    colors.selectedColor = UiTheme.ButtonHighlight;
                    colors.disabledColor = UiTheme.InventorySlotFill;
                    colors.fadeDuration = 0.08f;
                    slot.Button.colors = colors;
                }

                RefreshQuantityBadge(slot);
            }
        }

        private void StartTransition(bool show)
        {
            if (transitionCoroutine != null)
            {
                StopCoroutine(transitionCoroutine);
            }

            transitionCoroutine = StartCoroutine(TransitionRoutine(show));
        }

        private IEnumerator TransitionRoutine(bool show)
        {
            var fromAlpha = show ? 0f : 1f;
            var toAlpha = show ? 1f : 0f;
            var fromPos = show ? hiddenAnchoredPosition : shownAnchoredPosition;
            var toPos = show ? shownAnchoredPosition : hiddenAnchoredPosition;

            if (show)
            {
                panelGroup.blocksRaycasts = true;
            }
            else
            {
                panelGroup.interactable = false;
            }

            var elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = fadeDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / fadeDuration);
                var eased = EaseInOut(t);
                panelGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, eased);
                panelRect.anchoredPosition = Vector2.Lerp(fromPos, toPos, eased);
                yield return null;
            }

            panelGroup.alpha = toAlpha;
            panelRect.anchoredPosition = toPos;
            panelGroup.interactable = show;
            panelGroup.blocksRaycasts = show;
            transitionCoroutine = null;

            if (show)
            {
                StartNotificationPulse();
            }
            else
            {
                StopNotificationPulse();
            }
        }

        private static void EnableViewportScrollCapture(GameObject viewportObject)
        {
            var viewportImage = viewportObject.AddComponent<Image>();
            viewportImage.sprite = UiTheme.WhiteSprite;
            viewportImage.color = Color.clear;
            viewportImage.raycastTarget = true;
        }

        private static void StretchInset(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static float EaseInOut(float t)
        {
            return t * t * (3f - 2f * t);
        }

        private sealed class InventoryLayoutMarker : MonoBehaviour
        {
            public int Version;
        }
    }
}
