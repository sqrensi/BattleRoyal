using ShooterPrototype.Player;
using UnityEngine;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuUiSoundController : MonoBehaviour
    {
        [SerializeField] private string startClipPath = "Sounds/start";
        [SerializeField] private string cancelClipPath = "Sounds/cancel";
        [SerializeField] private string buttonClipPath = "Sounds/buttons";

        private AudioSource audioSource;
        private AudioClip startClip;
        private AudioClip cancelClip;
        private AudioClip buttonClip;

        private void Awake()
        {
            EnsureSource();
            LoadClips();
            ClientSettingsService.EnsureLoaded();
        }

        private void OnEnable()
        {
            ClientSettingsService.SettingsChanged += HandleSettingsChanged;
        }

        private void OnDisable()
        {
            ClientSettingsService.SettingsChanged -= HandleSettingsChanged;
        }

        public void PlayStart()
        {
            PlayClip(startClip);
        }

        public void PlayCancel()
        {
            PlayClip(cancelClip);
        }

        public void PlayButton()
        {
            PlayClip(buttonClip);
        }

        private void HandleSettingsChanged()
        {
            // Master volume is applied globally via AudioListener.
        }

        private void EnsureSource()
        {
            if (audioSource != null)
            {
                return;
            }

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;
            audioSource.priority = 32;
        }

        private void LoadClips()
        {
            startClip = Resources.Load<AudioClip>(startClipPath);
            cancelClip = Resources.Load<AudioClip>(cancelClipPath);
            buttonClip = Resources.Load<AudioClip>(buttonClipPath);

            if (startClip == null)
            {
                Debug.LogWarning($"[MainMenuUiSoundController] Clip not found: Resources/{startClipPath}");
            }

            if (cancelClip == null)
            {
                Debug.LogWarning($"[MainMenuUiSoundController] Clip not found: Resources/{cancelClipPath}");
            }

            if (buttonClip == null)
            {
                Debug.LogWarning($"[MainMenuUiSoundController] Clip not found: Resources/{buttonClipPath}");
            }
        }

        private void PlayClip(AudioClip clip)
        {
            if (clip == null || audioSource == null || ClientSettingsService.IsEffectivelyMuted())
            {
                return;
            }

            audioSource.PlayOneShot(clip, ClientSettingsService.SfxVolume);
        }
    }
}
