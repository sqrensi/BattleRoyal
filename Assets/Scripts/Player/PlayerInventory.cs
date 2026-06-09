using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ShooterPrototype.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerInventory : MonoBehaviour
    {
        [SerializeField] private int startingMedkitCount = 0;

        private readonly Dictionary<string, int> stacks = new Dictionary<string, int>();

        public int GetCount(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return 0;
            }

            return stacks.TryGetValue(itemId, out var count) ? Mathf.Max(0, count) : 0;
        }

        public void SetCount(string itemId, int count)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return;
            }

            var clamped = Mathf.Max(0, count);
            if (clamped <= 0)
            {
                stacks.Remove(itemId);
                return;
            }

            stacks[itemId] = clamped;
        }

        public bool CanAdd(string itemId, int amount)
        {
            if (amount <= 0 || string.IsNullOrWhiteSpace(itemId))
            {
                return false;
            }

            var max = ResolveMaxCount(itemId);
            if (max <= 0)
            {
                return false;
            }

            return GetCount(itemId) + amount <= max;
        }

        public bool TryAdd(string itemId, int amount)
        {
            if (!CanAdd(itemId, amount))
            {
                return false;
            }

            stacks[itemId] = GetCount(itemId) + amount;
            return true;
        }

        public bool TryRemove(string itemId, int amount)
        {
            if (amount <= 0 || string.IsNullOrWhiteSpace(itemId))
            {
                return false;
            }

            var current = GetCount(itemId);
            if (current < amount)
            {
                return false;
            }

            var next = current - amount;
            if (next <= 0)
            {
                stacks.Remove(itemId);
            }
            else
            {
                stacks[itemId] = next;
            }

            return true;
        }

        public void CollectEntries(List<InventoryEntry> buffer)
        {
            buffer.Clear();
            foreach (var pair in stacks)
            {
                if (pair.Value <= 0)
                {
                    continue;
                }

                buffer.Add(new InventoryEntry(pair.Key, pair.Value));
            }

            buffer.Sort((a, b) => string.CompareOrdinal(a.ItemId, b.ItemId));
        }

        public string BuildHudSummary(PlayerWeaponMount weaponMount, PlayerWeaponHolsterController holster)
        {
            var buffer = new List<InventoryEntry>(8);
            CollectEntries(buffer);

            var sb = new StringBuilder(96);
            sb.AppendLine("Inventory:");
            var loadoutController = GetComponent<PlayerWeaponLoadoutController>();
            AppendWeaponLine(sb, weaponMount, holster, loadoutController != null ? loadoutController.Loadout : null);

            if (buffer.Count == 0)
            {
                sb.Append("  (no items)");
                return sb.ToString();
            }

            for (var i = 0; i < buffer.Count; i++)
            {
                sb.AppendLine();
                sb.Append("  ");
                sb.Append(FormatItemLabel(buffer[i].ItemId));
                sb.Append(" x");
                sb.Append(buffer[i].Count);
            }

            return sb.ToString();
        }

        private static void AppendWeaponLine(
            StringBuilder sb,
            PlayerWeaponMount weaponMount,
            PlayerWeaponHolsterController holster,
            PlayerWeaponLoadout loadout)
        {
            if (weaponMount == null || !weaponMount.HasMountedWeapon)
            {
                if (loadout != null && loadout.HasAnyWeapon)
                {
                    var slot = loadout.GetSlot(loadout.ActiveSlotIndex);
                    if (!slot.Occupied)
                    {
                        slot = loadout.IsSlotOccupied(0) ? loadout.GetSlot(0) : loadout.GetSlot(1);
                    }

                    sb.Append("  Weapon: ");
                    sb.Append(string.IsNullOrWhiteSpace(slot.ItemId)
                        ? WeaponCatalog.GetDefaultItemId(slot.Kind)
                        : slot.ItemId);
                    if (loadout.IsBothHolstered)
                    {
                        sb.Append(" (holstered)");
                    }

                    return;
                }

                sb.Append("  Weapon: none");
                return;
            }

            var weaponRoot = weaponMount.MountedWeaponRoot;
            var label = weaponRoot != null ? weaponRoot.name : "Weapon";
            if (holster != null && (holster.IsHolstered || holster.IsMedkitWeaponLocked))
            {
                sb.Append("  Weapon: ");
                sb.Append(label);
                sb.Append(" (holstered)");
                return;
            }

            sb.Append("  Weapon: ");
            sb.Append(label);
        }

        private void Awake()
        {
            if (GetComponent<RemoteThirdPersonPlayerBootstrap>() != null)
            {
                enabled = false;
                return;
            }

            if (GetCount(InventoryItemIds.Medkit) <= 0 && startingMedkitCount > 0)
            {
                SetCount(InventoryItemIds.Medkit, startingMedkitCount);
            }
        }

        private static int ResolveMaxCount(string itemId)
        {
            if (itemId == InventoryItemIds.Medkit)
            {
                return PickupApplyRules.MaxMedkitInventory;
            }

            if (itemId == InventoryItemIds.Grenade)
            {
                return 8;
            }

            return 99;
        }

        private static string FormatItemLabel(string itemId)
        {
            if (itemId == InventoryItemIds.Medkit)
            {
                return "Medkit";
            }

            if (itemId == InventoryItemIds.Grenade)
            {
                return "Grenade";
            }

            if (itemId == InventoryItemIds.Ammo)
            {
                return "Ammo";
            }

            return itemId;
        }

        public readonly struct InventoryEntry
        {
            public InventoryEntry(string itemId, int count)
            {
                ItemId = itemId;
                Count = count;
            }

            public string ItemId { get; }
            public int Count { get; }
        }
    }
}
