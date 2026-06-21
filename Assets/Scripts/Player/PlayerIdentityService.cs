using System;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class PlayerIdentityService
    {
        private const string PlayerIdPrefKey = "player_external_id_v1";
        private const string YandexUniqueIdPrefKey = "player_yandex_unique_id_v1";
        private const string YandexPlayerIdPrefix = "yg-";

        public static string GetOrCreatePlayerId()
        {
            var launchOverride = ResolveLaunchPlayerIdOverride();
            if (!string.IsNullOrWhiteSpace(launchOverride))
            {
                return launchOverride;
            }

            var saved = PlayerPrefs.GetString(PlayerIdPrefKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(saved))
            {
                return saved.Trim();
            }

            var generated = BuildStablePlayerId();
            PlayerPrefs.SetString(PlayerIdPrefKey, generated);
            PlayerPrefs.Save();
            return generated;
        }

        public static bool TryGetSavedYandexUniqueId(out string uniqueId)
        {
            uniqueId = PlayerPrefs.GetString(YandexUniqueIdPrefKey, string.Empty).Trim();
            return !string.IsNullOrWhiteSpace(uniqueId);
        }

        public static bool TryApplyYandexUniqueId(string uniqueId)
        {
            var normalizedUniqueId = NormalizePlayerId(uniqueId);
            if (string.IsNullOrWhiteSpace(normalizedUniqueId))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(ResolveLaunchPlayerIdOverride()))
            {
                return false;
            }

            var externalPlayerId = BuildExternalPlayerIdFromYandexUniqueId(normalizedUniqueId);
            PlayerPrefs.SetString(YandexUniqueIdPrefKey, normalizedUniqueId);
            PlayerPrefs.SetString(PlayerIdPrefKey, externalPlayerId);
            PlayerPrefs.Save();
            return true;
        }

        public static string BuildExternalPlayerIdFromYandexUniqueId(string uniqueId)
        {
            var normalizedUniqueId = NormalizePlayerId(uniqueId);
            if (string.IsNullOrWhiteSpace(normalizedUniqueId))
            {
                return string.Empty;
            }

            return YandexPlayerIdPrefix + normalizedUniqueId;
        }

        /// <summary>
        /// Clears saved player id so the next launch creates a fresh profile (unless -playerId is passed).
        /// </summary>
        public static void ResetSavedPlayerId()
        {
            PlayerPrefs.DeleteKey(PlayerIdPrefKey);
            PlayerPrefs.DeleteKey(YandexUniqueIdPrefKey);
            PlayerPrefs.Save();
        }

        private static string ResolveLaunchPlayerIdOverride()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (string.IsNullOrWhiteSpace(arg))
                {
                    continue;
                }

                if (TryReadArgValue(args, i, "-playerId", out var playerIdValue) ||
                    TryReadArgValue(args, i, "--playerId", out playerIdValue))
                {
                    return NormalizePlayerId(playerIdValue);
                }

                if (TryReadArgValue(args, i, "-clientIndex", out var clientIndexValue) ||
                    TryReadArgValue(args, i, "--clientIndex", out clientIndexValue))
                {
                    if (int.TryParse(clientIndexValue, out var clientIndex))
                    {
                        return NormalizePlayerId($"player-local-{Mathf.Max(1, clientIndex)}");
                    }
                }
            }

            return string.Empty;
        }

        private static bool TryReadArgValue(string[] args, int index, string key, out string value)
        {
            value = string.Empty;
            if (!string.Equals(args[index], key, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (index + 1 >= args.Length)
            {
                return false;
            }

            value = args[index + 1];
            return !string.IsNullOrWhiteSpace(value);
        }

        private static string NormalizePlayerId(string value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? string.Empty : trimmed;
        }

        private static string BuildStablePlayerId()
        {
            var deviceId = SystemInfo.deviceUniqueIdentifier;
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                return $"player-{Mathf.Abs(deviceId.GetHashCode()):X8}";
            }

            return $"player-{Guid.NewGuid():N}";
        }
    }
}
