using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Applies world pickups by <see cref="PickupKind"/>. Add new kinds here for future items.
    /// </summary>
    internal static class PlayerPickupApplier
    {
        public static bool CanPickup(in PlayerPickupContext context, in PickupItemDefinition definition)
        {
            if (!definition.IsValid)
            {
                return false;
            }

            if (context.Health != null && context.Health.IsDead)
            {
                return false;
            }

            switch (definition.Kind)
            {
                case PickupKind.Weapon:
                    return context.WeaponLoadout != null
                        || (context.WeaponMount != null && !context.WeaponMount.HasMountedWeapon);
                case PickupKind.Ammo:
                    return CanPickupAmmo(context);
                case PickupKind.Grenade:
                    return context.Health != null;
                case PickupKind.Medkit:
                    return context.Inventory != null &&
                           context.Inventory.CanAdd(InventoryItemIds.Medkit, definition.Amount);
                default:
                    return false;
            }
        }

        public static PickupApplyResult TryApply(
            in PlayerPickupContext context,
            in PickupItemDefinition definition,
            in PickupApplyServerState serverState)
        {
            if (!CanPickup(context, definition))
            {
                return new PickupApplyResult(false, "cannot_pickup");
            }

            switch (definition.Kind)
            {
                case PickupKind.Weapon:
                    return TryApplyWeapon(context, definition, serverState);
                case PickupKind.Ammo:
                    return TryApplyAmmo(context, definition, serverState);
                case PickupKind.Grenade:
                    return TryApplyGrenade(context, definition);
                case PickupKind.Medkit:
                    return TryApplyMedkit(context, definition, serverState);
                default:
                    return new PickupApplyResult(false, "unknown_kind");
            }
        }

        private static PickupApplyResult TryApplyWeapon(
            in PlayerPickupContext context,
            in PickupItemDefinition definition,
            in PickupApplyServerState serverState)
        {
            if (serverState.WeaponLoadout.HasWeaponLoadout && context.WeaponLoadout != null)
            {
                context.WeaponLoadout.ApplyServerPickup(serverState.WeaponLoadout);
                return PickupApplyResult.Ok;
            }

            if (context.WeaponLoadout != null)
            {
                var kind = WeaponCatalog.ResolveKindFromItemId(definition.ResolvedItemId);
                var pickupMagAmmo = definition.MagAmmo >= 0 ? definition.MagAmmo : 0;
                if (!context.WeaponLoadout.TryApplyLocalPickup(
                        definition.ResolvedItemId,
                        kind,
                        definition.VisualPrefab,
                        pickupMagAmmo))
                {
                    return new PickupApplyResult(false, "equip_failed");
                }

                return PickupApplyResult.Ok;
            }

            if (context.WeaponMount == null ||
                !context.WeaponMount.EquipWeapon(definition.VisualPrefab))
            {
                return new PickupApplyResult(false, "equip_failed");
            }

            if (context.WeaponController != null)
            {
                context.WeaponController.enabled = true;
                var pickupAmmo = definition.MagAmmo >= 0 ? definition.MagAmmo : 0;
                context.WeaponController.SetCurrentAmmo(pickupAmmo);
                context.WeaponController.SetReserveAmmo(0);
            }
            else
            {
                context.WeaponHolster?.ForceArmedState();
            }

            return PickupApplyResult.Ok;
        }

        private static bool CanPickupAmmo(in PlayerPickupContext context)
        {
            if (context.WeaponLoadout == null || !context.WeaponLoadout.Loadout.HasAnyWeapon)
            {
                return context.WeaponMount != null && context.WeaponMount.HasMountedWeapon;
            }

            if (context.WeaponController == null)
            {
                return true;
            }

            return true;
        }

        private static PickupApplyResult TryApplyAmmo(
            in PlayerPickupContext context,
            in PickupItemDefinition definition,
            in PickupApplyServerState serverState)
        {
            if (serverState.WeaponLoadout.HasWeaponLoadout && context.WeaponLoadout != null)
            {
                context.WeaponLoadout.ApplyServerPickup(serverState.WeaponLoadout);
                return PickupApplyResult.Ok;
            }

            if (context.WeaponController == null)
            {
                return new PickupApplyResult(false, "no_weapon");
            }

            var addedAmmo = Mathf.Max(1, definition.Amount);
            if (context.WeaponLoadout != null)
            {
                context.WeaponLoadout.Loadout.AddSpareAmmo(addedAmmo);
                context.WeaponController.SetReserveAmmo(context.WeaponLoadout.Loadout.SpareAmmo);
            }
            else
            {
                context.WeaponController.SetReserveAmmo(context.WeaponController.ReserveAmmo + addedAmmo);
            }

            return PickupApplyResult.Ok;
        }

        private static PickupApplyResult TryApplyGrenade(
            in PlayerPickupContext context,
            in PickupItemDefinition definition)
        {
            if (context.Inventory == null)
            {
                return new PickupApplyResult(false, "no_inventory");
            }

            var itemId = string.IsNullOrWhiteSpace(definition.ResolvedItemId)
                ? InventoryItemIds.Grenade
                : definition.ResolvedItemId;
            return context.Inventory.TryAdd(itemId, definition.Amount)
                ? PickupApplyResult.Ok
                : new PickupApplyResult(false, "inventory_full");
        }

        private static PickupApplyResult TryApplyMedkit(
            in PlayerPickupContext context,
            in PickupItemDefinition definition,
            in PickupApplyServerState serverState)
        {
            if (context.Inventory == null)
            {
                return new PickupApplyResult(false, "no_inventory");
            }

            if (serverState.HasMedkitCount)
            {
                context.Inventory.SetCount(InventoryItemIds.Medkit, serverState.MedkitCount);
                return PickupApplyResult.Ok;
            }

            return context.Inventory.TryAdd(InventoryItemIds.Medkit, definition.Amount)
                ? PickupApplyResult.Ok
                : new PickupApplyResult(false, "medkit_full");
        }
    }
}
