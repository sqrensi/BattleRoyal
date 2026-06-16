using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class WaterLayers
    {
        public const string LayerName = "Water";

        private static int cachedLayerIndex = int.MinValue;

        public static int LayerIndex
        {
            get
            {
                if (cachedLayerIndex == int.MinValue)
                {
                    cachedLayerIndex = LayerMask.NameToLayer(LayerName);
                    if (cachedLayerIndex < 0)
                    {
                        cachedLayerIndex = LayerMask.NameToLayer("water");
                    }
                }

                return cachedLayerIndex;
            }
        }

        public static bool IsWaterLayer(int layer)
        {
            return LayerIndex >= 0 && layer == LayerIndex;
        }

        public static bool IsWaterCollider(Collider collider)
        {
            return collider != null &&
                   collider.isTrigger &&
                   IsWaterLayer(collider.gameObject.layer);
        }
    }
}
