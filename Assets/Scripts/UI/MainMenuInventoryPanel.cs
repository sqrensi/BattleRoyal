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
        private static Sprite whiteSprite;

        private static readonly Color PanelColor = new Color(0.06f, 0.08f, 0.1f, 0.72f);
        private static readonly Color ItemBackgroundColor = new Color(0.12f, 0.14f, 0.17f, 0.88f);
        private static readonly Color EquippedBackgroundColor = new Color(0.18f, 0.48f, 0.42f, 0.96f);
        private static readonly Color EquippedHighlightedColor = new Color(0.22f, 0.58f, 0.5f, 1f);
        private static readonly Color EquippedPressedColor = new Color(0.14f, 0.38f, 0.34f, 1f);
        private static readonly Color ItemHighlightedColor = new Color(0.16f, 0.18f, 0.21f, 0.94f);
        private static readonly Color ItemPressedColor = new Color(0.1f, 0.12f, 0.14f, 0.98f);
        private static readonly Color ScrollTrackColor = new Color(0.1f, 0.12f, 0.14f, 0.55f);
        private static readonly Color ScrollHandleColor = new Color(0.24f, 0.28f, 0.32f, 0.92f);
        private static readonly Color TitleColor = new Color(0.94f, 0.96f, 0.98f, 0.98f);

        [SerializeField] private float edgeMargin = 44f;
        [SerializeField] private float panelWidth = 528f;
        [SerializeField] private float innerPadding = 24f;
        [SerializeField] private float fadeDuration = 0.38f;
        [SerializeField] private float slideOffset = 56f;
        [SerializeField] private float itemSpacing = 14f;
        [SerializeField] private float headerHeight = 76f;
        [SerializeField] private float scrollbarWidth = 12f;
        [SerializeField] private float scrollbarGap = 8f;
        [SerializeField] private float titleFontSize = 28f;
        [SerializeField] private float slotPadding = 12f;

        private readonly List<ItemSlotVisual> itemSlots = new List<ItemSlotVisual>(32);

        private CanvasGroup panelGroup;
        private RectTransform panelRect;
        private RectTransform contentRect;
        private Vector2 shownAnchoredPosition;
        private Vector2 hiddenAnchoredPosition;
        private float itemCellWidth;
        private float itemCellHeight;
        private MainMenuPlayerPreview playerPreview;
        private MainMenuUiSoundController uiSound;
        private bool isVisible;
        private Coroutine transitionCoroutine;
        private Coroutine pulseCoroutine;
        private string pendingPulseItemId;

        private sealed class ItemSlotVisual
        {
            public PlayerSkinDefinition Definition;
            public Image Background;
            public Button Button;
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

            itemCellWidth = (panelWidth - innerPadding * 2f - itemSpacing - scrollbarWidth - scrollbarGap) * 0.5f;
            itemCellHeight = itemCellWidth;

            var panelObject = new GameObject("MainMenuInventoryPanel");
            panelObject.transform.SetParent(canvasRect, false);

            panelRect = panelObject.AddComponent<RectTransform>();
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
            background.sprite = GetWhiteSprite();
            background.type = Image.Type.Simple;
            background.color = PanelColor;
            background.raycastTarget = true;

            panelGroup = panelObject.AddComponent<CanvasGroup>();
            panelGroup.alpha = 0f;
            panelGroup.interactable = false;
            panelGroup.blocksRaycasts = false;

            BuildHeader(panelObject.transform);
            BuildItemsGrid(panelObject.transform);
        }

        private void OnEnable()
        {
            PlayerSkinOwnershipService.OwnershipChanged += RebuildItems;
            PlayerSkinOwnershipService.EquipmentChanged += OnEquipmentChanged;
        }

        private void OnDisable()
        {
            PlayerSkinOwnershipService.OwnershipChanged -= RebuildItems;
            PlayerSkinOwnershipService.EquipmentChanged -= OnEquipmentChanged;
        }

        private void OnEquipmentChanged()
        {
            playerPreview?.RefreshSkins();

            if (contentRect == null || itemSlots.Count == 0)
            {
                return;
            }

            RefreshEquippedVisuals(pendingPulseItemId);
            TryRefreshPreviewWeapon(pendingPulseItemId);
            pendingPulseItemId = null;
        }

        private void TryRefreshPreviewWeapon(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId) ||
                !PlayerSkinSelectionService.TryGetDefinitionById(itemId, out var definition))
            {
                return;
            }

            if (PlayerSkinSelectionService.TryResolveWeaponKind(definition, out var weaponKind))
            {
                playerPreview?.ShowWeaponWithSkin(weaponKind);
            }
        }

        public void Show()
        {
            if (isVisible)
            {
                RebuildItems();
                RefreshEquippedVisuals();
                return;
            }

            isVisible = true;
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
            title.fontStyle = FontStyles.Bold;
            title.alignment = TextAlignmentOptions.Center;
            title.color = TitleColor;
            title.raycastTarget = false;
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
            contentBackground.sprite = GetWhiteSprite();
            contentBackground.color = Color.clear;
            contentBackground.raycastTarget = true;

            var grid = contentObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(itemCellWidth, itemCellHeight);
            grid.spacing = new Vector2(itemSpacing, itemSpacing);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.padding = new RectOffset(0, 0, 4, 8);

            var fitter = contentObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewportRect;
            scroll.content = contentRect;

            RebuildItems();
        }

        private void RebuildItems()
        {
            if (contentRect == null)
            {
                return;
            }

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

            RefreshEquippedVisuals();
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
            trackImage.sprite = GetWhiteSprite();
            trackImage.type = Image.Type.Simple;
            trackImage.color = ScrollTrackColor;

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
            handleImage.sprite = GetWhiteSprite();
            handleImage.type = Image.Type.Simple;
            handleImage.color = ScrollHandleColor;

            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;
            return scrollbar;
        }

        private ItemSlotVisual CreateItemSlot(Transform parent, PlayerSkinDefinition item)
        {
            var slotObject = new GameObject("Item_" + item.Id);
            slotObject.transform.SetParent(parent, false);

            var slotLayout = slotObject.AddComponent<LayoutElement>();
            slotLayout.preferredWidth = itemCellWidth;
            slotLayout.preferredHeight = itemCellHeight;
            slotLayout.minWidth = itemCellWidth;
            slotLayout.minHeight = itemCellHeight;

            var background = slotObject.AddComponent<Image>();
            background.sprite = GetWhiteSprite();
            background.type = Image.Type.Simple;
            background.color = ItemBackgroundColor;

            var button = slotObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => OnItemClicked(item));

            var iconObject = new GameObject("Icon", typeof(RectTransform));
            iconObject.transform.SetParent(slotObject.transform, false);

            var iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(slotPadding, slotPadding);
            iconRect.offsetMax = new Vector2(-slotPadding, -slotPadding);

            var iconImage = iconObject.AddComponent<Image>();
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            iconImage.sprite = InventoryIconCatalog.GetSkinIcon(item.PictureResourcePath);

            return new ItemSlotVisual
            {
                Definition = item,
                Background = background,
                Button = button
            };
        }

        private void OnItemClicked(PlayerSkinDefinition item)
        {
            if (!PlayerSkinSelectionService.TryResolveSlot(item, out var slot))
            {
                return;
            }

            if (PlayerProfileService.IsServerSynced)
            {
                var menu = FindObjectOfType<MainMenuController>();
                if (menu != null && menu.ProfileApiClient != null)
                {
                    pendingPulseItemId = item.Id;
                    StartCoroutine(EquipFromServerRoutine(menu, item, slot));
                    return;
                }
            }

            if (!PlayerSkinSelectionService.TryEquip(item))
            {
                return;
            }

            pendingPulseItemId = item.Id;
            TryRefreshPreviewWeapon(item.Id);
            playerPreview?.RefreshSkins();
            RefreshEquippedVisuals(pendingPulseItemId);
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
                yield break;
            }

            var skinId = wasEquipped ? "__none__" : item.Id;
            var success = false;
            yield return PlayerProfileService.EquipSkin(
                this,
                menu.ProfileApiClient,
                menu.LocalPlayerId,
                slot,
                skinId,
                (ok, _) => success = ok);

            if (!success)
            {
                pendingPulseItemId = null;
                yield break;
            }

            RefreshEquippedVisuals(pendingPulseItemId);
            TryRefreshPreviewWeapon(item.Id);
            playerPreview?.RefreshSkins();
            uiSound?.PlayButton();
        }

        private void RefreshEquippedVisuals(string pulseItemId = null)
        {
            for (var i = 0; i < itemSlots.Count; i++)
            {
                var slot = itemSlots[i];
                var equipped = PlayerSkinSelectionService.IsEquipped(slot.Definition);
                var normalColor = equipped ? EquippedBackgroundColor : ItemBackgroundColor;
                var highlightedColor = equipped ? EquippedHighlightedColor : ItemHighlightedColor;
                var pressedColor = equipped ? EquippedPressedColor : ItemPressedColor;

                if (slot.Background != null)
                {
                    slot.Background.color = normalColor;
                }

                if (slot.Button != null)
                {
                    var colors = slot.Button.colors;
                    colors.normalColor = normalColor;
                    colors.highlightedColor = highlightedColor;
                    colors.pressedColor = pressedColor;
                    colors.selectedColor = highlightedColor;
                    colors.disabledColor = normalColor;
                    colors.fadeDuration = 0f;
                    slot.Button.colors = colors;
                }
            }

            if (!string.IsNullOrWhiteSpace(pulseItemId))
            {
                PulseItem(pulseItemId);
            }
        }

        private void PulseItem(string itemId)
        {
            if (pulseCoroutine != null)
            {
                StopCoroutine(pulseCoroutine);
            }

            pulseCoroutine = StartCoroutine(PulseItemRoutine(itemId));
        }

        private IEnumerator PulseItemRoutine(string itemId)
        {
            ItemSlotVisual targetSlot = null;
            for (var i = 0; i < itemSlots.Count; i++)
            {
                if (itemSlots[i].Definition.IsValid &&
                    string.Equals(itemSlots[i].Definition.Id, itemId, System.StringComparison.OrdinalIgnoreCase))
                {
                    targetSlot = itemSlots[i];
                    break;
                }
            }

            if (targetSlot?.Background == null)
            {
                pulseCoroutine = null;
                yield break;
            }

            var equipped = PlayerSkinSelectionService.IsEquipped(targetSlot.Definition);
            var baseColor = equipped ? EquippedBackgroundColor : ItemBackgroundColor;
            var pulseColor = equipped ? EquippedHighlightedColor : ItemHighlightedColor;

            targetSlot.Background.color = pulseColor;
            yield return new WaitForSecondsRealtime(0.1f);
            targetSlot.Background.color = baseColor;
            pulseCoroutine = null;
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
        }

        private static void EnableViewportScrollCapture(GameObject viewportObject)
        {
            var viewportImage = viewportObject.AddComponent<Image>();
            viewportImage.sprite = GetWhiteSprite();
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
