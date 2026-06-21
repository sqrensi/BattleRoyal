using ShooterPrototype.UI;

namespace ShooterPrototype.Matchmaking
{
    public static class ActiveMatchContext
    {
        public static MainMenuGameMode SelectedMode { get; private set; } = MainMenuGameMode.BattleRoyale;

        public static bool IsDuel => SelectedMode == MainMenuGameMode.Duel1v1;

        public static void SetMode(MainMenuGameMode mode)
        {
            SelectedMode = mode;
        }

        /// <summary>
        /// Keeps gameplay rules aligned with the loaded match scene (duel vs BR).
        /// Menu selection can be stale after scene transitions or hot reloads.
        /// </summary>
        public static void SyncFromScene(string sceneName, string battleRoyaleScene, string duelScene)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
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

        public static string ResolveGameSceneName(string battleRoyaleScene, string duelScene)
        {
            return IsDuel ? duelScene : battleRoyaleScene;
        }
    }
}
