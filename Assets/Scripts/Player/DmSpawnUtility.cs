using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShooterPrototype.Player
{
    public static class DmSpawnUtility
    {
        private const string SpawnRootName = "SpawnPoints";
        private const float SpawnProbeHeight = 8f;
        private const float SpawnProbeDistance = 40f;
        private const float SpawnSurfaceOffset = 0.05f;

        private static Transform spawnRoot;

        public static void ClearCachedRoots()
        {
            spawnRoot = null;
        }

        public static int GetSpawnPointCount()
        {
            var root = ResolveSpawnRoot();
            return root == null ? 0 : CollectChildPoints(root).Count;
        }

        public static int RollRandomSpawnSlot(int avoidSlot = -1)
        {
            var count = GetSpawnPointCount();
            if (count <= 0)
            {
                return 0;
            }

            if (count == 1)
            {
                return 0;
            }

            var slot = Random.Range(0, count);
            if (avoidSlot >= 0 && avoidSlot < count && slot == avoidSlot)
            {
                slot = (slot + 1 + Random.Range(0, count - 1)) % count;
            }

            return slot;
        }

        public static bool TryResolveRandomSpawnPose(out Vector3 position, out Quaternion rotation, int avoidSlot = -1)
        {
            return TryResolveSpawnPose(RollRandomSpawnSlot(avoidSlot), out position, out rotation);
        }

        public static bool TryResolveSpawnPose(int slotIndex, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            var root = ResolveSpawnRoot();
            if (root == null)
            {
                return false;
            }

            var points = CollectChildPoints(root);
            if (points.Count == 0)
            {
                return false;
            }

            var index = slotIndex < 0 ? 0 : Mathf.Clamp(slotIndex, 0, points.Count - 1);
            var point = points[index];
            position = ResolveGroundedFeetPosition(point.position);
            rotation = Quaternion.Euler(0f, point.rotation.eulerAngles.y, 0f);
            return true;
        }

        public static Vector3 GroundFeetPosition(Vector3 requestedPosition)
        {
            return ResolveGroundedFeetPosition(requestedPosition);
        }

        public static bool TryApplyGroundedPose(
            Transform playerRoot,
            CharacterController characterController,
            Vector3 groundedPosition,
            Quaternion rotation)
        {
            if (playerRoot == null)
            {
                return false;
            }

            var wasEnabled = characterController != null && characterController.enabled;
            if (characterController != null)
            {
                characterController.enabled = false;
            }

            playerRoot.SetPositionAndRotation(groundedPosition, rotation);
            Physics.SyncTransforms();

            if (characterController != null)
            {
                characterController.enabled = wasEnabled;
            }

            return true;
        }

        private static Vector3 ResolveGroundedFeetPosition(Vector3 requestedPosition)
        {
            if (TryRaycastGround(requestedPosition, SpawnProbeHeight, SpawnProbeDistance, out var hit) ||
                TryRaycastGround(requestedPosition, 16f, 64f, out hit) ||
                TryRaycastGround(requestedPosition, 2f, 12f, out hit))
            {
                return hit.point + Vector3.up * SpawnSurfaceOffset;
            }

            return requestedPosition;
        }

        private static bool TryRaycastGround(
            Vector3 requestedPosition,
            float probeHeight,
            float probeDistance,
            out RaycastHit hit)
        {
            var origin = requestedPosition + Vector3.up * probeHeight;
            return Physics.Raycast(
                origin,
                Vector3.down,
                out hit,
                probeHeight + probeDistance,
                PickupGroundLayers.EnvironmentMask,
                QueryTriggerInteraction.Ignore);
        }

        private static Transform ResolveSpawnRoot()
        {
            if (spawnRoot == null)
            {
                spawnRoot = GameObject.Find(SpawnRootName)?.transform;
            }

            return spawnRoot;
        }

        private static List<Transform> CollectChildPoints(Transform root)
        {
            var points = new List<Transform>();
            if (root == null)
            {
                return points;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child != null)
                {
                    points.Add(child);
                }
            }

            points.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
            return points;
        }

        public static bool IsDeathmatchScene(Scene scene)
        {
            return ShooterPrototype.Matchmaking.MatchMapPool.IsDeathmatchScene(scene);
        }
    }
}
