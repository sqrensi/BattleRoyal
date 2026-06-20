using ShooterPrototype.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuAmbienceController : MonoBehaviour
    {
        private const string MainMenuSceneName = "MainMenu";

        [Header("Resources")]
        [SerializeField] private string rainResourcePath = "Sounds/rain";
        [SerializeField] private string musicResourcePath = "Sounds/music";

        private AudioSource rainSource;
        private AudioSource musicSource;

        private void Awake()
        {
            EnsureSources();
            ClientSettingsService.EnsureLoaded();
        }

        private void OnEnable()
        {
            ClientSettingsService.SettingsChanged += ApplyAudioSettings;
            RefreshAmbienceForActiveScene();
        }

        private void OnDisable()
        {
            ClientSettingsService.SettingsChanged -= ApplyAudioSettings;
            StopAmbience();
        }

        private void RefreshAmbienceForActiveScene()
        {
            if (ShouldPlayAmbience())
            {
                StartAmbience();
            }
            else
            {
                StopAmbience();
            }
        }

        private bool ShouldPlayAmbience()
        {
            if (Application.isBatchMode)
            {
                return false;
            }

            var launcher = FindFirstObjectByType<Network.NetworkLauncher>();
            if (launcher != null && launcher.IsMockServerRunning && !launcher.IsClientConnected)
            {
                return false;
            }

            var scene = SceneManager.GetActiveScene();
            return scene.IsValid() &&
                   string.Equals(scene.name, MainMenuSceneName, System.StringComparison.Ordinal);
        }

        private void EnsureSources()
        {
            if (rainSource == null)
            {
                rainSource = CreateLoopSource("RainLoop");
            }

            if (musicSource == null)
            {
                musicSource = CreateLoopSource("MusicLoop");
            }
        }

        private AudioSource CreateLoopSource(string objectName)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(transform, false);

            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.priority = objectName.Contains("Music") ? 64 : 128;
            return source;
        }

        private void StartAmbience()
        {
            EnsureSources();
            ApplyAudioSettings();
            PlayLoop(rainSource, rainResourcePath);
            PlayLoop(musicSource, musicResourcePath);
        }

        private void StopAmbience()
        {
            if (rainSource != null)
            {
                rainSource.Stop();
            }

            if (musicSource != null)
            {
                musicSource.Stop();
            }
        }

        private void ApplyAudioSettings()
        {
            ClientSettingsService.EnsureLoaded();
            ClientSettingsService.ApplyMasterVolume();

            if (rainSource != null)
            {
                rainSource.volume = ClientSettingsService.RainVolume;
            }

            if (musicSource != null)
            {
                musicSource.volume = ClientSettingsService.MusicVolume;
            }
        }

        private static void PlayLoop(AudioSource source, string resourcePath)
        {
            if (source == null || string.IsNullOrWhiteSpace(resourcePath))
            {
                return;
            }

            var clip = Resources.Load<AudioClip>(resourcePath.Trim().Trim('/'));
            if (clip == null)
            {
                Debug.LogWarning($"[MainMenuAmbienceController] Audio clip not found at Resources/{resourcePath}");
                return;
            }

            if (source.clip != clip)
            {
                source.clip = clip;
            }

            if (!source.isPlaying)
            {
                source.Play();
            }
        }
    }
}
