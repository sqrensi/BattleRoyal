using ShooterPrototype.Network;
using ShooterPrototype.UI;
using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Slot switching (1/2), holster all (X), drop (G), and weapon mount orchestration.
    /// </summary>
    [DefaultExecutionOrder(540)]
    [DisallowMultipleComponent]
    public sealed class PlayerWeaponLoadoutController : MonoBehaviour
    {
        [SerializeField] private float dropForwardDistance = 1.2f;

        private PlayerWeaponLoadout loadout;
        private PlayerWeaponMount weaponMount;
        private PlayerWeaponController weaponController;
        private PlayerWeaponHolsterController weaponHolster;
        private PlayerHealth playerHealth;
        private PlayerMedkitController medkitController;
        private PlayerInventory inventory;
        private RealtimeTransportClient transportClient;
        private MatchPresenceSync presenceSync;
        private Transform dropOrigin;
        private Coroutine slotSwitchRoutine;

        public PlayerWeaponLoadout Loadout => loadout;

        private void Awake()
        {
            if (GetComponent<RemoteThirdPersonPlayerBootstrap>() != null)
            {
                enabled = false;
                return;
            }

            loadout = GetComponent<PlayerWeaponLoadout>();
            if (loadout == null)
            {
                loadout = gameObject.AddComponent<PlayerWeaponLoadout>();
            }

            weaponMount = GetComponent<PlayerWeaponMount>();
            weaponController = GetComponent<PlayerWeaponController>();
            weaponHolster = GetComponent<PlayerWeaponHolsterController>();
            playerHealth = GetComponent<PlayerHealth>();
            medkitController = GetComponent<PlayerMedkitController>();
            inventory = GetComponent<PlayerInventory>();
            presenceSync = GetComponent<MatchPresenceSync>();
            dropOrigin = transform;
        }

        private void Start()
        {
            if (transportClient == null)
            {
                transportClient = FindFirstObjectByType<RealtimeTransportClient>();
            }

            var camera = GetComponentInChildren<Camera>(true);
            if (camera != null)
            {
                dropOrigin = camera.transform;
            }

            SeedLoadoutFromMountIfNeeded();
        }

        private void SeedLoadoutFromMountIfNeeded()
        {
            if (loadout == null || loadout.HasAnyWeapon || weaponMount == null || !weaponMount.HasMountedWeapon)
            {
                return;
            }

            var kind = weaponController != null
                ? weaponController.CurrentWeaponKind
                : weaponMount.ActiveWeaponProfile != null
                    ? weaponMount.ActiveWeaponProfile.Kind
                    : WeaponKind.AssaultRifle;
            var holstered = weaponHolster != null && weaponHolster.IsHolstered;
            loadout.TrySeedFromMountedWeapon(
                kind,
                WeaponCatalog.GetDefaultItemId(kind),
                holstered);
        }

        private void Update()
        {
            if (CanAcceptWeaponSlotInput())
            {
                if (ReadSlotPressed(0))
                {
                    RequestSelectSlot(0);
                }
                else if (ReadSlotPressed(1))
                {
                    RequestSelectSlot(1);
                }
            }

            if (!CanAcceptInput())
            {
                return;
            }

            if (ReadDropPressed())
            {
                RequestDropActiveWeapon();
            }
        }

        public void RequestHolsterAll()
        {
            if (loadout == null || !loadout.HasAnyWeapon)
            {
                return;
            }

            if (weaponHolster != null && weaponHolster.IsHolstered)
            {
                loadout.SetBothHolstered(false);
                weaponHolster.BeginDrawEquippedWeapon();
                presenceSync?.FlushLocalPose();
                GetComponent<PlayerPickupController>()?.RefreshWeaponAvailability();
                return;
            }

            loadout.SetBothHolstered(true);
            weaponHolster?.BeginHolsterAll();
            presenceSync?.FlushLocalPose();
        }

        public void RequestSelectSlot(int slotIndex)
        {
            if (loadout == null || !loadout.IsSlotOccupied(slotIndex))
            {
                return;
            }

            if (slotSwitchRoutine != null ||
                weaponHolster != null && weaponHolster.IsTransitioning)
            {
                return;
            }

            var sameActiveSlot = loadout.ActiveSlotIndex == slotIndex &&
                                 !loadout.IsBothHolstered &&
                                 weaponHolster != null &&
                                 !weaponHolster.IsHolstered;
            if (sameActiveSlot)
            {
                return;
            }

            if (weaponHolster != null && weaponMount != null && weaponMount.HasMountedWeapon)
            {
                slotSwitchRoutine = StartCoroutine(AnimateSlotSwitchRoutine(slotIndex));
                return;
            }

            loadout.SetActiveSlot(slotIndex);
            EquipActiveSlotWeapon(true);
        }

        private IEnumerator AnimateSlotSwitchRoutine(int slotIndex)
        {
            loadout.SetBothHolstered(true);
            weaponHolster.BeginHolsterAll();
            presenceSync?.FlushLocalPose();

            while (weaponHolster != null && (weaponHolster.IsTransitioning || !weaponHolster.IsHolstered))
            {
                yield return null;
            }

            loadout.SetActiveSlot(slotIndex);
            loadout.SetBothHolstered(true);
            EquipActiveSlotWeapon(false);
            weaponHolster?.PrepareEquippedWeaponForDraw();
            presenceSync?.FlushLocalPose();

            weaponHolster?.BeginDrawEquippedWeapon();
            while (weaponHolster != null && weaponHolster.IsTransitioning)
            {
                yield return null;
            }

            loadout.SetBothHolstered(false);
            presenceSync?.FlushLocalPose();
            GetComponent<PlayerPickupController>()?.RefreshWeaponAvailability();
            slotSwitchRoutine = null;
        }

        public void PublishAmmoStateToServer()
        {
            if (loadout == null || !ShouldUseServerActions() || transportClient == null)
            {
                return;
            }

            transportClient.SendAmmoState(
                loadout.GetSpareAmmo(WeaponKind.AssaultRifle),
                loadout.GetSpareAmmo(WeaponKind.SniperRifle),
                loadout.GetSpareAmmo(WeaponKind.Pistol),
                loadout.GetSpareAmmo(WeaponKind.Mp7),
                loadout.GetSlotMagAmmo(0),
                loadout.GetSlotMagAmmo(1));
        }

        public void RequestDropActiveWeapon()
        {
            if (loadout == null || !loadout.HasAnyWeapon)
            {
                return;
            }

            var slotIndex = ResolveDropSlotIndex();
            RequestDropWeaponSlot(slotIndex);
        }

        public void RequestDropWeaponSlot(int slotIndex)
        {
            if (loadout == null || slotIndex < 0 || slotIndex > 1 || !loadout.IsSlotOccupied(slotIndex))
            {
                return;
            }

            SyncActiveSlotMagAmmoFromController();

            if (ShouldUseServerActions())
            {
                PublishAmmoStateToServer();
                TryResolveDropPosition(out var dropPosition);
                transportClient.SendWeaponDrop(
                    slotIndex,
                    ResolveDropMagAmmo(slotIndex),
                    dropPosition,
                    loadout.GetSpareAmmo(WeaponKind.AssaultRifle),
                    loadout.GetSpareAmmo(WeaponKind.SniperRifle),
                    loadout.GetSpareAmmo(WeaponKind.Pistol),
                    loadout.GetSpareAmmo(WeaponKind.Mp7));
                return;
            }

            ApplyLocalDrop(slotIndex);
        }

        public void RequestSwapWeaponSlots(int slotA, int slotB)
        {
            if (loadout == null || slotA == slotB || slotA < 0 || slotA > 1 || slotB < 0 || slotB > 1)
            {
                return;
            }

            if (!loadout.IsSlotOccupied(slotA) && !loadout.IsSlotOccupied(slotB))
            {
                return;
            }

            SyncActiveSlotMagAmmoFromController();

            if (ShouldUseServerActions())
            {
                PublishAmmoStateToServer();
                transportClient.SendWeaponSwap(slotA, slotB);
                return;
            }

            ApplyLocalSwap(slotA, slotB);
        }

        public void RequestDropInventoryItem(string itemId, int amount = 1)
        {
            if (inventory == null)
            {
                inventory = GetComponent<PlayerInventory>();
            }

            if (string.IsNullOrWhiteSpace(itemId) || amount <= 0)
            {
                return;
            }

            if (AmmoCatalog.TryResolveKindFromItemId(itemId, out var ammoKind))
            {
                RequestDropAmmo(ammoKind, amount);
                return;
            }

            if (inventory == null || inventory.GetCount(itemId) < amount)
            {
                return;
            }

            if (ShouldUseServerActions())
            {
                TryResolveDropPosition(out var dropPosition);
                transportClient.SendInventoryItemDrop(itemId, amount, dropPosition);
                return;
            }

            ApplyLocalInventoryDrop(itemId, amount);
        }

        public void RequestDropAmmo(WeaponKind kind, int amount)
        {
            if (loadout == null || amount <= 0)
            {
                return;
            }

            var spare = loadout.GetSpareAmmo(kind);
            if (spare < amount)
            {
                return;
            }

            var itemId = AmmoCatalog.GetAmmoItemId(kind);
            if (ShouldUseServerActions())
            {
                TryResolveDropPosition(out var dropPosition);
                transportClient.SendInventoryItemDrop(itemId, amount, dropPosition);
                return;
            }

            ApplyLocalAmmoDrop(kind, itemId, amount);
        }

        public void ApplyServerSwap(in WeaponLoadoutServerState serverState)
        {
            if (loadout == null)
            {
                return;
            }

            ApplyServerLoadout(serverState);
            RefreshWeaponPresentationAfterServerChange();
        }

        public void ApplyServerInventoryDrop(
            string itemId,
            int medkitCount,
            int grenadeCount,
            int spareAmmoAssault,
            int spareAmmoSniper,
            int spareAmmoPistol,
            int spareAmmoMp7,
            string droppedSpawnId,
            Vector3 dropPosition)
        {
            if (inventory == null)
            {
                inventory = GetComponent<PlayerInventory>();
            }

            if (inventory != null)
            {
                if (medkitCount >= 0)
                {
                    inventory.SetCount(InventoryItemIds.Medkit, medkitCount);
                }

                if (grenadeCount >= 0)
                {
                    inventory.SetCount(InventoryItemIds.Grenade, grenadeCount);
                }
            }

            if (loadout != null)
            {
                loadout.ApplyServerSpareAmmo(
                    spareAmmoAssault,
                    spareAmmoSniper,
                    spareAmmoPistol,
                    spareAmmoMp7);
                SyncControllerAmmoFromLoadout();
            }

            GetComponent<PlayerPickupController>()?.RefreshWeaponAvailability();
            presenceSync?.FlushLocalPose();
        }

        public void SyncActiveSlotMagAmmoFromController()
        {
            if (loadout == null || weaponController == null || weaponMount == null || !weaponMount.HasMountedWeapon)
            {
                return;
            }

            var slotIndex = ResolveEquippedSlotIndex();
            if (slotIndex >= 0 && slotIndex <= 1 && loadout.IsSlotOccupied(slotIndex))
            {
                loadout.SetSlotMagAmmo(slotIndex, weaponController.CurrentAmmo);
            }
        }

        public void SyncLoadoutSpareAmmoFromController()
        {
            if (loadout == null || weaponController == null)
            {
                return;
            }

            loadout.SetSpareAmmo(ResolveEquippedWeaponKind(), weaponController.ReserveAmmo);
        }

        public void SyncControllerAmmoFromLoadout()
        {
            if (loadout == null || weaponController == null || weaponMount == null || !weaponMount.HasMountedWeapon)
            {
                return;
            }

            var kind = ResolveEquippedWeaponKind();
            if (weaponController.CurrentWeaponKind != kind)
            {
                return;
            }

            var slotIndex = ResolveEquippedSlotIndex();
            var magAmmo = 0;
            if (slotIndex >= 0 && slotIndex <= 1)
            {
                var slotMag = loadout.GetSlotMagAmmo(slotIndex);
                magAmmo = slotMag >= 0 ? slotMag : 0;
            }

            weaponController.SetCurrentAmmo(magAmmo);
            weaponController.SetReserveAmmo(loadout.GetSpareAmmo(kind));
        }

        public WeaponKind ResolveEquippedWeaponKind()
        {
            if (loadout == null)
            {
                return weaponController != null
                    ? weaponController.CurrentWeaponKind
                    : WeaponKind.AssaultRifle;
            }

            var slotIndex = ResolveEquippedSlotIndex();
            if (slotIndex >= 0 && slotIndex <= 1)
            {
                return loadout.GetSlot(slotIndex).Kind;
            }

            if (weaponController != null && weaponMount != null && weaponMount.HasMountedWeapon)
            {
                return weaponController.CurrentWeaponKind;
            }

            return loadout.GetActiveWeaponKind();
        }

        private int ResolveEquippedSlotIndex()
        {
            if (loadout == null)
            {
                return -1;
            }

            var slotIndex = loadout.ActiveSlotIndex;
            if (slotIndex >= 0 && slotIndex <= 1 && loadout.IsSlotOccupied(slotIndex))
            {
                return slotIndex;
            }

            return ResolveDropSlotIndex();
        }

        private WeaponProfile ResolveEquippedWeaponProfile()
        {
            if (weaponMount == null)
            {
                return null;
            }

            if (weaponMount.ActiveWeaponProfile != null)
            {
                return weaponMount.ActiveWeaponProfile;
            }

            var weaponRoot = weaponMount.MountedWeaponRoot;
            return weaponRoot != null ? weaponRoot.GetComponent<WeaponProfile>() : null;
        }

        public void ApplyServerDrop(int slotIndex, in WeaponLoadoutServerState serverState)
        {
            if (loadout == null)
            {
                return;
            }

            ApplyServerLoadout(serverState);
            RefreshWeaponPresentationAfterServerChange();
        }

        public void ApplyServerPickup(in WeaponLoadoutServerState serverState)
        {
            if (loadout == null)
            {
                return;
            }

            if (ShouldAnimateServerWeaponChange(serverState))
            {
                // Apply authoritative slot/ammo data immediately so a later ammo pickup
                // cannot be overwritten when this holster/draw animation finishes.
                ApplyServerLoadout(serverState);

                if (slotSwitchRoutine != null)
                {
                    StopCoroutine(slotSwitchRoutine);
                }

                var shouldDrawAfterSwitch = !serverState.BothHolstered;
                slotSwitchRoutine = StartCoroutine(AnimateServerLoadoutSwitchRoutine(shouldDrawAfterSwitch));
                return;
            }

            ApplyServerLoadout(serverState);
            RefreshWeaponPresentationAfterServerChange();
            EnsureActiveWeaponEquipped(!serverState.BothHolstered);
        }

        /// <summary>
        /// Syncs authoritative ammo/loadout counters from server without mounting or drawing weapons.
        /// </summary>
        public void ApplyServerAmmoPickup(in WeaponLoadoutServerState serverState)
        {
            if (loadout == null)
            {
                return;
            }

            loadout.ApplyServerSpareAmmo(
                serverState.AssaultReserveAmmo,
                serverState.SniperReserveAmmo,
                serverState.PistolReserveAmmo,
                serverState.Mp7ReserveAmmo);

            SyncControllerAmmoFromLoadout();

            GetComponent<PlayerPickupController>()?.RefreshWeaponAvailability();
        }

        public bool TryApplyLocalPickup(
            string itemId,
            WeaponKind kind,
            GameObject equipPrefab,
            int magAmmo = -1,
            int preferredSlot = -1)
        {
            if (loadout == null)
            {
                return false;
            }

            if (equipPrefab == null)
            {
                equipPrefab = WeaponCatalog.GetWeaponPrefab(kind);
            }

            if (equipPrefab == null)
            {
                return false;
            }

            var usePreferredSlot = preferredSlot == 0 || preferredSlot == 1;
            if (usePreferredSlot)
            {
                if (loadout.IsSlotOccupied(preferredSlot))
                {
                    ApplyLocalDrop(preferredSlot, spawnWorldPickup: true);
                }
            }
            else if (loadout.OccupiedCount >= PlayerWeaponLoadout.MaxSlots)
            {
                var dropSlot = ResolveDropSlotIndex();
                if (dropSlot >= 0)
                {
                    ApplyLocalDrop(dropSlot, spawnWorldPickup: true);
                }
            }

            var previousActiveSlot = loadout.ActiveSlotIndex;
            var shouldAnimatePickup = weaponHolster != null &&
                                      weaponMount != null &&
                                      weaponMount.HasMountedWeapon &&
                                      !weaponHolster.IsHolstered &&
                                      !weaponHolster.IsTransitioning;
            int assignedSlot;
            if (usePreferredSlot)
            {
                if (!loadout.TryAddWeaponToSlot(preferredSlot, itemId, kind))
                {
                    return false;
                }

                assignedSlot = preferredSlot;
            }
            else if (!loadout.TryAddWeapon(itemId, kind, out assignedSlot))
            {
                return false;
            }

            var resolvedMagAmmo = magAmmo >= 0 ? magAmmo : 0;
            loadout.SetSlotMagAmmo(assignedSlot, resolvedMagAmmo);

            if (shouldAnimatePickup &&
                previousActiveSlot >= 0 &&
                previousActiveSlot < PlayerWeaponLoadout.MaxSlots &&
                loadout.IsSlotOccupied(previousActiveSlot) &&
                assignedSlot != previousActiveSlot)
            {
                loadout.SetActiveSlot(previousActiveSlot);
                slotSwitchRoutine = StartCoroutine(AnimateSlotSwitchRoutine(assignedSlot));
                GetComponent<PlayerPickupController>()?.RefreshWeaponAvailability();
                return true;
            }

            EquipActiveSlotWeapon(true);
            EnsureActiveWeaponEquipped(true);
            GetComponent<PlayerPickupController>()?.RefreshWeaponAvailability();
            return true;
        }

        public void EnsureActiveWeaponEquipped(bool drawWeapon = true)
        {
            if (loadout == null || !loadout.HasAnyWeapon || weaponMount == null)
            {
                return;
            }

            if (!weaponMount.HasMountedWeapon)
            {
                EquipActiveSlotWeapon(drawWeapon);
            }

            if (!weaponMount.HasMountedWeapon)
            {
                return;
            }

            weaponMount.SetThirdPersonWeaponRenderersEnabled(true);
            if (drawWeapon && weaponHolster != null)
            {
                loadout.SetBothHolstered(false);
                weaponHolster.ForceArmedState();
            }

            GetComponent<PlayerPickupController>()?.RefreshWeaponAvailability();
        }

        private bool ShouldAnimateServerWeaponChange(in WeaponLoadoutServerState serverState)
        {
            if (!serverState.HasWeaponLoadout ||
                serverState.BothHolstered ||
                slotSwitchRoutine != null ||
                weaponHolster == null ||
                weaponMount == null ||
                !weaponMount.HasMountedWeapon ||
                weaponHolster.IsHolstered ||
                weaponHolster.IsTransitioning)
            {
                return false;
            }

            var currentSlot = loadout.ActiveSlotIndex;
            if (currentSlot != 0 && currentSlot != 1)
            {
                return true;
            }

            if (serverState.ActiveWeaponSlot != 0 && serverState.ActiveWeaponSlot != 1)
            {
                return false;
            }

            if (serverState.ActiveWeaponSlot != currentSlot)
            {
                return true;
            }

            var currentKind = loadout.IsSlotOccupied(currentSlot)
                ? WeaponKindUtility.ClampKindByte((int)loadout.GetSlot(currentSlot).Kind)
                : PlayerWeaponLoadout.EmptySlotKind;
            var nextKind = serverState.ActiveWeaponSlot == 0
                ? serverState.Slot0Kind
                : serverState.Slot1Kind;
            return currentKind != nextKind;
        }

        private IEnumerator AnimateServerLoadoutSwitchRoutine(bool shouldDrawAfterSwitch)
        {
            loadout.SetBothHolstered(true);
            weaponHolster.BeginHolsterAll();
            presenceSync?.FlushLocalPose();

            while (weaponHolster != null && (weaponHolster.IsTransitioning || !weaponHolster.IsHolstered))
            {
                yield return null;
            }

            if (!loadout.HasAnyWeapon)
            {
                RefreshWeaponPresentationAfterServerChange();
                slotSwitchRoutine = null;
                yield break;
            }

            loadout.SetBothHolstered(true);
            EquipActiveSlotWeapon(false);
            weaponHolster?.PrepareEquippedWeaponForDraw();
            presenceSync?.FlushLocalPose();

            if (shouldDrawAfterSwitch)
            {
                weaponHolster?.BeginDrawEquippedWeapon();
                while (weaponHolster != null && weaponHolster.IsTransitioning)
                {
                    yield return null;
                }

                loadout.SetBothHolstered(false);
                presenceSync?.FlushLocalPose();
                GetComponent<PlayerPickupController>()?.RefreshWeaponAvailability();
            }

            EnsureActiveWeaponEquipped(shouldDrawAfterSwitch);
            slotSwitchRoutine = null;
        }

        private void ApplyLocalDrop(int slotIndex, bool spawnWorldPickup = true)
        {
            weaponController?.CancelActiveReload();
            var droppedMagAmmo = ResolveDropMagAmmo(slotIndex);
            if (loadout == null || !loadout.TryRemoveSlot(slotIndex, out var removed))
            {
                return;
            }

            if (spawnWorldPickup)
            {
                SpawnDroppedPickup(removed, droppedMagAmmo);
            }

            if (!loadout.HasAnyWeapon)
            {
                weaponMount?.UnequipWeapon();
                if (weaponController != null)
                {
                    weaponController.enabled = false;
                    weaponController.SetCurrentAmmo(0);
                    weaponController.SetReserveAmmo(0);
                }
            }
            else
            {
                RefreshWeaponPresentationAfterServerChange();
            }

            GetComponent<PlayerPickupController>()?.RefreshWeaponAvailability();
            presenceSync?.FlushLocalPose();
        }

        private void SpawnDroppedPickup(PlayerWeaponLoadout.Slot removed, int magAmmo)
        {
            if (!removed.Occupied)
            {
                return;
            }

            var spawnManager = FindFirstObjectByType<PickupSpawnManager>();
            if (spawnManager == null)
            {
                return;
            }

            var sourcePrefab = spawnManager.ResolveWeaponSourcePrefab(removed.Kind);
            if (sourcePrefab == null)
            {
                Debug.LogWarning(
                    $"[WeaponLoadout] No source prefab for dropped {removed.Kind}.");
                return;
            }

            var definition = PickupItemDefinition.Create(
                PickupKind.Weapon,
                sourcePrefab,
                removed.ItemId,
                1).WithMagAmmo(Mathf.Clamp(magAmmo, 0, 999));
            var forward = dropOrigin != null ? dropOrigin.forward : transform.forward;
            var origin = dropOrigin != null ? dropOrigin.position : transform.position;
            Vector3 position;
            if (spawnManager.TryResolveWeaponDropPose(origin, forward, dropForwardDistance, out position, out forward))
            {
                // Ground + wall resolved by spawn manager.
            }
            else
            {
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.001f)
                {
                    forward = transform.forward;
                }

                forward.Normalize();
                position = origin + forward * dropForwardDistance;
            }

            var spawnId = $"local_drop_{System.Guid.NewGuid():N}";
            spawnManager.SpawnDynamicPickupAtWorld(spawnId, position, forward, definition);
        }

        private bool TryResolveDropPosition(out Vector3 dropPosition)
        {
            dropPosition = Vector3.zero;
            var forward = dropOrigin != null ? dropOrigin.forward : transform.forward;
            var origin = dropOrigin != null ? dropOrigin.position : transform.position;
            var spawnManager = FindFirstObjectByType<PickupSpawnManager>();
            if (spawnManager == null)
            {
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.001f)
                {
                    forward = transform.forward;
                }

                forward.Normalize();
                dropPosition = origin + forward * dropForwardDistance;
                return true;
            }

            return spawnManager.TryResolveWeaponDropPose(
                origin,
                forward,
                dropForwardDistance,
                out dropPosition,
                out _);
        }

        private void EquipActiveSlotWeapon(bool drawIfHolstered)
        {
            if (loadout == null || weaponMount == null)
            {
                return;
            }

            var equipSlotIndex = loadout.ActiveSlotIndex;
            if (equipSlotIndex < 0 || equipSlotIndex > 1)
            {
                equipSlotIndex = loadout.IsSlotOccupied(0) ? 0 : 1;
            }

            var slot = loadout.GetSlot(equipSlotIndex);
            var prefab = slot.Occupied ? WeaponCatalog.GetWeaponPrefab(slot.Kind) : null;
            if (prefab == null)
            {
                weaponMount.UnequipWeapon();
                Debug.LogWarning(
                    $"[WeaponLoadout] No equip prefab for slot {equipSlotIndex} " +
                    $"(kind={slot.Kind}).");
                return;
            }

            if (!weaponMount.ReplaceEquippedWeapon(prefab) && !weaponMount.EquipWeapon(prefab))
            {
                Debug.LogWarning($"[WeaponLoadout] Failed to mount equip prefab '{prefab.name}'.");
                return;
            }

            weaponMount.RefreshEquippedWeaponSkin();
            weaponMount.SetThirdPersonWeaponRenderersEnabled(true);

            weaponHolster?.SyncArmedPoseFromMountedWeapon();
            GetComponent<SyntyWeaponHandBinder>()?.RefreshWeaponHandBindings();

            if (weaponController != null)
            {
                weaponController.enabled = true;
                var profile = ResolveEquippedWeaponProfile();
                if (profile != null)
                {
                    weaponController.ApplyWeaponProfile(profile, resetAmmo: false);
                }

                SyncControllerAmmoFromLoadout();
                loadout.SetSlotMagAmmo(equipSlotIndex, weaponController.CurrentAmmo);
            }

            if (drawIfHolstered && weaponHolster != null &&
                (loadout.IsBothHolstered || weaponHolster.IsHolstered))
            {
                loadout.SetBothHolstered(false);
                weaponHolster.ForceArmedState();
            }

            GetComponent<PlayerPickupController>()?.RefreshWeaponAvailability();
            presenceSync?.FlushLocalPose();
        }

        private void RefreshWeaponPresentationAfterServerChange()
        {
            if (loadout == null || !loadout.HasAnyWeapon)
            {
                weaponController?.CancelActiveReload();
                weaponMount?.UnequipWeapon();
                if (weaponController != null)
                {
                    weaponController.enabled = false;
                }

                weaponHolster?.ForceHolsteredIdleState();
                return;
            }

            if (loadout.IsBothHolstered)
            {
                EquipActiveSlotWeapon(false);
                weaponHolster?.BeginHolsterAllImmediate();
                return;
            }

            EquipActiveSlotWeapon(true);
        }

        private void ApplyServerLoadout(in WeaponLoadoutServerState serverState)
        {
            if (!serverState.HasWeaponLoadout || loadout == null)
            {
                return;
            }

            loadout.ApplyServerLoadout(
                serverState.Slot0Kind,
                serverState.Slot1Kind,
                serverState.Slot0ItemId,
                serverState.Slot1ItemId,
                serverState.ActiveWeaponSlot,
                serverState.BothHolstered,
                serverState.ActiveMagAmmo,
                serverState.ActiveReserveAmmo,
                serverState.AssaultReserveAmmo,
                serverState.SniperReserveAmmo,
                serverState.PistolReserveAmmo,
                serverState.Mp7ReserveAmmo);
        }

        private int ResolveDropMagAmmo(int slotIndex)
        {
            if (loadout == null || slotIndex < 0 || slotIndex > 1 || !loadout.IsSlotOccupied(slotIndex))
            {
                return -1;
            }

            if (weaponController != null &&
                (loadout.OccupiedCount <= 1 || IsDropSlotCurrentlyWielded(slotIndex)))
            {
                loadout.SetSlotMagAmmo(slotIndex, weaponController.CurrentAmmo);
                return weaponController.CurrentAmmo;
            }

            SyncActiveSlotMagAmmoFromController();
            var slotMag = loadout.GetSlotMagAmmo(slotIndex);
            return slotMag >= 0 ? slotMag : -1;
        }

        private bool IsDropSlotCurrentlyWielded(int slotIndex)
        {
            if (loadout.ActiveSlotIndex == slotIndex)
            {
                return true;
            }

            if (loadout.ActiveSlotIndex != PlayerWeaponLoadout.NoActiveSlot)
            {
                return false;
            }

            return ResolveDropSlotIndex() == slotIndex;
        }

        private int ResolveDropSlotIndex()
        {
            if (loadout == null)
            {
                return -1;
            }

            if (loadout.ActiveSlotIndex == 0 || loadout.ActiveSlotIndex == 1)
            {
                return loadout.ActiveSlotIndex;
            }

            return loadout.IsSlotOccupied(0) ? 0 : 1;
        }

        private bool CanAcceptWeaponSlotInput()
        {
            if (slotSwitchRoutine != null)
            {
                return false;
            }

            if (playerHealth != null && playerHealth.IsDead)
            {
                return false;
            }

            if (medkitController != null && medkitController.IsUsingMedkit)
            {
                return false;
            }

            var fps = GetComponent<FpsCharacterController>();
            return fps == null || fps.enabled;
        }

        private bool CanAcceptInput()
        {
            if (PlayerInventoryPanelController.IsOpen || GameHudController.IsPauseMenuOpen)
            {
                return false;
            }

            if (slotSwitchRoutine != null)
            {
                return false;
            }

            if (playerHealth != null && playerHealth.IsDead)
            {
                return false;
            }

            if (medkitController != null && medkitController.IsUsingMedkit)
            {
                return false;
            }

            var fps = GetComponent<FpsCharacterController>();
            return fps == null || fps.enabled;
        }

        private void ApplyLocalSwap(int slotA, int slotB)
        {
            if (loadout == null || !loadout.TrySwapSlots(slotA, slotB))
            {
                return;
            }

            RefreshWeaponPresentationAfterServerChange();
            GetComponent<PlayerPickupController>()?.RefreshWeaponAvailability();
            presenceSync?.FlushLocalPose();
        }

        private void ApplyLocalInventoryDrop(string itemId, int amount)
        {
            if (inventory == null || !inventory.TryRemove(itemId, amount))
            {
                return;
            }

            var spawnId = $"local_drop_{System.Guid.NewGuid():N}";
            TryResolveDropPosition(out var dropPosition);
            TrySpawnDroppedItemPickup(spawnId, itemId, dropPosition, amount);
            GetComponent<PlayerPickupController>()?.RefreshWeaponAvailability();
            presenceSync?.FlushLocalPose();
        }

        private void ApplyLocalAmmoDrop(WeaponKind kind, string itemId, int amount)
        {
            if (loadout == null)
            {
                return;
            }

            var spare = loadout.GetSpareAmmo(kind);
            if (spare < amount)
            {
                return;
            }

            loadout.SetSpareAmmo(kind, spare - amount);
            SyncControllerAmmoFromLoadout();

            var spawnId = $"local_drop_{System.Guid.NewGuid():N}";
            TryResolveDropPosition(out var dropPosition);
            TrySpawnDroppedItemPickup(spawnId, itemId, dropPosition, amount);
            GetComponent<PlayerPickupController>()?.RefreshWeaponAvailability();
            presenceSync?.FlushLocalPose();
        }

        private void TrySpawnDroppedItemPickup(
            string spawnId,
            string itemId,
            Vector3 dropPosition,
            int amount = 1)
        {
            var spawnManager = FindFirstObjectByType<PickupSpawnManager>();
            if (spawnManager == null || string.IsNullOrWhiteSpace(spawnId))
            {
                return;
            }

            var definition = spawnManager.ResolveDefinitionFromProtocol(
                ResolvePickupKind(itemId),
                itemId,
                Mathf.Max(1, amount));
            if (!definition.IsValid)
            {
                return;
            }

            spawnManager.SpawnDynamicPickupAtWorld(spawnId, dropPosition, transform.forward, definition);
        }

        private static PickupKind ResolvePickupKind(string itemId)
        {
            if (itemId == InventoryItemIds.Medkit)
            {
                return PickupKind.Medkit;
            }

            if (itemId == InventoryItemIds.Grenade)
            {
                return PickupKind.Grenade;
            }

            if (AmmoCatalog.TryResolveKindFromItemId(itemId, out _))
            {
                return PickupKind.Ammo;
            }

            return PickupKind.Weapon;
        }

        private bool ShouldUseServerActions()
        {
            return transportClient != null && transportClient.IsReady;
        }

        private static bool ReadSlotPressed(int slotIndex)
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null)
            {
                return false;
            }

            return slotIndex switch
            {
                0 => Keyboard.current.digit1Key.wasPressedThisFrame,
                1 => Keyboard.current.digit2Key.wasPressedThisFrame,
                _ => false
            };
#else
            return slotIndex switch
            {
                0 => Input.GetKeyDown(KeyCode.Alpha1),
                1 => Input.GetKeyDown(KeyCode.Alpha2),
                _ => false
            };
#endif
        }

        private static bool ReadDropPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.G);
#endif
        }
    }
}
