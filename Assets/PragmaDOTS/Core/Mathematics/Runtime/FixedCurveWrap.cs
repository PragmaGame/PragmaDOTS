namespace Pragma.Dots.Mathematics
{
    /// <summary>What a <see cref="FixedCurve"/> gives before its first key or after its last one.</summary>
    public enum FixedCurveWrap
    {
        /// <summary>The value of the nearest end key.</summary>
        Clamp = 0,
        /// <summary>The curve repeats.</summary>
        Loop = 1,
        /// <summary>The curve repeats, every other time backwards.</summary>
        PingPong = 2,
    }
}
