using System.Collections;
using System.Collections.Generic;
using ShooterPrototype.Platform;
using ShooterPrototype.Player;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuShopPanel : MonoBehaviour
    {
        [SerializeField] private float edgeMargin = 28f;
        [SerializeField] private float leftReservedWidth = 228f;
        [SerializeField] private float topReservedHeight = 92f;
        [SerializeField] private float innerPadding = 28f;
        [SerializeField] private float fadeDuration = 0.38f;
        [SerializeField] private float itemSpacing = 16f;
        [SerializeField] private float headerHeight = 72f;
        [SerializeField] private float scrollbarWidth = 10f;
        [SerializeField] private float scrollbarGap = 10f;
        [SerializeField] private float titleFontSize = 30f;
        [SerializeField] private float slotPadding = 10f;
        [SerializeField] private int gridColumns = 4;
        [SerializeField] private float priceBadgeHeight = 32f;
        [SerializeField] private float priceFontSize = 20f;

        private readonly List<ShopSlotVisual> itemSlots = new List<ShopSlotVisual>(64);
        private readonly List<CaseSlotVisual> caseSlots = new List<CaseSlotVisual>(8);
        private readonly List<IapSlotVisual> iapSlots = new List<IapSlotVisual>(4);

        private RewardedCoinsSlotVisual rewardedCoinsSlot;

        private CanvasGroup panelGroup;
        private RectTransform panelRect;
        private RectTransform contentRect;
        private Canvas hostCanvas;
        private GameObject emptyShopState;
        private ScrollRect itemsScrollRect;
        private GridLayoutGroup itemGrid;
        private float itemCellWidth;
        private float itemCellHeight;
        private MainMenuUiSoundController uiSound;
        private MainMenuPlayerPreview playerPreview;
        private bool isVisible;
        private bool casePurchaseInProgress;
        private bool iapPurchaseInProgress;
        private bool rewardedAdInProgress;
        private Coroutine transitionCoroutine;

        private sealed class ShopSlotVisual
        {
            public PlayerSkinDefinition Definition;
            public Image Background;
            public Button Button;
            public TMP_Text PriceLabel;
            public Image PriceBadge;
        }

        private sealed class CaseSlotVisual
        {
            public CaseDefinition Definition;
            public Image Background;
            public Button Button;
            public TMP_Text PriceLabel;
            public Image PriceBadge;
        }

        private sealed class IapSlotVisual
        {
            public ShopIapProductDefinition Definition;
            public Image Background;
            public Button Button;
            public TMP_Text PriceLabel;
            public Image PriceBadge;
        }

        private sealed class RewardedCoinsSlotVisual
        {
            public Image Background;
            public Button Button;
            public TMP_Text PriceLabel;
            public Image PriceBadge;
        }

        public void Configure(MainMenuPlayerPreview preview, MainMenuUiSoundController sound)
        {
            playerPreview = preview;
            uiSound = sound;
        }

        public void Build(RectTransform canvasRect)
        {
            if (panelRect != null || canvasRect == null)
            {
                return;
            }

            ComputeCellSize(canvasRect.rect.width);
            hostCanvas = canvasRect.GetComponent<Canvas>();

            var panelObject = new GameObject("MainMenuShopPanel");
            panelObject.transform.SetParent(canvasRect, false);

            panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = new Vector2(leftReservedWidth, edgeMargin);
            panelRect.offsetMax = new Vector2(-edgeMargin, -topReservedHeight);

            var background = panelObject.AddComponent<Image>();
            UiTheme.ApplyPanel(background, UiPanelStyle.Heavy);
            UiDecor.AttachPanelChrome(panelRect, 12f, 16f);

            panelGroup = panelObject.AddComponent<CanvasGroup>();
            panelGroup.alpha = 0f;
            panelGroup.interactable = false;
            panelGroup.blocksRaycasts = false;

            BuildHeader(panelObject.transform);
            BuildItemsGrid(panelObject.transform);
        }

        public void Show()
        {
            UpdateGridCellSize();

            if (isVisible)
            {
                RebuildItems();
                RefreshSlotVisuals();
                return;
            }

            isVisible = true;
            UiMenuBackdrop.PushOpen(hostCanvas);
            RebuildItems();
            StartTransition(show: true);
        }

        public void Hide()
        {
            if (!isVisible)
            {
                return;
            }

            isVisible = false;
            MainMenuCaseLootTooltip.HideImmediate();
            UiMenuBackdrop.PopClosed();
            StartTransition(show: false);
        }

        private void OnEnable()
        {
            YandexGamesShopPurchaseService.EnsureInitialized(this);
            PlayerCurrencyService.BalanceChanged += RefreshSlotVisuals;
            PlayerSkinOwnershipService.OwnershipChanged += RefreshSlotVisuals;
            PlayerProfileService.ProfileSynced += RefreshSlotVisuals;
        }

        private void OnDisable()
        {
            PlayerCurrencyService.BalanceChanged -= RefreshSlotVisuals;
            PlayerSkinOwnershipService.OwnershipChanged -= RefreshSlotVisuals;
            PlayerProfileService.ProfileSynced -= RefreshSlotVisuals;
        }

        private void ComputeCellSize(float canvasWidth)
        {
            if (canvasWidth <= 0f)
            {
                canvasWidth = 1920f;
            }

            var scrollAreaWidth = canvasWidth - leftReservedWidth - edgeMargin - innerPadding * 2f - scrollbarWidth - scrollbarGap;
            var columns = Mathf.Max(3, gridColumns);
            itemCellWidth = (scrollAreaWidth - itemSpacing * (columns - 1)) / columns;
            itemCellHeight = itemCellWidth;
        }

        private void UpdateGridCellSize()
        {
            if (panelRect == null || itemGrid == null)
            {
                return;
            }

            ComputeCellSize(panelRect.rect.width);
            itemGrid.cellSize = new Vector2(itemCellWidth, itemCellHeight);
        }

        private void BuildHeader(Transform parent)
        {
            var headerObject = new GameObject("Header");
            headerObject.transform.SetParent(parent, false);

            var rect = headerObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(innerPadding, -(headerHeight + innerPadding * 0.35f));
            rect.offsetMax = new Vector2(-innerPadding, -innerPadding * 0.35f);

            var title = headerObject.AddComponent<TextMeshProUGUI>();
            title.text = "Магазин";
            title.fontSize = titleFontSize;
            title.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyMilitaryHeader(title, UiTextRole.Heading);
        }

        private void BuildItemsGrid(Transform parent)
        {
            var scrollObject = new GameObject("ItemsScroll");
            scrollObject.transform.SetParent(parent, false);

            var scrollRectTransform = scrollObject.AddComponent<RectTransform>();
            scrollRectTransform.anchorMin = Vector2.zero;
            scrollRectTransform.anchorMax = Vector2.one;
            scrollRectTransform.offsetMin = new Vector2(innerPadding, innerPadding);
            scrollRectTransform.offsetMax = new Vector2(-innerPadding, -(headerHeight + innerPadding));

            var scroll = scrollObject.AddComponent<ScrollRect>();
            itemsScrollRect = scroll;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;

            var viewportObject = new GameObject("Viewport");
            viewportObject.transform.SetParent(scrollObject.transform, false);
            var viewportRect = viewportObject.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = new Vector2(-(scrollbarWidth + scrollbarGap), 0f);
            viewportObject.AddComponent<RectMask2D>();
            EnableViewportScrollCapture(viewportObject);

            var scrollbar = CreateVerticalScrollbar(scrollObject.transform);
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

            itemGrid = contentObject.AddComponent<GridLayoutGroup>();
            itemGrid.cellSize = new Vector2(itemCellWidth, itemCellHeight);
            itemGrid.spacing = new Vector2(itemSpacing, itemSpacing);
            itemGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            itemGrid.constraintCount = Mathf.Max(3, gridColumns);
            itemGrid.childAlignment = TextAnchor.UpperCenter;
            itemGrid.padding = new RectOffset(0, 0, 4, 12);

            var fitter = contentObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.onValueChanged.AddListener(_ => MainMenuCaseLootTooltip.HideImmediate());

            emptyShopState = UiDecor.CreateEmptyState(
                viewportObject.transform,
                "Магазин пуст",
                "Новые товары появятся позже",
                UiIconCatalog.IconKind.Shop).gameObject;
            emptyShopState.SetActive(false);
        }

        private void RebuildItems()
        {
            if (contentRect == null)
            {
                return;
            }

            MainMenuCaseLootTooltip.HideImmediate();
            InventoryIconCatalog.ClearCache();
            UpdateGridCellSize();

            for (var i = contentRect.childCount - 1; i >= 0; i--)
            {
                var child = contentRect.GetChild(i);
                if (child != null)
                {
                    Destroy(child.gameObject);
                }
            }

            itemSlots.Clear();
            caseSlots.Clear();
            iapSlots.Clear();
            rewardedCoinsSlot = null;

            ShopIapCatalogService.EnsureLoaded();
            CaseCatalogService.EnsureLoaded();

            rewardedCoinsSlot = CreateRewardedCoinsSlot(contentRect);

            var currencyProducts = ShopIapCatalogService.GetCurrencyProducts();
            for (var i = 0; i < currencyProducts.Count; i++)
            {
                iapSlots.Add(CreateIapCurrencySlot(contentRect, currencyProducts[i]));
            }

            var premiumProducts = ShopIapCatalogService.GetPremiumProducts();
            for (var i = 0; i < premiumProducts.Count; i++)
            {
                var product = premiumProducts[i];
                if (product.IsVipPrefixReward)
                {
                    iapSlots.Add(CreateIapVipSlot(contentRect, product));
                }
                else if (product.IsNoAdsReward)
                {
                    iapSlots.Add(CreateIapNoAdsSlot(contentRect, product));
                }
            }

            var shopCases = CaseCatalogService.GetShopCases();
            for (var i = 0; i < shopCases.Count; i++)
            {
                caseSlots.Add(CreateCaseSlot(contentRect, shopCases[i]));
            }

            var shopItems = PlayerSkinOwnershipService.GetShopCatalogItems();
            for (var i = 0; i < shopItems.Count; i++)
            {
                itemSlots.Add(CreateItemSlot(contentRect, shopItems[i]));
            }

            if (emptyShopState != null)
            {
                emptyShopState.SetActive(
                    shopItems.Count == 0 && caseSlots.Count == 0 && iapSlots.Count == 0 && rewardedCoinsSlot == null);
            }

            RefreshSlotVisuals();
        }

        private void RefreshSlotVisuals()
        {
            RefreshIapSlotVisuals();
            RefreshCaseSlotVisuals();

            var balance = PlayerProfileService.GetSpendableBalance();
            for (var i = 0; i < itemSlots.Count; i++)
            {
                var slot = itemSlots[i];
                var owned = PlayerSkinOwnershipService.IsOwned(slot.Definition);
                if (owned)
                {
                    if (slot.Background != null)
                    {
                        slot.Background.color = UiTheme.SlotEmpty;
                    }

                    if (slot.Button != null)
                    {
                        slot.Button.interactable = false;
                        var colors = slot.Button.colors;
                        colors.normalColor = UiTheme.SlotEmpty;
                        colors.highlightedColor = UiTheme.SlotEmpty;
                        colors.pressedColor = UiTheme.SlotEmpty;
                        colors.selectedColor = UiTheme.SlotEmpty;
                        colors.disabledColor = UiTheme.SlotEmpty;
                        colors.fadeDuration = 0.1f;
                        slot.Button.colors = colors;
                    }

                    if (slot.PriceLabel != null)
                    {
                        slot.PriceLabel.text = "Куплено";
                        slot.PriceLabel.color = UiTheme.TextMuted;
                    }

                    if (slot.PriceBadge != null)
                    {
                        UiTheme.ApplyFlatFill(slot.PriceBadge, UiTheme.PriceBadgeFillMuted);
                    }

                    continue;
                }

                var price = PlayerSkinOwnershipService.GetShopPrice(slot.Definition);
                var canAfford = balance >= price;
                var normalColor = canAfford ? UiTheme.SlotFill : UiTheme.SlotEmpty;

                if (slot.Background != null)
                {
                    slot.Background.color = normalColor;
                }

                if (slot.Button != null)
                {
                    slot.Button.interactable = true;
                    var colors = slot.Button.colors;
                    colors.normalColor = normalColor;
                    colors.highlightedColor = canAfford ? UiTheme.ButtonHighlight : UiTheme.SlotEmpty;
                    colors.pressedColor = canAfford ? UiTheme.ButtonPressed : UiTheme.SlotEmpty;
                    colors.selectedColor = colors.highlightedColor;
                    colors.disabledColor = normalColor;
                    colors.fadeDuration = 0.1f;
                    slot.Button.colors = colors;
                }

                if (slot.PriceLabel != null)
                {
                    slot.PriceLabel.text = FormatPrice(price);
                    slot.PriceLabel.color = canAfford
                        ? new Color(0.78f, 0.64f, 0.26f, 0.96f)
                        : UiTheme.TextMuted;
                }

                if (slot.PriceBadge != null)
                {
                    UiTheme.ApplyPriceBadge(slot.PriceBadge, canAfford);
                }
            }
        }

        private void RefreshIapSlotVisuals()
        {
            var canPurchase = !iapPurchaseInProgress && !casePurchaseInProgress && !rewardedAdInProgress;
            var authLinked = PlayerIdentityService.HasAuthorizedYandexLink();
            RefreshRewardedCoinsSlotVisual(canPurchase);

            for (var i = 0; i < iapSlots.Count; i++)
            {
                var slot = iapSlots[i];
                var ownedVip = slot.Definition.IsVipPrefixReward && PlayerProfileService.HasVipPrefix;
                var activeNoAds = slot.Definition.IsNoAdsReward && PlayerProfileService.HasNoAdsPass;
                var slotCanPurchase = authLinked && canPurchase && !(ownedVip && slot.Definition.IsVipPrefixReward);
                var normalColor = slotCanPurchase ? UiTheme.SlotFill : UiTheme.SlotEmpty;

                if (slot.Background != null)
                {
                    slot.Background.color = normalColor;
                }

                if (slot.Button != null)
                {
                    slot.Button.interactable = slotCanPurchase;
                    var colors = slot.Button.colors;
                    colors.normalColor = normalColor;
                    colors.highlightedColor = slotCanPurchase ? UiTheme.ButtonHighlight : UiTheme.SlotEmpty;
                    colors.pressedColor = slotCanPurchase ? UiTheme.ButtonPressed : UiTheme.SlotEmpty;
                    colors.selectedColor = colors.highlightedColor;
                    colors.disabledColor = normalColor;
                    colors.fadeDuration = 0.1f;
                    slot.Button.colors = colors;
                }

                if (slot.PriceLabel != null)
                {
                    var expiryLabel = ownedVip
                        ? PlayerProfileService.FormatVipExpiryShopLabel()
                        : activeNoAds
                            ? PlayerProfileService.FormatNoAdsExpiryShopLabel()
                            : string.Empty;
                    slot.PriceLabel.text = ownedVip || activeNoAds
                        ? (string.IsNullOrEmpty(expiryLabel) ? "Активно" : expiryLabel)
                        : !authLinked
                            ? "Нужен вход"
                        : ShopIapCatalogService.FormatRubles(slot.Definition.PriceRubles);
                    slot.PriceLabel.color = slotCanPurchase
                        ? new Color(0.92f, 0.78f, 0.34f, 0.98f)
                        : UiTheme.TextMuted;
                }

                if (slot.PriceBadge != null)
                {
                    UiTheme.ApplyPriceBadge(slot.PriceBadge, slotCanPurchase);
                }
            }
        }

        private void RefreshCaseSlotVisuals()
        {
            var balance = PlayerProfileService.GetSpendableBalance();
            var authLinked = PlayerIdentityService.HasAuthorizedYandexLink();
            for (var i = 0; i < caseSlots.Count; i++)
            {
                var slot = caseSlots[i];
                var isRealMoney = slot.Definition.IsRealMoneyPurchase;
                var canAfford = isRealMoney
                    ? authLinked && !casePurchaseInProgress && !iapPurchaseInProgress && !rewardedAdInProgress
                    : balance >= CaseCatalogService.GetPrice(slot.Definition) &&
                      !casePurchaseInProgress &&
                      !iapPurchaseInProgress &&
                      !rewardedAdInProgress;
                var normalColor = canAfford ? UiTheme.SlotFill : UiTheme.SlotEmpty;

                if (slot.Background != null)
                {
                    slot.Background.color = normalColor;
                }

                if (slot.Button != null)
                {
                    slot.Button.interactable = canAfford;
                    var colors = slot.Button.colors;
                    colors.normalColor = normalColor;
                    colors.highlightedColor = canAfford ? UiTheme.ButtonHighlight : UiTheme.SlotEmpty;
                    colors.pressedColor = canAfford ? UiTheme.ButtonPressed : UiTheme.SlotEmpty;
                    colors.selectedColor = colors.highlightedColor;
                    colors.disabledColor = normalColor;
                    colors.fadeDuration = 0.1f;
                    slot.Button.colors = colors;
                }

                if (slot.PriceLabel != null)
                {
                    slot.PriceLabel.text = isRealMoney
                        ? (authLinked
                            ? ShopIapCatalogService.FormatRubles(slot.Definition.PriceRubles)
                            : "Нужен вход")
                        : FormatPrice(CaseCatalogService.GetPrice(slot.Definition));
                    slot.PriceLabel.color = canAfford
                        ? new Color(isRealMoney ? 0.92f : 0.78f, isRealMoney ? 0.78f : 0.64f, isRealMoney ? 0.34f : 0.26f, 0.96f)
                        : UiTheme.TextMuted;
                }

                if (slot.PriceBadge != null)
                {
                    UiTheme.ApplyPriceBadge(slot.PriceBadge, canAfford);
                }
            }
        }

        private CaseSlotVisual CreateCaseSlot(Transform parent, CaseDefinition caseDefinition)
        {
            var slotObject = new GameObject("ShopCase_" + caseDefinition.Id);
            slotObject.transform.SetParent(parent, false);

            var slotLayout = slotObject.AddComponent<LayoutElement>();
            slotLayout.preferredWidth = itemCellWidth;
            slotLayout.preferredHeight = itemCellHeight;
            slotLayout.minWidth = itemCellWidth;
            slotLayout.minHeight = itemCellHeight;

            var background = slotObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(background, UiTheme.SlotFill);

            var button = slotObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => OnCaseClicked(caseDefinition));

            var iconObject = new GameObject("Icon", typeof(RectTransform));
            iconObject.transform.SetParent(slotObject.transform, false);

            var iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(slotPadding, slotPadding + priceBadgeHeight + 6f);
            iconRect.offsetMax = new Vector2(-slotPadding, -slotPadding);

            var iconImage = iconObject.AddComponent<Image>();
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            iconImage.sprite = InventoryIconCatalog.GetCaseIcon(caseDefinition.PictureResourcePath);

            var priceBadgeObject = new GameObject("PriceBadge", typeof(RectTransform));
            priceBadgeObject.transform.SetParent(slotObject.transform, false);

            var priceBadgeRect = priceBadgeObject.GetComponent<RectTransform>();
            priceBadgeRect.anchorMin = new Vector2(0.5f, 0f);
            priceBadgeRect.anchorMax = new Vector2(0.5f, 0f);
            priceBadgeRect.pivot = new Vector2(0.5f, 0f);
            priceBadgeRect.anchoredPosition = new Vector2(0f, slotPadding);
            priceBadgeRect.sizeDelta = new Vector2(Mathf.Max(72f, itemCellWidth * 0.62f), priceBadgeHeight);

            var priceBadge = priceBadgeObject.AddComponent<Image>();
            UiTheme.ApplyPriceBadge(priceBadge, canAfford: true);

            var priceLabelObject = new GameObject("PriceLabel", typeof(RectTransform));
            priceLabelObject.transform.SetParent(priceBadgeObject.transform, false);

            var priceLabelRect = priceLabelObject.GetComponent<RectTransform>();
            StretchFull(priceLabelRect);
            priceLabelRect.offsetMin = new Vector2(8f, 2f);
            priceLabelRect.offsetMax = new Vector2(-8f, -2f);

            var priceLabel = priceLabelObject.AddComponent<TextMeshProUGUI>();
            priceLabel.text = caseDefinition.IsRealMoneyPurchase
                ? ShopIapCatalogService.FormatRubles(caseDefinition.PriceRubles)
                : FormatPrice(CaseCatalogService.GetPrice(caseDefinition));
            priceLabel.fontSize = priceFontSize;
            priceLabel.fontStyle = FontStyles.Normal;
            priceLabel.characterSpacing = 1.5f;
            priceLabel.alignment = TextAlignmentOptions.Center;
            priceLabel.verticalAlignment = VerticalAlignmentOptions.Middle;
            UiTheme.ApplyTmp(priceLabel, UiTextRole.Accent);
            priceLabel.raycastTarget = false;

            var lootHover = slotObject.AddComponent<MainMenuShopCaseLootHover>();
            lootHover.Configure(caseDefinition, hostCanvas);

            return new CaseSlotVisual
            {
                Definition = caseDefinition,
                Background = background,
                Button = button,
                PriceLabel = priceLabel,
                PriceBadge = priceBadge
            };
        }

        private IapSlotVisual CreateIapCurrencySlot(Transform parent, ShopIapProductDefinition product)
        {
            var slotObject = new GameObject("ShopIap_" + product.ProductId);
            slotObject.transform.SetParent(parent, false);

            var slotLayout = slotObject.AddComponent<LayoutElement>();
            slotLayout.preferredWidth = itemCellWidth;
            slotLayout.preferredHeight = itemCellHeight;
            slotLayout.minWidth = itemCellWidth;
            slotLayout.minHeight = itemCellHeight;

            var background = slotObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(background, UiTheme.SlotFill);

            var button = slotObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => OnIapProductClicked(product));

            var amountObject = new GameObject("Amount", typeof(RectTransform));
            amountObject.transform.SetParent(slotObject.transform, false);
            var amountRect = amountObject.GetComponent<RectTransform>();
            amountRect.anchorMin = Vector2.zero;
            amountRect.anchorMax = Vector2.one;
            amountRect.offsetMin = new Vector2(slotPadding, slotPadding + priceBadgeHeight + 6f);
            amountRect.offsetMax = new Vector2(-slotPadding, -slotPadding);

            var amountLabel = amountObject.AddComponent<TextMeshProUGUI>();
            amountLabel.text = product.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            amountLabel.fontSize = Mathf.Clamp(itemCellWidth * 0.18f, 24f, 34f);
            amountLabel.fontStyle = FontStyles.Bold;
            amountLabel.alignment = TextAlignmentOptions.Center;
            amountLabel.verticalAlignment = VerticalAlignmentOptions.Middle;
            amountLabel.color = new Color(0.92f, 0.76f, 0.28f, 0.98f);
            UiTheme.ApplyTmp(amountLabel, UiTextRole.Accent);
            amountLabel.raycastTarget = false;

            var priceBadgeObject = new GameObject("PriceBadge", typeof(RectTransform));
            priceBadgeObject.transform.SetParent(slotObject.transform, false);

            var priceBadgeRect = priceBadgeObject.GetComponent<RectTransform>();
            priceBadgeRect.anchorMin = new Vector2(0.5f, 0f);
            priceBadgeRect.anchorMax = new Vector2(0.5f, 0f);
            priceBadgeRect.pivot = new Vector2(0.5f, 0f);
            priceBadgeRect.anchoredPosition = new Vector2(0f, slotPadding);
            priceBadgeRect.sizeDelta = new Vector2(Mathf.Max(88f, itemCellWidth * 0.68f), priceBadgeHeight);

            var priceBadge = priceBadgeObject.AddComponent<Image>();
            UiTheme.ApplyPriceBadge(priceBadge, canAfford: true);

            var priceLabelObject = new GameObject("PriceLabel", typeof(RectTransform));
            priceLabelObject.transform.SetParent(priceBadgeObject.transform, false);
            var priceLabelRect = priceLabelObject.GetComponent<RectTransform>();
            StretchFull(priceLabelRect);

            var priceLabel = priceLabelObject.AddComponent<TextMeshProUGUI>();
            priceLabel.text = ShopIapCatalogService.FormatRubles(product.PriceRubles);
            priceLabel.fontSize = priceFontSize;
            priceLabel.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyTmp(priceLabel, UiTextRole.Body);

            var hover = slotObject.AddComponent<MainMenuShopCoinsHover>();
            hover.Configure(hostCanvas, product);

            return new IapSlotVisual
            {
                Definition = product,
                Background = background,
                Button = button,
                PriceLabel = priceLabel,
                PriceBadge = priceBadge
            };
        }

        private IapSlotVisual CreateIapVipSlot(Transform parent, ShopIapProductDefinition product)
        {
            var slotObject = new GameObject("ShopIap_" + product.ProductId);
            slotObject.transform.SetParent(parent, false);

            var slotLayout = slotObject.AddComponent<LayoutElement>();
            slotLayout.preferredWidth = itemCellWidth;
            slotLayout.preferredHeight = itemCellHeight;
            slotLayout.minWidth = itemCellWidth;
            slotLayout.minHeight = itemCellHeight;

            var background = slotObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(background, UiTheme.SlotFill);
            UiDecor.CreateRarityStripe(slotObject.transform, NicknamePrefixUtility.GetPrefixColor(NicknamePrefixUtility.Vip), 4f);

            var button = slotObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => OnIapProductClicked(product));

            var titleObject = new GameObject("Title", typeof(RectTransform));
            titleObject.transform.SetParent(slotObject.transform, false);
            var titleRect = titleObject.GetComponent<RectTransform>();
            titleRect.anchorMin = Vector2.zero;
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = new Vector2(slotPadding, slotPadding + priceBadgeHeight + 6f);
            titleRect.offsetMax = new Vector2(-slotPadding, -slotPadding);

            var titleLabel = titleObject.AddComponent<TextMeshProUGUI>();
            titleLabel.text = "VIP";
            titleLabel.fontSize = Mathf.Clamp(itemCellWidth * 0.22f, 28f, 40f);
            titleLabel.fontStyle = FontStyles.Bold;
            titleLabel.alignment = TextAlignmentOptions.Center;
            titleLabel.verticalAlignment = VerticalAlignmentOptions.Middle;
            titleLabel.color = NicknamePrefixUtility.GetPrefixColor(NicknamePrefixUtility.Vip);
            UiTheme.ApplyTmp(titleLabel, UiTextRole.Accent);
            titleLabel.raycastTarget = false;

            var priceBadgeObject = new GameObject("PriceBadge", typeof(RectTransform));
            priceBadgeObject.transform.SetParent(slotObject.transform, false);

            var priceBadgeRect = priceBadgeObject.GetComponent<RectTransform>();
            priceBadgeRect.anchorMin = new Vector2(0.5f, 0f);
            priceBadgeRect.anchorMax = new Vector2(0.5f, 0f);
            priceBadgeRect.pivot = new Vector2(0.5f, 0f);
            priceBadgeRect.anchoredPosition = new Vector2(0f, slotPadding);
            priceBadgeRect.sizeDelta = new Vector2(Mathf.Max(88f, itemCellWidth * 0.68f), priceBadgeHeight);

            var priceBadge = priceBadgeObject.AddComponent<Image>();
            UiTheme.ApplyPriceBadge(priceBadge, canAfford: true);

            var priceLabelObject = new GameObject("PriceLabel", typeof(RectTransform));
            priceLabelObject.transform.SetParent(priceBadgeObject.transform, false);
            var priceLabelRect = priceLabelObject.GetComponent<RectTransform>();
            StretchFull(priceLabelRect);

            var priceLabel = priceLabelObject.AddComponent<TextMeshProUGUI>();
            priceLabel.text = ShopIapCatalogService.FormatRubles(product.PriceRubles);
            priceLabel.fontSize = priceFontSize;
            priceLabel.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyTmp(priceLabel, UiTextRole.Body);

            var hover = slotObject.AddComponent<MainMenuShopVipHover>();
            hover.Configure(hostCanvas);

            return new IapSlotVisual
            {
                Definition = product,
                Background = background,
                Button = button,
                PriceLabel = priceLabel,
                PriceBadge = priceBadge
            };
        }

        private IapSlotVisual CreateIapNoAdsSlot(Transform parent, ShopIapProductDefinition product)
        {
            var slotObject = new GameObject("ShopIap_" + product.ProductId);
            slotObject.transform.SetParent(parent, false);

            var slotLayout = slotObject.AddComponent<LayoutElement>();
            slotLayout.preferredWidth = itemCellWidth;
            slotLayout.preferredHeight = itemCellHeight;
            slotLayout.minWidth = itemCellWidth;
            slotLayout.minHeight = itemCellHeight;

            var background = slotObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(background, UiTheme.SlotFill);
            UiDecor.CreateRarityStripe(slotObject.transform, new Color(0.42f, 0.72f, 0.96f, 0.95f), 4f);

            var button = slotObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => OnIapProductClicked(product));

            var titleObject = new GameObject("Title", typeof(RectTransform));
            titleObject.transform.SetParent(slotObject.transform, false);
            var titleRect = titleObject.GetComponent<RectTransform>();
            titleRect.anchorMin = Vector2.zero;
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = new Vector2(slotPadding, slotPadding + priceBadgeHeight + 6f);
            titleRect.offsetMax = new Vector2(-slotPadding, -slotPadding);

            var titleLabel = titleObject.AddComponent<TextMeshProUGUI>();
            titleLabel.text = product.DisplayName;
            titleLabel.fontSize = Mathf.Clamp(itemCellWidth * 0.12f, 16f, 22f);
            titleLabel.fontStyle = FontStyles.Bold;
            titleLabel.alignment = TextAlignmentOptions.Center;
            titleLabel.verticalAlignment = VerticalAlignmentOptions.Middle;
            titleLabel.enableWordWrapping = true;
            titleLabel.color = new Color(0.62f, 0.86f, 1f, 0.98f);
            UiTheme.ApplyTmp(titleLabel, UiTextRole.Accent);
            titleLabel.raycastTarget = false;

            var priceBadgeObject = new GameObject("PriceBadge", typeof(RectTransform));
            priceBadgeObject.transform.SetParent(slotObject.transform, false);

            var priceBadgeRect = priceBadgeObject.GetComponent<RectTransform>();
            priceBadgeRect.anchorMin = new Vector2(0.5f, 0f);
            priceBadgeRect.anchorMax = new Vector2(0.5f, 0f);
            priceBadgeRect.pivot = new Vector2(0.5f, 0f);
            priceBadgeRect.anchoredPosition = new Vector2(0f, slotPadding);
            priceBadgeRect.sizeDelta = new Vector2(Mathf.Max(88f, itemCellWidth * 0.68f), priceBadgeHeight);

            var priceBadge = priceBadgeObject.AddComponent<Image>();
            UiTheme.ApplyPriceBadge(priceBadge, canAfford: true);

            var priceLabelObject = new GameObject("PriceLabel", typeof(RectTransform));
            priceLabelObject.transform.SetParent(priceBadgeObject.transform, false);
            var priceLabelRect = priceLabelObject.GetComponent<RectTransform>();
            StretchFull(priceLabelRect);

            var priceLabel = priceLabelObject.AddComponent<TextMeshProUGUI>();
            priceLabel.text = ShopIapCatalogService.FormatRubles(product.PriceRubles);
            priceLabel.fontSize = priceFontSize;
            priceLabel.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyTmp(priceLabel, UiTextRole.Body);

            var hover = slotObject.AddComponent<MainMenuShopNoAdsHover>();
            hover.Configure(hostCanvas, product);

            return new IapSlotVisual
            {
                Definition = product,
                Background = background,
                Button = button,
                PriceLabel = priceLabel,
                PriceBadge = priceBadge
            };
        }

        private RewardedCoinsSlotVisual CreateRewardedCoinsSlot(Transform parent)
        {
            var slotObject = new GameObject("ShopRewardedCoins");
            slotObject.transform.SetParent(parent, false);

            var slotLayout = slotObject.AddComponent<LayoutElement>();
            slotLayout.preferredWidth = itemCellWidth;
            slotLayout.preferredHeight = itemCellHeight;
            slotLayout.minWidth = itemCellWidth;
            slotLayout.minHeight = itemCellHeight;

            var background = slotObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(background, UiTheme.SlotFill);

            var button = slotObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(OnRewardedCoinsClicked);

            var amountObject = new GameObject("Amount", typeof(RectTransform));
            amountObject.transform.SetParent(slotObject.transform, false);
            var amountRect = amountObject.GetComponent<RectTransform>();
            amountRect.anchorMin = Vector2.zero;
            amountRect.anchorMax = Vector2.one;
            amountRect.offsetMin = new Vector2(slotPadding, slotPadding + priceBadgeHeight + 6f);
            amountRect.offsetMax = new Vector2(-slotPadding, -slotPadding);

            var amountLabel = amountObject.AddComponent<TextMeshProUGUI>();
            amountLabel.text = GameAdsService.RewardedCoinsAmount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            amountLabel.fontSize = Mathf.Clamp(itemCellWidth * 0.18f, 24f, 34f);
            amountLabel.fontStyle = FontStyles.Bold;
            amountLabel.alignment = TextAlignmentOptions.Center;
            amountLabel.verticalAlignment = VerticalAlignmentOptions.Middle;
            amountLabel.color = new Color(0.92f, 0.76f, 0.28f, 0.98f);
            UiTheme.ApplyTmp(amountLabel, UiTextRole.Accent);
            amountLabel.raycastTarget = false;

            var priceBadgeObject = new GameObject("PriceBadge", typeof(RectTransform));
            priceBadgeObject.transform.SetParent(slotObject.transform, false);

            var priceBadgeRect = priceBadgeObject.GetComponent<RectTransform>();
            priceBadgeRect.anchorMin = new Vector2(0.5f, 0f);
            priceBadgeRect.anchorMax = new Vector2(0.5f, 0f);
            priceBadgeRect.pivot = new Vector2(0.5f, 0f);
            priceBadgeRect.anchoredPosition = new Vector2(0f, slotPadding);
            priceBadgeRect.sizeDelta = new Vector2(Mathf.Max(88f, itemCellWidth * 0.68f), priceBadgeHeight);

            var priceBadge = priceBadgeObject.AddComponent<Image>();
            UiTheme.ApplyPriceBadge(priceBadge, canAfford: true);

            var priceLabelObject = new GameObject("PriceLabel", typeof(RectTransform));
            priceLabelObject.transform.SetParent(priceBadgeObject.transform, false);
            var priceLabelRect = priceLabelObject.GetComponent<RectTransform>();
            StretchFull(priceLabelRect);

            var priceLabel = priceLabelObject.AddComponent<TextMeshProUGUI>();
            priceLabel.text = "Реклама";
            priceLabel.fontSize = priceFontSize;
            priceLabel.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyTmp(priceLabel, UiTextRole.Body);

            var hover = slotObject.AddComponent<MainMenuShopRewardedCoinsHover>();
            hover.Configure(hostCanvas);

            return new RewardedCoinsSlotVisual
            {
                Background = background,
                Button = button,
                PriceLabel = priceLabel,
                PriceBadge = priceBadge
            };
        }

        private void RefreshRewardedCoinsSlotVisual(bool canPurchase)
        {
            if (rewardedCoinsSlot == null)
            {
                return;
            }

            var canClaim = canPurchase;

            if (rewardedCoinsSlot.Background != null)
            {
                rewardedCoinsSlot.Background.color = canClaim ? UiTheme.SlotFill : UiTheme.SlotEmpty;
            }

            if (rewardedCoinsSlot.Button != null)
            {
                rewardedCoinsSlot.Button.interactable = canClaim;
                var colors = rewardedCoinsSlot.Button.colors;
                colors.normalColor = canClaim ? UiTheme.SlotFill : UiTheme.SlotEmpty;
                colors.highlightedColor = canClaim ? UiTheme.ButtonHighlight : UiTheme.SlotEmpty;
                colors.pressedColor = canClaim ? UiTheme.ButtonPressed : UiTheme.SlotEmpty;
                colors.selectedColor = colors.highlightedColor;
                colors.disabledColor = canClaim ? UiTheme.SlotFill : UiTheme.SlotEmpty;
                colors.fadeDuration = 0.1f;
                rewardedCoinsSlot.Button.colors = colors;
            }

            if (rewardedCoinsSlot.PriceLabel != null)
            {
                rewardedCoinsSlot.PriceLabel.text = "Реклама";
                rewardedCoinsSlot.PriceLabel.color = canClaim
                    ? new Color(0.92f, 0.78f, 0.34f, 0.98f)
                    : UiTheme.TextMuted;
            }

            if (rewardedCoinsSlot.PriceBadge != null)
            {
                UiTheme.ApplyPriceBadge(rewardedCoinsSlot.PriceBadge, canClaim);
            }
        }

        private void OnRewardedCoinsClicked()
        {
            if (rewardedAdInProgress || iapPurchaseInProgress || casePurchaseInProgress)
            {
                return;
            }

            StartCoroutine(PurchaseRewardedCoinsRoutine());
        }

        private IEnumerator PurchaseRewardedCoinsRoutine()
        {
            rewardedAdInProgress = true;
            RefreshSlotVisuals();

            var completed = false;
            var success = false;
            GameAdsService.RunRewardedCoins(this, granted =>
            {
                success = granted;
                completed = true;
            });

            while (!completed)
            {
                yield return null;
            }

            rewardedAdInProgress = false;
            uiSound?.PlayButton();
            RefreshSlotVisuals();
        }

        private void OnIapProductClicked(ShopIapProductDefinition product)
        {
            if (!product.IsValid || iapPurchaseInProgress || rewardedAdInProgress)
            {
                return;
            }

            if (!PlayerIdentityService.HasAuthorizedYandexLink())
            {
                return;
            }

            if (product.IsVipPrefixReward && PlayerProfileService.HasVipPrefix)
            {
                return;
            }

            StartCoroutine(PurchaseIapProductRoutine(product.ProductId));
        }

        private IEnumerator PurchaseIapProductRoutine(string productId)
        {
            iapPurchaseInProgress = true;
            RefreshSlotVisuals();

            var completed = false;
            YandexGamesShopPurchaseService.TryPurchase(this, productId, _ =>
            {
                completed = true;
            });

            var timeoutAt = Time.unscaledTime + 120f;
            while (!completed && Time.unscaledTime < timeoutAt)
            {
                yield return null;
            }

            if (!completed)
            {
                Debug.LogWarning("[MainMenuShopPanel] IAP purchase timed out waiting for Yandex callback.");
                YandexGamesShopPurchaseService.CancelPendingPurchase();
            }

            iapPurchaseInProgress = false;
            uiSound?.PlayButton();
            RefreshSlotVisuals();
        }

        private void OnCaseClicked(CaseDefinition caseDefinition)
        {
            if (!caseDefinition.IsValid || casePurchaseInProgress || iapPurchaseInProgress || rewardedAdInProgress)
            {
                return;
            }

            if (caseDefinition.IsRealMoneyPurchase)
            {
                if (!PlayerIdentityService.HasAuthorizedYandexLink())
                {
                    return;
                }

                StartCoroutine(PurchaseIapProductRoutine(caseDefinition.RealMoneyProductId));
                return;
            }

            var price = CaseCatalogService.GetPrice(caseDefinition);
            if (PlayerProfileService.GetSpendableBalance() < price)
            {
                uiSound?.PlayButton();
                return;
            }

            if (PlayerProfileService.IsServerSynced)
            {
                if (!PlayerProfileService.TryResolveApiClient(out var apiClient))
                {
                    uiSound?.PlayButton();
                    Debug.LogWarning("[MainMenuShopPanel] Case purchase unavailable until server profile is ready.");
                    return;
                }

                StartCoroutine(PurchaseCaseFromServerRoutine(apiClient, caseDefinition));
                return;
            }

            if (!CaseOpeningService.TryPurchaseLocal(caseDefinition))
            {
                uiSound?.PlayButton();
                Debug.LogWarning("[MainMenuShopPanel] Case purchase failed.");
                return;
            }

            uiSound?.PlayButton();
            RefreshSlotVisuals();
        }

        private IEnumerator PurchaseCaseFromServerRoutine(
            PlayerProfileApiClient apiClient,
            CaseDefinition caseDefinition)
        {
            casePurchaseInProgress = true;
            RefreshCaseSlotVisuals();

            var playerId = PlayerIdentityService.GetOrCreatePlayerId();
            var success = false;
            var error = string.Empty;
            yield return PlayerProfileService.PurchaseCase(
                this,
                apiClient,
                playerId,
                caseDefinition.Id,
                (ok, responseError) =>
                {
                    success = ok;
                    error = responseError;
                });

            casePurchaseInProgress = false;
            uiSound?.PlayButton();
            RefreshSlotVisuals();

            if (!success && !string.IsNullOrWhiteSpace(error))
            {
                Debug.LogWarning($"[MainMenuShopPanel] Case purchase failed: {error}");
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

        private ShopSlotVisual CreateItemSlot(Transform parent, PlayerSkinDefinition item)
        {
            var slotObject = new GameObject("ShopItem_" + item.Id);
            slotObject.transform.SetParent(parent, false);

            var slotLayout = slotObject.AddComponent<LayoutElement>();
            slotLayout.preferredWidth = itemCellWidth;
            slotLayout.preferredHeight = itemCellHeight;
            slotLayout.minWidth = itemCellWidth;
            slotLayout.minHeight = itemCellHeight;

            var background = slotObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(background, UiTheme.SlotFill);
            UiDecor.CreateRarityStripe(slotObject.transform, ShopCatalogService.GetRarityStripeColor(item.Id));

            var button = slotObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => OnItemClicked(item));
            UiMotion.AttachButtonMotion(button);

            var iconObject = new GameObject("Icon", typeof(RectTransform));
            iconObject.transform.SetParent(slotObject.transform, false);

            var iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(slotPadding, slotPadding + priceBadgeHeight + 6f);
            iconRect.offsetMax = new Vector2(-slotPadding, -slotPadding);

            var iconImage = iconObject.AddComponent<Image>();
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            iconImage.sprite = InventoryIconCatalog.GetSkinIcon(item);

            var priceBadgeObject = new GameObject("PriceBadge", typeof(RectTransform));
            priceBadgeObject.transform.SetParent(slotObject.transform, false);

            var priceBadgeRect = priceBadgeObject.GetComponent<RectTransform>();
            priceBadgeRect.anchorMin = new Vector2(0.5f, 0f);
            priceBadgeRect.anchorMax = new Vector2(0.5f, 0f);
            priceBadgeRect.pivot = new Vector2(0.5f, 0f);
            priceBadgeRect.anchoredPosition = new Vector2(0f, slotPadding);
            priceBadgeRect.sizeDelta = new Vector2(Mathf.Max(72f, itemCellWidth * 0.62f), priceBadgeHeight);

            var priceBadge = priceBadgeObject.AddComponent<Image>();
            UiTheme.ApplyPriceBadge(priceBadge, canAfford: true);

            var priceLabelObject = new GameObject("PriceLabel", typeof(RectTransform));
            priceLabelObject.transform.SetParent(priceBadgeObject.transform, false);

            var priceLabelRect = priceLabelObject.GetComponent<RectTransform>();
            StretchFull(priceLabelRect);
            priceLabelRect.offsetMin = new Vector2(8f, 2f);
            priceLabelRect.offsetMax = new Vector2(-8f, -2f);

            var priceLabel = priceLabelObject.AddComponent<TextMeshProUGUI>();
            priceLabel.text = FormatPrice(PlayerSkinOwnershipService.GetShopPrice(item));
            priceLabel.fontSize = priceFontSize;
            priceLabel.fontStyle = FontStyles.Normal;
            priceLabel.characterSpacing = 1.5f;
            priceLabel.alignment = TextAlignmentOptions.Center;
            priceLabel.verticalAlignment = VerticalAlignmentOptions.Middle;
            UiTheme.ApplyTmp(priceLabel, UiTextRole.Accent);
            priceLabel.raycastTarget = false;

            UiTooltipController.AttachSlotTooltip(
                slotObject,
                hostCanvas,
                () => item.DisplayName,
                () =>
                {
                    var price = PlayerSkinOwnershipService.GetShopPrice(item);
                    var rarity = SkinRarityUtility.GetDisplayName(ShopCatalogService.GetRarity(item.Id));
                    return rarity + " · " + FormatPrice(price) + " монет";
                });

            return new ShopSlotVisual
            {
                Definition = item,
                Background = background,
                Button = button,
                PriceLabel = priceLabel,
                PriceBadge = priceBadge
            };
        }

        private void OnItemClicked(PlayerSkinDefinition item)
        {
            if (PlayerSkinOwnershipService.IsOwned(item))
            {
                return;
            }

            if (PlayerProfileService.IsServerSynced)
            {
                var menu = FindObjectOfType<MainMenuController>();
                if (menu != null && menu.ProfileApiClient != null)
                {
                    StartCoroutine(PurchaseFromServerRoutine(menu, item));
                    return;
                }
            }

            if (!PlayerSkinOwnershipService.TryPurchase(item))
            {
                uiSound?.PlayButton();
                return;
            }

            MainMenuNotificationState.MarkSkinAsNew(item.Id);
            uiSound?.PlayButton();
            RefreshSlotVisuals();
        }

        private IEnumerator PurchaseFromServerRoutine(MainMenuController menu, PlayerSkinDefinition item)
        {
            var success = false;
            yield return PlayerProfileService.PurchaseSkin(
                this,
                menu.ProfileApiClient,
                menu.LocalPlayerId,
                item.Id,
                (ok, _) => success = ok);

            uiSound?.PlayButton();
            if (success)
            {
                RefreshSlotVisuals();
                playerPreview?.PreviewInventoryItem(item);
            }
        }

        private void RefreshAffordability()
        {
            RefreshSlotVisuals();
        }

        private static string FormatPrice(int price)
        {
            return price.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
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
                yield return null;
            }

            panelGroup.alpha = toAlpha;
            panelGroup.interactable = show;
            panelGroup.blocksRaycasts = show;
            transitionCoroutine = null;
        }

        private static void EnableViewportScrollCapture(GameObject viewportObject)
        {
            var viewportImage = viewportObject.AddComponent<Image>();
            viewportImage.sprite = UiTheme.WhiteSprite;
            viewportImage.color = Color.clear;
            viewportImage.raycastTarget = true;
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
    }
}
