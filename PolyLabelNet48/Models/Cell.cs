using System.Runtime.CompilerServices;

namespace PolyLabelNet
{
    /// <summary>
    /// Represents a square cell of the polygon search grid, holding its center, half-size and
    /// signed distance to the polygon outline.
    /// </summary>
    internal class Cell
    {
        /// <summary>The X coordinate of the cell center.</summary>
        public float X;

        /// <summary>The Y coordinate of the cell center.</summary>
        public float Y;

        /// <summary>Half the cell size.</summary>
        public float H;

        /// <summary>Signed distance from the cell center to the polygon outline.</summary>
        public float D;

        /// <summary>The maximum distance to the polygon outline within the cell (used as priority).</summary>
        public float Max;

        // nsx1..nsy2 hold the nearest segment found below, so child cells can seed
        // their scan with it (a child is almost always nearest to the same segment)
        public float NsX1;
        public float NsY1;
        public float NsX2;
        public float NsY2;

        public Cell() { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Cell(float x, float y, float h, float d)
        {
            X = x;
            Y = y;
            H = h;
            D = d;
            Max = d + h * 1.4142135623730951f; // d + h * sqrt(2)

            NsX1 = 0;
            NsY1 = 0;
            NsX2 = 0;
            NsY2 = 0;
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