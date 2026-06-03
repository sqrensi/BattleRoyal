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
        [SerializeField] private float pickupRadius = 2.2f;
        [SerializeField] private float pickupLookDot = 0.1f;

        private PlayerWeaponMount weaponMount;
        private PlayerWeaponController weaponController;
        private PlayerWeaponHolsterController weaponHolster;
        private PlayerHealth health;
        private RealtimeTransportClient transportClient;
        private PickupSpawnManager pickupSpawnManager;
        private Transform pickupOrigin;
        private Camera playerCamera;
        private string pendingSpawnId = string.Empty;
        private PlayerPickupContext pickupContext;

        private void Awake()
        {
            var bootstrap = GetComponent<RemoteThirdPersonPlayerBootstrap>();
            var identity = GetComponent<PlayerNetworkIdentity>();
            if (bootstrap != null || (identity != null && !identity.IsLocalPlayer))
            {
                enabled = false;
                return;
            }

            weaponMount = GetComponent<PlayerWeaponMount>();
            weaponController = GetComponent<PlayerWeaponController>();
            weaponHolster = GetComponent<PlayerWeaponHolsterController>();
            health = GetComponent<PlayerHealth>();
            playerCamera = GetComponentInChildren<Camera>(true);
            pickupOrigin = playerCamera != null ? playerCamera.transform : transform;
            pickupContext = new PlayerPickupContext(weaponMount, weaponController, weaponHolster, health);
        }

        public void Configure(RealtimeTransportClient client, PickupSpawnManager spawnManager)
        {
            transportClient = client;
            pickupSpawnManager = spawnManager;
        }

        private void Start()
        {
            if (pickupSpawnManager == null)
            {
                pickupSpawnManager = FindFirstObjectByType<PickupSpawnManager>();
            }

            RefreshWeaponAvailability();
        }

        private void Update()
        {
            if (!ReadPickupPressed())
            {
                return;
            }

            var pickup = FindBestPickup();
            if (pickup == null)
            {
                return;
            }

            if (!PlayerPickupApplier.CanPickup(pickupContext, pickup.Definition))
            {
                return;
            }

            RequestPickup(pickup);
        }

        public void RefreshWeaponAvailability()
        {
            var hasWeapon = weaponMount != null && weaponMount.HasMountedWeapon;
            if (weaponController != null)
            {
                weaponController.enabled = hasWeapon;
                if (hasWeapon)
                {
                    weaponController.RefreshWeaponAvailability();
                }
            }
        }

        public void HandlePickupRejected(string spawnId)
        {
            if (string.IsNullOrWhiteSpace(spawnId) ||
                !string.Equals(pendingSpawnId, spawnId, System.StringComparison.Ordinal))
            {
                return;
            }

            pendingSpawnId = string.Empty;
        }

        public void ApplyConfirmedPickup(PickupConfirmedInfo confirmed)
        {
            if (string.IsNullOrWhiteSpace(confirmed.SpawnId))
            {
                return;
            }

            pendingSpawnId = string.Empty;
            var definition = ResolveDefinition(confirmed);
            if (!definition.IsValid)
            {
                pickupSpawnManager?.ApplyServerPickupTaken(confirmed.SpawnId);
                return;
            }

            if (!PlayerPickupApplier.TryApply(pickupContext, definition))
            {
                pickupSpawnManager?.ApplyServerPickupTaken(confirmed.SpawnId);
                return;
            }

            pickupSpawnManager?.ApplyServerPickupTaken(confirmed.SpawnId);
            RefreshWeaponAvailability();
        }

        private PickupItemDefinition ResolveDefinition(PickupConfirmedInfo confirmed)
        {
            var visualPrefab = pickupSpawnManager != null
                ? pickupSpawnManager.ResolveDefinitionForSpawnId(confirmed.SpawnId).VisualPrefab
                : null;

            if (visualPrefab == null)
            {
                var pickup = FindPickupBySpawnId(confirmed.SpawnId);
                visualPrefab = pickup != null ? pickup.VisualPrefab : null;
            }

            if (visualPrefab == null)
            {
                return default;
            }

            return confirmed.ToDefinition(visualPrefab);
        }

        private void RequestPickup(WorldPickup pickup)
        {
            if (pickup == null || string.IsNullOrWhiteSpace(pickup.SpawnId))
            {
                return;
            }

            if (transportClient != null && transportClient.IsConnected)
            {
                pendingSpawnId = pickup.SpawnId;
                transportClient.SendPickupRequest(pickup.SpawnId);
                return;
            }

            TryPickupLocalOnly(pickup);
        }

        private bool TryPickupLocalOnly(WorldPickup pickup)
        {
            if (pickup == null || !PlayerPickupApplier.TryApply(pickupContext, pickup.Definition))
            {
                return false;
            }

            pickup.Collect(pickupSpawnManager);
            RefreshWeaponAvailability();
            return true;
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

        private WorldPickup FindBestPickup()
        {
            var origin = pickupOrigin != null ? pickupOrigin.position : transform.position;
            var forward = pickupOrigin != null ? pickupOrigin.forward : transform.forward;
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

                if (!pickup.IsAvailableForPickup(origin, forward, pickupLookDot, out var distanceSqr))
                {
                    continue;
                }

                if (pickupRadius > 0.01f && distanceSqr > pickupRadius * pickupRadius)
                {
                    continue;
                }

                if (!PlayerPickupApplier.CanPickup(pickupContext, pickup.Definition))
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
