using System;
using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Client settings are always stored per player id, independent of server sync / offline progress mode.
    /// </summary>
    public static class PlayerSettingsPrefs
    {
        private const string KeyPrefix = "settings.user.";

        public static string BuildKey(string baseKey)
        {
            var playerId = NormalizePlayerId(PlayerIdentityService.GetOrCreatePlayerId());
            return KeyPrefix + playerId + "." + baseKey;
        }

        public static bool HasKey(string baseKey)
        {
            return TryReadExistingKey(baseKey, out _);
        }

        public static float GetFloat(string baseKey, float defaultValue)
        {
            if (TryReadExistingKey(baseKey, out var key))
            {
                return PlayerPrefs.GetFloat(key, defaultValue);
            }

            return defaultValue;
        }

        public static void SetFloat(string baseKey, float value)
        {
            PlayerPrefs.SetFloat(BuildKey(baseKey), value);
        }

        public static int GetInt(string baseKey, int defaultValue)
        {
            if (TryReadExistingKey(baseKey, out var key))
            {
                return PlayerPrefs.GetInt(key, defaultValue);
            }

            return defaultValue;
        }

        public static void SetInt(string baseKey, int value)
        {
            PlayerPrefs.SetInt(BuildKey(baseKey), value);
        }

        public static string GetString(string baseKey, string defaultValue = "")
        {
            if (TryReadExistingKey(baseKey, out var key))
            {
                return PlayerPrefs.GetString(key, defaultValue);
            }

            return defaultValue;
        }

        public static void SetString(string baseKey, string value)
        {
            PlayerPrefs.SetString(BuildKey(baseKey), value ?? string.Empty);
        }

        public static void DeleteKey(string baseKey)
        {
            if (TryReadExistingKey(baseKey, out var key))
            {
                PlayerPrefs.DeleteKey(key);
            }

            if (PlayerPrefs.HasKey(baseKey))
            {
                PlayerPrefs.DeleteKey(baseKey);
            }
        }

        public static void MigrateFromPlayerId(string fromPlayerId, string toPlayerId)
        {
            var normalizedFrom = NormalizePlayerId(fromPlayerId);
            var normalizedTo = NormalizePlayerId(toPlayerId);
            if (string.IsNullOrWhiteSpace(normalizedFrom) ||
                string.Equals(normalizedFrom, normalizedTo, StringComparison.Ordinal))
            {
                return;
            }

            foreach (var baseKey in ClientSettingsService.GetMigratableBaseKeys())
            {
                CopyBaseKeyIfMissing(normalizedFrom, normalizedTo, baseKey);
            }

            PlayerPrefs.Save();
        }

        private static void CopyBaseKeyIfMissing(string fromPlayerId, string toPlayerId, string baseKey)
        {
            var destinationKey = BuildKeyForPlayer(toPlayerId, baseKey);
            if (PlayerPrefs.HasKey(destinationKey))
            {
                return;
            }

            var sourceKeys = new[]
            {
                BuildKeyForPlayer(fromPlayerId, baseKey),
                "offline_local." + baseKey,
                "user." + fromPlayerId + "." + baseKey,
                baseKey
            };

            for (var i = 0; i < sourceKeys.Length; i++)
            {
                var sourceKey = sourceKeys[i];
                if (!PlayerPrefs.HasKey(sourceKey))
                {
                    continue;
                }

                if (ClientSettingsService.IsIntegerSettingKey(baseKey))
                {
                    PlayerPrefs.SetInt(destinationKey, PlayerPrefs.GetInt(sourceKey, 0));
                }
                else
                {
                    PlayerPrefs.SetFloat(destinationKey, PlayerPrefs.GetFloat(sourceKey, 0f));
                }

                return;
            }
        }

        private static string BuildKeyForPlayer(string playerId, string baseKey)
        {
            return KeyPrefix + NormalizePlayerId(playerId) + "." + baseKey;
        }

        private static bool TryReadExistingKey(string baseKey, out string resolvedKey)
        {
            var scopedKey = BuildKey(baseKey);
            if (PlayerPrefs.HasKey(scopedKey))
            {
                resolvedKey = scopedKey;
                return true;
            }

            var offlineKey = "offline_local." + baseKey;
            if (PlayerPrefs.HasKey(offlineKey))
            {
                resolvedKey = offlineKey;
                return true;
            }

            var legacyUserKey = "user." + NormalizePlayerId(PlayerIdentityService.GetOrCreatePlayerId()) + "." + baseKey;
            if (PlayerPrefs.HasKey(legacyUserKey))
            {
                resolvedKey = legacyUserKey;
                return true;
            }

            if (PlayerPrefs.HasKey(baseKey))
            {
                resolvedKey = baseKey;
                return true;
            }

            resolvedKey = null;
            return false;
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
