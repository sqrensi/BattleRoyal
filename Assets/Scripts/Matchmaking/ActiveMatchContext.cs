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

        public static string ResolveGameSceneName(string battleRoyaleScene, string duelScene)
        {
            return IsDuel ? duelScene : battleRoyaleScene;
        }
    }
}
