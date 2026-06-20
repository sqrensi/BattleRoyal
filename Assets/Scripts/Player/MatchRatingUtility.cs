using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class MatchRatingUtility
    {
        public const int DefaultRating = 1000;
        public const int MaxDelta = 30;
        public const int MinDelta = -30;
        private const int BrSize = 20;
        private const int MaxKillBonus = 15;
        private const int PointsPerKill = 3;

        public static int CalculateDelta(int placement, int kills = 0)
        {
            placement = Mathf.Clamp(placement, 1, BrSize);
            kills = Mathf.Max(0, kills);

            var placementDelta = Mathf.RoundToInt(22f - ((placement - 1f) / (BrSize - 1f)) * 44f);
            var killBonus = Mathf.Min(kills * PointsPerKill, MaxKillBonus);
            return Mathf.Clamp(placementDelta + killBonus, MinDelta, MaxDelta);
        }

        public static int CalculateDuelDelta(bool won)
        {
            return won ? 15 : -15;
        }

        public static string FormatDelta(int delta)
        {
            return delta >= 0 ? $"+{delta}" : delta.ToString();
        }
    }
}
