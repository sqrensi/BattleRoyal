using ShooterPrototype.Network;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ShooterPrototype.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerPickupController : MonoBehaviour
    {
        [SerializeField] private float pickupRadius = 3f;
        [SerializeField] private float pickupLookDot = 0.7f;
        [SerializeField] private float pickupSampleHeight = 0.35f;

        private PlayerWeaponMount weaponMount;
        private PlayerWeaponController weaponController;
        private PlayerWeaponHolsterController weaponHolster;
        private PlayerWeaponLoadoutController weaponLoadout;
        private PlayerHealth health;
        private PlayerInventory inventory;
        private PlayerMedkitController medkitController;
        private RealtimeTransportClient transportClient;
        private PickupSpawnManager pickupSpawnManager;
        private Transform pickupOrigin;
        private Camera playerCamera;
        private string pendingSpawnId = string.Empty;

        private void Awake()
        {
            if (GetComponent<RemoteThirdPersonPlayerBootstrap>() != null)
            {
                enabled = false;
                return;
            }

            CacheComponents();
            playerCamera = GetComponentInChildren<Camera>(true);
            pickupOrigin = playerCamera != null ? playerCamera.transform : transform;
        }

        public void Configure(RealtimeTransportClient client, PickupSpawnManager spawnManager)
        {
            transportClient = client;
            pickupSpawnManager = spawnManager;
        }

        public void RefreshPickupContext()
        {
            CacheComponents();
        }

        private void CacheComponents()
        {
            weaponMount = GetComponent<PlayerWeaponMount>();
            weaponController = GetComponent<PlayerWeaponController>();
            weaponHolster = GetComponent<PlayerWeaponHolsterController>();
            weaponLoadout = GetComponent<PlayerWeaponLoadoutController>();
            health = GetComponent<PlayerHealth>();
            inventory = GetComponent<PlayerInventory>();
            medkitController = GetComponent<PlayerMedkitController>();
        }

        private PlayerPickupContext BuildPickupContext()
        {
            if (inventory == null)
            {
                inventory = GetComponent<PlayerInventory>();
            }

            if (medkitController == null)
            {
                medkitController = GetComponent<PlayerMedkitController>();
            }

            return new PlayerPickupContext(
                weaponMount,
                weaponController,
                weaponHolster,
                weaponLoadout,
                health,
                inventory,
                medkitController);
        }

        private void Start()
        {
            if (pickupSpawnManager == null)
            {
                pickupSpawnManager = FindFirstObjectByType<PickupSpawnManager>();
            }

            if (transportClient == null)
            {
                transportClient = FindFirstObjectByType<RealtimeTransportClient>();
            }

            if (pickupSpawnManager != null && transportClient != null)
            {
                Configure(transportClient, pickupSpawnManager);
            }

            RefreshPickupContext();
            RefreshWeaponAvailability();
        }

        private void Update()
        {
            if (medkitController != null && medkitController.IsUsingMedkit)
            {
                return;
            }

            if (!ReadPickupPressed())
            {
                return;
            }

            TryPickupBestNearby();
        }

        private void TryPickupBestNearby()
        {
            var pickup = FindBestPickup();
            if (pickup == null)
            {
                return;
            }

            var context = BuildPickupContext();
            if (!PlayerPickupApplier.CanPickup(context, pickup.Definition))
            {
                return;
            }

            RequestPickup(pickup);
        }

        public void RefreshWeaponAvailability()
        {
            var loadoutController = GetComponent<PlayerWeaponLoadoutController>();
            var hasInventoryWeapon = loadoutController != null &&
                                     loadoutController.Loadout != null &&
                                     loadoutController.Loadout.HasAnyWeapon;
            var hasWeapon = hasInventoryWeapon || (weaponMount != null && weaponMount.HasMountedWeapon);
            if (weaponController != null)
            {
                var holstered = weaponHolster != null && weaponHolster.IsHolstered;
                weaponController.enabled = hasWeapon && weaponMount != null &&
                                           weaponMount.HasMountedWeapon && !holstered;
                if (weaponController.enabled)
                {
                    weaponController.RefreshWeaponAvailability();
                }
            }

            RefreshFirstPersonArmsVisibility();
        }

        private void RefreshFirstPersonArmsVisibility()
        {
            if (weaponHolster != null)
            {
                weaponHolster.SyncFirstPersonArmsPresentation();
                return;
            }

            var armsPresenter = GetComponent<SyntyFirstPersonArmsPresenter>();
            if (armsPresenter == null)
            {
                return;
            }

            var showArms = weaponMount != null && weaponMount.HasMountedWeapon;
            if (showArms)
            {
                var viewPresentation = GetComponent<PlayerViewPresentation>();
                var isLocal = viewPresentation == null || viewPresentation.IsLocalPlayerView;
                armsPresenter.ApplyFirstPersonVisibility(isLocal);
                GetComponent<SyntyWeaponHandBinder>()?.SetHandIkEnabled(true);
            }
            else
            {
                armsPresenter.SetHolsteredArmsPresentation(true);
                GetComponent<SyntyWeaponHandBinder>()?.SetHandIkEnabled(false);
            }

            GetComponent<SyntySplitBodyPresentation>()?.SetHolsteredFirstPersonPresentation(!showArms);
        }

        public void HandlePickupRejected(string spawnId, string reason)
        {
            if (string.IsNullOrWhiteSpace(spawnId) ||
                !string.Equals(pendingSpawnId, spawnId, System.StringComparison.Ordinal))
            {
                return;
            }

            pendingSpawnId = string.Empty;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                Debug.LogWarning($"[PlayerPickup] Server rejected pickup: {reason} spawn={spawnId}");
            }
        }

        public void ApplyConfirmedPickup(PickupConfirmedInfo confirmed, PickupApplyServerState serverState)
        {
            if (string.IsNullOrWhiteSpace(confirmed.SpawnId))
            {
                return;
            }

            pendingSpawnId = string.Empty;
            var definition = ResolveDefinition(confirmed, serverState);
            if (!definition.IsValid)
            {
                pickupSpawnManager?.ApplyServerPickupTaken(confirmed.SpawnId);
                return;
            }

            var result = PlayerPickupApplier.TryApply(BuildPickupContext(), definition, serverState);
            if (!result.Success)
            {
                Debug.LogWarning(
                    $"[PlayerPickup] Apply failed: {result.FailureReason} kind={definition.Kind} spawn={confirmed.SpawnId}");
                pickupSpawnManager?.ApplyServerPickupTaken(confirmed.SpawnId);
                return;
            }

            pickupSpawnManager?.ApplyServerPickupTaken(confirmed.SpawnId);
            RefreshWeaponAvailability();
        }

        private PickupItemDefinition ResolveDefinition(
            PickupConfirmedInfo confirmed,
            PickupApplyServerState serverState)
        {
            if (pickupSpawnManager != null)
            {
                var magAmmo = -1;
                if (confirmed.Kind == PickupKind.Weapon &&
                    serverState.WeaponLoadout.HasWeaponLoadout)
                {
                    magAmmo = serverState.WeaponLoadout.ActiveMagAmmo;
                }

                var definition = pickupSpawnManager.ResolveDefinitionFromProtocol(
                    confirmed.Kind,
                    confirmed.ItemId,
                    confirmed.Amount,
                    magAmmo);
                if (definition.IsValid)
                {
                    return definition;
                }
            }

            var pickup = FindPickupBySpawnId(confirmed.SpawnId);
            if (pickup != null && pickup.Definition.IsValid)
            {
                return pickup.Definition;
            }

            return default;
        }

        private void RequestPickup(WorldPickup pickup)
        {
            if (pickup == null || string.IsNullOrWhiteSpace(pickup.SpawnId))
            {
                return;
            }

            if (ShouldUseServerPickup())
            {
                pendingSpawnId = pickup.SpawnId;
                transportClient.SendPickupRequest(pickup.SpawnId);
                return;
            }

            CompleteLocalPickup(pickup, PickupApplyServerState.None);
        }

        private bool ShouldUseServerPickup()
        {
            return transportClient != null && transportClient.IsReady;
        }

        private void CompleteLocalPickup(WorldPickup pickup, PickupApplyServerState serverState)
        {
            if (pickup == null)
            {
                return;
            }

            var definition = pickup.Definition;
            var result = PlayerPickupApplier.TryApply(BuildPickupContext(), definition, serverState);
            if (!result.Success)
            {
                Debug.LogWarning($"[PlayerPickup] Local apply failed: {result.FailureReason} kind={definition.Kind}");
                return;
            }

            pickup.Collect(pickupSpawnManager);
            RefreshWeaponAvailability();
        }

        private WorldPickup FindPickupBySpawnId(string spawnId)
        {
            var pickups = WorldPickup.Active;
            for (var i = 0; i < pickups.Count; i++)
            {
                var pickup = pickups[i];
                if (pickup != null &&
                    string.Equals(pickup.SpawnId, spawnId, System.StringComparison.Ordinal))
                {
                    return pickup;
                }
            }

            return null;
        }

        private Vector3 GetPickupSamplePosition()
        {
            return transform.position + Vector3.up * pickupSampleHeight;
        }

        private Vector3 GetPickupLookForward()
        {
            return pickupOrigin != null ? pickupOrigin.forward : transform.forward;
        }

        private Vector3 GetPickupLookOrigin()
        {
            return pickupOrigin != null ? pickupOrigin.position : GetPickupSamplePosition();
        }

        private WorldPickup FindBestPickup()
        {
            var playerPosition = GetPickupSamplePosition();
            var lookOrigin = GetPickupLookOrigin();
            var forward = GetPickupLookForward();
            var context = BuildPickupContext();
            WorldPickup best = null;
            var bestDistanceSqr = float.MaxValue;

            var pickups = WorldPickup.Active;
            for (var i = 0; i < pickups.Count; i++)
            {
                var pickup = pickups[i];
                if (pickup == null || !pickup.isActiveAndEnabled)
                {
                    continue;
                }

                if (!pickup.IsAvailableForPickup(
                        playerPosition,
                        lookOrigin,
                        forward,
                        pickupRadius,
                        pickupLookDot,
                        out var distanceSqr))
                {
                    continue;
                }

                if (!PlayerPickupApplier.CanPickup(context, pickup.Definition))
                {
                    continue;
                }

                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    best = pickup;
                }
            }

            return best;
        }

        private static bool ReadPickupPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.F);
#endif
        }
    }
}
