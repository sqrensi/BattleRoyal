using System.Collections;
using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Clears persistent gameplay pools and caches that survive scene loads via DontDestroyOnLoad.
    /// </summary>
    public static class GameplaySessionCleanup
    {
        public static void RunAfterMatch()
        {
            GameplayVfxPool.ResetSession();
            ShotTracerSpawner.ResetSession();
            GameplayRuntimeCache.ClearLocalPlayer();
            DuelBotLineOfSight.ResetCachedSceneState();
            DmSpawnUtility.ClearCachedRoots();
            WeaponBlockUtility.ResetProcessedScenes();
            MatchScoreboardTracker.Reset();
        }

        public static IEnumerator RunAfterMatchAndReleaseMemoryRoutine()
        {
            RunAfterMatch();
            yield return null;

            RemoteResourceClothingApplier.ClearSessionStaticCaches();
            yield return null;

#if UNITY_WEBGL && !UNITY_EDITOR
            var unload = Resources.UnloadUnusedAssets();
            while (!unload.isDone)
            {
                yield return null;
            }

            System.GC.Collect();
#endif
        }
    }
}
