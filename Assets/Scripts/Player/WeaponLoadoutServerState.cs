namespace ShooterPrototype.Player
{
    /// <summary>
    /// Authoritative weapon inventory values from pickup_result / weapon_drop_result.
    /// </summary>
    public readonly struct WeaponLoadoutServerState
    {
        public WeaponLoadoutServerState(
            bool hasWeaponLoadout,
            byte slot0Kind,
            byte slot1Kind,
            string slot0ItemId,
            string slot1ItemId,
            int activeWeaponSlot,
            bool bothHolstered,
            string droppedSpawnId,
            int activeMagAmmo = -1,
            int activeReserveAmmo = -1)
        {
            HasWeaponLoadout = hasWeaponLoadout;
            Slot0Kind = slot0Kind;
            Slot1Kind = slot1Kind;
            Slot0ItemId = slot0ItemId ?? string.Empty;
            Slot1ItemId = slot1ItemId ?? string.Empty;
            ActiveWeaponSlot = activeWeaponSlot;
            BothHolstered = bothHolstered;
            DroppedSpawnId = droppedSpawnId ?? string.Empty;
            ActiveMagAmmo = activeMagAmmo;
            ActiveReserveAmmo = activeReserveAmmo;
        }

        public bool HasWeaponLoadout { get; }
        public byte Slot0Kind { get; }
        public byte Slot1Kind { get; }
        public string Slot0ItemId { get; }
        public string Slot1ItemId { get; }
        public int ActiveWeaponSlot { get; }
        public bool BothHolstered { get; }
        public string DroppedSpawnId { get; }
        public int ActiveMagAmmo { get; }

        public int ActiveReserveAmmo { get; }

        public static WeaponLoadoutServerState None =>
            new WeaponLoadoutServerState(false, PlayerWeaponLoadout.EmptySlotKind, PlayerWeaponLoadout.EmptySlotKind,
                string.Empty, string.Empty, PlayerWeaponLoadout.NoActiveSlot, true, string.Empty);
    }
}
