using ShooterPrototype.UI;

namespace ShooterPrototype.Matchmaking
{
    public static class ActiveMatchContext
    {
        public static MainMenuGameMode SelectedMode { get; private set; } = MainMenuGameMode.Duel1v1;

        public static bool IsOfflineTrainingSession { get; private set; }

        public static bool IsOfflineChallengeSession { get; private set; }

        public static bool IsDuel => SelectedMode == MainMenuGameMode.Duel1v1;

        public static bool IsDeathmatch => SelectedMode == MainMenuGameMode.Deathmatch;

        public static bool IsTraining => SelectedMode == MainMenuGameMode.Training;

        public static bool IsChallenge => SelectedMode == MainMenuGameMode.Challenge;

        public static bool IsSoloPracticeScene => IsTraining || IsChallenge;

        public static bool IsOfflineSoloSession =>
            IsOfflineTrainingSession || IsOfflineChallengeSession || IsOfflineDuelSession ||
            IsOfflineDeathmatchSession;

        public static bool IsOfflineDuelSession { get; private set; }

        public static bool IsOfflineDeathmatchSession { get; private set; }

        public static string SelectedMapScene { get; private set; }

        public static void SetSelectedMapScene(string sceneName)
        {
            SelectedMapScene = string.IsNullOrWhiteSpace(sceneName) ? null : sceneName.Trim();
        }

        public static void ClearSelectedMapScene()
        {
            SelectedMapScene = null;
        }

        public static void PrepareRandomMapForCurrentMode(string duelFallback, string deathmatchFallback)
        {
            if (IsDuel)
            {
                SetSelectedMapScene(MatchMapPool.PickRandomDuelScene(duelFallback));
                return;
            }

            if (IsDeathmatch)
            {
                SetSelectedMapScene(MatchMapPool.PickRandomDeathmatchScene(deathmatchFallback));
                return;
            }

            ClearSelectedMapScene();
        }

        public static void SetOfflineDuelSession(bool active)
        {
            IsOfflineDuelSession = active;
            if (active)
            {
                IsOfflineTrainingSession = false;
                IsOfflineChallengeSession = false;
                IsOfflineDeathmatchSession = false;
            }
        }

        public static void SetOfflineDeathmatchSession(bool active)
        {
            IsOfflineDeathmatchSession = active;
            if (active)
            {
                IsOfflineTrainingSession = false;
                IsOfflineChallengeSession = false;
                IsOfflineDuelSession = false;
            }
        }

        public static void SetOfflineTrainingSession(bool active)
        {
            IsOfflineTrainingSession = active;
            if (active)
            {
                IsOfflineChallengeSession = false;
                IsOfflineDuelSession = false;
                IsOfflineDeathmatchSession = false;
            }
        }

        public static void SetOfflineChallengeSession(bool active)
        {
            IsOfflineChallengeSession = active;
            if (active)
            {
                IsOfflineTrainingSession = false;
                IsOfflineDuelSession = false;
                IsOfflineDeathmatchSession = false;
            }
        }

        public static void SetMode(MainMenuGameMode mode)
        {
            SelectedMode = mode;
        }

        public static void SyncFromScene(
            string sceneName,
            string battleRoyaleScene,
            string duelScene,
            string trainingScene = null,
            string challengeScene = null,
            string deathmatchScene = null)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(deathmatchScene) &&
                MatchMapPool.IsDeathmatchSceneName(sceneName))
            {
                SetMode(MainMenuGameMode.Deathmatch);
                SetSelectedMapScene(sceneName);
                return;
            }

            if (!string.IsNullOrWhiteSpace(challengeScene) &&
                string.Equals(sceneName, challengeScene, System.StringComparison.OrdinalIgnoreCase))
            {
                SetMode(MainMenuGameMode.Challenge);
                return;
            }

            if (!string.IsNullOrWhiteSpace(trainingScene) &&
                string.Equals(sceneName, trainingScene, System.StringComparison.OrdinalIgnoreCase))
            {
                SetMode(MainMenuGameMode.Training);
                return;
            }

            if (!string.IsNullOrWhiteSpace(duelScene) &&
                MatchMapPool.IsDuelSceneName(sceneName))
            {
                SetMode(MainMenuGameMode.Duel1v1);
                SetSelectedMapScene(sceneName);
                return;
            }

            if (!string.IsNullOrWhiteSpace(battleRoyaleScene) &&
                string.Equals(sceneName, battleRoyaleScene, System.StringComparison.OrdinalIgnoreCase))
            {
                SetMode(MainMenuGameMode.BattleRoyale);
            }
        }

        public static string ResolveGameSceneName(
            string battleRoyaleScene,
            string duelScene,
            string trainingScene = null,
            string challengeScene = null,
            string deathmatchScene = null)
        {
            if (IsChallenge && !string.IsNullOrWhiteSpace(challengeScene))
            {
                return challengeScene;
            }

            if (IsTraining && !string.IsNullOrWhiteSpace(trainingScene))
            {
                return trainingScene;
            }

            if (IsDeathmatch)
            {
                if (!string.IsNullOrWhiteSpace(SelectedMapScene))
                {
                    return SelectedMapScene;
                }

                return !string.IsNullOrWhiteSpace(deathmatchScene)
                    ? deathmatchScene
                    : MatchMapPool.PickRandomDeathmatchScene("dm");
            }

            if (IsDuel)
            {
                if (!string.IsNullOrWhiteSpace(SelectedMapScene))
                {
                    return SelectedMapScene;
                }

                return !string.IsNullOrWhiteSpace(duelScene)
                    ? duelScene
                    : MatchMapPool.PickRandomDuelScene("1x1");
            }

            return battleRoyaleScene;
        }
    }
}
