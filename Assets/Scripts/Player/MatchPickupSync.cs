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
        private PlayerPickupController localPickupController;
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
            if (localPickupController == null)
            {
                return;
            }

            localPickupController.Configure(transportClient, pickupSpawnManager);
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
                pickupSpawnManager.ApplyServerPickupRespawn(message.spawnId);
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
                localPickupController?.HandlePickupRejected(message.spawnId);
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

            var confirmed = new PickupConfirmedInfo(
                message.spawnId,
                kind,
                itemId,
                message.amount > 0 ? message.amount : 1);
            localPickupController?.ApplyConfirmedPickup(confirmed);
        }
    }
}
