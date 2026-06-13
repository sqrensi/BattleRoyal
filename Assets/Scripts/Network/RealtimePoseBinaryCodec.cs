using System;
using System.Text;

namespace ShooterPrototype.Network
{
    internal static class RealtimePoseBinaryCodec
    {
        private static readonly byte[] Magic = { (byte)'R', (byte)'T', (byte)'P', (byte)'1' };
        public const byte Version = 1;
        private const int HeaderSize = 10; // magic4 + version1 + poseSeq4 + modelLen1
        private const int BodySize = 121; // 5*f32 + u16 + 5*u8 + i16 + 6*u32 + 17*f32

        public static byte[] Encode(in RealtimePosePacket packet)
        {
            var model = packet.CharacterModel ?? string.Empty;
            var modelBytes = Encoding.UTF8.GetBytes(model);
            if (modelBytes.Length > 64)
            {
                Array.Resize(ref modelBytes, 64);
            }

            var buffer = new byte[HeaderSize + modelBytes.Length + BodySize];
            var offset = 0;
            WriteMagic(buffer, ref offset);
            WriteU8(buffer, ref offset, Version);
            WriteI32(buffer, ref offset, packet.PoseSeq);
            WriteU8(buffer, ref offset, (byte)modelBytes.Length);
            if (modelBytes.Length > 0)
            {
                Buffer.BlockCopy(modelBytes, 0, buffer, offset, modelBytes.Length);
                offset += modelBytes.Length;
            }

            WriteF32(buffer, ref offset, packet.PosX);
            WriteF32(buffer, ref offset, packet.PosY);
            WriteF32(buffer, ref offset, packet.PosZ);
            WriteF32(buffer, ref offset, packet.Yaw);
            WriteF32(buffer, ref offset, packet.LookPitch);

            ushort flags = 0;
            if (packet.IsCrouching) flags |= 1;
            if (packet.IsSprinting) flags |= 2;
            if (packet.IsDead) flags |= 4;
            if (packet.IsHolstered) flags |= 8;
            if (packet.IsGrounded) flags |= 16;
            if (packet.InputAuth) flags |= 32;
            if (packet.JumpPressed) flags |= 64;
            if (packet.ShotHasEndPoint) flags |= 128;
            if (packet.IsAiming) flags |= 256;
            WriteU16(buffer, ref offset, flags);

            WriteU8(buffer, ref offset, (byte)Math.Clamp(packet.JumpState, 0, 2));
            WriteU8(buffer, ref offset, (byte)Math.Clamp(packet.WeaponKind, 0, 255));
            WriteU8(buffer, ref offset, (byte)Math.Clamp(packet.WeaponSlot0Kind, 0, 255));
            WriteU8(buffer, ref offset, (byte)Math.Clamp(packet.WeaponSlot1Kind, 0, 255));
            WriteU8(buffer, ref offset, (byte)Math.Clamp(packet.ActiveWeaponSlot, 0, 255));
            WriteI16(buffer, ref offset, (short)Math.Clamp(packet.ActiveWeaponMagAmmo, -1, 999));
            WriteU32(buffer, ref offset, (uint)Math.Max(0, packet.WeaponPickupSeq));
            WriteU32(buffer, ref offset, (uint)Math.Max(0, packet.ShotSeq));
            WriteU32(buffer, ref offset, (uint)Math.Max(0, packet.ReloadSeq));
            WriteU32(buffer, ref offset, (uint)Math.Max(0, packet.HitPlayerSeq));
            WriteU32(buffer, ref offset, (uint)Math.Max(0, packet.FootstepSeq));
            WriteU32(buffer, ref offset, (uint)Math.Max(0, packet.DeathSeq));
            WriteF32(buffer, ref offset, packet.AnimSpeed);
            WriteF32(buffer, ref offset, packet.AnimPhase);
            WriteF32(buffer, ref offset, packet.WallAvoidBlend);
            WriteF32(buffer, ref offset, packet.MoveInputX);
            WriteF32(buffer, ref offset, packet.MoveInputZ);
            WriteF32(buffer, ref offset, packet.DeathFallDirX);
            WriteF32(buffer, ref offset, packet.DeathFallDirY);
            WriteF32(buffer, ref offset, packet.DeathFallDirZ);
            WriteF32(buffer, ref offset, packet.ShotOriginX);
            WriteF32(buffer, ref offset, packet.ShotOriginY);
            WriteF32(buffer, ref offset, packet.ShotOriginZ);
            WriteF32(buffer, ref offset, packet.ShotDirX);
            WriteF32(buffer, ref offset, packet.ShotDirY);
            WriteF32(buffer, ref offset, packet.ShotDirZ);
            WriteF32(buffer, ref offset, packet.ShotEndX);
            WriteF32(buffer, ref offset, packet.ShotEndY);
            WriteF32(buffer, ref offset, packet.ShotEndZ);

            if (offset != buffer.Length)
            {
                Array.Resize(ref buffer, offset);
            }

            return buffer;
        }

