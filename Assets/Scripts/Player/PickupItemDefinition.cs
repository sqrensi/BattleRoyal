using System;
using UnityEngine;

namespace ShooterPrototype.Player
{
    [Serializable]
    public struct PickupItemDefinition
    {
        public PickupKind kind;
        public GameObject visualPrefab;
        public string itemId;
        public int amount;
        public int magAmmo;

        public PickupKind Kind => kind;
        public GameObject VisualPrefab => visualPrefab;
        public int Amount => Mathf.Max(1, amount);
        public int MagAmmo => magAmmo;

        public string ResolvedItemId
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(itemId))
                {
                    return itemId.Trim();
                }

                return visualPrefab != null ? visualPrefab.name : string.Empty;
            }
        }

        public bool IsValid
        {
            get
            {
                if (kind == PickupKind.Weapon)
                {
                    return !string.IsNullOrWhiteSpace(ResolvedItemId) &&
                           (visualPrefab != null || ResolveEquipPrefab() != null);
                }

                return visualPrefab != null && !string.IsNullOrWhiteSpace(ResolvedItemId);
            }
        }

        /// <summary>
        /// Source prefab for world spawn (weapons are stripped to mesh at spawn time).
        /// Equip always uses <see cref="WeaponCatalog"/>.
        /// </summary>
        public GameObject ResolveWorldSourcePrefab()
        {
            if (kind == PickupKind.Weapon)
            {
                return visualPrefab != null ? visualPrefab : ResolveEquipPrefab();
            }

            return visualPrefab;
        }

        public GameObject ResolveEquipPrefab()
        {
            if (kind == PickupKind.Weapon)
            {
                return WeaponCatalog.ResolveEquipPrefab(ResolvedItemId);
            }

            return null;
        }

        public static PickupItemDefinition Create(
            PickupKind pickupKind,
            GameObject prefab,
            string explicitItemId = "",
            int pickupAmount = 1)
        {
            return new PickupItemDefinition
            {
                kind = pickupKind,
                visualPrefab = prefab,
                itemId = explicitItemId ?? string.Empty,
                amount = Mathf.Max(1, pickupAmount),
                magAmmo = -1
            };
        }

        public PickupItemDefinition WithMagAmmo(int ammo)
        {
            return new PickupItemDefinition
            {
                kind = kind,
                visualPrefab = visualPrefab,
                itemId = itemId,
                amount = amount,
                magAmmo = ammo
            };
        }

        public PickupItemDefinition WithKind(PickupKind pickupKind)
        {
            return new PickupItemDefinition
            {
                kind = pickupKind,
                visualPrefab = visualPrefab,
                itemId = itemId,
                amount = amount,
                magAmmo = magAmmo
            };
        }

        public PickupItemDefinition WithWorldVisual(GameObject worldVisual)
        {
            return new PickupItemDefinition
            {
                kind = kind,
                visualPrefab = worldVisual,
                itemId = itemId,
                amount = amount,
                magAmmo = magAmmo
            };
        }

        public void RegisterWeaponSourcePrefab(GameObject sourcePrefab, string itemId = null)
        {
            var kind = !string.IsNullOrWhiteSpace(itemId)
                ? WeaponCatalog.ResolveKindFromItemId(itemId)
                : WeaponCatalog.ResolveKindFromPrefab(sourcePrefab, itemId);
            WeaponCatalog.EnsureWeaponPrefabRegistered(kind);

            if (sourcePrefab == null || !WeaponCatalog.IsFullWeaponPrefab(sourcePrefab))
            {
                return;
            }

            WeaponCatalog.RegisterWeaponPrefab(kind, sourcePrefab);
        }
    }

    public readonly struct PickupConfirmedInfo
    {
        public PickupConfirmedInfo(string spawnId, PickupKind kind, string itemId, int amount)
        {
            SpawnId = spawnId ?? string.Empty;
            Kind = kind;
            ItemId = itemId ?? string.Empty;
            Amount = Mathf.Max(1, amount);
        }

        public string SpawnId { get; }
        public PickupKind Kind { get; }
        public string ItemId { get; }
        public int Amount { get; }

        public PickupItemDefinition ToDefinition(GameObject visualPrefab)
        {
            return PickupItemDefinition.Create(Kind, visualPrefab, ItemId, Amount);
        }
    }
}
