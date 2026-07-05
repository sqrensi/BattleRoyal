using ShooterPrototype.Player;
using UnityEngine;

namespace ShooterPrototype.UI
{
    public readonly struct MatchOutcomeSummary
    {
        public int KillCount { get; }
        public int Placement { get; }
        public int SurvivalSeconds { get; }
        public int CoinReward { get; }
        public int RoundLosses { get; }
        public int DamageDealt { get; }
        public int PlayerRating { get; }
        public int OpponentRating { get; }

        public MatchOutcomeSummary(int killCount, int placement, int survivalSeconds)
        {
            KillCount = Mathf.Max(0, killCount);
            Placement = Mathf.Clamp(placement, 1, 20);
            SurvivalSeconds = Mathf.Max(0, survivalSeconds);
            CoinReward = CalculateCoinReward(KillCount, SurvivalSeconds, Placement);
            RoundLosses = 0;
            DamageDealt = 0;
            PlayerRating = MatchRatingUtility.DefaultRating;
            OpponentRating = MatchRatingUtility.DefaultRating;
        }

        private MatchOutcomeSummary(
            int killCount,
            int placement,
            int survivalSeconds,
            int coinReward,
            int roundLosses,
            int damageDealt,
            int playerRating,
            int opponentRating)
        {
            KillCount = Mathf.Max(0, killCount);
            Placement = Mathf.Clamp(placement, 1, 20);
            SurvivalSeconds = Mathf.Max(0, survivalSeconds);
            CoinReward = Mathf.Max(0, coinReward);
            RoundLosses = Mathf.Max(0, roundLosses);
            DamageDealt = Mathf.Max(0, damageDealt);
            PlayerRating = Mathf.Max(0, playerRating);
            OpponentRating = Mathf.Max(0, opponentRating);
        }

        public static MatchOutcomeSummary CreateTraining(int kills)
        {
            return new MatchOutcomeSummary(Mathf.Max(0, kills), 1, 0, 0, 0, 0, 0, 0);
        }

        public static MatchOutcomeSummary CreateChallenge(float elapsedSeconds)
        {
            var timeMs = Mathf.Max(0, Mathf.RoundToInt(elapsedSeconds * 1000f));
            return new MatchOutcomeSummary(0, 1, timeMs, 0, 0, 0, 0, 0);
        }

        public static MatchOutcomeSummary CreateDuel(
            bool won,
            int roundWins,
            int roundLosses,
            int damageDealt,
            int playerRating,
            int opponentRating)
        {
            roundWins = Mathf.Max(0, roundWins);
            roundLosses = Mathf.Max(0, roundLosses);
            damageDealt = Mathf.Max(0, damageDealt);
            playerRating = Mathf.Max(0, playerRating);
            opponentRating = Mathf.Max(0, opponentRating);

            var rewards = MatchRatingUtility.CalculateDuelRewards(
                new DuelRewardInput(won, roundWins, roundLosses, playerRating, opponentRating, damageDealt));

            return new MatchOutcomeSummary(
                roundWins,
                won ? 1 : 2,
                0,
                rewards.CoinReward,
                roundLosses,
                damageDealt,
                playerRating,
                opponentRating);
        }

        public static MatchOutcomeSummary CreateDeathmatch(bool won, int kills, int placement)
        {
            kills = Mathf.Max(0, kills);
            placement = Mathf.Clamp(placement, 1, 20);
            var coinReward = CalculateCoinReward(kills, 0, placement);
            return new MatchOutcomeSummary(kills, placement, 0, coinReward, 0, 0, 0, 0);
        }

        public static int CalculateCoinReward(int kills, int survivalSeconds, int placement)
        {
            placement = Mathf.Clamp(placement, 1, 20);
            return Mathf.Max(0, (kills * 11) + (survivalSeconds * 12) + ((20 - placement) * 13));
        }

        public int ResolveDuelRatingDelta(bool won)
        {
            return MatchRatingUtility.CalculateDuelDelta(
                new DuelRewardInput(
                    won,
                    KillCount,
                    RoundLosses,
                    PlayerRating > 0 ? PlayerRating : MatchRatingUtility.DefaultRating,
                    OpponentRating > 0 ? OpponentRating : MatchRatingUtility.DefaultRating,
                    DamageDealt));
        }
    }
}
