using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Raycast mask for world pickups: environment geometry only, not players or hitboxes.
    /// </summary>
    public static class PickupGroundLayers
    {
        private static int cachedEnvironmentMask;
        private static bool hasCachedEnvironmentMask;

        public static LayerMask EnvironmentMask
        {
            get
            {
                if (!hasCachedEnvironmentMask)
                {
                    cachedEnvironmentMask = BuildEnvironmentMask();
                    hasCachedEnvironmentMask = true;
                }

                return cachedEnvironmentMask;
            }
        }

        public static LayerMask SanitizeMask(LayerMask configuredMask)
        {
            if (configuredMask.value == 0 || configuredMask.value == -1)
            {
                return EnvironmentMask;
            }

            return configuredMask;
        }

        private static int BuildEnvironmentMask()
        {
            var mask = ~0;
            ExcludeLayer(ref mask, "PlayerBody");
            ExcludeLayer(ref mask, "PlayerHitbox");
            ExcludeLayer(ref mask, "Ignore Raycast");
            ExcludeLayer(ref mask, "UI");
            return mask;
        }

        private static void ExcludeLayer(ref int mask, string layerName)
        {
            var layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0)
            {
                mask &= ~(1 << layer);
            }
        }
    }
}
