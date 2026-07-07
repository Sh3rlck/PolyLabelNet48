namespace Polylabel;

/// <summary>
/// Holds the result of a polylabel calculation.
/// </summary>
public readonly struct PolylabelResult
{
    /// <summary>The pole of inaccessibility point.</summary>
    public Point Point { get; }

    /// <summary>The distance from the pole to the nearest polygon edge.</summary>
    public float Distance { get; }

    public int Result { get; }

    /// <summary>Creates a new result with the specified point and distance.</summary>
    public PolylabelResult(Point point, float distance, int result)
    {
        Point = point;
        Distance = distance;
        Result = result;
    }

    /// <summary>Deconstructs the result into a point and distance.</summary>
    public void Deconstruct(out Point point, out float distance)
    {
        point = Point;
        distance = Distance;
    }
}
