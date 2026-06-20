using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShooterPrototype.Player
{
    public static class DuelSpawnUtility
    {
        private const string TeamOneRootName = "SpawnPoints1";
        private const string TeamTwoRootName = "SpawnPoints2";

        private static Transform teamOneRoot;
        private static Transform teamTwoRoot;

        public static void ClearCachedRoots()
        {
            teamOneRoot = null;
            teamTwoRoot = null;
        }

        public static bool TryResolveSpawnPose(int teamIndex, int slotIndex, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            var root = ResolveTeamRoot(teamIndex);
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

        private static Vector3 ResolveGroundedFeetPosition(Vector3 requestedPosition)
        {
            var origin = requestedPosition + Vector3.up * 2f;
            if (Physics.Raycast(origin, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore))
            {
                return hit.point;
            }

            return requestedPosition;
        }

        private static Transform ResolveTeamRoot(int teamIndex)
        {
            if (teamIndex <= 0)
            {
                if (teamOneRoot == null)
                {
                    teamOneRoot = GameObject.Find(TeamOneRootName)?.transform;
                }

                return teamOneRoot;
            }

            if (teamTwoRoot == null)
            {
                teamTwoRoot = GameObject.Find(TeamTwoRootName)?.transform;
            }

            return teamTwoRoot;
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

        public static bool IsDuelScene(Scene scene)
        {
            return scene.IsValid() &&
                   string.Equals(scene.name, "1x1", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
