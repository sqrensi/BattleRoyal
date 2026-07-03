using System;
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
        private const string TargetFpsKey = "client_settings_target_fps";
        private const string LegacyMuteKey = "client_audio_muted";
        private const string AdsSensitivityKeyPrefix = "client_settings_ads_sensitivity_";

        public const float DefaultMasterVolume = 1f;
        public const float DefaultMusicVolume = 0.2f;
        public const float DefaultRainVolume = 0.42f;
        public const float DefaultSfxVolume = 0.75f;
        public const float DefaultMouseSensitivity = 2.2f;
        public const float DefaultRenderScale = 1f;
        public const float DefaultGlobalAdsSensitivity = 1f;
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

            if (UserScopedPlayerPrefs.HasKey(LegacyMuteKey) && !UserScopedPlayerPrefs.HasKey(MasterVolumeKey))
            {
                MasterVolume = UserScopedPlayerPrefs.GetInt(LegacyMuteKey, 0) == 1 ? 0f : DefaultMasterVolume;
            }
            else
            {
                MasterVolume = UserScopedPlayerPrefs.GetFloat(MasterVolumeKey, DefaultMasterVolume);
            }

            MusicVolume = UserScopedPlayerPrefs.GetFloat(MusicVolumeKey, DefaultMusicVolume);
            RainVolume = UserScopedPlayerPrefs.GetFloat(RainVolumeKey, DefaultRainVolume);
            SfxVolume = UserScopedPlayerPrefs.GetFloat(SfxVolumeKey, DefaultSfxVolume);
            MouseSensitivity = UserScopedPlayerPrefs.GetFloat(MouseSensitivityKey, DefaultMouseSensitivity);
            RenderScale = UserScopedPlayerPrefs.GetFloat(RenderScaleKey, DefaultRenderScale);
            GlobalAdsSensitivityMultiplier = UserScopedPlayerPrefs.GetFloat(
                GlobalAdsSensitivityKey,
                DefaultGlobalAdsSensitivity);
            AllowBotMatchmaking = UserScopedPlayerPrefs.GetInt(AllowBotMatchmakingKey, 1) == 1;
            TargetFps = NormalizeTargetFps(UserScopedPlayerPrefs.GetInt(TargetFpsKey, DefaultTargetFps));

            if (UserScopedPlayerPrefs.HasKey(ShadowsEnabledKey))
            {
                ShadowsEnabled = UserScopedPlayerPrefs.GetInt(ShadowsEnabledKey, 1) == 1;
                PostProcessingEnabled = UserScopedPlayerPrefs.GetInt(PostProcessingKey, 1) == 1;
                MsaaSampleCount = NormalizeMsaa(UserScopedPlayerPrefs.GetInt(MsaaKey, 4));
                TextureMipmapLimit = Mathf.Clamp(UserScopedPlayerPrefs.GetInt(TextureMipmapLimitKey, 0), 0, 2);
            }
            else if (UserScopedPlayerPrefs.GetInt(LegacyMaxPerformanceKey, 0) == 1)
            {
                ApplyLowQualityBundle(save: true);
            }
            else
            {
                ApplyHighQualityBundle(save: false);
            }

            UserScopedPlayerPrefs.DeleteKey(LegacyMaxPerformanceKey);

            MasterVolume = Mathf.Clamp01(MasterVolume);
            MusicVolume = Mathf.Clamp01(MusicVolume);
            RainVolume = Mathf.Clamp01(RainVolume);
            SfxVolume = Mathf.Clamp01(SfxVolume);
            MouseSensitivity = Mathf.Clamp(MouseSensitivity, 0.1f, 10f);
            RenderScale = Mathf.Clamp(RenderScale, 0.65f, 1f);
            GlobalAdsSensitivityMultiplier = Mathf.Clamp(GlobalAdsSensitivityMultiplier, 0.1f, 3f);

            loaded = true;
        }

        public static float GetDefaultAdsSensitivity(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.AssaultRifle:
                    return 1.85f;
                case WeaponKind.SniperRifle:
                    return 2.6f;
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
            var perWeapon = UserScopedPlayerPrefs.GetFloat(
                BuildAdsSensitivityKey(kind),
                GetDefaultAdsSensitivity(kind));
            return perWeapon * GlobalAdsSensitivityMultiplier;
        }

        public static float GetWeaponAdsSensitivity(WeaponKind kind)
        {
            EnsureLoaded();
            return UserScopedPlayerPrefs.GetFloat(BuildAdsSensitivityKey(kind), GetDefaultAdsSensitivity(kind));
        }

        public static void SetMasterVolume(float value)
        {
            EnsureLoaded();
            MasterVolume = Mathf.Clamp01(value);
            UserScopedPlayerPrefs.SetFloat(MasterVolumeKey, MasterVolume);
            UserScopedPlayerPrefs.SetInt(LegacyMuteKey, MasterVolume <= 0.001f ? 1 : 0);
            SaveAndNotify();
            ApplyMasterVolume();
        }

        public static void SetMusicVolume(float value)
        {
            EnsureLoaded();
            MusicVolume = Mathf.Clamp01(value);
            UserScopedPlayerPrefs.SetFloat(MusicVolumeKey, MusicVolume);
            SaveAndNotify();
        }

        public static void SetRainVolume(float value)
        {
            EnsureLoaded();
            RainVolume = Mathf.Clamp01(value);
            UserScopedPlayerPrefs.SetFloat(RainVolumeKey, RainVolume);
            SaveAndNotify();
        }

        public static void SetSfxVolume(float value)
        {
            EnsureLoaded();
            SfxVolume = Mathf.Clamp01(value);
            UserScopedPlayerPrefs.SetFloat(SfxVolumeKey, SfxVolume);
            SaveAndNotify();
        }

        public static void SetMouseSensitivity(float value)
        {
            EnsureLoaded();
            MouseSensitivity = Mathf.Clamp(value, 0.1f, 10f);
            UserScopedPlayerPrefs.SetFloat(MouseSensitivityKey, MouseSensitivity);
            SaveAndNotify();
        }

        public static void SetGlobalAdsSensitivity(float value)
        {
            EnsureLoaded();
            GlobalAdsSensitivityMultiplier = Mathf.Clamp(value, 0.1f, 3f);
            UserScopedPlayerPrefs.SetFloat(GlobalAdsSensitivityKey, GlobalAdsSensitivityMultiplier);
            SaveAndNotify();
        }

        public static void SetAdsSensitivity(WeaponKind kind, float value)
        {
            EnsureLoaded();
            var clamped = Mathf.Clamp(value, 0.1f, 5f);
            UserScopedPlayerPrefs.SetFloat(BuildAdsSensitivityKey(kind), clamped);
            SaveAndNotify();
        }

        public static void SetRenderScale(float value)
        {
            EnsureLoaded();
            RenderScale = Mathf.Clamp(value, 0.65f, 1f);
            UserScopedPlayerPrefs.SetFloat(RenderScaleKey, RenderScale);
            SaveAndNotify();
            ApplyGraphicsPreset();
        }

        public static void SetShadowsEnabled(bool enabled)
        {
            EnsureLoaded();
            ShadowsEnabled = enabled;
            UserScopedPlayerPrefs.SetInt(ShadowsEnabledKey, enabled ? 1 : 0);
            SaveAndNotify();
            ApplyGraphicsPreset();
        }

        public static void SetPostProcessingEnabled(bool enabled)
        {
            EnsureLoaded();
            PostProcessingEnabled = enabled;
            UserScopedPlayerPrefs.SetInt(PostProcessingKey, enabled ? 1 : 0);
            SaveAndNotify();
            ApplyGraphicsPreset();
        }

        public static void SetMsaaSampleCount(int sampleCount)
        {
            EnsureLoaded();
            MsaaSampleCount = NormalizeMsaa(sampleCount);
            UserScopedPlayerPrefs.SetInt(MsaaKey, MsaaSampleCount);
            SaveAndNotify();
            ApplyGraphicsPreset();
        }

        public static void SetTextureMipmapLimit(int limit)
        {
            EnsureLoaded();
            TextureMipmapLimit = Mathf.Clamp(limit, 0, 2);
            UserScopedPlayerPrefs.SetInt(TextureMipmapLimitKey, TextureMipmapLimit);
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
            UserScopedPlayerPrefs.SetInt(AllowBotMatchmakingKey, enabled ? 1 : 0);
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
            UserScopedPlayerPrefs.SetInt(TargetFpsKey, TargetFps);
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

            UserScopedPlayerPrefs.SetFloat(RenderScaleKey, RenderScale);
            UserScopedPlayerPrefs.SetInt(ShadowsEnabledKey, 0);
            UserScopedPlayerPrefs.SetInt(PostProcessingKey, 0);
            UserScopedPlayerPrefs.SetInt(MsaaKey, MsaaSampleCount);
            UserScopedPlayerPrefs.SetInt(TextureMipmapLimitKey, TextureMipmapLimit);
        }

        private static void ApplyHighQualityBundle(bool save)
        {
            RenderScale = DefaultRenderScale;
            ShadowsEnabled = true;
            PostProcessingEnabled = true;
            MsaaSampleCount = 4;
            TextureMipmapLimit = 0;

            if (!save)
            {
                return;
            }

            UserScopedPlayerPrefs.SetFloat(RenderScaleKey, RenderScale);
            UserScopedPlayerPrefs.SetInt(ShadowsEnabledKey, 1);
            UserScopedPlayerPrefs.SetInt(PostProcessingKey, 1);
            UserScopedPlayerPrefs.SetInt(MsaaKey, MsaaSampleCount);
            UserScopedPlayerPrefs.SetInt(TextureMipmapLimitKey, TextureMipmapLimit);
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
            SettingsChanged?.Invoke();
        }
    }
}
