using ShooterPrototype.Player;
using System;
using System.Collections;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ShooterPrototype.Network
{
    [DefaultExecutionOrder(-100)]
    public sealed class RealtimeTransportClient : MonoBehaviour
    {
        private static readonly Stopwatch MonotonicClock = Stopwatch.StartNew();

        public static float MonotonicNowSeconds => (float)MonotonicClock.Elapsed.TotalSeconds;

        [Serializable]
        public sealed class RealtimeStateSample
        {
            public int sampleTick;
            public float x;
            public float y;
            public float z;
            public float yaw;
            public float velX;
            public float velY;
            public float velZ;
        }

        [Serializable]
        public sealed class RealtimeShotEvent
        {
            public int seq;
            public float originX;
            public float originY;
            public float originZ;
            public float dirX;
            public float dirY;
            public float dirZ;
            public float endX;
            public float endY;
            public float endZ;
            public bool hasEndPoint;
        }

        [Serializable]
        public sealed class RealtimePlayerState
        {
        public string ticketId;
        public string nickname;
        public int duelRating;
        public string characterModel;
            public string skinShirt;
            public string skinPants;
            public string skinBoots;
            public string skinGloves;
            public string skinFace;
            public string skinHair;
            public string skinWeaponAssault;
            public string skinWeaponSniper;
            public string skinWeaponPistol;
            public string skinWeaponMp7;
            public PositionDto position;
            public float yaw;
            public float lookPitch;
            public int shotSeq;
            public int reloadSeq;
            public int hitPlayerSeq;
            public int footstepSeq;
            public bool isCrouching;
            public bool isSprinting;
            public bool isSwimming;
            public float wallAvoidBlend;
            public bool isDead;
            public int deathSeq;
            public float deathFallDirX;
            public float deathFallDirY;
            public float deathFallDirZ;
            public float animSpeed;
            public bool isAiming;
            public bool isHolstered;
            public bool hasWeapon;
            public bool isUsingMedkit;
            public float medkitRemainingSeconds;
            public int medkitCount;
            public int killCount;
            public int weaponPickupSeq;
            public int weaponKind;
            public int weaponSlot0Kind;
            public int weaponSlot1Kind;
            public int activeWeaponSlot;
            public bool isGrounded;
            public int jumpState;
            public float animPhase;
            public float velX;
            public float velY;
            public float velZ;
            public float moveInputX;
            public float moveInputZ;
            public int sampleTick;
            public long sampleTimeMs;
            public float shotOriginX;
            public float shotOriginY;
            public float shotOriginZ;
            public float shotDirX;
            public float shotDirY;
            public float shotDirZ;
            public float shotEndX;
            public float shotEndY;
            public float shotEndZ;
            public bool shotHasEndPoint;
            public RealtimeShotEvent[] recentShots;
            public RealtimeStateSample[] history;
        }

        [Serializable]
        public sealed class RealtimeSnapshot
        {
            public string type;
            public int serverTick;
            public int serverTickRate;
            public int movementSampleRateHz;
            public int binaryVersion;
            public RealtimePlayerState[] players;
            public SelfAuthoritativePose selfAuthoritative;
        }

        [Serializable]
        public sealed class SelfAuthoritativePose
        {
            public PositionDto position;
            public float yaw;
            public int sampleTick;
        }

        [Serializable]
        private sealed class JoinedMessage
        {
            public string type;
            public string ticketId;
        }

        [Serializable]
        public sealed class PositionDto
        {
            public float x;
            public float y;
            public float z;
        }

        [Serializable]
        private sealed class JoinMessage
        {
            public string type;
            public string ticketId;
        }

        [Serializable]
        public sealed class PickupSpawnRegistration
        {
            public string spawnId;
            public string pickupKind;
            public string itemId;
            public string weaponId;
            public int amount;
            public int magAmmo = -1;
            public float x;
            public float y;
            public float z;
            public float respawnDelaySeconds;
        }

        [Serializable]
        public sealed class PickupSpawnState
        {
            public string spawnId;
            public string pickupKind;
            public string itemId;
            public string weaponId;
            public int amount;
            public bool available;
            public float x;
            public float y;
            public float z;
            public int magAmmo;
        }

        [Serializable]
        public sealed class PickupStateMessage
        {
            public string type;
            public PickupSpawnState[] spawns;
        }

        [Serializable]
        public sealed class PickupEventMessage
        {
            public string type;
            public string spawnId;
            public string ticketId;
            public int weaponPickupSeq;
            public string pickupKind;
            public string itemId;
            public string weaponId;
            public int amount;
            public bool available;
            public float x;
            public float y;
            public float z;
        }

        [Serializable]
        public sealed class PickupResultMessage
        {
            public string type;
            public bool success;
            public string reason;
            public string spawnId;
            public string ticketId;
            public int weaponPickupSeq;
            public string pickupKind;
            public string itemId;
            public string weaponId;
            public int amount;
            public int medkitCount;
            public int grenadeCount;
            public int weaponSlot0Kind;
            public int weaponSlot1Kind;
            public string weaponSlot0ItemId;
            public string weaponSlot1ItemId;
            public int activeWeaponSlot;
            public bool bothHolstered;
            public string droppedSpawnId;
            public int magAmmo;
            public int reserveAmmo;
            public int spareAmmoAssault = -1;
            public int spareAmmoSniper = -1;
            public int spareAmmoPistol = -1;
            public int spareAmmoMp7 = -1;
        }

        [Serializable]
        public sealed class WeaponDropResultMessage
        {
            public string type;
            public bool success;
            public string reason;
            public string ticketId;
            public int slotIndex;
            public string droppedSpawnId;
            public string itemId;
            public float x;
            public float y;
            public float z;
            public int weaponSlot0Kind;
            public int weaponSlot1Kind;
            public string weaponSlot0ItemId;
            public string weaponSlot1ItemId;
            public int activeWeaponSlot;
            public bool bothHolstered;
            public int weaponPickupSeq;
            public int magAmmo;
            public int reserveAmmo;
            public int spareAmmoAssault = -1;
            public int spareAmmoSniper = -1;
            public int spareAmmoPistol = -1;
            public int spareAmmoMp7 = -1;
        }

        [Serializable]
        public sealed class WeaponSwapResultMessage
        {
            public string type;
            public bool success;
            public string reason;
            public string ticketId;
            public int slotA;
            public int slotB;
            public int weaponPickupSeq;
            public int weaponSlot0Kind;
            public int weaponSlot1Kind;
            public string weaponSlot0ItemId;
            public string weaponSlot1ItemId;
            public int activeWeaponSlot;
            public bool bothHolstered;
            public int spareAmmoAssault = -1;
            public int spareAmmoSniper = -1;
            public int spareAmmoPistol = -1;
            public int spareAmmoMp7 = -1;
        }

        [Serializable]
        public sealed class InventoryItemDropResultMessage
        {
            public string type;
            public bool success;
            public string reason;
            public string ticketId;
            public string itemId;
            public int amount;
            public int medkitCount = -1;
            public int grenadeCount = -1;
            public int spareAmmoAssault = -1;
            public int spareAmmoSniper = -1;
            public int spareAmmoPistol = -1;
            public int spareAmmoMp7 = -1;
            public string droppedSpawnId;
            public float x;
            public float y;
            public float z;
        }
    private sealed class WeaponDropRequestMessage
    {
        public string type;
        public int slotIndex;
        public int magAmmo;
        public bool hasDropPosition;
        public float x;
        public float y;
        public float z;
        public int spareAmmoAssault = -1;
        public int spareAmmoSniper = -1;
        public int spareAmmoPistol = -1;
        public int spareAmmoMp7 = -1;
    }

    private sealed class WeaponSwapRequestMessage
    {
        public string type;
        public int slotA;
        public int slotB;
    }

    private sealed class InventoryItemDropRequestMessage
    {
        public string type;
        public string itemId;
        public int amount;
        public bool hasDropPosition;
        public float x;
        public float y;
        public float z;
    }

        [Serializable]
        private sealed class AmmoStateMessage
        {
            public string type;
            public int spareAmmoAssault = -1;
            public int spareAmmoSniper = -1;
            public int spareAmmoPistol = -1;
            public int spareAmmoMp7 = -1;
            public int slot0MagAmmo = -1;
            public int slot1MagAmmo = -1;
        }

        [Serializable]
        public sealed class MedkitResultMessage
        {
            public string type;
            public bool success;
            public string reason;
            public string ticketId;
            public int medkitSeq;
            public float durationSeconds;
            public int medkitCount;
        }

        [Serializable]
        public sealed class HealMessage
        {
            public string type;
            public float amount;
            public int medkitSeq;
        }

        [Serializable]
        private sealed class MedkitUseMessage
        {
            public string type;
        }

        [Serializable]
        private sealed class MedkitCancelMessage
        {
            public string type;
        }

        [Serializable]
        private sealed class RegisterPickupsMessage
        {
            public string type;
            public PickupSpawnRegistration[] spawns;
        }

        [Serializable]
        private sealed class RegisterDamageZoneMessage
        {
            public string type;
            public float centerX;
            public float centerZ;
            public float phase1CenterOffset;
            public float phase2CenterOffset;
            public float initialRadius;
            public float phase1EndRadius;
            public float finalRadius;
            public float totalShrinkDurationSeconds;
            public float phase1DurationSeconds;
            public float phase2MoveDurationSeconds;
            public float phase2DurationSeconds;
            public float phase3ShrinkDurationSeconds;
            public float damageMinPerSecond;
            public float damageMaxPerSecond;
            public float damageRampSeconds;
        }

        [Serializable]
        public sealed class DamageZoneStateMessage
        {
            public string type;
            public int phase;
            public float centerX;
            public float centerZ;
            public float phase1CenterX;
            public float phase1CenterZ;
            public float phase2CenterX;
            public float phase2CenterZ;
            public float mapCenterX;
            public float mapCenterZ;
            public float initialRadius;
            public float phase1EndRadius;
            public float finalRadius;
            public float totalShrinkDurationSeconds;
            public float phase1DurationSeconds;
            public float phase2MoveDurationSeconds;
            public float phase2DurationSeconds;
            public float phase3ShrinkDurationSeconds;
            public float damageMinPerSecond;
            public float damageMaxPerSecond;
            public float damageRampSeconds;
            public float radius;
            public float damagePerSecond;
            public float elapsedSeconds;
            public long startedAtMs;
        }

        [Serializable]
        public sealed class MatchStateMessage
        {
            public string type;
            public string phase;
            public bool joinLocked;
            public int countdownRemainingSeconds;
            public long planeStartedAtMs;
            public long planeEndsAtMs;
            public long playingStartedAtMs;
            public long endingStartedAtMs;
            public string winnerTicketId;
            public int aliveCount;
            public int localPlacement;
            public int connectedCount;
            public bool hasJumped;
            public bool hasLanded;
            public bool inCombat;
            public bool forceJump;
            public bool useForcedDrop;
            public float dropPosX;
            public float dropPosZ;
            public float mapCenterX;
            public float mapCenterZ;
            public float planeStartX;
            public float planeStartZ;
            public float planeEndX;
            public float planeEndZ;
            public float planeY;
            public float planeSpeed;
            public float planePosX;
            public float planePosY;
            public float planePosZ;
            public int planeSpawnIndex = -1;
            public int planeSpawnSlotCount = 12;
            public string localTicketId;
            public int localKillCount;
            public bool isLocalWinner;
            public int winnerDisconnectSeconds;
            public string matchMode;
            public int duelRoundNumber;
            public int duelLocalRoundWins;
            public int duelOpponentRoundWins;
            public int duelRoundsToWin;
            public bool duelMovementLocked;
            public bool duelCombatEnabled;
            public int duelPickedWeaponKind = -1;
            public int duelTeamIndex = -1;
            public int duelSpawnSlotIndex = -1;
            public int duelOpponentTeamIndex = -1;
            public int duelOpponentSpawnSlotIndex = -1;
            public int duelLocalDuelRating;
            public string duelOpponentNickname;
            public int duelOpponentDuelRating;
        }

        [Serializable]
        public sealed class MatchDisconnectMessage
        {
            public string type;
            public string ticketId;
            public string reason;
            public int localPlacement;
        }

        [Serializable]
        public sealed class KillFeedMessage
        {
            public string type;
            public long seq;
            public string killerTicketId;
            public string victimTicketId;
            public string killerNickname;
            public string victimNickname;
            public int weaponKind;
            public string cause;
        }

        [Serializable]
        public sealed class MatchStatsMessage
        {
            public string type;
            public string sourceId;
            public bool won;
            public int ratingDelta;
            public bool alreadyReported;
            public MatchStatsProfileDto profile;
        }

        [Serializable]
        public sealed class MatchStatsProfileDto
        {
            public int duelRating;
            public int rating;
        }

        [Serializable]
        public sealed class PlayerLandMessage
        {
            public string type;
            public string ticketId;
        }

        [Serializable]
        private sealed class PlaneLandedMessage
        {
            public string type;
        }

        [Serializable]
        private sealed class DeathFallLandedMessage
        {
            public string type;
        }

        [Serializable]
        private sealed class PlaneJumpMessage
        {
            public string type;
        }

        [Serializable]
        private sealed class PickupRequestMessage
        {
            public string type;
            public string spawnId;
            public int targetWeaponSlot = -1;
        }

        [Serializable]
        private sealed class HitMessage
        {
            public string type;
            public string targetTicketId;
            public float damage;
            public float dirX;
            public float dirY;
            public float dirZ;
            public int shotSeq;
            public int shotTick;
            public float hitX;
            public float hitY;
            public float hitZ;
            public string hitZone;
        }

        [Serializable]
        private sealed class ShotMessage
        {
            public string type;
            public int shotSeq;
            public float shotOriginX;
            public float shotOriginY;
            public float shotOriginZ;
            public float shotDirX;
            public float shotDirY;
            public float shotDirZ;
            public float shotEndX;
            public float shotEndY;
            public float shotEndZ;
            public bool shotHasEndPoint;
        }

        [Serializable]
        public sealed class DamageMessage
        {
            public string type;
            public string attackerTicketId;
            public string targetTicketId;
            public float damage;
            public float remainingHealth = -1f;
            public float dirX;
            public float dirY;
            public float dirZ;
        }

        [Serializable]
        private sealed class PoseMessage
        {
            public string type;
            public string characterModel;
            public string skinShirt;
            public string skinPants;
            public string skinBoots;
            public string skinGloves;
            public string skinFace;
            public string skinHair;
            public string skinWeaponAssault;
            public string skinWeaponSniper;
            public string skinWeaponPistol;
            public string skinWeaponMp7;
            public PositionDto position;
            public float yaw;
            public float lookPitch;
            public int shotSeq;
            public int reloadSeq;
            public int hitPlayerSeq;
            public int footstepSeq;
            public bool isCrouching;
            public bool isSprinting;
            public bool isSwimming;
            public float wallAvoidBlend;
            public bool isDead;
            public int deathSeq;
            public float deathFallDirX;
            public float deathFallDirY;
            public float deathFallDirZ;
            public float animSpeed;
            public bool isAiming;
            public bool isHolstered;
            public bool isGrounded;
            public int jumpState;
            public float animPhase;
            public int poseSeq;
            public float moveInputX;
            public float moveInputZ;
            public bool jumpPressed;
            public bool inputAuth;
            public float shotOriginX;
            public float shotOriginY;
            public float shotOriginZ;
            public float shotDirX;
            public float shotDirY;
            public float shotDirZ;
            public float shotEndX;
            public float shotEndY;
            public float shotEndZ;
            public bool shotHasEndPoint;
            public int weaponKind;
            public int weaponSlot0Kind;
            public int weaponSlot1Kind;
            public int activeWeaponSlot;
            public int activeWeaponMagAmmo;
            public int weaponPickupSeq;
        }

        [Serializable]
        private sealed class PingMessage
        {
            public string type;
            public double clientTimeMs;
        }

        [Serializable]
        private sealed class PongMessage
        {
            public string type;
            public double clientTimeMs;
        }

        [SerializeField] private string websocketUrl = "ws://127.0.0.1:5051";
        [SerializeField] private bool useBinaryPoses = true;
        [SerializeField] private bool useDeltaPoseFilter = true;
        [SerializeField] private float poseHeartbeatSeconds = 0.1f;
        [SerializeField] private float posePositionEpsilon = 0.008f;
        [SerializeField] private float poseYawEpsilon = 0.25f;
        [SerializeField] private float poseAnimEpsilon = 0.02f;

        public readonly struct NetworkStats
        {
            public NetworkStats(
                int smoothedRoundTripMs,
                float posesSentPerSecond,
                float posesSkippedPerSecond,
                float snapshotsPerSecond,
                bool useBinaryPoses,
                float poseHeartbeatSeconds,
                int latestServerTick)
            {
                SmoothedRoundTripMs = smoothedRoundTripMs;
                PosesSentPerSecond = posesSentPerSecond;
                PosesSkippedPerSecond = posesSkippedPerSecond;
                SnapshotsPerSecond = snapshotsPerSecond;
                UseBinaryPoses = useBinaryPoses;
                PoseHeartbeatSeconds = poseHeartbeatSeconds;
                LatestServerTick = latestServerTick;
            }

            public int SmoothedRoundTripMs { get; }
            public float PosesSentPerSecond { get; }
            public float PosesSkippedPerSecond { get; }
            public float SnapshotsPerSecond { get; }
            public bool UseBinaryPoses { get; }
            public float PoseHeartbeatSeconds { get; }
            public int LatestServerTick { get; }
        }

        private ClientWebSocket socket;
        private CancellationTokenSource cts;
        private SemaphoreSlim sendSemaphore;
        private Task receiveTask;
        private string connectedTicketId = string.Empty;
        private string pendingConnectTicketId = string.Empty;
        private bool isConnecting;
        private volatile bool hasJoinAck;
        private float nextReconnectAllowedAt;
        private PoseMessage pendingPoseMessage;
        private byte[] pendingPoseBinary;
        private int nextPoseSeq;
        private bool hasPendingPose;
        private bool poseSendLoopRunning;
        private bool hasLastSentPose;
        private float lastPoseSentUnscaledTime = -999f;
        private PoseMessage lastSentPoseMessage;
        private int posesSentCounter;
        private int posesSkippedCounter;
        private int snapshotsCounter;
        private float networkStatsWindowStart = -1f;
        private float lastSendErrorLogAt;
        private float lastSnapshotDecodeErrorLogAt;
        private readonly object snapshotLock = new object();
        private readonly Queue<RealtimeSnapshot> pendingSnapshotQueue = new Queue<RealtimeSnapshot>();
        private const int MaxPendingSnapshots = 64;
        private RealtimeSnapshot latestSnapshot;
        private bool hasLatestSnapshot;
        private float lastPingSentUnscaledTime = -10f;
        private int smoothedRoundTripMs = -1;
        private int lastRoundTripMs = -1;
        private float lastSnapshotReceivedUnscaledTime;
        private Coroutine pingCoroutine;
        private Coroutine connectCoroutine;
        private int connectGeneration;
        private string sessionTicketId = string.Empty;
        private bool sessionActive;
        private bool autoReconnectEnabled = true;
        private int socketGeneration;
        private float reconnectBackoffSeconds = 0.35f;
        private const float MaxReconnectBackoffSeconds = 6f;
        private const float StaleSnapshotReconnectSeconds = 18f;
        private const float JoinStuckReconnectSeconds = 10f;
        private const float ConnectStuckSeconds = 25f;
        private const int MaxConnectAttemptsPerCycle = 3;
        private float socketOpenedAtMonotonic = -1f;
        private float connectAttemptStartedAtMonotonic = -1f;
        private Coroutine connectionSupervisorCoroutine;
        private readonly object mainThreadActionsLock = new object();
        private readonly Queue<Action> criticalMainThreadActions = new Queue<Action>();
        private readonly Queue<Action> mainThreadActions = new Queue<Action>();
        private MatchStateMessage pendingMatchStateMessage;
        private DamageZoneStateMessage pendingZoneStateMessage;
        private PickupStateMessage pendingPickupStateMessage;
        private bool matchStateFlushScheduled;
        private bool zoneStateFlushScheduled;
        private bool pickupStateFlushScheduled;

        public bool IsConnected => socket != null && socket.State == WebSocketState.Open;
        public bool IsConnecting => isConnecting;
        public bool IsReady => IsConnected && hasJoinAck;
        public string ConnectedTicketId => connectedTicketId;
        public int SmoothedRoundTripMs => smoothedRoundTripMs;
        public int LastRoundTripMs => lastRoundTripMs;
        public float LastSnapshotReceivedUnscaledTime => lastSnapshotReceivedUnscaledTime;
        public int LatestServerTick { get; private set; }
        public int LatestServerTickRate { get; private set; } = 30;
        public int LatestMovementSampleRate { get; private set; } = 64;

        public NetworkStats GetNetworkStats()
        {
            var now = Time.unscaledTime;
            if (networkStatsWindowStart < 0f)
            {
                networkStatsWindowStart = now;
            }

            var elapsed = Mathf.Max(0.001f, now - networkStatsWindowStart);
            var stats = new NetworkStats(
                smoothedRoundTripMs,
                posesSentCounter / elapsed,
                posesSkippedCounter / elapsed,
                snapshotsCounter / elapsed,
                useBinaryPoses,
                poseHeartbeatSeconds,
                LatestServerTick);

            posesSentCounter = 0;
            posesSkippedCounter = 0;
            snapshotsCounter = 0;
            networkStatsWindowStart = now;
            return stats;
        }

        public event Action<DamageMessage> DamageReceived;
        public event Action JoinAcknowledged;
        public static RealtimeTransportClient Active { get; private set; }
        public event Action<PickupStateMessage> PickupStateReceived;
        public event Action<PickupEventMessage> PickupEventReceived;
        public event Action<PickupResultMessage> PickupResultReceived;
        public event Action<WeaponDropResultMessage> WeaponDropResultReceived;
        public event Action<WeaponSwapResultMessage> WeaponSwapResultReceived;
        public event Action<InventoryItemDropResultMessage> InventoryItemDropResultReceived;
        public event Action<MedkitResultMessage> MedkitResultReceived;
        public event Action<HealMessage> HealReceived;
        public event Action<DamageZoneStateMessage> DamageZoneStateReceived;
        public event Action<MatchStateMessage> MatchStateReceived;
        public event Action<MatchDisconnectMessage> MatchDisconnectReceived;
        public event Action<KillFeedMessage> KillFeedReceived;
        public event Action<MatchStatsMessage> MatchStatsReceived;
        public event Action<PlayerLandMessage> PlayerLandReceived;

        private void Awake()
        {
            if (Active != null && Active != this)
            {
                Debug.LogWarning("[RealtimeTransportClient] Duplicate instance detected; destroying duplicate.");
                Destroy(this);
                return;
            }

            Active = this;
        }

        private void Update()
        {
            ProcessMainThreadActions();
        }

        private void LateUpdate()
        {
            ProcessMainThreadActions();
        }

        private void OnDestroy()
        {
            if (Active == this)
            {
                Active = null;
            }

            if (connectCoroutine != null)
            {
                StopCoroutine(connectCoroutine);
                connectCoroutine = null;
            }

            _ = DisconnectInternalAsync();
        }

        private void MarkJoinAcknowledged(string source)
        {
            if (hasJoinAck)
            {
                return;
            }

            hasJoinAck = true;
            ResetPoseSendCache();
            MovementNetworkDiagnostics.LogWsState(source, connectedTicketId, "ready=1");
            JoinAcknowledged?.Invoke();
        }

        private void RequestJoinAcknowledgement(string source)
        {
            EnqueueMainThreadAction(() => MarkJoinAcknowledged(source));
        }

        private void EnqueueMainThreadAction(Action action, bool critical = false)
        {
            if (action == null)
            {
                return;
            }

            lock (mainThreadActionsLock)
            {
                if (critical)
                {
                    criticalMainThreadActions.Enqueue(action);
                }
                else
                {
                    mainThreadActions.Enqueue(action);
                }
            }
        }

        private void EnqueueCoalescedMatchState(MatchStateMessage message)
        {
            if (message == null)
            {
                return;
            }

            lock (mainThreadActionsLock)
            {
                pendingMatchStateMessage = message;
                if (matchStateFlushScheduled)
                {
                    return;
                }

                matchStateFlushScheduled = true;
                mainThreadActions.Enqueue(FlushPendingMatchState);
            }
        }

        private void EnqueueCoalescedZoneState(DamageZoneStateMessage message)
        {
            if (message == null)
            {
                return;
            }

            lock (mainThreadActionsLock)
            {
                pendingZoneStateMessage = message;
                if (zoneStateFlushScheduled)
                {
                    return;
                }

                zoneStateFlushScheduled = true;
                mainThreadActions.Enqueue(FlushPendingZoneState);
            }
        }

        private void EnqueueCoalescedPickupState(PickupStateMessage message)
        {
            if (message == null)
            {
                return;
            }

            lock (mainThreadActionsLock)
            {
                pendingPickupStateMessage = message;
                if (pickupStateFlushScheduled)
                {
                    return;
                }

                pickupStateFlushScheduled = true;
                mainThreadActions.Enqueue(FlushPendingPickupState);
            }
        }

        private void FlushPendingMatchState()
        {
            MatchStateMessage message;
            lock (mainThreadActionsLock)
            {
                message = pendingMatchStateMessage;
                pendingMatchStateMessage = null;
                matchStateFlushScheduled = false;
            }

            if (message != null)
            {
                MatchStateReceived?.Invoke(message);
            }
        }

        private void FlushPendingZoneState()
        {
            DamageZoneStateMessage message;
            lock (mainThreadActionsLock)
            {
                message = pendingZoneStateMessage;
                pendingZoneStateMessage = null;
                zoneStateFlushScheduled = false;
            }

            if (message != null)
            {
                DamageZoneStateReceived?.Invoke(message);
            }
        }

        private void FlushPendingPickupState()
        {
            PickupStateMessage message;
            lock (mainThreadActionsLock)
            {
                message = pendingPickupStateMessage;
                pendingPickupStateMessage = null;
                pickupStateFlushScheduled = false;
            }

            if (message != null)
            {
                PickupStateReceived?.Invoke(message);
            }
        }

        private const int MaxCriticalMainThreadActionsPerPass = 256;
        private const int MaxMainThreadActionsPerPass = 512;

        private void ProcessMainThreadActions()
        {
            ProcessMainThreadQueue(criticalMainThreadActions, MaxCriticalMainThreadActionsPerPass);
            ProcessMainThreadQueue(mainThreadActions, MaxMainThreadActionsPerPass);
        }

        private void ProcessMainThreadQueue(Queue<Action> queue, int maxPerPass)
        {
            var processed = 0;
            while (processed < maxPerPass)
            {
                Action action;
                lock (mainThreadActionsLock)
                {
                    if (queue.Count == 0)
                    {
                        return;
                    }

                    action = queue.Dequeue();
                }

                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }

                processed++;
            }
        }

        public void Configure(string wsUrl)
        {
            if (!string.IsNullOrWhiteSpace(wsUrl))
            {
                websocketUrl = wsUrl.Trim();
            }
        }

        public void BeginMatchSession(string ticketId)
        {
            if (string.IsNullOrWhiteSpace(ticketId))
            {
                return;
            }

            sessionTicketId = ticketId.Trim();
            sessionActive = true;
            autoReconnectEnabled = true;
            EnsureConnected();
        }

        public void EndMatchSession()
        {
            sessionActive = false;
            sessionTicketId = string.Empty;
            autoReconnectEnabled = false;
            connectGeneration++;
            if (connectCoroutine != null)
            {
                StopCoroutine(connectCoroutine);
                connectCoroutine = null;
            }

            isConnecting = false;
            pendingConnectTicketId = string.Empty;
            Disconnect();
        }

        public void SetAutoReconnectEnabled(bool enabled)
        {
            autoReconnectEnabled = enabled;
        }

        public void EnsureConnected()
        {
            if (!sessionActive || !autoReconnectEnabled || string.IsNullOrWhiteSpace(sessionTicketId))
            {
                return;
            }

            if (Time.unscaledTime < nextReconnectAllowedAt)
            {
                return;
            }

            if (IsReady && string.Equals(connectedTicketId, sessionTicketId, StringComparison.Ordinal))
            {
                return;
            }

            if (isConnecting)
            {
                return;
            }

            Connect(sessionTicketId);
        }

        private void EnsureWebSocketUrlConfigured()
        {
            if (!string.IsNullOrWhiteSpace(websocketUrl))
            {
                return;
            }

            var launcher = FindFirstObjectByType<NetworkLauncher>();
            var configUrl = launcher != null && launcher.Config != null
                ? launcher.Config.ResolveRealtimeWsUrl()
                : null;
            if (!string.IsNullOrWhiteSpace(configUrl))
            {
                Configure(configUrl);
                return;
            }

            Configure("ws://127.0.0.1:5051");
        }

        public void Connect(string ticketId)
        {
            if (string.IsNullOrWhiteSpace(ticketId))
            {
                return;
            }

            ticketId = ticketId.Trim();
            if (sessionActive && !string.Equals(ticketId, sessionTicketId, StringComparison.Ordinal))
            {
                return;
            }

            EnsureWebSocketUrlConfigured();
            if (Time.unscaledTime < nextReconnectAllowedAt)
            {
                return;
            }

            if (IsReady && string.Equals(connectedTicketId, ticketId, StringComparison.Ordinal))
            {
                return;
            }

            if (isConnecting && string.Equals(pendingConnectTicketId, ticketId, StringComparison.Ordinal))
            {
                return;
            }

            if (isConnecting)
            {
                return;
            }

            if (connectCoroutine != null)
            {
                connectGeneration++;
                StopCoroutine(connectCoroutine);
                connectCoroutine = null;
                isConnecting = false;
                pendingConnectTicketId = string.Empty;
            }

            isConnecting = true;
            pendingConnectTicketId = ticketId;
            connectAttemptStartedAtMonotonic = MonotonicNowSeconds;
            connectGeneration++;
            var generation = connectGeneration;
            connectCoroutine = StartCoroutine(ConnectCoroutine(ticketId, generation));
        }

        public void Disconnect()
        {
            _ = DisconnectInternalAsync(socket, socketGeneration);
        }

        public void ResetPoseSendCache()
        {
            hasLastSentPose = false;
            lastPoseSentUnscaledTime = -999f;
            lastSentPoseMessage = null;
        }

        public void SendPose(
            Vector3 position,
            float yaw,
            string characterModel = "",
            PlayerSkinNetworkState skinState = default,
            float lookPitch = 0f,
            int shotSeq = 0,
            int reloadSeq = 0,
            int hitPlayerSeq = 0,
            int footstepSeq = 0,
            bool isCrouching = false,
            bool isSprinting = false,
            bool isSwimming = false,
            float wallAvoidBlend = 0f,
            bool isDead = false,
            int deathSeq = 0,
            Vector3 deathFallDirection = default,
            bool isAiming = false,
            bool isHolstered = false,
            float animSpeed = 0f,
            bool isGrounded = true,
            int jumpState = 0,
            float animPhase = 0f,
            float moveInputX = 0f,
            float moveInputZ = 0f,
            bool jumpPressed = false,
            bool inputAuth = false,
            Vector3 shotOrigin = default,
            Vector3 shotDirection = default,
            Vector3 shotEndPoint = default,
            bool shotHasEndPoint = false,
            int weaponKind = 0,
            int weaponSlot0Kind = 255,
            int weaponSlot1Kind = 255,
            int activeWeaponSlot = 255,
            int activeWeaponMagAmmo = -1,
            int weaponPickupSeq = 0,
            bool forceImmediate = false)
        {
            if (!IsConnected)
            {
                return;
            }

            pendingPoseMessage = new PoseMessage
            {
                type = "pose",
                position = new PositionDto
                {
                    x = position.x,
                    y = position.y,
                    z = position.z
                },
                characterModel = string.IsNullOrWhiteSpace(characterModel) ? string.Empty : characterModel,
                skinShirt = skinState.ShirtId ?? string.Empty,
                skinPants = skinState.PantsId ?? string.Empty,
                skinBoots = skinState.BootsId ?? string.Empty,
                skinGloves = skinState.GlovesId ?? string.Empty,
                skinFace = skinState.FaceId ?? string.Empty,
                skinHair = skinState.HairId ?? string.Empty,
                skinWeaponAssault = skinState.WeaponAssaultId ?? string.Empty,
                skinWeaponSniper = skinState.WeaponSniperId ?? string.Empty,
                skinWeaponPistol = skinState.WeaponPistolId ?? string.Empty,
                skinWeaponMp7 = skinState.WeaponMp7Id ?? string.Empty,
                yaw = yaw,
                lookPitch = lookPitch,
                shotSeq = Math.Max(0, shotSeq),
                reloadSeq = Math.Max(0, reloadSeq),
                hitPlayerSeq = Math.Max(0, hitPlayerSeq),
                footstepSeq = Math.Max(0, footstepSeq),
                isCrouching = isCrouching,
                isSprinting = isSprinting,
                isSwimming = isSwimming,
                wallAvoidBlend = Mathf.Clamp01(wallAvoidBlend),
                isDead = isDead,
                deathSeq = Math.Max(0, deathSeq),
                deathFallDirX = deathFallDirection.x,
                deathFallDirY = deathFallDirection.y,
                deathFallDirZ = deathFallDirection.z,
                animSpeed = Mathf.Clamp(animSpeed, 0f, ProceduralLocomotionRig.MaxNetworkAnimSpeed01),
                isAiming = isAiming,
                isHolstered = isHolstered,
                isGrounded = isGrounded,
                jumpState = Mathf.Clamp(jumpState, 0, 2),
                animPhase = Mathf.Repeat(animPhase, 1f),
                moveInputX = Mathf.Clamp(moveInputX, -1f, 1f),
                moveInputZ = Mathf.Clamp(moveInputZ, -1f, 1f),
                jumpPressed = jumpPressed,
                inputAuth = inputAuth,
                shotOriginX = shotOrigin.x,
                shotOriginY = shotOrigin.y,
                shotOriginZ = shotOrigin.z,
                shotDirX = shotDirection.x,
                shotDirY = shotDirection.y,
                shotDirZ = shotDirection.z,
                shotEndX = shotEndPoint.x,
                shotEndY = shotEndPoint.y,
                shotEndZ = shotEndPoint.z,
                shotHasEndPoint = shotHasEndPoint,
                weaponKind = WeaponKindUtility.ClampKindByte(weaponKind),
                weaponSlot0Kind = Mathf.Clamp(weaponSlot0Kind, 0, 255),
                weaponSlot1Kind = Mathf.Clamp(weaponSlot1Kind, 0, 255),
                activeWeaponSlot = Mathf.Clamp(activeWeaponSlot, 0, 255),
                activeWeaponMagAmmo = Mathf.Clamp(activeWeaponMagAmmo, -1, 999),
                weaponPickupSeq = Math.Max(0, weaponPickupSeq),
                poseSeq = ++nextPoseSeq
            };

            if (!forceImmediate && useDeltaPoseFilter && !ShouldSendPose(pendingPoseMessage))
            {
                posesSkippedCounter++;
                return;
            }

            pendingPoseBinary = null;
            if (useBinaryPoses)
            {
                try
                {
                    pendingPoseBinary = RealtimePoseBinaryCodec.Encode(BuildPosePacket(pendingPoseMessage));
                }
                catch (Exception ex)
                {
                    if (Time.unscaledTime - lastSendErrorLogAt > 1f)
                    {
                        lastSendErrorLogAt = Time.unscaledTime;
                        Debug.LogWarning($"[RealtimeTransportClient] Binary pose encode failed, using JSON: {ex.Message}");
                    }
                }
            }

            hasPendingPose = true;
            if (!poseSendLoopRunning)
            {
                _ = FlushLatestPoseLoopAsync();
            }
        }

        private static RealtimePosePacket BuildPosePacket(PoseMessage message)
        {
            return new RealtimePosePacket
            {
                PoseSeq = message.poseSeq,
                CharacterModel = message.characterModel ?? string.Empty,
                SkinState = new PlayerSkinNetworkState(
                    message.skinShirt,
                    message.skinPants,
                    message.skinBoots,
                    message.skinGloves,
                    message.skinFace,
                    message.skinHair,
                    message.skinWeaponAssault,
                    message.skinWeaponSniper,
                    message.skinWeaponPistol,
                    message.skinWeaponMp7),
                PosX = message.position?.x ?? 0f,
                PosY = message.position?.y ?? 0f,
                PosZ = message.position?.z ?? 0f,
                Yaw = message.yaw,
                LookPitch = message.lookPitch,
                IsCrouching = message.isCrouching,
                IsSprinting = message.isSprinting,
                IsSwimming = message.isSwimming,
                IsDead = message.isDead,
                IsHolstered = message.isHolstered,
                IsGrounded = message.isGrounded,
                InputAuth = message.inputAuth,
                JumpPressed = message.jumpPressed,
                ShotHasEndPoint = message.shotHasEndPoint,
                IsAiming = message.isAiming,
                JumpState = message.jumpState,
                WeaponKind = message.weaponKind,
                WeaponSlot0Kind = message.weaponSlot0Kind,
                WeaponSlot1Kind = message.weaponSlot1Kind,
                ActiveWeaponSlot = message.activeWeaponSlot,
                ActiveWeaponMagAmmo = message.activeWeaponMagAmmo,
                WeaponPickupSeq = message.weaponPickupSeq,
                ShotSeq = message.shotSeq,
                ReloadSeq = message.reloadSeq,
                HitPlayerSeq = message.hitPlayerSeq,
                FootstepSeq = message.footstepSeq,
                DeathSeq = message.deathSeq,
                AnimSpeed = message.animSpeed,
                AnimPhase = message.animPhase,
                WallAvoidBlend = message.wallAvoidBlend,
                MoveInputX = message.moveInputX,
                MoveInputZ = message.moveInputZ,
                DeathFallDirX = message.deathFallDirX,
                DeathFallDirY = message.deathFallDirY,
                DeathFallDirZ = message.deathFallDirZ,
                ShotOriginX = message.shotOriginX,
                ShotOriginY = message.shotOriginY,
                ShotOriginZ = message.shotOriginZ,
                ShotDirX = message.shotDirX,
                ShotDirY = message.shotDirY,
                ShotDirZ = message.shotDirZ,
                ShotEndX = message.shotEndX,
                ShotEndY = message.shotEndY,
                ShotEndZ = message.shotEndZ
            };
        }

        private bool ShouldSendPose(PoseMessage nextPose)
        {
            if (!hasLastSentPose || nextPose == null)
            {
                return true;
            }

            if (Time.unscaledTime - lastPoseSentUnscaledTime >= Mathf.Max(0.05f, poseHeartbeatSeconds))
            {
                return true;
            }

            if (nextPose.shotSeq != lastSentPoseMessage.shotSeq ||
                nextPose.reloadSeq != lastSentPoseMessage.reloadSeq ||
                nextPose.hitPlayerSeq != lastSentPoseMessage.hitPlayerSeq ||
                nextPose.footstepSeq != lastSentPoseMessage.footstepSeq ||
                nextPose.deathSeq != lastSentPoseMessage.deathSeq ||
                nextPose.weaponPickupSeq != lastSentPoseMessage.weaponPickupSeq)
            {
                return true;
            }

            if (nextPose.isDead != lastSentPoseMessage.isDead ||
                nextPose.isHolstered != lastSentPoseMessage.isHolstered ||
                nextPose.isCrouching != lastSentPoseMessage.isCrouching ||
                nextPose.isSprinting != lastSentPoseMessage.isSprinting ||
                nextPose.isSwimming != lastSentPoseMessage.isSwimming ||
                nextPose.isGrounded != lastSentPoseMessage.isGrounded ||
                nextPose.inputAuth != lastSentPoseMessage.inputAuth ||
                nextPose.jumpPressed != lastSentPoseMessage.jumpPressed ||
                nextPose.jumpState != lastSentPoseMessage.jumpState ||
                nextPose.weaponKind != lastSentPoseMessage.weaponKind ||
                nextPose.weaponSlot0Kind != lastSentPoseMessage.weaponSlot0Kind ||
                nextPose.weaponSlot1Kind != lastSentPoseMessage.weaponSlot1Kind ||
                nextPose.activeWeaponSlot != lastSentPoseMessage.activeWeaponSlot ||
                nextPose.activeWeaponMagAmmo != lastSentPoseMessage.activeWeaponMagAmmo ||
                nextPose.shotHasEndPoint != lastSentPoseMessage.shotHasEndPoint)
            {
                return true;
            }

            if (!string.Equals(nextPose.characterModel, lastSentPoseMessage.characterModel, StringComparison.Ordinal))
            {
                return true;
            }

            if (!string.Equals(nextPose.skinShirt, lastSentPoseMessage.skinShirt, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(nextPose.skinPants, lastSentPoseMessage.skinPants, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(nextPose.skinBoots, lastSentPoseMessage.skinBoots, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(nextPose.skinGloves, lastSentPoseMessage.skinGloves, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(nextPose.skinFace, lastSentPoseMessage.skinFace, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(nextPose.skinHair, lastSentPoseMessage.skinHair, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(nextPose.skinWeaponAssault, lastSentPoseMessage.skinWeaponAssault, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(nextPose.skinWeaponSniper, lastSentPoseMessage.skinWeaponSniper, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(nextPose.skinWeaponPistol, lastSentPoseMessage.skinWeaponPistol, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(nextPose.skinWeaponMp7, lastSentPoseMessage.skinWeaponMp7, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var lastPos = lastSentPoseMessage.position;
            var nextPos = nextPose.position;
            if (lastPos == null || nextPos == null)
            {
                return true;
            }

            if (Vector3.Distance(
                    new Vector3(lastPos.x, lastPos.y, lastPos.z),
                    new Vector3(nextPos.x, nextPos.y, nextPos.z)) > posePositionEpsilon)
            {
                return true;
            }

            if (Mathf.Abs(Mathf.DeltaAngle(lastSentPoseMessage.yaw, nextPose.yaw)) > poseYawEpsilon)
            {
                return true;
            }

            if (Mathf.Abs(lastSentPoseMessage.lookPitch - nextPose.lookPitch) > 0.5f ||
                Mathf.Abs(lastSentPoseMessage.animSpeed - nextPose.animSpeed) > poseAnimEpsilon ||
                Mathf.Abs(lastSentPoseMessage.animPhase - nextPose.animPhase) > poseAnimEpsilon ||
                Mathf.Abs(lastSentPoseMessage.wallAvoidBlend - nextPose.wallAvoidBlend) > poseAnimEpsilon ||
                Mathf.Abs(lastSentPoseMessage.moveInputX - nextPose.moveInputX) > 0.03f ||
                Mathf.Abs(lastSentPoseMessage.moveInputZ - nextPose.moveInputZ) > 0.03f)
            {
                return true;
            }

            return false;
        }

        private void RememberSentPose(PoseMessage message)
        {
            lastSentPoseMessage = ClonePoseMessage(message);
            hasLastSentPose = true;
            lastPoseSentUnscaledTime = Time.unscaledTime;
            posesSentCounter++;
        }

        private static PoseMessage ClonePoseMessage(PoseMessage source)
        {
            if (source == null)
            {
                return null;
            }

            return new PoseMessage
            {
                type = source.type,
                characterModel = source.characterModel,
                skinShirt = source.skinShirt,
                skinPants = source.skinPants,
                skinBoots = source.skinBoots,
                skinGloves = source.skinGloves,
                skinFace = source.skinFace,
                skinHair = source.skinHair,
                skinWeaponAssault = source.skinWeaponAssault,
                skinWeaponSniper = source.skinWeaponSniper,
                skinWeaponPistol = source.skinWeaponPistol,
                skinWeaponMp7 = source.skinWeaponMp7,
                position = source.position == null
                    ? null
                    : new PositionDto
                    {
                        x = source.position.x,
                        y = source.position.y,
                        z = source.position.z
                    },
                yaw = source.yaw,
                lookPitch = source.lookPitch,
                shotSeq = source.shotSeq,
                reloadSeq = source.reloadSeq,
                hitPlayerSeq = source.hitPlayerSeq,
                footstepSeq = source.footstepSeq,
                isCrouching = source.isCrouching,
                isSprinting = source.isSprinting,
                isSwimming = source.isSwimming,
                wallAvoidBlend = source.wallAvoidBlend,
                isDead = source.isDead,
                deathSeq = source.deathSeq,
                deathFallDirX = source.deathFallDirX,
                deathFallDirY = source.deathFallDirY,
                deathFallDirZ = source.deathFallDirZ,
                animSpeed = source.animSpeed,
                isAiming = source.isAiming,
                isHolstered = source.isHolstered,
                isGrounded = source.isGrounded,
                jumpState = source.jumpState,
                animPhase = source.animPhase,
                poseSeq = source.poseSeq,
                moveInputX = source.moveInputX,
                moveInputZ = source.moveInputZ,
                jumpPressed = source.jumpPressed,
                inputAuth = source.inputAuth,
                shotOriginX = source.shotOriginX,
                shotOriginY = source.shotOriginY,
                shotOriginZ = source.shotOriginZ,
                shotDirX = source.shotDirX,
                shotDirY = source.shotDirY,
                shotDirZ = source.shotDirZ,
                shotEndX = source.shotEndX,
                shotEndY = source.shotEndY,
                shotEndZ = source.shotEndZ,
                shotHasEndPoint = source.shotHasEndPoint,
                weaponKind = source.weaponKind,
                weaponSlot0Kind = source.weaponSlot0Kind,
                weaponSlot1Kind = source.weaponSlot1Kind,
                activeWeaponSlot = source.activeWeaponSlot,
                activeWeaponMagAmmo = source.activeWeaponMagAmmo,
                weaponPickupSeq = source.weaponPickupSeq
            };
        }

        public bool TryGetLatestSnapshot(out RealtimeSnapshot snapshot)
        {
            lock (snapshotLock)
            {
                if (!hasLatestSnapshot || latestSnapshot == null)
                {
                    snapshot = null;
                    return false;
                }

                snapshot = latestSnapshot;
                return true;
            }
        }

        public bool TryDequeueSnapshot(out RealtimeSnapshot snapshot)
        {
            lock (snapshotLock)
            {
                if (pendingSnapshotQueue.Count == 0)
                {
                    snapshot = null;
                    return false;
                }

                snapshot = pendingSnapshotQueue.Dequeue();
                return snapshot != null;
            }
        }

        public void TickNetworkMeasurement(float intervalSeconds = 1f)
        {
            if (!IsConnected || socket == null || sendSemaphore == null)
            {
                return;
            }

            if (Time.unscaledTime - lastPingSentUnscaledTime < Mathf.Max(0.25f, intervalSeconds))
            {
                return;
            }

            lastPingSentUnscaledTime = Time.unscaledTime;
            _ = SendPingAsync(cts != null ? cts.Token : CancellationToken.None);
        }

        private void OnEnable()
        {
            if (pingCoroutine == null)
            {
                pingCoroutine = StartCoroutine(PingLoop());
            }

            if (connectionSupervisorCoroutine == null)
            {
                connectionSupervisorCoroutine = StartCoroutine(ConnectionSupervisorLoop());
            }
        }

        private void OnDisable()
        {
            if (pingCoroutine != null)
            {
                StopCoroutine(pingCoroutine);
                pingCoroutine = null;
            }

            if (connectionSupervisorCoroutine != null)
            {
                StopCoroutine(connectionSupervisorCoroutine);
                connectionSupervisorCoroutine = null;
            }
        }

        private IEnumerator ConnectionSupervisorLoop()
        {
            var wait = new WaitForSecondsRealtime(0.5f);
            while (true)
            {
                yield return wait;

                if (!sessionActive || !autoReconnectEnabled)
                {
                    continue;
                }

                if (isConnecting)
                {
                    if (connectAttemptStartedAtMonotonic > 0f &&
                        MonotonicNowSeconds - connectAttemptStartedAtMonotonic > ConnectStuckSeconds)
                    {
                        ForceReconnect("connect_stuck");
                    }

                    continue;
                }

                if (IsReady)
                {
                    reconnectBackoffSeconds = 0.35f;
                    if (!Application.isFocused || lastSnapshotReceivedUnscaledTime <= 0f)
                    {
                        continue;
                    }

                    var snapshotAge = MonotonicNowSeconds - lastSnapshotReceivedUnscaledTime;
                    if (snapshotAge > StaleSnapshotReconnectSeconds)
                    {
                        ForceReconnect($"snapshot_stale age={snapshotAge:F1}s");
                    }

                    continue;
                }

                if (IsConnected && !IsReady)
                {
                    if (socketOpenedAtMonotonic > 0f &&
                        MonotonicNowSeconds - socketOpenedAtMonotonic > JoinStuckReconnectSeconds)
                    {
                        ForceReconnect("join_stuck");
                    }

                    continue;
                }

                EnsureConnected();
            }
        }

        private void ForceReconnect(string reason)
        {
            if (!sessionActive || !autoReconnectEnabled)
            {
                return;
            }

            Debug.LogWarning($"[RealtimeTransportClient] Force reconnect: {reason}");
            connectGeneration++;
            if (connectCoroutine != null)
            {
                StopCoroutine(connectCoroutine);
                connectCoroutine = null;
            }

            isConnecting = false;
            pendingConnectTicketId = string.Empty;
            connectAttemptStartedAtMonotonic = -1f;
            socketOpenedAtMonotonic = -1f;
            Disconnect();
            reconnectBackoffSeconds = Mathf.Min(reconnectBackoffSeconds * 1.5f, MaxReconnectBackoffSeconds);
            nextReconnectAllowedAt = Time.unscaledTime + reconnectBackoffSeconds;
            EnsureConnected();
        }

        private IEnumerator PingLoop()
        {
            var wait = new WaitForSecondsRealtime(1f);
            while (true)
            {
                yield return wait;
                TickNetworkMeasurement(1f);
            }
        }

        private async Task SendPingAsync(CancellationToken token)
        {
            if (!IsConnected || socket == null || sendSemaphore == null)
            {
                return;
            }

            await sendSemaphore.WaitAsync(token);
            try
            {
                if (socket == null || socket.State != WebSocketState.Open)
                {
                    return;
                }

                var ping = new PingMessage
                {
                    type = "ping",
                    clientTimeMs = MonotonicClock.Elapsed.TotalMilliseconds
                };
                var json = JsonUtility.ToJson(ping);
                var bytes = Encoding.UTF8.GetBytes(json);
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
            }
            catch
            {
                if (Time.unscaledTime - lastSendErrorLogAt > 1f)
                {
                    lastSendErrorLogAt = Time.unscaledTime;
                    Debug.LogWarning("[RealtimeTransportClient] Ping send failed");
                }
            }
            finally
            {
                sendSemaphore.Release();
            }
        }

        public void SendShotEvent(
            int shotSeq,
            Vector3 shotOrigin,
            Vector3 shotDirection,
            Vector3 shotEndPoint,
            bool shotHasEndPoint)
        {
            if (!IsConnected || shotSeq <= 0)
            {
                return;
            }

            var direction = shotDirection.sqrMagnitude > 0.0001f ? shotDirection.normalized : Vector3.forward;
            _ = SendJsonAsync(new ShotMessage
            {
                type = "shot",
                shotSeq = Math.Max(0, shotSeq),
                shotOriginX = shotOrigin.x,
                shotOriginY = shotOrigin.y,
                shotOriginZ = shotOrigin.z,
                shotDirX = direction.x,
                shotDirY = direction.y,
                shotDirZ = direction.z,
                shotEndX = shotEndPoint.x,
                shotEndY = shotEndPoint.y,
                shotEndZ = shotEndPoint.z,
                shotHasEndPoint = shotHasEndPoint
            }, cts != null ? cts.Token : CancellationToken.None);
        }

        public void SendHit(
            string targetTicketId,
            float damage,
            Vector3 shotDirection,
            int shotSeq = 0,
            int shotTick = 0,
            Vector3 hitPoint = default,
            string hitZone = "body")
        {
            if (!IsConnected || string.IsNullOrWhiteSpace(targetTicketId))
            {
                return;
            }

            var direction = shotDirection.sqrMagnitude > 0.0001f ? shotDirection.normalized : Vector3.forward;
            _ = SendJsonAsync(new HitMessage
            {
                type = "hit",
                targetTicketId = targetTicketId.Trim(),
                damage = Mathf.Max(0f, damage),
                dirX = direction.x,
                dirY = direction.y,
                dirZ = direction.z,
                shotSeq = Math.Max(0, shotSeq),
                shotTick = Math.Max(0, shotTick),
                hitX = hitPoint.x,
                hitY = hitPoint.y,
                hitZ = hitPoint.z,
                hitZone = string.IsNullOrWhiteSpace(hitZone) ? "body" : hitZone.Trim().ToLowerInvariant()
            }, cts != null ? cts.Token : CancellationToken.None);
        }

        public void SendRegisterPickups(PickupSpawnRegistration[] spawns)
        {
            if (!IsReady || spawns == null || spawns.Length == 0)
            {
                return;
            }

            _ = SendJsonAsync(new RegisterPickupsMessage
            {
                type = "register_pickups",
                spawns = spawns
            }, cts != null ? cts.Token : CancellationToken.None);
        }

        public void SendRegisterDamageZone(
            float mapCenterX,
            float mapCenterZ,
            float phase1CenterOffset,
            float phase2CenterOffset,
            float initialRadius,
            float phase1EndRadius,
            float finalRadius,
            float totalShrinkDurationSeconds,
            float damageMinPerSecond,
            float damageMaxPerSecond,
            float damageRampSeconds)
        {
            if (!IsReady)
            {
                return;
            }

            _ = SendJsonAsync(new RegisterDamageZoneMessage
            {
                type = "register_damage_zone",
                centerX = mapCenterX,
                centerZ = mapCenterZ,
                phase1CenterOffset = Mathf.Max(0f, phase1CenterOffset),
                phase2CenterOffset = Mathf.Max(0f, phase2CenterOffset),
                initialRadius = Mathf.Max(0f, initialRadius),
                phase1EndRadius = Mathf.Max(0f, phase1EndRadius),
                finalRadius = Mathf.Max(0f, finalRadius),
                totalShrinkDurationSeconds = Mathf.Max(1f, totalShrinkDurationSeconds),
                damageMinPerSecond = Mathf.Max(0f, damageMinPerSecond),
                damageMaxPerSecond = Mathf.Max(0f, damageMaxPerSecond),
                damageRampSeconds = Mathf.Max(1f, damageRampSeconds)
            }, cts != null ? cts.Token : CancellationToken.None);
        }

        [Serializable]
        private sealed class DuelWeaponPickMessage
        {
            public string type;
            public string ticketId;
            public int weaponKind;
        }

        public void SendDuelWeaponPick(int weaponKind)
        {
            if (!IsReady)
            {
                return;
            }

            _ = SendJsonAsync(new DuelWeaponPickMessage
            {
                type = "duel_weapon_pick",
                ticketId = connectedTicketId,
                weaponKind = Mathf.Clamp(weaponKind, 0, (int)WeaponKind.Mp7)
            }, cts != null ? cts.Token : CancellationToken.None);
        }

        public void SendPickupRequest(string spawnId, int targetWeaponSlot = -1)
        {
            if (!IsReady || string.IsNullOrWhiteSpace(spawnId))
            {
                return;
            }

            _ = SendJsonAsync(new PickupRequestMessage
            {
                type = "pickup",
                spawnId = spawnId.Trim(),
                targetWeaponSlot = targetWeaponSlot
            }, cts != null ? cts.Token : CancellationToken.None);
        }

        public void SendWeaponDrop(
            int slotIndex,
            int magAmmo = -1,
            Vector3? dropPosition = null,
            int spareAmmoAssault = -1,
            int spareAmmoSniper = -1,
            int spareAmmoPistol = -1,
            int spareAmmoMp7 = -1)
        {
            if (!IsReady)
            {
                return;
            }

            var message = new WeaponDropRequestMessage
            {
                type = "weapon_drop",
                slotIndex = slotIndex,
                magAmmo = magAmmo,
                spareAmmoAssault = spareAmmoAssault,
                spareAmmoSniper = spareAmmoSniper,
                spareAmmoPistol = spareAmmoPistol,
                spareAmmoMp7 = spareAmmoMp7
            };
            if (dropPosition.HasValue)
            {
                var position = dropPosition.Value;
                message.hasDropPosition = true;
                message.x = position.x;
                message.y = position.y;
                message.z = position.z;
            }

            _ = SendJsonAsync(message, cts != null ? cts.Token : CancellationToken.None);
        }

        public void SendWeaponSwap(int slotA, int slotB)
        {
            if (!IsReady)
            {
                return;
            }

            if (slotA < 0 || slotA > 1 || slotB < 0 || slotB > 1 || slotA == slotB)
            {
                return;
            }

            _ = SendJsonAsync(new WeaponSwapRequestMessage
            {
                type = "weapon_swap",
                slotA = slotA,
                slotB = slotB
            }, cts != null ? cts.Token : CancellationToken.None);
        }

        public void SendInventoryItemDrop(string itemId, int amount, Vector3? dropPosition = null)
        {
            if (!IsReady || string.IsNullOrWhiteSpace(itemId) || amount <= 0)
            {
                return;
            }

            var message = new InventoryItemDropRequestMessage
            {
                type = "inventory_item_drop",
                itemId = itemId.Trim(),
                amount = Mathf.Clamp(amount, 1, 99)
            };

            if (dropPosition.HasValue)
            {
                var position = dropPosition.Value;
                message.hasDropPosition = true;
                message.x = position.x;
                message.y = position.y;
                message.z = position.z;
            }

            _ = SendJsonAsync(message, cts != null ? cts.Token : CancellationToken.None);
        }

        public void SendAmmoState(
            int spareAmmoAssault,
            int spareAmmoSniper,
            int spareAmmoPistol,
            int spareAmmoMp7,
            int slot0MagAmmo = -1,
            int slot1MagAmmo = -1)
        {
            if (!IsReady)
            {
                return;
            }

            _ = SendJsonAsync(new AmmoStateMessage
            {
                type = "ammo_state",
                spareAmmoAssault = Mathf.Clamp(spareAmmoAssault, 0, 999),
                spareAmmoSniper = Mathf.Clamp(spareAmmoSniper, 0, 999),
                spareAmmoPistol = Mathf.Clamp(spareAmmoPistol, 0, 999),
                spareAmmoMp7 = Mathf.Clamp(spareAmmoMp7, 0, 999),
                slot0MagAmmo = Mathf.Clamp(slot0MagAmmo, -1, 999),
                slot1MagAmmo = Mathf.Clamp(slot1MagAmmo, -1, 999)
            }, cts != null ? cts.Token : CancellationToken.None);
        }

        public void SendMedkitUse()
        {
            if (!IsConnected)
            {
                return;
            }

            _ = SendJsonAsync(new MedkitUseMessage
            {
                type = "medkit_use"
            }, cts != null ? cts.Token : CancellationToken.None);
        }

        public void SendMedkitCancel()
        {
            if (!IsConnected)
            {
                return;
            }

            _ = SendJsonAsync(new MedkitCancelMessage
            {
                type = "medkit_cancel"
            }, cts != null ? cts.Token : CancellationToken.None);
        }

        public void SendPlaneJump()
        {
            if (!IsConnected)
            {
                return;
            }

            _ = SendJsonAsync(new PlaneJumpMessage
            {
                type = "plane_jump"
            }, cts != null ? cts.Token : CancellationToken.None);
        }

        public void SendPlaneLanded()
        {
            if (!IsConnected)
            {
                return;
            }

            _ = SendJsonAsync(new PlaneLandedMessage
            {
                type = "plane_landed"
            }, cts != null ? cts.Token : CancellationToken.None);
        }

        public void SendDeathFallLanded()
        {
            if (!IsConnected)
            {
                return;
            }

            _ = SendJsonAsync(new DeathFallLandedMessage
            {
                type = "death_fall_landed"
            }, cts != null ? cts.Token : CancellationToken.None);
        }

        public void SendPlayerLand()
        {
            if (!IsConnected)
            {
                return;
            }

            _ = SendJsonAsync(new PlayerLandMessage
            {
                type = "player_land",
                ticketId = connectedTicketId
            }, cts != null ? cts.Token : CancellationToken.None);
        }

        private IEnumerator ConnectCoroutine(string ticketId, int generation)
        {
            try
            {
                Debug.Log($"[RealtimeTransportClient] WS connect starting url={websocketUrl} ticket={ticketId}");

                yield return null;
                yield return new WaitForEndOfFrame();

                if (!IsConnectGenerationCurrent(generation))
                {
                    yield break;
                }

                var disconnectTask = DisconnectInternalAsync(preserveConnecting: true);
            while (!disconnectTask.IsCompleted)
            {
                yield return null;
                if (!IsConnectGenerationCurrent(generation))
                {
                    yield break;
                }
            }

            if (disconnectTask.IsFaulted)
            {
                yield return CleanupAfterFailedConnect(
                    disconnectTask.Exception?.GetBaseException() ?? disconnectTask.Exception,
                    generation);
                yield break;
            }

            if (!IsConnectGenerationCurrent(generation))
            {
                yield break;
            }

                cts = new CancellationTokenSource();
                sendSemaphore = new SemaphoreSlim(1, 1);
                hasJoinAck = false;
                ResetPoseSendCache();

                if (string.IsNullOrWhiteSpace(websocketUrl))
                {
                    yield return CleanupAfterFailedConnect(
                        new InvalidOperationException("Realtime websocket URL is not configured."),
                        generation);
                    yield break;
                }

                Exception lastConnectError = null;
                var connected = false;
                for (var attempt = 0; attempt < MaxConnectAttemptsPerCycle; attempt++)
                {
                    if (!IsConnectGenerationCurrent(generation))
                    {
                        yield break;
                    }

                    if (attempt > 0)
                    {
                        yield return new WaitForSecondsRealtime(0.2f + (0.15f * attempt));
                        if (!IsConnectGenerationCurrent(generation))
                        {
                            yield break;
                        }
                    }

                    if (socket != null)
                    {
                        try
                        {
                            socket.Dispose();
                        }
                        catch
                        {
                            // ignored
                        }

                        socket = null;
                    }

                    var attemptSocketGeneration = ++socketGeneration;
                    socket = new ClientWebSocket();
                    Task connectTask = null;
                    Exception connectStartError = null;
                    try
                    {
                        connectTask = socket.ConnectAsync(new Uri(websocketUrl), cts.Token);
                    }
                    catch (Exception ex)
                    {
                        connectStartError = ex;
                    }

                    if (connectStartError != null)
                    {
                        lastConnectError = connectStartError;
                        continue;
                    }

                    while (connectTask != null && !connectTask.IsCompleted)
                    {
                        yield return null;
                        if (!IsConnectGenerationCurrent(generation))
                        {
                            yield break;
                        }
                    }

                    if (!IsConnectGenerationCurrent(generation))
                    {
                        yield break;
                    }

                    if (connectTask != null && !connectTask.IsFaulted && !connectTask.IsCanceled &&
                        socket != null && socket.State == WebSocketState.Open &&
                        attemptSocketGeneration == socketGeneration)
                    {
                        connected = true;
                        break;
                    }

                    lastConnectError = connectTask?.Exception?.GetBaseException()
                                       ?? new Exception("WebSocket connect task failed.");
                }

                if (!connected)
                {
                    yield return CleanupAfterFailedConnect(lastConnectError, generation);
                    yield break;
                }

                socketOpenedAtMonotonic = MonotonicNowSeconds;
                connectedTicketId = ticketId;
                Task joinTask = null;
                Exception joinStartError = null;
                try
                {
                    joinTask = SendJsonAsync(new JoinMessage
                    {
                        type = "join",
                        ticketId = ticketId
                    }, cts.Token, required: true);
                }
                catch (Exception ex)
                {
                    joinStartError = ex;
                }

                if (joinStartError != null)
                {
                    yield return CleanupAfterFailedConnect(joinStartError, generation);
                    yield break;
                }

                while (joinTask != null && !joinTask.IsCompleted)
                {
                    yield return null;
                    if (!IsConnectGenerationCurrent(generation))
                    {
                        yield break;
                    }
                }

                if (!IsConnectGenerationCurrent(generation))
                {
                    yield break;
                }

                if (joinTask == null || joinTask.IsFaulted || joinTask.IsCanceled ||
                    socket == null || socket.State != WebSocketState.Open)
                {
                    var fault = joinTask?.Exception?.GetBaseException()
                                ?? new Exception("WebSocket join send failed.");
                    yield return CleanupAfterFailedConnect(fault, generation);
                    yield break;
                }

                var activeSocketGeneration = socketGeneration;
                receiveTask = ReceiveLoopAsync(socket, cts.Token, activeSocketGeneration);

                var joinWaitUntil = Time.unscaledTime + 8f;
                while (!hasJoinAck && Time.unscaledTime < joinWaitUntil)
                {
                    yield return null;
                    if (!IsConnectGenerationCurrent(generation))
                    {
                        yield break;
                    }
                }

                if (!hasJoinAck)
                {
                    yield return CleanupAfterFailedConnect(
                        new TimeoutException("WebSocket join acknowledgement timed out."),
                        generation);
                    yield break;
                }

                reconnectBackoffSeconds = 0.35f;
                connectAttemptStartedAtMonotonic = -1f;
                Debug.Log($"[RealtimeTransportClient] WS connected url={websocketUrl} ticket={ticketId}");
            }
            finally
            {
                if (IsConnectGenerationCurrent(generation))
                {
                    FinishConnectAttempt(generation);
                }
            }
        }

        private bool IsConnectGenerationCurrent(int generation)
        {
            return generation == connectGeneration;
        }

        private void FinishConnectAttempt(int generation)
        {
            if (!IsConnectGenerationCurrent(generation))
            {
                return;
            }

            isConnecting = false;
            pendingConnectTicketId = string.Empty;
            connectCoroutine = null;
        }

        private IEnumerator CleanupAfterFailedConnect(Exception connectError, int generation)
        {
            if (connectError != null)
            {
                Debug.LogError(
                    $"[RealtimeTransportClient] Connect failed url={websocketUrl}: {connectError.GetType().Name}: {connectError.Message}\n{connectError.StackTrace}");
            }

            if (IsConnectGenerationCurrent(generation))
            {
                reconnectBackoffSeconds = Mathf.Min(reconnectBackoffSeconds * 1.5f, MaxReconnectBackoffSeconds);
                nextReconnectAllowedAt = Time.unscaledTime + reconnectBackoffSeconds;
            }

            var cleanupTask = DisconnectInternalAsync(preserveConnecting: true);
            while (!cleanupTask.IsCompleted)
            {
                yield return null;
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket ownerSocket, CancellationToken token, int ownerSocketGeneration)
        {
            var buffer = new byte[16384];
            var segment = new ArraySegment<byte>(buffer);
            var textBuilder = new StringBuilder(4096);
            var binaryBuilder = new List<byte>(8192);

            while (!token.IsCancellationRequested && ownerSocket != null && ownerSocket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await ownerSocket.ReceiveAsync(segment, token);
                }
                catch
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    for (var i = 0; i < result.Count; i++)
                    {
                        binaryBuilder.Add(buffer[i]);
                    }

                    if (!result.EndOfMessage)
                    {
                        continue;
                    }

                    TryHandleIncomingBinary(binaryBuilder.ToArray());
                    binaryBuilder.Clear();
                    continue;
                }

                var chunk = Encoding.UTF8.GetString(buffer, 0, result.Count);
                textBuilder.Append(chunk);

                if (!result.EndOfMessage)
                {
                    continue;
                }

                var json = textBuilder.ToString();
                textBuilder.Clear();
                TryHandleIncomingJson(json);
            }

            if (!token.IsCancellationRequested &&
                ownerSocketGeneration == socketGeneration &&
                sessionActive &&
                autoReconnectEnabled)
            {
                EnqueueMainThreadAction(() =>
                {
                    if (ownerSocketGeneration != socketGeneration)
                    {
                        return;
                    }

                    reconnectBackoffSeconds = Mathf.Min(reconnectBackoffSeconds * 1.5f, MaxReconnectBackoffSeconds);
                    nextReconnectAllowedAt = Time.unscaledTime + reconnectBackoffSeconds;
                    EnsureConnected();
                });
            }

            await DisconnectInternalAsync(ownerSocket, ownerSocketGeneration);
        }

        private void TryHandleIncomingJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            RealtimeSnapshot snapshot;
            try
            {
                snapshot = JsonUtility.FromJson<RealtimeSnapshot>(json);
            }
            catch
            {
                return;
            }

            if (snapshot == null || !string.Equals(snapshot.type, "snapshot", StringComparison.Ordinal))
            {
                DamageMessage damageMessage = null;
                try
                {
                    damageMessage = JsonUtility.FromJson<DamageMessage>(json);
                }
                catch
                {
                    // ignored
                }

                if (damageMessage != null && string.Equals(damageMessage.type, "damage", StringComparison.Ordinal))
                {
                    var message = damageMessage;
                    EnqueueMainThreadAction(() => DamageReceived?.Invoke(message), critical: true);
                    return;
                }

                PickupStateMessage pickupStateMessage = null;
                try
                {
                    pickupStateMessage = JsonUtility.FromJson<PickupStateMessage>(json);
                }
                catch
                {
                    // ignored
                }

                if (pickupStateMessage != null &&
                    string.Equals(pickupStateMessage.type, "pickup_state", StringComparison.Ordinal))
                {
                    EnqueueCoalescedPickupState(pickupStateMessage);
                    return;
                }

                PickupEventMessage pickupEventMessage = null;
                try
                {
                    pickupEventMessage = JsonUtility.FromJson<PickupEventMessage>(json);
                }
                catch
                {
                    // ignored
                }

                if (pickupEventMessage != null &&
                    string.Equals(pickupEventMessage.type, "pickup_event", StringComparison.Ordinal))
                {
                    var message = pickupEventMessage;
                    EnqueueMainThreadAction(() => PickupEventReceived?.Invoke(message), critical: true);
                    return;
                }

                PickupResultMessage pickupResultMessage = null;
                try
                {
                    pickupResultMessage = JsonUtility.FromJson<PickupResultMessage>(json);
                }
                catch
                {
                    // ignored
                }

                if (pickupResultMessage != null &&
                    string.Equals(pickupResultMessage.type, "pickup_result", StringComparison.Ordinal))
                {
                    var message = pickupResultMessage;
                    EnqueueMainThreadAction(() => PickupResultReceived?.Invoke(message), critical: true);
                    return;
                }

                WeaponDropResultMessage weaponDropResultMessage = null;
                try
                {
                    weaponDropResultMessage = JsonUtility.FromJson<WeaponDropResultMessage>(json);
                }
                catch
                {
                    // ignored
                }

                if (weaponDropResultMessage != null &&
                    string.Equals(weaponDropResultMessage.type, "weapon_drop_result", StringComparison.Ordinal))
                {
                    var message = weaponDropResultMessage;
                    EnqueueMainThreadAction(() => WeaponDropResultReceived?.Invoke(message), critical: true);
                    return;
                }

                WeaponSwapResultMessage weaponSwapResultMessage = null;
                try
                {
                    weaponSwapResultMessage = JsonUtility.FromJson<WeaponSwapResultMessage>(json);
                }
                catch
                {
                    // ignored
                }

                if (weaponSwapResultMessage != null &&
                    string.Equals(weaponSwapResultMessage.type, "weapon_swap_result", StringComparison.Ordinal))
                {
                    var message = weaponSwapResultMessage;
                    EnqueueMainThreadAction(() => WeaponSwapResultReceived?.Invoke(message), critical: true);
                    return;
                }

                InventoryItemDropResultMessage inventoryItemDropResultMessage = null;
                try
                {
                    inventoryItemDropResultMessage = JsonUtility.FromJson<InventoryItemDropResultMessage>(json);
                }
                catch
                {
                    // ignored
                }

                if (inventoryItemDropResultMessage != null &&
                    string.Equals(inventoryItemDropResultMessage.type, "inventory_item_drop_result", StringComparison.Ordinal))
                {
                    var message = inventoryItemDropResultMessage;
                    EnqueueMainThreadAction(() => InventoryItemDropResultReceived?.Invoke(message), critical: true);
                    return;
                }

                MedkitResultMessage medkitResultMessage = null;
                try
                {
                    medkitResultMessage = JsonUtility.FromJson<MedkitResultMessage>(json);
                }
                catch
                {
                    // ignored
                }

                if (medkitResultMessage != null &&
                    string.Equals(medkitResultMessage.type, "medkit_result", StringComparison.Ordinal))
                {
                    var message = medkitResultMessage;
                    EnqueueMainThreadAction(() => MedkitResultReceived?.Invoke(message), critical: true);
                    return;
                }

                HealMessage healMessage = null;
                try
                {
                    healMessage = JsonUtility.FromJson<HealMessage>(json);
                }
                catch
                {
                    // ignored
                }

                if (healMessage != null && string.Equals(healMessage.type, "heal", StringComparison.Ordinal))
                {
                    var message = healMessage;
                    EnqueueMainThreadAction(() => HealReceived?.Invoke(message), critical: true);
                    return;
                }

                DamageZoneStateMessage damageZoneStateMessage = null;
                try
                {
                    damageZoneStateMessage = JsonUtility.FromJson<DamageZoneStateMessage>(json);
                }
                catch
                {
                    // ignored
                }

                if (damageZoneStateMessage != null &&
                    string.Equals(damageZoneStateMessage.type, "zone_state", StringComparison.Ordinal))
                {
                    EnqueueCoalescedZoneState(damageZoneStateMessage);
                    return;
                }

                MatchStateMessage matchStateMessage = null;
                try
                {
                    matchStateMessage = JsonUtility.FromJson<MatchStateMessage>(json);
                }
                catch
                {
                    // ignored
                }

                if (matchStateMessage != null &&
                    string.Equals(matchStateMessage.type, "match_state", StringComparison.Ordinal))
                {
                    EnqueueCoalescedMatchState(matchStateMessage);
                    return;
                }

                MatchDisconnectMessage matchDisconnectMessage = null;
                try
                {
                    matchDisconnectMessage = JsonUtility.FromJson<MatchDisconnectMessage>(json);
                }
                catch
                {
                    // ignored
                }

                if (matchDisconnectMessage != null &&
                    string.Equals(matchDisconnectMessage.type, "match_disconnect", StringComparison.Ordinal))
                {
                    var message = matchDisconnectMessage;
                    EnqueueMainThreadAction(() => MatchDisconnectReceived?.Invoke(message), critical: true);
                    return;
                }

                KillFeedMessage killFeedMessage = null;
                try
                {
                    killFeedMessage = JsonUtility.FromJson<KillFeedMessage>(json);
                }
                catch
                {
                    // ignored
                }

                if (killFeedMessage != null &&
                    string.Equals(killFeedMessage.type, "kill_feed", StringComparison.Ordinal))
                {
                    var message = killFeedMessage;
                    EnqueueMainThreadAction(() => KillFeedReceived?.Invoke(message), critical: true);
                    return;
                }

                MatchStatsMessage matchStatsMessage = null;
                try
                {
                    matchStatsMessage = JsonUtility.FromJson<MatchStatsMessage>(json);
                }
                catch
                {
                    // ignored
                }

                if (matchStatsMessage != null &&
                    string.Equals(matchStatsMessage.type, "match_stats", StringComparison.Ordinal))
                {
                    var message = matchStatsMessage;
                    EnqueueMainThreadAction(() => MatchStatsReceived?.Invoke(message), critical: true);
                    return;
                }

                PlayerLandMessage playerLandMessage = null;
                try
                {
                    playerLandMessage = JsonUtility.FromJson<PlayerLandMessage>(json);
                }
                catch
                {
                    // ignored
                }

                if (playerLandMessage != null &&
                    string.Equals(playerLandMessage.type, "player_land", StringComparison.Ordinal))
                {
                    var message = playerLandMessage;
                    EnqueueMainThreadAction(() => PlayerLandReceived?.Invoke(message));
                    return;
                }

                PongMessage pongMessage = null;
                try
                {
                    pongMessage = JsonUtility.FromJson<PongMessage>(json);
                }
                catch
                {
                    // ignored
                }

                if (pongMessage != null && string.Equals(pongMessage.type, "pong", StringComparison.Ordinal))
                {
                    ApplyPongSample(pongMessage.clientTimeMs);
                    return;
                }

                var joined = JsonUtility.FromJson<JoinedMessage>(json);
                if (joined != null &&
                    string.Equals(joined.type, "joined", StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(joined.ticketId))
                {
                    var ackTicketId = joined.ticketId.Trim();
                    if (string.IsNullOrWhiteSpace(connectedTicketId))
                    {
                        connectedTicketId = ackTicketId;
                    }

                    if (string.Equals(ackTicketId, connectedTicketId, StringComparison.Ordinal))
                    {
                        hasJoinAck = true;
                        ResetPoseSendCache();
                        EnqueueMainThreadAction(() =>
                        {
                            MovementNetworkDiagnostics.LogWsState("joined", connectedTicketId, "ready=1");
                            JoinAcknowledged?.Invoke();
                        });
                    }
                }
                return;
            }

            ApplyIncomingSnapshot(snapshot);
        }

        private void TryHandleIncomingBinary(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return;
            }

            if (RealtimeSnapshotBinaryCodec.TryDecode(data, out var snapshot))
            {
            if (snapshot != null && snapshot.binaryVersion > 0)
            {
                MovementNetworkDiagnostics.LogSnapshotDecodeOk(snapshot.binaryVersion, snapshot.serverTick);
            }
            else if (snapshot?.players != null && snapshot.players.Length > 0 &&
                     Time.unscaledTime - lastSnapshotDecodeErrorLogAt > 5f)
            {
                lastSnapshotDecodeErrorLogAt = Time.unscaledTime;
                Debug.Log(
                    $"[RealtimeTransportClient] JSON snapshot ok tick={snapshot.serverTick} " +
                    $"players={snapshot.players.Length}");
            }

            ApplyIncomingSnapshot(snapshot);
                return;
            }

            if (data.Length >= 2 && data[0] == (byte)'{' &&
                RealtimeSnapshotBinaryCodec.TryDecodeJsonSnapshot(data, out snapshot))
            {
                ApplyIncomingSnapshot(snapshot);
                return;
            }

            if (Time.unscaledTime - lastSnapshotDecodeErrorLogAt > 2f)
            {
                lastSnapshotDecodeErrorLogAt = Time.unscaledTime;
                var magic = data.Length >= 4
                    ? Encoding.UTF8.GetString(data, 0, 4)
                    : "?";
                Debug.LogWarning(
                    $"[MoveDiag][snapshot-decode-fail] len={data.Length} magic={magic} " +
                    "Deploy updated QueueService (snapshotBinary.js field order fix).");
            }
        }

        private void ApplyIncomingSnapshot(RealtimeSnapshot snapshot)
        {
            if (snapshot == null || !string.Equals(snapshot.type, "snapshot", StringComparison.Ordinal))
            {
                return;
            }

            if (!hasJoinAck)
            {
                hasJoinAck = true;
                ResetPoseSendCache();
                EnqueueMainThreadAction(() =>
                {
                    MovementNetworkDiagnostics.LogWsState("joined-via-snapshot", connectedTicketId, "ready=1");
                    JoinAcknowledged?.Invoke();
                });
            }

            lock (snapshotLock)
            {
                pendingSnapshotQueue.Enqueue(snapshot);
                while (pendingSnapshotQueue.Count > MaxPendingSnapshots)
                {
                    pendingSnapshotQueue.Dequeue();
                }

                latestSnapshot = snapshot;
                hasLatestSnapshot = true;
                lastSnapshotReceivedUnscaledTime = MonotonicNowSeconds;
                snapshotsCounter++;
                if (snapshot.serverTick > 0)
                {
                    LatestServerTick = snapshot.serverTick;
                }

                if (snapshot.serverTickRate > 0)
                {
                    LatestServerTickRate = snapshot.serverTickRate;
                }

                if (snapshot.movementSampleRateHz > 0)
                {
                    LatestMovementSampleRate = snapshot.movementSampleRateHz;
                }
                else if (snapshot.serverTickRate > 0 && snapshot.serverTickRate <= 128)
                {
                    // v12 fallback: old servers reused serverTickRate for pose timeline.
                    LatestMovementSampleRate = snapshot.serverTickRate;
                }
            }
        }

        private void ApplyPongSample(double sentClientTimeMs)
        {
            var nowMs = MonotonicClock.Elapsed.TotalMilliseconds;
            var rttMs = (int)Math.Max(1.0, nowMs - sentClientTimeMs);
            if (ShouldRejectPingSample(rttMs))
            {
                return;
            }

            lastRoundTripMs = rttMs;
            if (smoothedRoundTripMs <= 0)
            {
                smoothedRoundTripMs = rttMs;
            }
            else
            {
                smoothedRoundTripMs = Mathf.RoundToInt(Mathf.Lerp(smoothedRoundTripMs, rttMs, 0.45f));
            }
        }

        private bool ShouldRejectPingSample(int rttMs)
        {
            if (smoothedRoundTripMs <= 0)
            {
                return rttMs > 750;
            }

            if (rttMs <= smoothedRoundTripMs * 3)
            {
                return false;
            }

            return rttMs > Mathf.Max(120, smoothedRoundTripMs * 2);
        }

        private async Task SendBinaryAsync(byte[] payload, CancellationToken token)
        {
            var targetSocket = socket;
            var semaphore = sendSemaphore;
            if (payload == null || payload.Length == 0 || targetSocket == null ||
                targetSocket.State != WebSocketState.Open || semaphore == null)
            {
                return;
            }

            var segment = new ArraySegment<byte>(payload);
            try
            {
                await semaphore.WaitAsync(token);
            }
            catch
            {
                return;
            }

            try
            {
                if (targetSocket.State == WebSocketState.Open)
                {
                    await targetSocket.SendAsync(segment, WebSocketMessageType.Binary, true, token);
                }
            }
            catch
            {
                if (Time.unscaledTime - lastSendErrorLogAt > 1f)
                {
                    lastSendErrorLogAt = Time.unscaledTime;
                    Debug.LogWarning("[RealtimeTransportClient] Binary send failed");
                }
            }
            finally
            {
                try
                {
                    semaphore.Release();
                }
                catch
                {
                    // Socket teardown can dispose the semaphore while a send is finishing.
                }
            }
        }

        private async Task<bool> SendJsonAsync(object payload, CancellationToken token, bool required = false)
        {
            var targetSocket = socket;
            var semaphore = sendSemaphore;
            if (targetSocket == null || targetSocket.State != WebSocketState.Open || semaphore == null)
            {
                if (required)
                {
                    throw new InvalidOperationException("WebSocket is not ready to send JSON.");
                }

                return false;
            }

            var json = JsonUtility.ToJson(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(bytes);

            try
            {
                await semaphore.WaitAsync(token);
            }
            catch
            {
                if (required)
                {
                    throw;
                }

                return false;
            }

            try
            {
                if (targetSocket.State == WebSocketState.Open)
                {
                    await targetSocket.SendAsync(segment, WebSocketMessageType.Text, true, token);
                    return true;
                }

                if (required)
                {
                    throw new InvalidOperationException("WebSocket closed before JSON send completed.");
                }

                return false;
            }
            catch (Exception ex)
            {
                if (Time.unscaledTime - lastSendErrorLogAt > 1f)
                {
                    lastSendErrorLogAt = Time.unscaledTime;
                    Debug.LogWarning($"[RealtimeTransportClient] Send failed payload={payload?.GetType().Name ?? "null"}: {ex.Message}");
                }

                if (required)
                {
                    throw;
                }

                return false;
            }
            finally
            {
                try
                {
                    semaphore.Release();
                }
                catch
                {
                    // Socket teardown can dispose the semaphore while a send is finishing.
                }
            }
        }

        private async Task FlushLatestPoseLoopAsync()
        {
            if (poseSendLoopRunning)
            {
                return;
            }

            poseSendLoopRunning = true;
            try
            {
                while (hasPendingPose && socket != null && socket.State == WebSocketState.Open)
                {
                    var msg = pendingPoseMessage;
                    var binary = pendingPoseBinary;
                    hasPendingPose = false;
                    pendingPoseBinary = null;

                    if (binary != null && binary.Length > 0)
                    {
                        await SendBinaryAsync(binary, cts != null ? cts.Token : CancellationToken.None);
                    }
                    else
                    {
                        await SendJsonAsync(msg, cts != null ? cts.Token : CancellationToken.None);
                    }

                    RememberSentPose(msg);
                }
            }
            catch
            {
                // ignored; regular reconnect flow handles broken socket.
            }
            finally
            {
                poseSendLoopRunning = false;
            }
        }

        private async Task DisconnectInternalAsync(
            ClientWebSocket ownerSocket = null,
            int ownerSocketGeneration = -1,
            bool preserveConnecting = false)
        {
            if (ownerSocket != null)
            {
                if (socket != ownerSocket)
                {
                    return;
                }

                if (ownerSocketGeneration >= 0 && ownerSocketGeneration != socketGeneration)
                {
                    return;
                }
            }

            var localCts = cts;
            cts = null;

            try
            {
                localCts?.Cancel();
            }
            catch
            {
                // ignored
            }

            if (socket != null)
            {
                try
                {
                    if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "disconnect", CancellationToken.None);
                    }
                }
                catch
                {
                    // ignored
                }
                finally
                {
                    socket.Dispose();
                    socket = null;
                }
            }

            var pendingReceive = receiveTask;
            receiveTask = null;
            if (pendingReceive != null)
            {
                _ = pendingReceive.ContinueWith(_ => { }, TaskScheduler.Default);
            }

            if (sendSemaphore != null)
            {
                sendSemaphore.Dispose();
                sendSemaphore = null;
            }

            connectedTicketId = string.Empty;
            if (!preserveConnecting)
            {
                isConnecting = false;
                pendingConnectTicketId = string.Empty;
                connectAttemptStartedAtMonotonic = -1f;
            }

            if (!preserveConnecting)
            {
                socketOpenedAtMonotonic = -1f;
            }

            hasJoinAck = false;
            ResetPoseSendCache();
            MovementNetworkDiagnostics.LogWsState("disconnected", string.Empty, "snapshots_cleared=1");
            nextPoseSeq = 0;
            hasPendingPose = false;
            pendingPoseBinary = null;
            poseSendLoopRunning = false;
            hasLastSentPose = false;
            lastPoseSentUnscaledTime = -999f;
            lastSentPoseMessage = null;
            lock (snapshotLock)
            {
                latestSnapshot = null;
                hasLatestSnapshot = false;
                pendingSnapshotQueue.Clear();
                lastSnapshotReceivedUnscaledTime = 0f;
            }

            localCts?.Dispose();
        }
    }
}
