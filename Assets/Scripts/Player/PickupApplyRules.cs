namespace ShooterPrototype.Player
{
    /// <summary>
    /// Shared pickup validation limits (keep in sync with server env MEDKIT_MAX_COUNT).
    /// </summary>
    internal static class PickupApplyRules
    {
        public const int MaxMedkitInventory = 8;

        public static bool RequiresFacingCheck(PickupKind kind)
        {
            return kind == PickupKind.Weapon || kind == PickupKind.Ammo;
        }
    }
}
