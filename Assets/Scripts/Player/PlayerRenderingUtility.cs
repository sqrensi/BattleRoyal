using UnityEngine;
using UnityEngine.Rendering;

namespace ShooterPrototype.Player
{
    public static class PlayerRenderingUtility
    {
        public static void ApplyPlayerShadowSettings(GameObject playerRoot, bool shadowsEnabled)
        {
            if (playerRoot == null)
            {
                return;
            }

            var renderers = playerRoot.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                ApplyRendererShadowSettings(renderers[i], shadowsEnabled);
            }
        }

        public static void RefreshAllPlayerShadowsInScene()
        {
            ClientSettingsService.EnsureLoaded();
            var shadowsEnabled = ClientSettingsService.ShadowsEnabled;

            var localMarkers = Object.FindObjectsByType<LocalPlayerMarker>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (var i = 0; i < localMarkers.Length; i++)
            {
                var marker = localMarkers[i];
                if (marker != null)
                {
                    ApplyPlayerShadowSettings(marker.gameObject, shadowsEnabled);
                }
            }

            var remoteBootstraps = Object.FindObjectsByType<RemoteThirdPersonPlayerBootstrap>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (var i = 0; i < remoteBootstraps.Length; i++)
            {
                var bootstrap = remoteBootstraps[i];
                if (bootstrap != null)
                {
                    ApplyPlayerShadowSettings(bootstrap.gameObject, shadowsEnabled);
                }
            }

            var bots = Object.FindObjectsByType<TrainingBotController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (var i = 0; i < bots.Length; i++)
            {
                var bot = bots[i];
                if (bot != null)
                {
                    ApplyPlayerShadowSettings(bot.gameObject, shadowsEnabled);
                }
            }
        }

        public static void DisablePlayerShadows(GameObject playerRoot)
        {
            ApplyPlayerShadowSettings(playerRoot, shadowsEnabled: false);
        }

        private static void ApplyRendererShadowSettings(Renderer renderer, bool shadowsEnabled)
        {
            if (renderer == null)
            {
                return;
            }

            if (renderer is LineRenderer)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                return;
            }

            if (renderer.gameObject.name.EndsWith("_FirstPersonArms", System.StringComparison.Ordinal))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                return;
            }

            if (renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
            {
                renderer.shadowCastingMode = shadowsEnabled
                    ? ShadowCastingMode.ShadowsOnly
                    : ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                return;
            }

            renderer.shadowCastingMode = shadowsEnabled ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = shadowsEnabled;
        }
    }
}
