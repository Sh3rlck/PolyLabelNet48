using System.Runtime.CompilerServices;

namespace PolyLabelNet
{
    /// <summary>
    /// Represents a square cell of the polygon search grid, holding its center, half-size and
    /// signed distance to the polygon outline.
    /// </summary>
    internal readonly struct Cell
    {
        /// <summary>The X coordinate of the cell center.</summary>
        public float X { get; }

        /// <summary>The Y coordinate of the cell center.</summary>
        public float Y { get; }

        /// <summary>Half the cell size.</summary>
        public float H { get; }

        /// <summary>Signed distance from the cell center to the polygon outline.</summary>
        public float D { get; }

        /// <summary>The maximum distance to the polygon outline within the cell (used as priority).</summary>
        public float Max { get; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Cell(float x, float y, float h, float d)
        {
            X = x;
            Y = y;
            H = h;
            D = d;
            Max = d + h * 1.4142135623730951f; // d + h * sqrt(2)
        }
    }

    /// <summary>
    /// Abstraction over the priority queue used to drive the cell search, enabling alternative
    /// queue implementations to be substituted with zero overhead.
    /// </summary>
    internal interface ICellQueue
    {
        /// <summary>Adds a cell to the queue.</summary>
        void Enqueue(Cell cell);

        /// <summary>Removes and returns the highest-priority cell.</summary>
        Cell Dequeue();

        /// <summary>The number of cells currently in the queue.</summary>
        int Count { get; }
    }
}