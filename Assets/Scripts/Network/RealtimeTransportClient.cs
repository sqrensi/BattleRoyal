using ShooterPrototype.Player;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ShooterPrototype.Network
{
    public sealed class RealtimeTransportClient : MonoBehaviour
    {
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
            public string characterModel;
            public PositionDto position;
            public float yaw;
            public float lookPitch;
            public int shotSeq;
            public int reloadSeq;
            public int hitPlayerSeq;
            public int footstepSeq;
            public bool isCrouching;
            public bool isSprinting;
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
            public int weaponSlot0Kind;
            public int weaponSlot1Kind;
            public string weaponSlot0ItemId;
            public string weaponSlot1ItemId;
            public int activeWeaponSlot;
            public bool bothHolstered;
            public string droppedSpawnId;
            public int magAmmo;
            public int reserveAmmo;
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
        }

    [Serializable]
    private sealed class WeaponDropRequestMessage
    {
        public string type;
        public int slotIndex;
        public int magAmmo;
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
        private sealed class PickupRequestMessage
        {
            public string type;
            public string spawnId;
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
            public float dirX;
            public float dirY;
            public float dirZ;
        }

        [Serializable]
        private sealed class PoseMessage
        {
            public string type;
            public string characterModel;
            public PositionDto position;
            public float yaw;
            public float lookPitch;
            public int shotSeq;
            public int reloadSeq;
            public int hitPlayerSeq;
            public int footstepSeq;
            public bool isCrouching;
            public bool isSprinting;
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
            public long clientTimeMs;
        }

        [Serializable]
        private sealed class PongMessage
        {
            public string type;
            public long clientTimeMs;
        }

        [SerializeField] private string websocketUrl = "ws://127.0.0.1:5051";

        private ClientWebSocket socket;
        private CancellationTokenSource cts;
        private SemaphoreSlim sendSemaphore;
        private Task receiveTask;
        private string connectedTicketId = string.Empty;
        private string pendingConnectTicketId = string.Empty;
        private bool isConnecting;
        private bool hasJoinAck;
        private float nextReconnectAllowedAt;
        private PoseMessage pendingPoseMessage;
        private int nextPoseSeq;
        private bool hasPendingPose;
        private bool poseSendLoopRunning;
        private float lastSendErrorLogAt;
        private float lastSnapshotDecodeErrorLogAt;
        private readonly object snapshotLock = new object();
        private RealtimeSnapshot latestSnapshot;
        private bool hasLatestSnapshot;
        private float lastPingSentUnscaledTime = -10f;
        private int smoothedRoundTripMs = -1;
        private float lastSnapshotReceivedUnscaledTime;
        private Coroutine pingCoroutine;
        private readonly object mainThreadActionsLock = new object();
        private readonly Queue<Action> mainThreadActions = new Queue<Action>();

        public bool IsConnected => socket != null && socket.State == WebSocketState.Open;
        public bool IsConnecting => isConnecting;
        public bool IsReady => IsConnected && hasJoinAck;
        public string ConnectedTicketId => connectedTicketId;
        public int SmoothedRoundTripMs => smoothedRoundTripMs;
        public float LastSnapshotReceivedUnscaledTime => lastSnapshotReceivedUnscaledTime;
        public int LatestServerTick { get; private set; }
        public int LatestServerTickRate { get; private set; } = 128;
        public event Action<DamageMessage> DamageReceived;
        public event Action<PickupStateMessage> PickupStateReceived;
        public event Action<PickupEventMessage> PickupEventReceived;
        public event Action<PickupResultMessage> PickupResultReceived;
        public event Action<WeaponDropResultMessage> WeaponDropResultReceived;
        public event Action<MedkitResultMessage> MedkitResultReceived;
        public event Action<HealMessage> HealReceived;

        private void Update()
        {
            ProcessMainThreadActions();
        }

        private void EnqueueMainThreadAction(Action action)
        {
            if (action == null)
            {
                return;
            }

            lock (mainThreadActionsLock)
            {
                mainThreadActions.Enqueue(action);
            }
        }

        private void ProcessMainThreadActions()
        {
            while (true)
            {
                Action action;
                lock (mainThreadActionsLock)
                {
                    if (mainThreadActions.Count == 0)
                    {
                        return;
                    }

                    action = mainThreadActions.Dequeue();
                }

                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        public void Configure(string wsUrl)
        {
            if (!string.IsNullOrWhiteSpace(wsUrl))
            {
                websocketUrl = wsUrl.Trim();
            }
        }

        public void Connect(string ticketId)
        {
            if (string.IsNullOrWhiteSpace(ticketId))
            {
                return;
            }

            ticketId = ticketId.Trim();
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

            if (IsConnected &&
                string.Equals(connectedTicketId, ticketId, StringComparison.Ordinal) &&
                !IsReady)
            {
                return;
            }

            if (isConnecting)
            {
                return;
            }

            isConnecting = true;
            pendingConnectTicketId = ticketId;
            _ = ConnectInternalAsync(ticketId);
        }

        public void Disconnect()
        {
            _ = DisconnectInternalAsync();
        }

        public void SendPose(
            Vector3 position,
            float yaw,
            string characterModel = "",
            float lookPitch = 0f,
            int shotSeq = 0,
            int reloadSeq = 0,
            int hitPlayerSeq = 0,
            int footstepSeq = 0,
            bool isCrouching = false,
            bool isSprinting = false,
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
            int weaponPickupSeq = 0)
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
                yaw = yaw,
                lookPitch = lookPitch,
                shotSeq = Math.Max(0, shotSeq),
                reloadSeq = Math.Max(0, reloadSeq),
                hitPlayerSeq = Math.Max(0, hitPlayerSeq),
                footstepSeq = Math.Max(0, footstepSeq),
                isCrouching = isCrouching,
                isSprinting = isSprinting,
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
                weaponKind = Mathf.Clamp(weaponKind, 0, 1),
                weaponSlot0Kind = Mathf.Clamp(weaponSlot0Kind, 0, 255),
                weaponSlot1Kind = Mathf.Clamp(weaponSlot1Kind, 0, 255),
                activeWeaponSlot = Mathf.Clamp(activeWeaponSlot, 0, 255),
                activeWeaponMagAmmo = Mathf.Clamp(activeWeaponMagAmmo, -1, 999),
                weaponPickupSeq = Math.Max(0, weaponPickupSeq),
                poseSeq = ++nextPoseSeq
            };
            hasPendingPose = true;
            if (!poseSendLoopRunning)
            {
                _ = FlushLatestPoseLoopAsync();
            }
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

        public void TickNetworkMeasurement(float intervalSeconds = 1f)
        {
            if (!IsReady || socket == null || sendSemaphore == null)
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
        }

        private void OnDisable()
        {
            if (pingCoroutine != null)
            {
                StopCoroutine(pingCoroutine);
                pingCoroutine = null;
            }
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
            if (!IsReady || socket == null || sendSemaphore == null)
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
                    clientTimeMs = (long)(Time.realtimeSinceStartupAsDouble * 1000.0)
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

        public void SendPickupRequest(string spawnId)
        {
            if (!IsReady || string.IsNullOrWhiteSpace(spawnId))
            {
                return;
            }

            _ = SendJsonAsync(new PickupRequestMessage
            {
                type = "pickup",
                spawnId = spawnId.Trim()
            }, cts != null ? cts.Token : CancellationToken.None);
        }

        public void SendWeaponDrop(int slotIndex, int magAmmo = -1)
        {
            if (!IsReady)
            {
                return;
            }

            _ = SendJsonAsync(new WeaponDropRequestMessage
            {
                type = "weapon_drop",
                slotIndex = slotIndex,
                magAmmo = magAmmo
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

        private async Task ConnectInternalAsync(string ticketId)
        {
            try
            {
                await DisconnectInternalAsync(preserveConnecting: true);
                cts = new CancellationTokenSource();
                sendSemaphore = new SemaphoreSlim(1, 1);
                socket = new ClientWebSocket();
                hasJoinAck = false;
                await socket.ConnectAsync(new Uri(websocketUrl), cts.Token);

                connectedTicketId = ticketId;
                await SendJsonAsync(new JoinMessage
                {
                    type = "join",
                    ticketId = ticketId
                }, cts.Token);

                receiveTask = ReceiveLoopAsync(socket, cts.Token);
                MovementNetworkDiagnostics.LogWsState("connected", ticketId, $"url={websocketUrl}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RealtimeTransportClient] Connect failed: {ex.Message}");
                nextReconnectAllowedAt = Time.unscaledTime + 0.35f;
                await DisconnectInternalAsync(preserveConnecting: true);
            }
            finally
            {
                isConnecting = false;
                pendingConnectTicketId = string.Empty;
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket ownerSocket, CancellationToken token)
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

            if (!token.IsCancellationRequested)
            {
                nextReconnectAllowedAt = Time.unscaledTime + 0.25f;
            }

            await DisconnectInternalAsync(ownerSocket);
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
                    EnqueueMainThreadAction(() => DamageReceived?.Invoke(message));
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
                    var message = pickupStateMessage;
                    EnqueueMainThreadAction(() => PickupStateReceived?.Invoke(message));
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
                    EnqueueMainThreadAction(() => PickupEventReceived?.Invoke(message));
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
                    EnqueueMainThreadAction(() => PickupResultReceived?.Invoke(message));
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
                    EnqueueMainThreadAction(() => WeaponDropResultReceived?.Invoke(message));
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
                    EnqueueMainThreadAction(() => MedkitResultReceived?.Invoke(message));
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
                    EnqueueMainThreadAction(() => HealReceived?.Invoke(message));
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
                    var nowMs = (long)(Time.realtimeSinceStartupAsDouble * 1000.0);
                    var rttMs = (int)Mathf.Max(1f, nowMs - pongMessage.clientTimeMs);
                    if (ShouldRejectPingSample(rttMs))
                    {
                        return;
                    }

                    if (smoothedRoundTripMs <= 0)
                    {
                        smoothedRoundTripMs = rttMs;
                    }
                    else
                    {
                        smoothedRoundTripMs = Mathf.RoundToInt(Mathf.Lerp(smoothedRoundTripMs, rttMs, 0.25f));
                    }

                    return;
                }

                var joined = JsonUtility.FromJson<JoinedMessage>(json);
                if (joined != null &&
                    string.Equals(joined.type, "joined", StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(joined.ticketId) &&
                    string.Equals(joined.ticketId, connectedTicketId, StringComparison.Ordinal))
                {
                    hasJoinAck = true;
                    MovementNetworkDiagnostics.LogWsState("joined", connectedTicketId, "ready=1");
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

            if (!RealtimeSnapshotBinaryCodec.TryDecode(data, out var snapshot))
            {
                if (Time.unscaledTime - lastSnapshotDecodeErrorLogAt > 2f)
                {
                    lastSnapshotDecodeErrorLogAt = Time.unscaledTime;
                    Debug.LogWarning(
                        $"[MoveDiag][snapshot-decode-fail] len={data?.Length ?? 0} " +
                        "Restart QueueService after server updates.");
                }
                return;
            }

            if (snapshot != null && snapshot.binaryVersion > 0)
            {
                MovementNetworkDiagnostics.LogSnapshotDecodeOk(snapshot.binaryVersion, snapshot.serverTick);
            }

            ApplyIncomingSnapshot(snapshot);
        }

        private void ApplyIncomingSnapshot(RealtimeSnapshot snapshot)
        {
            if (snapshot == null || !string.Equals(snapshot.type, "snapshot", StringComparison.Ordinal))
            {
                return;
            }

            lock (snapshotLock)
            {
                latestSnapshot = snapshot;
                hasLatestSnapshot = true;
                lastSnapshotReceivedUnscaledTime = Time.unscaledTime;
                if (snapshot.serverTick > 0)
                {
                    LatestServerTick = snapshot.serverTick;
                }

                if (snapshot.serverTickRate > 0)
                {
                    LatestServerTickRate = snapshot.serverTickRate;
                }
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

        private async Task SendJsonAsync(object payload, CancellationToken token)
        {
            var targetSocket = socket;
            var semaphore = sendSemaphore;
            if (targetSocket == null || targetSocket.State != WebSocketState.Open || semaphore == null)
            {
                return;
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
                return;
            }

            try
            {
                if (targetSocket.State == WebSocketState.Open)
                {
                    await targetSocket.SendAsync(segment, WebSocketMessageType.Text, true, token);
                }
            }
            catch
            {
                if (Time.unscaledTime - lastSendErrorLogAt > 1f)
                {
                    lastSendErrorLogAt = Time.unscaledTime;
                    Debug.LogWarning($"[RealtimeTransportClient] Send failed payload={payload?.GetType().Name ?? "null"}");
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
                    hasPendingPose = false;
                    await SendJsonAsync(msg, cts != null ? cts.Token : CancellationToken.None);
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
            bool preserveConnecting = false)
        {
            if (ownerSocket != null && socket != ownerSocket)
            {
                return;
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
            }
            hasJoinAck = false;
            MovementNetworkDiagnostics.LogWsState("disconnected", string.Empty, "snapshots_cleared=1");
            nextPoseSeq = 0;
            hasPendingPose = false;
            poseSendLoopRunning = false;
            lock (snapshotLock)
            {
                latestSnapshot = null;
                hasLatestSnapshot = false;
                lastSnapshotReceivedUnscaledTime = 0f;
            }

            lock (mainThreadActionsLock)
            {
                mainThreadActions.Clear();
            }

            localCts?.Dispose();
        }

        private void OnDestroy()
        {
            _ = DisconnectInternalAsync();
        }
    }
}
