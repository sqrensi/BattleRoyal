using System;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class PlayerIdentityService
    {
        private const string PlayerIdPrefKey = "player_external_id_v1";
        private const string YandexUniqueIdPrefKey = "player_yandex_unique_id_v1";
        private const string YandexAuthLinkedPrefKey = "player_yandex_auth_linked_v1";
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

        public static bool HasAuthorizedYandexLink()
        {
            return PlayerPrefs.GetInt(YandexAuthLinkedPrefKey, 0) == 1 &&
                   TryGetSavedYandexUniqueId(out _);
        }

        public static bool TryApplyYandexUniqueId(string uniqueId, bool requireAuthorizedLink = false)
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

            var previousPlayerId = PlayerPrefs.GetString(PlayerIdPrefKey, string.Empty).Trim();
            var externalPlayerId = BuildExternalPlayerIdFromYandexUniqueId(normalizedUniqueId);
            PlayerPrefs.SetString(YandexUniqueIdPrefKey, normalizedUniqueId);
            PlayerPrefs.SetString(PlayerIdPrefKey, externalPlayerId);
            if (requireAuthorizedLink)
            {
                PlayerPrefs.SetInt(YandexAuthLinkedPrefKey, 1);
            }

            PlayerPrefs.Save();

            if (!string.IsNullOrWhiteSpace(previousPlayerId) &&
                !string.Equals(previousPlayerId, externalPlayerId, StringComparison.Ordinal))
            {
                PlayerSettingsPrefs.MigrateFromPlayerId(previousPlayerId, externalPlayerId);
                ClientSettingsService.ReloadForCurrentPlayer();
            }

            return true;
        }

        public static bool TryApplyAuthorizedYandexUniqueId(string uniqueId)
        {
            return TryApplyYandexUniqueId(uniqueId, requireAuthorizedLink: true);
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
            PlayerPrefs.DeleteKey(YandexAuthLinkedPrefKey);
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
#if UNITY_WEBGL && !UNITY_EDITOR
            // In WebGL guest ids should be scoped to browser storage.
            // deviceUniqueIdentifier can collapse multiple incognito/browser sessions
            // into the same guest profile, so use a random id and persist it via PlayerPrefs.
            return $"player-{Guid.NewGuid():N}";
#else
            var deviceId = SystemInfo.deviceUniqueIdentifier;
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                return $"player-{Mathf.Abs(deviceId.GetHashCode()):X8}";
            }

            return $"player-{Guid.NewGuid():N}";
#endif
        }
    }
}
