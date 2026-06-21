using System;
using System.Collections;
using ShooterPrototype.Network;
using UnityEngine;

namespace ShooterPrototype.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PickupSpawnManager))]
    public sealed class MatchPickupSync : MonoBehaviour
    {
        private PickupSpawnManager pickupSpawnManager;
        private RealtimeTransportClient transportClient;
        private NetworkLauncher networkLauncher;
        private MatchPresenceSync localPresenceSync;
        private PlayerPickupController localPickupController;
        private PlayerWeaponLoadoutController localLoadoutController;
        private string localTicketId = string.Empty;
        private bool eventsSubscribed;
        private Coroutine networkRoutine;

        private void Awake()
        {
            pickupSpawnManager = GetComponent<PickupSpawnManager>();
        }

        private void OnEnable()
        {
            if (networkRoutine == null)
            {
                networkRoutine = StartCoroutine(NetworkRoutine());
            }
        }

        private void OnDisable()
        {
            if (networkRoutine != null)
            {
                StopCoroutine(networkRoutine);
                networkRoutine = null;
            }

            UnsubscribeFromTransport();
        }

        private IEnumerator NetworkRoutine()
        {
            var wait = new WaitForSecondsRealtime(0.25f);
            while (true)
            {
                EnsureReferences();

                if (transportClient != null)
                {
                    SubscribeToTransport();

                    if (!transportClient.IsReady)
                    {
                        pickupSpawnManager?.ResetServerRegistration();
                    }
                    else
                    {
                        pickupSpawnManager?.TryRegisterWithServer(transportClient);
                    }
                }

                TryConfigureLocalPickupController();
                yield return wait;
            }
        }

        private void EnsureReferences()
        {
            if (networkLauncher == null)
            {
                networkLauncher = FindFirstObjectByType<NetworkLauncher>();
            }

            if (transportClient == null)
            {
                transportClient = FindFirstObjectByType<RealtimeTransportClient>();
            }

            if (networkLauncher != null &&
                !string.IsNullOrWhiteSpace(networkLauncher.CurrentTicketId))
            {
                localTicketId = networkLauncher.CurrentTicketId.Trim();
            }
        }

        private void TryConfigureLocalPickupController()
        {
            if (localPickupController != null || transportClient == null || pickupSpawnManager == null)
            {
                return;
            }

            var localMarker = FindFirstObjectByType<LocalPlayerMarker>();
            if (localMarker == null)
            {
                return;
            }

            localPickupController = localMarker.GetComponent<PlayerPickupController>();
            localLoadoutController = localMarker.GetComponent<PlayerWeaponLoadoutController>();
            localPresenceSync = localMarker.GetComponent<MatchPresenceSync>();
            if (localPickupController == null)
            {
                return;
            }

            localPickupController.Configure(transportClient, pickupSpawnManager);
            localPickupController.RefreshPickupContext();
        }

        private void SubscribeToTransport()
        {
            if (eventsSubscribed || transportClient == null)
            {
                return;
            }

            transportClient.PickupStateReceived += HandlePickupState;
            transportClient.PickupEventReceived += HandlePickupEvent;
            transportClient.PickupResultReceived += HandlePickupResult;
            transportClient.WeaponDropResultReceived += HandleWeaponDropResult;
            transportClient.WeaponSwapResultReceived += HandleWeaponSwapResult;
            transportClient.InventoryItemDropResultReceived += HandleInventoryItemDropResult;
            eventsSubscribed = true;
        }

        private void UnsubscribeFromTransport()
        {
            if (!eventsSubscribed || transportClient == null)
            {
                eventsSubscribed = false;
                return;
            }

            transportClient.PickupStateReceived -= HandlePickupState;
            transportClient.PickupEventReceived -= HandlePickupEvent;
            transportClient.PickupResultReceived -= HandlePickupResult;
            transportClient.WeaponDropResultReceived -= HandleWeaponDropResult;
            transportClient.WeaponSwapResultReceived -= HandleWeaponSwapResult;
            transportClient.InventoryItemDropResultReceived -= HandleInventoryItemDropResult;
            eventsSubscribed = false;
        }

        private void HandlePickupState(RealtimeTransportClient.PickupStateMessage message)
        {
            if (message?.spawns == null || pickupSpawnManager == null)
            {
                return;
            }

            pickupSpawnManager.ApplyServerPickupState(message.spawns);
        }

        private void HandlePickupEvent(RealtimeTransportClient.PickupEventMessage message)
        {
            if (message == null || pickupSpawnManager == null ||
                string.IsNullOrWhiteSpace(message.spawnId))
            {
                return;
            }

            if (message.available)
            {
                if (TryReadEventPosition(message, out var eventPosition))
                {
                    pickupSpawnManager.ApplyServerSpawnPosition(message.spawnId, eventPosition);
                }

                TrySpawnDynamicPickupFromEvent(message);
                pickupSpawnManager.ApplyServerPickupRespawn(message.spawnId, true);
                return;
            }

            pickupSpawnManager.ApplyServerPickupTaken(message.spawnId);
        }

        private void HandlePickupResult(RealtimeTransportClient.PickupResultMessage message)
        {
            if (message == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(localTicketId) &&
                !string.IsNullOrWhiteSpace(message.ticketId) &&
                !string.Equals(message.ticketId, localTicketId, StringComparison.Ordinal))
            {
                return;
            }

            if (!message.success)
            {
                if (!string.IsNullOrWhiteSpace(message.reason))
                {
                    Debug.LogWarning(
                        $"[MatchPickupSync] Pickup rejected: {message.reason} spawn={message.spawnId}");
                }

                if (string.IsNullOrWhiteSpace(message.spawnId))
                {
                    FindFirstObjectByType<MatchDuelController>()?.HandleWeaponPickRejected(message.reason);
                }

                localPickupController?.HandlePickupRejected(message.spawnId, message.reason);
                return;
            }

            var itemId = !string.IsNullOrWhiteSpace(message.itemId)
                ? message.itemId
                : message.weaponId;
            var kind = PickupKindUtility.FromProtocol(message.pickupKind);
            if (string.IsNullOrWhiteSpace(message.pickupKind) && pickupSpawnManager != null)
            {
                kind = pickupSpawnManager.ResolveDefinitionForSpawnId(message.spawnId).Kind;
            }

            var serverState = BuildServerState(message, kind);
            localPresenceSync?.AcknowledgeWeaponPickupSeq(message.weaponPickupSeq);
            var confirmed = new PickupConfirmedInfo(
                message.spawnId,
                kind,
                itemId,
                message.amount > 0 ? message.amount : 1);
            localPickupController?.ApplyConfirmedPickup(confirmed, serverState);
            if (kind == PickupKind.Weapon)
            {
                var holster = localPickupController != null
                    ? localPickupController.GetComponent<PlayerWeaponHolsterController>()
                    : null;
                holster?.ForceArmedState();
            }
            if (kind == PickupKind.Grenade && message.grenadeCount >= 0)
            {
                var inventory = localPickupController != null
                    ? localPickupController.GetComponent<PlayerInventory>()
                    : null;
                inventory?.SetCount(InventoryItemIds.Grenade, message.grenadeCount);
            }

            localPresenceSync?.FlushLocalPose();
        }

        private static PickupApplyServerState BuildServerState(
            RealtimeTransportClient.PickupResultMessage message,
            PickupKind kind)
        {
            if (kind == PickupKind.Weapon)
            {
                return new PickupApplyServerState(
                    message.medkitCount,
                    false,
                    BuildWeaponLoadoutFromPickupResult(message));
            }

            if (kind == PickupKind.Ammo)
            {
                return new PickupApplyServerState(
                    message.medkitCount,
                    false,
                    BuildWeaponLoadoutFromPickupResult(message));
            }

            if (kind == PickupKind.Medkit)
            {
                return new PickupApplyServerState(message.medkitCount, true);
            }

            return PickupApplyServerState.None;
        }

        private void HandleWeaponDropResult(RealtimeTransportClient.WeaponDropResultMessage message)
        {
            if (message == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(localTicketId) &&
                !string.IsNullOrWhiteSpace(message.ticketId) &&
                !string.Equals(message.ticketId, localTicketId, StringComparison.Ordinal))
            {
                return;
            }

            if (!message.success)
            {
                Debug.LogWarning($"[MatchPickupSync] Weapon drop rejected: {message.reason}");
                return;
            }

            if (!string.IsNullOrWhiteSpace(message.droppedSpawnId))
            {
                TrySpawnDynamicPickupFromDropResult(message);
            }

            localLoadoutController?.ApplyServerDrop(
                message.slotIndex,
                BuildWeaponLoadoutFromDropResult(message));
            localPresenceSync?.AcknowledgeWeaponPickupSeq(message.weaponPickupSeq);
            localPickupController?.RefreshPickupContext();
            localPickupController?.RefreshWeaponAvailability();
            localPresenceSync?.FlushLocalPose();
        }

        private void HandleWeaponSwapResult(RealtimeTransportClient.WeaponSwapResultMessage message)
        {
            if (message == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(localTicketId) &&
                !string.IsNullOrWhiteSpace(message.ticketId) &&
                !string.Equals(message.ticketId, localTicketId, StringComparison.Ordinal))
            {
                return;
            }

            if (!message.success)
            {
                Debug.LogWarning($"[MatchPickupSync] Weapon swap rejected: {message.reason}");
                return;
            }

            localLoadoutController?.ApplyServerSwap(BuildWeaponLoadoutFromSwapResult(message));
            localPresenceSync?.AcknowledgeWeaponPickupSeq(message.weaponPickupSeq);
            localPickupController?.RefreshWeaponAvailability();
            localPresenceSync?.FlushLocalPose();
        }

        private void HandleInventoryItemDropResult(RealtimeTransportClient.InventoryItemDropResultMessage message)
        {
            if (message == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(localTicketId) &&
                !string.IsNullOrWhiteSpace(message.ticketId) &&
                !string.Equals(message.ticketId, localTicketId, StringComparison.Ordinal))
            {
                return;
            }

            if (!message.success)
            {
                Debug.LogWarning($"[MatchPickupSync] Inventory drop rejected: {message.reason}");
                return;
            }

            if (!string.IsNullOrWhiteSpace(message.droppedSpawnId) && pickupSpawnManager != null)
            {
                TrySpawnDynamicPickupFromInventoryDrop(message);
            }

            localLoadoutController?.ApplyServerInventoryDrop(
                message.itemId,
                message.medkitCount,
                message.grenadeCount,
                message.spareAmmoAssault,
                message.spareAmmoSniper,
                message.spareAmmoPistol,
                message.spareAmmoMp7,
                message.droppedSpawnId,
                new Vector3(message.x, message.y, message.z));
            localPickupController?.RefreshPickupContext();
            localPresenceSync?.FlushLocalPose();
        }

        private void TrySpawnDynamicPickupFromInventoryDrop(
            RealtimeTransportClient.InventoryItemDropResultMessage message)
        {
            if (pickupSpawnManager == null || string.IsNullOrWhiteSpace(message.droppedSpawnId))
            {
                return;
            }

            var kind = message.itemId == InventoryItemIds.Medkit
                ? PickupKind.Medkit
                : message.itemId == InventoryItemIds.Grenade
                    ? PickupKind.Grenade
                    : AmmoCatalog.TryResolveKindFromItemId(message.itemId, out _)
                        ? PickupKind.Ammo
                        : PickupKind.Weapon;
            var definition = pickupSpawnManager.ResolveDefinitionFromProtocol(
                kind,
                message.itemId,
                message.amount > 0 ? message.amount : 1);
            if (!definition.IsValid)
            {
                return;
            }

            var position = new Vector3(message.x, message.y, message.z);
            pickupSpawnManager.EnsureDynamicSlot(message.droppedSpawnId, position, Vector3.forward, definition);
            pickupSpawnManager.ApplyServerPickupRespawn(message.droppedSpawnId);
        }

        private void TrySpawnDynamicPickupFromEvent(RealtimeTransportClient.PickupEventMessage message)
        {
            if (pickupSpawnManager == null || message == null ||
                string.IsNullOrWhiteSpace(message.spawnId))
            {
                return;
            }

            var itemId = !string.IsNullOrWhiteSpace(message.itemId)
                ? message.itemId
                : message.weaponId;
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return;
            }

            var kind = PickupKindUtility.FromProtocol(message.pickupKind);
            if (string.IsNullOrWhiteSpace(message.pickupKind))
            {
                kind = PickupKind.Weapon;
            }

            var definition = pickupSpawnManager.ResolveDefinitionFromProtocol(
                kind,
                itemId,
                message.amount > 0 ? message.amount : 1);
            if (!definition.IsValid)
            {
                return;
            }

            pickupSpawnManager.CacheServerDefinition(message.spawnId, definition);

            if (!pickupSpawnManager.HasRegisteredSlot(message.spawnId))
            {
                var position = new Vector3(message.x, message.y, message.z);
                if (position.sqrMagnitude < 0.001f)
                {
                    return;
                }

                pickupSpawnManager.EnsureDynamicSlot(message.spawnId, position, Vector3.forward, definition);
            }
        }

        private void TrySpawnDynamicPickupFromDropResult(RealtimeTransportClient.WeaponDropResultMessage message)
        {
            if (pickupSpawnManager == null || message == null ||
                string.IsNullOrWhiteSpace(message.droppedSpawnId) ||
                string.IsNullOrWhiteSpace(message.itemId))
            {
                return;
            }

            var kind = WeaponCatalog.ResolveKindFromItemId(message.itemId);
            var sourcePrefab = pickupSpawnManager.ResolveWeaponSourcePrefab(kind);
            if (sourcePrefab == null)
            {
                return;
            }

            var definition = PickupItemDefinition.Create(PickupKind.Weapon, sourcePrefab, message.itemId, 1)
                .WithMagAmmo(Mathf.Clamp(message.magAmmo, 0, 999));
            var position = new Vector3(message.x, message.y, message.z);
            pickupSpawnManager.EnsureDynamicSlot(message.droppedSpawnId, position, Vector3.forward, definition);
            pickupSpawnManager.ApplyServerPickupRespawn(message.droppedSpawnId);
        }

        private static WeaponLoadoutServerState BuildWeaponLoadoutFromPickupResult(
            RealtimeTransportClient.PickupResultMessage message)
        {
            return new WeaponLoadoutServerState(
                true,
                (byte)Mathf.Clamp(message.weaponSlot0Kind, 0, 255),
                (byte)Mathf.Clamp(message.weaponSlot1Kind, 0, 255),
                message.weaponSlot0ItemId ?? string.Empty,
                message.weaponSlot1ItemId ?? string.Empty,
                message.activeWeaponSlot,
                message.bothHolstered,
                message.droppedSpawnId ?? string.Empty,
                message.magAmmo,
                message.reserveAmmo,
                message.spareAmmoAssault,
                message.spareAmmoSniper,
                message.spareAmmoPistol,
                message.spareAmmoMp7);
        }

        private static bool TryReadEventPosition(
            RealtimeTransportClient.PickupEventMessage message,
            out Vector3 position)
        {
            position = Vector3.zero;
            if (message == null)
            {
                return false;
            }

            if (Mathf.Abs(message.x) < 0.001f &&
                Mathf.Abs(message.y) < 0.001f &&
                Mathf.Abs(message.z) < 0.001f)
            {
                return false;
            }

            position = new Vector3(message.x, message.y, message.z);
            return true;
        }

        private static WeaponLoadoutServerState BuildWeaponLoadoutFromDropResult(
            RealtimeTransportClient.WeaponDropResultMessage message)
        {
            return new WeaponLoadoutServerState(
                true,
                (byte)Mathf.Clamp(message.weaponSlot0Kind, 0, 255),
                (byte)Mathf.Clamp(message.weaponSlot1Kind, 0, 255),
                message.weaponSlot0ItemId ?? string.Empty,
                message.weaponSlot1ItemId ?? string.Empty,
                message.activeWeaponSlot,
                message.bothHolstered,
                message.droppedSpawnId ?? string.Empty,
                -1,
                message.reserveAmmo,
                message.spareAmmoAssault,
                message.spareAmmoSniper,
                message.spareAmmoPistol,
                message.spareAmmoMp7);
        }

        private static WeaponLoadoutServerState BuildWeaponLoadoutFromSwapResult(
            RealtimeTransportClient.WeaponSwapResultMessage message)
        {
            return new WeaponLoadoutServerState(
                true,
                (byte)Mathf.Clamp(message.weaponSlot0Kind, 0, 255),
                (byte)Mathf.Clamp(message.weaponSlot1Kind, 0, 255),
                message.weaponSlot0ItemId ?? string.Empty,
                message.weaponSlot1ItemId ?? string.Empty,
                message.activeWeaponSlot,
                message.bothHolstered,
                string.Empty,
                -1,
                -1,
                message.spareAmmoAssault,
                message.spareAmmoSniper,
                message.spareAmmoPistol,
                message.spareAmmoMp7);
        }
    }
}
