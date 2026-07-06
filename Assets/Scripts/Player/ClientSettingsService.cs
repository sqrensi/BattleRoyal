using System;
using System.Collections.Generic;
using System.Globalization;
using ShooterPrototype.Bootstrap;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class ClientSettingsService
    {
        private const string MasterVolumeKey = "client_settings_master_volume";
        private const string MusicVolumeKey = "client_settings_music_volume";
        private const string RainVolumeKey = "client_settings_rain_volume";
        private const string SfxVolumeKey = "client_settings_sfx_volume";
        private const string MouseSensitivityKey = "client_settings_mouse_sensitivity";
        private const string RenderScaleKey = "client_settings_render_scale";
        private const string ShadowsEnabledKey = "client_settings_shadows_enabled";
        private const string PostProcessingKey = "client_settings_post_processing";
        private const string MsaaKey = "client_settings_msaa";
        private const string TextureMipmapLimitKey = "client_settings_texture_mipmap";
        private const string GlobalAdsSensitivityKey = "client_settings_global_ads_sensitivity";
        private const string LegacyMaxPerformanceKey = "client_max_performance";
        private const string AllowBotMatchmakingKey = "client_settings_allow_bot_matchmaking";
        private const string ShowMatchControlHintsKey = "client_settings_show_match_control_hints";
        private const string TargetFpsKey = "client_settings_target_fps";
        private const string LegacyMuteKey = "client_audio_muted";
        private const string AdsSensitivityKeyPrefix = "client_settings_ads_sensitivity_";

        public const float DefaultMasterVolume = 0.17f;
        public const float DefaultMusicVolume = 0.21f;
        public const float DefaultRainVolume = 0.30f;
        public const float DefaultSfxVolume = 0.75f;
        public const float DefaultMouseSensitivity = 2.2f;
        public const float DefaultRenderScale = 1f;
        public const float DefaultGlobalAdsSensitivity = 0.83f;
        public const int DefaultTargetFps = 60;

        private static readonly int[] TargetFpsOptions = { 30, 60, 144 };

        private static bool loaded;
        private static string loadedForPlayerId;

        public static event Action SettingsChanged;

        public static float MasterVolume { get; private set; } = DefaultMasterVolume;
        public static float MusicVolume { get; private set; } = DefaultMusicVolume;
        public static float RainVolume { get; private set; } = DefaultRainVolume;
        public static float SfxVolume { get; private set; } = DefaultSfxVolume;
        public static float MouseSensitivity { get; private set; } = DefaultMouseSensitivity;
        public static float RenderScale { get; private set; } = DefaultRenderScale;
        public static bool ShadowsEnabled { get; private set; } = true;
        public static bool PostProcessingEnabled { get; private set; } = true;
        public static int MsaaSampleCount { get; private set; } = 4;
        public static int TextureMipmapLimit { get; private set; }
        public static float GlobalAdsSensitivityMultiplier { get; private set; } = DefaultGlobalAdsSensitivity;
        public static bool AllowBotMatchmaking { get; private set; } = true;
        public static bool ShowMatchControlHints { get; private set; } = true;
        public static int TargetFps { get; private set; } = DefaultTargetFps;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            EnsureLoaded();
            ApplyMasterVolume();
            ApplyTargetFrameRate();
        }

        public static void EnsureLoaded()
        {
            var playerId = PlayerIdentityService.GetOrCreatePlayerId();
            if (loaded && string.Equals(loadedForPlayerId, playerId, StringComparison.Ordinal))
            {
                return;
            }

            loaded = false;
            loadedForPlayerId = playerId;

            if (UserScopedPlayerPrefs.HasKey(LegacyMuteKey) && !PlayerSettingsPrefs.HasKey(MasterVolumeKey))
            {
                MasterVolume = UserScopedPlayerPrefs.GetInt(LegacyMuteKey, 0) == 1 ? 0f : DefaultMasterVolume;
                PlayerSettingsPrefs.SetFloat(MasterVolumeKey, MasterVolume);
            }
            else
            {
                MasterVolume = PlayerSettingsPrefs.GetFloat(MasterVolumeKey, DefaultMasterVolume);
            }

            MusicVolume = PlayerSettingsPrefs.GetFloat(MusicVolumeKey, DefaultMusicVolume);
            RainVolume = PlayerSettingsPrefs.GetFloat(RainVolumeKey, DefaultRainVolume);
            SfxVolume = PlayerSettingsPrefs.GetFloat(SfxVolumeKey, DefaultSfxVolume);
            MouseSensitivity = PlayerSettingsPrefs.GetFloat(MouseSensitivityKey, DefaultMouseSensitivity);
            RenderScale = PlayerSettingsPrefs.GetFloat(RenderScaleKey, DefaultRenderScale);
            GlobalAdsSensitivityMultiplier = PlayerSettingsPrefs.GetFloat(
                GlobalAdsSensitivityKey,
                DefaultGlobalAdsSensitivity);
            AllowBotMatchmaking = PlayerSettingsPrefs.GetInt(AllowBotMatchmakingKey, 1) == 1;
            ShowMatchControlHints = PlayerSettingsPrefs.GetInt(ShowMatchControlHintsKey, 1) == 1;
            TargetFps = NormalizeTargetFps(PlayerSettingsPrefs.GetInt(TargetFpsKey, DefaultTargetFps));

            if (PlayerSettingsPrefs.HasKey(ShadowsEnabledKey))
            {
                ShadowsEnabled = PlayerSettingsPrefs.GetInt(ShadowsEnabledKey, 1) == 1;
                PostProcessingEnabled = PlayerSettingsPrefs.GetInt(PostProcessingKey, 1) == 1;
                MsaaSampleCount = NormalizeMsaa(PlayerSettingsPrefs.GetInt(MsaaKey, 4));
                TextureMipmapLimit = Mathf.Clamp(PlayerSettingsPrefs.GetInt(TextureMipmapLimitKey, 0), 0, 2);
            }
            else if (UserScopedPlayerPrefs.GetInt(LegacyMaxPerformanceKey, 0) == 1 ||
                     PlayerPrefs.GetInt(LegacyMaxPerformanceKey, 0) == 1)
            {
                ApplyLowQualityBundle(save: true);
            }
            else
            {
                ApplyHighQualityBundle(save: false);
            }

            UserScopedPlayerPrefs.DeleteKey(LegacyMaxPerformanceKey);
            PlayerSettingsPrefs.DeleteKey(LegacyMaxPerformanceKey);

            MasterVolume = Mathf.Clamp01(MasterVolume);
            MusicVolume = Mathf.Clamp01(MusicVolume);
            RainVolume = Mathf.Clamp01(RainVolume);
            SfxVolume = Mathf.Clamp01(SfxVolume);
            MouseSensitivity = Mathf.Clamp(MouseSensitivity, 0.1f, 10f);
            RenderScale = Mathf.Clamp(RenderScale, 0.65f, 1f);
            GlobalAdsSensitivityMultiplier = Mathf.Clamp(GlobalAdsSensitivityMultiplier, 0.1f, 3f);

            loaded = true;
        }

        public static void ReloadForCurrentPlayer()
        {
            loaded = false;
            loadedForPlayerId = null;
            EnsureLoaded();
            ApplyMasterVolume();
            ApplyTargetFrameRate();
            ApplyGraphicsPreset();
            SettingsChanged?.Invoke();
        }

        public static string ExportSettingsJson()
        {
            EnsureLoaded();
            var entries = BuildSettingsEntries();
            return JsonUtility.ToJson(new SettingsExportPayload { entries = entries });
        }

        public static bool IsServerSettingsPayloadEmpty(string settingsJson)
        {
            if (string.IsNullOrWhiteSpace(settingsJson))
            {
                return true;
            }

            SettingsExportPayload payload;
            try
            {
                payload = JsonUtility.FromJson<SettingsExportPayload>(settingsJson);
            }
            catch (Exception)
            {
                return true;
            }

            return payload?.entries == null || payload.entries.Length == 0;
        }

        public static bool HasAnySavedSettings()
        {
            foreach (var baseKey in GetMigratableBaseKeys())
            {
                if (PlayerSettingsPrefs.HasKey(baseKey))
                {
                    return true;
                }
            }

            return false;
        }

        public static string[] GetMigratableBaseKeys()
        {
            var keys = new List<string>
            {
                MasterVolumeKey,
                MusicVolumeKey,
                RainVolumeKey,
                SfxVolumeKey,
                MouseSensitivityKey,
                RenderScaleKey,
                ShadowsEnabledKey,
                PostProcessingKey,
                MsaaKey,
                TextureMipmapLimitKey,
                GlobalAdsSensitivityKey,
                AllowBotMatchmakingKey,
                ShowMatchControlHintsKey,
                TargetFpsKey,
            };

            foreach (WeaponKind kind in Enum.GetValues(typeof(WeaponKind)))
            {
                keys.Add(BuildAdsSensitivityKey(kind));
            }

            return keys.ToArray();
        }

        public static bool IsIntegerSettingKey(string baseKey)
        {
            return baseKey == AllowBotMatchmakingKey ||
                   baseKey == ShowMatchControlHintsKey ||
                   baseKey == TargetFpsKey ||
                   baseKey == ShadowsEnabledKey ||
                   baseKey == PostProcessingKey ||
                   baseKey == MsaaKey ||
                   baseKey == TextureMipmapLimitKey;
        }

        public static void ApplyImportedSettingsSideEffects()
        {
            ApplyMasterVolume();
            ApplyTargetFrameRate();
            ApplyGraphicsPreset();
            SettingsChanged?.Invoke();
        }

        public static void ImportSettingsJson(string settingsJson, bool saveLocally)
        {
            if (string.IsNullOrWhiteSpace(settingsJson))
            {
                return;
            }

            SettingsExportPayload payload;
            try
            {
                payload = JsonUtility.FromJson<SettingsExportPayload>(settingsJson);
            }
            catch (Exception)
            {
                return;
            }

            if (payload?.entries == null || payload.entries.Length == 0)
            {
                return;
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < payload.entries.Length; i++)
            {
                var entry = payload.entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.key))
                {
                    continue;
                }

                values[entry.key] = entry.value ?? string.Empty;
            }

            ApplyImportedValues(values, saveLocally);
        }

        private static void ApplyImportedValues(Dictionary<string, string> values, bool saveLocally)
        {
            if (values.Count == 0)
            {
                return;
            }

            loaded = true;
            loadedForPlayerId = PlayerIdentityService.GetOrCreatePlayerId();

            if (TryReadFloat(values, MasterVolumeKey, out var masterVolume))
            {
                MasterVolume = masterVolume;
            }

            if (TryReadFloat(values, MusicVolumeKey, out var musicVolume))
            {
                MusicVolume = musicVolume;
            }

            if (TryReadFloat(values, RainVolumeKey, out var rainVolume))
            {
                RainVolume = rainVolume;
            }

            if (TryReadFloat(values, SfxVolumeKey, out var sfxVolume))
            {
                SfxVolume = sfxVolume;
            }

            if (TryReadFloat(values, MouseSensitivityKey, out var mouseSensitivity))
            {
                MouseSensitivity = mouseSensitivity;
            }

            if (TryReadFloat(values, RenderScaleKey, out var renderScale))
            {
                RenderScale = renderScale;
            }

            if (TryReadFloat(values, GlobalAdsSensitivityKey, out var adsSensitivity))
            {
                GlobalAdsSensitivityMultiplier = adsSensitivity;
            }

            if (TryReadInt(values, AllowBotMatchmakingKey, out var allowBots))
            {
                AllowBotMatchmaking = allowBots == 1;
            }

            if (TryReadInt(values, ShowMatchControlHintsKey, out var showHints))
            {
                ShowMatchControlHints = showHints == 1;
            }

            if (TryReadInt(values, TargetFpsKey, out var targetFps))
            {
                TargetFps = NormalizeTargetFps(targetFps);
            }

            if (TryReadInt(values, ShadowsEnabledKey, out var shadows))
            {
                ShadowsEnabled = shadows == 1;
            }

            if (TryReadInt(values, PostProcessingKey, out var postProcessing))
            {
                PostProcessingEnabled = postProcessing == 1;
            }

            if (TryReadInt(values, MsaaKey, out var msaa))
            {
                MsaaSampleCount = NormalizeMsaa(msaa);
            }

            if (TryReadInt(values, TextureMipmapLimitKey, out var textureLimit))
            {
                TextureMipmapLimit = Mathf.Clamp(textureLimit, 0, 2);
            }

            MasterVolume = Mathf.Clamp01(MasterVolume);
            MusicVolume = Mathf.Clamp01(MusicVolume);
            RainVolume = Mathf.Clamp01(RainVolume);
            SfxVolume = Mathf.Clamp01(SfxVolume);
            MouseSensitivity = Mathf.Clamp(MouseSensitivity, 0.1f, 10f);
            RenderScale = Mathf.Clamp(RenderScale, 0.65f, 1f);
            GlobalAdsSensitivityMultiplier = Mathf.Clamp(GlobalAdsSensitivityMultiplier, 0.1f, 3f);

            if (!saveLocally)
            {
                return;
            }

            SaveImportedValuesToPrefs(values);
        }

        private static void SaveImportedValuesToPrefs(Dictionary<string, string> values)
        {
            PlayerSettingsPrefs.SetFloat(MasterVolumeKey, MasterVolume);
            PlayerSettingsPrefs.SetFloat(MusicVolumeKey, MusicVolume);
            PlayerSettingsPrefs.SetFloat(RainVolumeKey, RainVolume);
            PlayerSettingsPrefs.SetFloat(SfxVolumeKey, SfxVolume);
            PlayerSettingsPrefs.SetFloat(MouseSensitivityKey, MouseSensitivity);
            PlayerSettingsPrefs.SetFloat(RenderScaleKey, RenderScale);
            PlayerSettingsPrefs.SetFloat(GlobalAdsSensitivityKey, GlobalAdsSensitivityMultiplier);
            PlayerSettingsPrefs.SetInt(AllowBotMatchmakingKey, AllowBotMatchmaking ? 1 : 0);
            PlayerSettingsPrefs.SetInt(ShowMatchControlHintsKey, ShowMatchControlHints ? 1 : 0);
            PlayerSettingsPrefs.SetInt(TargetFpsKey, TargetFps);
            PlayerSettingsPrefs.SetInt(ShadowsEnabledKey, ShadowsEnabled ? 1 : 0);
            PlayerSettingsPrefs.SetInt(PostProcessingKey, PostProcessingEnabled ? 1 : 0);
            PlayerSettingsPrefs.SetInt(MsaaKey, MsaaSampleCount);
            PlayerSettingsPrefs.SetInt(TextureMipmapLimitKey, TextureMipmapLimit);

            foreach (WeaponKind kind in Enum.GetValues(typeof(WeaponKind)))
            {
                var key = BuildAdsSensitivityKey(kind);
                if (!values.TryGetValue(key, out var raw) ||
                    !float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var perWeapon))
                {
                    continue;
                }

                PlayerSettingsPrefs.SetFloat(key, Mathf.Clamp(perWeapon, 0.1f, 5f));
            }

            PlayerPrefs.Save();
        }

        private static SettingsEntry[] BuildSettingsEntries()
        {
            var entries = new List<SettingsEntry>
            {
                Entry(MasterVolumeKey, MasterVolume),
                Entry(MusicVolumeKey, MusicVolume),
                Entry(RainVolumeKey, RainVolume),
                Entry(SfxVolumeKey, SfxVolume),
                Entry(MouseSensitivityKey, MouseSensitivity),
                Entry(RenderScaleKey, RenderScale),
                Entry(GlobalAdsSensitivityKey, GlobalAdsSensitivityMultiplier),
                Entry(AllowBotMatchmakingKey, AllowBotMatchmaking ? 1 : 0),
                Entry(ShowMatchControlHintsKey, ShowMatchControlHints ? 1 : 0),
                Entry(TargetFpsKey, TargetFps),
                Entry(ShadowsEnabledKey, ShadowsEnabled ? 1 : 0),
                Entry(PostProcessingKey, PostProcessingEnabled ? 1 : 0),
                Entry(MsaaKey, MsaaSampleCount),
                Entry(TextureMipmapLimitKey, TextureMipmapLimit),
            };

            foreach (WeaponKind kind in Enum.GetValues(typeof(WeaponKind)))
            {
                var key = BuildAdsSensitivityKey(kind);
                var value = PlayerSettingsPrefs.GetFloat(key, GetDefaultAdsSensitivity(kind));
                entries.Add(Entry(key, value));
            }

            return entries.ToArray();
        }

        private static SettingsEntry Entry(string key, float value)
        {
            return new SettingsEntry
            {
                key = key,
                value = value.ToString(CultureInfo.InvariantCulture)
            };
        }

        private static SettingsEntry Entry(string key, int value)
        {
            return new SettingsEntry
            {
                key = key,
                value = value.ToString(CultureInfo.InvariantCulture)
            };
        }

        private static bool TryReadFloat(Dictionary<string, string> values, string key, out float result)
        {
            result = 0f;
            return values.TryGetValue(key, out var raw) &&
                   float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryReadInt(Dictionary<string, string> values, string key, out int result)
        {
            result = 0;
            return values.TryGetValue(key, out var raw) &&
                   int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        [Serializable]
        private sealed class SettingsExportPayload
        {
            public SettingsEntry[] entries;
        }

        [Serializable]
        private sealed class SettingsEntry
        {
            public string key;
            public string value;
        }

        public static float GetDefaultAdsSensitivity(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.AssaultRifle:
                    return 1.85f;
                case WeaponKind.SniperRifle:
                    return 2.07f;
                case WeaponKind.Pistol:
                    return 1.4f;
                case WeaponKind.Mp7:
                    return 1.55f;
                default:
                    return 1f;
            }
        }

        public static string GetWeaponDisplayName(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.AssaultRifle:
                    return "AK-47";
                case WeaponKind.SniperRifle:
                    return "Снайперка";
                case WeaponKind.Pistol:
                    return "Пистолет";
                case WeaponKind.Mp7:
                    return "MP7";
                default:
                    return kind.ToString();
            }
        }

        public static float GetAdsSensitivity(WeaponKind kind)
        {
            EnsureLoaded();
            var perWeapon = PlayerSettingsPrefs.GetFloat(
                BuildAdsSensitivityKey(kind),
                GetDefaultAdsSensitivity(kind));
            return perWeapon * GlobalAdsSensitivityMultiplier;
        }

        public static float GetWeaponAdsSensitivity(WeaponKind kind)
        {
            EnsureLoaded();
            return PlayerSettingsPrefs.GetFloat(BuildAdsSensitivityKey(kind), GetDefaultAdsSensitivity(kind));
        }

        public static void SetMasterVolume(float value)
        {
            EnsureLoaded();
            MasterVolume = Mathf.Clamp01(value);
            PlayerSettingsPrefs.SetFloat(MasterVolumeKey, MasterVolume);
            PlayerSettingsPrefs.SetInt(LegacyMuteKey, MasterVolume <= 0.001f ? 1 : 0);
            SaveAndNotify();
            ApplyMasterVolume();
        }

        public static void SetMusicVolume(float value)
        {
            EnsureLoaded();
            MusicVolume = Mathf.Clamp01(value);
            PlayerSettingsPrefs.SetFloat(MusicVolumeKey, MusicVolume);
            SaveAndNotify();
        }

        public static void SetRainVolume(float value)
        {
            EnsureLoaded();
            RainVolume = Mathf.Clamp01(value);
            PlayerSettingsPrefs.SetFloat(RainVolumeKey, RainVolume);
            SaveAndNotify();
        }

        public static void SetSfxVolume(float value)
        {
            EnsureLoaded();
            SfxVolume = Mathf.Clamp01(value);
            PlayerSettingsPrefs.SetFloat(SfxVolumeKey, SfxVolume);
            SaveAndNotify();
        }

        public static void SetMouseSensitivity(float value)
        {
            EnsureLoaded();
            MouseSensitivity = Mathf.Clamp(value, 0.1f, 10f);
            PlayerSettingsPrefs.SetFloat(MouseSensitivityKey, MouseSensitivity);
            SaveAndNotify();
        }

        public static void SetGlobalAdsSensitivity(float value)
        {
            EnsureLoaded();
            GlobalAdsSensitivityMultiplier = Mathf.Clamp(value, 0.1f, 3f);
            PlayerSettingsPrefs.SetFloat(GlobalAdsSensitivityKey, GlobalAdsSensitivityMultiplier);
            SaveAndNotify();
        }

        public static void SetAdsSensitivity(WeaponKind kind, float value)
        {
            EnsureLoaded();
            var clamped = Mathf.Clamp(value, 0.1f, 5f);
            PlayerSettingsPrefs.SetFloat(BuildAdsSensitivityKey(kind), clamped);
            SaveAndNotify();
        }

        public static void SetRenderScale(float value)
        {
            EnsureLoaded();
            RenderScale = Mathf.Clamp(value, 0.65f, 1f);
            PlayerSettingsPrefs.SetFloat(RenderScaleKey, RenderScale);
            SaveAndNotify();
            ApplyGraphicsPreset();
        }

        public static void SetShadowsEnabled(bool enabled)
        {
            EnsureLoaded();
            ShadowsEnabled = enabled;
            PlayerSettingsPrefs.SetInt(ShadowsEnabledKey, enabled ? 1 : 0);
            SaveAndNotify();
            ApplyGraphicsPreset();
        }

        public static void SetPostProcessingEnabled(bool enabled)
        {
            EnsureLoaded();
            PostProcessingEnabled = enabled;
            PlayerSettingsPrefs.SetInt(PostProcessingKey, enabled ? 1 : 0);
            SaveAndNotify();
            ApplyGraphicsPreset();
        }

        public static void SetMsaaSampleCount(int sampleCount)
        {
            EnsureLoaded();
            MsaaSampleCount = NormalizeMsaa(sampleCount);
            PlayerSettingsPrefs.SetInt(MsaaKey, MsaaSampleCount);
            SaveAndNotify();
            ApplyGraphicsPreset();
        }

        public static void SetTextureMipmapLimit(int limit)
        {
            EnsureLoaded();
            TextureMipmapLimit = Mathf.Clamp(limit, 0, 2);
            PlayerSettingsPrefs.SetInt(TextureMipmapLimitKey, TextureMipmapLimit);
            SaveAndNotify();
            ApplyGraphicsPreset();
        }

        public static int TextureQualitySliderIndex => 2 - TextureMipmapLimit;

        public static void SetTextureQualitySliderIndex(int index)
        {
            SetTextureMipmapLimit(2 - Mathf.Clamp(index, 0, 2));
        }

        public static string GetTextureQualityLabelFromSliderIndex(int index)
        {
            return GetTextureQualityLabel(2 - Mathf.Clamp(index, 0, 2));
        }

        public static void SetAllowBotMatchmaking(bool enabled)
        {
            EnsureLoaded();
            AllowBotMatchmaking = enabled;
            PlayerSettingsPrefs.SetInt(AllowBotMatchmakingKey, enabled ? 1 : 0);
            SaveAndNotify();
        }

        public static void SetShowMatchControlHints(bool enabled)
        {
            EnsureLoaded();
            ShowMatchControlHints = enabled;
            PlayerSettingsPrefs.SetInt(ShowMatchControlHintsKey, enabled ? 1 : 0);
            SaveAndNotify();
        }

        public static bool CanToggleBotMatchmaking()
        {
            return PlayerProfileService.IsServerSynced;
        }

        public static int TargetFpsSliderIndex
        {
            get
            {
                for (var i = 0; i < TargetFpsOptions.Length; i++)
                {
                    if (TargetFpsOptions[i] == TargetFps)
                    {
                        return i;
                    }
                }

                return 1;
            }
        }

        public static void SetTargetFpsSliderIndex(int index)
        {
            EnsureLoaded();
            var clamped = Mathf.Clamp(index, 0, TargetFpsOptions.Length - 1);
            TargetFps = TargetFpsOptions[clamped];
            PlayerSettingsPrefs.SetInt(TargetFpsKey, TargetFps);
            SaveAndNotify();
            ApplyTargetFrameRate();
        }

        public static string GetTargetFpsLabelFromSliderIndex(int index)
        {
            var clamped = Mathf.Clamp(index, 0, TargetFpsOptions.Length - 1);
            return TargetFpsOptions[clamped].ToString();
        }

        public static void ApplyTargetFrameRate()
        {
            EnsureLoaded();
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = TargetFps;
        }

        public static void ApplyGraphicsPreset()
        {
            EnsureLoaded();
            var controller = UnityEngine.Object.FindFirstObjectByType<PerformancePresetController>();
            if (controller == null)
            {
                return;
            }

            controller.ApplyClientGraphicsSettings(
                RenderScale,
                ShadowsEnabled,
                PostProcessingEnabled,
                MsaaSampleCount,
                TextureMipmapLimit);
        }

        public static void ApplyMasterVolume()
        {
            EnsureLoaded();
            AudioListener.volume = MasterVolume;
        }

        public static bool IsEffectivelyMuted()
        {
            EnsureLoaded();
            return MasterVolume <= 0.001f;
        }

        public static int MsaaIndexFromSampleCount(int sampleCount)
        {
            switch (NormalizeMsaa(sampleCount))
            {
                case 1:
                    return 0;
                case 2:
                    return 1;
                case 4:
                    return 2;
                default:
                    return 3;
            }
        }

        public static int MsaaSampleCountFromIndex(int index)
        {
            switch (Mathf.Clamp(index, 0, 3))
            {
                case 0:
                    return 1;
                case 1:
                    return 2;
                case 2:
                    return 4;
                default:
                    return 8;
            }
        }

        public static string GetMsaaLabel(int sampleCount)
        {
            switch (NormalizeMsaa(sampleCount))
            {
                case 1:
                    return "Выкл";
                case 2:
                    return "2x";
                case 4:
                    return "4x";
                default:
                    return "8x";
            }
        }

        public static string GetTextureQualityLabel(int mipmapLimit)
        {
            switch (Mathf.Clamp(mipmapLimit, 0, 2))
            {
                case 0:
                    return "Высокое";
                case 1:
                    return "Среднее";
                default:
                    return "Низкое";
            }
        }

        private static void ApplyLowQualityBundle(bool save)
        {
            RenderScale = 0.65f;
            ShadowsEnabled = false;
            PostProcessingEnabled = false;
            MsaaSampleCount = 1;
            TextureMipmapLimit = 2;

            if (!save)
            {
                return;
            }

            PlayerSettingsPrefs.SetFloat(RenderScaleKey, RenderScale);
            PlayerSettingsPrefs.SetInt(ShadowsEnabledKey, 0);
            PlayerSettingsPrefs.SetInt(PostProcessingKey, 0);
            PlayerSettingsPrefs.SetInt(MsaaKey, MsaaSampleCount);
            PlayerSettingsPrefs.SetInt(TextureMipmapLimitKey, TextureMipmapLimit);
        }

        private static void ApplyHighQualityBundle(bool save)
        {
            RenderScale = DefaultRenderScale;
            ShadowsEnabled = true;
            PostProcessingEnabled = true;
            MsaaSampleCount = 2;
            TextureMipmapLimit = 2;

            if (!save)
            {
                return;
            }

            PlayerSettingsPrefs.SetFloat(RenderScaleKey, RenderScale);
            PlayerSettingsPrefs.SetInt(ShadowsEnabledKey, 1);
            PlayerSettingsPrefs.SetInt(PostProcessingKey, 1);
            PlayerSettingsPrefs.SetInt(MsaaKey, MsaaSampleCount);
            PlayerSettingsPrefs.SetInt(TextureMipmapLimitKey, TextureMipmapLimit);
        }

        private static int NormalizeTargetFps(int fps)
        {
            for (var i = 0; i < TargetFpsOptions.Length; i++)
            {
                if (TargetFpsOptions[i] == fps)
                {
                    return fps;
                }
            }

            return DefaultTargetFps;
        }

        private static int NormalizeMsaa(int sampleCount)
        {
            if (sampleCount <= 1)
            {
                return 1;
            }

            if (sampleCount <= 2)
            {
                return 2;
            }

            if (sampleCount <= 4)
            {
                return 4;
            }

            return 8;
        }

        private static string BuildAdsSensitivityKey(WeaponKind kind)
        {
            return AdsSensitivityKeyPrefix + kind.ToString().ToLowerInvariant();
        }

        private static void SaveAndNotify()
        {
            PlayerPrefs.Save();
            PlayerProgressSyncService.QueueSettingsSave();
            SettingsChanged?.Invoke();
        }
    }
}
