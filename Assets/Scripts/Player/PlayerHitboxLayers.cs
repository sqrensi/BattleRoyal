using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class PlayerHitboxLayers
    {
        public const string HitboxLayerName = "PlayerHitbox";
        public const string BodyLayerName = "PlayerBody";

        public static int HitboxLayerIndex => LayerMask.NameToLayer(HitboxLayerName);
        public static int BodyLayerIndex => LayerMask.NameToLayer(BodyLayerName);

        public static int HitboxMask
        {
            get
            {
                var layer = HitboxLayerIndex;
                return layer >= 0 ? 1 << layer : 0;
            }
        }

        public static int BodyMask
        {
            get
            {
                var layer = BodyLayerIndex;
                return layer >= 0 ? 1 << layer : 0;
            }
        }

        public static bool IsConfigured => HitboxLayerIndex >= 0 && BodyLayerIndex >= 0;

        public static int ResolveWeaponRaycastMask(LayerMask configuredMask)
        {
            var mask = configuredMask.value;
            var bodyMask = BodyMask;
            if (bodyMask != 0)
            {
                mask &= ~bodyMask;
            }

            return mask;
        }

        public static void ApplyHitboxLayer(GameObject hitboxObject)
        {
            if (hitboxObject == null)
            {
                return;
            }

            var layer = HitboxLayerIndex;
            if (layer >= 0)
            {
                hitboxObject.layer = layer;
            }
        }

        public static void ApplyBodyLayerToPlayerRoot(GameObject playerRoot)
        {
            if (playerRoot == null)
            {
                return;
            }

            var layer = BodyLayerIndex;
            if (layer >= 0)
            {
                playerRoot.layer = layer;
            }
        }
    }
}
