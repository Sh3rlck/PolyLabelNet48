namespace PolyLabelNet48.Models
{
    /// <summary>
    /// Represents a 2F point with double-precision coordinates.
    /// </summary>
    public readonly struct Point : IPoint
    {
        /// <summary>The X coordinate.</summary>
        public float X { get; }

        /// <summary>The Y coordinate.</summary>
        public float Y { get; }

        /// <summary>Creates a new point with the specified coordinates.</summary>
        public Point(float x, float y)
        {
            X = x;
            Y = y;
        }

        /// <inheritdoc />
        public override string ToString() => $"({X}, {Y})";
    }
}