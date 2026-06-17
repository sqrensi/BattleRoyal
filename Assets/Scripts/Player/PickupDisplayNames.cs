namespace ShooterPrototype.Player
{
    public static class PickupDisplayNames
    {
        public static string Resolve(in PickupItemDefinition definition)
        {
            switch (definition.Kind)
            {
                case PickupKind.Medkit:
                    return "аптечку";
                case PickupKind.Grenade:
                    return "гранату";
                case PickupKind.Ammo:
                    return ResolveAmmoName(definition);
                case PickupKind.Weapon:
                default:
                    return ResolveWeaponName(definition);
            }
        }

        private static string ResolveAmmoName(in PickupItemDefinition definition)
        {
            if (AmmoCatalog.TryResolveKindFromItemId(definition.ResolvedItemId, out var ammoKind))
            {
                switch (ammoKind)
                {
                    case WeaponKind.SniperRifle:
                        return "патроны снайперки";
                    case WeaponKind.Pistol:
                        return "патроны пистолета";
                    case WeaponKind.Mp7:
                        return "патроны MP7";
                    default:
                        return "патроны автомата";
                }
            }

            return "патроны";
        }

        private static string ResolveWeaponName(in PickupItemDefinition definition)
        {
            var kind = WeaponCatalog.ResolveKindFromItemId(definition.ResolvedItemId);
            switch (kind)
            {
                case WeaponKind.SniperRifle:
                    return "снайперскую винтовку";
                case WeaponKind.Pistol:
                    return "пистолет";
                case WeaponKind.Mp7:
                    return "MP7";
                default:
                    return "автомат";
            }
        }
    }
}
