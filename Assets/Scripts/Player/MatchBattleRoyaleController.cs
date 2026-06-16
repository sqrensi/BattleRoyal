using System;
using System.Collections;
using System.Collections.Generic;
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
        private const string PlanePrefabAssetPath = "Assets/Prefabs/Plane/Plane.prefab";
        private const string PlanePrefabResourcesPath = "Prefabs/Plane/Plane";
        private const string PlaneLoopClipResourcesPath = "Sounds/2";
        private enum LocalDropState
        {
            None,
            OnPlane,
            Falling,
            Gliding,
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
        [SerializeField] private float planeColliderRestoreDistance = 28f;

        [Header("Drop")]
        [SerializeField] private float mapPlayableHalfEdge = 60f;
        [SerializeField] private float freeFallGravity = -24f;
        [SerializeField] private float freeFallTerminalSpeed = 52f;
        [SerializeField] private float dropDescentSpeed = 13f;
        [SerializeField] private float dropGlideSpeed = 10f;
        [SerializeField] private float dropSteerRate = 5f;
        [SerializeField] private float dropStrafeInfluence = 0.45f;
        [SerializeField] private float dropMinAirTimeBeforeLand = 2f;
        [SerializeField] private float dropGroundLandClearance = 0.22f;
        [SerializeField] private float dropGroundProbeMaxDistance = 600f;
        [SerializeField] private float dropPoseFlushInterval = 0.08f;
        [SerializeField] private float dropLandingReconcileSuppressSeconds = 8f;
        [SerializeField] private float autoParachuteHeightAboveGround = 32f;
        [SerializeField] private float minFreeFallSecondsBeforeParachute = 1f;

        [Header("Audio")]
        [SerializeField] private AudioClip planeLoopClip;
        [SerializeField] private AudioClip windLoopClip;
        [SerializeField] private float planeLoopVolume = 0.55f;
        [SerializeField] private float planeLoopMinDistance = 8f;
        [SerializeField] private float planeLoopMaxDistance = 58f;
        [SerializeField] private float planeLoopDepartFadeExponent = 2.4f;
        [SerializeField] private float windLoopVolume = 0.65f;
        [SerializeField] private float windGlideVolume = 0.38f;
        [SerializeField] private float windFreeFallPitch = 1f;
        [SerializeField] private float windGlidePitch = 0.82f;

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
        private bool parachuteDeployed;
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
        private float fallVerticalVelocity;
        private readonly List<Collider> cachedPlaneColliders = new List<Collider>();
        private bool planeCollidersSuppressed;
        private Coroutine landingRoutine;
        private bool planeDeparting;
        private AudioSource planeLoopSource;
        private AudioSource windLoopSource;
        private PlayerAudioController localAudioController;

        public bool IsLocalParachuting =>
            localDropState == LocalDropState.Falling || localDropState == LocalDropState.Gliding;
        public bool IsLocalOnPlane =>
            isOnPlane && localDropState == LocalDropState.OnPlane;
        public bool IsPlanePhaseActive =>
            currentPhase == "plane" && planeInstance != null;
        public Transform PlaneTransform =>
            planeInstance != null ? planeInstance.transform : null;
        public bool ShouldSuppressPoseReconcile =>
            IsLocalOnPlane || Time.unscaledTime < dropReconcileSuppressUntil;

        public void PrepareForNewMatch()
        {
            ResetForNewMatch();
        }

        public void ConfigurePlanePrefab(GameObject prefab)
        {
            if (prefab != null)
            {
                planePrefab = prefab;
            }
        }

        private void Awake()
        {
            EnsurePlanePrefabAssigned();
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
            parachuteDeployed = false;
            planeDeparting = false;
            cachedPlaneColliders.Clear();
            planeCollidersSuppressed = false;
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
                if (ShouldAutoEjectAtMapExit(lastState) || lastState.forceJump)
                {
                    RequestJumpFromPlane(lastState);
                }
                else if (lastState.useForcedDrop)
                {
                    BeginFallFromPlane(lastState, forcedExtract: true);
                }
                else if (ReadJumpPressed())
                {
                    RequestJumpFromPlane(lastState);
                }
            }

            if (localDropState == LocalDropState.Falling)
            {
                UpdateFallingMotion();
                TryRestorePlaneCollidersAfterDrop();
                TryDeployParachute();
                TryAutoDeployParachute();
                TryCompleteLanding();
            }

            if (localDropState == LocalDropState.Gliding)
            {
                UpdateGlidingMotion();
                TryRestorePlaneCollidersAfterDrop();
                TryCompleteLanding();
            }

            UpdatePlaneLoopAttenuation();
        }

        private void LateUpdate()
        {
            if (planeInstance == null || !planeTimingInitialized)
            {
                return;
            }

            if (isOnPlane || planeDeparting)
            {
                SyncPlaneMotion();
            }

            if (planeDeparting)
            {
                TryRetireDepartingPlane();
            }
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
            localAudioController = marker.GetComponent<PlayerAudioController>();
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
                localDropState != LocalDropState.Falling &&
                localDropState != LocalDropState.Gliding)
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
                SoftResyncPlaneTiming(message);
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

            if (localDropState == LocalDropState.Gliding || (hasJumpedLocally && parachuteDeployed))
            {
                gameHud.SetMatchStatusMessage("Планирование: камера + WASD, выберите место посадки");
                return;
            }

            if (localDropState == LocalDropState.Falling || (hasJumpedLocally && !parachuteDeployed))
            {
                gameHud.SetMatchStatusMessage("[F] — парашют (авто-раскрытие на малой высоте)");
                return;
            }

            if (hasJumpedLocally)
            {
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

            if (localDropState == LocalDropState.Falling || localDropState == LocalDropState.Gliding)
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
                        BeginFallFromPlane(message, forcedExtract: false);
                    }
                    break;
                case "playing":
                    if (localDropState == LocalDropState.OnPlane)
                    {
                        if (message.useForcedDrop)
                        {
                            BeginFallFromPlane(message, forcedExtract: true);
                        }
                        else
                        {
                            RequestJumpFromPlane(message);
                        }
                    }
                    else if (!hasJumpedLocally)
                    {
                        ExitPlaneIfNeeded();
                    }

                    if (localDropState != LocalDropState.Falling && localDropState != LocalDropState.Gliding)
                    {
                        fpsController?.SetMovementLocked(false);
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
            hasJumpedLocally = false;
            parachuteDeployed = false;
            fpsController?.SetMovementLocked(true);
            fpsController?.SetServerReconciliationSuspended(true);
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
            presenceSync?.FlushLocalPose();
            StartPlaneLoopAudio();
        }

        private void RequestJumpFromPlane(RealtimeTransportClient.MatchStateMessage message)
        {
            if (!isOnPlane || hasJumpedLocally)
            {
                return;
            }

            BeginFallFromPlane(message, forcedExtract: false);
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

        private Vector3 ResolveJumpOrigin()
        {
            if (planeSeat != null)
            {
                return planeSeat.position + Vector3.down * 1.5f;
            }

            if (planeInstance != null)
            {
                return planeInstance.transform.TransformPoint(planeSeatLocalOffset) + Vector3.down * 1.5f;
            }

            return localPlayer.position;
        }

        private void CachePlaneColliders()
        {
            cachedPlaneColliders.Clear();
            if (planeInstance == null)
            {
                return;
            }

            var colliders = planeInstance.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider != null)
                {
                    cachedPlaneColliders.Add(collider);
                }
            }
        }

        private void SetPlaneCollidersEnabled(bool enabled)
        {
            if (cachedPlaneColliders.Count == 0)
            {
                CachePlaneColliders();
            }

            for (var i = 0; i < cachedPlaneColliders.Count; i++)
            {
                var collider = cachedPlaneColliders[i];
                if (collider != null)
                {
                    collider.enabled = enabled;
                }
            }

            planeCollidersSuppressed = !enabled;
        }

        private void TryRestorePlaneCollidersAfterDrop()
        {
            if (!planeCollidersSuppressed || planeInstance == null || localPlayer == null)
            {
                return;
            }

            var distance = Vector3.Distance(localPlayer.position, planeInstance.transform.position);
            if (distance >= Mathf.Max(12f, planeColliderRestoreDistance))
            {
                SetPlaneCollidersEnabled(true);
            }
        }

        private bool IsPlaneCollider(Collider collider)
        {
            if (collider == null || planeInstance == null)
            {
                return false;
            }

            var hitTransform = collider.transform;
            return hitTransform == planeInstance.transform || hitTransform.IsChildOf(planeInstance.transform);
        }

        private bool ShouldAutoEjectAtMapExit(RealtimeTransportClient.MatchStateMessage message)
        {
            if (message == null || planeInstance == null)
            {
                return false;
            }

            var halfEdge = Mathf.Max(1f, mapPlayableHalfEdge);
            var centerX = message.mapCenterX;
            var centerZ = message.mapCenterZ;
            var pos = planeInstance.transform.position;
            var relX = pos.x - centerX;
            var relZ = pos.z - centerZ;

            if (Mathf.Abs(relX) <= halfEdge && Mathf.Abs(relZ) <= halfEdge)
            {
                return false;
            }

            var planePos2 = new Vector2(pos.x, pos.z);
            var distToStart = Vector2.Distance(
                planePos2,
                new Vector2(message.planeStartX, message.planeStartZ));
            var distToEnd = Vector2.Distance(
                planePos2,
                new Vector2(message.planeEndX, message.planeEndZ));
            return distToEnd < distToStart;
        }

        private bool TryResolveForcedDropPosition(
            RealtimeTransportClient.MatchStateMessage message,
            out Vector3 worldPosition)
        {
            worldPosition = Vector3.zero;
            if (message == null || !message.useForcedDrop)
            {
                return false;
            }

            if (Mathf.Abs(message.dropPosX) < 0.01f && Mathf.Abs(message.dropPosZ) < 0.01f)
            {
                return false;
            }

            var altitude = message.planeY > 1f
                ? message.planeY
                : planePathEnd.y > 1f
                    ? planePathEnd.y
                    : 120f;
            worldPosition = new Vector3(message.dropPosX, altitude, message.dropPosZ);
            return true;
        }

        private void PlacePlayerAtForcedDrop(RealtimeTransportClient.MatchStateMessage message)
        {
            if (localPlayer == null || !TryResolveForcedDropPosition(message, out var dropPosition))
            {
                PlacePlayerAtPlaneRearExit();
                return;
            }

            localPlayer.position = dropPosition;

            var lookForward = ResolveJumpPlaneForward();
            if (lookForward.sqrMagnitude > 0.001f)
            {
                localPlayer.rotation = Quaternion.LookRotation(lookForward, Vector3.up);
            }
        }

        private void PlacePlayerAtPlaneRearExit()
        {
            if (localPlayer == null || planeInstance == null)
            {
                return;
            }

            var exitLocal = planeSeatLocalOffset + new Vector3(0f, -1.25f, -3.5f);
            localPlayer.position = planeInstance.transform.TransformPoint(exitLocal);

            var planeForward = planeInstance.transform.forward;
            planeForward.y = 0f;
            if (planeForward.sqrMagnitude > 0.001f)
            {
                localPlayer.rotation = Quaternion.LookRotation(planeForward.normalized, Vector3.up);
            }
        }

        private void BeginFallFromPlane(RealtimeTransportClient.MatchStateMessage message, bool forcedExtract)
        {
            if (localPlayer == null || hasJumpedLocally)
            {
                return;
            }

            hasJumpedLocally = true;
            isOnPlane = false;
            localDropState = LocalDropState.Falling;
            parachuteDeployed = false;
            planeDeparting = true;
            fallVerticalVelocity = 0f;
            dropHorizontalVelocity = Vector3.zero;
            dropStartedRealtime = Time.realtimeSinceStartup;
            dropReconcileSuppressUntil = Time.unscaledTime + 600f;

            if (forcedExtract)
            {
                if (!TryResolveForcedDropPosition(message, out _))
                {
                    SnapPlaneToRouteEnd();
                }
            }

            if (localPlayer.parent != null)
            {
                localPlayer.SetParent(null, true);
            }

            if (forcedExtract)
            {
                PlacePlayerAtForcedDrop(message);
                jumpPlaneForward = ResolveJumpPlaneForward();
            }
            else
            {
                localPlayer.position = ResolveJumpOrigin();
                ApplyPlayerLookFromPlaneCamera();
                jumpPlaneForward = ResolveJumpLookForward();
            }

            SetPlaneCollidersEnabled(false);

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
                Destroy(planeCamera.gameObject);
                planeCamera = null;
            }

            transportClient?.SendPlaneJump();
            localAudioController?.PlayPlaneJump();
            StartWindLoopAudio(isGliding: false);
            fpsController?.SetServerReconciliationSuspended(true);
            fpsController?.SetMovementLocked(true);
            fpsController?.ClearExternalLaunchVelocity();
            nextDropPoseFlushRealtime = Time.unscaledTime;
            presenceSync?.FlushLocalPose();
            RefreshHud(message ?? lastState);
        }

        private void TryDeployParachute()
        {
            if (localDropState != LocalDropState.Falling || parachuteDeployed)
            {
                return;
            }

            if (Time.realtimeSinceStartup - dropStartedRealtime < Mathf.Max(0.1f, minFreeFallSecondsBeforeParachute))
            {
                return;
            }

            if (!ReadParachutePressed())
            {
                return;
            }

            DeployParachute();
        }

        private void TryAutoDeployParachute()
        {
            if (localDropState != LocalDropState.Falling || parachuteDeployed)
            {
                return;
            }

            if (TryGetDropGroundClearance(out var clearance, out _))
            {
                if (clearance <= Mathf.Max(8f, autoParachuteHeightAboveGround))
                {
                    DeployParachute();
                }

                return;
            }

            var referenceAltitude = lastState != null && lastState.planeY > 1f
                ? lastState.planeY
                : planePathStart.y;
            if (referenceAltitude > 1f &&
                GetFeetAltitude() <= referenceAltitude - Mathf.Max(8f, autoParachuteHeightAboveGround))
            {
                DeployParachute();
            }
        }

        private void DeployParachute()
        {
            if (localDropState != LocalDropState.Falling || parachuteDeployed)
            {
                return;
            }

            parachuteDeployed = true;
            localDropState = LocalDropState.Gliding;
            jumpPlaneForward = GetDropLookForward();
            dropHorizontalVelocity = jumpPlaneForward * dropGlideSpeed;
            if (fallVerticalVelocity < -dropDescentSpeed)
            {
                fallVerticalVelocity = -dropDescentSpeed;
            }

            ApplyWindLoopProfile(isGliding: true);
            localAudioController?.PlayParachuteOpen();
            presenceSync?.FlushLocalPose();
            RefreshHud(lastState);
        }

        private void UpdateFallingMotion()
        {
            if (localPlayer == null || characterController == null || !characterController.enabled)
            {
                return;
            }

            fallVerticalVelocity += freeFallGravity * Time.deltaTime;
            fallVerticalVelocity = Mathf.Max(fallVerticalVelocity, -Mathf.Max(4f, freeFallTerminalSpeed));
            characterController.Move(Vector3.up * (fallVerticalVelocity * Time.deltaTime));

            if (Time.unscaledTime >= nextDropPoseFlushRealtime)
            {
                nextDropPoseFlushRealtime = Time.unscaledTime + Mathf.Max(0.05f, dropPoseFlushInterval);
                presenceSync?.FlushLocalPose();
            }
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
            StopWindLoopAudio();
            localAudioController?.PlayLand(true);
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

        private void UpdateGlidingMotion()
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
            var hits = Physics.RaycastAll(
                origin,
                Vector3.down,
                Mathf.Max(2f, dropGroundProbeMaxDistance),
                ~0,
                QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0)
            {
                return false;
            }

            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (var i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (planeInstance != null && IsPlaneCollider(hit.collider))
                {
                    continue;
                }

                groundPoint = hit.point;
                clearance = GetFeetAltitude() - hit.point.y;
                return true;
            }

            return false;
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

            fpsController?.SetMovementLocked(false);
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
                Destroy(planeCamera.gameObject);
                planeCamera = null;
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
                EnsurePlanePrefabAssigned();
            }

            if (planePrefab == null)
            {
                Debug.LogWarning($"[MatchBattleRoyale] Plane prefab is missing. Expected {PlanePrefabAssetPath}.");
                return;
            }

            planeInstance = Instantiate(planePrefab);
            planeInstance.name = "BattleRoyalePlane";
            planeSeat = new GameObject("PlaneSeat").transform;
            planeSeat.SetParent(planeInstance.transform, false);
            planeSeat.localPosition = planeSeatLocalOffset;
            CachePlaneColliders();
            SetPlaneCollidersEnabled(false);
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
            parachuteDeployed = false;
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

        private void SoftResyncPlaneTiming(RealtimeTransportClient.MatchStateMessage message)
        {
            if (!planeTimingInitialized || message == null || message.planeEndsAtMs <= message.planeStartedAtMs)
            {
                return;
            }

            var serverNowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var serverElapsedSec = Mathf.Max(
                0f,
                (serverNowMs - message.planeStartedAtMs) / 1000f);
            var serverProgress = Mathf.Clamp01(serverElapsedSec / planePathDurationSeconds);
            var localProgress = Mathf.Clamp01(
                (Time.realtimeSinceStartup - planeLocalStartRealtime) / planePathDurationSeconds);
            var progressError = serverProgress - localProgress;
            if (Mathf.Abs(progressError) > 0.02f)
            {
                planeLocalStartRealtime += progressError * planePathDurationSeconds * 0.35f;
            }
        }

        private void SnapPlaneToRouteEnd()
        {
            if (!planeTimingInitialized || planePathDurationSeconds <= 0.001f)
            {
                return;
            }

            planeLocalStartRealtime = Time.realtimeSinceStartup - planePathDurationSeconds;
            ApplyPlaneMotionAtProgress(1f);
        }

        private void SyncPlaneMotion()
        {
            if (planeInstance == null || !planeTimingInitialized)
            {
                return;
            }

            var elapsed = Time.realtimeSinceStartup - planeLocalStartRealtime;
            var progress = Mathf.Clamp01(elapsed / planePathDurationSeconds);
            ApplyPlaneMotionAtProgress(progress);
            UpdatePlaneOrbitCamera();
        }

        private void ApplyPlaneMotionAtProgress(float progress)
        {
            if (planeInstance == null)
            {
                return;
            }

            progress = Mathf.Clamp01(progress);
            var position = Vector3.Lerp(planePathStart, planePathEnd, progress);

            var direction = planePathEnd - planePathStart;
            direction.y = 0f;
            var rotation = direction.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                : planeInstance.transform.rotation;

            planeInstance.transform.SetPositionAndRotation(position, rotation);
        }

        private void CleanupPlane()
        {
            StopWindLoopAudio();
            planeDeparting = false;
            RetireDepartingPlane();
            planeTimingInitialized = false;
            dropHorizontalVelocity = Vector3.zero;

            if (planeCamera != null)
            {
                Destroy(planeCamera.gameObject);
                planeCamera = null;
            }

            planeSeat = null;
        }

        private void RetireDepartingPlane()
        {
            if (planeLoopSource != null)
            {
                planeLoopSource.Stop();
            }

            if (planeInstance != null)
            {
                Destroy(planeInstance);
                planeInstance = null;
            }

            planeLoopSource = null;
            cachedPlaneColliders.Clear();
            planeCollidersSuppressed = false;
            planeSeat = null;
        }

        private void TryRetireDepartingPlane()
        {
            if (planeInstance == null || localPlayer == null)
            {
                return;
            }

            var elapsed = Time.realtimeSinceStartup - planeLocalStartRealtime;
            var progress = Mathf.Clamp01(elapsed / planePathDurationSeconds);
            var distance = Vector3.Distance(localPlayer.position, planeInstance.transform.position);
            var retireDistance = Mathf.Max(planeLoopMinDistance + 1f, planeLoopMaxDistance * 0.9f);
            if (progress >= 1f && distance >= retireDistance)
            {
                planeDeparting = false;
                RetireDepartingPlane();
            }
        }

        private void EnsurePlaneLoopClip()
        {
            if (planeLoopClip == null)
            {
                planeLoopClip = Resources.Load<AudioClip>(PlaneLoopClipResourcesPath);
            }
        }

        private void EnsureWindLoopClip()
        {
            if (windLoopClip == null)
            {
                windLoopClip = Resources.Load<AudioClip>("Sounds/1");
            }
        }

        private void StartPlaneLoopAudio()
        {
            EnsurePlaneLoopClip();
            if (planeLoopClip == null || planeInstance == null)
            {
                return;
            }

            if (planeLoopSource == null)
            {
                var audioObject = new GameObject("PlaneLoopAudio");
                audioObject.transform.SetParent(planeInstance.transform, false);
                planeLoopSource = audioObject.AddComponent<AudioSource>();
                planeLoopSource.playOnAwake = false;
                planeLoopSource.loop = true;
                planeLoopSource.spatialBlend = 1f;
                planeLoopSource.rolloffMode = AudioRolloffMode.Logarithmic;
                planeLoopSource.minDistance = Mathf.Max(1f, planeLoopMinDistance);
                planeLoopSource.maxDistance = Mathf.Max(
                    planeLoopSource.minDistance + 1f,
                    planeLoopMaxDistance);
                planeLoopSource.dopplerLevel = 0.2f;
                planeLoopSource.spread = 55f;
            }

            ApplyPlaneLoopVolume();
            planeLoopSource.clip = planeLoopClip;
            planeLoopSource.pitch = 1f;
            if (!planeLoopSource.isPlaying)
            {
                planeLoopSource.Play();
            }
        }

        private void UpdatePlaneLoopAttenuation()
        {
            if (planeLoopSource == null || !planeLoopSource.isPlaying)
            {
                return;
            }

            ApplyPlaneLoopVolume();
        }

        private void ApplyPlaneLoopVolume()
        {
            if (planeLoopSource == null)
            {
                return;
            }

            var volume = Mathf.Clamp01(planeLoopVolume);
            if (!isOnPlane && localPlayer != null)
            {
                var distance = Vector3.Distance(localPlayer.position, planeLoopSource.transform.position);
                var fade = 1f - Mathf.Clamp01(
                    Mathf.InverseLerp(planeLoopMinDistance, planeLoopMaxDistance, distance));
                fade = Mathf.Pow(Mathf.Max(0f, fade), Mathf.Max(1f, planeLoopDepartFadeExponent));
                volume *= fade;
            }

            planeLoopSource.volume = volume;
        }

        private void StartWindLoopAudio(bool isGliding)
        {
            EnsureWindLoopClip();
            if (windLoopClip == null)
            {
                Debug.LogWarning("[MatchBattleRoyale] Wind clip is missing at Resources/Sounds/1.");
                return;
            }

            if (windLoopSource == null)
            {
                var audioObject = new GameObject("WindLoopAudio");
                audioObject.transform.SetParent(transform, false);
                windLoopSource = audioObject.AddComponent<AudioSource>();
                windLoopSource.playOnAwake = false;
                windLoopSource.loop = true;
                windLoopSource.spatialBlend = 0f;
                windLoopSource.dopplerLevel = 0f;
                windLoopSource.priority = 32;
            }

            windLoopSource.clip = windLoopClip;
            ApplyWindLoopProfile(isGliding);
            if (!windLoopSource.isPlaying)
            {
                windLoopSource.Play();
            }
        }

        private void ApplyWindLoopProfile(bool isGliding)
        {
            if (windLoopSource == null)
            {
                return;
            }

            windLoopSource.volume = Mathf.Clamp01(isGliding ? windGlideVolume : windLoopVolume);
            windLoopSource.pitch = Mathf.Clamp(isGliding ? windGlidePitch : windFreeFallPitch, 0.25f, 2f);
        }

        private void StopWindLoopAudio()
        {
            if (windLoopSource == null)
            {
                return;
            }

            windLoopSource.Stop();
        }

        private void EnsurePlanePrefabAssigned()
        {
            if (planePrefab != null)
            {
                return;
            }

#if UNITY_EDITOR
            planePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlanePrefabAssetPath);
#endif
            if (planePrefab == null)
            {
                planePrefab = Resources.Load<GameObject>(PlanePrefabResourcesPath);
            }
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

        private static bool ReadJumpPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.fKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.F);
#endif
        }

        private static bool ReadParachutePressed()
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
