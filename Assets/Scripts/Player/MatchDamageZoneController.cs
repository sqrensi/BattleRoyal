using System.Collections;
using ShooterPrototype.Network;
using UnityEngine;
using UnityEngine.Serialization;

namespace ShooterPrototype.Player
{
    [DisallowMultipleComponent]
    public sealed class MatchDamageZoneController : MonoBehaviour
    {
        [Header("Zone Center")]
        [Tooltip("Центр всей карты. Фаза 1: центр зоны ±offset от этой точки. Фаза 2: новый центр внутри безопасной зоны фазы 1.")]
        [SerializeField] private Transform zoneCenter;
        [SerializeField] private float phase1CenterOffset = 10f;
        [SerializeField] private float phase2CenterOffset = 40f;
        [SerializeField] private float centerGroundProbeHeight = 12f;
        [SerializeField] private float centerGroundProbeDistance = 40f;
        [SerializeField] private LayerMask centerGroundMask = ~0;

        [Header("Shrink Phases")]
        [Tooltip("Фаза 1: сужение до 100 в первоначальном центре. Фаза 2: та же зона едет к финальному центру и сужается до 0.")]
        [SerializeField] private float initialRadius = 220f;
        [SerializeField] private float phase1EndRadius = 100f;
        [SerializeField] private float finalRadius = 0f;
        [FormerlySerializedAs("phase1DurationSeconds")]
        [SerializeField] private float totalShrinkDurationSeconds = 180f;
        [SerializeField] private AnimationCurve shrinkCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Damage")]
        [SerializeField] private float damageMinPerSecond = 0.5f;
        [SerializeField] private float damageMaxPerSecond = 14f;
        [SerializeField] private float damageRampSeconds = 480f;
        [SerializeField] private AnimationCurve damageCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Visual")]
        [SerializeField] private GameObject damageZoneVisualPrefab;
        [SerializeField] private float visualHeightOffset = 0.05f;

        [Header("Runtime")]
        [SerializeField] private bool applyLocalDamageWhenOffline = true;

        private Transform visualRoot;
        private float visualBaseDiameter = 100f;
        private float displayedRadius;
        private float targetRadius;
        private Vector3 displayedCenter;
        private Vector3 targetCenter;
        private Vector3 phase1CenterWorld;
        private Vector3 phase2CenterWorld;
        private float elapsedSeconds;
        private int currentPhase = 1;
        private double startedAtMs;
        private bool hasAuthoritativeStart;
        private bool useServerAuthority;
        private bool registeredWithServer;
        private bool phasesConfigured;
        private RealtimeTransportClient transportClient;
        private Coroutine networkRoutine;
        private float localDamageAccumulator;

        public Vector3 ZoneCenter => phasesConfigured ? targetCenter : GetMapCenterPosition();
        public float CurrentRadius => displayedRadius;
        public float TargetRadius => targetRadius;
        public int CurrentPhase => currentPhase;
        public float ElapsedSeconds => elapsedSeconds;
        public float CurrentDamagePerSecond => DamageZoneMath.EvaluateDamagePerSecond(
            damageMinPerSecond,
            damageMaxPerSecond,
            elapsedSeconds,
            damageRampSeconds,
            damageCurve);

