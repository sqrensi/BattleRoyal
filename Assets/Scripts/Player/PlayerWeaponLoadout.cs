using System;
using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Authoritative local mirror of the two weapon inventory slots (synced from server on pickup/drop).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerWeaponLoadout : MonoBehaviour
    {
        public const byte EmptySlotKind = 255;
        public const int NoActiveSlot = 255;
        public const int MaxSlots = 2;

        [Serializable]
        public struct Slot
        {
            public string ItemId;
            public WeaponKind Kind;

            public bool Occupied => !string.IsNullOrWhiteSpace(ItemId);
        }

        private Slot slot0;
        private Slot slot1;
        private int activeSlotIndex = NoActiveSlot;
        private bool bothHolstered;
        private int slot0MagAmmo = -1;
        private int slot1MagAmmo = -1;

        public bool HasAnyWeapon => slot0.Occupied || slot1.Occupied;
        public int OccupiedCount => (slot0.Occupied ? 1 : 0) + (slot1.Occupied ? 1 : 0);
        public bool IsBothHolstered => bothHolstered;
        public int ActiveSlotIndex => activeSlotIndex;

        public bool IsSlotOccupied(int slotIndex) => GetSlot(slotIndex).Occupied;

        public int GetSlotMagAmmo(int slotIndex)
        {
            if (slotIndex == 0)
            {
                return slot0MagAmmo;
            }

            if (slotIndex == 1)
            {
                return slot1MagAmmo;
            }

            return -1;
        }

        public void SetSlotMagAmmo(int slotIndex, int ammo)
        {
            var clamped = ammo < 0 ? -1 : ammo;
            if (slotIndex == 0)
            {
                slot0MagAmmo = clamped;
                return;
            }

            if (slotIndex == 1)
            {
                slot1MagAmmo = clamped;
            }
        }

        public void ClearSlotMagAmmo(int slotIndex)
        {
            SetSlotMagAmmo(slotIndex, -1);
        }

        public Slot GetSlot(int slotIndex) => slotIndex == 0 ? slot0 : slot1;

        public WeaponKind GetActiveWeaponKind()
        {
            if (activeSlotIndex == 0 && slot0.Occupied)
            {
                return slot0.Kind;
            }

            if (activeSlotIndex == 1 && slot1.Occupied)
            {
                return slot1.Kind;
            }

            if (slot0.Occupied)
            {
                return slot0.Kind;
            }

            if (slot1.Occupied)
            {
                return slot1.Kind;
            }

            return WeaponKind.AssaultRifle;
        }

        /// <summary>
        /// Mirrors a weapon already equipped on the mount into slot 0 until server loadout arrives.
        /// </summary>
        public bool TrySeedFromMountedWeapon(WeaponKind kind, string itemId, bool holstered)
        {
            if (HasAnyWeapon)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(itemId))
            {
                itemId = WeaponCatalog.GetDefaultItemId(kind);
            }

            SetSlot(0, itemId, kind);
            activeSlotIndex = 0;
            bothHolstered = holstered;
            return true;
        }

        public GameObject ResolvePrefabForSlot(int slotIndex)
        {
            var slot = GetSlot(slotIndex);
            return slot.Occupied ? WeaponCatalog.GetWeaponPrefab(slot.Kind) : null;
        }

        public int FindFirstEmptySlot()
        {
            if (!slot0.Occupied)
            {
                return 0;
            }

            if (!slot1.Occupied)
            {
                return 1;
            }

            return -1;
        }

        public void SetBothHolstered(bool holstered)
        {
            bothHolstered = holstered;
            if (holstered)
            {
                return;
            }

            if (activeSlotIndex == NoActiveSlot)
            {
                if (slot0.Occupied)
                {
                    activeSlotIndex = 0;
                }
                else if (slot1.Occupied)
                {
                    activeSlotIndex = 1;
                }
            }
        }

        public void SetActiveSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex > 1 || !IsSlotOccupied(slotIndex))
            {
                return;
            }

            activeSlotIndex = slotIndex;
            bothHolstered = false;
        }

        public bool TryAddWeapon(string itemId, WeaponKind kind, out int assignedSlot)
        {
            assignedSlot = FindFirstEmptySlot();
            if (assignedSlot < 0)
            {
                return false;
            }

            SetSlot(assignedSlot, itemId, kind);
            activeSlotIndex = assignedSlot;
            bothHolstered = false;
            return true;
        }

        public bool TryRemoveSlot(int slotIndex, out Slot removed)
        {
            removed = default;
            if (slotIndex < 0 || slotIndex > 1)
            {
                return false;
            }

            removed = GetSlot(slotIndex);
            if (!removed.Occupied)
            {
                return false;
            }

            SetSlot(slotIndex, string.Empty, WeaponKind.AssaultRifle);
            ClearSlotMagAmmo(slotIndex);
            if (activeSlotIndex == slotIndex)
            {
                activeSlotIndex = NoActiveSlot;
                bothHolstered = true;
            }

            if (!HasAnyWeapon)
            {
                activeSlotIndex = NoActiveSlot;
                bothHolstered = true;
            }
            else if (activeSlotIndex == NoActiveSlot && !bothHolstered)
            {
                activeSlotIndex = slot0.Occupied ? 0 : 1;
            }

            return true;
        }

        public void ApplyServerLoadout(
            byte slot0Kind,
            byte slot1Kind,
            string slot0ItemId,
            string slot1ItemId,
            int activeSlot,
            bool holsteredBoth,
            int activeMagAmmo = -1)
        {
            slot0 = BuildSlot(slot0Kind, slot0ItemId);
            slot1 = BuildSlot(slot1Kind, slot1ItemId);
            if (!slot0.Occupied)
            {
                ClearSlotMagAmmo(0);
            }

            if (!slot1.Occupied)
            {
                ClearSlotMagAmmo(1);
            }

            activeSlotIndex = activeSlot;
            bothHolstered = holsteredBoth;

            if (!HasAnyWeapon)
            {
                activeSlotIndex = NoActiveSlot;
                bothHolstered = true;
                slot0MagAmmo = -1;
                slot1MagAmmo = -1;
                return;
            }

            if (activeSlotIndex != 0 && activeSlotIndex != 1)
            {
                activeSlotIndex = slot0.Occupied ? 0 : 1;
            }

            if (!IsSlotOccupied(activeSlotIndex))
            {
                activeSlotIndex = slot0.Occupied ? 0 : 1;
            }

            if (activeMagAmmo >= 0 && activeSlotIndex >= 0 && activeSlotIndex <= 1)
            {
                SetSlotMagAmmo(activeSlotIndex, activeMagAmmo);
            }
        }

        public byte EncodeSlotKind(int slotIndex)
        {
            var slot = GetSlot(slotIndex);
            if (!slot.Occupied)
            {
                return EmptySlotKind;
            }

            return (byte)Mathf.Clamp((int)slot.Kind, 0, 1);
        }

        private static Slot BuildSlot(byte kind, string itemId)
        {
            if (kind == EmptySlotKind)
            {
                return default;
            }

            var resolvedKind = (WeaponKind)Mathf.Clamp((int)kind, 0, 1);
            return new Slot
            {
                ItemId = string.IsNullOrWhiteSpace(itemId)
                    ? WeaponCatalog.GetDefaultItemId(resolvedKind)
                    : itemId.Trim(),
                Kind = resolvedKind
            };
        }

        private void SetSlot(int slotIndex, string itemId, WeaponKind kind)
        {
            var slot = new Slot
            {
                ItemId = string.IsNullOrWhiteSpace(itemId) ? string.Empty : itemId.Trim(),
                Kind = kind
            };

            if (slotIndex == 0)
            {
                slot0 = slot;
            }
            else
            {
                slot1 = slot;
            }
        }
    }
}
