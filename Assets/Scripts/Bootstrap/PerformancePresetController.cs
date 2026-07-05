using ShooterPrototype.Player;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using QualityShadowResolution = UnityEngine.ShadowResolution;
using QualityShadowsMode = UnityEngine.ShadowQuality;

namespace ShooterPrototype.Bootstrap
{
    [DefaultExecutionOrder(-100)]
    public sealed class PerformancePresetController : MonoBehaviour
    {
        private const string GameSceneName = "Game";
        private const int ShadowsDisabledMaximumLodLevel = 1;
        private const float ShadowsDisabledLodBias = 0.5f;

        [Header("Graphics defaults")]
        [SerializeField] private QualityShadowResolution visualQualityShadowResolution = QualityShadowResolution.VeryHigh;
        [SerializeField] private float visualQualityShadowDistance = 120f;
        [SerializeField] private int visualQualityShadowCascades = 4;
        [SerializeField] private int visualQualityPixelLightCount = 4;
        [SerializeField] private float visualQualityLodBias = 2f;
        [SerializeField] private int visualQualityMainLightShadowResolution = 2048;
        [SerializeField] private int visualQualityShadowCascadeCount = 4;
        [SerializeField] private bool visualQualityRequireDepthTexture = true;
        [SerializeField] private bool visualQualityRequireOpaqueTexture = true;
        [SerializeField] private bool visualQualitySupportsHdr = true;

        private Volume[] cachedVolumes;
        private float[] cachedVolumeWeights;
        private bool hasCachedVolumeWeights;
        private bool hasCapturedProjectUrp;
        private float renderScaleOverride = ClientSettingsService.DefaultRenderScale;
        private UrpPresetState projectUrpState;
        private QualityShadowsMode projectShadowsMode;
        private int projectMaximumLodLevel;

        private struct UrpPresetState
        {
            public float RenderScale;
            public int MsaaSampleCount;
            public bool SupportsHdr;
            public bool RequireDepthTexture;
            public bool RequireOpaqueTexture;
            public float ShadowDistance;
            public int MainLightShadowResolution;
            public int ShadowCascadeCount;
        }

        private void Awake()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            Application.targetFrameRate = ClientSettingsService.TargetFps;
            QualitySettings.vSyncCount = 0;
            projectShadowsMode = QualitySettings.shadows;
            projectMaximumLodLevel = QualitySettings.maximumLODLevel;
            CaptureProjectUrpIfNeeded();

            ClientSettingsService.EnsureLoaded();
            renderScaleOverride = ClientSettingsService.RenderScale;

            CacheVolumeWeightsIfNeeded();
            ApplyClientGraphicsSettings(
                ClientSettingsService.RenderScale,
                ClientSettingsService.ShadowsEnabled,
                ClientSettingsService.PostProcessingEnabled,
                ClientSettingsService.MsaaSampleCount,
                ClientSettingsService.TextureMipmapLimit);
        }

