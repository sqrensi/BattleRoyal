using UnityEngine;
using UnityEngine.Rendering;

namespace ShooterPrototype.Player
{
    public static class PlayerRenderingUtility
    {
        public static void DisablePlayerShadows(GameObject playerRoot)
        {
            if (playerRoot == null)
            {
                return;
            }

            var renderers = playerRoot.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }
    }
}
