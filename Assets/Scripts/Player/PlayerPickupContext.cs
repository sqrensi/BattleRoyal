using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Runtime services needed to apply a world pickup on the local player.
    /// </summary>
    internal readonly struct PlayerPickupContext
    {
        public PlayerPickupContext(
            PlayerWeaponMount weaponMount,
            PlayerWeaponController weaponController,
            PlayerWeaponHolsterController weaponHolster,
            PlayerWeaponLoadoutController weaponLoadout,
            PlayerHealth health,
            PlayerInventory inventory,
            PlayerMedkitController medkit)
        {
            WeaponMount = weaponMount;
            WeaponController = weaponController;
            WeaponHolster = weaponHolster;
            WeaponLoadout = weaponLoadout;
            Health = health;
            Inventory = inventory;
            Medkit = medkit;
        }

        public PlayerWeaponMount WeaponMount { get; }
        public PlayerWeaponController WeaponController { get; }
        public PlayerWeaponHolsterController WeaponHolster { get; }
        public PlayerWeaponLoadoutController WeaponLoadout { get; }
        public PlayerHealth Health { get; }
        public PlayerInventory Inventory { get; }
        public PlayerMedkitController Medkit { get; }

        public static PlayerPickupContext FromPlayer(GameObject playerRoot)
        {
            if (playerRoot == null)
            {
                return default;
            }

            return new PlayerPickupContext(
                playerRoot.GetComponent<PlayerWeaponMount>(),
                playerRoot.GetComponent<PlayerWeaponController>(),
                playerRoot.GetComponent<PlayerWeaponHolsterController>(),
                playerRoot.GetComponent<PlayerWeaponLoadoutController>(),
                playerRoot.GetComponent<PlayerHealth>(),
                playerRoot.GetComponent<PlayerInventory>(),
                playerRoot.GetComponent<PlayerMedkitController>());
        }
    }
}
