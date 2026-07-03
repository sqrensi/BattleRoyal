using System;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class UserScopedPlayerPrefs
    {
        public static string BuildKey(string baseKey)
        {
            var playerId = NormalizePlayerId(PlayerIdentityService.GetOrCreatePlayerId());
            return "user." + playerId + "." + baseKey;
        }

        public static bool HasKey(string baseKey)
        {
            return PlayerPrefs.HasKey(BuildKey(baseKey)) || PlayerPrefs.HasKey(baseKey);
        }

        public static float GetFloat(string baseKey, float defaultValue)
        {
            var scopedKey = BuildKey(baseKey);
            if (PlayerPrefs.HasKey(scopedKey))
            {
                return PlayerPrefs.GetFloat(scopedKey, defaultValue);
            }

            if (PlayerPrefs.HasKey(baseKey))
            {
                return PlayerPrefs.GetFloat(baseKey, defaultValue);
            }

            return defaultValue;
        }

        public static void SetFloat(string baseKey, float value)
        {
            PlayerPrefs.SetFloat(BuildKey(baseKey), value);
        }

        public static int GetInt(string baseKey, int defaultValue)
        {
            var scopedKey = BuildKey(baseKey);
            if (PlayerPrefs.HasKey(scopedKey))
            {
                return PlayerPrefs.GetInt(scopedKey, defaultValue);
            }

            if (PlayerPrefs.HasKey(baseKey))
            {
                return PlayerPrefs.GetInt(baseKey, defaultValue);
            }

            return defaultValue;
        }

        public static void SetInt(string baseKey, int value)
        {
            PlayerPrefs.SetInt(BuildKey(baseKey), value);
        }

        public static string GetString(string baseKey, string defaultValue = "")
        {
            var scopedKey = BuildKey(baseKey);
            if (PlayerPrefs.HasKey(scopedKey))
            {
                return PlayerPrefs.GetString(scopedKey, defaultValue);
            }

            if (PlayerPrefs.HasKey(baseKey))
            {
                return PlayerPrefs.GetString(baseKey, defaultValue);
            }

            return defaultValue;
        }

        public static void SetString(string baseKey, string value)
        {
            PlayerPrefs.SetString(BuildKey(baseKey), value ?? string.Empty);
        }

        public static void DeleteKey(string baseKey)
        {
            var scopedKey = BuildKey(baseKey);
            if (PlayerPrefs.HasKey(scopedKey))
            {
                PlayerPrefs.DeleteKey(scopedKey);
            }

            if (PlayerPrefs.HasKey(baseKey))
            {
                PlayerPrefs.DeleteKey(baseKey);
            }
        }

        private static string NormalizePlayerId(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
            {
                return "default";
            }

            var trimmed = playerId.Trim();
            var buffer = new char[trimmed.Length];
            for (var i = 0; i < trimmed.Length; i++)
            {
                var ch = trimmed[i];
                buffer[i] = char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_';
            }

            return new string(buffer);
        }
    }
}
