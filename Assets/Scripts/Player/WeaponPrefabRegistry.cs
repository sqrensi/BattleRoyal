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

        public bool TryValidate(out string error)
        {
            for (var kindValue = 0; kindValue <= WeaponKindUtility.MaxKindId; kindValue++)
            {
                var kind = (WeaponKind)kindValue;
                var prefab = GetPrefab(kind);
                if (prefab == null)
                {
                    error = $"Missing prefab for {kind}.";
                    return false;
                }

                if (!WeaponCatalog.TryGetProfile(prefab, out var profile))
                {
                    error = $"Prefab '{prefab.name}' for {kind} has no WeaponProfile.";
                    return false;
                }

                if (profile.Kind != kind)
                {
                    error =
                        $"Prefab '{prefab.name}' profile kind is {profile.Kind}, expected {kind}.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }
    }
}
