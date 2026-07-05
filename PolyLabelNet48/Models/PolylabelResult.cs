namespace PolyLabelNet48.Models
{
    /// <summary>
    /// Represents the result of a Polylabel calculation.
    /// </summary>
    public readonly struct PolylabelResult
    {
        /// <summary>The pole of inaccessibility point.</summary>
        public Point Point { get; }

        /// <summary>The distance from the pole to the nearest polygon edge.</summary>
        public float Distance { get; }

        /// <summary>Creates a new result from the specified point and distance.</summary>
        public PolylabelResult(Point point, float distance)
        {
            Point = point;
            Distance = distance;
        }

        /// <summary>Deconstructs the result into its point and distance components.</summary>
        public void Deconstruct(out Point point, out float distance)
        {
            point = Point;
            distance = Distance;
        }
    }
}