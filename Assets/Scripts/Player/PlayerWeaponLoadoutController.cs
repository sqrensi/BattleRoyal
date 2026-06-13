using ShooterPrototype.Network;
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
            if (!CanAcceptInput())
            {
                return;
            }

            if (ReadSlotPressed(0))
            {
                RequestSelectSlot(0);
            }
            else if (ReadSlotPressed(1))
            {
                RequestSelectSlot(1);
            }
            else if (ReadDropPressed())
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

        public void RequestDropActiveWeapon()
        {
            if (loadout == null || !loadout.HasAnyWeapon)
            {
                return;
            }

            var slotIndex = ResolveDropSlotIndex();
            if (slotIndex < 0 || !loadout.IsSlotOccupied(slotIndex))
            {
                return;
            }

            SyncActiveSlotMagAmmoFromController();

            if (ShouldUseServerActions())
            {
                TryResolveDropPosition(out var dropPosition);
                transportClient.SendWeaponDrop(slotIndex, ResolveDropMagAmmo(slotIndex), dropPosition);
                return;
            }

            ApplyLocalDrop(slotIndex);
        }

        public void SyncActiveSlotMagAmmoFromController()
        {
            if (loadout == null || weaponController == null || weaponMount == null || !weaponMount.HasMountedWeapon)
            {
                return;
            }

            var slotIndex = loadout.ActiveSlotIndex;
            if (slotIndex < 0 || slotIndex > 1 || !loadout.IsSlotOccupied(slotIndex))
            {
                slotIndex = ResolveDropSlotIndex();
            }

            if (slotIndex >= 0 && slotIndex <= 1 && loadout.IsSlotOccupied(slotIndex))
            {
                loadout.SetSlotMagAmmo(slotIndex, weaponController.CurrentAmmo);
                loadout.SetSpareAmmo(weaponController.ReserveAmmo);
            }
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
                if (slotSwitchRoutine != null)
                {
                    StopCoroutine(slotSwitchRoutine);
                }

                slotSwitchRoutine = StartCoroutine(AnimateServerLoadoutSwitchRoutine(serverState));
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
            if (loadout == null || !serverState.HasWeaponLoadout)
            {
                return;
            }

            ApplyServerLoadout(serverState);

            if (weaponController != null)
            {
                weaponController.SetReserveAmmo(loadout.SpareAmmo);
            }

            GetComponent<PlayerPickupController>()?.RefreshWeaponAvailability();
        }

        public bool TryApplyLocalPickup(string itemId, WeaponKind kind, GameObject equipPrefab, int magAmmo = -1)
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

            if (loadout.OccupiedCount >= PlayerWeaponLoadout.MaxSlots)
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
            if (!loadout.TryAddWeapon(itemId, kind, out var assignedSlot))
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

        private IEnumerator AnimateServerLoadoutSwitchRoutine(WeaponLoadoutServerState serverState)
        {
            loadout.SetBothHolstered(true);
            weaponHolster.BeginHolsterAll();
            presenceSync?.FlushLocalPose();

            while (weaponHolster != null && (weaponHolster.IsTransitioning || !weaponHolster.IsHolstered))
            {
                yield return null;
            }

            ApplyServerLoadout(serverState);
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

            if (!serverState.BothHolstered)
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

            EnsureActiveWeaponEquipped(!serverState.BothHolstered);
            slotSwitchRoutine = null;
        }

        private void ApplyLocalDrop(int slotIndex, bool spawnWorldPickup = true)
        {
            weaponController?.CancelActiveReload();
            DrainWeaponMagIntoSpare(slotIndex);
            if (loadout == null || !loadout.TryRemoveSlot(slotIndex, out var removed))
            {
                return;
            }

            if (spawnWorldPickup)
            {
                SpawnDroppedPickup(removed);
            }

            if (!loadout.HasAnyWeapon)
            {
                weaponMount?.UnequipWeapon();
                if (weaponController != null)
                {
                    weaponController.enabled = false;
                    weaponController.SetCurrentAmmo(0);
                    weaponController.SetReserveAmmo(loadout.SpareAmmo);
                }
            }
            else
            {
                RefreshWeaponPresentationAfterServerChange();
                if (weaponController != null)
                {
                    weaponController.SetReserveAmmo(loadout.SpareAmmo);
                }
            }

            GetComponent<PlayerPickupController>()?.RefreshWeaponAvailability();
            presenceSync?.FlushLocalPose();
        }

        private void SpawnDroppedPickup(PlayerWeaponLoadout.Slot removed)
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
                1).WithMagAmmo(0);
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

            weaponMount.SetThirdPersonWeaponRenderersEnabled(true);

            if (weaponController != null)
            {
                weaponController.enabled = true;
                var magAmmo = loadout.GetSlotMagAmmo(equipSlotIndex);
                if (magAmmo < 0)
                {
                    magAmmo = 0;
                }

                weaponController.ApplyWeaponProfile(weaponMount.ActiveWeaponProfile, resetAmmo: false);
                weaponController.SetCurrentAmmo(magAmmo);
                weaponController.SetReserveAmmo(loadout.SpareAmmo);
                loadout.SetSlotMagAmmo(equipSlotIndex, magAmmo);
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
                serverState.ActiveReserveAmmo);
        }

        private int DrainWeaponMagIntoSpare(int slotIndex)
        {
            if (loadout == null || slotIndex < 0 || slotIndex > 1 || !loadout.IsSlotOccupied(slotIndex))
            {
                return 0;
            }

            var magAmmo = ResolveDropMagAmmo(slotIndex);
            if (magAmmo > 0)
            {
                loadout.AddSpareAmmo(magAmmo);
            }

            loadout.SetSlotMagAmmo(slotIndex, 0);
            if (weaponController != null && IsDropSlotCurrentlyWielded(slotIndex))
            {
                weaponController.SetCurrentAmmo(0);
                weaponController.SetReserveAmmo(loadout.SpareAmmo);
            }

            return magAmmo;
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

        private bool CanAcceptInput()
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
