using System;
using System.Text;
using ShooterPrototype.Player;

namespace ShooterPrototype.Network
{
    internal static class PlayerSkinNetworkCodec
    {
        public const int MaxSkinIdLength = 48;
        public const int ClothingSlotCount = 6;
        public const int WeaponSlotCount = 4;
        public const int SlotCount = PlayerSkinNetworkState.SlotCount;

        public static int WriteClothingSlotIds(byte[] buffer, int offset, in PlayerSkinNetworkState state)
        {
            offset = WriteSlotId(buffer, offset, state.ShirtId);
            offset = WriteSlotId(buffer, offset, state.PantsId);
            offset = WriteSlotId(buffer, offset, state.BootsId);
            offset = WriteSlotId(buffer, offset, state.GlovesId);
            offset = WriteSlotId(buffer, offset, state.FaceId);
            offset = WriteSlotId(buffer, offset, state.HairId);
            return offset;
        }

        public static int WriteWeaponSlotIds(byte[] buffer, int offset, in PlayerSkinNetworkState state)
        {
            offset = WriteSlotId(buffer, offset, state.WeaponAssaultId);
            offset = WriteSlotId(buffer, offset, state.WeaponSniperId);
            offset = WriteSlotId(buffer, offset, state.WeaponPistolId);
            offset = WriteSlotId(buffer, offset, state.WeaponMp7Id);
            return offset;
        }

        public static int WriteSlotIds(byte[] buffer, int offset, in PlayerSkinNetworkState state)
        {
            offset = WriteClothingSlotIds(buffer, offset, in state);
            return WriteWeaponSlotIds(buffer, offset, in state);
        }

        public static int GetClothingEncodedSize(in PlayerSkinNetworkState state)
        {
            return GetEncodedSizeForRange(state, 0, ClothingSlotCount);
        }

        public static int GetWeaponEncodedSize(in PlayerSkinNetworkState state)
        {
            return GetEncodedSizeForRange(state, ClothingSlotCount, WeaponSlotCount);
        }

        public static int GetEncodedSize(in PlayerSkinNetworkState state)
        {
            return GetClothingEncodedSize(in state) + GetWeaponEncodedSize(in state);
        }

        public static bool TryReadClothingSlotIds(byte[] data, ref int offset, out PlayerSkinNetworkState state)
        {
            return TryReadSlotRange(data, ref offset, 0, ClothingSlotCount, out state);
        }

        public static bool TryReadWeaponSlotIds(byte[] data, ref int offset, in PlayerSkinNetworkState clothingState, out PlayerSkinNetworkState state)
        {
            state = clothingState;
            if (data == null || offset >= data.Length)
            {
                return true;
            }

            if (!TryReadSlotRange(data, ref offset, ClothingSlotCount, WeaponSlotCount, out var weaponOnly))
            {
                return false;
            }

            state = MergeClothingAndWeapon(clothingState, weaponOnly);
            return true;
        }

        public static bool TryReadSlotIds(byte[] data, ref int offset, out PlayerSkinNetworkState state)
        {
            return TryReadSlotIds(data, ref offset, SlotCount, out state);
        }

        public static bool TryReadSlotIds(
            byte[] data,
            ref int offset,
            int slotCount,
            out PlayerSkinNetworkState state)
        {
            return TryReadSlotRange(data, ref offset, 0, slotCount, out state);
        }

        private static bool TryReadSlotRange(
            byte[] data,
            ref int offset,
            int startIndex,
            int count,
            out PlayerSkinNetworkState state)
        {
            state = default;
            if (data == null || count <= 0)
            {
                return false;
            }

            var ids = new string[SlotCount];
            for (var i = 0; i < count; i++)
            {
                if (offset >= data.Length)
                {
                    return false;
                }

                var len = data[offset++];
                if (len > MaxSkinIdLength || offset + len > data.Length)
                {
                    return false;
                }

                ids[startIndex + i] = len > 0 ? Encoding.UTF8.GetString(data, offset, len) : string.Empty;
                offset += len;
            }

            state = PlayerSkinNetworkState.FromSlotIds(ids);
            return true;
        }

        private static PlayerSkinNetworkState MergeClothingAndWeapon(
            in PlayerSkinNetworkState clothingState,
            in PlayerSkinNetworkState weaponState)
        {
            return new PlayerSkinNetworkState(
                clothingState.ShirtId,
                clothingState.PantsId,
                clothingState.BootsId,
                clothingState.GlovesId,
                clothingState.FaceId,
                clothingState.HairId,
                weaponState.WeaponAssaultId,
                weaponState.WeaponSniperId,
                weaponState.WeaponPistolId,
                weaponState.WeaponMp7Id);
        }

        private static int GetEncodedSizeForRange(in PlayerSkinNetworkState state, int startIndex, int count)
        {
            var size = count;
            for (var i = 0; i < count; i++)
            {
                size += GetEncodedIdLength(state.GetSlotId(startIndex + i));
            }

            return size;
        }

        private static int WriteSlotId(byte[] buffer, int offset, string id)
        {
            id ??= string.Empty;
            var bytes = Encoding.UTF8.GetBytes(id);
            if (bytes.Length > MaxSkinIdLength)
            {
                Array.Resize(ref bytes, MaxSkinIdLength);
            }

            buffer[offset++] = (byte)bytes.Length;
            if (bytes.Length > 0)
            {
                Buffer.BlockCopy(bytes, 0, buffer, offset, bytes.Length);
                offset += bytes.Length;
            }

            return offset;
        }

        private static int GetEncodedIdLength(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return 0;
            }

            return Math.Min(Encoding.UTF8.GetByteCount(id), MaxSkinIdLength);
        }
    }
}
