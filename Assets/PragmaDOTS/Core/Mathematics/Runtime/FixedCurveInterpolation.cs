namespace Pragma.Dots.Mathematics
{
    /// <summary>How a <see cref="FixedCurve"/> goes from a key to the next one.</summary>
    public enum FixedCurveInterpolation
    {
        /// <summary>Cubic Hermite through the key's out tangent and the next key's in tangent, like <c>AnimationCurve</c>.</summary>
        Cubic = 0,
        /// <summary>A straight line to the next key; the tangents are ignored.</summary>
        Linear = 1,
        /// <summary>Holds the key's value until the next key: a step.</summary>
        Constant = 2,
    }
}