        private void Awake()
        {
            if (zoneCenter == null)
            {
                zoneCenter = transform;
            }

            var hasNetworkClient = FindFirstObjectByType<RealtimeTransportClient>() != null;
            if (!hasNetworkClient)
            {
                ConfigureLocalPhases();
            }

            SpawnVisual();
            if (visualRoot != null)
            {
                visualRoot.gameObject.SetActive(false);
            }
            displayedRadius = initialRadius;
            targetRadius = initialRadius;
            displayedCenter = GetMapCenterPosition();
            targetCenter = displayedCenter;
            if (phasesConfigured)
            {
                RefreshTargetState();
                displayedRadius = targetRadius;
                displayedCenter = targetCenter;
            }
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

        private void Update()
        {
            AdvanceElapsedTime();
            RefreshTargetState();
            UpdateVisual();

            if (!useServerAuthority && applyLocalDamageWhenOffline)
            {
                TickLocalDamage(Time.deltaTime);
            }
        }

        public void ApplyServerZoneState(RealtimeTransportClient.DamageZoneStateMessage message)
        {
            if (message == null)
            {
                return;
            }

            useServerAuthority = true;
            hasAuthoritativeStart = message.startedAtMs > 0;
            if (hasAuthoritativeStart)
            {
                startedAtMs = message.startedAtMs;
            }

            initialRadius = Mathf.Max(0f, message.initialRadius);
            phase1EndRadius = Mathf.Max(0f, message.phase1EndRadius);
            finalRadius = Mathf.Max(0f, message.finalRadius);
            totalShrinkDurationSeconds = ResolveTotalShrinkDuration(message);
            damageMinPerSecond = Mathf.Max(0f, message.damageMinPerSecond);
            damageMaxPerSecond = Mathf.Max(damageMinPerSecond, message.damageMaxPerSecond);
            damageRampSeconds = Mathf.Max(1f, message.damageRampSeconds);

            ConfigurePhaseCenters(
                message.phase1CenterX,
                message.phase1CenterZ,
                message.phase2CenterX,
                message.phase2CenterZ);
        }

        private IEnumerator NetworkRoutine()
        {
            var wait = new WaitForSecondsRealtime(0.25f);
            while (true)
            {
                if (transportClient == null)
                {
                    transportClient = FindFirstObjectByType<RealtimeTransportClient>();
                }

                if (transportClient != null)
                {
                    SubscribeToTransport();

                    if (transportClient.IsReady && !registeredWithServer)
                    {
                        TryRegisterWithServer();
                    }
                    else if (!transportClient.IsReady)
                    {
                        registeredWithServer = false;
                        useServerAuthority = false;
                    }
                }
                else
                {
                    useServerAuthority = false;
                }

                yield return wait;
            }
        }

        private void SubscribeToTransport()
        {
            if (transportClient == null)
            {
                return;
            }

            transportClient.DamageZoneStateReceived -= HandleDamageZoneState;
            transportClient.DamageZoneStateReceived += HandleDamageZoneState;
        }

        private void UnsubscribeFromTransport()
        {
            if (transportClient == null)
            {
                return;
            }

            transportClient.DamageZoneStateReceived -= HandleDamageZoneState;
        }

        private void HandleDamageZoneState(RealtimeTransportClient.DamageZoneStateMessage message)
        {
            ApplyServerZoneState(message);
        }

        private void TryRegisterWithServer()
        {
            if (transportClient == null || registeredWithServer)
            {
                return;
            }

            var mapCenter = GetMapCenterPosition();
            transportClient.SendRegisterDamageZone(
                mapCenter.x,
                mapCenter.z,
                phase1CenterOffset,
                phase2CenterOffset,
                initialRadius,
                phase1EndRadius,
                finalRadius,
                totalShrinkDurationSeconds,
                damageMinPerSecond,
                damageMaxPerSecond,
                damageRampSeconds);
            registeredWithServer = true;
        }

        private void AdvanceElapsedTime()
        {
            if (useServerAuthority && hasAuthoritativeStart)
            {
                var nowMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                elapsedSeconds = Mathf.Max(0f, (float)((nowMs - startedAtMs) * 0.001));
                return;
            }

            if (!hasAuthoritativeStart)
            {
                hasAuthoritativeStart = true;
                startedAtMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }

            var localNowMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            elapsedSeconds = Mathf.Max(0f, (float)((localNowMs - startedAtMs) * 0.001));
        }

        private void RefreshTargetState()
        {
            if (!phasesConfigured)
            {
                return;
            }

            var easingCurve = useServerAuthority ? null : shrinkCurve;
            var state = DamageZoneMath.EvaluateZonePhases(
                elapsedSeconds,
                GetMapCenterPosition(),
                phase1CenterWorld,
                phase2CenterWorld,
                initialRadius,
                phase1EndRadius,
                finalRadius,
                totalShrinkDurationSeconds,
                easingCurve);

            currentPhase = state.Phase;
            targetCenter = state.Center;
            targetRadius = state.Radius;
        }

        private void UpdateVisual()
        {
            if (visualRoot == null)
            {
                return;
            }

            displayedRadius = targetRadius;
            displayedCenter = targetCenter;

            visualRoot.position = new Vector3(
                displayedCenter.x,
                displayedCenter.y + visualHeightOffset,
                displayedCenter.z);

            var diameter = Mathf.Max(0.01f, displayedRadius * 2f);
            var scale = diameter / Mathf.Max(0.01f, visualBaseDiameter);
            visualRoot.localScale = new Vector3(scale, 1f, scale);
        }

        private void TickLocalDamage(float deltaTime)
        {
            if (deltaTime <= 0f || targetRadius <= 0.001f)
            {
                return;
            }

            var localMarker = FindFirstObjectByType<LocalPlayerMarker>();
            if (localMarker == null)
            {
                return;
            }

            var health = localMarker.GetComponent<PlayerHealth>();
            if (health == null || health.IsDead)
            {
                return;
            }

            if (!DamageZoneMath.IsOutsideSafeZone(localMarker.transform.position, targetCenter, targetRadius))
            {
                localDamageAccumulator = 0f;
                return;
            }

            var dps = CurrentDamagePerSecond;
            if (dps <= 0f)
            {
                return;
            }

            localDamageAccumulator += dps * deltaTime;
            if (localDamageAccumulator < 0.25f)
            {
                return;
            }

            var damage = localDamageAccumulator;
            localDamageAccumulator = 0f;
            var direction = targetCenter - localMarker.transform.position;
            direction.y = 0f;
            health.ApplyEnvironmentalDamage(damage, direction);
        }

        private void ConfigureLocalPhases()
        {
            if (phasesConfigured)
            {
                return;
            }

            var mapCenter = GetMapCenterPosition();
            var phase1 = DamageZoneMath.RollPhase1Center(
                new Vector2(mapCenter.x, mapCenter.z),
                phase1CenterOffset);
            var phase2 = DamageZoneMath.RollPhase2Center(
                new Vector2(mapCenter.x, mapCenter.z),
                phase2CenterOffset);
            ConfigurePhaseCenters(phase1.x, phase1.y, phase2.x, phase2.y);
        }

        private void ConfigurePhaseCenters(float phase1X, float phase1Z, float phase2X, float phase2Z)
        {
            phase1CenterWorld = BuildGroundedCenter(phase1X, phase1Z);
            phase2CenterWorld = BuildGroundedCenter(phase2X, phase2Z);
            phasesConfigured = true;
        }

        private float ResolveTotalShrinkDuration(RealtimeTransportClient.DamageZoneStateMessage message)
        {
            if (message.totalShrinkDurationSeconds > 0f)
            {
                return Mathf.Max(1f, message.totalShrinkDurationSeconds);
            }

            var legacyPhase1 = message.phase1DurationSeconds;
            var legacyPhase2 = message.phase2MoveDurationSeconds > 0f
                ? message.phase2MoveDurationSeconds
                : message.phase2DurationSeconds;
            var legacyPhase3 = message.phase3ShrinkDurationSeconds;
            if (legacyPhase1 > 0f || legacyPhase2 > 0f || legacyPhase3 > 0f)
            {
                return Mathf.Max(1f, legacyPhase1 + legacyPhase2 + legacyPhase3);
            }

            return Mathf.Max(1f, totalShrinkDurationSeconds);
        }

        private Vector3 GetMapCenterPosition()
        {
            return zoneCenter != null ? zoneCenter.position : transform.position;
        }

        private Vector3 BuildGroundedCenter(float worldX, float worldZ)
        {
            var mapCenter = GetMapCenterPosition();
            var grounded = new Vector3(worldX, mapCenter.y, worldZ);
            grounded.y = ProbeGroundY(grounded);
            return grounded;
        }

        private float ProbeGroundY(Vector3 worldPosition)
        {
            var origin = worldPosition + Vector3.up * Mathf.Max(0.1f, centerGroundProbeHeight);
            if (Physics.Raycast(
                    origin,
                    Vector3.down,
                    out var hit,
                    Mathf.Max(0.1f, centerGroundProbeDistance),
                    centerGroundMask,
                    QueryTriggerInteraction.Ignore))
            {
                return hit.point.y;
            }

            return worldPosition.y;
        }

        public void SetZoneVisualActive(bool active)
        {
            if (visualRoot == null)
            {
                if (!active)
                {
                    return;
                }

                SpawnVisual();
            }

            if (visualRoot != null)
            {
                visualRoot.gameObject.SetActive(active);
            }
        }

        private void SpawnVisual()
        {
            if (damageZoneVisualPrefab == null || visualRoot != null)
            {
                return;
            }

            var instance = Instantiate(damageZoneVisualPrefab, ZoneCenter, Quaternion.identity, transform);
            instance.name = "DamageZoneVisual";
            visualRoot = instance.transform;
            CacheVisualBaseDiameter(instance);
        }

        private void CacheVisualBaseDiameter(GameObject visualInstance)
        {
            var meshFilter = visualInstance.GetComponentInChildren<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                visualBaseDiameter = 100f;
                return;
            }

            var bounds = meshFilter.sharedMesh.bounds;
            visualBaseDiameter = Mathf.Max(bounds.size.x, bounds.size.z, 0.01f);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            var mapCenter = GetMapCenterPosition();
            var phase1Offset = Mathf.Max(0f, phase1CenterOffset);
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.85f);
            DrawCircleGizmo(mapCenter, phase1Offset);

            if (Application.isPlaying && phasesConfigured)
            {
            Gizmos.color = new Color(0.35f, 1f, 0.45f, 0.75f);
            DrawCircleGizmo(mapCenter, phase2CenterOffset);
                Gizmos.color = new Color(1f, 0.55f, 0.2f, 0.9f);
                Gizmos.DrawSphere(phase2CenterWorld, 1.5f);
            }

            var center = Application.isPlaying && phasesConfigured ? targetCenter : mapCenter;
            var radius = Application.isPlaying ? targetRadius : initialRadius;
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.85f);
            DrawCircleGizmo(center, radius);
            Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.65f);
            DrawCircleGizmo(center, finalRadius);
        }

        private static void DrawCircleGizmo(Vector3 center, float radius)
        {
            if (radius <= 0.01f)
            {
                return;
            }

            const int segments = 48;
            var prev = center + new Vector3(radius, 0f, 0f);
            for (var i = 1; i <= segments; i++)
            {
                var angle = (i / (float)segments) * Mathf.PI * 2f;
                var next = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
#endif
    }
}
