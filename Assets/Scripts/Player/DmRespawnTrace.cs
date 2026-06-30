using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Temporary DM respawn diagnostics. Enable in browser console:
    ///   localStorage.setItem('unity.playerprefs.dm_respawn_trace', '1'); location.reload();
    /// Or in Editor: PlayerPrefs.SetInt("dm_respawn_trace", 1);
    /// </summary>
    public static class DmRespawnTrace
    {
        private const string PrefKey = "dm_respawn_trace";
        private static bool prefsLoaded;
        private static bool enabled;

        public static bool Enabled
        {
            get
            {
                EnsurePrefsLoaded();
                return enabled;
            }
        }

        public static void EnsurePrefsLoaded()
        {
            if (prefsLoaded)
            {
                return;
            }

            prefsLoaded = true;
            enabled = PlayerPrefs.GetInt(PrefKey, 0) == 1;
        }

        public static void SetEnabled(bool value)
        {
            enabled = value;
            prefsLoaded = true;
            PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }

        public static void Log(string category, string message)
        {
            if (!Enabled)
            {
                return;
            }

            Debug.Log($"[DMRespawnTrace][{category}] {message}");
        }

        public static void LogThrottled(
            ref float lastLoggedAt,
            float intervalSeconds,
            string category,
            string message)
        {
            if (!Enabled)
            {
                return;
            }

            var now = Time.unscaledTime;
            if (now - lastLoggedAt < Mathf.Max(0.1f, intervalSeconds))
            {
                return;
            }

            lastLoggedAt = now;
            Log(category, message);
        }
    }
}
