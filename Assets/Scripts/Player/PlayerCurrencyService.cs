using System;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class PlayerCurrencyService
    {
        private const string BalancePrefKey = "player_currency_balance";

        public static event Action BalanceChanged;

        public static int Balance => UserScopedPlayerPrefs.GetInt(BalancePrefKey, 0);

        public static void ApplyFromServer(int amount)
        {
            var clamped = Mathf.Max(0, amount);
            UserScopedPlayerPrefs.SetInt(BalancePrefKey, clamped);
            BalanceChanged?.Invoke();
        }

        public static void SetBalance(int amount)
        {
            var clamped = Mathf.Max(0, amount);
            UserScopedPlayerPrefs.SetInt(BalancePrefKey, clamped);
            PlayerPrefs.Save();
            BalanceChanged?.Invoke();
        }

        public static void AddCurrency(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            SetBalance(Balance + amount);
        }

        public static bool TrySpend(int amount)
        {
            if (amount <= 0)
            {
                return true;
            }

            if (Balance < amount)
            {
                return false;
            }

            SetBalance(Balance - amount);
            return true;
        }
    }
}
