using ShooterPrototype.UI;

namespace ShooterPrototype.Matchmaking
{
    public static class ActiveMatchContext
    {
        public static MainMenuGameMode SelectedMode { get; private set; } = MainMenuGameMode.Duel1v1;

        public static bool IsOfflineTrainingSession { get; private set; }

        public static bool IsOfflineChallengeSession { get; private set; }

        public static bool IsDuel => SelectedMode == MainMenuGameMode.Duel1v1;

        public static bool IsTraining => SelectedMode == MainMenuGameMode.Training;

        public static bool IsChallenge => SelectedMode == MainMenuGameMode.Challenge;

        public static bool IsSoloPracticeScene => IsTraining || IsChallenge;

        public static bool IsOfflineSoloSession =>
            IsOfflineTrainingSession || IsOfflineChallengeSession || IsOfflineDuelSession;

        public static bool IsOfflineDuelSession { get; private set; }

        public static void SetOfflineDuelSession(bool active)
        {
            IsOfflineDuelSession = active;
            if (active)
            {
                IsOfflineTrainingSession = false;
                IsOfflineChallengeSession = false;
            }
        }

        public static void SetOfflineTrainingSession(bool active)
        {
            IsOfflineTrainingSession = active;
            if (active)
            {
                IsOfflineChallengeSession = false;
                IsOfflineDuelSession = false;
            }
        }

        public static void SetOfflineChallengeSession(bool active)
        {
            IsOfflineChallengeSession = active;
            if (active)
            {
                IsOfflineTrainingSession = false;
                IsOfflineDuelSession = false;
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
            string challengeScene = null)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
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
                string.Equals(sceneName, duelScene, System.StringComparison.OrdinalIgnoreCase))
            {
                SetMode(MainMenuGameMode.Duel1v1);
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
            string challengeScene = null)
        {
            if (IsChallenge && !string.IsNullOrWhiteSpace(challengeScene))
            {
                return challengeScene;
            }

            if (IsTraining && !string.IsNullOrWhiteSpace(trainingScene))
            {
                return trainingScene;
            }

            return IsDuel ? duelScene : battleRoyaleScene;
        }
    }
}
