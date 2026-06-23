using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Builds world pickup visuals from source prefabs by keeping render meshes only.
    /// Pickups always keep the prefab default look; player inventory skins are applied only after pickup.
    /// </summary>
    internal static class PickupWorldVisualBuilder
    {
        public static GameObject InstantiateWorldVisual(
            PickupKind kind,
            GameObject sourcePrefab,
            Transform parent)
        {
            if (sourcePrefab == null || parent == null)
            {
                return null;
            }

            if (kind == PickupKind.Weapon &&
                (!WeaponCatalog.IsAlive(sourcePrefab) || !WeaponCatalog.IsFullWeaponPrefab(sourcePrefab)))
            {
                var weaponKind = WeaponCatalog.ResolveKindFromPrefab(sourcePrefab);
                var catalogPrefab = WeaponCatalog.GetWeaponPrefab(weaponKind);
                if (catalogPrefab != null)
                {
                    sourcePrefab = catalogPrefab;
                }
            }

            var instance = Object.Instantiate(sourcePrefab, parent);
            instance.SetActive(true);

            if (kind == PickupKind.Weapon)
            {
                StripToMeshOnly(instance);
            }

            return instance;
        }

        public static void StripToMeshOnly(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var components = root.GetComponentsInChildren<Component>(true);
            for (var i = components.Length - 1; i >= 0; i--)
            {
                var component = components[i];
                if (component == null || component is Transform)
                {
                    continue;
                }

                if (IsMeshPresentationComponent(component))
                {
                    continue;
                }

                Object.Destroy(component);
            }
        }

        private static bool IsMeshPresentationComponent(Component component)
        {
            return component is MeshFilter ||
                   component is MeshRenderer ||
                   component is SkinnedMeshRenderer;
        }
    }
}
