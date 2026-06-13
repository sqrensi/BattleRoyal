using System;
using System.Collections;
using ShooterPrototype.Network;
using ShooterPrototype.UI;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ShooterPrototype.Player
{
    [DisallowMultipleComponent]
    public sealed class MatchBattleRoyaleController : MonoBehaviour
    {
        private enum LocalDropState
        {
            None,
            OnPlane,
            Jumping,
            InCombat
        }

        [Header("Plane")]
        [SerializeField] private GameObject planePrefab;
        [SerializeField] private Vector3 planeSeatLocalOffset = new Vector3(0f, 2.5f, -2f);
        [SerializeField] private Vector3 planeCameraLocalOffset = new Vector3(-42f, 16f, 0f);
        [SerializeField] private float planeCameraLookAhead = 18f;
        [SerializeField] private float jumpForwardSpeed = 2f;
        [SerializeField] private float jumpDownSpeed = 1f;

        private RealtimeTransportClient transportClient;
        private GameHudController gameHud;
        private Transform localPlayer;
        private FpsCharacterController fpsController;
        private CharacterController characterController;
        private PlayerViewPresentation viewPresentation;
        private PlayerWeaponController weaponController;
        private PlayerPickupController pickupController;
        private PlayerHealth playerHealth;
        private PlayerNetworkIdentity playerIdentity;
        private MatchPresenceSync presenceSync;
        private Camera playerCamera;
        private Camera planeCamera;
        private Transform planeSeat;
        private GameObject planeInstance;
        private Coroutine networkRoutine;

        private string currentPhase = "lobby";
        private LocalDropState localDropState = LocalDropState.None;
        private bool isOnPlane;
        private bool hasJumpedLocally;
        private bool hasLandedLocally;
        private RealtimeTransportClient.MatchStateMessage lastState;

        private Vector3 planePathStart;
        private Vector3 planePathEnd;
        private float planePathDurationSeconds = 1f;
        private float planeLocalStartRealtime;
        private bool planeTimingInitialized;
        private Vector3 smoothedCameraPosition;
        private Vector3 cameraPositionVelocity;
        private bool planeCameraInitialized;

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
            CleanupPlane();
        }

        private IEnumerator NetworkRoutine()
        {
            var wait = new WaitForSecondsRealtime(0.25f);
            while (true)
            {
                EnsureLocalPlayer();
                EnsureTransport();
                yield return wait;
            }
        }

        private void Update()
        {
            if (isOnPlane && !hasJumpedLocally && lastState != null)
            {
                if (ReadJumpPressed() || lastState.forceJump)
                {
                    RequestJumpFromPlane(lastState);
                }
            }

            if (localDropState == LocalDropState.Jumping)
            {
                TryCompleteLanding();
            }
        }

        private void LateUpdate()
        {
            if (!isOnPlane || planeInstance == null || !planeTimingInitialized)
            {
                return;
            }

            SyncPlaneMotion();
        }

        private void EnsureLocalPlayer()
        {
            if (localPlayer != null)
            {
                return;
            }

            var marker = FindFirstObjectByType<LocalPlayerMarker>();
            if (marker == null)
            {
                return;
            }

            localPlayer = marker.transform;
            fpsController = marker.GetComponent<FpsCharacterController>();
            characterController = marker.GetComponent<CharacterController>();
            viewPresentation = marker.GetComponent<PlayerViewPresentation>();
            weaponController = marker.GetComponent<PlayerWeaponController>();
            pickupController = marker.GetComponent<PlayerPickupController>();
            playerHealth = marker.GetComponent<PlayerHealth>();
            playerIdentity = marker.GetComponent<PlayerNetworkIdentity>();
            presenceSync = marker.GetComponent<MatchPresenceSync>();
            playerCamera = marker.GetComponentInChildren<Camera>(true);
            playerHealth?.SetEliminationMode(true);
        }

        private void EnsureTransport()
        {
            if (transportClient == null)
            {
                transportClient = FindFirstObjectByType<RealtimeTransportClient>();
            }

            if (gameHud == null)
            {
                gameHud = FindFirstObjectByType<GameHudController>();
            }

            SubscribeToTransport();
        }

        private void SubscribeToTransport()
        {
            if (transportClient == null)
            {
                return;
            }

            transportClient.MatchStateReceived -= HandleMatchState;
            transportClient.MatchStateReceived += HandleMatchState;
            transportClient.MatchDisconnectReceived -= HandleMatchDisconnect;
            transportClient.MatchDisconnectReceived += HandleMatchDisconnect;
        }

        private void UnsubscribeFromTransport()
        {
            if (transportClient == null)
            {
                return;
            }

            transportClient.MatchStateReceived -= HandleMatchState;
            transportClient.MatchDisconnectReceived -= HandleMatchDisconnect;
        }

        private void HandleMatchState(RealtimeTransportClient.MatchStateMessage message)
        {
            if (message == null)
            {
                return;
            }

            var previousPhase = currentPhase;
            lastState = message;
            if (!string.IsNullOrWhiteSpace(message.phase))
            {
                currentPhase = message.phase;
            }

            gameHud?.SetKillCount(message.localKillCount);

            if (currentPhase == "ending")
            {
                EnterMatchEndedState(message);
                return;
            }

            if (message.hasLanded || message.inCombat)
            {
                hasLandedLocally = true;
                localDropState = LocalDropState.InCombat;
                EnableLocalCombat(message);
            }

            if (previousPhase != "plane" && currentPhase == "plane")
            {
                ResetPlaneTiming();
            }

            if (currentPhase == "plane" && isOnPlane)
            {
                InitializePlaneTiming(message);
            }

            RefreshHud(message);
            ApplyPhase(message);
        }

        private void HandleMatchDisconnect(RealtimeTransportClient.MatchDisconnectMessage message)
        {
            if (message == null)
            {
                return;
            }

            if (playerIdentity != null &&
                !string.IsNullOrWhiteSpace(playerIdentity.TicketId) &&
                !string.IsNullOrWhiteSpace(message.ticketId) &&
                !string.Equals(playerIdentity.TicketId, message.ticketId, StringComparison.Ordinal))
            {
                return;
            }

            var reason = message.reason ?? string.Empty;
            if (reason == "winner")
            {
                gameHud?.SetVictoryBanner(false);
                gameHud?.SetMatchStatusMessage("Возврат в меню...");
            }
            else if (reason == "eliminated")
            {
                gameHud?.SetMatchStatusMessage("Вы выбыли. Возврат в меню...");
            }
            else
            {
                gameHud?.SetMatchStatusMessage("Матч завершён.");
            }

            gameHud?.RequestReturnToMenu(reason);
        }

        private void RefreshHud(RealtimeTransportClient.MatchStateMessage message)
        {
            if (gameHud == null || message == null)
            {
                return;
            }

            gameHud.SetKillCount(message.localKillCount);

            if (currentPhase == "ending" || message.phase == "ending")
            {
                return;
            }

            if (localDropState == LocalDropState.InCombat || message.inCombat || hasLandedLocally)
            {
                gameHud.SetMatchStatusMessage($"В бою. Осталось: {Mathf.Max(0, message.aliveCount)}");
                return;
            }

            if (localDropState == LocalDropState.Jumping || hasJumpedLocally)
            {
                gameHud.SetMatchStatusMessage("Прыжок...");
                return;
            }

            switch (message.phase)
            {
                case "lobby":
                    gameHud.SetMatchStatusMessage(
                        $"Ожидание игроков... ({message.connectedCount}/{Mathf.Max(2, message.connectedCount)})");
                    break;
                case "countdown":
                    gameHud.SetMatchStatusMessage(
                        $"Старт через {Mathf.Max(0, message.countdownRemainingSeconds)}...");
                    break;
                case "plane":
                    gameHud.SetMatchStatusMessage("Нажмите [F] чтобы выпрыгнуть из самолёта");
                    break;
                case "playing":
                    gameHud.SetMatchStatusMessage($"В бою. Осталось: {Mathf.Max(0, message.aliveCount)}");
                    break;
            }
        }

        private void EnterMatchEndedState(RealtimeTransportClient.MatchStateMessage message)
        {
            ExitPlaneIfNeeded();
            fpsController?.SetMovementLocked(true);
            fpsController?.ClearExternalLaunchVelocity();
            SetCombatEnabled(false);
            SetZoneVisualActive(false);

            var isWinner = message.isLocalWinner ||
                           (playerIdentity != null &&
                            !string.IsNullOrWhiteSpace(message.winnerTicketId) &&
                            string.Equals(playerIdentity.TicketId, message.winnerTicketId, StringComparison.Ordinal));

            if (isWinner)
            {
                var subtitle = message.winnerDisconnectSeconds > 0
                    ? $"Возврат в меню через {message.winnerDisconnectSeconds}..."
                    : string.Empty;
                gameHud?.SetVictoryBanner(true, subtitle);
                gameHud?.SetMatchStatusMessage(string.Empty);
            }
            else
            {
                gameHud?.SetVictoryBanner(false);
                gameHud?.SetMatchStatusMessage("Матч завершён");
            }
        }

        private void ApplyPhase(RealtimeTransportClient.MatchStateMessage message)
        {
            var phase = message.phase ?? "lobby";

            if (phase == "ending")
            {
                EnterMatchEndedState(message);
                return;
            }

            if (localDropState == LocalDropState.InCombat || message.inCombat || hasLandedLocally)
            {
                ExitPlaneIfNeeded();
                fpsController?.SetMovementLocked(false);
                EnableLocalCombat(message);
                return;
            }

            switch (phase)
            {
                case "lobby":
                case "countdown":
                    ResetLocalDropState();
                    ExitPlaneIfNeeded();
                    fpsController?.SetMovementLocked(false);
                    SetCombatEnabled(false);
                    SetZoneVisualActive(false);
                    break;
                case "plane":
                    SetCombatEnabled(false);
                    SetZoneVisualActive(false);
                    if (!message.hasJumped && !hasJumpedLocally)
                    {
                        EnterPlanePhase(message);
                    }
                    else if (!hasJumpedLocally && message.hasJumped)
                    {
                        ExecuteJumpFromPlane(message);
                    }
                    break;
                case "playing":
                    if (isOnPlane && !hasJumpedLocally)
                    {
                        ExecuteJumpFromPlane(message);
                    }
                    else
                    {
                        ExitPlaneIfNeeded();
                    }

                    fpsController?.SetMovementLocked(false);
                    if (localDropState != LocalDropState.Jumping)
                    {
                        EnableLocalCombat(message);
                    }
                    break;
            }
        }

        private void EnableLocalCombat(RealtimeTransportClient.MatchStateMessage message)
        {
            localDropState = LocalDropState.InCombat;
            hasLandedLocally = true;
            SetCombatEnabled(true);
            SetZoneVisualActive(true);
            RefreshHud(message);
        }

        private void SetCombatEnabled(bool enabled)
        {
            if (pickupController != null)
            {
                pickupController.enabled = enabled;
            }

            if (weaponController != null && !enabled)
            {
                weaponController.enabled = false;
            }

            if (enabled)
            {
                pickupController?.RefreshWeaponAvailability();
            }
        }

        private static void SetZoneVisualActive(bool active)
        {
            var zone = FindFirstObjectByType<MatchDamageZoneController>();
            zone?.SetZoneVisualActive(active);
        }

        private void EnterPlanePhase(RealtimeTransportClient.MatchStateMessage message)
        {
            if (localPlayer == null || isOnPlane)
            {
                return;
            }

            EnsurePlaneInstance(message);
            if (planeInstance == null)
            {
                return;
            }

            localDropState = LocalDropState.OnPlane;
            isOnPlane = true;
            fpsController?.SetMovementLocked(true);
            viewPresentation?.SetForceThirdPersonBody(true);

            if (characterController != null)
            {
                characterController.enabled = false;
            }

            if (playerCamera != null)
            {
                playerCamera.enabled = false;
            }

            EnsurePlaneCamera();
            if (planeCamera != null)
            {
                planeCamera.enabled = true;
            }

            localPlayer.SetParent(planeSeat, true);
            localPlayer.localPosition = Vector3.zero;
            localPlayer.localRotation = Quaternion.identity;
            InitializePlaneTiming(message);
            SyncPlaneMotion();
        }

        private void RequestJumpFromPlane(RealtimeTransportClient.MatchStateMessage message)
        {
            if (!isOnPlane || hasJumpedLocally)
            {
                return;
            }

            transportClient?.SendPlaneJump();
            ExecuteJumpFromPlane(message);
        }

        private void ExecuteJumpFromPlane(RealtimeTransportClient.MatchStateMessage message)
        {
            if (localPlayer == null || hasJumpedLocally)
            {
                return;
            }

            hasJumpedLocally = true;
            isOnPlane = false;
            localDropState = LocalDropState.Jumping;

            var jumpOrigin = ResolveJumpOrigin(message);
            localPlayer.SetParent(null, true);
            localPlayer.position = jumpOrigin;

            viewPresentation?.SetForceThirdPersonBody(false);

            if (characterController != null)
            {
                characterController.enabled = true;
            }

            if (playerCamera != null)
            {
                playerCamera.enabled = true;
            }

            if (planeCamera != null)
            {
                planeCamera.enabled = false;
            }

            fpsController?.SetMovementLocked(false);
            fpsController?.ClearExternalLaunchVelocity();
            fpsController?.NotifyLocalRespawned(2.5f);
            ApplyJumpVelocity();
            presenceSync?.FlushLocalPose();
            CleanupPlane();

            RefreshHud(message);
        }

        private Vector3 ResolveJumpOrigin(RealtimeTransportClient.MatchStateMessage message)
        {
            if (message != null && message.useForcedDrop)
            {
                return new Vector3(message.dropPosX, message.planeY - 1.5f, message.dropPosZ);
            }

            if (planeSeat != null)
            {
                return planeSeat.position + Vector3.down * 1.5f;
            }

            if (planeInstance != null)
            {
                return planeInstance.transform.position + Vector3.down * 1.5f;
            }

            return localPlayer.position;
        }

        private void TryCompleteLanding()
        {
            if (fpsController == null || !fpsController.IsGrounded)
            {
                return;
            }

            hasLandedLocally = true;
            localDropState = LocalDropState.InCombat;
            fpsController.ClearExternalLaunchVelocity();
            transportClient?.SendPlaneLanded();
            presenceSync?.FlushLocalPose();

            if (lastState != null)
            {
                EnableLocalCombat(lastState);
            }
            else
            {
                SetCombatEnabled(true);
                SetZoneVisualActive(true);
                gameHud?.SetMatchStatusMessage("В бою.");
            }
        }

        private void ApplyJumpVelocity()
        {
            if (fpsController == null)
            {
                return;
            }

            var forward = planeInstance != null ? planeInstance.transform.forward : localPlayer.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();
            fpsController.ApplyExternalLaunchVelocity(
                forward * jumpForwardSpeed + Vector3.down * jumpDownSpeed);
        }

        private void ExitPlaneIfNeeded()
        {
            if (!isOnPlane && planeInstance == null)
            {
                return;
            }

            if (localPlayer != null && planeSeat != null && localPlayer.parent == planeSeat)
            {
                localPlayer.SetParent(null, true);
            }

            isOnPlane = false;
            viewPresentation?.SetForceThirdPersonBody(false);

            if (characterController != null && (playerHealth == null || !playerHealth.IsDead))
            {
                characterController.enabled = true;
            }

            if (playerCamera != null)
            {
                playerCamera.enabled = true;
            }

            if (planeCamera != null)
            {
                planeCamera.enabled = false;
            }

            CleanupPlane();
        }

        private void EnsurePlaneInstance(RealtimeTransportClient.MatchStateMessage message)
        {
            if (planeInstance != null)
            {
                return;
            }

            if (planePrefab == null)
            {
                planePrefab = Resources.Load<GameObject>("Plane/Plane");
            }

            if (planePrefab == null)
            {
                Debug.LogWarning("[MatchBattleRoyale] Plane prefab is not assigned.");
                return;
            }

            planeInstance = Instantiate(planePrefab);
            planeInstance.name = "BattleRoyalePlane";
            planeSeat = new GameObject("PlaneSeat").transform;
            planeSeat.SetParent(planeInstance.transform, false);
            planeSeat.localPosition = planeSeatLocalOffset;
            InitializePlaneTiming(message);
        }

        private void ResetPlaneTiming()
        {
            planeTimingInitialized = false;
            planeLocalStartRealtime = 0f;
            planeCameraInitialized = false;
            smoothedCameraPosition = Vector3.zero;
            cameraPositionVelocity = Vector3.zero;
        }

        private void ResetLocalDropState()
        {
            if (localDropState == LocalDropState.InCombat)
            {
                return;
            }

            localDropState = LocalDropState.None;
            hasJumpedLocally = false;
            hasLandedLocally = false;
        }

        private void InitializePlaneTiming(RealtimeTransportClient.MatchStateMessage message)
        {
            if (message == null || message.planeEndsAtMs <= message.planeStartedAtMs)
            {
                return;
            }

            planePathDurationSeconds = Mathf.Max(
                0.01f,
                (message.planeEndsAtMs - message.planeStartedAtMs) / 1000f);
            planePathStart = new Vector3(message.planeStartX, message.planeY, message.planeStartZ);
            planePathEnd = new Vector3(message.planeEndX, message.planeY, message.planeEndZ);

            if (planeTimingInitialized)
            {
                return;
            }

            var serverNowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var serverElapsedSec = Mathf.Max(
                0f,
                (serverNowMs - message.planeStartedAtMs) / 1000f);
            planeLocalStartRealtime = Time.realtimeSinceStartup - serverElapsedSec;
            planeTimingInitialized = true;
        }

        private void SyncPlaneMotion()
        {
            if (planeInstance == null || !planeTimingInitialized)
            {
                return;
            }

            var elapsed = Time.realtimeSinceStartup - planeLocalStartRealtime;
            var progress = Mathf.Clamp01(elapsed / planePathDurationSeconds);
            var position = Vector3.Lerp(planePathStart, planePathEnd, progress);

            var direction = planePathEnd - planePathStart;
            direction.y = 0f;
            var rotation = direction.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                : planeInstance.transform.rotation;

            planeInstance.transform.SetPositionAndRotation(position, rotation);
            UpdatePlaneCameraSmooth();
        }

        private void UpdatePlaneCameraSmooth()
        {
            if (planeCamera == null || !planeCamera.enabled || planeInstance == null)
            {
                return;
            }

            var targetCameraPosition = planeInstance.transform.TransformPoint(planeCameraLocalOffset);
            var lookTarget = planeInstance.transform.position +
                             planeInstance.transform.forward * planeCameraLookAhead +
                             Vector3.up * 2f;

            if (!planeCameraInitialized)
            {
                smoothedCameraPosition = targetCameraPosition;
                planeCameraInitialized = true;
            }
            else
            {
                smoothedCameraPosition = Vector3.SmoothDamp(
                    smoothedCameraPosition,
                    targetCameraPosition,
                    ref cameraPositionVelocity,
                    0.12f);
            }

            planeCamera.transform.position = smoothedCameraPosition;
            planeCamera.transform.rotation = Quaternion.Slerp(
                planeCamera.transform.rotation,
                Quaternion.LookRotation(lookTarget - smoothedCameraPosition, Vector3.up),
                Mathf.Clamp01(Time.deltaTime * 8f));
        }

        private void EnsurePlaneCamera()
        {
            if (planeCamera != null)
            {
                return;
            }

            var cameraObject = new GameObject("PlaneFollowCamera");
            planeCamera = cameraObject.AddComponent<Camera>();
            planeCamera.depth = playerCamera != null ? playerCamera.depth + 1f : 1f;
        }

        private void CleanupPlane()
        {
            if (planeCamera != null)
            {
                Destroy(planeCamera.gameObject);
                planeCamera = null;
            }

            if (planeInstance != null)
            {
                Destroy(planeInstance);
                planeInstance = null;
            }

            planeSeat = null;
            planeTimingInitialized = false;
            planeCameraInitialized = false;
            smoothedCameraPosition = Vector3.zero;
            cameraPositionVelocity = Vector3.zero;
        }

        private static bool ReadJumpPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.fKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.F);
#endif
        }
    }
}
