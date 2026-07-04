namespace ShooterPrototype.Matchmaking
{
    public enum MainMenuGameMode
    {
        BattleRoyale = 0,
        Training = 1,
        Duel1v1 = 2,
        Challenge = 3,
        Deathmatch = 4,
    }

    public static class MainMenuGameModeUtility
    {
        public static string ToApiValue(MainMenuGameMode mode)
        {
            return mode switch
            {
                MainMenuGameMode.Training => "training",
                MainMenuGameMode.Duel1v1 => "duel",
                MainMenuGameMode.Challenge => "challenge",
                MainMenuGameMode.Deathmatch => "deathmatch",
                _ => "battle_royale",
            };
        }

        public static string GetDisplayName(MainMenuGameMode mode)
        {
            return mode switch
            {
                MainMenuGameMode.Training => "Тренировка",
                MainMenuGameMode.Duel1v1 => "1 на 1",
                MainMenuGameMode.Challenge => "Челлендж",
                MainMenuGameMode.Deathmatch => "Бой насмерть",
                _ => "Королевская битва",
            };
        }

        public static string GetModeIntroDescription(MainMenuGameMode mode)
        {
            return mode switch
            {
                MainMenuGameMode.Training => "Тренировка — отточите мастерство владения оружием.",
                MainMenuGameMode.Challenge => "Челлендж — попадите по всем мишеням на карте за минимальное время.",
                MainMenuGameMode.Duel1v1 => "Дуэль 1 на 1 — побеждает тот, кто первым выиграет 5 раундов.",
                MainMenuGameMode.Deathmatch => "Бой насмерть — наберите больше всего убийств за 5 минут.",
                _ => "Королевская битва — останься последним выжившим.",
            };
        }

        public static string GetLeaderboardModeKey(MainMenuGameMode mode)
        {
            return ToApiValue(mode);
        }

        public static bool IsOfflineSoloMode(MainMenuGameMode mode)
        {
            return mode is MainMenuGameMode.Training or MainMenuGameMode.Challenge;
        }

        public static bool IsOfflinePlayMode(MainMenuGameMode mode)
        {
            return mode is MainMenuGameMode.Training
                or MainMenuGameMode.Challenge
                or MainMenuGameMode.Duel1v1
                or MainMenuGameMode.Deathmatch;
        }

        public static MainMenuGameMode FromApiValue(string apiValue)
        {
            var normalized = string.IsNullOrWhiteSpace(apiValue)
                ? string.Empty
                : apiValue.Trim().ToLowerInvariant();

            return normalized switch
            {
                "training" => MainMenuGameMode.Training,
                "duel" or "1v1" => MainMenuGameMode.Duel1v1,
                "challenge" => MainMenuGameMode.Challenge,
                "deathmatch" or "dm" => MainMenuGameMode.Deathmatch,
                _ => MainMenuGameMode.BattleRoyale,
            };
        }
    }
}
