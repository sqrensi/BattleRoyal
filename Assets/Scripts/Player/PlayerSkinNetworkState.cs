using System;

namespace ShooterPrototype.Player
{
    public readonly struct PlayerSkinNetworkState : IEquatable<PlayerSkinNetworkState>
    {
        public const int SlotCount = 6;
        private const string NoneSentinel = "__none__";

        public string ShirtId { get; }
        public string PantsId { get; }
        public string BootsId { get; }
        public string GlovesId { get; }
        public string FaceId { get; }
        public string HairId { get; }

        public PlayerSkinNetworkState(
            string shirtId,
            string pantsId,
            string bootsId,
            string glovesId,
            string faceId,
            string hairId)
        {
            ShirtId = NormalizeClothingId(shirtId);
            PantsId = NormalizeClothingId(pantsId);
            BootsId = NormalizeClothingId(bootsId);
            GlovesId = NormalizeClothingId(glovesId);
            FaceId = NormalizeAttachmentId(faceId);
            HairId = NormalizeAttachmentId(hairId);
        }

        public string GetSlotId(int index)
        {
            switch (index)
            {
                case 0:
                    return ShirtId;
                case 1:
                    return PantsId;
                case 2:
                    return BootsId;
                case 3:
                    return GlovesId;
                case 4:
                    return FaceId;
                case 5:
                    return HairId;
                default:
                    return string.Empty;
            }
        }

        public static PlayerSkinNetworkState FromSlotIds(string[] ids)
        {
            return new PlayerSkinNetworkState(
                GetSlot(ids, 0),
                GetSlot(ids, 1),
                GetSlot(ids, 2),
                GetSlot(ids, 3),
                GetSlot(ids, 4),
                GetSlot(ids, 5));
        }

        public bool Equals(PlayerSkinNetworkState other)
        {
            return string.Equals(ShirtId, other.ShirtId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(PantsId, other.PantsId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(BootsId, other.BootsId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(GlovesId, other.GlovesId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(FaceId, other.FaceId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(HairId, other.HairId, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            return obj is PlayerSkinNetworkState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(ShirtId ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(PantsId ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(BootsId ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(GlovesId ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(FaceId ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(HairId ?? string.Empty);
                return hash;
            }
        }

        private static string GetSlot(string[] ids, int index)
        {
            return ids != null && index >= 0 && index < ids.Length ? ids[index] : string.Empty;
        }

        private static string NormalizeClothingId(string id)
        {
            return string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim();
        }

        private static string NormalizeAttachmentId(string id)
        {
            var normalized = NormalizeClothingId(id);
            return string.Equals(normalized, NoneSentinel, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : normalized;
        }
    }
}
