#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ShooterPrototype.EditorTools
{
    /// <summary>
    /// One-shot prefab rebuild when Library/PrefabRebuildRequested exists (used from CLI/agent).
    /// </summary>
    [InitializeOnLoad]
    internal static class PendingPrefabRebuild
    {
        private const string RequestPath = "Library/PrefabRebuildRequested";

        static PendingPrefabRebuild()
        {
            EditorApplication.delayCall += TryRunPendingRebuild;
        }

        private static void TryRunPendingRebuild()
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var requestFile = Path.Combine(projectRoot, RequestPath);
            if (!File.Exists(requestFile))
            {
                return;
            }

            try
            {
                File.Delete(requestFile);
            }
            catch (IOException exception)
            {
                Debug.LogWarning($"[PendingPrefabRebuild] Could not delete request file: {exception.Message}");
            }

            Debug.Log("[PendingPrefabRebuild] Running FP + remote prefab rebuild...");
            PlayerCleanPrefabCreator.RebuildFpAndRemotePrefabs();
            Debug.Log("[PendingPrefabRebuild] Prefab rebuild finished.");
        }
    }
}
#endif
