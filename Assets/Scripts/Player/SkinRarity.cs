using UnityEngine;

namespace ShooterPrototype.Player
{
    public enum SkinRarity
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2,
        Epic = 3,
        Legendary = 4
    }

    public static class SkinRarityUtility
    {
        public static bool TryParse(string raw, out SkinRarity rarity)
        {
            rarity = SkinRarity.Common;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            switch (raw.Trim().ToLowerInvariant())
            {
                case "common":
                case "gray":
                case "grey":
                    rarity = SkinRarity.Common;
                    return true;
                case "uncommon":
                case "blue":
                    rarity = SkinRarity.Uncommon;
                    return true;
                case "rare":
                case "purple":
                    rarity = SkinRarity.Rare;
                    return true;
                case "epic":
                case "pink":
                    rarity = SkinRarity.Epic;
                    return true;
                case "legendary":
                case "red":
                    rarity = SkinRarity.Legendary;
                    return true;
                default:
                    return false;
            }
        }

        public static SkinRarity ParseOrDefault(string raw)
        {
            return TryParse(raw, out var rarity) ? rarity : SkinRarity.Common;
        }

        public static Color GetCardBackgroundColor(SkinRarity rarity)
        {
            switch (rarity)
            {
                case SkinRarity.Uncommon:
                    return new Color(0.14f, 0.28f, 0.58f, 0.96f);
                case SkinRarity.Rare:
                    return new Color(0.34f, 0.16f, 0.58f, 0.96f);
                case SkinRarity.Epic:
                    return new Color(0.62f, 0.2f, 0.52f, 0.96f);
                case SkinRarity.Legendary:
                    return new Color(0.72f, 0.16f, 0.14f, 0.96f);
                default:
                    return new Color(0.34f, 0.36f, 0.4f, 0.96f);
            }
        }
    }
}
