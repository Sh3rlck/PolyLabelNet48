namespace PolyLabelNet
{
    /// <summary>
    /// Defines a contract for a polygon, enabling zero-overhead generic execution over custom geometry containers.
    /// </summary>
    /// <typeparam name="TPoint">The type of point, which must be a struct implementing IPoint.</typeparam>
    public interface IPolygon<TPoint>
        where TPoint : struct, IPoint
    {
        /// <summary>The total number of rings (the outer boundary plus any holes).</summary>
        int RingCount { get; }

        /// <summary>
        /// Retrieves a specific ring as an array, with the outer boundary at index 0 followed by interior rings (holes).
        /// </summary>
        /// <param name="index">The zero-based ring index.</param>
        TPoint[] GetRing(int index);
    }
}