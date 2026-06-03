using UnityEngine;

namespace ShooterPrototype.Player
{
    internal readonly struct PlayerPickupContext
    {
        public PlayerPickupContext(
            PlayerWeaponMount weaponMount,
            PlayerWeaponController weaponController,
            PlayerWeaponHolsterController weaponHolster,
            PlayerHealth health)
        {
            WeaponMount = weaponMount;
            WeaponController = weaponController;
            WeaponHolster = weaponHolster;
            Health = health;
        }

        public PlayerWeaponMount WeaponMount { get; }
        public PlayerWeaponController WeaponController { get; }
        public PlayerWeaponHolsterController WeaponHolster { get; }
        public PlayerHealth Health { get; }
    }

    internal static class PlayerPickupApplier
    {
        public static bool CanPickup(in PlayerPickupContext context, in PickupItemDefinition definition)
        {
            if (!definition.IsValid)
            {
                return false;
            }

            switch (definition.Kind)
            {
                case PickupKind.Weapon:
                    return context.WeaponMount != null && !context.WeaponMount.HasMountedWeapon;
                case PickupKind.Ammo:
                    return CanPickupAmmo(context, definition);
                case PickupKind.Grenade:
                    return CanPickupGrenade(context, definition);
                case PickupKind.Medkit:
                    return context.Health != null &&
                           !context.Health.IsDead &&
                           context.Health.CurrentHealth < context.Health.MaxHealth - 0.001f;
                default:
                    return false;
            }
        }

        public static bool TryApply(in PlayerPickupContext context, in PickupItemDefinition definition)
        {
            if (!CanPickup(context, definition))
            {
                return false;
            }

            switch (definition.Kind)
            {
                case PickupKind.Weapon:
                    return TryApplyWeapon(context, definition);
                case PickupKind.Ammo:
                    return TryApplyAmmo(context, definition);
                case PickupKind.Grenade:
                    return TryApplyGrenade(context, definition);
                case PickupKind.Medkit:
                    return TryApplyMedkit(context, definition);
                default:
                    return false;
            }
        }

        private static bool CanPickupAmmo(in PlayerPickupContext context, in PickupItemDefinition definition)
        {
            // Placeholder until an inventory/ammo system exists.
            return context.WeaponMount != null && context.WeaponMount.HasMountedWeapon;
        }

        private static bool CanPickupGrenade(in PlayerPickupContext context, in PickupItemDefinition definition)
        {
            // Placeholder until a grenade inventory exists.
            return context.Health != null && !context.Health.IsDead;
        }

        private static bool TryApplyWeapon(in PlayerPickupContext context, in PickupItemDefinition definition)
        {
            if (context.WeaponMount == null ||
                !context.WeaponMount.EquipWeapon(definition.VisualPrefab))
            {
                return false;
            }

            if (context.WeaponController != null)
            {
                context.WeaponController.enabled = true;
                context.WeaponController.RestoreAfterRespawn();
            }
            else
            {
                context.WeaponHolster?.ForceArmedState();
            }

            return true;
        }

        private static bool TryApplyAmmo(in PlayerPickupContext context, in PickupItemDefinition definition)
        {
            Debug.Log(
                $"[PlayerPickup] Ammo pickup reserved for future inventory: item={definition.ResolvedItemId} amount={definition.Amount}");
            return true;
        }

        private static bool TryApplyGrenade(in PlayerPickupContext context, in PickupItemDefinition definition)
        {
            Debug.Log(
                $"[PlayerPickup] Grenade pickup reserved for future inventory: item={definition.ResolvedItemId} amount={definition.Amount}");
            return true;
        }

        private static bool TryApplyMedkit(in PlayerPickupContext context, in PickupItemDefinition definition)
        {
            if (context.Health == null)
            {
                return false;
            }

            return context.Health.TryHeal(definition.Amount);
        }
    }
}
