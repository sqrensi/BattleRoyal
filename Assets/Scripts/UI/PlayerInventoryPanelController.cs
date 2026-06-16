using System.Collections.Generic;
using ShooterPrototype.Network;
using ShooterPrototype.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(650)]
    public sealed class PlayerInventoryPanelController : MonoBehaviour
    {
        private const float NearbyRadius = 4f;
        private const float DoubleClickWindowSeconds = 0.35f;
        private static readonly Color ItemSlotBackgroundTint = new Color(0.34f, 0.34f, 0.36f, 0.52f);
        private static readonly Color ItemSlotEmptyBackgroundTint = new Color(0.26f, 0.26f, 0.28f, 0.42f);
        private static PlayerInventoryPanelController activePanel;

        [SerializeField] private bool hideLegacyHudInventory = true;

        private PlayerWeaponLoadoutController weaponLoadoutController;
        private PlayerPickupController pickupController;
        private PlayerWeaponMount weaponMount;
        private PlayerInventory inventory;
        private PlayerHealth health;
        private FpsCharacterController fpsController;
        private RealtimeTransportClient transportClient;

        private GameObject panelRoot;
        private Canvas panelCanvas;
        private InventorySlotView floorAreaView;
        private InventorySlotView inventoryAreaView;
        private InventorySlotView weaponAreaView;
        private InventorySlotView weaponSlot0View;
        private InventorySlotView weaponSlot1View;
        private Transform inventoryItemsRow;
        private readonly List<InventorySlotView> inventoryItemViews = new List<InventorySlotView>(12);
        private readonly List<InventoryDisplayKey> itemDisplayOrder = new List<InventoryDisplayKey>(12);
        private readonly List<InventorySlotView> floorItemViews = new List<InventorySlotView>(8);
        private Transform floorItemsRow;
        private InventorySlotView dragSourceView;
        private InventoryDragPayload activeDragPayload;
        private bool isDragging;

        private string lastFloorClickSpawnId = string.Empty;
        private float lastFloorClickTime;

        private bool wasOpen;

        private readonly List<PlayerPickupController.NearbyPickupInfo> nearbyBuffer =
            new List<PlayerPickupController.NearbyPickupInfo>(12);

        private static readonly WeaponKind[] AmmoKinds =
        {
            WeaponKind.AssaultRifle,
            WeaponKind.SniperRifle,
            WeaponKind.Pistol,
            WeaponKind.Mp7
        };

        public static bool IsOpen => activePanel != null && activePanel.wasOpen;

        private enum InventoryDisplayKind
        {
            Medkit = 0,
            Grenade = 1,
            Ammo = 2
        }

        private struct InventoryDisplayKey
        {
            public InventoryDisplayKind Kind;
            public WeaponKind AmmoKind;
            public string ItemId;
        }

        private void Awake()
        {
            if (GetComponent<RemoteThirdPersonPlayerBootstrap>() != null)
            {
                enabled = false;
                return;
            }

            CacheComponents();
            FpsCharacterController.SuppressTabCursorToggle = true;
            EnsureEventSystemExists();
            BuildUi();
            SetPanelOpen(false);
            if (hideLegacyHudInventory)
            {
                GameHudController.SetLegacyInventoryVisible(false);
            }
        }

        private void OnDestroy()
        {
            if (activePanel == this)
            {
                activePanel = null;
            }

            if (GetComponent<RemoteThirdPersonPlayerBootstrap>() == null)
            {
                FpsCharacterController.SuppressTabCursorToggle = false;
            }
        }

        private void Start()
        {
            if (transportClient == null)
            {
                transportClient = FindFirstObjectByType<RealtimeTransportClient>();
            }
        }

        private void Update()
        {
            if (health != null && health.IsDead && wasOpen)
            {
                SetPanelOpen(false);
            }

            if (ReadInventoryTogglePressed())
            {
                SetPanelOpen(!wasOpen);
            }

            if (wasOpen && !isDragging)
            {
                RefreshPanelContents();
            }
        }

        private void CacheComponents()
        {
            weaponLoadoutController = GetComponent<PlayerWeaponLoadoutController>();
            pickupController = GetComponent<PlayerPickupController>();
            weaponMount = GetComponent<PlayerWeaponMount>();
            inventory = GetComponent<PlayerInventory>();
            health = GetComponent<PlayerHealth>();
            fpsController = GetComponent<FpsCharacterController>();
        }

        private void BuildUi()
        {
            panelRoot = new GameObject("InventoryPanelRoot");
            panelRoot.transform.SetParent(transform, false);

            panelCanvas = panelRoot.AddComponent<Canvas>();
            panelCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            panelCanvas.sortingOrder = 120;
            panelRoot.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            panelRoot.AddComponent<GraphicRaycaster>();

            var dimmer = CreateRect("Dimmer", panelRoot.transform);
            StretchFull(dimmer);
            var dimmerImage = dimmer.gameObject.AddComponent<Image>();
            dimmerImage.color = new Color(0f, 0f, 0f, 0.22f);
            dimmerImage.raycastTarget = true;

            var panel = CreateRect("Panel", panelRoot.transform);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(760f, 360f);
            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = new Color(0.08f, 0.08f, 0.1f, 0.52f);

            var title = CreateLabel(panel, "Title", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            title.rectTransform.offsetMin = new Vector2(16f, -34f);
            title.rectTransform.offsetMax = new Vector2(-16f, -8f);
            title.text = "Инвентарь  [Tab]";
            title.fontSize = 18;
            title.alignment = TextAnchor.MiddleLeft;

            var columns = CreateRect("Columns", panel);
            columns.anchorMin = new Vector2(0f, 0f);
            columns.anchorMax = new Vector2(1f, 1f);
            columns.offsetMin = new Vector2(12f, 12f);
            columns.offsetMax = new Vector2(-12f, -40f);

            var floorColumn = BuildZoneColumn(columns, "На полу", 0f, 0.34f);
            var itemsColumn = BuildZoneColumn(columns, "Предметы", 0.35f, 0.64f);
            var weaponsColumn = BuildZoneColumn(columns, "Оружие", 0.65f, 1f);

            floorAreaView = CreateAreaSlot(floorColumn, InventorySlotKind.FloorArea, ConfigureFloorArea);
            floorAreaView.SetAreaTint(new Color(0.14f, 0.12f, 0.1f, 0.32f));
            floorItemsRow = CreateRect("FloorItemsRow", floorColumn);
            var floorItemsRect = floorItemsRow as RectTransform;
            floorItemsRect.anchorMin = new Vector2(0f, 0f);
            floorItemsRect.anchorMax = new Vector2(1f, 1f);
            floorItemsRect.offsetMin = new Vector2(8f, 8f);
            floorItemsRect.offsetMax = new Vector2(-8f, -28f);

            inventoryAreaView = CreateAreaSlot(itemsColumn, InventorySlotKind.InventoryArea, ConfigureInventoryArea);
            inventoryAreaView.SetAreaTint(new Color(0.1f, 0.12f, 0.16f, 0.32f));
            inventoryItemsRow = CreateRect("InventoryItemsRow", itemsColumn);
            var inventoryItemsRect = inventoryItemsRow as RectTransform;
            inventoryItemsRect.anchorMin = new Vector2(0f, 0f);
            inventoryItemsRect.anchorMax = new Vector2(1f, 1f);
            inventoryItemsRect.offsetMin = new Vector2(8f, 8f);
            inventoryItemsRect.offsetMax = new Vector2(-8f, -8f);

            weaponAreaView = CreateAreaSlot(weaponsColumn, InventorySlotKind.WeaponArea, ConfigureWeaponArea);
            weaponAreaView.SetAreaTint(new Color(0.1f, 0.14f, 0.11f, 0.32f));
            BuildWeaponSlots(weaponsColumn);

            BindAllSlots(this);
        }

        private static RectTransform BuildZoneColumn(RectTransform parent, string title, float anchorMinX, float anchorMaxX)
        {
            var column = CreateRect(title + "Column", parent);
            column.anchorMin = new Vector2(anchorMinX, 0f);
            column.anchorMax = new Vector2(anchorMaxX, 1f);
            column.offsetMin = new Vector2(4f, 0f);
            column.offsetMax = new Vector2(-4f, 0f);

            var header = CreateLabel(column, title + "Header", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            header.rectTransform.offsetMin = new Vector2(4f, -22f);
            header.rectTransform.offsetMax = new Vector2(-4f, -2f);
            header.fontSize = 13;
            header.alignment = TextAnchor.MiddleLeft;
            header.text = title;

            var body = CreateRect(title + "Body", column);
            body.anchorMin = new Vector2(0f, 0f);
            body.anchorMax = new Vector2(1f, 1f);
            body.offsetMin = new Vector2(0f, 0f);
            body.offsetMax = new Vector2(0f, -24f);
            return body;
        }

        private void BuildWeaponSlots(RectTransform parent)
        {
            var row = CreateRect("WeaponSlotsRow", parent);
            row.anchorMin = new Vector2(0f, 0f);
            row.anchorMax = new Vector2(1f, 1f);
            row.offsetMin = new Vector2(8f, 8f);
            row.offsetMax = new Vector2(-8f, -8f);

            weaponSlot0View = CreateWeaponSlot(row, new Vector2(0f, 0.52f), new Vector2(1f, 1f), 0);
            weaponSlot1View = CreateWeaponSlot(row, new Vector2(0f, 0f), new Vector2(1f, 0.48f), 1);
        }

        private InventorySlotView CreateAreaSlot(
            RectTransform parent,
            InventorySlotKind kind,
            System.Action<InventorySlotView> configure)
        {
            var slotObject = new GameObject(kind + "Area");
            var view = slotObject.AddComponent<InventorySlotView>();
            view.BuildVisual(parent, Vector2.zero, stretch: true);
            configure(view);
            return view;
        }

        private static void ConfigureFloorArea(InventorySlotView view) => view.ConfigureFloorArea();
        private static void ConfigureInventoryArea(InventorySlotView view) => view.ConfigureInventoryArea();
        private static void ConfigureWeaponArea(InventorySlotView view) => view.ConfigureWeaponArea();

        private InventorySlotView CreateInventoryItemView(int index)
        {
            var slotObject = new GameObject("InventoryItem" + index);
            var view = slotObject.AddComponent<InventorySlotView>();
            view.BuildVisual(inventoryItemsRow as RectTransform, new Vector2(68f, 76f));
            var rect = slotObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            view.ConfigureInventoryItem(string.Empty, draggable: false);
            return view;
        }

        private InventorySlotView CreateWeaponSlot(RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, int slotIndex)
        {
            var slotObject = new GameObject("WeaponSlot" + slotIndex);
            var view = slotObject.AddComponent<InventorySlotView>();
            view.BuildVisual(parent, Vector2.zero, stretch: true);
            var rect = slotObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = new Vector2(0f, 2f);
            rect.offsetMax = new Vector2(0f, -2f);
            view.ConfigureWeaponSlot(slotIndex);
            return view;
        }

        private InventorySlotView CreateFloorItemSlot(int index)
        {
            var slotObject = new GameObject("FloorItem" + index);
            var view = slotObject.AddComponent<InventorySlotView>();
            view.BuildVisual(floorItemsRow as RectTransform, new Vector2(68f, 76f));
            var rect = slotObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            view.ConfigureFloorItem();
            return view;
        }

        private void BindAllSlots(PlayerInventoryPanelController owner)
        {
            floorAreaView.Bind(owner);
            inventoryAreaView.Bind(owner);
            weaponAreaView.Bind(owner);
            weaponSlot0View.Bind(owner);
            weaponSlot1View.Bind(owner);
            for (var i = 0; i < inventoryItemViews.Count; i++)
            {
                inventoryItemViews[i]?.Bind(owner);
            }
        }

        private void SetPanelOpen(bool open)
        {
            wasOpen = open;
            activePanel = open ? this : (activePanel == this ? null : activePanel);
            if (panelRoot != null)
            {
                panelRoot.SetActive(open);
            }

            if (open)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                weaponMount?.ForceExitAds();
                RefreshPanelContents();
            }
            else if (fpsController != null && fpsController.isActiveAndEnabled)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                CancelDrag();
            }

            if (hideLegacyHudInventory)
            {
                GameHudController.SetLegacyInventoryVisible(!open);
            }
        }

        private void RefreshPanelContents()
        {
            SyncDisplayOrderWithInventory();
            RefreshWeaponSlot(weaponSlot0View, 0);
            RefreshWeaponSlot(weaponSlot1View, 1);
            RefreshInventoryItemGrid();
            RefreshFloorItems();
        }

        private void RefreshWeaponSlot(InventorySlotView view, int slotIndex)
        {
            var loadout = weaponLoadoutController != null ? weaponLoadoutController.Loadout : null;
            if (loadout == null || !loadout.IsSlotOccupied(slotIndex))
            {
                view.SetIconPresentation(
                    null,
                    string.Empty,
                    $"Слот {slotIndex + 1}\nПусто",
                    new Color(0.1f, 0.1f, 0.1f, 0.85f),
                    default);
                return;
            }

            var slot = loadout.GetSlot(slotIndex);
            var active = loadout.ActiveSlotIndex == slotIndex && !loadout.IsBothHolstered;
            var mag = loadout.GetSlotMagAmmo(slotIndex);
            var countText = mag >= 0 ? mag.ToString() : string.Empty;
            if (active && mag >= 0)
            {
                countText += "  •";
            }

            var payload = new InventoryDragPayload
            {
                Source = InventoryDragSource.WeaponSlot,
                WeaponSlotIndex = slotIndex,
                Amount = 1,
                PickupKind = PickupKind.Weapon
            };

            view.SetIconPresentation(
                InventoryIconCatalog.GetWeaponIcon(slot.Kind),
                countText,
                FormatWeaponLabel(slot.ItemId, slot.Kind),
                active ? new Color(0.16f, 0.28f, 0.18f, 0.95f) : new Color(0.1f, 0.1f, 0.12f, 0.92f),
                payload);
        }

        private void RefreshInventoryItemGrid()
        {
            var columns = 2;
            const float cellWidth = 68f;
            const float cellHeight = 76f;
            const float gap = 8f;

            while (inventoryItemViews.Count < itemDisplayOrder.Count)
            {
                var slot = CreateInventoryItemView(inventoryItemViews.Count);
                slot.Bind(this);
                inventoryItemViews.Add(slot);
            }

            for (var i = 0; i < inventoryItemViews.Count; i++)
            {
                var view = inventoryItemViews[i];
                if (i >= itemDisplayOrder.Count)
                {
                    view.gameObject.SetActive(false);
                    continue;
                }

                view.gameObject.SetActive(true);
                var rect = view.GetComponent<RectTransform>();
                var col = i % columns;
                var row = i / columns;
                rect.anchoredPosition = new Vector2(col * (cellWidth + gap), -row * (cellHeight + gap));

                var key = itemDisplayOrder[i];
                var count = GetCountForDisplayKey(key);
                ApplyInventoryItemPresentation(view, key, count);
            }
        }

        private void ApplyInventoryItemPresentation(InventorySlotView view, InventoryDisplayKey key, int count)
        {
            switch (key.Kind)
            {
                case InventoryDisplayKind.Medkit:
                {
                    var payload = count > 0
                        ? new InventoryDragPayload
                        {
                            Source = InventoryDragSource.InventoryItem,
                            ItemId = InventoryItemIds.Medkit,
                            Amount = 1,
                            PickupKind = PickupKind.Medkit
                        }
                        : default;
                    view.ConfigureInventoryItem(InventoryItemIds.Medkit, draggable: count > 0);
                    view.SetIconPresentation(
                        null,
                        count > 0 ? count.ToString() : string.Empty,
                        "Аптечка",
                        count > 0 ? ItemSlotBackgroundTint : ItemSlotEmptyBackgroundTint,
                        payload);
                    return;
                }
                case InventoryDisplayKind.Grenade:
                {
                    var payload = count > 0
                        ? new InventoryDragPayload
                        {
                            Source = InventoryDragSource.InventoryItem,
                            ItemId = InventoryItemIds.Grenade,
                            Amount = 1,
                            PickupKind = PickupKind.Grenade
                        }
                        : default;
                    view.ConfigureInventoryItem(InventoryItemIds.Grenade, draggable: count > 0);
                    view.SetIconPresentation(
                        null,
                        count > 0 ? count.ToString() : string.Empty,
                        "Граната",
                        count > 0 ? ItemSlotBackgroundTint : ItemSlotEmptyBackgroundTint,
                        payload);
                    return;
                }
                default:
                {
                    var itemId = AmmoCatalog.GetAmmoItemId(key.AmmoKind);
                    var dropAmount = count > 0
                        ? Mathf.Min(count, AmmoCatalog.GetDefaultPickupAmount(key.AmmoKind))
                        : 0;
                    var payload = count > 0
                        ? new InventoryDragPayload
                        {
                            Source = InventoryDragSource.InventoryItem,
                            ItemId = itemId,
                            Amount = dropAmount,
                            PickupKind = PickupKind.Ammo
                        }
                        : default;
                    view.ConfigureInventoryItem(itemId, draggable: count > 0);
                    view.SetIconPresentation(
                        InventoryIconCatalog.GetAmmoIcon(key.AmmoKind),
                        count > 0 ? count.ToString() : string.Empty,
                        FormatWeaponKindLabel(key.AmmoKind),
                        count > 0 ? ItemSlotBackgroundTint : ItemSlotEmptyBackgroundTint,
                        payload);
                    return;
                }
            }
        }

        private void SyncDisplayOrderWithInventory()
        {
            for (var i = itemDisplayOrder.Count - 1; i >= 0; i--)
            {
                if (GetCountForDisplayKey(itemDisplayOrder[i]) <= 0)
                {
                    itemDisplayOrder.RemoveAt(i);
                }
            }

            EnsureDisplayKeyIfOwned(BuildMedkitKey());
            EnsureDisplayKeyIfOwned(BuildGrenadeKey());

            var loadout = weaponLoadoutController != null ? weaponLoadoutController.Loadout : null;
            if (loadout != null)
            {
                for (var i = 0; i < AmmoKinds.Length; i++)
                {
                    EnsureDisplayKeyIfOwned(BuildAmmoKey(AmmoKinds[i]));
                }
            }
        }

        private void EnsureDisplayKeyIfOwned(InventoryDisplayKey key)
        {
            if (GetCountForDisplayKey(key) <= 0)
            {
                return;
            }

            RegisterDisplayKey(key);
        }

        private void RegisterDisplayOrderFromPickup(PickupKind kind, string itemId)
        {
            RegisterDisplayKey(BuildDisplayKeyFromPickup(kind, itemId));
        }

        private void RegisterDisplayKey(InventoryDisplayKey key)
        {
            for (var i = 0; i < itemDisplayOrder.Count; i++)
            {
                if (DisplayKeysEqual(itemDisplayOrder[i], key))
                {
                    return;
                }
            }

            itemDisplayOrder.Add(key);
        }

        private int GetCountForDisplayKey(InventoryDisplayKey key)
        {
            switch (key.Kind)
            {
                case InventoryDisplayKind.Medkit:
                    return inventory != null ? inventory.GetCount(InventoryItemIds.Medkit) : 0;
                case InventoryDisplayKind.Grenade:
                    return inventory != null ? inventory.GetCount(InventoryItemIds.Grenade) : 0;
                default:
                {
                    var loadout = weaponLoadoutController != null ? weaponLoadoutController.Loadout : null;
                    return loadout != null ? loadout.GetSpareAmmo(key.AmmoKind) : 0;
                }
            }
        }

        private static InventoryDisplayKey BuildDisplayKeyFromPickup(PickupKind kind, string itemId)
        {
            switch (kind)
            {
                case PickupKind.Medkit:
                    return BuildMedkitKey();
                case PickupKind.Grenade:
                    return BuildGrenadeKey();
                case PickupKind.Ammo:
                    if (AmmoCatalog.TryResolveKindFromItemId(itemId, out var ammoKind))
                    {
                        return BuildAmmoKey(ammoKind);
                    }

                    return BuildAmmoKey(WeaponKind.AssaultRifle);
                default:
                    return default;
            }
        }

        private static InventoryDisplayKey BuildMedkitKey()
        {
            return new InventoryDisplayKey
            {
                Kind = InventoryDisplayKind.Medkit,
                ItemId = InventoryItemIds.Medkit
            };
        }

        private static InventoryDisplayKey BuildGrenadeKey()
        {
            return new InventoryDisplayKey
            {
                Kind = InventoryDisplayKind.Grenade,
                ItemId = InventoryItemIds.Grenade
            };
        }

        private static InventoryDisplayKey BuildAmmoKey(WeaponKind kind)
        {
            return new InventoryDisplayKey
            {
                Kind = InventoryDisplayKind.Ammo,
                AmmoKind = kind,
                ItemId = AmmoCatalog.GetAmmoItemId(kind)
            };
        }

        private static bool DisplayKeysEqual(InventoryDisplayKey a, InventoryDisplayKey b)
        {
            if (a.Kind != b.Kind)
            {
                return false;
            }

            return a.Kind != InventoryDisplayKind.Ammo || a.AmmoKind == b.AmmoKind;
        }

        private void RefreshFloorItems()
        {
            nearbyBuffer.Clear();
            pickupController?.CollectNearbyPickups(nearbyBuffer, NearbyRadius);

            while (floorItemViews.Count < nearbyBuffer.Count)
            {
                var slot = CreateFloorItemSlot(floorItemViews.Count);
                slot.Bind(this);
                floorItemViews.Add(slot);
            }

            var columns = 3;
            const float cellWidth = 68f;
            const float cellHeight = 76f;
            const float gap = 8f;

            for (var i = 0; i < floorItemViews.Count; i++)
            {
                var view = floorItemViews[i];
                if (i >= nearbyBuffer.Count)
                {
                    view.gameObject.SetActive(false);
                    continue;
                }

                view.gameObject.SetActive(true);
                var rect = view.GetComponent<RectTransform>();
                var col = i % columns;
                var row = i / columns;
                rect.anchoredPosition = new Vector2(col * (cellWidth + gap), -row * (cellHeight + gap));

                var pickup = nearbyBuffer[i].Pickup;
                var definition = pickup.Definition;
                var icon = InventoryIconCatalog.GetPickupIcon(definition.Kind, definition.ResolvedItemId);
                var countText = definition.Kind == PickupKind.Ammo || definition.Amount > 1
                    ? definition.Amount.ToString()
                    : string.Empty;
                var payload = new InventoryDragPayload
                {
                    Source = InventoryDragSource.FloorItem,
                    SpawnId = pickup.SpawnId,
                    ItemId = definition.ResolvedItemId,
                    Amount = definition.Amount,
                    PickupKind = definition.Kind
                };

                view.SetIconPresentation(
                    icon,
                    countText,
                    FormatPickupTitle(definition),
                    ItemSlotBackgroundTint,
                    payload);
            }
        }

        public void BeginDrag(InventorySlotView source, InventoryDragPayload payload, PointerEventData eventData)
        {
            if (!payload.HasDragData || source == null)
            {
                return;
            }

            dragSourceView = source;
            activeDragPayload = payload;
            isDragging = true;
            source.BeginDragMove(panelCanvas, eventData);
            source.UpdateDragMove(panelCanvas, eventData);
        }

        public void UpdateDrag(PointerEventData eventData)
        {
            if (!isDragging || dragSourceView == null)
            {
                return;
            }

            dragSourceView.UpdateDragMove(panelCanvas, eventData);
        }

        public void EndDrag(PointerEventData eventData, bool droppedOnTarget)
        {
            if (!isDragging)
            {
                return;
            }

            CancelDrag();
        }

        public void HandleDropOnSlot(InventorySlotView target, PointerEventData eventData)
        {
            if (!isDragging || target == null)
            {
                return;
            }

            if (TryApplyDrop(target, activeDragPayload))
            {
                CancelDrag();
                RefreshPanelContents();
            }
        }

        public void HandleRightClick(InventorySlotView source, InventoryDragPayload payload)
        {
            if (!payload.HasDragData || !payload.IsInventoryOwned)
            {
                return;
            }

            if (TryDropPayloadToWorld(payload))
            {
                RefreshPanelContents();
            }
        }

        public void HandleFloorLeftClick(InventoryDragPayload payload)
        {
            if (!payload.IsFloorPickup || string.IsNullOrWhiteSpace(payload.SpawnId))
            {
                return;
            }

            var now = Time.unscaledTime;
            if (string.Equals(payload.SpawnId, lastFloorClickSpawnId, System.StringComparison.Ordinal) &&
                now - lastFloorClickTime <= DoubleClickWindowSeconds)
            {
                pickupController?.RequestPickupBySpawnId(payload.SpawnId);
                RegisterDisplayOrderFromPickup(payload.PickupKind, payload.ItemId);
                lastFloorClickSpawnId = string.Empty;
                RefreshPanelContents();
                return;
            }

            lastFloorClickSpawnId = payload.SpawnId;
            lastFloorClickTime = now;
        }

        private void CancelDrag()
        {
            if (dragSourceView != null)
            {
                dragSourceView.EndDragMove(restoreLayout: true);
                dragSourceView = null;
            }

            isDragging = false;
            activeDragPayload = default;
        }

        private bool TryApplyDrop(InventorySlotView target, InventoryDragPayload payload)
        {
            if (!payload.HasDragData || target == null)
            {
                return false;
            }

            if (IsFloorDropTarget(target.SlotKind) && payload.IsInventoryOwned)
            {
                return TryDropPayloadToWorld(payload);
            }

            if (payload.IsFloorPickup)
            {
                return TryPickupFloorToTarget(target, payload);
            }

            if (payload.Source == InventoryDragSource.WeaponSlot &&
                target.SlotKind == InventorySlotKind.WeaponSlot &&
                target.WeaponSlotIndex >= 0 &&
                target.WeaponSlotIndex != payload.WeaponSlotIndex)
            {
                weaponLoadoutController?.RequestSwapWeaponSlots(
                    payload.WeaponSlotIndex,
                    target.WeaponSlotIndex);
                return true;
            }

            return false;
        }

        private static bool IsFloorDropTarget(InventorySlotKind kind)
        {
            return kind == InventorySlotKind.FloorArea || kind == InventorySlotKind.FloorItem;
        }

        private bool TryPickupFloorToTarget(InventorySlotView target, InventoryDragPayload payload)
        {
            if (pickupController == null || string.IsNullOrWhiteSpace(payload.SpawnId))
            {
                return false;
            }

            switch (payload.PickupKind)
            {
                case PickupKind.Weapon:
                    if (target.SlotKind == InventorySlotKind.WeaponSlot)
                    {
                        pickupController.RequestPickupBySpawnId(payload.SpawnId, target.WeaponSlotIndex);
                        return true;
                    }

                    if (target.SlotKind == InventorySlotKind.WeaponArea)
                    {
                        pickupController.RequestPickupBySpawnId(payload.SpawnId);
                        return true;
                    }

                    return false;

                case PickupKind.Medkit:
                case PickupKind.Grenade:
                case PickupKind.Ammo:
                    if (target.SlotKind == InventorySlotKind.InventoryArea ||
                        target.SlotKind == InventorySlotKind.InventoryItem)
                    {
                        pickupController.RequestPickupBySpawnId(payload.SpawnId);
                        RegisterDisplayOrderFromPickup(payload.PickupKind, payload.ItemId);
                        return true;
                    }

                    return false;

                default:
                    return false;
            }
        }

        private bool TryDropPayloadToWorld(InventoryDragPayload payload)
        {
            switch (payload.Source)
            {
                case InventoryDragSource.WeaponSlot:
                    if (payload.WeaponSlotIndex >= 0 && payload.WeaponSlotIndex <= 1)
                    {
                        weaponLoadoutController?.RequestDropWeaponSlot(payload.WeaponSlotIndex);
                        return true;
                    }

                    return false;

                case InventoryDragSource.InventoryItem:
                    if (!string.IsNullOrWhiteSpace(payload.ItemId))
                    {
                        weaponLoadoutController?.RequestDropInventoryItem(payload.ItemId, Mathf.Max(1, payload.Amount));
                        return true;
                    }

                    return false;

                default:
                    return false;
            }
        }

        private static string FormatPickupTitle(in PickupItemDefinition definition)
        {
            switch (definition.Kind)
            {
                case PickupKind.Weapon:
                    return FormatWeaponLabel(
                        definition.ResolvedItemId,
                        WeaponCatalog.ResolveKindFromItemId(definition.ResolvedItemId));
                case PickupKind.Ammo:
                    if (AmmoCatalog.TryResolveKindFromItemId(definition.ResolvedItemId, out var kind))
                    {
                        return "Патроны: " + FormatWeaponKindLabel(kind);
                    }

                    return "Патроны";
                case PickupKind.Medkit:
                    return "Аптечка";
                case PickupKind.Grenade:
                    return "Граната";
                default:
                    return definition.ResolvedItemId;
            }
        }

        private static string FormatWeaponLabel(string itemId, WeaponKind kind)
        {
            if (!string.IsNullOrWhiteSpace(itemId))
            {
                var trimmed = itemId.Trim();
                if (trimmed.EndsWith("_001", System.StringComparison.OrdinalIgnoreCase))
                {
                    trimmed = trimmed.Substring(0, trimmed.Length - 4);
                }

                trimmed = trimmed.Replace('_', ' ');
                if (trimmed.Length > 0)
                {
                    return char.ToUpper(trimmed[0]) + trimmed.Substring(1);
                }
            }

            return FormatWeaponKindLabel(kind);
        }

        private static string FormatWeaponKindLabel(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.SniperRifle:
                    return "Снайпер";
                case WeaponKind.Pistol:
                    return "Пистолет";
                case WeaponKind.Mp7:
                    return "MP7";
                default:
                    return "Штурм";
            }
        }

        private static bool ReadInventoryTogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Tab);
#endif
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<RectTransform>();
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Text CreateLabel(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot)
        {
            var labelObject = new GameObject(name);
            labelObject.transform.SetParent(parent, false);
            var rect = labelObject.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            var label = labelObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.color = Color.white;
            return label;
        }

        private static void EnsureEventSystemExists()
        {
            if (EventSystem.current != null && EventSystem.current.gameObject.activeInHierarchy)
            {
                return;
            }

            var eventSystemObject = new GameObject("RuntimeEventSystem");
            eventSystemObject.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            eventSystemObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            eventSystemObject.AddComponent<StandaloneInputModule>();
#endif
        }
    }
}
