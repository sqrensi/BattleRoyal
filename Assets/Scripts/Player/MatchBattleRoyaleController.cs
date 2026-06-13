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
        [SerializeField] private float planeOrbitDistance = 48f;
        [SerializeField] private float planeOrbitHeight = 14f;
        [SerializeField] private float planeOrbitYaw = 200f;
        [SerializeField] private float planeOrbitPitch = 18f;
        [SerializeField] private float planeOrbitSensitivity = 0.925f;

        [Header("Drop")]
        [SerializeField] private float dropDescentSpeed = 13f;
        [SerializeField] private float dropGlideSpeed = 10f;
        [SerializeField] private float dropSteerRate = 5f;
        [SerializeField] private float dropStrafeInfluence = 0.45f;
        [SerializeField] private float dropMinAirTimeBeforeLand = 2f;
        [SerializeField] private float dropGroundLandClearance = 0.22f;
        [SerializeField] private float dropGroundProbeMaxDistance = 600f;
        [SerializeField] private float dropPoseFlushInterval = 0.08f;
        [SerializeField] private float dropLandingReconcileSuppressSeconds = 8f;

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
        private Vector3 dropHorizontalVelocity;
        private Vector3 jumpPlaneForward = Vector3.forward;
        private float nextDropPoseFlushRealtime;
        private float dropReconcileSuppressUntil;
        private float dropStartedRealtime;
        private Coroutine landingRoutine;

        public bool IsLocalParachuting => localDropState == LocalDropState.Jumping;
        public bool ShouldSuppressPoseReconcile => Time.unscaledTime < dropReconcileSuppressUntil;

        public void PrepareForNewMatch()
        {
            ResetForNewMatch();
        }

        private void OnEnable()
        {
            ResetForNewMatch();
            if (networkRoutine == null)
            {
                networkRoutine = StartCoroutine(NetworkRoutine());
            }
        }

        private void ResetForNewMatch()
        {
            currentPhase = "lobby";
            localDropState = LocalDropState.None;
            isOnPlane = false;
            hasJumpedLocally = false;
            hasLandedLocally = false;
            dropReconcileSuppressUntil = 0f;
            lastState = null;
            gameHud?.ResetMatchOverlay();
        }

        private void OnDisable()
        {
            if (networkRoutine != null)
            {
                StopCoroutine(networkRoutine);
                networkRoutine = null;
            }

            if (landingRoutine != null)
            {
                StopCoroutine(landingRoutine);
                landingRoutine = null;
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
                UpdatePlaneOrbitCameraInput();
                if (ReadJumpPressed() || lastState.forceJump)
                {
                    RequestJumpFromPlane(lastState);
                }
            }

            if (localDropState == LocalDropState.Jumping)
            {
                UpdateDropMotion();
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

            if ((message.hasLanded || message.inCombat) &&
                localDropState != LocalDropState.Jumping)
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
                gameHud.SetMatchStatusMessage("Прыжок: вращайте камерой и WASD, выберите место посадки");
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
                    gameHud.SetMatchStatusMessage("Вращайте мышью вокруг самолёта. [F] — прыжок");
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

            if (localDropState == LocalDropState.Jumping)
            {
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

            jumpPlaneForward = ResolveJumpLookForward();
            dropHorizontalVelocity = jumpPlaneForward * dropGlideSpeed;
            dropStartedRealtime = Time.realtimeSinceStartup;
            dropReconcileSuppressUntil = Time.unscaledTime + 600f;

            var jumpOrigin = ResolveJumpOrigin(message);
            localPlayer.SetParent(null, true);
            localPlayer.position = jumpOrigin;
            ApplyPlayerLookFromPlaneCamera();

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

            fpsController?.SetServerReconciliationSuspended(true);
            fpsController?.SetMovementLocked(true);
            fpsController?.ClearExternalLaunchVelocity();
            nextDropPoseFlushRealtime = Time.unscaledTime;
            presenceSync?.FlushLocalPose();
            CleanupPlane();

            RefreshHud(message);
        }

        private Vector3 ResolveJumpLookForward()
        {
            var cameraForward = GetPlaneCameraLookForward();
            if (cameraForward.sqrMagnitude > 0.001f)
            {
                return cameraForward;
            }

            return ResolveJumpPlaneForward();
        }

        private Vector3 GetPlaneCameraLookForward()
        {
            if (planeCamera == null)
            {
                return Vector3.zero;
            }

            var forward = planeCamera.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                return Vector3.zero;
            }

            return forward.normalized;
        }

        private void ApplyPlayerLookFromPlaneCamera()
        {
            if (planeCamera == null || fpsController == null)
            {
                return;
            }

            var lookForward = planeCamera.transform.forward;
            if (lookForward.sqrMagnitude < 0.001f)
            {
                return;
            }

            var yaw = Mathf.Atan2(lookForward.x, lookForward.z) * Mathf.Rad2Deg;
            var horizontalMag = new Vector2(lookForward.x, lookForward.z).magnitude;
            var pitch = -Mathf.Atan2(lookForward.y, Mathf.Max(0.0001f, horizontalMag)) * Mathf.Rad2Deg;
            fpsController.ApplyLookOrientation(yaw, pitch);
        }

        private Vector3 ResolveJumpPlaneForward()
        {
            if (lastState != null)
            {
                var path = new Vector3(
                    lastState.planeEndX - lastState.planeStartX,
                    0f,
                    lastState.planeEndZ - lastState.planeStartZ);
                if (path.sqrMagnitude > 0.001f)
                {
                    return path.normalized;
                }
            }

            var forward = planeInstance != null ? planeInstance.transform.forward : localPlayer.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }

            return forward.normalized;
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
            if (landingRoutine != null || fpsController == null || characterController == null || !characterController.enabled)
            {
                return;
            }

            if (Time.realtimeSinceStartup - dropStartedRealtime < dropMinAirTimeBeforeLand)
            {
                return;
            }

            if (!TryGetDropGroundClearance(out var clearance, out _))
            {
                return;
            }

            if (clearance > dropGroundLandClearance)
            {
                return;
            }

            landingRoutine = StartCoroutine(CompleteLandingRoutine(clearance));
        }

        private IEnumerator CompleteLandingRoutine(float clearance)
        {
            dropHorizontalVelocity = Vector3.zero;

            var settleDelta = Vector3.down * Mathf.Max(0f, clearance - 0.04f);
            if (settleDelta.sqrMagnitude > 0.000001f)
            {
                characterController.Move(settleDelta);
            }

            presenceSync?.FlushLocalPose();
            transportClient?.SendPlaneLanded();
            yield return null;
            presenceSync?.FlushLocalPose();
            yield return new WaitForSecondsRealtime(0.06f);
            presenceSync?.FlushLocalPose();
            presenceSync?.ResetSelfAuthoritativeReconcileCursor();

            hasLandedLocally = true;
            localDropState = LocalDropState.InCombat;
            dropReconcileSuppressUntil = Time.unscaledTime + Mathf.Max(2f, dropLandingReconcileSuppressSeconds);
            fpsController.ClearExternalLaunchVelocity();
            fpsController.SetMovementLocked(false);
            fpsController.NotifyLocalRespawned(dropLandingReconcileSuppressSeconds);

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

            landingRoutine = null;
        }

        private void UpdateDropMotion()
        {
            if (localPlayer == null || characterController == null || !characterController.enabled)
            {
                return;
            }

            var lookForward = GetDropLookForward();
            var steerInput = ReadDropSteerInput();
            var right = Vector3.Cross(Vector3.up, lookForward);
            if (right.sqrMagnitude > 0.001f)
            {
                right.Normalize();
            }

            var steerDirection = lookForward + (right * (steerInput.x * dropStrafeInfluence));
            if (steerInput.y > 0.01f)
            {
                steerDirection += lookForward * (steerInput.y * dropStrafeInfluence);
            }
            else if (steerInput.y < -0.01f)
            {
                steerDirection -= lookForward * (Mathf.Abs(steerInput.y) * dropStrafeInfluence * 0.5f);
            }

            if (steerDirection.sqrMagnitude > 0.001f)
            {
                steerDirection.Normalize();
            }
            else
            {
                steerDirection = lookForward;
            }

            var targetHorizontal = steerDirection * dropGlideSpeed;
            dropHorizontalVelocity = Vector3.MoveTowards(
                dropHorizontalVelocity,
                targetHorizontal,
                dropSteerRate * Time.deltaTime);

            characterController.Move(dropHorizontalVelocity * Time.deltaTime);
            characterController.Move(Vector3.down * (dropDescentSpeed * Time.deltaTime));

            if (Time.unscaledTime >= nextDropPoseFlushRealtime)
            {
                nextDropPoseFlushRealtime = Time.unscaledTime + Mathf.Max(0.05f, dropPoseFlushInterval);
                presenceSync?.FlushLocalPose();
            }
        }

        private Vector3 GetDropLookForward()
        {
            var forward = localPlayer != null ? localPlayer.forward : jumpPlaneForward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = jumpPlaneForward;
                forward.y = 0f;
            }

            if (forward.sqrMagnitude < 0.001f)
            {
                return Vector3.forward;
            }

            return forward.normalized;
        }

        private static Vector2 ReadDropSteerInput()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return Vector2.zero;
            }

            var x = 0f;
            var y = 0f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
            {
                x -= 1f;
            }

            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
            {
                x += 1f;
            }

            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
            {
                y += 1f;
            }

            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
            {
                y -= 1f;
            }

            var input = new Vector2(x, y);
            return input.sqrMagnitude > 1f ? input.normalized : input;
