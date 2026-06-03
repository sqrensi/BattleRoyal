namespace ShooterPrototype.Player
{
    public enum PickupKind
    {
        Weapon = 0,
        Ammo = 1,
        Grenade = 2,
        Medkit = 3,
    }

    public static class PickupKindUtility
    {
        public static string ToProtocol(PickupKind kind)
        {
            switch (kind)
            {
                case PickupKind.Ammo:
                    return "ammo";
                case PickupKind.Grenade:
                    return "grenade";
                case PickupKind.Medkit:
                    return "medkit";
                default:
                    return "weapon";
            }
        }

        public static PickupKind FromProtocol(string value, PickupKind fallback = PickupKind.Weapon)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "ammo":
                    return PickupKind.Ammo;
                case "grenade":
                case "grenades":
                    return PickupKind.Grenade;
                case "medkit":
                case "med":
                case "health":
                    return PickupKind.Medkit;
                case "weapon":
                    return PickupKind.Weapon;
                default:
                    return fallback;
            }
        }
    }
}
