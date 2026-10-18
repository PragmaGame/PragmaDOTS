using System;
using Unity.Collections.LowLevel.Unsafe;

namespace Pragma.Dots.Utils
{
    /// <summary>
    /// The number an enum value is stored as in components and buffers: its numeric value, for an enum of any size.
    /// Burst-compatible, so jobs and systems convert the game's enums the same way managed code does.
    /// </summary>
    public static class EnumId
    {
        /// <summary>
        /// The value of <paramref name="value"/> as an <c>int</c>: byte and ushort enums read unsigned, a long enum is
        /// truncated to its low 32 bits.
        /// </summary>
        public static int Of<TEnum>(TEnum value) where TEnum : unmanaged, Enum
        {
            // An enum is its underlying integer, and its size is a constant to Burst.
            switch (UnsafeUtility.SizeOf<TEnum>())
            {
                case 1:
                    return UnsafeUtility.As<TEnum, byte>(ref value);
                case 2:
                    return UnsafeUtility.As<TEnum, ushort>(ref value);
                case 8:
                    return (int)UnsafeUtility.As<TEnum, long>(ref value);
                default:
                    return UnsafeUtility.As<TEnum, int>(ref value);
            }
        }
    }
}
