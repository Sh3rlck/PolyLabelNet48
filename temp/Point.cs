namespace Polylabel;

/// <summary>
/// Represents a 2D point with double-precision coordinates.
/// </summary>
public readonly struct Point : IPoint
{
    /// <summary>The X coordinate.</summary>
    public float X { get; }

    /// <summary>The Y coordinate.</summary>
    public float Y { get; }

    /// <summary>Creates a new point with the specified coordinates.</summary>
    public Point(float x, float y) => (X, Y) = (x, y);

    /// <inheritdoc />
    public override string ToString() => $"({X}, {Y})";
}
