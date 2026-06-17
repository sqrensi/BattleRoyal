using System;
using System.Text;
using ShooterPrototype.Player;

namespace ShooterPrototype.Network
{
    internal static class PlayerSkinNetworkCodec
    {
        public const int MaxSkinIdLength = 48;
        public const int SlotCount = PlayerSkinNetworkState.SlotCount;

        public static int WriteSlotIds(byte[] buffer, int offset, in PlayerSkinNetworkState state)
        {
            offset = WriteSlotId(buffer, offset, state.ShirtId);
            offset = WriteSlotId(buffer, offset, state.PantsId);
            offset = WriteSlotId(buffer, offset, state.BootsId);
            offset = WriteSlotId(buffer, offset, state.GlovesId);
            offset = WriteSlotId(buffer, offset, state.FaceId);
            offset = WriteSlotId(buffer, offset, state.HairId);
            return offset;
        }

        public static int GetEncodedSize(in PlayerSkinNetworkState state)
        {
            var size = SlotCount;
            for (var i = 0; i < SlotCount; i++)
            {
                size += GetEncodedIdLength(state.GetSlotId(i));
            }

            return size;
        }

        public static bool TryReadSlotIds(byte[] data, ref int offset, out PlayerSkinNetworkState state)
        {
            state = default;
            if (data == null)
            {
                return false;
            }

            var ids = new string[SlotCount];
            for (var i = 0; i < SlotCount; i++)
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

                ids[i] = len > 0 ? Encoding.UTF8.GetString(data, offset, len) : string.Empty;
                offset += len;
            }

            state = PlayerSkinNetworkState.FromSlotIds(ids);
            return true;
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
