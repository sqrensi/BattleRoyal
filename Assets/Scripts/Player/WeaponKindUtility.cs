using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class WeaponKindUtility
    {
        public const int MaxKindId = (int)WeaponKind.Mp7;

        public static WeaponKind ClampKind(int kind)
        {
            return (WeaponKind)Mathf.Clamp(kind, 0, MaxKindId);
        }

        public static byte ClampKindByte(int kind)
        {
            return (byte)Mathf.Clamp(kind, 0, MaxKindId);
        }
    }
}
