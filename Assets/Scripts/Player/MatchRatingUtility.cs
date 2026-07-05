using UnityEngine;

namespace ShooterPrototype.Player
{
    public readonly struct DuelRewardInput
    {
        public bool Won { get; }
        public int RoundWins { get; }
        public int RoundLosses { get; }
        public int PlayerRating { get; }
        public int OpponentRating { get; }
        public int DamageDealt { get; }

        public DuelRewardInput(
            bool won,
            int roundWins,
            int roundLosses,
            int playerRating,
            int opponentRating,
            int damageDealt)
        {
            Won = won;
            RoundWins = Mathf.Max(0, roundWins);
            RoundLosses = Mathf.Max(0, roundLosses);
            PlayerRating = Mathf.Max(0, playerRating);
            OpponentRating = Mathf.Max(0, opponentRating);
            DamageDealt = Mathf.Max(0, damageDealt);
        }
    }

    public readonly struct DuelRewardResult
    {
        public int RatingDelta { get; }
        public int CoinReward { get; }

        public DuelRewardResult(int ratingDelta, int coinReward)
        {
            RatingDelta = ratingDelta;
            CoinReward = coinReward;
        }
    }

    public static class MatchRatingUtility
    {
        public const int DefaultRating = 1000;
        public const int MaxDelta = 30;
        public const int MinDelta = -30;
        public const int DuelMaxDelta = 27;
        public const int DuelMinDelta = -27;
        public const int DuelMaxCoins = 155;
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

        public static DuelRewardResult CalculateDuelRewards(DuelRewardInput input)
        {
            var margin = input.RoundWins - input.RoundLosses;
            var rating = input.Won ? 10 : -10;

            if (input.Won)
            {
                rating += margin >= 2 ? 9 : 4;
            }
            else if (input.RoundLosses >= 2 && input.RoundWins == 0)
            {
                rating -= 5;
            }
            else if (input.RoundWins >= 1)
            {
                rating += 6;
            }

            var expected = 1f / (1f + Mathf.Pow(10f, (input.PlayerRating - input.OpponentRating) / 400f));
            var score = input.Won ? 1f : 0f;
            rating += Mathf.RoundToInt(10f * (score - expected));

            rating += input.Won
                ? Mathf.Min(5, input.DamageDealt / 120)
                : Mathf.Min(3, input.DamageDealt / 180);

            var ratingDelta = Mathf.Clamp(rating, DuelMinDelta, DuelMaxDelta);

            var coins = 0;
            if (input.Won)
            {
                coins = 55;
                coins += margin >= 2 ? 35 : 18;
                coins += input.RoundWins * 12;
            }
            else
            {
                coins = 12;
                coins += input.RoundWins * 22;
                if (input.RoundLosses == 2 && input.RoundWins == 1)
                {
                    coins += 15;
                }
            }

            if (input.Won && input.OpponentRating > input.PlayerRating)
            {
                coins += Mathf.Min(25, (input.OpponentRating - input.PlayerRating) / 40);
            }
            else if (!input.Won && input.OpponentRating < input.PlayerRating)
            {
                coins += Mathf.Min(18, (input.PlayerRating - input.OpponentRating) / 50);
            }

            coins += Mathf.Min(input.Won ? 20 : 12, input.DamageDealt / 55);
            var coinReward = Mathf.Clamp(coins, 0, DuelMaxCoins);

            return new DuelRewardResult(ratingDelta, coinReward);
        }

        public static int CalculateDuelDelta(DuelRewardInput input)
        {
            return CalculateDuelRewards(input).RatingDelta;
        }

        public static string FormatDelta(int delta)
        {
            return delta >= 0 ? $"+{delta}" : delta.ToString();
        }

        public static int RollNearbyDuelRating(int playerRating)
        {
            var baseRating = Mathf.Max(0, playerRating);
            var offset = Random.Range(18, 91);
            if (Random.value < 0.5f)
            {
                offset = -offset;
            }

            return Mathf.Max(0, baseRating + offset);
        }
    }
}
