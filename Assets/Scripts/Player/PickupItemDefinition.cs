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

        public PickupKind Kind => kind;
        public GameObject VisualPrefab => visualPrefab;
        public int Amount => Mathf.Max(1, amount);

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

        public bool IsValid => visualPrefab != null && !string.IsNullOrWhiteSpace(ResolvedItemId);

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
                amount = Mathf.Max(1, pickupAmount)
            };
        }

        public PickupItemDefinition WithKind(PickupKind pickupKind)
        {
            return new PickupItemDefinition
            {
                kind = pickupKind,
                visualPrefab = visualPrefab,
                itemId = itemId,
                amount = amount
            };
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
