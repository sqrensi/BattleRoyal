using ShooterPrototype.Network;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuAmbienceController : MonoBehaviour
    {
        private const string MainMenuSceneName = "MainMenu";
        private const string MutePrefKey = "client_audio_muted";

        [Header("Resources")]
        [SerializeField] private string rainResourcePath = "Sounds/rain";
        [SerializeField] private string musicResourcePath = "Sounds/music";

        [Header("Volume")]
        [SerializeField] private float rainVolume = 0.42f;
        [SerializeField] private float musicVolume = 0.2f;

        private AudioSource rainSource;
        private AudioSource musicSource;
        private bool isMuted;

        private void Awake()
        {
            EnsureSources();
            LoadMuteState();
        }

        private void OnEnable()
        {
            RefreshAmbienceForActiveScene();
        }

        private void OnDisable()
        {
            StopAmbience();
        }

        private void Update()
        {
            if (!ShouldPlayAmbience())
            {
                return;
            }

            if (ReadToggleMutePressed())
            {
                ToggleMute();
            }
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

            var launcher = FindFirstObjectByType<NetworkLauncher>();
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
                rainSource = CreateLoopSource("RainLoop", rainVolume);
            }

            if (musicSource == null)
            {
                musicSource = CreateLoopSource("MusicLoop", musicVolume);
            }
        }

        private AudioSource CreateLoopSource(string objectName, float volume)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(transform, false);

            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.volume = volume;
            source.priority = objectName.Contains("Music") ? 64 : 128;
            return source;
        }

        private void StartAmbience()
        {
            EnsureSources();
            if (rainSource != null)
            {
                rainSource.volume = rainVolume;
            }

            if (musicSource != null)
            {
                musicSource.volume = musicVolume;
            }

            PlayLoop(rainSource, rainResourcePath);
            PlayLoop(musicSource, musicResourcePath);
            ApplyMuteState();
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

        private void ToggleMute()
        {
            isMuted = !isMuted;
            PlayerPrefs.SetInt(MutePrefKey, isMuted ? 1 : 0);
            PlayerPrefs.Save();
            ApplyMuteState();
        }

        private void LoadMuteState()
        {
            isMuted = PlayerPrefs.GetInt(MutePrefKey, 0) == 1;
            ApplyMuteState();
        }

        private void ApplyMuteState()
        {
            if (!ShouldPlayAmbience())
            {
                return;
            }

            AudioListener.volume = isMuted ? 0f : 1f;
        }

        private static bool ReadToggleMutePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.F8);
#endif
        }
    }
}
