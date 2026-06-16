using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ShooterPrototype.Network
{
    internal static class RealtimeSnapshotBinaryCodec
    {
        private static readonly byte[] Magic = { (byte)'R', (byte)'T', (byte)'S', (byte)'1' };

        public static bool TryDecode(byte[] data, out RealtimeTransportClient.RealtimeSnapshot snapshot)
        {
            snapshot = null;
            if (data == null || data.Length < 12)
            {
                return false;
            }

            for (var i = 0; i < Magic.Length; i++)
            {
                if (data[i] != Magic[i])
                {
                    return false;
                }
            }

            var offset = 4;
            var version = ReadU8(data, ref offset);
            if (version != 1 && version != 2 && version != 3 && version != 4 && version != 5 && version != 6 && version != 7 && version != 8 && version != 9 && version != 10)
            {
                return false;
            }

            var serverTick = ReadU32(data, ref offset);
            var serverTickRate = ReadU16(data, ref offset);
            var flags = ReadU8(data, ref offset);

            RealtimeTransportClient.SelfAuthoritativePose selfAuth = null;
            if ((flags & 1) != 0)
            {
                if (offset + 20 > data.Length)
                {
                    return false;
                }

                var sampleTick = ReadU32(data, ref offset);
                var x = ReadF32(data, ref offset);
                var y = ReadF32(data, ref offset);
                var z = ReadF32(data, ref offset);
                var yaw = ReadF32(data, ref offset);
                selfAuth = new RealtimeTransportClient.SelfAuthoritativePose
                {
                    sampleTick = (int)sampleTick,
                    yaw = yaw,
                    position = new RealtimeTransportClient.PositionDto { x = x, y = y, z = z }
                };
            }

            if (offset >= data.Length)
            {
                return false;
            }

            var playerCount = ReadU8(data, ref offset);
            var players = new List<RealtimeTransportClient.RealtimePlayerState>(playerCount);
            for (var i = 0; i < playerCount; i++)
            {
                if (offset >= data.Length)
                {
                    return false;
                }

                var ticketLen = ReadU8(data, ref offset);
                if (offset + ticketLen > data.Length)
                {
                    return false;
                }

                var ticketId = Encoding.UTF8.GetString(data, offset, ticketLen);
                offset += ticketLen;
                var characterModel = string.Empty;
                if (version >= 6)
                {
                    if (offset >= data.Length)
                    {
                        return false;
                    }

                    var modelLen = ReadU8(data, ref offset);
                    if (offset + modelLen > data.Length)
                    {
                        return false;
                    }

                    characterModel = Encoding.UTF8.GetString(data, offset, modelLen);
                    offset += modelLen;
                }

                if (offset + 35 > data.Length)
                {
                    return false;
                }

                var sampleTick = (int)ReadU32(data, ref offset);
                var px = ReadF32(data, ref offset);
                var py = ReadF32(data, ref offset);
                var pz = ReadF32(data, ref offset);
                var yaw = ReadF32(data, ref offset);
                var velX = ReadF32(data, ref offset);
                var velY = ReadF32(data, ref offset);
                var velZ = ReadF32(data, ref offset);
                var flags2 = ReadU16(data, ref offset);
                var jumpState = ReadU8(data, ref offset);

                if (offset + (version >= 10 ? 106 : version >= 9 ? 103 : version >= 8 ? 102 : version >= 7 ? 97 : version >= 4 ? 93 : version >= 3 ? 80 : version >= 2 ? 72 : 48) > data.Length)
                {
                    return false;
                }

                var lookPitch = ReadF32(data, ref offset);
                var shotSeq = (int)ReadU32(data, ref offset);
                var reloadSeq = (int)ReadU32(data, ref offset);
                var hitPlayerSeq = (int)ReadU32(data, ref offset);
                var footstepSeq = (int)ReadU32(data, ref offset);
                var wallAvoidBlend = ReadF32(data, ref offset);
                var animSpeed = ReadF32(data, ref offset);
                var animPhase = ReadF32(data, ref offset);
                var deathSeq = (int)ReadU32(data, ref offset);
                var deathFallDirX = ReadF32(data, ref offset);
                var deathFallDirY = ReadF32(data, ref offset);
                var deathFallDirZ = ReadF32(data, ref offset);
                var shotOriginX = 0f;
                var shotOriginY = 0f;
                var shotOriginZ = 0f;
                var shotDirX = 0f;
                var shotDirY = 0f;
                var shotDirZ = 0f;
                var shotEndX = 0f;
                var shotEndY = 0f;
                var shotEndZ = 0f;
                var shotHasEndPoint = false;
                var weaponPickupSeq = 0;
                var moveInputX = 0f;
                var moveInputZ = 0f;
                if (version >= 2)
                {
                    if (offset + 24 > data.Length)
                    {
                        return false;
                    }

                    shotOriginX = ReadF32(data, ref offset);
                    shotOriginY = ReadF32(data, ref offset);
                    shotOriginZ = ReadF32(data, ref offset);
                    shotDirX = ReadF32(data, ref offset);
                    shotDirY = ReadF32(data, ref offset);
                    shotDirZ = ReadF32(data, ref offset);
                }

                if (version >= 3)
                {
                    if (offset + 8 > data.Length)
                    {
                        return false;
                    }

                    moveInputX = ReadF32(data, ref offset);
                    moveInputZ = ReadF32(data, ref offset);
                }

                if (version >= 4)
                {
                    if (offset + 13 > data.Length)
                    {
                        return false;
                    }

                    shotEndX = ReadF32(data, ref offset);
                    shotEndY = ReadF32(data, ref offset);
                    shotEndZ = ReadF32(data, ref offset);
                    shotHasEndPoint = ReadU8(data, ref offset) != 0;
                }

                var medkitRemainingSeconds = 0f;
                var medkitCount = 0;
                var weaponKind = 0;
                var weaponSlot0Kind = 255;
                var weaponSlot1Kind = 255;
                var activeWeaponSlot = 255;
                if (version >= 7)
                {
                    if (offset + 4 > data.Length)
                    {
                        return false;
                    }

                    weaponPickupSeq = (int)ReadU32(data, ref offset);
                }

                if (version >= 8)
                {
                    if (offset + 5 > data.Length)
                    {
                        return false;
                    }

                    medkitRemainingSeconds = ReadF32(data, ref offset);
                    medkitCount = ReadU8(data, ref offset);
                }

                if (version >= 9)
                {
                    if (offset + 1 > data.Length)
                    {
                        return false;
                    }

                    weaponKind = ReadU8(data, ref offset);
                }

                if (version >= 10)
                {
                    if (offset + 3 > data.Length)
                    {
                        return false;
                    }

                    weaponSlot0Kind = ReadU8(data, ref offset);
                    weaponSlot1Kind = ReadU8(data, ref offset);
                    activeWeaponSlot = ReadU8(data, ref offset);
                }

                RealtimeTransportClient.RealtimeShotEvent[] recentShots = null;
                if (version >= 5)
                {
                    if (offset >= data.Length)
                    {
                        return false;
                    }

                    var ringCount = ReadU8(data, ref offset);
                    if (ringCount > 0)
                    {
                        if (offset + ringCount * 41 > data.Length)
                        {
                            return false;
                        }

                        recentShots = new RealtimeTransportClient.RealtimeShotEvent[ringCount];
                        for (var s = 0; s < ringCount; s++)
                        {
                            recentShots[s] = new RealtimeTransportClient.RealtimeShotEvent
                            {
                                seq = (int)ReadU32(data, ref offset),
                                originX = ReadF32(data, ref offset),
                                originY = ReadF32(data, ref offset),
                                originZ = ReadF32(data, ref offset),
                                dirX = ReadF32(data, ref offset),
                                dirY = ReadF32(data, ref offset),
                                dirZ = ReadF32(data, ref offset),
                                endX = ReadF32(data, ref offset),
                                endY = ReadF32(data, ref offset),
                                endZ = ReadF32(data, ref offset),
                                hasEndPoint = ReadU8(data, ref offset) != 0
                            };
                        }
                    }
                }

                if (offset >= data.Length)
                {
                    return false;
                }

                var historyCount = ReadU8(data, ref offset);
                RealtimeTransportClient.RealtimeStateSample[] history = null;
                if (historyCount > 0)
                {
                    history = new RealtimeTransportClient.RealtimeStateSample[historyCount];
                    for (var h = 0; h < historyCount; h++)
                    {
                        if (offset + 32 > data.Length)
                        {
                            return false;
                        }

                        history[h] = new RealtimeTransportClient.RealtimeStateSample
                        {
                            sampleTick = (int)ReadU32(data, ref offset),
                            x = ReadF32(data, ref offset),
                            y = ReadF32(data, ref offset),
                            z = ReadF32(data, ref offset),
                            yaw = ReadF32(data, ref offset),
                            velX = ReadF32(data, ref offset),
                            velY = ReadF32(data, ref offset),
                            velZ = ReadF32(data, ref offset)
                        };
                    }
                }

                players.Add(new RealtimeTransportClient.RealtimePlayerState
                {
                    ticketId = ticketId,
                    characterModel = characterModel,
                    position = new RealtimeTransportClient.PositionDto { x = px, y = py, z = pz },
                    yaw = yaw,
                    velX = velX,
                    velY = velY,
                    velZ = velZ,
                    moveInputX = moveInputX,
                    moveInputZ = moveInputZ,
                    lookPitch = lookPitch,
                    shotSeq = shotSeq,
                    reloadSeq = reloadSeq,
                    hitPlayerSeq = hitPlayerSeq,
                    footstepSeq = footstepSeq,
                    wallAvoidBlend = wallAvoidBlend,
                    animSpeed = animSpeed,
                    animPhase = animPhase,
                    deathSeq = deathSeq,
                    deathFallDirX = deathFallDirX,
                    deathFallDirY = deathFallDirY,
                    deathFallDirZ = deathFallDirZ,
                    shotOriginX = shotOriginX,
                    shotOriginY = shotOriginY,
                    shotOriginZ = shotOriginZ,
                    shotDirX = shotDirX,
                    shotDirY = shotDirY,
                    shotDirZ = shotDirZ,
                    shotEndX = shotEndX,
                    shotEndY = shotEndY,
                    shotEndZ = shotEndZ,
                    shotHasEndPoint = shotHasEndPoint,
                    recentShots = recentShots,
                    isDead = (flags2 & 1) != 0,
                    isGrounded = (flags2 & 2) != 0,
                    isCrouching = (flags2 & 4) != 0,
                    isSprinting = (flags2 & 8) != 0,
                    isSwimming = (flags2 & 256) != 0,
                    isAiming = (flags2 & 16) != 0,
                    isHolstered = (flags2 & 32) != 0,
                    hasWeapon = (flags2 & 64) != 0,
                    isUsingMedkit = (flags2 & 128) != 0,
                    medkitRemainingSeconds = medkitRemainingSeconds,
                    medkitCount = medkitCount,
                    weaponPickupSeq = weaponPickupSeq,
                    weaponKind = weaponKind,
                    weaponSlot0Kind = weaponSlot0Kind,
                    weaponSlot1Kind = weaponSlot1Kind,
                    activeWeaponSlot = activeWeaponSlot,
                    jumpState = jumpState,
                    sampleTick = sampleTick,
                    history = history
                });
            }

            snapshot = new RealtimeTransportClient.RealtimeSnapshot
            {
                type = "snapshot",
                serverTick = (int)serverTick,
                serverTickRate = serverTickRate,
                binaryVersion = version,
                players = players.ToArray(),
                selfAuthoritative = selfAuth
            };
            return true;
        }

        private static byte ReadU8(byte[] data, ref int offset)
        {
            return data[offset++];
        }

        private static ushort ReadU16(byte[] data, ref int offset)
        {
            var value = BitConverter.ToUInt16(data, offset);
            offset += 2;
            return value;
        }

        private static uint ReadU32(byte[] data, ref int offset)
        {
            var value = BitConverter.ToUInt32(data, offset);
            offset += 4;
            return value;
        }

        private static float ReadF32(byte[] data, ref int offset)
        {
            var value = BitConverter.ToSingle(data, offset);
            offset += 4;
            return value;
        }
    }
}