        public void ApplyClientGraphicsSettings(
            float renderScale,
            bool shadowsEnabled,
            bool postProcessingEnabled,
            int msaaSampleCount,
            int textureMipmapLimit)
        {
            if (Application.isBatchMode)
            {
                return;
            }

            renderScaleOverride = Mathf.Clamp(renderScale, 0.65f, 1f);

            QualitySettings.globalTextureMipmapLimit = Mathf.Clamp(textureMipmapLimit, 0, 2);
            QualitySettings.anisotropicFiltering = textureMipmapLimit > 0
                ? AnisotropicFiltering.Enable
                : AnisotropicFiltering.ForceEnable;
            QualitySettings.antiAliasing = msaaSampleCount > 1 ? msaaSampleCount : 0;
            QualitySettings.shadows = shadowsEnabled ? projectShadowsMode : QualityShadowsMode.Disable;
            QualitySettings.shadowDistance = shadowsEnabled ? visualQualityShadowDistance : 0f;
            QualitySettings.shadowResolution = visualQualityShadowResolution;
            QualitySettings.shadowCascades = shadowsEnabled ? visualQualityShadowCascades : 1;
            QualitySettings.pixelLightCount = shadowsEnabled ? visualQualityPixelLightCount : 0;
            QualitySettings.lodBias = shadowsEnabled ? visualQualityLodBias : ShadowsDisabledLodBias;
            QualitySettings.maximumLODLevel = shadowsEnabled
                ? projectMaximumLodLevel
                : ShadowsDisabledMaximumLodLevel;
            QualitySettings.realtimeReflectionProbes = postProcessingEnabled;
            QualitySettings.softParticles = postProcessingEnabled;
            QualitySettings.skinWeights = SkinWeights.TwoBones;
            QualitySettings.vSyncCount = 0;

            ApplyUrpSettings(new UrpPresetState
            {
                RenderScale = renderScaleOverride,
                MsaaSampleCount = Mathf.Max(1, msaaSampleCount),
                SupportsHdr = postProcessingEnabled && visualQualitySupportsHdr,
                RequireDepthTexture = postProcessingEnabled && visualQualityRequireDepthTexture,
                RequireOpaqueTexture = postProcessingEnabled && visualQualityRequireOpaqueTexture,
                ShadowDistance = shadowsEnabled
                    ? (hasCapturedProjectUrp ? projectUrpState.ShadowDistance : visualQualityShadowDistance)
                    : 0f,
                MainLightShadowResolution = shadowsEnabled
                    ? visualQualityMainLightShadowResolution
                    : 256,
                ShadowCascadeCount = shadowsEnabled
                    ? visualQualityShadowCascadeCount
                    : 1
            });

            ApplyVolumeAndCameraSettings(postProcessingEnabled, shadowsEnabled);
            PlayerRenderingUtility.RefreshAllPlayerShadowsInScene();
        }

        public void SetRenderScaleOverride(float value)
        {
            renderScaleOverride = Mathf.Clamp(value, 0.65f, 1f);
            ClientSettingsService.EnsureLoaded();
            ApplyClientGraphicsSettings(
                renderScaleOverride,
                ClientSettingsService.ShadowsEnabled,
                ClientSettingsService.PostProcessingEnabled,
                ClientSettingsService.MsaaSampleCount,
                ClientSettingsService.TextureMipmapLimit);
        }

