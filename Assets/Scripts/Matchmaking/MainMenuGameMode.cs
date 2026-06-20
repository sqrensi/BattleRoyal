namespace ShooterPrototype.Matchmaking
{
    public enum MainMenuGameMode
    {
        BattleRoyale = 0,
        Training = 1,
        Duel1v1 = 2,
    }

    public static class MainMenuGameModeUtility
    {
        public static string ToApiValue(MainMenuGameMode mode)
        {
            return mode switch
            {
                MainMenuGameMode.Training => "training",
                MainMenuGameMode.Duel1v1 => "duel",
                _ => "battle_royale",
            };
        }

        public static string GetDisplayName(MainMenuGameMode mode)
        {
            return mode switch
            {
                MainMenuGameMode.Training => "Тренировка",
                MainMenuGameMode.Duel1v1 => "1 на 1",
                _ => "Королевская битва",
            };
        }
    }
}
