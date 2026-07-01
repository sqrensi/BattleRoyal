using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShooterPrototype.Matchmaking
{
    public static class MatchMapPool
    {
        private static readonly string[] DefaultDuelScenes = { "1x1", "1x1 1" };
        private static readonly string[] DefaultDeathmatchScenes = { "dm", "dm 1" };

        public static IReadOnlyList<string> DuelScenes => DefaultDuelScenes;

        public static IReadOnlyList<string> DeathmatchScenes => DefaultDeathmatchScenes;

        public static bool IsDuelSceneName(string sceneName)
        {
            return MatchesSceneName(sceneName, DefaultDuelScenes);
        }

        public static bool IsDeathmatchSceneName(string sceneName)
        {
            return MatchesSceneName(sceneName, DefaultDeathmatchScenes);
        }

        public static bool IsDuelScene(Scene scene)
        {
            return scene.IsValid() && IsDuelSceneName(scene.name);
        }

        public static bool IsDeathmatchScene(Scene scene)
        {
            return scene.IsValid() && IsDeathmatchSceneName(scene.name);
        }

        public static string PickRandomDuelScene(string fallback = "1x1")
        {
            return PickRandomLoadableScene(DefaultDuelScenes, fallback);
        }

        public static string PickRandomDeathmatchScene(string fallback = "dm")
        {
            return PickRandomLoadableScene(DefaultDeathmatchScenes, fallback);
        }

        private static string PickRandomLoadableScene(string[] pool, string fallback)
        {
            var candidates = new List<string>(pool.Length);
            for (var i = 0; i < pool.Length; i++)
            {
                var sceneName = pool[i];
                if (!string.IsNullOrWhiteSpace(sceneName) && Application.CanStreamedLevelBeLoaded(sceneName))
                {
                    candidates.Add(sceneName);
                }
            }

            if (candidates.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(fallback) && Application.CanStreamedLevelBeLoaded(fallback))
                {
                    return fallback;
                }

                return pool.Length > 0 ? pool[0] : fallback;
            }

            return candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        private static bool MatchesSceneName(string sceneName, string[] pool)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return false;
            }

            for (var i = 0; i < pool.Length; i++)
            {
                if (string.Equals(sceneName, pool[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
