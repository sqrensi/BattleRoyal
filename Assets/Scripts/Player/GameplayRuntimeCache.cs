using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Cached local-player references to avoid per-frame FindObject calls in hot paths.
    /// </summary>
    public static class GameplayRuntimeCache
    {
        private const int RefreshIntervalFrames = 45;

        private static LocalPlayerMarker localPlayer;
        private static Camera localPlayerCamera;
        private static PlayerAudioController localPlayerAudio;
        private static int lastRefreshFrame = -1000;

        public static void RegisterLocalPlayer(LocalPlayerMarker marker)
        {
            localPlayer = marker;
            localPlayerCamera = null;
            localPlayerAudio = null;
            lastRefreshFrame = -1000;
        }

        public static void ClearLocalPlayer()
        {
            localPlayer = null;
            localPlayerCamera = null;
            localPlayerAudio = null;
            lastRefreshFrame = -1000;
        }

        public static LocalPlayerMarker LocalPlayer
        {
            get
            {
                RefreshIfStale();
                return localPlayer;
            }
        }

        public static Camera LocalPlayerCamera
        {
            get
            {
                RefreshIfStale();
                return localPlayerCamera;
            }
        }

        public static PlayerAudioController LocalPlayerAudio
        {
            get
            {
                RefreshIfStale();
                return localPlayerAudio;
            }
        }

        private static void RefreshIfStale()
        {
            if (Time.frameCount - lastRefreshFrame < RefreshIntervalFrames &&
                localPlayer != null)
            {
                return;
            }

            lastRefreshFrame = Time.frameCount;

            if (localPlayer == null)
            {
                localPlayer = Object.FindFirstObjectByType<LocalPlayerMarker>();
            }

            if (localPlayer == null)
            {
                localPlayerCamera = Camera.main;
                localPlayerAudio = null;
                return;
            }

            if (localPlayerCamera == null || !localPlayerCamera.isActiveAndEnabled)
            {
                localPlayerCamera = localPlayer.GetComponentInChildren<Camera>(true);
            }

            if (localPlayerCamera == null || !localPlayerCamera.enabled)
            {
                localPlayerCamera = Camera.main;
            }

            if (localPlayerAudio == null)
            {
                localPlayerAudio = localPlayer.GetComponent<PlayerAudioController>();
            }
        }
    }
}
