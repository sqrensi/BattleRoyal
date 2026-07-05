using System;
using System.Collections;
using System.Collections.Generic;
using ShooterPrototype.Matchmaking;
using ShooterPrototype.Network;
using ShooterPrototype.UI;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public sealed class MatchPresenceSync : MonoBehaviour
    {
        [SerializeField] private int syncTickRate = 64;
        [Header("Remote locomotion smoothing")]
        [SerializeField] private float remoteAnimSpeedSmoothTime = 0.12f;
        [SerializeField] private float remoteAnimStopSmoothTime = 0.05f;
        [SerializeField] private float remoteMoveInputSmoothTime = 0.1f;
        [SerializeField] private float remoteMoveStopSmoothTime = 0.05f;

        [Header("Entity interpolation (CS-style)")]
        [SerializeField] private float interpolationBackTimeSeconds = 0.024f;
        [SerializeField] private float interpolationBackTimeMin = 0.016f;
        [SerializeField] private float interpolationBackTimeMax = 0.055f;
        [SerializeField] private int interpolationBackTicks = 4;
        [SerializeField] private bool useAdaptiveInterpolation = true;
        [SerializeField] private bool useSmoothedServerClock = true;
        [SerializeField] private float serverClockSmoothRate = 8f;
        [SerializeField] private bool useFixedLowLatencyInterpolation = false;
        [SerializeField] private float adaptivePingOneWayScale = 0.45f;
        [SerializeField] private float adaptiveJitterMarginSeconds = 0.008f;
        [SerializeField] private int maxAdaptivePingMs = 80;
        [SerializeField] private float networkSmoothMaxDeltaSeconds = 1f / 30f;
        [SerializeField] private int lowLatencyPingThresholdMs = 25;
        [SerializeField] private float extrapolationLimitSeconds = 0.1f;
        [SerializeField] private bool useEntityInterpolation = true;
        [SerializeField] private bool useExtraPositionSmoothing = true;
        [SerializeField] private float remotePositionLerpSpeed = 34f;
        [SerializeField] private float remoteVerticalLerpSpeed = 18f;
        [SerializeField] private float remoteRotationLerpSpeed = 34f;
        [SerializeField] private float remoteHorizontalSmoothTime = 0.055f;
        [SerializeField] private float remoteVerticalSmoothTime = 0.05f;
        [SerializeField] private float remotePlaneLocalLerpSpeed = 18f;
        [SerializeField] private float remoteDirectFollowDistance = 0f;
        [SerializeField] private float remoteMissingGraceSeconds = 1.25f;
        [SerializeField] private float deliberateTeleportSnapDistance = 20f;
        [SerializeField] private float verticalSnapDistance = 1.2f;
        [SerializeField] private float remoteStaleSeconds = 8f;
        [SerializeField] private float snapshotSilenceReconnectSeconds = 12f;
        [SerializeField] private float wsReconnectIntervalSeconds = 2f;
        [SerializeField] private float wsJoinGraceSeconds = 8f;
        [SerializeField] private bool debugRealtimeLogs = false;
        [SerializeField] private string charactersResourcesFolder = "Characters";

        private RealtimeTransportClient realtimeClient;
        private NetworkLauncher networkLauncher;
        private string localTicketId;
        private GameObject remotePlayerPrefab;
        private Coroutine syncCoroutine;
        private double latestServerTimeSeconds;
        private double latestServerTimeReceiptRealtimeSeconds;
        private double latestServerTickRate = 30.0;
        private double latestMovementSampleRate = 64.0;
        private double smoothedServerTimeSeconds;
        private double smoothedClockLastRealtimeSeconds;
        private bool hasSmoothedServerClock;
        private float lastSnapshotReceivedAt;
        private int lastAppliedServerTick = -1;
        private int lastAppliedSnapshotSignature;
        private int lastAppliedMovementSignature;
        private int lastReconciledSelfAuthSampleTick = -1;
        private int lastSnapshotBinaryVersion;
        private float lastConnectRequestAt = -10f;
        private float wsJoinGraceUntilRealtime = -1f;
        private float lastSnapshotDebugAt;
        private float lastSnapshotApplyDebugAt;
        private PlayerWeaponMount localWeaponMount;
        private PlayerWeaponController localWeaponController;
        private PlayerWeaponHolsterController localWeaponHolster;
        private PlayerWeaponLoadout localWeaponLoadout;
        private int localWeaponPickupSeq;
        private PlayerAudioController localAudioController;
        private FpsCharacterController localFpsController;
        private ProceduralLocomotionRig localLocomotionRig;
        private PlayerHealth localHealth;
        private PlayerMedkitController localMedkitController;
        private MatchBattleRoyaleController battleRoyaleController;
        private MatchDuelController duelController;
        private MatchDeathmatchController deathmatchController;
        private string localCharacterModelName = string.Empty;
        private bool transportEventsSubscribed;
        private readonly Dictionary<string, RemoteAvatar> remoteAvatars = new Dictionary<string, RemoteAvatar>();
        private bool remoteAvatarsSuppressed;
        private bool presenceInitialized;
        private float lastRemoteMissingWarningAt = -10f;

        public int LiveMatchPlayerCount
        {
            get
            {
                var fromRemotes = realtimeClient != null && realtimeClient.IsReady
                    ? remoteAvatars.Count + 1
                    : 0;
                var fromLauncher = networkLauncher != null
                    ? networkLauncher.CurrentMatchPlayerCount
                    : 0;
                return Mathf.Max(fromRemotes, fromLauncher);
            }
        }

        public int RemoteAvatarCount => remoteAvatars.Count;

        private sealed class RemoteAvatar
        {
            public GameObject Root;
            public ProceduralLocomotionRig LocomotionRig;
            public PlayerHealth Health;
            public float LastSeenAt;
            public Vector3 LastKnownPosition;
            public float LastKnownYaw;
            public bool HasKnownPose;
            public bool NetworkGrounded = true;
            public int NetworkJumpState;
            public bool NetworkCrouching;
            public bool NetworkSprinting;
            public float NetworkAnimSpeed;
            public float NetworkAnimPhase;
            public float NetworkLookPitch;
            public float NetworkMoveInputX;
            public float NetworkMoveInputZ;
            public float SmoothedNetworkAnimSpeed;
            public float AnimSpeedSmoothVelocity;
            public float SmoothedMoveInputX;
            public float SmoothedMoveInputZ;
            public float MoveInputXSmoothVelocity;
            public float MoveInputZSmoothVelocity;
            public Vector2 HorizontalSmoothVelocity;
            public float VerticalSmoothVelocity;
            public int LastAppliedShotSeq = -1;
            public int LastAppliedReloadSeq = -1;
            public int LastAppliedHitPlayerSeq = -1;
            public int LastAppliedFootstepSeq = -1;
            public bool WasDead;
            public int LastDeathSeq = -1;
            public int LastRevivedDeathSeq = -1;
            public int PendingRespawnSpawnSlot = -1;
            public float PostRespawnTeleportGuardUntil;
            public float LastDriftLogAt;
            public Vector3 ReviveSpawnPosition;
            public bool WasHolstered;
            public bool HadWeapon;
            public int LastAppliedStateTick = -1;
            public string AppliedCharacterModelName;
            public PlayerSkinNetworkState AppliedSkinState;
            public bool HasAppliedSkinState;
            public int LastDebugLoggedWeaponPickupSeq = -1;
            public int LastAppliedWeaponPickupSeq = -1;
            public int LastDebugLoggedSkinSignature;
            public RemoteWeaponPresentation RemoteWeapon;
            public RemoteLookPitchPosture RemotePitchPosture;
            public RemoteMedkitPresentation RemoteMedkit;
            public RemotePlayerShotEffects RemoteShotEffects;
            public PlayerAudioController RemoteAudio;
            public bool PlaneRiding;
            public bool HasSmoothedPlaneLocalPosition;
            public Vector3 SmoothedPlaneLocalPosition;
            public readonly List<PresenceSnapshot> Snapshots = new List<PresenceSnapshot>();
            public string DisplayNickname = string.Empty;
            public int DisplayDuelRating;
        }

        private struct PresenceSnapshot
        {
            public double TimeSeconds;
            public Vector3 Position;
            public float Yaw;
            public Vector3 Velocity;
            public float YawVelocity;
        }

        private struct InterpolatedPose
        {
            public Vector3 Position;
            public float Yaw;
            public Vector3 Velocity;
            public float YawVelocity;
        }

        public void SetSyncTickRate(int tickRate)
        {
            syncTickRate = Mathf.Clamp(tickRate, 10, 128);
        }

        private void SyncRemotePresentationDebugLogs()
        {
            RemoteThirdPersonPlayerBootstrap.DebugLogs = debugRealtimeLogs;
            RemoteWeaponPresentation.DebugLogs = debugRealtimeLogs;
        }

        public void Initialize(NetworkLauncher launcher, RealtimeTransportClient transportClient, string ticketId, GameObject remotePrefab)
        {
            if (presenceInitialized &&
                string.Equals(localTicketId, ticketId, StringComparison.Ordinal) &&
                realtimeClient != null)
            {
                networkLauncher = launcher;
                SubscribeTransportEvents();
                return;
            }

            presenceInitialized = true;
            networkLauncher = launcher;
            realtimeClient = RealtimeTransportClient.Active != null &&
                             (transportClient == null || transportClient == RealtimeTransportClient.Active)
                ? RealtimeTransportClient.Active
                : transportClient;
            localTicketId = ticketId;
            remotePlayerPrefab = remotePrefab;
            lastSnapshotReceivedAt = MonotonicNowSeconds();
            lastAppliedServerTick = -1;
            lastAppliedSnapshotSignature = 0;
            lastReconciledSelfAuthSampleTick = -1;
            hasSmoothedServerClock = false;
            smoothedServerTimeSeconds = 0.0;
            smoothedClockLastRealtimeSeconds = 0.0;
            localWeaponMount = GetComponent<PlayerWeaponMount>();
            localWeaponController = GetComponent<PlayerWeaponController>();
            localWeaponHolster = GetComponent<PlayerWeaponHolsterController>();
            localWeaponLoadout = GetComponent<PlayerWeaponLoadout>();
            ResolveLocalWeaponLoadout();
            localAudioController = GetComponent<PlayerAudioController>();
            localFpsController = GetComponent<FpsCharacterController>();
            localLocomotionRig = GetComponent<ProceduralLocomotionRig>();
            localHealth = GetComponent<PlayerHealth>();
            localMedkitController = GetComponent<PlayerMedkitController>();
            SyncRemotePresentationDebugLogs();
            if (localLocomotionRig == null)
            {
                localLocomotionRig = GetComponentInChildren<ProceduralLocomotionRig>(true);
            }

            localCharacterModelName = CharacterSelectionService.GetSelectedModelName(charactersResourcesFolder);
            PlayerSkinSelectionService.ApplyToPlayer(gameObject, forceReapply: true);
            SyncLocalWeaponLoadoutFromMount();
            SubscribeTransportEvents();
            if (syncCoroutine == null)
            {
                syncCoroutine = StartCoroutine(SyncRoutine());
            }

            if (realtimeClient != null)
            {
                realtimeClient.BeginMatchSession(ticketId);
            }

            var gameHud = FindFirstObjectByType<GameHudController>();
            gameHud?.SetScoreboardLocalTicket(ticketId);

            if (realtimeClient != null && realtimeClient.IsReady)
            {
                SendLocalPose(forceImmediate: true);
            }
        }

        private void SubscribeTransportEvents()
        {
            if (transportEventsSubscribed || realtimeClient == null)
            {
                return;
            }

            realtimeClient.JoinAcknowledged += HandleTransportJoinAcknowledged;
            realtimeClient.PlayerLandReceived += HandleRemotePlayerLand;
            realtimeClient.RespawnReceived += HandleRemoteRespawnBroadcast;
            transportEventsSubscribed = true;
        }

        private void UnsubscribeTransportEvents()
        {
            if (!transportEventsSubscribed || realtimeClient == null)
            {
                transportEventsSubscribed = false;
                return;
            }

            realtimeClient.JoinAcknowledged -= HandleTransportJoinAcknowledged;
            realtimeClient.PlayerLandReceived -= HandleRemotePlayerLand;
            realtimeClient.RespawnReceived -= HandleRemoteRespawnBroadcast;
            transportEventsSubscribed = false;
        }

        private void HandleTransportJoinAcknowledged()
        {
            SendLocalPose(forceImmediate: true);
        }

        private void HandleRemotePlayerLand(RealtimeTransportClient.PlayerLandMessage message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.ticketId))
            {
                return;
            }

            if (string.Equals(message.ticketId, localTicketId, StringComparison.Ordinal))
            {
                return;
            }

            if (!remoteAvatars.TryGetValue(message.ticketId, out var avatar))
            {
                return;
            }

            avatar.RemoteAudio?.PlayLand(false);
        }

        private void OnEnable()
        {
            PlayerSkinOwnershipService.EquipmentChanged += HandleLocalEquipmentChanged;
            TryAutoInitializeFromLauncher();
        }

        private void OnDisable()
        {
            PlayerSkinOwnershipService.EquipmentChanged -= HandleLocalEquipmentChanged;
            UnsubscribeTransportEvents();

            if (syncCoroutine != null)
            {
                StopCoroutine(syncCoroutine);
                syncCoroutine = null;
            }

            ClearRemoteAvatars();
        }

        private void Update()
        {
            if (!presenceInitialized)
            {
                TryAutoInitializeFromLauncher();
            }

            using (NetworkPerformanceMonitor.PollSnapshotMarker.Auto())
            {
                PollLatestSnapshot();
            }

            if (remoteAvatars.Count == 0)
            {
                return;
            }

            var renderTime = GetRenderServerTimeSeconds() - GetInterpolationBackSeconds();
            var now = Time.unscaledTime;
            var staleIds = ListPool<string>.Get();
            foreach (var kv in remoteAvatars)
            {
                var avatar = kv.Value;
                if (avatar.Root == null)
                {
                    staleIds.Add(kv.Key);
                    continue;
                }

                var avatarHealth = avatar.Health;
                if (avatarHealth != null && avatarHealth.IsDead)
                {
                    EnsureRemoteAvatarVisible(avatar);
                    if (now - avatar.LastSeenAt > remoteStaleSeconds)
                    {
                        staleIds.Add(kv.Key);
                    }

                    continue;
                }

                EnsureRemoteAvatarVisible(avatar);

                if (now - avatar.LastSeenAt > remoteMissingGraceSeconds &&
                    avatar.Snapshots.Count == 0 &&
                    !avatar.HasKnownPose)
                {
                    staleIds.Add(kv.Key);
                    continue;
                }

                var targetPose = EvaluatePose(avatar.Snapshots, renderTime, avatar);
                ApplyRemoteTransform(avatar, targetPose.Position, targetPose.Yaw);
                if (EnemyPresentationVisibilityUtility.IsPresentationActive(avatar.Root))
                {
                    DriveRemoteLocomotion(avatar, targetPose);
                }

                TraceRemotePositionDrift(kv.Key, avatar, targetPose, now);

                if (now - avatar.LastSeenAt > remoteStaleSeconds)
                {
                    staleIds.Add(kv.Key);
                }
            }

            for (var i = 0; i < staleIds.Count; i++)
            {
                RemoveAvatar(staleIds[i]);
            }

            ListPool<string>.Release(staleIds);
        }

        private void ApplyRemoteTransform(RemoteAvatar avatar, Vector3 targetPosition, float targetYaw)
        {
            if (avatar?.Root == null)
            {
                return;
            }

            if (ShouldHideRemoteDuringPlanePhase(targetPosition))
            {
                DetachRemoteFromPlane(avatar, avatar.Root.transform);
                if (avatar.Root.activeSelf)
                {
                    avatar.Root.SetActive(false);
                }

                return;
            }

            EnsureRemoteAvatarVisible(avatar);

            DetachRemoteFromPlane(avatar, avatar.Root.transform);

            var rootTransform = avatar.Root.transform;
            var currentPosition = rootTransform.position;
            var delta = targetPosition - currentPosition;
            if (delta.sqrMagnitude >= deliberateTeleportSnapDistance * deliberateTeleportSnapDistance)
            {
                rootTransform.SetPositionAndRotation(
                    targetPosition,
                    Quaternion.Euler(0f, targetYaw, 0f));
                avatar.HorizontalSmoothVelocity = Vector2.zero;
                avatar.VerticalSmoothVelocity = 0f;
                return;
            }

            if (useEntityInterpolation && !useExtraPositionSmoothing)
            {
                rootTransform.SetPositionAndRotation(
                    targetPosition,
                    Quaternion.Euler(0f, targetYaw, 0f));
                avatar.HorizontalSmoothVelocity = Vector2.zero;
                avatar.VerticalSmoothVelocity = 0f;
                return;
            }

            var horizontalCurrent = new Vector2(currentPosition.x, currentPosition.z);
            var horizontalTarget = new Vector2(targetPosition.x, targetPosition.z);
            var horizontalNext = Vector2.SmoothDamp(
                horizontalCurrent,
                horizontalTarget,
                ref avatar.HorizontalSmoothVelocity,
                Mathf.Max(0.001f, remoteHorizontalSmoothTime),
                remotePositionLerpSpeed * 2f,
                GetNetworkSmoothDeltaTime());
            var horizontalNextX = horizontalNext.x;
            var horizontalNextZ = horizontalNext.y;

            var yDelta = Mathf.Abs(targetPosition.y - currentPosition.y);
            var yNext = yDelta >= verticalSnapDistance
                ? targetPosition.y
                : Mathf.SmoothDamp(
                    currentPosition.y,
                    targetPosition.y,
                    ref avatar.VerticalSmoothVelocity,
                    Mathf.Max(0.001f, remoteVerticalSmoothTime),
                    remoteVerticalLerpSpeed * 2f,
                    GetNetworkSmoothDeltaTime());

            rootTransform.position = new Vector3(horizontalNextX, yNext, horizontalNextZ);
            var currentYaw = rootTransform.eulerAngles.y;
            var smoothDelta = GetNetworkSmoothDeltaTime();
            var nextYaw = Mathf.LerpAngle(currentYaw, targetYaw, smoothDelta * remoteRotationLerpSpeed);
            rootTransform.rotation = Quaternion.Euler(0f, nextYaw, 0f);
        }

        private bool ShouldHideRemoteDuringPlanePhase(Vector3 targetPosition)
        {
            if (ActiveMatchContext.IsDeathmatch || ActiveMatchContext.IsDuel)
            {
                return false;
            }

            if (battleRoyaleController == null)
            {
                battleRoyaleController = FindFirstObjectByType<MatchBattleRoyaleController>();
            }

            if (battleRoyaleController == null || !battleRoyaleController.IsPlanePhaseActive)
            {
                return false;
            }

            var planeTransform = battleRoyaleController.PlaneTransform;
            if (planeTransform == null)
            {
                return true;
            }

            var altitudeDelta = targetPosition.y - planeTransform.position.y;
            return altitudeDelta > -10f && altitudeDelta < 24f;
        }

        private static void DetachRemoteFromPlane(RemoteAvatar avatar, Transform rootTransform)
        {
            if (avatar == null || rootTransform == null || !avatar.PlaneRiding)
            {
                return;
            }

            rootTransform.SetParent(null, true);
            avatar.PlaneRiding = false;
            avatar.HasSmoothedPlaneLocalPosition = false;
        }

        private void DriveRemoteLocomotion(RemoteAvatar avatar, InterpolatedPose pose)
        {
            if (avatar?.Root == null)
            {
                return;
            }

            if (avatar.LocomotionRig != null)
            {
                ResolveRemoteMoveInput(avatar, pose, out var moveInputX, out var moveInputZ);

                var dt = Mathf.Max(0.0001f, GetNetworkSmoothDeltaTime());
                var moveTargetMag = moveInputX * moveInputX + moveInputZ * moveInputZ;
                var moveCurrentMag = avatar.SmoothedMoveInputX * avatar.SmoothedMoveInputX +
                                     avatar.SmoothedMoveInputZ * avatar.SmoothedMoveInputZ;
                var moveSmooth = Mathf.Max(
                    0.01f,
                    moveTargetMag < moveCurrentMag ? remoteMoveStopSmoothTime : remoteMoveInputSmoothTime);
                avatar.SmoothedMoveInputX = Mathf.SmoothDamp(
                    avatar.SmoothedMoveInputX,
                    moveInputX,
                    ref avatar.MoveInputXSmoothVelocity,
                    moveSmooth,
                    Mathf.Infinity,
                    dt);
                avatar.SmoothedMoveInputZ = Mathf.SmoothDamp(
                    avatar.SmoothedMoveInputZ,
                    moveInputZ,
                    ref avatar.MoveInputZSmoothVelocity,
                    moveSmooth,
                    Mathf.Infinity,
                    dt);

                var targetAnimSpeed = ResolveRemoteAnimatorSpeed01(avatar, pose);
                var animSpeedSmooth = Mathf.Max(
                    0.01f,
                    targetAnimSpeed < avatar.SmoothedNetworkAnimSpeed
                        ? remoteAnimStopSmoothTime
                        : remoteAnimSpeedSmoothTime);
                avatar.SmoothedNetworkAnimSpeed = Mathf.SmoothDamp(
                    avatar.SmoothedNetworkAnimSpeed,
                    targetAnimSpeed,
                    ref avatar.AnimSpeedSmoothVelocity,
                    animSpeedSmooth,
                    Mathf.Infinity,
                    dt);

                avatar.LocomotionRig.DriveNetworkLocomotionFromVelocity(
                    pose.Velocity,
                    avatar.NetworkGrounded,
                    avatar.NetworkJumpState,
                    avatar.NetworkCrouching,
                    avatar.NetworkSprinting);
                avatar.LocomotionRig.SetNetworkMoveInput(avatar.SmoothedMoveInputX, avatar.SmoothedMoveInputZ);
                avatar.LocomotionRig.SetNetworkAnimationState(
                    avatar.SmoothedNetworkAnimSpeed,
                    avatar.NetworkGrounded,
                    avatar.NetworkJumpState,
                    avatar.NetworkAnimPhase,
                    avatar.NetworkCrouching,
                    avatar.NetworkSprinting);
                avatar.LocomotionRig.SetNetworkLookPitch(avatar.NetworkLookPitch);
            }

            var remoteWeapon = avatar.RemoteWeapon;
            remoteWeapon?.SetNetworkLookPitch(avatar.NetworkLookPitch);
            remoteWeapon?.SetNetworkCrouchState(avatar.NetworkCrouching);

            avatar.RemotePitchPosture?.SetNetworkLookPitch(avatar.NetworkLookPitch);
        }

        private static void ResolveRemoteMoveInput(
            RemoteAvatar avatar,
            InterpolatedPose pose,
            out float moveInputX,
            out float moveInputZ)
        {
            moveInputX = avatar.NetworkMoveInputX;
            moveInputZ = avatar.NetworkMoveInputZ;
            var inputMag = moveInputX * moveInputX + moveInputZ * moveInputZ;
            if (inputMag >= 0.04f)
            {
                var moveDir = new Vector2(moveInputX, moveInputZ);
                if (moveDir.sqrMagnitude > 1f)
                {
                    moveDir.Normalize();
                    moveInputX = moveDir.x;
                    moveInputZ = moveDir.y;
                }

                return;
            }

            if (avatar.NetworkAnimSpeed <= 0.05f)
            {
                moveInputX = 0f;
                moveInputZ = 0f;
                return;
            }

            if (avatar.NetworkSprinting)
            {
                moveInputX = 0f;
                moveInputZ = 1f;
                return;
            }

            var flatVelocity = new Vector3(pose.Velocity.x, 0f, pose.Velocity.z);
            if (flatVelocity.sqrMagnitude >= 0.04f)
            {
                var localDirection = Quaternion.Euler(0f, -pose.Yaw, 0f) * flatVelocity;
                var magnitude = new Vector2(localDirection.x, localDirection.z).magnitude;
                if (magnitude > 0.01f)
                {
                    moveInputX = localDirection.x / magnitude;
                    moveInputZ = localDirection.z / magnitude;
                    return;
                }
            }

            moveInputX = 0f;
            moveInputZ = 0f;
        }

        private static float ResolveRemoteAnimatorSpeed01(RemoteAvatar avatar, InterpolatedPose pose)
        {
            const float walkSpeedReference = 3.3f;
            const float sprintSpeedReference = 5.94f;
            const float crouchSpeedReference = 2.2f;

            var speedReference = avatar.NetworkCrouching
                ? crouchSpeedReference
                : avatar.NetworkSprinting
                    ? sprintSpeedReference
                    : walkSpeedReference;

            var networkSpeed01 = Mathf.Clamp(avatar.NetworkAnimSpeed, 0f, ProceduralLocomotionRig.MaxNetworkAnimSpeed01);
            var inputMag = avatar.NetworkMoveInputX * avatar.NetworkMoveInputX + avatar.NetworkMoveInputZ * avatar.NetworkMoveInputZ;
            if (networkSpeed01 <= 0.05f && inputMag < 0.04f)
            {
                return 0f;
            }

            if (inputMag >= 0.04f)
            {
                return networkSpeed01;
            }

            var horizontalSpeed = new Vector3(pose.Velocity.x, 0f, pose.Velocity.z).magnitude;

            var velocitySpeed01 = Mathf.Clamp01(horizontalSpeed / Mathf.Max(0.01f, speedReference));

            if (horizontalSpeed <= 0.05f)
            {
                return networkSpeed01;
            }

            if (networkSpeed01 <= 0.05f)
            {
                return velocitySpeed01;
            }

            var desync = Mathf.Abs(velocitySpeed01 - networkSpeed01);
            if (desync <= 0.12f)
            {
                return networkSpeed01;
            }

            return Mathf.Clamp(
                Mathf.Lerp(networkSpeed01, velocitySpeed01, 0.35f),
                0f,
                ProceduralLocomotionRig.MaxNetworkAnimSpeed01);
        }

        public void FlushLocalPose()
        {
            SendLocalPose(forceImmediate: true);
        }

        public void SetRemoteAvatarsVisible(bool visible)
        {
            remoteAvatarsSuppressed = !visible;
            ApplyRemoteAvatarSuppressionState();
        }

        public void ApplyScoreboardNicknames(RealtimeTransportClient.DmScoreboardRowMessage[] rows)
        {
            if (rows == null || rows.Length == 0)
            {
                return;
            }

            for (var i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                if (row == null ||
                    string.IsNullOrWhiteSpace(row.ticketId) ||
                    string.IsNullOrWhiteSpace(row.nickname))
                {
                    continue;
                }

                if (remoteAvatars.TryGetValue(row.ticketId, out var avatar))
                {
                    avatar.DisplayNickname = row.nickname.Trim();
                }
            }
        }

        public void SnapAllRemoteAvatarsToLastKnownPose()
        {
            foreach (var kv in remoteAvatars)
            {
                var avatar = kv.Value;
                if (avatar?.Root == null || !avatar.HasKnownPose)
                {
                    continue;
                }

                SnapRemoteAvatarToPose(avatar, avatar.LastKnownPosition, avatar.LastKnownYaw);
            }
        }

        public bool TryGetDuelOpponentDisplay(
            string localTicketId,
            out string nickname,
            out int duelRating)
        {
            nickname = string.Empty;
            duelRating = 0;

            foreach (var pair in remoteAvatars)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) ||
                    string.Equals(pair.Key, localTicketId, StringComparison.Ordinal))
                {
                    continue;
                }

                var avatar = pair.Value;
                if (avatar == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(avatar.DisplayNickname))
                {
                    nickname = avatar.DisplayNickname.Trim();
                }

                if (avatar.DisplayDuelRating > 0)
                {
                    duelRating = avatar.DisplayDuelRating;
                }

                if (!string.IsNullOrWhiteSpace(nickname))
                {
                    return true;
                }
            }

            return false;
        }

        public void SnapAllRemoteAvatarsToDuelSpawn(int teamIndex, int slotIndex)
        {
            if (!DuelSpawnUtility.TryResolveSpawnPose(teamIndex, slotIndex, out var position, out var rotation))
            {
                return;
            }

            var yaw = rotation.eulerAngles.y;
            foreach (var kv in remoteAvatars)
            {
                var avatar = kv.Value;
                if (avatar?.Root == null)
                {
                    continue;
                }

                SnapRemoteAvatarToPose(avatar, position, yaw);
                avatar.LastKnownPosition = position;
                avatar.LastKnownYaw = yaw;
                avatar.HasKnownPose = true;
            }
        }

        private void ReconcileRemoteAvatarAfterRespawn(RemoteAvatar avatar, Vector3 position, float yaw, int revivedDeathSeq = -1)
        {
            if (avatar == null)
            {
                return;
            }

            if (ActiveMatchContext.IsDeathmatch)
            {
                position = DmSpawnUtility.GroundFeetPosition(position);
            }

            if (revivedDeathSeq >= 0)
            {
                avatar.LastRevivedDeathSeq = revivedDeathSeq;
                avatar.LastDeathSeq = revivedDeathSeq;
            }

            avatar.ReviveSpawnPosition = position;
            avatar.PostRespawnTeleportGuardUntil = Time.unscaledTime + 0.75f;
            avatar.Snapshots.Clear();
            avatar.LastAppliedStateTick = -1;
            avatar.HorizontalSmoothVelocity = Vector2.zero;
            avatar.VerticalSmoothVelocity = 0f;
            avatar.LastKnownPosition = position;
            avatar.LastKnownYaw = yaw;
            avatar.HasKnownPose = true;

            if (avatar.Root != null)
            {
                EnsureRemoteAvatarVisible(avatar);
                RemotePlayerLocomotionUtility.RestoreNetworkRemoteLocomotion(avatar.Root);
            }

            SnapRemoteAvatarToPose(avatar, position, yaw);
        }

        private void HandleRemoteRespawnBroadcast(RealtimeTransportClient.RespawnMessage message)
        {
            if (message == null ||
                string.IsNullOrWhiteSpace(message.ticketId) ||
                !ActiveMatchContext.IsDeathmatch)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(localTicketId) &&
                string.Equals(message.ticketId, localTicketId, StringComparison.Ordinal))
            {
                return;
            }

            if (!remoteAvatars.TryGetValue(message.ticketId, out var avatar))
            {
                return;
            }

            if (!avatar.WasDead && message.deathSeq <= avatar.LastDeathSeq)
            {
                DmRespawnTrace.Log(
                    "respawn-ws-skip",
                    $"ticket={message.ticketId} reason=duplicate deathSeq={message.deathSeq} last={avatar.LastDeathSeq}");
                return;
            }

            avatar.PendingRespawnSpawnSlot = message.spawnSlotIndex;
            avatar.LastDeathSeq = message.deathSeq;

            if (!DmSpawnUtility.TryResolveSpawnPose(
                    message.spawnSlotIndex,
                    out var position,
                    out var rotation))
            {
                return;
            }

            avatar.Health?.SetNetworkDeadState(false, message.deathSeq, Vector3.forward);
            avatar.WasDead = false;
            ReconcileRemoteAvatarAfterRespawn(avatar, position, rotation.eulerAngles.y, message.deathSeq);
            LogDmRespawnRemote(
                avatar,
                new RealtimeTransportClient.RealtimePlayerState
                {
                    ticketId = message.ticketId,
                    deathSeq = message.deathSeq,
                    position = new RealtimeTransportClient.PositionDto
                    {
                        x = position.x,
                        y = position.y,
                        z = position.z,
                    },
                    isDead = false,
                },
                "respawn-ws",
                avatar.LastDeathSeq);
        }

        private void HandleRemoteDeathSeqTransition(
            RemoteAvatar avatar,
            RealtimeTransportClient.RealtimePlayerState player)
        {
            if (avatar == null || player == null)
            {
                return;
            }

            if (player.deathSeq == avatar.LastDeathSeq)
            {
                return;
            }

            if (player.deathSeq < avatar.LastDeathSeq)
            {
                return;
            }

            if (avatar.LastDeathSeq < 0)
            {
                avatar.LastDeathSeq = player.deathSeq;
                avatar.WasDead = player.isDead;
                return;
            }

            var previousDeathSeq = avatar.LastDeathSeq;
            avatar.LastDeathSeq = player.deathSeq;
            var fallDirection = new Vector3(player.deathFallDirX, player.deathFallDirY, player.deathFallDirZ);

            if (player.isDead)
            {
                avatar.Health?.SetNetworkDeadState(true, player.deathSeq, fallDirection);
                avatar.WasDead = true;
                avatar.Snapshots.Clear();
                avatar.LastAppliedStateTick = -1;
                LogDmRespawnRemote(avatar, player, "death", previousDeathSeq);
                return;
            }

            avatar.Health?.SetNetworkDeadState(false, player.deathSeq, fallDirection);
            avatar.WasDead = false;
            var revivedPosition = new Vector3(player.position.x, player.position.y, player.position.z);
            var revivedYaw = player.yaw;
            if (avatar.PendingRespawnSpawnSlot >= 0 &&
                DmSpawnUtility.TryResolveSpawnPose(
                    avatar.PendingRespawnSpawnSlot,
                    out var groundedPosition,
                    out var groundedRotation))
            {
                revivedPosition = groundedPosition;
                revivedYaw = groundedRotation.eulerAngles.y;
                avatar.PendingRespawnSpawnSlot = -1;
            }

            ReconcileRemoteAvatarAfterRespawn(avatar, revivedPosition, revivedYaw, player.deathSeq);
            LogDmRespawnRemote(avatar, player, "respawn", previousDeathSeq);
        }

        private void LogDmRespawnRemote(
            RemoteAvatar avatar,
            RealtimeTransportClient.RealtimePlayerState player,
            string phase,
            int previousDeathSeq)
        {
            if ((!DmRespawnTrace.Enabled && !debugRealtimeLogs) || avatar?.Root == null || player == null)
            {
                return;
            }

            var root = avatar.Root.transform.position;
            var message =
                $"remote-{phase} ticket={player.ticketId} " +
                $"deathSeq {previousDeathSeq}->{player.deathSeq} isDead={player.isDead} " +
                $"pos=({player.position.x:F2},{player.position.y:F2},{player.position.z:F2}) " +
                $"WasDead={avatar.WasDead} LastAppliedStateTick={avatar.LastAppliedStateTick} " +
                $"buffer={avatar.Snapshots.Count} guardUntil={avatar.PostRespawnTeleportGuardUntil:F2} " +
                $"root=({root.x:F2},{root.y:F2},{root.z:F2})";

            if (DmRespawnTrace.Enabled)
            {
                DmRespawnTrace.Log(phase, message);
            }
            else
            {
                Debug.Log($"[DMRespawn] {message}");
            }
        }

        private void TraceRemotePositionDrift(
            string ticketId,
            RemoteAvatar avatar,
            InterpolatedPose targetPose,
            float now)
        {
            if (!DmRespawnTrace.Enabled || !ActiveMatchContext.IsDeathmatch || avatar?.Root == null)
            {
                return;
            }

            var rootPos = avatar.Root.transform.position;
            var driftFromLastKnown = Vector3.Distance(rootPos, avatar.LastKnownPosition);
            var driftFromInterp = Vector3.Distance(rootPos, targetPose.Position);
            if (driftFromLastKnown < 1.5f && driftFromInterp < 1.5f)
            {
                return;
            }

            DmRespawnTrace.LogThrottled(
                ref avatar.LastDriftLogAt,
                0.5f,
                "remote-drift",
                $"ticket={ticketId} deathSeq={avatar.LastDeathSeq} revived={avatar.LastRevivedDeathSeq} " +
                $"root=({rootPos.x:F2},{rootPos.y:F2},{rootPos.z:F2}) " +
                $"lastKnown=({avatar.LastKnownPosition.x:F2},{avatar.LastKnownPosition.y:F2},{avatar.LastKnownPosition.z:F2}) " +
                $"interp=({targetPose.Position.x:F2},{targetPose.Position.y:F2},{targetPose.Position.z:F2}) " +
                $"driftKnown={driftFromLastKnown:F2} driftInterp={driftFromInterp:F2} " +
                $"buffer={avatar.Snapshots.Count} guardActive={now < avatar.PostRespawnTeleportGuardUntil}");
        }

        private void LogIngestSkip(
            RemoteAvatar avatar,
            RealtimeTransportClient.RealtimePlayerState player,
            string reason)
        {
            if (!DmRespawnTrace.Enabled || avatar == null || player == null)
            {
                return;
            }

            var root = avatar.Root != null ? avatar.Root.transform.position : Vector3.zero;
            DmRespawnTrace.Log(
                "ingest-skip",
                $"ticket={player.ticketId} reason={reason} deathSeq={player.deathSeq} isDead={player.isDead} " +
                $"sampleTick={player.sampleTick} lastApplied={avatar.LastAppliedStateTick} " +
                $"revived={avatar.LastRevivedDeathSeq} guardActive={Time.unscaledTime < avatar.PostRespawnTeleportGuardUntil} " +
                $"root=({root.x:F2},{root.y:F2},{root.z:F2}) buffer={avatar.Snapshots.Count}");
        }

        private static void SnapRemoteAvatarToPose(RemoteAvatar avatar, Vector3 position, float yaw)
        {
            if (avatar?.Root == null)
            {
                return;
            }

            avatar.Snapshots.Clear();
            avatar.HorizontalSmoothVelocity = Vector2.zero;
            avatar.VerticalSmoothVelocity = 0f;
            avatar.HasSmoothedPlaneLocalPosition = false;
            avatar.Root.transform.SetPositionAndRotation(
                position,
                Quaternion.Euler(0f, yaw, 0f));
        }

        private bool ShouldSkipIngestDuringPostRespawnGuard(
            RemoteAvatar avatar,
            RealtimeTransportClient.RealtimePlayerState player)
        {
            if (avatar == null || player == null || player.position == null)
            {
                return false;
            }

            if (Time.unscaledTime >= avatar.PostRespawnTeleportGuardUntil)
            {
                return false;
            }

            var snapPos = new Vector3(player.position.x, player.position.y, player.position.z);
            if (avatar.ReviveSpawnPosition == Vector3.zero)
            {
                return false;
            }

            // Only block snapshots that would teleport back to a stale pre-respawn position.
            return Vector3.Distance(snapPos, avatar.ReviveSpawnPosition) >= deliberateTeleportSnapDistance;
        }

        private bool ShouldSnapRemoteTeleport(RemoteAvatar avatar, Vector3 position)
        {
            if (avatar?.Root == null)
            {
                return false;
            }

            if (Time.unscaledTime < avatar.PostRespawnTeleportGuardUntil)
            {
                return false;
            }

            if (avatar.Snapshots.Count > 0)
            {
                var lastSnapshot = avatar.Snapshots[avatar.Snapshots.Count - 1].Position;
                if (Vector3.Distance(lastSnapshot, position) >= deliberateTeleportSnapDistance)
                {
                    return true;
                }
            }
            else if (Vector3.Distance(avatar.Root.transform.position, position) >= deliberateTeleportSnapDistance)
            {
                return true;
            }

            return false;
        }

        private void ApplyRemoteAvatarSuppressionState()
        {
            foreach (var kv in remoteAvatars)
            {
                if (kv.Value?.Root != null)
                {
                    kv.Value.Root.SetActive(!remoteAvatarsSuppressed);
                }
            }
        }

        private void EnsureRemoteAvatarVisible(RemoteAvatar avatar)
        {
            if (remoteAvatarsSuppressed || avatar?.Root == null)
            {
                return;
            }

            if (!avatar.Root.activeSelf)
            {
                avatar.Root.SetActive(true);
            }
        }

        private void HandleLocalEquipmentChanged()
        {
            PlayerSkinSelectionService.ApplyToPlayer(gameObject, forceReapply: true);
            localWeaponMount?.RefreshEquippedWeaponSkin();
            SendLocalPose(forceImmediate: true);
        }

        public void ResetSelfAuthoritativeReconcileCursor()
        {
            lastReconciledSelfAuthSampleTick = -1;
        }

        public void AcknowledgeWeaponPickupSeq(int weaponPickupSeq)
        {
            if (weaponPickupSeq > localWeaponPickupSeq)
            {
                localWeaponPickupSeq = weaponPickupSeq;
            }
        }

        private void ResolveLocalWeaponLoadout()
        {
            if (localWeaponLoadout == null)
            {
                localWeaponLoadout = GetComponent<PlayerWeaponLoadout>();
            }

            if (localWeaponLoadout == null)
            {
                var loadoutController = GetComponent<PlayerWeaponLoadoutController>();
                localWeaponLoadout = loadoutController != null ? loadoutController.Loadout : null;
            }
        }

        private void SyncLocalWeaponLoadoutFromMount()
        {
            ResolveLocalWeaponLoadout();
            if (localWeaponLoadout == null || localWeaponLoadout.HasAnyWeapon)
            {
                return;
            }

            if (localWeaponMount == null || !localWeaponMount.HasMountedWeapon)
            {
                return;
            }

            var kind = localWeaponController != null
                ? localWeaponController.CurrentWeaponKind
                : localWeaponMount.ActiveWeaponProfile != null
                    ? localWeaponMount.ActiveWeaponProfile.Kind
                    : WeaponKind.AssaultRifle;
            var holstered = localWeaponHolster != null && localWeaponHolster.IsHolstered;
            localWeaponLoadout.TrySeedFromMountedWeapon(
                kind,
                WeaponCatalog.GetDefaultItemId(kind),
                holstered);
        }

        private void ResolveLocalWeaponPoseState(
            out bool isHolstered,
            out bool hasWeapon,
            out int weaponKind,
            out byte weaponSlot0Kind,
            out byte weaponSlot1Kind,
            out int activeWeaponSlot)
        {
            if (deathmatchController == null)
            {
                deathmatchController = FindFirstObjectByType<MatchDeathmatchController>();
            }

            if (deathmatchController != null &&
                ActiveMatchContext.IsDeathmatch &&
                !deathmatchController.ShouldSendWeaponInNetworkPose)
            {
                isHolstered = true;
                hasWeapon = false;
                weaponKind = 0;
                weaponSlot0Kind = PlayerWeaponLoadout.EmptySlotKind;
                weaponSlot1Kind = PlayerWeaponLoadout.EmptySlotKind;
                activeWeaponSlot = PlayerWeaponLoadout.NoActiveSlot;
                return;
            }

            SyncLocalWeaponLoadoutFromMount();

            var loadoutReady = localWeaponLoadout != null && localWeaponLoadout.HasAnyWeapon;
            hasWeapon = loadoutReady ||
                        (localWeaponMount != null && localWeaponMount.HasMountedWeapon);

            var mountedKind = localWeaponController != null
                ? localWeaponController.CurrentWeaponKind
                : localWeaponMount != null && localWeaponMount.ActiveWeaponProfile != null
                    ? localWeaponMount.ActiveWeaponProfile.Kind
                    : WeaponKind.AssaultRifle;

            isHolstered = localWeaponHolster != null && localWeaponHolster.IsHolstered;
            if (loadoutReady)
            {
                if (localWeaponMount != null &&
                    localWeaponMount.HasMountedWeapon &&
                    localWeaponHolster != null)
                {
                    isHolstered = localWeaponHolster.IsHolstered;
                    if (!isHolstered && localWeaponLoadout.IsBothHolstered)
                    {
                        localWeaponLoadout.SetBothHolstered(false);
                    }
                }
                else
                {
                    isHolstered = localWeaponLoadout.IsBothHolstered;
                }
            }

            weaponSlot0Kind = PlayerWeaponLoadout.EmptySlotKind;
            weaponSlot1Kind = PlayerWeaponLoadout.EmptySlotKind;
            activeWeaponSlot = PlayerWeaponLoadout.NoActiveSlot;

            weaponKind = hasWeapon ? (int)mountedKind : 0;
            if (loadoutReady)
            {
                weaponSlot0Kind = localWeaponLoadout.EncodeSlotKind(0);
                weaponSlot1Kind = localWeaponLoadout.EncodeSlotKind(1);
                activeWeaponSlot = isHolstered
                    ? PlayerWeaponLoadout.NoActiveSlot
                    : localWeaponLoadout.ActiveSlotIndex;

                if (!isHolstered &&
                    activeWeaponSlot >= 0 &&
                    activeWeaponSlot <= 1 &&
                    localWeaponLoadout.IsSlotOccupied(activeWeaponSlot))
                {
                    weaponKind = (int)localWeaponLoadout.GetSlot(activeWeaponSlot).Kind;
                }
                else if (localWeaponLoadout.IsSlotOccupied(0))
                {
                    weaponKind = (int)localWeaponLoadout.GetSlot(0).Kind;
                }
                else if (localWeaponLoadout.IsSlotOccupied(1))
                {
                    weaponKind = (int)localWeaponLoadout.GetSlot(1).Kind;
                }
            }
            else if (hasWeapon)
            {
                weaponSlot0Kind = WeaponKindUtility.ClampKindByte(weaponKind);
                weaponSlot1Kind = PlayerWeaponLoadout.EmptySlotKind;
                activeWeaponSlot = isHolstered ? PlayerWeaponLoadout.NoActiveSlot : 0;
            }
        }

        private static bool ShouldApplyRemoteLoadout(RealtimeTransportClient.RealtimePlayerState player)
        {
            if (player == null)
            {
                return false;
            }

            if (player.weaponSlot0Kind != PlayerWeaponLoadout.EmptySlotKind ||
                player.weaponSlot1Kind != PlayerWeaponLoadout.EmptySlotKind)
            {
                return true;
            }

            return player.hasWeapon &&
                   player.weaponKind >= 0 &&
                   player.weaponKind <= (int)WeaponKind.Mp7;
        }

        private static WeaponKind ResolveRemoteActiveWeaponKind(RealtimeTransportClient.RealtimePlayerState player)
        {
            if (player == null)
            {
                return WeaponKind.AssaultRifle;
            }

            if (player.isHolstered || player.activeWeaponSlot == PlayerWeaponLoadout.NoActiveSlot)
            {
                return WeaponKindUtility.ClampKind(player.weaponKind);
            }

            if (player.activeWeaponSlot == 0 &&
                player.weaponSlot0Kind != PlayerWeaponLoadout.EmptySlotKind)
            {
                return WeaponKindUtility.ClampKind(player.weaponSlot0Kind);
            }

            if (player.activeWeaponSlot == 1 &&
                player.weaponSlot1Kind != PlayerWeaponLoadout.EmptySlotKind)
            {
                return WeaponKindUtility.ClampKind(player.weaponSlot1Kind);
            }

            return WeaponKindUtility.ClampKind(player.weaponKind);
        }

        private void SendLocalPose(bool forceImmediate = false)
        {
            if (realtimeClient == null ||
                string.IsNullOrWhiteSpace(localTicketId) ||
                !realtimeClient.IsConnected)
            {
                return;
            }

            ResolveLocalWeaponLoadout();

            if (localLocomotionRig == null)
            {
                localLocomotionRig = GetComponentInChildren<ProceduralLocomotionRig>(true);
            }

            var currentPos = transform.position;
            var currentYaw = transform.eulerAngles.y;
            var shotSeq = localWeaponController != null ? localWeaponController.LastShotSequence : 0;
            var reloadSeq = localWeaponController != null ? localWeaponController.LastReloadSequence : 0;
            var hitPlayerSeq = localWeaponController != null ? localWeaponController.LastHitPlayerSequence : 0;
            var footstepSeq = localFpsController != null ? localFpsController.LastFootstepSequence : 0;
            var isCrouching = localFpsController != null && localFpsController.IsCrouching;
            var isSprinting = localFpsController != null && localFpsController.IsSprinting;
            var wallAvoidBlend = localWeaponMount != null ? localWeaponMount.CurrentWallAvoidBlend : 0f;
            var isDead = localHealth != null && localHealth.IsDead;
            var deathSeq = localHealth != null ? localHealth.DeathSequence : 0;
            var deathFallDirection = localHealth != null ? localHealth.DeathFallDirection : Vector3.forward;
            var lookPitch = localFpsController != null ? localFpsController.CurrentLookPitch : 0f;
            var shotOrigin = localWeaponController != null ? localWeaponController.LastShotOrigin : Vector3.zero;
            var shotDirection = localWeaponController != null ? localWeaponController.LastShotDirection : Vector3.zero;
            var shotEndPoint = localWeaponController != null ? localWeaponController.LastShotEndPoint : Vector3.zero;
            var shotHasEndPoint = localWeaponController != null && localWeaponController.LastShotHasEndPoint;
            if (!shotHasEndPoint && localWeaponController != null && shotSeq > 0)
            {
                var travel = (shotEndPoint - shotOrigin).magnitude;
                if (travel > 0.05f && travel <= localWeaponController.ShotMaxDistance + 1f)
                {
                    shotHasEndPoint = true;
                }
            }
            SyncLocalWeaponLoadoutFromMount();
            ResolveLocalWeaponPoseState(
                out var isHolstered,
                out var hasWeapon,
                out var weaponKind,
                out var weaponSlot0Kind,
                out var weaponSlot1Kind,
                out var activeWeaponSlot);
            var activeWeaponMagAmmo = -1;
            if (localWeaponLoadout != null &&
                activeWeaponSlot >= 0 &&
                activeWeaponSlot <= 1 &&
                localWeaponLoadout.IsSlotOccupied(activeWeaponSlot))
            {
                activeWeaponMagAmmo = localWeaponLoadout.GetSlotMagAmmo(activeWeaponSlot);
                if (localWeaponController != null &&
                    localWeaponMount != null &&
                    localWeaponMount.HasMountedWeapon &&
                    !isHolstered)
                {
                    activeWeaponMagAmmo = localWeaponController.CurrentAmmo;
                }
            }

            var animSpeed = localLocomotionRig != null
                ? localLocomotionRig.GetNetworkAnimSpeed01()
                : (localFpsController != null ? Mathf.Clamp01(localFpsController.MoveInputMagnitude) : 0f);
            var animGrounded = localLocomotionRig != null
                ? localLocomotionRig.CurrentGrounded
                : (localFpsController == null || localFpsController.IsGrounded);
            var animJumpState = localLocomotionRig != null
                ? localLocomotionRig.CurrentJumpState
                : (animGrounded ? 0 : 2);
            var animPhase = localLocomotionRig != null
                ? localLocomotionRig.CurrentAnimPhase01
                : 0f;
            var inputAuth = !isDead &&
                            localFpsController != null &&
                            !localFpsController.IsMovementLocked;
            var moveInputX = inputAuth ? localFpsController.NetworkMoveInputX : 0f;
            var moveInputZ = inputAuth ? localFpsController.NetworkMoveInputZ : 0f;
            var jumpPressed = inputAuth && localFpsController.NetworkJumpPressed;
            if (string.IsNullOrWhiteSpace(localCharacterModelName))
            {
                localCharacterModelName = CharacterSelectionService.GetSelectedModelName(charactersResourcesFolder);
            }

            using (NetworkPerformanceMonitor.SendLocalPoseMarker.Auto())
            {
                realtimeClient.SendPose(
                    currentPos,
                    currentYaw,
                    localCharacterModelName,
                    PlayerSkinSelectionService.CaptureLocalNetworkState(),
                    lookPitch,
                    shotSeq,
                    reloadSeq,
                    hitPlayerSeq,
                    footstepSeq,
                    isCrouching,
                    isSprinting,
                    wallAvoidBlend,
                    isDead,
                    deathSeq,
                    deathFallDirection,
                    false,
                    isHolstered,
                    animSpeed,
                    animGrounded,
                    animJumpState,
                    animPhase,
                    moveInputX,
                    moveInputZ,
                    jumpPressed,
                    inputAuth,
                    shotOrigin,
                    shotDirection,
                    shotEndPoint,
                    shotHasEndPoint,
                    weaponKind,
                    weaponSlot0Kind,
                    weaponSlot1Kind,
                    activeWeaponSlot,
                    activeWeaponMagAmmo,
                    localWeaponPickupSeq,
                    forceImmediate);
            }

            if (MovementNetworkDiagnostics.Enabled)
            {
                MovementNetworkDiagnostics.LogPoseSend(
                    currentPos,
                    moveInputX,
                    moveInputZ,
                    inputAuth,
                    isDead,
                    weaponSlot0Kind,
                    weaponSlot1Kind);
            }
        }

        public void SendLocalPoseImmediate()
        {
            SendLocalPose(forceImmediate: true);
        }

        private IEnumerator SyncRoutine()
        {
            while (true)
            {
                if (realtimeClient != null &&
                    !string.IsNullOrWhiteSpace(localTicketId))
                {
                    realtimeClient.SetAutoReconnectEnabled(!ShouldSkipTransportReconnect());
                    realtimeClient.EnsureConnected();

                    if (localLocomotionRig == null)
                    {
                        localLocomotionRig = GetComponentInChildren<ProceduralLocomotionRig>(true);
                    }

                    SendLocalPose();
                }

                yield return new WaitForSecondsRealtime(1f / Mathf.Clamp(syncTickRate, 10, 128));
            }
        }

        private void PollLatestSnapshot()
        {
            if (realtimeClient == null)
            {
                return;
            }

            RealtimeTransportClient.RealtimeSnapshot latestSnapshot = null;
            while (realtimeClient.TryDequeueSnapshot(out var snapshot) && snapshot != null)
            {
                if (latestSnapshot == null ||
                    snapshot.serverTick > latestSnapshot.serverTick ||
                    (snapshot.serverTick == latestSnapshot.serverTick &&
                     snapshot.players != null &&
                     (latestSnapshot.players == null || snapshot.players.Length >= latestSnapshot.players.Length)))
                {
                    latestSnapshot = snapshot;
                }
            }

            if (latestSnapshot != null)
            {
                ApplySnapshotIfNew(latestSnapshot);
            }
        }

        private void ApplySnapshotIfNew(RealtimeTransportClient.RealtimeSnapshot snapshot)
        {
            var serverTick = snapshot.serverTick;
            var signature = ComputeSnapshotPresenceSignature(snapshot);
            var movementSignature = ComputeSnapshotMovementSignature(snapshot);
            var snapshotPlayerCount = snapshot.players != null ? snapshot.players.Length : 0;
            var needsNewRemoteAvatar = snapshotPlayerCount > remoteAvatars.Count;
            if (serverTick > 0 &&
                serverTick == lastAppliedServerTick &&
                signature == lastAppliedSnapshotSignature &&
                movementSignature == lastAppliedMovementSignature &&
                !needsNewRemoteAvatar)
            {
                lastSnapshotReceivedAt = MonotonicNowSeconds();
                lastSnapshotBinaryVersion = snapshot.binaryVersion;
                return;
            }

            if (debugRealtimeLogs)
            {
                var sameTick = serverTick > 0 && serverTick == lastAppliedServerTick;
                if (sameTick || Time.unscaledTime - lastSnapshotApplyDebugAt >= 1f)
                {
                    lastSnapshotApplyDebugAt = Time.unscaledTime;
                    var reason = sameTick ? "same-tick-signature-change" : "new-server-tick";
                    Debug.Log(
                        $"[MatchPresenceSync] apply snapshot tick={serverTick} sig={signature} " +
                        $"prevTick={lastAppliedServerTick} prevSig={lastAppliedSnapshotSignature} " +
                        $"reason={reason} players={snapshot.players?.Length ?? 0} binVer={snapshot.binaryVersion}");
                }
            }

            if (serverTick > 0)
            {
                lastAppliedServerTick = serverTick;
            }

            lastAppliedSnapshotSignature = signature;
            lastAppliedMovementSignature = movementSignature;
            ApplyRealtimeSnapshot(snapshot);
        }

        private static int ComputeSnapshotMovementSignature(RealtimeTransportClient.RealtimeSnapshot snapshot)
        {
            if (snapshot?.players == null || snapshot.players.Length == 0)
            {
                return 0;
            }

            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + snapshot.serverTick;
                for (var i = 0; i < snapshot.players.Length; i++)
                {
                    var player = snapshot.players[i];
                    if (player?.position == null)
                    {
                        continue;
                    }

                    hash = (hash * 31) + player.sampleTick;
                    hash = (hash * 31) + QuantizeMovementComponent(player.position.x);
                    hash = (hash * 31) + QuantizeMovementComponent(player.position.y);
                    hash = (hash * 31) + QuantizeMovementComponent(player.position.z);
                    hash = (hash * 31) + QuantizeMovementComponent(player.yaw);
                    hash = (hash * 31) + QuantizeMovementComponent(player.velX);
                    hash = (hash * 31) + QuantizeMovementComponent(player.velY);
                    hash = (hash * 31) + QuantizeMovementComponent(player.velZ);
                    if (player.history != null && player.history.Length > 0)
                    {
                        hash = (hash * 31) + player.history.Length;
                        var lastHistory = player.history[player.history.Length - 1];
                        if (lastHistory != null)
                        {
                            hash = (hash * 31) + lastHistory.sampleTick;
                        }
                    }
                }

                return hash;
            }
        }

        private static int QuantizeMovementComponent(float value)
        {
            return Mathf.RoundToInt(value * 20f);
        }

        private static string ShortTicketId(string ticketId)
        {
            if (string.IsNullOrWhiteSpace(ticketId))
            {
                return "?";
            }

            return ticketId.Length <= 6 ? ticketId : ticketId.Substring(0, 6);
        }

        private void LogRemoteWeaponApply(
            RemoteAvatar avatar,
            RealtimeTransportClient.RealtimePlayerState player,
            string path)
        {
            if (!debugRealtimeLogs || avatar == null || player == null)
            {
                return;
            }

            if (player.weaponPickupSeq == avatar.LastDebugLoggedWeaponPickupSeq)
            {
                return;
            }

            avatar.LastDebugLoggedWeaponPickupSeq = player.weaponPickupSeq;
            var weaponRoot = avatar.RemoteWeapon != null ? avatar.RemoteWeapon.WeaponRoot : null;
            Debug.Log(
                $"[MatchPresenceSync] remote weapon ticket={ShortTicketId(player.ticketId)} path={path} " +
                $"pickSeq={player.weaponPickupSeq} hasWeapon={player.hasWeapon} holstered={player.isHolstered} " +
                $"slots={player.weaponSlot0Kind},{player.weaponSlot1Kind} active={player.activeWeaponSlot} " +
                $"kind={player.weaponKind} weaponRoot={(weaponRoot != null ? weaponRoot.name : "null")}");
        }

        private void LogRemoteSkinApply(
            RemoteAvatar avatar,
            RealtimeTransportClient.RealtimePlayerState player,
            PlayerSkinNetworkState skinState,
            string action)
        {
            if (!debugRealtimeLogs || avatar == null || player == null)
            {
                return;
            }

            var skinSignature = ComputeSkinStateSignature(skinState);
            if (action == "skip" && skinSignature == avatar.LastDebugLoggedSkinSignature)
            {
                return;
            }

            avatar.LastDebugLoggedSkinSignature = skinSignature;
            Debug.Log(
                $"[MatchPresenceSync] remote skin {action} ticket={ShortTicketId(player.ticketId)} " +
                $"shirt={skinState.ShirtId} pants={skinState.PantsId} boots={skinState.BootsId} " +
                $"gloves={skinState.GlovesId} face={skinState.FaceId} hair={skinState.HairId} " +
                $"wpn={skinState.WeaponAssaultId}/{skinState.WeaponSniperId}/" +
                $"{skinState.WeaponPistolId}/{skinState.WeaponMp7Id} model={player.characterModel}");
        }

        private static int ComputeSkinStateSignature(in PlayerSkinNetworkState skinState)
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + HashSkinField(skinState.ShirtId);
                hash = (hash * 31) + HashSkinField(skinState.PantsId);
                hash = (hash * 31) + HashSkinField(skinState.BootsId);
                hash = (hash * 31) + HashSkinField(skinState.GlovesId);
                hash = (hash * 31) + HashSkinField(skinState.FaceId);
                hash = (hash * 31) + HashSkinField(skinState.HairId);
                hash = (hash * 31) + HashSkinField(skinState.WeaponAssaultId);
                hash = (hash * 31) + HashSkinField(skinState.WeaponSniperId);
                hash = (hash * 31) + HashSkinField(skinState.WeaponPistolId);
                hash = (hash * 31) + HashSkinField(skinState.WeaponMp7Id);
                return hash;
            }
        }

        private static int ComputeSnapshotPresenceSignature(RealtimeTransportClient.RealtimeSnapshot snapshot)
        {
            if (snapshot?.players == null || snapshot.players.Length == 0)
            {
                return 0;
            }

            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + snapshot.players.Length;
                for (var i = 0; i < snapshot.players.Length; i++)
                {
                    var player = snapshot.players[i];
                    if (player == null)
                    {
                        continue;
                    }

                    hash = (hash * 31) + (player.ticketId?.GetHashCode(StringComparison.Ordinal) ?? 0);
                    hash = (hash * 31) + player.weaponPickupSeq;
                    hash = (hash * 31) + (player.hasWeapon ? 1 : 0);
                    hash = (hash * 31) + player.weaponKind;
                    hash = (hash * 31) + player.weaponSlot0Kind;
                    hash = (hash * 31) + player.weaponSlot1Kind;
                    hash = (hash * 31) + player.activeWeaponSlot;
                    hash = (hash * 31) + (player.isHolstered ? 1 : 0);
                    hash = (hash * 31) + HashSkinField(player.characterModel);
                    hash = (hash * 31) + HashSkinField(player.skinShirt);
                    hash = (hash * 31) + HashSkinField(player.skinPants);
                    hash = (hash * 31) + HashSkinField(player.skinBoots);
                    hash = (hash * 31) + HashSkinField(player.skinGloves);
                    hash = (hash * 31) + HashSkinField(player.skinFace);
                    hash = (hash * 31) + HashSkinField(player.skinHair);
                    hash = (hash * 31) + HashSkinField(player.skinWeaponAssault);
                    hash = (hash * 31) + HashSkinField(player.skinWeaponSniper);
                    hash = (hash * 31) + HashSkinField(player.skinWeaponPistol);
                    hash = (hash * 31) + HashSkinField(player.skinWeaponMp7);
                    hash = (hash * 31) + (player.isDead ? 1 : 0);
                    hash = (hash * 31) + player.deathSeq;
                }

                return hash;
            }
        }

        private static int HashSkinField(string value)
        {
            return string.IsNullOrEmpty(value)
                ? 0
                : StringComparer.OrdinalIgnoreCase.GetHashCode(value);
        }

        private void ApplyRemoteWeaponPresence(RealtimeTransportClient.RealtimePlayerState[] players)
        {
            if (players == null)
            {
                return;
            }

            for (var i = 0; i < players.Length; i++)
            {
                var p = players[i];
                if (p == null || string.IsNullOrWhiteSpace(p.ticketId) ||
                    !remoteAvatars.TryGetValue(p.ticketId, out var avatar) ||
                    avatar?.Root == null)
                {
                    continue;
                }

                var remoteWeapon = avatar.RemoteWeapon;
                if (remoteWeapon == null)
                {
                    continue;
                }

                if (ShouldApplyRemoteLoadout(p))
                {
                    remoteWeapon.SetWeaponLoadout(
                        (byte)Mathf.Clamp(p.weaponSlot0Kind, 0, 255),
                        (byte)Mathf.Clamp(p.weaponSlot1Kind, 0, 255),
                        p.activeWeaponSlot,
                        p.isHolstered,
                        p.hasWeapon,
                        WeaponKindUtility.ClampKindByte(p.weaponKind));
                }
                else
                {
                    remoteWeapon.SetWeaponEquipped(p.hasWeapon);
                    if (p.hasWeapon)
                    {
                        remoteWeapon.SetWeaponKind(ResolveRemoteActiveWeaponKind(p));
                    }

                    remoteWeapon.SetHolstered(p.isHolstered);
                }

                ApplyRemoteWeaponEffects(avatar, ResolveRemoteActiveWeaponKind(p));
                MaybeStopRemoteReloadOnWeaponInterrupt(avatar, p);
            }
        }

        private void ApplyRemotePresenceEvents(RealtimeTransportClient.RealtimePlayerState[] players)
        {
            if (players == null)
            {
                return;
            }

            for (var i = 0; i < players.Length; i++)
            {
                var p = players[i];
                if (p == null || string.IsNullOrWhiteSpace(p.ticketId))
                {
                    continue;
                }

                if (!remoteAvatars.TryGetValue(p.ticketId, out var avatar))
                {
                    continue;
                }

                ApplyRemotePresenceEventsForAvatar(avatar, p);
            }
        }

        private void ApplyRemotePresenceEventsForAvatar(
            RemoteAvatar avatar,
            RealtimeTransportClient.RealtimePlayerState playerState)
        {
            TryPlayRemoteShots(avatar, playerState);
            TryPlayRemoteReload(avatar, playerState);
            TryPlayRemoteHitPlayer(avatar, playerState);
            TryPlayRemoteFootsteps(avatar, playerState);
        }

        private void ApplyRealtimeSnapshot(RealtimeTransportClient.RealtimeSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            lastSnapshotReceivedAt = MonotonicNowSeconds();

            if (snapshot.serverTickRate > 0)
            {
                latestServerTickRate = snapshot.serverTickRate;
            }
            else if (realtimeClient != null && realtimeClient.LatestServerTickRate > 0)
            {
                latestServerTickRate = realtimeClient.LatestServerTickRate;
            }

            if (snapshot.movementSampleRateHz > 0)
            {
                latestMovementSampleRate = snapshot.movementSampleRateHz;
            }
            else if (realtimeClient != null && realtimeClient.LatestMovementSampleRate > 0)
            {
                latestMovementSampleRate = realtimeClient.LatestMovementSampleRate;
            }
            else
            {
                latestMovementSampleRate = syncTickRate;
            }

            latestServerTimeSeconds = snapshot.serverTick > 0
                ? snapshot.serverTick / Math.Max(1.0, latestServerTickRate)
                : Time.realtimeSinceStartupAsDouble;
            latestServerTimeReceiptRealtimeSeconds = Time.realtimeSinceStartupAsDouble;
            lastSnapshotBinaryVersion = snapshot.binaryVersion;

            ApplyRemotePresence(snapshot.players);
            ApplyLocalMedkitFromSnapshot(snapshot.players);
            ApplySelfAuthoritativePose(snapshot.selfAuthoritative);
            var remoteCount = snapshot.players != null ? snapshot.players.Length : 0;
            if (remoteCount > 0)
            {
                SetRemoteAvatarsVisible(true);
            }

            if (remoteCount > 0 &&
                remoteAvatars.Count == 0 &&
                Time.unscaledTime - lastRemoteMissingWarningAt > 3f)
            {
                lastRemoteMissingWarningAt = Time.unscaledTime;
                Debug.LogWarning(
                    $"[MatchPresenceSync] snapshot listed {remoteCount} remote player(s) but created 0 avatars " +
                    $"(ticket={ShortTicketId(localTicketId)} tick={snapshot.serverTick}).");
            }
            networkLauncher?.SetCurrentMatchPlayerCount(remoteCount + 1);
            if (debugRealtimeLogs && Time.unscaledTime - lastSnapshotDebugAt >= 1f)
            {
                lastSnapshotDebugAt = Time.unscaledTime;
                Debug.Log($"[MatchPresenceSync] snapshot players={remoteCount} avatars={remoteAvatars.Count} tick={snapshot.serverTick}");
            }
        }

        private double GetEstimatedServerTimeSeconds()
        {
            if (latestServerTimeReceiptRealtimeSeconds <= 0.0)
            {
                return latestServerTimeSeconds > 0.0
                    ? latestServerTimeSeconds
                    : Time.realtimeSinceStartupAsDouble;
            }

            var elapsedSinceSnapshot = Time.realtimeSinceStartupAsDouble - latestServerTimeReceiptRealtimeSeconds;
            return latestServerTimeSeconds + Math.Max(0.0, elapsedSinceSnapshot);
        }

        private void AdvanceSmoothedServerClock()
        {
            if (!useSmoothedServerClock)
            {
                return;
            }

            var nowRealtime = Time.realtimeSinceStartupAsDouble;
            var target = GetEstimatedServerTimeSeconds();
            if (!hasSmoothedServerClock)
            {
                smoothedServerTimeSeconds = target;
                smoothedClockLastRealtimeSeconds = nowRealtime;
                hasSmoothedServerClock = true;
                return;
            }

            var dt = Math.Max(0.0, nowRealtime - smoothedClockLastRealtimeSeconds);
            smoothedClockLastRealtimeSeconds = nowRealtime;
            smoothedServerTimeSeconds += dt;

            var error = target - smoothedServerTimeSeconds;
            var smoothRate = Math.Max(1.0, serverClockSmoothRate);
            var correction = error * (1.0 - Math.Exp(-smoothRate * dt));
            smoothedServerTimeSeconds += correction;
        }

        private double GetRenderServerTimeSeconds()
        {
            AdvanceSmoothedServerClock();
            if (useSmoothedServerClock && hasSmoothedServerClock)
            {
                return smoothedServerTimeSeconds;
            }

            return GetEstimatedServerTimeSeconds();
        }

        private double GetMovementSampleRate()
        {
            if (latestMovementSampleRate > 0.0)
            {
                return latestMovementSampleRate;
            }

            if (realtimeClient != null && realtimeClient.LatestMovementSampleRate > 0)
            {
                return realtimeClient.LatestMovementSampleRate;
            }

            return Math.Max(1.0, syncTickRate);
        }

        private float GetInterpolationBackSeconds()
        {
            var tickRate = GetMovementSampleRate();
            var backByTicks = interpolationBackTicks / (float)Math.Max(1.0, tickRate);
            if (useFixedLowLatencyInterpolation)
            {
                return Mathf.Max(interpolationBackTimeMin, backByTicks);
            }

            var backSeconds = interpolationBackTimeSeconds;
            if (useAdaptiveInterpolation)
            {
                var pingMs = realtimeClient != null && realtimeClient.SmoothedRoundTripMs > 0
                    ? realtimeClient.SmoothedRoundTripMs
                    : (networkLauncher != null ? networkLauncher.LastMeasuredPingMs : -1);
                if (pingMs > 0)
                {
                    pingMs = Mathf.Min(pingMs, maxAdaptivePingMs);
                    if (pingMs <= lowLatencyPingThresholdMs)
                    {
                        backSeconds = interpolationBackTimeMin;
                    }
                    else
                    {
                        var oneWaySeconds = pingMs * 0.001f * Mathf.Clamp(adaptivePingOneWayScale, 0.35f, 0.65f);
                        backSeconds = oneWaySeconds + Mathf.Max(0f, adaptiveJitterMarginSeconds);
                    }
                }
            }

            backSeconds = Mathf.Clamp(backSeconds, interpolationBackTimeMin, interpolationBackTimeMax);
            backSeconds = Mathf.Max(backSeconds, backByTicks);

            var fps = 1f / Mathf.Max(Time.unscaledDeltaTime, 0.001f);
            if (fps < 50f)
            {
                backSeconds *= Mathf.Clamp(fps / 50f, 0.45f, 1f);
            }

            return backSeconds;
        }

        private static float MonotonicNowSeconds()
        {
            return RealtimeTransportClient.MonotonicNowSeconds;
        }

        private float GetNetworkSmoothDeltaTime()
        {
            var maxDelta = Mathf.Max(1f / 120f, networkSmoothMaxDeltaSeconds);
            return Mathf.Min(Time.unscaledDeltaTime, maxDelta);
        }

        private bool ShouldSuppressLocalPoseReconcile()
        {
            if (battleRoyaleController == null)
            {
                battleRoyaleController = FindFirstObjectByType<MatchBattleRoyaleController>();
            }

            if (duelController == null)
            {
                duelController = FindFirstObjectByType<MatchDuelController>();
            }

            if (deathmatchController == null)
            {
                deathmatchController = FindFirstObjectByType<MatchDeathmatchController>();
            }

            if (battleRoyaleController != null && battleRoyaleController.ShouldSuppressPoseReconcile)
            {
                return true;
            }

            if (deathmatchController != null && deathmatchController.ShouldSuppressPoseReconcile)
            {
                return true;
            }

            return duelController != null && duelController.ShouldSuppressPoseReconcile;
        }

        private bool ShouldSkipTransportReconnect()
        {
            if (battleRoyaleController == null)
            {
                battleRoyaleController = FindFirstObjectByType<MatchBattleRoyaleController>();
            }

            if (battleRoyaleController != null && battleRoyaleController.ShouldSkipTransportReconnect)
            {
                return true;
            }

            return false;
        }

        private bool ShouldSkipInputAuthorityReconcile()
        {
            if (localFpsController == null || localFpsController.IsMovementLocked)
            {
                return false;
            }

            if (localHealth != null && localHealth.IsDead)
            {
                return false;
            }

            return true;
        }

        private void ApplySelfAuthoritativePose(RealtimeTransportClient.SelfAuthoritativePose selfPose)
        {
            if (selfPose == null || selfPose.position == null || localFpsController == null)
            {
                if (MovementNetworkDiagnostics.Enabled)
                {
                    MovementNetworkDiagnostics.LogSnapshotSelfAuth(
                        false,
                        transform.position,
                        Vector3.zero,
                        lastAppliedServerTick,
                        lastSnapshotBinaryVersion,
                        0,
                        realtimeClient != null
                            ? MonotonicNowSeconds() - realtimeClient.LastSnapshotReceivedUnscaledTime
                            : -1f);
                }

                return;
            }

            if (localHealth != null && localHealth.IsDead)
            {
                return;
            }

            if (realtimeClient == null || !realtimeClient.IsReady)
            {
                return;
            }

            if (!Application.isFocused)
            {
                return;
            }

            if (ShouldSuppressLocalPoseReconcile())
            {
                return;
            }

            if (ShouldSkipInputAuthorityReconcile())
            {
                if (selfPose.sampleTick > 0)
                {
                    lastReconciledSelfAuthSampleTick = selfPose.sampleTick;
                }

                return;
            }

            if (selfPose.sampleTick > 0 && selfPose.sampleTick <= lastReconciledSelfAuthSampleTick)
            {
                return;
            }

            var serverPos = new Vector3(selfPose.position.x, selfPose.position.y, selfPose.position.z);
            if (MovementNetworkDiagnostics.Enabled)
            {
                MovementNetworkDiagnostics.LogSnapshotSelfAuth(
                    true,
                    transform.position,
                    serverPos,
                    selfPose.sampleTick,
                    lastSnapshotBinaryVersion,
                    0,
                    realtimeClient != null
                        ? MonotonicNowSeconds() - realtimeClient.LastSnapshotReceivedUnscaledTime
                        : -1f);
            }

            var roundTripMs = realtimeClient != null ? realtimeClient.SmoothedRoundTripMs : 0;
            localFpsController.ReconcileToServer(serverPos, selfPose.yaw, selfPose.sampleTick, roundTripMs);
            if (selfPose.sampleTick > 0)
            {
                lastReconciledSelfAuthSampleTick = selfPose.sampleTick;
            }
        }

        private void IngestPlayerStateSamples(RemoteAvatar avatar, RealtimeTransportClient.RealtimePlayerState player)
        {
            if (avatar == null || player == null)
            {
                return;
            }

            if (avatar.LastDeathSeq >= 0 && player.deathSeq < avatar.LastDeathSeq)
            {
                LogIngestSkip(avatar, player, "stale_death_seq");
                return;
            }

            if (player.isDead && avatar.LastRevivedDeathSeq >= player.deathSeq && !avatar.WasDead)
            {
                LogIngestSkip(avatar, player, "stale_death_snapshot");
                return;
            }

            if (ShouldSkipIngestDuringPostRespawnGuard(avatar, player))
            {
                LogIngestSkip(avatar, player, "post_respawn_guard_stale_pos");
                return;
            }

            var tickRate = GetMovementSampleRate();

            tickRate = Math.Max(1.0, tickRate);

            if (player.position != null &&
                player.sampleTick > avatar.LastAppliedStateTick &&
                ShouldSnapRemoteTeleport(
                    avatar,
                    new Vector3(player.position.x, player.position.y, player.position.z)))
            {
                var latestPosition = new Vector3(player.position.x, player.position.y, player.position.z);
                DmRespawnTrace.Log(
                    "ingest-snap",
                    $"ticket={player.ticketId} sampleTick={player.sampleTick} deathSeq={player.deathSeq} " +
                    $"pos=({latestPosition.x:F2},{latestPosition.y:F2},{latestPosition.z:F2})");
                SnapRemoteAvatarToPose(avatar, latestPosition, player.yaw);
                var timeSeconds = player.sampleTick / tickRate;
                var velocity = new Vector3(player.velX, player.velY, player.velZ);
                AddSnapshot(avatar.Snapshots, timeSeconds, latestPosition, player.yaw, velocity, true);
                avatar.LastAppliedStateTick = player.sampleTick;
                return;
            }

            if (player.history != null)
            {
                for (var i = 0; i < player.history.Length; i++)
                {
                    var sample = player.history[i];
                    if (sample == null || sample.sampleTick <= avatar.LastAppliedStateTick)
                    {
                        continue;
                    }

                    var timeSeconds = sample.sampleTick / tickRate;
                    var position = new Vector3(sample.x, sample.y, sample.z);
                    if (ShouldSnapRemoteTeleport(avatar, position))
                    {
                        SnapRemoteAvatarToPose(avatar, position, sample.yaw);
                    }

                    var velocity = new Vector3(sample.velX, sample.velY, sample.velZ);
                    AddSnapshot(avatar.Snapshots, timeSeconds, position, sample.yaw, velocity, true);
                    avatar.LastAppliedStateTick = sample.sampleTick;
                }
            }

            if (player.sampleTick > avatar.LastAppliedStateTick && player.position != null)
            {
                var timeSeconds = player.sampleTick / tickRate;
                var position = new Vector3(player.position.x, player.position.y, player.position.z);
                var velocity = new Vector3(player.velX, player.velY, player.velZ);
                AddSnapshot(avatar.Snapshots, timeSeconds, position, player.yaw, velocity, true);
                avatar.LastAppliedStateTick = player.sampleTick;
            }
        }

        private void ApplyLocalMedkitFromSnapshot(RealtimeTransportClient.RealtimePlayerState[] players)
        {
            if (localMedkitController == null || string.IsNullOrWhiteSpace(localTicketId) || players == null)
            {
                return;
            }

            for (var i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (player == null || string.IsNullOrWhiteSpace(player.ticketId))
                {
                    continue;
                }

                if (!string.Equals(player.ticketId, localTicketId, StringComparison.Ordinal))
                {
                    continue;
                }

                localMedkitController.SyncFromSnapshot(player, lastSnapshotBinaryVersion >= 8);
                return;
            }
        }

        private void ApplyRemotePresence(RealtimeTransportClient.RealtimePlayerState[] players)
        {
            var now = Time.unscaledTime;

            if (players != null)
            {
                for (var i = 0; i < players.Length; i++)
                {
                    var p = players[i];
                    if (p == null || string.IsNullOrWhiteSpace(p.ticketId) || p.position == null)
                    {
                        continue;
                    }

                    if (!remoteAvatars.TryGetValue(p.ticketId, out var avatar))
                    {
                        var initialPosition = new Vector3(p.position.x, p.position.y, p.position.z);
                        avatar = CreateAvatar(p.ticketId, initialPosition, p.yaw);
                        avatar.LastAppliedShotSeq = Mathf.Max(0, p.shotSeq);
                        avatar.LastAppliedReloadSeq = Mathf.Max(0, p.reloadSeq);
                        avatar.LastAppliedHitPlayerSeq = Mathf.Max(0, p.hitPlayerSeq);
                        avatar.LastAppliedFootstepSeq = Mathf.Max(0, p.footstepSeq);
                        avatar.LastDeathSeq = p.deathSeq;
                        avatar.WasDead = p.isDead;
                        avatar.SmoothedNetworkAnimSpeed = p.animSpeed;
                        avatar.SmoothedMoveInputX = p.moveInputX;
                        avatar.SmoothedMoveInputZ = p.moveInputZ;
                        remoteAvatars[p.ticketId] = avatar;
                    }

                    if (avatar.LastDeathSeq >= 0 && p.deathSeq < avatar.LastDeathSeq)
                    {
                        continue;
                    }

                    if (p.isDead && avatar.LastRevivedDeathSeq >= p.deathSeq && !avatar.WasDead)
                    {
                        continue;
                    }

                    HandleRemoteDeathSeqTransition(avatar, p);

                    avatar.LastSeenAt = now;
                    if (!string.IsNullOrWhiteSpace(p.nickname))
                    {
                        avatar.DisplayNickname = p.nickname.Trim();
                    }
                    else if (MatchScoreboardTracker.TryGetEntry(p.ticketId, out var scoreEntry) &&
                             !string.IsNullOrWhiteSpace(scoreEntry.Nickname))
                    {
                        avatar.DisplayNickname = scoreEntry.Nickname;
                    }

                    if (p.duelRating > 0)
                    {
                        avatar.DisplayDuelRating = p.duelRating;
                    }

                    var samplePosition = new Vector3(p.position.x, p.position.y, p.position.z);
                    avatar.LastKnownPosition = samplePosition;
                    avatar.LastKnownYaw = p.yaw;
                    avatar.HasKnownPose = true;
                    avatar.NetworkGrounded = p.isGrounded;
                    avatar.NetworkJumpState = p.jumpState;
                    avatar.NetworkCrouching = p.isCrouching;
                    avatar.NetworkSprinting = p.isSprinting;
                    avatar.NetworkAnimSpeed = p.animSpeed;
                    avatar.NetworkAnimPhase = p.animPhase;
                    avatar.NetworkLookPitch = p.lookPitch;
                    avatar.NetworkMoveInputX = p.moveInputX;
                    avatar.NetworkMoveInputZ = p.moveInputZ;
                    avatar.LocomotionRig?.SetNetworkLookPitch(p.lookPitch);
                    IngestPlayerStateSamples(avatar, p);
                    ApplyRemoteCharacterModel(avatar, p.characterModel);

                    if (p.weaponPickupSeq != avatar.LastAppliedWeaponPickupSeq)
                    {
                        avatar.LastAppliedWeaponPickupSeq = p.weaponPickupSeq;
                        avatar.RemoteWeapon?.ResetAppliedLoadout();
                        avatar.HasAppliedSkinState = false;
                    }

                    ApplyRemoteSkins(avatar, p);

                    var remoteWeapon = avatar.RemoteWeapon;
                    remoteWeapon?.SetNetworkLookPitch(p.lookPitch);
                    remoteWeapon?.SetNetworkCrouchState(p.isCrouching);
                    if (ShouldApplyRemoteLoadout(p))
                    {
                        remoteWeapon?.SetWeaponLoadout(
                            (byte)Mathf.Clamp(p.weaponSlot0Kind, 0, 255),
                            (byte)Mathf.Clamp(p.weaponSlot1Kind, 0, 255),
                            p.activeWeaponSlot,
                            p.isHolstered,
                            p.hasWeapon,
                            WeaponKindUtility.ClampKindByte(p.weaponKind));
                        LogRemoteWeaponApply(avatar, p, "loadout");
                    }
                    else
                    {
                        remoteWeapon?.SetWeaponEquipped(p.hasWeapon);
                        if (p.hasWeapon)
                        {
                            remoteWeapon?.SetWeaponKind(ResolveRemoteActiveWeaponKind(p));
                        }

                        remoteWeapon?.SetHolstered(p.isHolstered);
                        LogRemoteWeaponApply(avatar, p, "legacy");
                    }

                    remoteWeapon?.RefreshAllWeaponSkins();

                    ApplyRemoteWeaponEffects(avatar, ResolveRemoteActiveWeaponKind(p));
                    MaybeStopRemoteReloadOnWeaponInterrupt(avatar, p);
                    avatar.RemoteMedkit?.SetNetworkMedkitState(p.isUsingMedkit);

                    avatar.RemotePitchPosture?.SetNetworkLookPitch(p.lookPitch);

                    ApplyRemotePresenceEventsForAvatar(avatar, p);

                    if (!(p.isDead && avatar.LastRevivedDeathSeq >= p.deathSeq && !avatar.WasDead))
                    {
                        avatar.Health?.SetNetworkDeadState(
                            p.isDead,
                            p.deathSeq,
                            new Vector3(p.deathFallDirX, p.deathFallDirY, p.deathFallDirZ));
                        avatar.WasDead = p.isDead;
                    }

                    if (p.isDead)
                    {
                        EnsureRemoteAvatarVisible(avatar);
                    }
                }
            }

        }

        private RemoteAvatar CreateAvatar(string ticketId, Vector3 initialPosition, float initialYaw)
        {
            GameObject root;
            if (remotePlayerPrefab != null)
            {
                root = Instantiate(remotePlayerPrefab);
            }
            else
            {
                root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Destroy(root.GetComponent<Collider>());
            }

            root.name = $"Remote_{ticketId.Substring(0, Mathf.Min(6, ticketId.Length))}";
            root.transform.position = initialPosition;
            root.transform.rotation = Quaternion.Euler(0f, initialYaw, 0f);
            if (debugRealtimeLogs)
            {
                Debug.Log($"[MatchPresenceSync] create avatar ticket={ticketId}");
            }

            EnsureAvatarHasFallbackModel(root);

            var bootstrap = root.GetComponent<RemoteThirdPersonPlayerBootstrap>();
            if (bootstrap != null)
            {
                bootstrap.ApplyRemoteThirdPersonMode();
            }
            else
            {
                var thirdPersonBody = root.transform.Find("ThirdPersonBody");
                var remoteWeapon = root.GetComponent<RemoteWeaponPresentation>();
                if (remoteWeapon == null)
                {
                    remoteWeapon = root.AddComponent<RemoteWeaponPresentation>();
                }

                remoteWeapon.Configure(thirdPersonBody);
                remoteWeapon.ClearEmbeddedPrefabWeapons();
                EnsureRemoteVisuals(root);
            }
            RefreshRemoteAvatarHitboxes(root);

            var identity = root.GetComponent<PlayerNetworkIdentity>();
            if (identity == null)
            {
                identity = root.AddComponent<PlayerNetworkIdentity>();
            }
            identity.Configure(ticketId, false);

            if (root.GetComponent<RemotePlayerShotEffects>() == null)
            {
                root.AddComponent<RemotePlayerShotEffects>();
            }

            var shotEffects = root.GetComponent<RemotePlayerShotEffects>();
            if (shotEffects != null && localWeaponController != null)
            {
                shotEffects.ApplyVisualSettings(
                    null,
                    localWeaponController.WorldHitVfx,
                    localWeaponController.PlayerHitVfx,
                    localWeaponController.ShotMaxDistance);
                shotEffects.ApplyForWeaponKind(WeaponKind.AssaultRifle);
            }

            EnsureRemoteAudio(root);

            if (remoteAvatarsSuppressed)
            {
                root.SetActive(false);
            }

            var health = root.GetComponent<PlayerHealth>();
            if (health == null)
            {
                health = root.AddComponent<PlayerHealth>();
            }
            health.SetNetworkMode(true);
            health.SetNetworkDeadState(false, 0, Vector3.forward);

            RemotePlayerLocomotionUtility.EnsureNetworkRemoteLocomotion(root);

            var locomotionRig = root.GetComponentInChildren<ProceduralLocomotionRig>(true);

            var selfSync = root.GetComponent<MatchPresenceSync>();
            if (selfSync != null)
            {
                Destroy(selfSync);
            }

            // Keep CharacterController enabled on remote avatars so they are hittable by raycasts.

            var avatar = new RemoteAvatar
            {
                Root = root,
                LocomotionRig = locomotionRig,
                Health = health,
                LastSeenAt = Time.unscaledTime,
                LastKnownPosition = initialPosition,
                LastKnownYaw = initialYaw,
                HasKnownPose = true,
                AppliedCharacterModelName = string.Empty,
                AppliedSkinState = default,
                HasAppliedSkinState = false,
                HadWeapon = false,
                WasHolstered = true
            };
            CacheRemoteAvatarComponents(avatar);
            return avatar;
        }

        private static void CacheRemoteAvatarComponents(RemoteAvatar avatar)
        {
            if (avatar?.Root == null)
            {
                return;
            }

            avatar.RemoteWeapon = avatar.Root.GetComponent<RemoteWeaponPresentation>();
            avatar.RemotePitchPosture = avatar.Root.GetComponent<RemoteLookPitchPosture>();
            avatar.RemoteMedkit = avatar.Root.GetComponent<RemoteMedkitPresentation>();
            avatar.RemoteShotEffects = avatar.Root.GetComponent<RemotePlayerShotEffects>();
            avatar.RemoteAudio = avatar.Root.GetComponent<PlayerAudioController>();
        }

        private void ApplyRemoteCharacterModel(RemoteAvatar avatar, string modelName)
        {
            if (avatar?.Root == null || string.IsNullOrWhiteSpace(modelName))
            {
                return;
            }

            if (string.Equals(avatar.AppliedCharacterModelName, modelName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var modelAsset = CharacterSelectionService.FindModelByName(charactersResourcesFolder, modelName);
            if (modelAsset == null)
            {
                return;
            }

            if (CharacterModelApplier.TryApplyToPlayer(avatar.Root, modelAsset))
            {
                avatar.AppliedCharacterModelName = modelName;
                avatar.HasAppliedSkinState = false;
                CacheRemoteAvatarComponents(avatar);
                avatar.RemoteWeapon?.RebindThirdPersonBody(avatar.Root.transform.Find("ThirdPersonBody"));
                if (debugRealtimeLogs)
                {
                    var ticketId = avatar.Root.GetComponent<PlayerNetworkIdentity>()?.TicketId ?? "?";
                    Debug.Log(
                        $"[MatchPresenceSync] remote model applied ticket={ShortTicketId(ticketId)} model={modelName}");
                }
            }
        }

        private void ApplyRemoteSkins(RemoteAvatar avatar, RealtimeTransportClient.RealtimePlayerState player)
        {
            if (avatar?.Root == null || player == null)
            {
                return;
            }

            if (!CharacterModelApplier.HasCharacterBody(avatar.Root))
            {
                EnsureAvatarHasFallbackModel(avatar.Root);
                CacheRemoteAvatarComponents(avatar);
                avatar.RemoteWeapon?.RebindThirdPersonBody(avatar.Root.transform.Find("ThirdPersonBody"));
            }

            if (lastSnapshotBinaryVersion > 0 && lastSnapshotBinaryVersion < 11)
            {
                LogRemoteSkinApply(avatar, player, default, "skip-binver");
                return;
            }

            var skinState = new PlayerSkinNetworkState(
                player.skinShirt,
                player.skinPants,
                player.skinBoots,
                player.skinGloves,
                player.skinFace,
                player.skinHair,
                player.skinWeaponAssault,
                player.skinWeaponSniper,
                player.skinWeaponPistol,
                player.skinWeaponMp7);
            if (avatar.HasAppliedSkinState && skinState.Equals(avatar.AppliedSkinState))
            {
                if (!HasRemoteClothingVisual(avatar.Root))
                {
                    avatar.HasAppliedSkinState = false;
                }
                else
                {
                    RefreshRemoteBodyClipping(avatar.Root);
                    LogRemoteSkinApply(avatar, player, skinState, "skip-unchanged");
                    return;
                }
            }

            StartCoroutine(ApplyRemoteSkinsWhenReady(avatar, player, skinState));
        }

        private IEnumerator ApplyRemoteSkinsWhenReady(
            RemoteAvatar avatar,
            RealtimeTransportClient.RealtimePlayerState player,
            PlayerSkinNetworkState skinState)
        {
            if (avatar?.Root == null || player == null)
            {
                yield break;
            }

            PlayerSkinSelectionService.ApplyNetworkStateToPlayer(
                avatar.Root,
                skinState,
                forceReapply: !avatar.HasAppliedSkinState || !avatar.AppliedSkinState.Equals(skinState));
            avatar.RemoteWeapon?.SetNetworkWeaponSkins(skinState);
            avatar.AppliedSkinState = skinState;
            avatar.HasAppliedSkinState = true;
            LogRemoteSkinApply(avatar, player, skinState, "apply-async");
        }

        private static bool HasRemoteClothingVisual(GameObject avatarRoot)
        {
            if (avatarRoot == null)
            {
                return false;
            }

            var thirdPersonBody = avatarRoot.transform.Find("ThirdPersonBody");
            var syntyVisual = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
            return syntyVisual != null && syntyVisual.Find(RemoteResourceClothingApplier.RemoteClothingRootName) != null;
        }

        private static void RefreshRemoteBodyClipping(GameObject avatarRoot)
        {
            if (avatarRoot == null || MainMenuPlayerPreview.IsMenuPreviewInstance(avatarRoot))
            {
                return;
            }

            var thirdPersonBody = avatarRoot.transform.Find("ThirdPersonBody");
            var syntyVisual = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
            if (syntyVisual == null || syntyVisual.Find(RemoteResourceClothingApplier.RemoteClothingRootName) == null)
            {
                return;
            }

            var clothingApplier = avatarRoot.GetComponent<RemoteResourceClothingApplier>();
            if (clothingApplier == null)
            {
                return;
            }

            clothingApplier.RefreshHiddenBodyForVisual(syntyVisual, hideHandsOnBody: true);
        }

        private void EnsureAvatarHasFallbackModel(GameObject avatarRoot)
        {
            if (avatarRoot == null || CharacterModelApplier.HasCharacterBody(avatarRoot))
            {
                return;
            }

            var fallbackModel = CharacterSelectionService.ResolveFallbackModel(charactersResourcesFolder);
            if (fallbackModel == null)
            {
                Debug.LogWarning("[MatchPresenceSync] Missing fallback model in Resources/Characters for remote avatar.");
                return;
            }

            CharacterModelApplier.TryApplyToPlayer(avatarRoot, fallbackModel);
        }

        private static void RefreshRemoteAvatarHitboxes(GameObject avatarRoot)
        {
            if (avatarRoot == null)
            {
                return;
            }

            var thirdPersonBody = avatarRoot.transform.Find("ThirdPersonBody");
            var syntyVisual = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
            if (syntyVisual == null)
            {
                return;
            }

            CharacterModelApplier.RefreshRemoteHitboxes(avatarRoot, syntyVisual, forceRebuild: false);
        }

        private void TryPlayRemoteShots(RemoteAvatar avatar, RealtimeTransportClient.RealtimePlayerState playerState)
        {
            if (avatar == null || avatar.Root == null || playerState == null)
            {
                return;
            }

            var shotEffects = avatar.RemoteShotEffects;
            if (shotEffects == null)
            {
                shotEffects = avatar.Root.AddComponent<RemotePlayerShotEffects>();
                avatar.RemoteShotEffects = shotEffects;
            }

            if (shotEffects != null && localWeaponController != null)
            {
                ApplyRemoteWeaponEffects(avatar, ResolveRemoteActiveWeaponKind(playerState));
            }

            EnsureRemoteAudio(avatar.Root);

            var playedAny = false;
            if (playerState.recentShots != null && playerState.recentShots.Length > 0)
            {
                for (var i = 0; i < playerState.recentShots.Length; i++)
                {
                    var shotEvent = playerState.recentShots[i];
                    if (shotEvent == null || shotEvent.seq <= avatar.LastAppliedShotSeq)
                    {
                        continue;
                    }

                    PlayRemoteShotEvent(shotEffects, shotEvent, playerState.lookPitch);
                    avatar.LastAppliedShotSeq = shotEvent.seq;
                    playedAny = true;
                }
            }

            if (playedAny || playerState.shotSeq <= avatar.LastAppliedShotSeq)
            {
                return;
            }

            PlayRemoteShotLegacy(shotEffects, playerState);
            avatar.LastAppliedShotSeq = playerState.shotSeq;
        }

        private void PlayRemoteShotEvent(
            RemotePlayerShotEffects shotEffects,
            RealtimeTransportClient.RealtimeShotEvent shotEvent,
            float lookPitch)
        {
            if (shotEffects == null || shotEvent == null)
            {
                return;
            }

            var shotOrigin = new Vector3(shotEvent.originX, shotEvent.originY, shotEvent.originZ);
            var shotDirection = new Vector3(shotEvent.dirX, shotEvent.dirY, shotEvent.dirZ);
            var shotEndPoint = new Vector3(shotEvent.endX, shotEvent.endY, shotEvent.endZ);
            var hasShotEndPoint = shotEvent.hasEndPoint;
            if (!hasShotEndPoint && localWeaponController != null)
            {
                var travel = (shotEndPoint - shotOrigin).magnitude;
                if (travel > 0.05f && travel <= localWeaponController.ShotMaxDistance + 1f)
                {
                    hasShotEndPoint = true;
                }
            }

            shotEffects.PlayRemoteShot(
                shotOrigin,
                shotDirection,
                shotEndPoint,
                hasShotEndPoint,
                lookPitch);
        }

        private void PlayRemoteShotLegacy(
            RemotePlayerShotEffects shotEffects,
            RealtimeTransportClient.RealtimePlayerState playerState)
        {
            if (shotEffects == null || playerState == null)
            {
                return;
            }

            var shotOrigin = new Vector3(playerState.shotOriginX, playerState.shotOriginY, playerState.shotOriginZ);
            var shotDirection = new Vector3(playerState.shotDirX, playerState.shotDirY, playerState.shotDirZ);
            var shotEndPoint = new Vector3(playerState.shotEndX, playerState.shotEndY, playerState.shotEndZ);
            var hasShotEndPoint = playerState.shotHasEndPoint;
            if (!hasShotEndPoint && localWeaponController != null)
            {
                var travel = (shotEndPoint - shotOrigin).magnitude;
                if (travel > 0.05f && travel <= localWeaponController.ShotMaxDistance + 1f)
                {
                    hasShotEndPoint = true;
                }
            }

            shotEffects.PlayRemoteShot(
                shotOrigin,
                shotDirection,
                shotEndPoint,
                hasShotEndPoint,
                playerState.lookPitch);
        }

        private void EnsureRemoteAudio(GameObject remoteRoot)
        {
            if (remoteRoot == null || localAudioController == null)
            {
                return;
            }

            var remoteAudio = remoteRoot.GetComponent<PlayerAudioController>();
            if (remoteAudio == null)
            {
                remoteAudio = remoteRoot.AddComponent<PlayerAudioController>();
            }

            remoteAudio.InheritFrom(localAudioController);
        }

        private static void MaybeStopRemoteReloadOnWeaponInterrupt(
            RemoteAvatar avatar,
            RealtimeTransportClient.RealtimePlayerState playerState)
        {
            if (avatar?.Root == null || playerState == null)
            {
                return;
            }

            var holsteredNow = playerState.isHolstered;
            var hasWeaponNow = playerState.hasWeapon;
            var interrupted = (holsteredNow && !avatar.WasHolstered) ||
                              (!hasWeaponNow && avatar.HadWeapon);
            if (interrupted)
            {
                avatar.RemoteShotEffects?.StopRemoteReload();
            }

            avatar.WasHolstered = holsteredNow;
            avatar.HadWeapon = hasWeaponNow;
        }

        private static void ApplyRemoteWeaponEffects(RemoteAvatar avatar, WeaponKind weaponKind)
        {
            if (avatar == null)
            {
                return;
            }

            ApplyRemoteWeaponEffects(avatar.RemoteWeapon, avatar.RemoteShotEffects, weaponKind);
        }

        private static void ApplyRemoteWeaponEffects(GameObject remoteRoot, WeaponKind weaponKind)
        {
            if (remoteRoot == null)
            {
                return;
            }

            ApplyRemoteWeaponEffects(
                remoteRoot.GetComponent<RemoteWeaponPresentation>(),
                remoteRoot.GetComponent<RemotePlayerShotEffects>(),
                weaponKind);
        }

        private static void ApplyRemoteWeaponEffects(
            RemoteWeaponPresentation remoteWeapon,
            RemotePlayerShotEffects shotEffects,
            WeaponKind weaponKind)
        {
            if (shotEffects == null)
            {
                return;
            }

            var profile = remoteWeapon != null ? remoteWeapon.GetActiveWeaponProfile() : null;
            if (profile != null)
            {
                shotEffects.ApplyFromWeaponProfile(profile, profile.ReloadDuration);
                return;
            }

            shotEffects.ApplyForWeaponKind(weaponKind);
        }

        private void TryPlayRemoteReload(RemoteAvatar avatar, RealtimeTransportClient.RealtimePlayerState playerState)
        {
            if (avatar == null || avatar.Root == null || playerState == null)
            {
                return;
            }

            if (playerState.reloadSeq <= avatar.LastAppliedReloadSeq)
            {
                return;
            }

            var shotEffects = avatar.RemoteShotEffects;
            shotEffects?.PlayRemoteReload(shotEffects != null ? shotEffects.ReloadDurationSeconds : 1.8f);

            avatar.LastAppliedReloadSeq = playerState.reloadSeq;
        }

        private void TryPlayRemoteHitPlayer(RemoteAvatar avatar, RealtimeTransportClient.RealtimePlayerState playerState)
        {
            if (avatar == null || avatar.Root == null || playerState == null)
            {
                return;
            }

            if (playerState.hitPlayerSeq <= avatar.LastAppliedHitPlayerSeq)
            {
                return;
            }

            avatar.RemoteShotEffects?.PlayRemoteHitPlayer();
            avatar.LastAppliedHitPlayerSeq = playerState.hitPlayerSeq;
        }

        private void TryPlayRemoteFootsteps(RemoteAvatar avatar, RealtimeTransportClient.RealtimePlayerState playerState)
        {
            if (avatar == null || avatar.Root == null || playerState == null)
            {
                return;
            }

            if (playerState.footstepSeq <= avatar.LastAppliedFootstepSeq)
            {
                return;
            }

            var audio = avatar.RemoteAudio;
            var maxReplay = playerState.isSprinting ? 6 : 3;
            var count = Mathf.Clamp(playerState.footstepSeq - avatar.LastAppliedFootstepSeq, 1, maxReplay);
            for (var i = 0; i < count; i++)
            {
                audio?.PlayFootstep(false, playerState.isSprinting);
            }

            avatar.LastAppliedFootstepSeq = playerState.footstepSeq;
        }

        private static void EnsureRemoteVisuals(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var allTransforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < allTransforms.Length; i++)
            {
                var tr = allTransforms[i];
                if (tr == null || tr == root.transform)
                {
                    continue;
                }

                if (string.Equals(tr.name, "FirstPersonHands", StringComparison.Ordinal) ||
                    string.Equals(tr.name, "FirstPersonView", StringComparison.Ordinal))
                {
                    tr.gameObject.SetActive(false);
                }
                else if (string.Equals(tr.name, "CameraPivot", StringComparison.Ordinal) ||
                         string.Equals(tr.name, "PlayerCamera", StringComparison.Ordinal))
                {
                    tr.gameObject.SetActive(false);
                }
                else if (string.Equals(tr.name, "ThirdPersonBody", StringComparison.Ordinal))
                {
                    tr.gameObject.SetActive(true);
                }
            }

            var thirdPersonRoot = root.transform.Find("ThirdPersonBody");
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var enabledCount = 0;
            for (var i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null)
                {
                    continue;
                }

                var isThirdPersonRenderer = thirdPersonRoot != null && r.transform.IsChildOf(thirdPersonRoot);
                var isWeaponRenderer = IsRemoteWeaponRenderer(r.transform);
                if (thirdPersonRoot != null)
                {
                    r.enabled = isThirdPersonRenderer || isWeaponRenderer;
                }
                else
                {
                    r.enabled = true;
                }
                if (r.enabled && r.gameObject.activeInHierarchy)
                {
                    enabledCount++;
                }
            }

            if (enabledCount > 0)
            {
                return;
            }

            // Safety net: ensure remote player is always visible.
            var fallback = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            fallback.name = "RemoteVisualFallback";
            fallback.transform.SetParent(root.transform, false);
            fallback.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            fallback.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
            var collider = fallback.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
        }

        private static bool IsRemoteWeaponRenderer(Transform rendererTransform)
        {
            if (rendererTransform == null)
            {
                return false;
            }

            var current = rendererTransform;
            while (current != null)
            {
                if (string.Equals(current.name, "WeaponModel", System.StringComparison.Ordinal) ||
                    string.Equals(current.name, "BackWeaponModel0", System.StringComparison.Ordinal) ||
                    string.Equals(current.name, "BackWeaponModel1", System.StringComparison.Ordinal))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private void TryAutoInitializeFromLauncher()
        {
            if (presenceInitialized || GetComponent<LocalPlayerMarker>() == null)
            {
                return;
            }

            if (ActiveMatchContext.IsOfflineDuelSession ||
                ActiveMatchContext.IsOfflineDeathmatchSession ||
                ActiveMatchContext.IsOfflineSoloSession ||
                PlayerProfileService.IsOfflineMode)
            {
                return;
            }

            var launcher = networkLauncher ?? FindFirstObjectByType<NetworkLauncher>();
            if (launcher == null || string.IsNullOrWhiteSpace(launcher.CurrentTicketId))
            {
                return;
            }

            var transport = RealtimeTransportClient.Active ?? FindFirstObjectByType<RealtimeTransportClient>();
            if (transport == null)
            {
                return;
            }

            var spawnManager = FindFirstObjectByType<PlayerSpawnManager>();
            var remotePrefab = spawnManager != null ? spawnManager.GetRemotePlayerPrefabForPreview() : null;
            Initialize(launcher, transport, launcher.CurrentTicketId, remotePrefab);
        }

        private void RemoveAvatar(string ticketId)
        {
            if (!remoteAvatars.TryGetValue(ticketId, out var avatar))
            {
                return;
            }

            if (avatar.Root != null)
            {
                Destroy(avatar.Root);
            }

            remoteAvatars.Remove(ticketId);
        }

        private void ClearRemoteAvatars()
        {
            foreach (var kv in remoteAvatars)
            {
                if (kv.Value.Root != null)
                {
                    Destroy(kv.Value.Root);
                }
            }

            remoteAvatars.Clear();
            lastAppliedServerTick = -1;
            lastAppliedSnapshotSignature = 0;
            lastReconciledSelfAuthSampleTick = -1;
        }

        private double ResolveSnapshotTimeSeconds(RealtimeTransportClient.RealtimePlayerState playerState)
        {
            if (playerState == null)
            {
                return latestServerTimeSeconds;
            }

            var tickRate = GetMovementSampleRate();

            if (playerState.sampleTick > 0)
            {
                return playerState.sampleTick / tickRate;
            }

            if (playerState.sampleTimeMs > 0)
            {
                return playerState.sampleTimeMs / 1000.0;
            }

            return latestServerTimeSeconds;
        }

        private static void AddSnapshot(
            List<PresenceSnapshot> snapshots,
            double timeSeconds,
            Vector3 position,
            float yaw,
            Vector3 serverVelocity = default,
            bool useServerVelocity = false)
        {
            if (snapshots.Count > 0 && timeSeconds < snapshots[snapshots.Count - 1].TimeSeconds)
            {
                return;
            }

            var velocity = Vector3.zero;
            var yawVelocity = 0f;

            if (snapshots.Count > 0)
            {
                var lastIndex = snapshots.Count - 1;
                var last = snapshots[lastIndex];
                var timeDelta = timeSeconds - last.TimeSeconds;

                // Replace same-time sample instead of adding duplicates (common on uneven network delivery).
                if (Math.Abs(timeDelta) <= 0.0001)
                {
                    if (useServerVelocity)
                    {
                        velocity = serverVelocity;
                        yawVelocity = Mathf.DeltaAngle(last.Yaw, yaw) / (float)Math.Max(0.0001, timeDelta);
                    }
                    else if (lastIndex > 0)
                    {
                        var prev = snapshots[lastIndex - 1];
                        var span = (float)(last.TimeSeconds - prev.TimeSeconds);
                        if (span > 0.0001f)
                        {
                            velocity = (position - prev.Position) / span;
                            yawVelocity = Mathf.DeltaAngle(prev.Yaw, yaw) / span;
                        }
                    }

                    snapshots[lastIndex] = new PresenceSnapshot
                    {
                        TimeSeconds = timeSeconds,
                        Position = position,
                        Yaw = yaw,
                        Velocity = velocity,
                        YawVelocity = yawVelocity
                    };
                    return;
                }

                // Ignore almost-identical samples that only add jitter noise.
                if (!useServerVelocity &&
                    timeDelta <= 0.05 &&
                    (position - last.Position).sqrMagnitude <= 0.000004f &&
                    Mathf.Abs(Mathf.DeltaAngle(last.Yaw, yaw)) <= 0.08f)
                {
                    return;
                }

                if (useServerVelocity)
                {
                    velocity = serverVelocity;
                    yawVelocity = Mathf.DeltaAngle(last.Yaw, yaw) / (float)Math.Max(0.0001, timeDelta);
                }
                else
                {
                    velocity = (position - last.Position) / (float)Math.Max(0.0001, timeDelta);
                    yawVelocity = Mathf.DeltaAngle(last.Yaw, yaw) / (float)Math.Max(0.0001, timeDelta);
                }
            }
            else if (useServerVelocity)
            {
                velocity = serverVelocity;
            }

            snapshots.Add(new PresenceSnapshot
            {
                TimeSeconds = timeSeconds,
                Position = position,
                Yaw = yaw,
                Velocity = velocity,
                YawVelocity = yawVelocity
            });

            if (snapshots.Count > 96)
            {
                snapshots.RemoveAt(0);
            }
        }

        private InterpolatedPose EvaluatePose(
            List<PresenceSnapshot> snapshots,
            double renderTime,
            RemoteAvatar avatar)
        {
            if (snapshots == null || snapshots.Count == 0)
            {
                if (avatar != null && avatar.HasKnownPose)
                {
                    return new InterpolatedPose
                    {
                        Position = avatar.LastKnownPosition,
                        Yaw = avatar.LastKnownYaw,
                        Velocity = Vector3.zero,
                        YawVelocity = 0f
                    };
                }

                return new InterpolatedPose
                {
                    Position = transform.position,
                    Yaw = transform.eulerAngles.y,
                    Velocity = Vector3.zero,
                    YawVelocity = 0f
                };
            }

            while (snapshots.Count >= 2 && snapshots[1].TimeSeconds <= renderTime)
            {
                snapshots.RemoveAt(0);
            }

            if (snapshots.Count >= 2 &&
                snapshots[0].TimeSeconds <= renderTime &&
                renderTime <= snapshots[1].TimeSeconds)
            {
                var from = snapshots[0];
                var to = snapshots[1];
                var range = Math.Max(0.0001, to.TimeSeconds - from.TimeSeconds);
                var t = Mathf.Clamp01((float)((renderTime - from.TimeSeconds) / range));
                return new InterpolatedPose
                {
                    Position = HermitePosition(t, from.Position, to.Position, from.Velocity, to.Velocity, (float)range),
                    Yaw = HermiteYaw(t, from.Yaw, to.Yaw, from.YawVelocity, to.YawVelocity, (float)range),
                    Velocity = HermiteVelocity(t, from.Position, to.Position, from.Velocity, to.Velocity, (float)range),
                    YawVelocity = HermiteYawVelocity(t, from.Yaw, to.Yaw, from.YawVelocity, to.YawVelocity, (float)range)
                };
            }

            if (snapshots.Count >= 2)
            {
                var prev = snapshots[snapshots.Count - 2];
                var last = snapshots[snapshots.Count - 1];
                var extra = Mathf.Clamp((float)(renderTime - last.TimeSeconds), 0f, extrapolationLimitSeconds);
                var horizontalVelocity = Vector3.ClampMagnitude(
                    new Vector3(last.Velocity.x, 0f, last.Velocity.z),
                    12f);
                var verticalVelocity = Mathf.Clamp(last.Velocity.y, -8f, 8f);
                var extrapolatedY = last.Position.y + verticalVelocity * extra;
                if (avatar != null && !avatar.NetworkGrounded)
                {
                    extrapolatedY = last.Position.y;
                }

                return new InterpolatedPose
                {
                    Position = new Vector3(
                        last.Position.x + horizontalVelocity.x * extra,
                        extrapolatedY,
                        last.Position.z + horizontalVelocity.z * extra),
                    Yaw = last.Yaw + last.YawVelocity * extra,
                    Velocity = last.Velocity,
                    YawVelocity = last.YawVelocity
                };
            }

            var only = snapshots[0];
            return new InterpolatedPose
            {
                Position = only.Position,
                Yaw = only.Yaw,
                Velocity = only.Velocity,
                YawVelocity = only.YawVelocity
            };
        }

        private static Vector3 HermitePosition(
            float t,
            Vector3 p0,
            Vector3 p1,
            Vector3 v0,
            Vector3 v1,
            float segmentDuration)
        {
            var m0 = v0 * segmentDuration;
            var m1 = v1 * segmentDuration;
            var t2 = t * t;
            var t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * p0 +
                   (t3 - 2f * t2 + t) * m0 +
                   (-2f * t3 + 3f * t2) * p1 +
                   (t3 - t2) * m1;
        }

        private static Vector3 HermiteVelocity(
            float t,
            Vector3 p0,
            Vector3 p1,
            Vector3 v0,
            Vector3 v1,
            float segmentDuration)
        {
            if (segmentDuration <= 0.0001f)
            {
                return v1;
            }

            var m0 = v0 * segmentDuration;
            var m1 = v1 * segmentDuration;
            var t2 = t * t;
            var derivative = (6f * t2 - 6f * t) * p0 +
                             (3f * t2 - 4f * t + 1f) * m0 +
                             (-6f * t2 + 6f * t) * p1 +
                             (3f * t2 - 2f * t) * m1;
            return derivative / segmentDuration;
        }

        private static float HermiteYaw(
            float t,
            float yaw0,
            float yaw1,
            float yawVel0,
            float yawVel1,
            float segmentDuration)
        {
            var unwrappedTarget = yaw0 + Mathf.DeltaAngle(yaw0, yaw1);
            var m0 = yawVel0 * segmentDuration;
            var m1 = yawVel1 * segmentDuration;
            var t2 = t * t;
            var t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * yaw0 +
                   (t3 - 2f * t2 + t) * m0 +
                   (-2f * t3 + 3f * t2) * unwrappedTarget +
                   (t3 - t2) * m1;
        }

        private static float HermiteYawVelocity(
            float t,
            float yaw0,
            float yaw1,
            float yawVel0,
            float yawVel1,
            float segmentDuration)
        {
            if (segmentDuration <= 0.0001f)
            {
                return yawVel1;
            }

            var unwrappedTarget = yaw0 + Mathf.DeltaAngle(yaw0, yaw1);
            var m0 = yawVel0 * segmentDuration;
            var m1 = yawVel1 * segmentDuration;
            var t2 = t * t;
            var derivative = (6f * t2 - 6f * t) * yaw0 +
                             (3f * t2 - 4f * t + 1f) * m0 +
                             (-6f * t2 + 6f * t) * unwrappedTarget +
                             (3f * t2 - 2f * t) * m1;
            return derivative / segmentDuration;
        }
    }

    internal static class ListPool<T>
    {
        private static readonly Stack<List<T>> Pool = new Stack<List<T>>();
        public static List<T> Get() => Pool.Count > 0 ? Pool.Pop() : new List<T>();
        public static void Release(List<T> list)
        {
            list.Clear();
            Pool.Push(list);
        }
    }

}
