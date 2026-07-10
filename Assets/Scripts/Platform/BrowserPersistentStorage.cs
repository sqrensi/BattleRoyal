using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace ShooterPrototype.Platform
{
    public static class BrowserPersistentStorage
    {
        private const string AuthYandexUniqueIdKey = "auth/yandex_unique_id";
        private const string AuthYandexLinkedKey = "auth/yandex_linked";
        private const string AuthPlayerExternalIdKey = "auth/player_external_id";
        private const string LegacyPlayerIdPrefKey = "player_external_id_v1";
        private const string LegacyYandexUniqueIdPrefKey = "player_yandex_unique_id_v1";
        private const string LegacyYandexAuthLinkedPrefKey = "player_yandex_auth_linked_v1";
        private const string LegacyNoAdsExpiresPrefKey = "player_no_ads_expires_v1";
        private const string LegacyVipPrefixExpiresPrefKey = "player_vip_prefix_expires_v1";

        private static bool legacyMigrationAttempted;

        public static bool IsAvailable
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        public static bool HasKey(string key)
        {
            if (!IsAvailable || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            return BrowserPersistentStorage_HasKey(key) != 0;
        }

        public static string GetString(string key, string defaultValue = "")
        {
            if (!IsAvailable || string.IsNullOrWhiteSpace(key))
            {
                return defaultValue ?? string.Empty;
            }

            var buffer = new byte[512];
            var bufferHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                BrowserPersistentStorage_GetString(key, bufferHandle.AddrOfPinnedObject(), buffer.Length);
                var length = 0;
                while (length < buffer.Length && buffer[length] != 0)
                {
                    length++;
                }

                if (length <= 0)
                {
                    return defaultValue ?? string.Empty;
                }

                return Encoding.UTF8.GetString(buffer, 0, length);
            }
            finally
            {
                bufferHandle.Free();
            }
        }

        public static void SetString(string key, string value)
        {
            if (!IsAvailable || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            BrowserPersistentStorage_SetString(key, value ?? string.Empty);
        }

        public static void RemoveKey(string key)
        {
            if (!IsAvailable || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            BrowserPersistentStorage_RemoveKey(key);
        }

        public static int GetInt(string key, int defaultValue = 0)
        {
            var stored = GetString(key, string.Empty);
            return int.TryParse(stored, out var value) ? value : defaultValue;
        }

        public static void SetInt(string key, int value)
        {
            SetString(key, value.ToString());
        }

        public static long GetLong(string key, long defaultValue = 0L)
        {
            var stored = GetString(key, string.Empty);
            return long.TryParse(stored, out var value) ? value : defaultValue;
        }

        public static void SetLong(string key, long value)
        {
            SetString(key, value.ToString());
        }

        public static void SaveAuthState(string yandexUniqueId, string externalPlayerId, bool linked)
        {
            if (!IsAvailable)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(yandexUniqueId))
            {
                SetString(AuthYandexUniqueIdKey, yandexUniqueId.Trim());
            }

            if (!string.IsNullOrWhiteSpace(externalPlayerId))
            {
                SetString(AuthPlayerExternalIdKey, externalPlayerId.Trim());
            }

            SetInt(AuthYandexLinkedKey, linked ? 1 : 0);
        }

        public static bool TryGetSavedAuth(out string yandexUniqueId, out string externalPlayerId, out bool linked)
        {
            yandexUniqueId = string.Empty;
            externalPlayerId = string.Empty;
            linked = false;

            if (!IsAvailable)
            {
                return false;
            }

            yandexUniqueId = GetString(AuthYandexUniqueIdKey, string.Empty).Trim();
            externalPlayerId = GetString(AuthPlayerExternalIdKey, string.Empty).Trim();
            linked = GetInt(AuthYandexLinkedKey, 0) == 1;
            return linked && !string.IsNullOrWhiteSpace(yandexUniqueId);
        }

        public static string BuildPassKey(string passType, string scopeId)
        {
            var normalizedScope = NormalizeScopeId(scopeId);
            return string.IsNullOrEmpty(normalizedScope)
                ? string.Empty
                : $"pass/{normalizedScope}/{passType}";
        }

        public static void SavePassExpiry(string passType, string scopeId, long expiresAtMs)
        {
            var key = BuildPassKey(passType, scopeId);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (expiresAtMs > 0)
            {
                SetLong(key, expiresAtMs);
            }
            else
            {
                RemoveKey(key);
            }
        }

        public static long GetPassExpiry(string passType, string scopeId)
        {
            var key = BuildPassKey(passType, scopeId);
            return string.IsNullOrEmpty(key) ? 0L : GetLong(key, 0L);
        }

        public static void MigrateLegacyPlayerPrefs()
        {
            if (!IsAvailable || legacyMigrationAttempted)
            {
                return;
            }

            legacyMigrationAttempted = true;

            if (!HasKey(AuthYandexUniqueIdKey))
            {
                var legacyYandexId = PlayerPrefs.GetString(LegacyYandexUniqueIdPrefKey, string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(legacyYandexId))
                {
                    SetString(AuthYandexUniqueIdKey, legacyYandexId);
                }
            }

            if (!HasKey(AuthPlayerExternalIdKey))
            {
                var legacyPlayerId = PlayerPrefs.GetString(LegacyPlayerIdPrefKey, string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(legacyPlayerId))
                {
                    SetString(AuthPlayerExternalIdKey, legacyPlayerId);
                }
            }

            if (!HasKey(AuthYandexLinkedKey))
            {
                var legacyLinked = PlayerPrefs.GetInt(LegacyYandexAuthLinkedPrefKey, 0);
                if (legacyLinked == 1)
                {
                    SetInt(AuthYandexLinkedKey, 1);
                }
            }

            var scopeId = GetString(AuthYandexUniqueIdKey, string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(scopeId))
            {
                scopeId = GetString(AuthPlayerExternalIdKey, string.Empty).Trim();
            }

            if (!string.IsNullOrWhiteSpace(scopeId))
            {
                MigrateLegacyPass(LegacyNoAdsExpiresPrefKey, "no_ads_expires_ms", scopeId);
                MigrateLegacyPass(LegacyVipPrefixExpiresPrefKey, "vip_prefix_expires_ms", scopeId);
            }
        }

        private static void MigrateLegacyPass(string legacyPrefKey, string passType, string scopeId)
        {
            var passKey = BuildPassKey(passType, scopeId);
            if (string.IsNullOrEmpty(passKey) || HasKey(passKey))
            {
                return;
            }

            var legacyValue = PlayerPrefs.GetString(legacyPrefKey, "0");
            if (long.TryParse(legacyValue, out var expiresAtMs) && expiresAtMs > 0)
            {
                SetLong(passKey, expiresAtMs);
            }
        }

        private static string NormalizeScopeId(string scopeId)
        {
            if (string.IsNullOrWhiteSpace(scopeId))
            {
                return string.Empty;
            }

            var trimmed = scopeId.Trim();
            if (trimmed.StartsWith("yg-", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(3);
            }

            return trimmed;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern int BrowserPersistentStorage_HasKey(string key);

        [DllImport("__Internal")]
        private static extern void BrowserPersistentStorage_GetString(string key, IntPtr buffer, int bufferLen);

        [DllImport("__Internal")]
        private static extern void BrowserPersistentStorage_SetString(string key, string value);

        [DllImport("__Internal")]
        private static extern void BrowserPersistentStorage_RemoveKey(string key);
#else
        private static int BrowserPersistentStorage_HasKey(string key) => 0;

        private static void BrowserPersistentStorage_GetString(string key, IntPtr buffer, int bufferLen)
        {
        }

        private static void BrowserPersistentStorage_SetString(string key, string value)
        {
        }

        private static void BrowserPersistentStorage_RemoveKey(string key)
        {
        }
#endif
    }
}