        private static bool ShouldUseDirectLocalPlayQuality()
        {
            if (Application.isBatchMode)
            {
                return false;
            }

            if (Object.FindAnyObjectByType<GameBootstrap>() != null)
            {
                return false;
            }

            var scene = SceneManager.GetActiveScene();
            return scene.IsValid() && scene.isLoaded && scene.name == GameSceneName;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureDirectGameSceneQualityPreset()
        {
            if (!ShouldUseDirectLocalPlayQuality())
            {
                return;
            }

            if (Object.FindAnyObjectByType<PerformancePresetController>() != null)
            {
                return;
            }

            var host = new GameObject("LocalPlayPerformancePreset");
            host.AddComponent<PerformancePresetController>();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void OnValidate()
        {
            if (!Application.isPlaying || Application.isBatchMode)
            {
                return;
            }

            ClientSettingsService.EnsureLoaded();
            ApplyClientGraphicsSettings(
                ClientSettingsService.RenderScale,
                ClientSettingsService.ShadowsEnabled,
                ClientSettingsService.PostProcessingEnabled,
                ClientSettingsService.MsaaSampleCount,
                ClientSettingsService.TextureMipmapLimit);
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (Application.isBatchMode)
            {
                return;
            }

            hasCachedVolumeWeights = false;
            CacheVolumeWeightsIfNeeded();

            ClientSettingsService.EnsureLoaded();
            ApplyClientGraphicsSettings(
                ClientSettingsService.RenderScale,
                ClientSettingsService.ShadowsEnabled,
                ClientSettingsService.PostProcessingEnabled,
                ClientSettingsService.MsaaSampleCount,
                ClientSettingsService.TextureMipmapLimit);
        }

        private void CaptureProjectUrpIfNeeded()
        {
            if (hasCapturedProjectUrp)
            {
                return;
            }

            var urp = GetActiveUrpAsset();
            if (urp == null)
            {
                return;
            }

            projectUrpState = ReadUrpState(urp);
            hasCapturedProjectUrp = true;
        }

        private static UrpPresetState ReadUrpState(UniversalRenderPipelineAsset urp)
        {
            return new UrpPresetState
            {
                RenderScale = urp.renderScale,
                MsaaSampleCount = urp.msaaSampleCount,
                SupportsHdr = urp.supportsHDR,
                RequireDepthTexture = urp.supportsCameraDepthTexture,
                RequireOpaqueTexture = urp.supportsCameraOpaqueTexture,
                ShadowDistance = urp.shadowDistance,
                MainLightShadowResolution = urp.mainLightShadowmapResolution,
                ShadowCascadeCount = urp.shadowCascadeCount
            };
        }

        private static void ApplyUrpSettings(UrpPresetState state)
        {
            var urp = GetActiveUrpAsset();
            if (urp == null)
            {
                return;
            }

            urp.renderScale = Mathf.Clamp(state.RenderScale, 0.5f, 1f);
            urp.msaaSampleCount = Mathf.Clamp(state.MsaaSampleCount, 1, 8);
            urp.supportsHDR = state.SupportsHdr;
            urp.supportsCameraDepthTexture = state.RequireDepthTexture;
            urp.supportsCameraOpaqueTexture = state.RequireOpaqueTexture;
            urp.shadowDistance = Mathf.Max(0f, state.ShadowDistance);
            urp.mainLightShadowmapResolution = Mathf.Clamp(state.MainLightShadowResolution, 256, 4096);
            urp.shadowCascadeCount = Mathf.Clamp(state.ShadowCascadeCount, 1, 4);
        }

        private void ApplyVolumeAndCameraSettings(bool postProcessingEnabled, bool shadowsEnabled)
        {
            if (!postProcessingEnabled)
            {
                CacheVolumeWeightsIfNeeded();
                if (cachedVolumes != null)
                {
                    for (var i = 0; i < cachedVolumes.Length; i++)
                    {
                        var volume = cachedVolumes[i];
                        if (volume != null)
                        {
                            volume.weight = 0f;
                        }
                    }
                }
            }
            else
            {
                RestoreVolumeWeights();
            }

            ApplyCameraRenderSettings(postProcessingEnabled, shadowsEnabled);
        }

        private static void ApplyCameraRenderSettings(bool postProcessingEnabled, bool shadowsEnabled)
        {
            var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < cameras.Length; i++)
            {
                var camera = cameras[i];
                if (camera == null || !camera.enabled)
                {
                    continue;
                }

                var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
                if (cameraData == null)
                {
                    continue;
                }

                cameraData.renderPostProcessing = postProcessingEnabled;
                cameraData.renderShadows = shadowsEnabled;
                cameraData.antialiasing = postProcessingEnabled
                    ? AntialiasingMode.FastApproximateAntialiasing
                    : AntialiasingMode.None;
                cameraData.antialiasingQuality = postProcessingEnabled
                    ? AntialiasingQuality.High
                    : AntialiasingQuality.Low;
            }
        }

        private void CacheVolumeWeightsIfNeeded()
        {
            if (hasCachedVolumeWeights)
            {
                return;
            }

            cachedVolumes = FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            cachedVolumeWeights = new float[cachedVolumes.Length];
            for (var i = 0; i < cachedVolumes.Length; i++)
            {
                cachedVolumeWeights[i] = cachedVolumes[i] != null ? cachedVolumes[i].weight : 1f;
            }

            hasCachedVolumeWeights = cachedVolumes.Length > 0;
        }

        private void RestoreVolumeWeights()
        {
            if (cachedVolumes == null || cachedVolumeWeights == null)
            {
                return;
            }

            for (var i = 0; i < cachedVolumes.Length; i++)
            {
                var volume = cachedVolumes[i];
                if (volume != null)
                {
                    volume.weight = cachedVolumeWeights[i];
                }
            }
        }

        private static UniversalRenderPipelineAsset GetActiveUrpAsset()
        {
            if (QualitySettings.renderPipeline is UniversalRenderPipelineAsset qualityPipeline)
            {
                return qualityPipeline;
            }

            return GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        }
    }
}
