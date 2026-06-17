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

        public static int CalculateCoinReward(int kills, int survivalSeconds, int placement)
        {
            placement = Mathf.Clamp(placement, 1, 20);
            return Mathf.Max(0, (kills * 11) + (survivalSeconds * 12) + ((20 - placement) * 13));
        }
    }
}
