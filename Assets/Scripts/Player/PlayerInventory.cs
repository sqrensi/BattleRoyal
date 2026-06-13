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

            var loadout = GetComponent<PlayerWeaponLoadout>();
            var sb = new StringBuilder(192);
            sb.AppendLine("Inventory:");
            AppendWeaponsSection(sb, loadout, weaponMount, holster);
            AppendSpareAmmoSection(sb, loadout);
            AppendItemsSection(sb, buffer);

            return sb.ToString().TrimEnd();
        }

        private static void AppendWeaponsSection(
            StringBuilder sb,
            PlayerWeaponLoadout loadout,
            PlayerWeaponMount weaponMount,
            PlayerWeaponHolsterController holster)
        {
            sb.AppendLine("Weapons:");
            if (loadout == null || !loadout.HasAnyWeapon)
            {
                sb.AppendLine("  none");
                return;
            }

            var bothHolstered = loadout.IsBothHolstered ||
                                holster != null && (holster.IsHolstered || holster.IsMedkitWeaponLocked);
            var activeSlot = loadout.ActiveSlotIndex;
            var hasOccupiedSlot = false;

            for (var slotIndex = 0; slotIndex < PlayerWeaponLoadout.MaxSlots; slotIndex++)
            {
                var slot = loadout.GetSlot(slotIndex);
                if (!slot.Occupied)
                {
                    continue;
                }

                hasOccupiedSlot = true;
                sb.Append("  ");
                sb.Append(slotIndex + 1);
                sb.Append(". ");
                sb.Append(FormatWeaponItemLabel(slot.ItemId, slot.Kind));

                if (!bothHolstered && activeSlot == slotIndex)
                {
                    sb.Append(" [active]");
                }
                else if (bothHolstered)
                {
                    sb.Append(" (holstered)");
                }

                if (weaponMount != null && weaponMount.HasMountedWeapon &&
                    !bothHolstered && activeSlot == slotIndex)
                {
                    var mountedRoot = weaponMount.MountedWeaponRoot;
                    if (mountedRoot != null &&
                        !string.Equals(mountedRoot.name, slot.ItemId, System.StringComparison.OrdinalIgnoreCase))
                    {
                        sb.Append(" (");
                        sb.Append(mountedRoot.name);
                        sb.Append(')');
                    }
                }

                sb.AppendLine();
            }

            if (!hasOccupiedSlot)
            {
                sb.AppendLine("  none");
            }
        }

        private static void AppendSpareAmmoSection(StringBuilder sb, PlayerWeaponLoadout loadout)
        {
            sb.AppendLine("Ammo:");
            if (loadout == null)
            {
                sb.AppendLine("  --");
                return;
            }

            var hasAmmo = false;
            for (var kindValue = 0; kindValue <= WeaponKindUtility.MaxKindId; kindValue++)
            {
                var kind = (WeaponKind)kindValue;
                var spare = loadout.GetSpareAmmo(kind);
                if (spare <= 0)
                {
                    continue;
                }

                hasAmmo = true;
                sb.Append("  ");
                sb.Append(FormatWeaponKindLabel(kind));
                sb.Append(": ");
                sb.Append(spare);
                sb.AppendLine();
            }

            if (!hasAmmo)
            {
                sb.AppendLine("  none");
            }
        }

        private static void AppendItemsSection(StringBuilder sb, List<InventoryEntry> entries)
        {
            sb.Append("Items:");
            var hasItems = false;
            for (var i = 0; i < entries.Count; i++)
            {
                if (IsAmmoInventoryEntry(entries[i].ItemId))
                {
                    continue;
                }

                if (!hasItems)
                {
                    sb.AppendLine();
                    hasItems = true;
                }

                sb.Append("  ");
                sb.Append(FormatItemLabel(entries[i].ItemId));
                sb.Append(" x");
                sb.Append(entries[i].Count);
                sb.AppendLine();
            }

            if (!hasItems)
            {
                sb.AppendLine();
                sb.AppendLine("  none");
            }
        }

        private static bool IsAmmoInventoryEntry(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return false;
            }

            if (itemId == InventoryItemIds.Ammo)
            {
                return true;
            }

            return AmmoCatalog.TryResolveKindFromItemId(itemId, out _);
        }

        private static string FormatWeaponKindLabel(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.SniperRifle:
                    return "Sniper";
                case WeaponKind.Pistol:
                    return "Pistol";
                case WeaponKind.Mp7:
                    return "MP7";
                default:
                    return "Assault";
            }
        }

        private static string FormatWeaponItemLabel(string itemId, WeaponKind kind)
        {
            if (!string.IsNullOrWhiteSpace(itemId))
            {
                var trimmed = itemId.Trim();
                if (trimmed.EndsWith("_001", System.StringComparison.OrdinalIgnoreCase))
                {
                    trimmed = trimmed.Substring(0, trimmed.Length - 4);
                }

                trimmed = trimmed.Replace('_', ' ');
                if (trimmed.Length > 0)
                {
                    return char.ToUpper(trimmed[0]) + trimmed.Substring(1);
                }
            }

            return FormatWeaponKindLabel(kind);
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
