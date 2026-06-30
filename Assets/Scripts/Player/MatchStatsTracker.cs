using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class MatchStatsTracker
    {
        private static int damageDealtThisMatch;

        public static int DamageDealtThisMatch => damageDealtThisMatch;

        public static void ResetForNewMatch()
        {
            damageDealtThisMatch = 0;
        }

        public static void AddDamageDealt(float damage)
        {
            if (damage <= 0f)
            {
                return;
            }

            var rounded = Mathf.RoundToInt(damage);
            damageDealtThisMatch += rounded;
            MatchScoreboardTracker.AddLocalDamage(rounded);
        }
    }
}