        private static void WriteMagic(byte[] buffer, ref int offset)
        {
            Buffer.BlockCopy(Magic, 0, buffer, offset, Magic.Length);
            offset += Magic.Length;
        }

        private static void WriteU8(byte[] buffer, ref int offset, byte value)
        {
            buffer[offset++] = value;
        }

        private static void WriteU16(byte[] buffer, ref int offset, ushort value)
        {
            buffer[offset++] = (byte)(value & 0xFF);
            buffer[offset++] = (byte)((value >> 8) & 0xFF);
        }

        private static void WriteI16(byte[] buffer, ref int offset, short value)
        {
            WriteU16(buffer, ref offset, (ushort)value);
        }

        private static void WriteI32(byte[] buffer, ref int offset, int value)
        {
            buffer[offset++] = (byte)(value & 0xFF);
            buffer[offset++] = (byte)((value >> 8) & 0xFF);
            buffer[offset++] = (byte)((value >> 16) & 0xFF);
            buffer[offset++] = (byte)((value >> 24) & 0xFF);
        }

        private static void WriteU32(byte[] buffer, ref int offset, uint value)
        {
            WriteI32(buffer, ref offset, (int)value);
        }

        private static void WriteF32(byte[] buffer, ref int offset, float value)
        {
            var bits = BitConverter.SingleToInt32Bits(value);
            WriteI32(buffer, ref offset, bits);
        }
    }

    internal struct RealtimePosePacket
    {
        public int PoseSeq;
        public string CharacterModel;
        public float PosX;
        public float PosY;
        public float PosZ;
        public float Yaw;
        public float LookPitch;
        public bool IsCrouching;
        public bool IsSprinting;
        public bool IsDead;
        public bool IsHolstered;
        public bool IsGrounded;
        public bool InputAuth;
        public bool JumpPressed;
        public bool ShotHasEndPoint;
        public bool IsAiming;
        public int JumpState;
        public int WeaponKind;
        public int WeaponSlot0Kind;
        public int WeaponSlot1Kind;
        public int ActiveWeaponSlot;
        public int ActiveWeaponMagAmmo;
        public int WeaponPickupSeq;
        public int ShotSeq;
        public int ReloadSeq;
        public int HitPlayerSeq;
        public int FootstepSeq;
        public int DeathSeq;
        public float AnimSpeed;
        public float AnimPhase;
        public float WallAvoidBlend;
        public float MoveInputX;
        public float MoveInputZ;
        public float DeathFallDirX;
        public float DeathFallDirY;
        public float DeathFallDirZ;
        public float ShotOriginX;
        public float ShotOriginY;
        public float ShotOriginZ;
        public float ShotDirX;
        public float ShotDirY;
        public float ShotDirZ;
        public float ShotEndX;
        public float ShotEndY;
        public float ShotEndZ;
    }
}
