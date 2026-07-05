namespace PolyLabelNet48.Models
{
    /// <summary>
    /// Defines a contract for a 2D point, enabling zero-overhead generic execution in Polylabel.
    /// </summary>
    public interface IPoint
    {
        /// <summary>The X coordinate.</summary>
        float X { get; }

        /// <summary>The Y coordinate.</summary>
        float Y { get; }
    }
}