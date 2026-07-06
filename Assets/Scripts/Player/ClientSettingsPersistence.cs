using System.Collections;
using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Ensures settings are written to browser storage and synced to the server before page unload.
    /// </summary>
    public sealed class ClientSettingsPersistence : MonoBehaviour
    {
        private static ClientSettingsPersistence instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (instance != null)
            {
                return;
            }

            var host = new GameObject(nameof(ClientSettingsPersistence));
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ClientSettingsPersistence>();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (!pauseStatus)
            {
                return;
            }

            PersistNow();
        }

        private void OnApplicationQuit()
        {
            PersistNow();
        }

        private void PersistNow()
        {
            ClientSettingsService.EnsureLoaded();
            PlayerPrefs.Save();
            PlayerProgressSyncService.QueueSettingsSave();

            if (!PlayerProfileService.IsServerSynced)
            {
                return;
            }

            StopAllCoroutines();
            StartCoroutine(FlushSettingsRoutine());
        }

        private IEnumerator FlushSettingsRoutine()
        {
            var playerId = PlayerIdentityService.GetOrCreatePlayerId();
            yield return PlayerProgressSyncService.FlushSettingsIfNeeded(this, null, playerId);
        }
    }
}
