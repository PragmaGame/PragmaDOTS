using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Pragma.Dots.Mathematics
{
    public partial struct FixedPoint
    {
        /// <summary>The sum, or <see cref="MaxValue"/> / <see cref="MinValue"/> when it does not fit.</summary>
        /// <returns><c>false</c> when the sum overflowed; the plain <c>+</c> would have wrapped.</returns>
        public static bool TryAdd(FixedPoint a, FixedPoint b, out FixedPoint result)
        {
            var sum = a.rawValue + b.rawValue;
            if (AddOverflows(a.rawValue, b.rawValue, sum))
            {
                result = FromRaw(a.rawValue < 0 ? MIN_RAW : MAX_RAW);
                return false;
            }
            result = FromRaw(sum);
            return true;
        }

        /// <summary>The difference, or <see cref="MaxValue"/> / <see cref="MinValue"/> when it does not fit.</summary>
        /// <returns><c>false</c> when the difference overflowed; the plain <c>-</c> would have wrapped.</returns>
        public static bool TrySubtract(FixedPoint a, FixedPoint b, out FixedPoint result)
        {
            var difference = a.rawValue - b.rawValue;
            if (SubtractOverflows(a.rawValue, b.rawValue, difference))
            {
                result = FromRaw(a.rawValue < 0 ? MIN_RAW : MAX_RAW);
                return false;
            }
            result = FromRaw(difference);
            return true;
        }

        /// <summary>
        /// The product, rounded like <c>a * b</c>, or <see cref="MaxValue"/> / <see cref="MinValue"/> when its 64-bit
        /// intermediate does not fit: from a magnitude of about 2.1e9.
        /// </summary>
        /// <returns><c>false</c> when the product overflowed; the plain <c>*</c> would have wrapped.</returns>
        public static bool TryMultiply(FixedPoint a, FixedPoint b, out FixedPoint result)
        {
            if (MultiplyOverflows(a.rawValue, b.rawValue))
            {
                result = FromRaw((a.rawValue < 0) ^ (b.rawValue < 0) ? MIN_RAW : MAX_RAW);
                return false;
            }
            result = FromRaw((a.rawValue * b.rawValue + HALF_RAW) >> FRACTIONAL_BITS);
            return true;
        }

        /// <summary>The exact product with an integer, or <see cref="MaxValue"/> / <see cref="MinValue"/> when it does not fit.</summary>
        /// <returns><c>false</c> when the product overflowed; the plain <c>*</c> would have wrapped.</returns>
        public static bool TryMultiply(FixedPoint a, int b, out FixedPoint result)
        {
            if (MultiplyByIntegerOverflows(a.rawValue, b))
            {
                result = FromRaw((a.rawValue < 0) ^ (b < 0) ? MIN_RAW : MAX_RAW);
                return false;
            }
            result = FromRaw(a.rawValue * b);
            return true;
        }

        // The checks below run only with PRAGMA_DOTS_CHECKED_MATH defined: without it the compiler removes their calls.

        [Conditional("PRAGMA_DOTS_CHECKED_MATH")]
        private static void CheckAdd(long a, long b)
        {
            if (AddOverflows(a, b, a + b))
            {
                throw new OverflowException("FixedPoint addition overflowed: the sum is beyond ±1.4e14.");
            }
        }

        [Conditional("PRAGMA_DOTS_CHECKED_MATH")]
        private static void CheckSubtract(long a, long b)
        {
            if (SubtractOverflows(a, b, a - b))
            {
                throw new OverflowException("FixedPoint subtraction overflowed: the difference is beyond ±1.4e14.");
            }
        }

        [Conditional("PRAGMA_DOTS_CHECKED_MATH")]
        private static void CheckMultiply(long a, long b)
        {
            if (MultiplyOverflows(a, b))
            {
                throw new OverflowException("FixedPoint multiplication overflowed: the product is beyond about ±2.1e9; use FixedMath.MulDiv.");
            }
        }

        [Conditional("PRAGMA_DOTS_CHECKED_MATH")]
        private static void CheckMultiplyByInteger(long a, int b)
        {
            if (MultiplyByIntegerOverflows(a, b))
            {
                throw new OverflowException("FixedPoint multiplication by an integer overflowed: the product is beyond ±1.4e14.");
            }
        }

        [Conditional("PRAGMA_DOTS_CHECKED_MATH")]
        private static void CheckNegate(long raw)
        {
            if (raw == long.MinValue)
            {
                throw new OverflowException("FixedPoint negation overflowed: the value had already wrapped below MinValue.");
            }
        }

        [Conditional("PRAGMA_DOTS_CHECKED_MATH")]
        private static void CheckFromLong(long value)
        {
            if (value > MAX_RAW >> FRACTIONAL_BITS || value < -(MAX_RAW >> FRACTIONAL_BITS))
            {
                throw new OverflowException("Conversion of a long to FixedPoint overflowed: the value is beyond ±1.4e14.");
            }
        }

        // long.MinValue is outside the symmetric range, so a result that lands on it overflowed too.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool AddOverflows(long a, long b, long sum) => ((a ^ sum) & (b ^ sum)) < 0 || sum == long.MinValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool SubtractOverflows(long a, long b, long difference) => ((a ^ b) & (a ^ difference)) < 0 || difference == long.MinValue;

        // a * b + HALF_RAW must fit in 64 bits before the shift; the 128-bit product tells whether it does.
        private static bool MultiplyOverflows(long a, long b)
        {
            FixedMath.Multiply(FixedMath.UnsignedAbs(a), FixedMath.UnsignedAbs(b), out var hi, out var lo);
            if (hi != 0)
            {
                return true;
            }
            var negative = (a < 0) ^ (b < 0);
            return negative ? lo > (1UL << 63) + (ulong)HALF_RAW : lo > (ulong)(long.MaxValue - HALF_RAW);
        }

        private static bool MultiplyByIntegerOverflows(long a, int b)
        {
            FixedMath.Multiply(FixedMath.UnsignedAbs(a), FixedMath.UnsignedAbs(b), out var hi, out var lo);
            return hi != 0 || lo > long.MaxValue;
        }
    }
}
