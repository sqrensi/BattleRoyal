using UnityEngine;

namespace ShooterPrototype.Player
{
    [CreateAssetMenu(
        fileName = "WeaponPrefabRegistry",
        menuName = "Shooter Prototype/Weapon Prefab Registry",
        order = 20)]
    public sealed class WeaponPrefabRegistry : ScriptableObject
    {
        [SerializeField] private GameObject assaultRiflePrefab;
        [SerializeField] private GameObject sniperRiflePrefab;
        [SerializeField] private GameObject pistolPrefab;
        [SerializeField] private GameObject mp7Prefab;

        public GameObject GetPrefab(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.SniperRifle:
                    return sniperRiflePrefab;
                case WeaponKind.Pistol:
                    return pistolPrefab;
                case WeaponKind.Mp7:
                    return mp7Prefab;
                default:
                    return assaultRiflePrefab;
            }
        }

        public void RegisterAll()
        {
            RegisterIfValid(WeaponKind.AssaultRifle, assaultRiflePrefab);
            RegisterIfValid(WeaponKind.SniperRifle, sniperRiflePrefab);
            RegisterIfValid(WeaponKind.Pistol, pistolPrefab);
            RegisterIfValid(WeaponKind.Mp7, mp7Prefab);
        }

        private static void RegisterIfValid(WeaponKind kind, GameObject prefab)
        {
            if (prefab != null)
            {
                WeaponCatalog.RegisterWeaponPrefab(kind, prefab);
            }
        }
    }
}
