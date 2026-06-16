using ShooterPrototype.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class InventorySlotView :
        MonoBehaviour,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler,
        IDropHandler,
        IPointerClickHandler
    {
        [SerializeField] private InventorySlotKind slotKind;
        [SerializeField] private int weaponSlotIndex = -1;
        [SerializeField] private string itemId = string.Empty;
        [SerializeField] private bool allowDrag = true;

        private PlayerInventoryPanelController panel;
        private Image background;
        private Image iconImage;
        private Text titleLabel;
        private Text countLabel;
        private InventoryDragPayload payload;

        private CanvasGroup canvasGroup;
        private Transform dragOriginalParent;
        private int dragOriginalSiblingIndex;
        private Vector2 dragOriginalAnchoredPosition;
        private Vector2 dragOriginalAnchorMin;
        private Vector2 dragOriginalAnchorMax;
        private Vector2 dragOriginalOffsetMin;
        private Vector2 dragOriginalOffsetMax;
        private Vector2 dragOriginalPivot;
        private Vector2 dragOriginalSizeDelta;
        private Vector2 dragPointerOffset;
        private bool isDragVisualActive;

        public InventorySlotKind SlotKind => slotKind;
        public int WeaponSlotIndex => weaponSlotIndex;
        public string ItemId => itemId;
        public InventoryDragPayload Payload => payload;
        public bool AllowDrag => allowDrag;

        public void Bind(PlayerInventoryPanelController owner)
        {
            panel = owner;
        }

        public void ConfigureFloorItem()
        {
            slotKind = InventorySlotKind.FloorItem;
            weaponSlotIndex = -1;
            itemId = string.Empty;
            allowDrag = true;
        }

        public void ConfigureFloorArea()
        {
            slotKind = InventorySlotKind.FloorArea;
            weaponSlotIndex = -1;
            itemId = string.Empty;
            allowDrag = false;
        }

        public void ConfigureInventoryItem(string stackItemId, bool draggable = true)
        {
            slotKind = InventorySlotKind.InventoryItem;
            weaponSlotIndex = -1;
            itemId = stackItemId ?? string.Empty;
            allowDrag = draggable;
        }

        public void ConfigureInventoryArea()
        {
            slotKind = InventorySlotKind.InventoryArea;
            weaponSlotIndex = -1;
            itemId = string.Empty;
            allowDrag = false;
        }

        public void ConfigureWeaponSlot(int slotIndex)
        {
            slotKind = InventorySlotKind.WeaponSlot;
            weaponSlotIndex = slotIndex;
            itemId = string.Empty;
            allowDrag = true;
        }

        public void ConfigureWeaponArea()
        {
            slotKind = InventorySlotKind.WeaponArea;
            weaponSlotIndex = -1;
            itemId = string.Empty;
            allowDrag = false;
        }

        public void SetAreaTint(Color tint)
        {
            payload = default;
            ApplyTint(tint);
            if (iconImage != null)
            {
                iconImage.enabled = false;
            }

            if (titleLabel != null)
            {
                titleLabel.text = string.Empty;
            }

            if (countLabel != null)
            {
                countLabel.text = string.Empty;
            }
        }

        public void SetPresentation(string title, string subtitle, Color tint, in InventoryDragPayload dragPayload)
        {
            payload = dragPayload;
            ApplyTint(tint);
            if (iconImage != null)
            {
                iconImage.enabled = false;
            }

            if (titleLabel != null)
            {
                titleLabel.text = string.IsNullOrWhiteSpace(subtitle)
                    ? title ?? string.Empty
                    : $"{title}\n{subtitle}";
            }

            if (countLabel != null)
            {
                countLabel.text = string.Empty;
            }
        }

        public void SetIconPresentation(
            Sprite icon,
            string countText,
            string fallbackTitle,
            Color tint,
            in InventoryDragPayload dragPayload,
            bool preserveAspect = true)
        {
            payload = dragPayload;
            ApplyTint(tint);

            if (iconImage != null)
            {
                iconImage.enabled = icon != null;
                iconImage.sprite = icon;
                iconImage.preserveAspect = preserveAspect;
            }

            if (titleLabel != null)
            {
                titleLabel.text = icon == null ? fallbackTitle ?? string.Empty : string.Empty;
            }

            if (countLabel != null)
            {
                countLabel.text = countText ?? string.Empty;
            }
        }

        public void ClearPresentation()
        {
            payload = default;
            if (titleLabel != null)
            {
                titleLabel.text = string.Empty;
            }

            if (countLabel != null)
            {
                countLabel.text = string.Empty;
            }

            if (iconImage != null)
            {
                iconImage.enabled = false;
                iconImage.sprite = null;
            }

            ApplyTint(ResolveDefaultTint(slotKind));
        }

        public void BuildVisual(RectTransform parent, Vector2 size, bool stretch = false)
        {
            var rect = gameObject.GetComponent<RectTransform>();
            if (rect == null)
            {
                rect = gameObject.AddComponent<RectTransform>();
            }

            rect.SetParent(parent, false);
            if (stretch)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
            else
            {
                rect.sizeDelta = size;
            }

            background = gameObject.GetComponent<Image>();
            if (background == null)
            {
                background = gameObject.AddComponent<Image>();
            }

            background.color = ResolveDefaultTint(slotKind);
            background.raycastTarget = true;

            var iconObject = new GameObject("Icon");
            iconObject.transform.SetParent(transform, false);
            var iconRect = iconObject.AddComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.08f, 0.22f);
            iconRect.anchorMax = new Vector2(0.92f, 0.96f);
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;
            iconImage = iconObject.AddComponent<Image>();
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            iconImage.enabled = false;

            var titleObject = new GameObject("Title");
            titleObject.transform.SetParent(transform, false);
            var titleRect = titleObject.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.05f, 0.2f);
            titleRect.anchorMax = new Vector2(0.95f, 0.95f);
            titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMax = Vector2.zero;
            titleLabel = titleObject.AddComponent<Text>();
            titleLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            titleLabel.fontSize = 11;
            titleLabel.alignment = TextAnchor.MiddleCenter;
            titleLabel.color = Color.white;
            titleLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            titleLabel.verticalOverflow = VerticalWrapMode.Overflow;
            titleLabel.raycastTarget = false;

            var countObject = new GameObject("Count");
            countObject.transform.SetParent(transform, false);
            var countRect = countObject.AddComponent<RectTransform>();
            countRect.anchorMin = new Vector2(0f, 0f);
            countRect.anchorMax = new Vector2(1f, 0f);
            countRect.pivot = new Vector2(0.5f, 0f);
            countRect.sizeDelta = new Vector2(0f, 18f);
            countRect.anchoredPosition = new Vector2(0f, 2f);
            countLabel = countObject.AddComponent<Text>();
            countLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            countLabel.fontSize = 12;
            countLabel.fontStyle = FontStyle.Bold;
            countLabel.alignment = TextAnchor.MiddleCenter;
            countLabel.color = new Color(1f, 0.95f, 0.7f, 1f);
            countLabel.raycastTarget = false;
        }

        private void ApplyTint(Color tint)
        {
            if (background != null)
            {
                background.color = tint;
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (panel == null || !allowDrag || !payload.HasDragData)
            {
                return;
            }

            panel.BeginDrag(this, payload, eventData);
        }

        public void BeginDragMove(Canvas rootCanvas, PointerEventData eventData)
        {
            var rect = transform as RectTransform;
            if (rect == null || rootCanvas == null)
            {
                return;
            }

            dragOriginalParent = transform.parent;
            dragOriginalSiblingIndex = transform.GetSiblingIndex();
            dragOriginalAnchoredPosition = rect.anchoredPosition;
            dragOriginalAnchorMin = rect.anchorMin;
            dragOriginalAnchorMax = rect.anchorMax;
            dragOriginalOffsetMin = rect.offsetMin;
            dragOriginalOffsetMax = rect.offsetMax;
            dragOriginalPivot = rect.pivot;
            dragOriginalSizeDelta = rect.sizeDelta;

            EnsureCanvasGroup().blocksRaycasts = false;
            transform.SetParent(rootCanvas.transform, true);
            transform.SetAsLastSibling();

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rootCanvas.transform as RectTransform,
                    eventData.position,
                    eventData.pressEventCamera,
                    out var localPoint))
            {
                dragPointerOffset = rect.anchoredPosition - localPoint;
            }

            isDragVisualActive = true;
        }

        public void UpdateDragMove(Canvas rootCanvas, PointerEventData eventData)
        {
            if (!isDragVisualActive || rootCanvas == null)
            {
                return;
            }

            var rect = transform as RectTransform;
            if (rect == null)
            {
                return;
            }

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rootCanvas.transform as RectTransform,
                    eventData.position,
                    eventData.pressEventCamera,
                    out var localPoint))
            {
                rect.anchoredPosition = localPoint + dragPointerOffset;
            }
        }

        public void EndDragMove(bool restoreLayout)
        {
            if (!isDragVisualActive)
            {
                return;
            }

            EnsureCanvasGroup().blocksRaycasts = true;
            isDragVisualActive = false;

            if (!restoreLayout || dragOriginalParent == null)
            {
                return;
            }

            var rect = transform as RectTransform;
            transform.SetParent(dragOriginalParent, false);
            transform.SetSiblingIndex(dragOriginalSiblingIndex);
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = dragOriginalAnchorMin;
            rect.anchorMax = dragOriginalAnchorMax;
            rect.offsetMin = dragOriginalOffsetMin;
            rect.offsetMax = dragOriginalOffsetMax;
            rect.pivot = dragOriginalPivot;
            rect.sizeDelta = dragOriginalSizeDelta;
            rect.anchoredPosition = dragOriginalAnchoredPosition;
        }

        private CanvasGroup EnsureCanvasGroup()
        {
            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }

            return canvasGroup;
        }

        public void OnDrag(PointerEventData eventData)
        {
            panel?.UpdateDrag(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            panel?.EndDrag(eventData, droppedOnTarget: false);
        }

        public void OnDrop(PointerEventData eventData)
        {
            panel?.HandleDropOnSlot(this, eventData);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (panel == null)
            {
                return;
            }

            if (eventData.button == PointerEventData.InputButton.Right)
            {
                panel.HandleRightClick(this, payload);
                return;
            }

            if (eventData.button == PointerEventData.InputButton.Left &&
                slotKind == InventorySlotKind.FloorItem)
            {
                panel.HandleFloorLeftClick(payload);
            }
        }

        private static Color ResolveDefaultTint(InventorySlotKind kind)
        {
            switch (kind)
            {
                case InventorySlotKind.FloorArea:
                    return new Color(0.14f, 0.12f, 0.1f, 0.32f);
                case InventorySlotKind.InventoryArea:
                    return new Color(0.1f, 0.12f, 0.16f, 0.32f);
                case InventorySlotKind.WeaponArea:
                    return new Color(0.1f, 0.14f, 0.11f, 0.32f);
                default:
                    return new Color(0.34f, 0.34f, 0.36f, 0.52f);
            }
        }
    }
}