#else
            var input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            return input.sqrMagnitude > 1f ? input.normalized : input;
#endif
        }

        private bool TryGetDropGroundClearance(out float clearance, out Vector3 groundPoint)
        {
            clearance = float.MaxValue;
            groundPoint = Vector3.zero;
            if (localPlayer == null)
            {
                return false;
            }

            var origin = new Vector3(localPlayer.position.x, GetFeetAltitude() + 0.35f, localPlayer.position.z);
            if (!Physics.Raycast(
                    origin,
                    Vector3.down,
                    out var hit,
                    Mathf.Max(2f, dropGroundProbeMaxDistance),
                    ~0,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            groundPoint = hit.point;
            clearance = GetFeetAltitude() - hit.point.y;
            return true;
        }

        private float GetFeetAltitude()
        {
            if (localPlayer == null)
            {
                return 0f;
            }

            if (characterController == null)
            {
                return localPlayer.position.y;
            }

            return localPlayer.position.y +
                   characterController.center.y -
                   (characterController.height * 0.5f);
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
            planeOrbitYaw = 200f;
            planeOrbitPitch = 18f;
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
            UpdatePlaneOrbitCamera();
        }

        private void UpdatePlaneOrbitCameraInput()
        {
            var mouseDelta = ReadMouseDelta();
            if (mouseDelta.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            planeOrbitYaw += mouseDelta.x * planeOrbitSensitivity;
            planeOrbitPitch = Mathf.Clamp(
                planeOrbitPitch - (mouseDelta.y * planeOrbitSensitivity),
                -5f,
                80f);
        }

        private void UpdatePlaneOrbitCamera()
        {
            if (planeCamera == null || !planeCamera.enabled || planeInstance == null)
            {
                return;
            }

            var orbitCenter = planeInstance.transform.position + Vector3.up * 2f;
            var rotation = Quaternion.Euler(planeOrbitPitch, planeOrbitYaw, 0f);
            var offset = rotation * new Vector3(0f, planeOrbitHeight, -planeOrbitDistance);
            planeCamera.transform.position = orbitCenter + offset;
            planeCamera.transform.rotation = Quaternion.LookRotation(orbitCenter - planeCamera.transform.position, Vector3.up);
        }

        private static Vector2 ReadMouseDelta()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse == null)
            {
                return Vector2.zero;
            }

            return mouse.delta.ReadValue() * 0.09f;
#else
            return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#endif
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
            dropHorizontalVelocity = Vector3.zero;
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
