using UnityEngine;

namespace ShooterPrototype.UI
{
    public readonly struct MatchOutcomeSummary
    {
        public int KillCount { get; }
        public int Placement { get; }
        public int SurvivalSeconds { get; }
        public int CoinReward { get; }

        public MatchOutcomeSummary(int killCount, int placement, int survivalSeconds)
        {
            KillCount = Mathf.Max(0, killCount);
            Placement = Mathf.Clamp(placement, 1, 20);
            SurvivalSeconds = Mathf.Max(0, survivalSeconds);
            CoinReward = CalculateCoinReward(KillCount, SurvivalSeconds, Placement);
        }

        private MatchOutcomeSummary(int killCount, int placement, int survivalSeconds, int coinReward)
        {
            KillCount = Mathf.Max(0, killCount);
            Placement = Mathf.Clamp(placement, 1, 20);
            SurvivalSeconds = Mathf.Max(0, survivalSeconds);
            CoinReward = Mathf.Max(0, coinReward);
        }

        public static MatchOutcomeSummary CreateTraining(int kills)
        {
            return new MatchOutcomeSummary(Mathf.Max(0, kills), 1, 0, 0);
        }

        public static MatchOutcomeSummary CreateDuel(bool won, int roundWins, int kills)
        {
            roundWins = Mathf.Max(0, roundWins);
            kills = Mathf.Max(0, kills);
            var coinReward = CalculateDuelCoinReward(won, roundWins, kills);
            return new MatchOutcomeSummary(kills, won ? 1 : 2, 0, coinReward);
        }

        public static int CalculateCoinReward(int kills, int survivalSeconds, int placement)
        {
            placement = Mathf.Clamp(placement, 1, 20);
            return Mathf.Max(0, (kills * 11) + (survivalSeconds * 12) + ((20 - placement) * 13));
        }

        public static int CalculateDuelCoinReward(bool won, int roundWins, int kills)
        {
            var baseReward = won ? 40 : 15;
            return Mathf.Max(0, baseReward + (roundWins * 6) + (kills * 4));
        }
    }
}
