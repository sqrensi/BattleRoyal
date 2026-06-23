using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Floor area inside buildings where pickups spawn at random XZ inside the box.
    /// Place the box collider bottom on the walkable floor of that level.
    /// Spawn height raycasts down from above the zone and uses the first walkable
    /// surface inside the zone vertical band (top of floor, not colliders below it).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class PickupSpawnZone : MonoBehaviour
    {
        [Tooltip("Optional id inside a prefab (e.g. floor1, floor2). Can repeat across prefab copies and match other zone object names.")]
        [SerializeField] private string zoneKey = string.Empty;

        [Tooltip("Max weapon pickup points in this zone. Actual count per respawn is rolled 0..max.")]
        [FormerlySerializedAs("pickupCount")]
        [SerializeField] private int maxWeaponSlots = 2;
        [SerializeField] private int maxStandaloneAmmoSlots = 1;
        [SerializeField] private float edgePadding = 0.35f;
        [SerializeField] private int maxPlacementAttempts = 24;
        [SerializeField] private float minSeparation = 1.2f;
        [SerializeField] private float groundProbeHeight = 4f;
        [SerializeField] private float groundProbeDistance = 10f;
        [SerializeField] private LayerMask groundMask;

        private static readonly RaycastHit[] RaycastHitBuffer = new RaycastHit[32];

        private BoxCollider zoneCollider;
        private readonly List<Transform> anchors = new List<Transform>();

        public int MaxWeaponSlots => Mathf.Clamp(maxWeaponSlots, 0, 8);

        public int MaxStandaloneAmmoSlots => Mathf.Clamp(maxStandaloneAmmoSlots, 0, 8);

        public void ConfigureTrainingSlots(int weaponSlots, int standaloneAmmoSlots)
        {
            maxWeaponSlots = Mathf.Clamp(weaponSlots, 0, 8);
            maxStandaloneAmmoSlots = Mathf.Clamp(standaloneAmmoSlots, 0, 8);
        }

        [System.Obsolete("Use MaxWeaponSlots.")]
        public int PickupCount => MaxWeaponSlots;

        public string BuildSpawnId(int slotIndex)
        {
            return $"pz_{ResolveStableInstanceKey()}_{ResolveLocalZoneKey()}_{slotIndex:00}";
        }

        public string BuildStandaloneAmmoSpawnId(int slotIndex)
        {
            return $"pz_{ResolveStableInstanceKey()}_{ResolveLocalZoneKey()}_sa_{slotIndex:00}";
        }

        public static bool IsStandaloneAmmoSpawnId(string spawnId)
        {
            return !string.IsNullOrWhiteSpace(spawnId) &&
                   spawnId.IndexOf("_sa_", System.StringComparison.Ordinal) >= 0;
        }

        public static bool TryParseSpawnSlotIndex(string spawnId, out int slotIndex)
        {
            slotIndex = -1;
            if (string.IsNullOrWhiteSpace(spawnId))
            {
                return false;
            }

            var separatorIndex = spawnId.LastIndexOf('_');
            return separatorIndex >= 0 &&
                   int.TryParse(spawnId.Substring(separatorIndex + 1), out slotIndex);
        }

        public Transform GetOrCreateAnchor(int slotIndex)
        {
            EnsureCollider();
            while (anchors.Count <= slotIndex)
            {
                var anchorObject = new GameObject($"PickupAnchor_{anchors.Count}");
                var anchor = anchorObject.transform;
                anchor.SetParent(transform, false);
                anchors.Add(anchor);
            }

            return anchors[slotIndex];
        }

        public bool TryResolveSpawnPose(
            int slotIndex,
            float yOffset,
            Vector3 eulerOffset,
            IReadOnlyList<Vector3> avoidPositions,
            out Vector3 position,
            out Quaternion rotation)
        {
            return TryResolveSpawnPose(null, slotIndex, yOffset, eulerOffset, avoidPositions, out position, out rotation);
        }

        public bool TryResolveSpawnPose(
            string deterministicSpawnId,
            int slotIndex,
            float yOffset,
            Vector3 eulerOffset,
            IReadOnlyList<Vector3> avoidPositions,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.Euler(eulerOffset);
            EnsureCollider();

            var bounds = GetSpawnBounds();
            if (bounds.size.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            for (var attempt = 0; attempt < maxPlacementAttempts; attempt++)
            {
                var candidate = SamplePointInBounds(bounds, deterministicSpawnId, attempt);
                candidate.y = ResolveSpawnSurfaceY(candidate, bounds, yOffset);

                if (!HasSeparation(candidate, avoidPositions))
                {
                    continue;
                }

                position = candidate;
                rotation = Quaternion.Euler(eulerOffset);
                return true;
            }

            position = new Vector3(
                bounds.center.x,
                ResolveSpawnSurfaceY(bounds.center, bounds, yOffset),
                bounds.center.z);
            rotation = Quaternion.Euler(eulerOffset);
            return true;
        }

        /// <summary>
        /// Fixed placement for training zones: child transforms first, otherwise a deterministic grid on the zone floor.
        /// </summary>
        public bool TryResolveMarkedSpawnPose(
            int slotIndex,
            int totalWeaponSlots,
            float yOffset,
            Vector3 eulerOffset,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.Euler(eulerOffset);
            EnsureCollider();

            var bounds = GetSpawnBounds();
            if (bounds.size.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            var childSpawnPoint = ResolveWeaponSpawnChild(slotIndex);
            if (childSpawnPoint != null)
            {
                position = ResolveGroundedWorldPosition(childSpawnPoint.position, yOffset);
                rotation = childSpawnPoint.rotation * Quaternion.Euler(eulerOffset);
                return true;
            }

            var xzPoint = SampleDeterministicPointInBounds(bounds, slotIndex, totalWeaponSlots);
            position = new Vector3(
                xzPoint.x,
                ResolveSpawnSurfaceY(xzPoint, bounds, yOffset),
                xzPoint.z);
            rotation = Quaternion.Euler(eulerOffset);
            return true;
        }

        private Transform ResolveWeaponSpawnChild(int slotIndex)
        {
            if (slotIndex < 0 || transform.childCount == 0)
            {
                return null;
            }

            var childIndex = 0;
            for (var i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child == null || !child.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (childIndex == slotIndex)
                {
                    return child;
                }

                childIndex++;
            }

            return null;
        }

        private Vector3 SampleDeterministicPointInBounds(Bounds bounds, int slotIndex, int totalSlots)
        {
            var min = bounds.min;
            var max = bounds.max;
            var safePadding = Mathf.Min(
                edgePadding,
                Mathf.Max(0f, (max.x - min.x) * 0.5f - 0.01f),
                Mathf.Max(0f, (max.z - min.z) * 0.5f - 0.01f));

            var clampedSlots = Mathf.Max(1, totalSlots);
            var clampedIndex = Mathf.Clamp(slotIndex, 0, clampedSlots - 1);

            if (clampedSlots == 1)
            {
                return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            }

            var columns = clampedSlots <= 2 ? clampedSlots : 2;
            var rows = (clampedSlots + columns - 1) / columns;
            var column = clampedIndex % columns;
            var row = clampedIndex / columns;
            var columnT = columns <= 1 ? 0.5f : column / (float)(columns - 1);
            var rowT = rows <= 1 ? 0.5f : row / (float)(rows - 1);
            var x = Mathf.Lerp(min.x + safePadding, max.x - safePadding, columnT);
            var z = Mathf.Lerp(min.z + safePadding, max.z - safePadding, rowT);
            return new Vector3(x, bounds.max.y, z);
        }

        public Vector3 ResolveGroundedWorldPosition(Vector3 approximateWorldPosition, float yOffset)
        {
            EnsureCollider();
            var bounds = GetSpawnBounds();
            return new Vector3(
                approximateWorldPosition.x,
                ResolveSpawnSurfaceY(approximateWorldPosition, bounds, yOffset),
                approximateWorldPosition.z);
        }

        private float ResolveSpawnSurfaceY(Vector3 xzPoint, Bounds bounds, float yOffset)
        {
            var probeOrigin = new Vector3(xzPoint.x, bounds.max.y + groundProbeHeight, xzPoint.z);
            var verticalSpan = Mathf.Max(0f, bounds.max.y - bounds.min.y);
            var probeDistance = groundProbeHeight + groundProbeDistance + verticalSpan;
            var hitCount = Physics.RaycastNonAlloc(
                probeOrigin,
                Vector3.down,
                RaycastHitBuffer,
                probeDistance,
                PickupGroundLayers.SanitizeMask(groundMask),
                QueryTriggerInteraction.Ignore);
            if (hitCount <= 0)
            {
                return bounds.min.y + yOffset;
            }

            var maxDriftSqr = 2.25f;
            var maxAboveZone = 0.25f;
            var maxBelowZone = 0.1f;
            var bandTop = bounds.max.y + maxAboveZone;
            var bandBottom = bounds.min.y - maxBelowZone;

            var closestDistance = float.MaxValue;
            var closestY = float.MinValue;
            var bestY = float.MinValue;
            for (var i = 0; i < hitCount; i++)
            {
                var hit = RaycastHitBuffer[i];
                if (!IsWalkableSpawnSurfaceHit(hit, xzPoint, bandBottom, bandTop, maxDriftSqr))
                {
                    continue;
                }

                if (hit.distance < closestDistance)
                {
                    closestDistance = hit.distance;
                    closestY = hit.point.y;
                }

                if (hit.point.y > bestY)
                {
                    bestY = hit.point.y;
                }
            }

            if (closestY > float.MinValue)
            {
                return closestY + yOffset;
            }

            if (bestY > float.MinValue)
            {
                return bestY + yOffset;
            }

            return bounds.min.y + yOffset;
        }

        private static bool IsWalkableSpawnSurfaceHit(
            RaycastHit hit,
            Vector3 xzPoint,
            float bandBottom,
            float bandTop,
            float maxDriftSqr)
        {
            if (hit.normal.y < 0.5f)
            {
                return false;
            }

            var driftSqr = (new Vector2(hit.point.x, hit.point.z) - new Vector2(xzPoint.x, xzPoint.z)).sqrMagnitude;
            if (driftSqr > maxDriftSqr)
            {
                return false;
            }

            return hit.point.y >= bandBottom && hit.point.y <= bandTop;
        }

        private string ResolveLocalZoneKey()
        {
            if (!string.IsNullOrWhiteSpace(zoneKey))
            {
                return zoneKey.Trim();
            }

            var localPosition = transform.localPosition;
            return $"{SanitizeKeyPart(gameObject.name)}@" +
                   $"{Mathf.RoundToInt(localPosition.x * 8f)}@" +
                   $"{Mathf.RoundToInt(localPosition.y * 8f)}@" +
                   $"{Mathf.RoundToInt(localPosition.z * 8f)}";
        }

        private string ResolveStableInstanceKey()
        {
            var instanceRoot = transform;
            while (instanceRoot.parent != null && instanceRoot.parent.parent != null)
            {
                instanceRoot = instanceRoot.parent;
            }

            var position = instanceRoot.position;
            return $"{SanitizeKeyPart(instanceRoot.name)}@" +
                   $"{Mathf.RoundToInt(position.x * 4f)}@" +
                   $"{Mathf.RoundToInt(position.y * 4f)}@" +
                   $"{Mathf.RoundToInt(position.z * 4f)}";
        }

        private static string SanitizeKeyPart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "root";
            }

            return value.Trim().Replace(' ', '_');
        }

        private void EnsureCollider()
        {
            if (zoneCollider == null)
            {
                zoneCollider = GetComponent<BoxCollider>();
            }

            if (zoneCollider != null)
            {
                zoneCollider.isTrigger = true;
            }

            var ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
            if (ignoreRaycastLayer >= 0)
            {
                gameObject.layer = ignoreRaycastLayer;
            }
        }

        private Bounds GetSpawnBounds()
        {
            EnsureCollider();
            return zoneCollider != null ? zoneCollider.bounds : new Bounds(transform.position, Vector3.one);
        }

        private Vector3 SamplePointInBounds(Bounds bounds, string deterministicSpawnId, int attemptIndex)
        {
            var min = bounds.min;
            var max = bounds.max;
            var safePadding = Mathf.Min(
                edgePadding,
                Mathf.Max(0f, (max.x - min.x) * 0.5f - 0.01f),
                Mathf.Max(0f, (max.z - min.z) * 0.5f - 0.01f));

            var previousState = UnityEngine.Random.state;
            if (!string.IsNullOrWhiteSpace(deterministicSpawnId))
            {
                UnityEngine.Random.InitState(ComputeStableSeed($"{deterministicSpawnId}_{attemptIndex}"));
            }

            try
            {
                var x = UnityEngine.Random.Range(min.x + safePadding, max.x - safePadding);
                var z = UnityEngine.Random.Range(min.z + safePadding, max.z - safePadding);
                return new Vector3(x, bounds.max.y, z);
            }
            finally
            {
                UnityEngine.Random.state = previousState;
            }
        }

        private static int ComputeStableSeed(string value)
        {
            unchecked
            {
                var hash = 17;
                for (var i = 0; i < value.Length; i++)
                {
                    hash = (hash * 31) + value[i];
                }

                return hash;
            }
        }

        private bool HasSeparation(Vector3 spawnPoint, IReadOnlyList<Vector3> avoidPositions)
        {
            if (avoidPositions == null || avoidPositions.Count == 0 || minSeparation <= 0.01f)
            {
                return true;
            }

            var minSeparationSqr = minSeparation * minSeparation;
            for (var i = 0; i < avoidPositions.Count; i++)
            {
                var other = avoidPositions[i];
                if ((new Vector2(other.x, other.z) - new Vector2(spawnPoint.x, spawnPoint.z)).sqrMagnitude < minSeparationSqr)
                {
                    return false;
                }
            }

            return true;
        }

        private void OnValidate()
        {
            groundMask = PickupGroundLayers.SanitizeMask(groundMask);
        }

        private void OnDrawGizmosSelected()
        {
            EnsureCollider();
            if (zoneCollider == null)
            {
                return;
            }

            var bounds = zoneCollider.bounds;
            Gizmos.color = new Color(0.2f, 0.85f, 0.35f, 0.35f);
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.DrawCube(bounds.center, bounds.size);
            Gizmos.color = new Color(0.2f, 0.85f, 0.35f, 0.9f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);

            Gizmos.color = Color.yellow;
            var surfaceY = ResolveSpawnSurfaceY(bounds.center, bounds, 0f);
            Gizmos.DrawSphere(new Vector3(bounds.center.x, surfaceY, bounds.center.z), 0.12f);
        }
    }
}
