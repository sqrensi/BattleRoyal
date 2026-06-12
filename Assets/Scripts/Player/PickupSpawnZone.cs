using System.Collections.Generic;
using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Floor area inside buildings where pickups spawn at random XZ inside the box.
    /// Spawn height uses the vertical center of the BoxCollider plus PickupSpawnManager offset.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class PickupSpawnZone : MonoBehaviour
    {
        [Tooltip("Optional id inside a prefab (e.g. floor1, floor2). Can repeat across prefab copies and match other zone object names.")]
        [SerializeField] private string zoneKey = string.Empty;

        [SerializeField] private int pickupCount = 2;
        [SerializeField] private float edgePadding = 0.35f;
        [SerializeField] private int maxPlacementAttempts = 24;
        [SerializeField] private float minSeparation = 1.2f;

        private BoxCollider zoneCollider;
        private readonly List<Transform> anchors = new List<Transform>();

        public int PickupCount => Mathf.Max(1, pickupCount);

        public string BuildSpawnId(int slotIndex)
        {
            return $"pz_{ResolvePrefabInstanceId()}_{ResolveLocalZoneKey()}_{slotIndex:00}";
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
            position = Vector3.zero;
            rotation = Quaternion.Euler(eulerOffset);
            EnsureCollider();

            var bounds = GetSpawnBounds();
            if (bounds.size.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            var spawnHeight = bounds.center.y + yOffset;

            for (var attempt = 0; attempt < maxPlacementAttempts; attempt++)
            {
                var candidate = SampleRandomPointInBounds(bounds);
                candidate.y = spawnHeight;

                if (!HasSeparation(candidate, avoidPositions))
                {
                    continue;
                }

                position = candidate;
                rotation = Quaternion.Euler(eulerOffset);
                return true;
            }

            position = new Vector3(bounds.center.x, spawnHeight, bounds.center.z);
            rotation = Quaternion.Euler(eulerOffset);
            return true;
        }

        private string ResolveLocalZoneKey()
        {
            if (!string.IsNullOrWhiteSpace(zoneKey))
            {
                return zoneKey.Trim();
            }

            return $"s{transform.GetSiblingIndex()}";
        }

        private int ResolvePrefabInstanceId()
        {
            var instanceRoot = transform;
            while (instanceRoot.parent != null && instanceRoot.parent.parent != null)
            {
                instanceRoot = instanceRoot.parent;
            }

            return instanceRoot.GetInstanceID();
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
        }

        private Bounds GetSpawnBounds()
        {
            EnsureCollider();
            return zoneCollider != null ? zoneCollider.bounds : new Bounds(transform.position, Vector3.one);
        }

        private Vector3 SampleRandomPointInBounds(Bounds bounds)
        {
            var min = bounds.min;
            var max = bounds.max;
            var safePadding = Mathf.Min(
                edgePadding,
                Mathf.Max(0f, (max.x - min.x) * 0.5f - 0.01f),
                Mathf.Max(0f, (max.z - min.z) * 0.5f - 0.01f));
            var x = Random.Range(min.x + safePadding, max.x - safePadding);
            var z = Random.Range(min.z + safePadding, max.z - safePadding);
            return new Vector3(x, bounds.center.y, z);
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
            Gizmos.DrawSphere(new Vector3(bounds.center.x, bounds.center.y, bounds.center.z), 0.12f);
        }
    }
}
