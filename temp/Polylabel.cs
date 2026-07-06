using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Polylabel;

/// <summary>
/// Provides methods for finding the pole of inaccessibility of a polygon.
/// </summary>
public static class Polylabel
{
    private const int K = 32;
    // Math.SQRT2; 
    private const float SQRT2 = 1.4142135623730951f;

    /// <summary>
    /// Finds the pole of inaccessibility for the given standard polygon with the specified precision.
    /// </summary>
    /// <param name="polygon">The standard polygon coordinates.</param>
    /// <param name="precision">The search precision (default is 1.0).</param>
    /// <param name="debug">Whether to write debug probe information to the Console (default is false).</param>
    /// <returns>A PolylabelResult containing the found pole and its distance to the outline.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PolylabelResult Run(Polygon polygon, float precision = 1.0f, bool debug = false)
    {
        return Run<Polygon, Point>(polygon, precision, debug);
    }

    /// <summary>
    /// Finds the pole of inaccessibility for the given generic polygon with the specified precision.
    /// Supports any point type implementing the IPoint interface with zero runtime overhead.
    /// </summary>
    /// <typeparam name="TPoint">The type of the point, which must be a struct implementing IPoint.</typeparam>
    /// <param name="polygon">The generic polygon coordinates.</param>
    /// <param name="precision">The search precision (default is 1.0).</param>
    /// <param name="debug">Whether to write debug probe information to the Console (default is false).</param>
    /// <returns>A PolylabelResult containing the found pole and its distance to the outline.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PolylabelResult Run<TPoint>(Polygon<TPoint> polygon, float precision = 1.0f, bool debug = false)
        where TPoint : struct, IPoint
    {
        return Run<Polygon<TPoint>, TPoint>(polygon, precision, debug);
    }

    /// <summary>
    /// Finds the pole of inaccessibility for any custom polygon implementation with the specified precision.
    /// Supports completely custom third-party polygon types (e.g. NetTopologySuite) with zero runtime overhead.
    /// </summary>
    /// <typeparam name="TPolygon">The type of the polygon, which must be a struct implementing IPolygon&lt;TPoint&gt;.</typeparam>
    /// <typeparam name="TPoint">The type of the point, which must be a struct implementing IPoint.</typeparam>
    /// <param name="polygon">The custom polygon coordinates.</param>
    /// <param name="precision">The search precision (default is 1.0).</param>
    /// <param name="debug">Whether to write debug probe information to the Console (default is false).</param>
    /// <returns>A PolylabelResult containing the found pole and its distance to the outline.</returns>
    public static PolylabelResult Run<TPolygon, TPoint>(TPolygon polygon, float precision = 1.0f, bool debug = false)
        where TPolygon : struct, IPolygon<TPoint>
        where TPoint : struct, IPoint
    {
        return RunCore<TPolygon, TPoint, NativeCellQueue>(polygon, new NativeCellQueue(), precision, debug);
    }

    internal static PolylabelResult RunCore<TPolygon, TPoint, TCellQueue>(
        TPolygon polygon, TCellQueue cellQueue, float precision, bool debug)
        where TPolygon : struct, IPolygon<TPoint>
        where TPoint : struct, IPoint
        where TCellQueue : struct, ICellQueue
    {
        int ringCount = polygon.RingCount;
        if (ringCount == 0)
        {
            return new PolylabelResult(new Point(0, 0), 0, 0);
        }

        ReadOnlySpan<TPoint> outerRing = polygon.GetRing(0);
        if (outerRing.Length == 0)
        {
            return new PolylabelResult(new Point(0, 0), 0, 0);
        }

        // 1. Find the bounding box of the outer ring
        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;

        for (int i = 0; i < outerRing.Length; i++)
        {
            TPoint p = outerRing[i];
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }

        float width = maxX - minX;
        float height = maxY - minY;
        float cellSize = Math.Max(precision, Math.Min(width, height));

        if (cellSize == precision)
        {
            return new PolylabelResult(new Point(minX, minY), 0, 0);
        }

        // flatten the polygon rings into a single contiguous coordinate buffer for
        // cache-friendly, pointer-chase-free access in the hot distance loop
        int numPoints = 0;
        for (int i = 0; i < ringCount; i++)
        {
            numPoints += polygon.GetRing(i).Length;
        }
        float[] coords = new float[numPoints * 2];
        List<int> ringEnds = new List<int>();
        int c = 0;
        for (int i = 0; i < ringCount; i++)
        {
            var ring = polygon.GetRing(i);
            for (int j = 0; j < ring.Length; j++)
            {
                coords[c++] = ring[j].X;
                coords[c++] = ring[j].Y;
            }
            ringEnds.Add(c);
        }

        float[] blocks = BuildBlocks(coords, ringEnds);

        // a priority queue of cells in order of their "potential" (max distance to polygon)
        // const cellQueue = new Queue([], (a, b) => b.max - a.max);

        // 2. Take centroid as the first best guess
        Cell bestCell = GetCentroidCell(coords, ringEnds, blocks);

        // 3. Second guess: bounding box centroid
        Cell bboxCell = CreateCell(minX + width / 2, minY + height / 2, 0, coords, ringEnds, blocks, float.NegativeInfinity, null);
        if (bboxCell.D > bestCell.D) bestCell = bboxCell;

        int numProbes = 2;

        // 4. Cover polygon with initial cells
        float h = cellSize / 2.0f;
        for (float x = minX; x < maxX; x += cellSize)
        {
            for (float y = minY; y < maxY; y += cellSize)
            {
                PotentiallyQueue<TCellQueue>(x + h, y + h, h, null, coords, ringEnds, blocks, ref numProbes, ref bestCell, cellQueue, precision, debug);
            }
        }

        // 5. Main queue processing loop
        while (cellQueue.Count > 0)
        {
            Cell cell = cellQueue.Dequeue();

            // Do not drill down further if there's no chance of a better solution
            if (cell.Max - bestCell.D <= precision) break;

            // Split the cell into four child cells
            h = cell.H / 2.0f;
            PotentiallyQueue<TCellQueue>(cell.X - h, cell.Y - h, h, cell, coords, ringEnds, blocks, ref numProbes, ref bestCell, cellQueue, precision, debug);
            PotentiallyQueue<TCellQueue>(cell.X + h, cell.Y - h, h, cell, coords, ringEnds, blocks, ref numProbes, ref bestCell, cellQueue, precision, debug);
            PotentiallyQueue<TCellQueue>(cell.X - h, cell.Y + h, h, cell, coords, ringEnds, blocks, ref numProbes, ref bestCell, cellQueue, precision, debug);
            PotentiallyQueue<TCellQueue>(cell.X + h, cell.Y + h, h, cell, coords, ringEnds, blocks, ref numProbes, ref bestCell, cellQueue, precision, debug);
        }

        if (debug)
        {
            Console.WriteLine($"num probes: {numProbes}\nbest distance: {bestCell.D}");
        }

        return new PolylabelResult(new Point(bestCell.X, bestCell.Y), bestCell.D, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void PotentiallyQueue<TCellQueue>(
        float x, float y, float h,
        Cell seed,
        float[] coords, List<int> ringEnds, float[] blocks,
        ref int numProbes,
        ref Cell bestCell,
        TCellQueue cellQueue,
        float precision,
        bool debug)
        where TCellQueue : struct, ICellQueue
    {
        // a cell is only useful if it can beat the best (d > bestCell.d) or is
        // worth subdividing (max = d + h¡¤¡Ì2 > bestCell.d + precision). Both fail
        // once d ¡Ü threshold, so the distance scan can bail there early.
        float threshold = bestCell.D - Math.Max(0, h * SQRT2 - precision);
        Cell cell = CreateCell(x, y, h, coords, ringEnds, blocks, threshold, seed);
        numProbes++;
        if (cell.Max > bestCell.D + precision) cellQueue.Enqueue(cell);

        // update the best cell if we found a better one
        if (cell.D > bestCell.D)
        {
            bestCell = cell;
            if (debug)
            {
                Console.WriteLine($"found best {Math.Round(1e4 * cell.D) / 1e4} after {numProbes} probes");
            }
        }
    }

    // [MethodImpl(MethodImplOptions.AggressiveInlining)]
    // private static void PotentiallyQueue<TPolygon, TPoint, TCellQueue>(
    //     float x, float y, float h,
    //     TPolygon polygon,
    //     ref int numProbes,
    //     ref Cell bestCell,
    //     TCellQueue cellQueue,
    //     float precision,
    //     bool debug)
    //     where TPolygon : struct, IPolygon<TPoint>
    //     where TPoint : struct, IPoint
    //     where TCellQueue : struct, ICellQueue
    // {
    //     Cell cell = CreateCell<TPolygon, TPoint>(x, y, h, polygon);
    //     numProbes++;
    //     if (cell.Max > bestCell.D + precision)
    //     {
    //         cellQueue.Enqueue(cell);
    //     }
    // 
    //     if (cell.D > bestCell.D)
    //     {
    //         bestCell = cell;
    //         if (debug)
    //         {
    //             Console.WriteLine($"found best {Math.Round(1e4 * cell.D) / 1e4} after {numProbes} probes");
    //         }
    //     }
    // }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Cell CreateCell(float x, float y, float h, float[] coords, List<int> ringEnds, float[] blocks, float maxD, Cell seed)
    {
        var cell = new Cell();
        cell.X = x; // cell center x
        cell.Y = y; // cell center y
        cell.H = h; // half the cell size
        // nsx1..nsy2 hold the nearest segment found below, so child cells can seed
        // their scan with it (a child is almost always nearest to the same segment)
        cell.NsX1 = 0; cell.NsY1 = 0; cell.NsX2 = 0; cell.NsY2 = 0;
        cell.D = PointToPolygonDist(cell, coords, ringEnds, blocks, maxD, seed); // distance from cell center to polygon
        // cell.Max = cell.D + h * Math.SQRT2; // max distance to polygon within a cell
        cell.Max = cell.D + h * SQRT2; // max distance to polygon within a cell

        return cell;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float PointToPolygonDist(Cell cell, float[] coords, List<int> ringEnds, float[] blocks, float maxD, Cell seed)
    {
        float x = cell.X;
        float y = cell.Y;
        bool inside = false;
        float minDistSq = float.PositiveInfinity;
        float thresholdSq = maxD > 0 ? maxD * maxD : -1f;

        if (seed != null)
        {
            cell.NsX1 = seed.NsX1; cell.NsY1 = seed.NsY1; cell.NsX2 = seed.NsX2; cell.NsY2 = seed.NsY2;
            minDistSq = GetSegDistSq(x, y, seed.NsX1, seed.NsY1, seed.NsX2, seed.NsY2);
            if (minDistSq <= thresholdSq) return maxD;
        }

        int stride = K * 2;
        int numRings = ringEnds.Count;
        int g = 0; // running block index into bboxes
        int ringStart = 0;

        for (int r = 0; r < numRings; r++)
        {
            var ringEnd = ringEnds[r];

            // previous vertex (b), starting from the last point in the ring; carried
            // across blocks so each block's first edge connects to the prior vertex
            float bx = coords[ringEnd - 2];
            float by = coords[ringEnd - 1];

            for (int s = ringStart; s < ringEnd; s += stride, g += 4)
            {
                int end = s + stride;
                if (end > ringEnd) end = ringEnd;
                float bminX = blocks[g], bminY = blocks[g + 1], bmaxX = blocks[g + 2], bmaxY = blocks[g + 3];

                // lower bound on the distance from (x, y) to any edge in this block
                float dx = x < bminX ? bminX - x : x > bmaxX ? x - bmaxX : 0;
                float dy = y < bminY ? bminY - y : y > bmaxY ? y - bmaxY : 0;
                bool skipDist = (dx * dx + dy * dy) >= minDistSq;

                // this block's edges can only flip ray-cast parity if its bbox straddles
                // y and extends right of x; else no edge crosses the rightward ray
                bool skipCross = y < bminY || y >= bmaxY || x > bmaxX;

                if (skipDist && skipCross)
                {
                    bx = coords[end - 2];
                    by = coords[end - 1];
                    continue;
                }

                for (int i = s; i < end; i += 2)
                {
                    float ax = coords[i];
                    float ay = coords[i + 1];

                    if (!skipCross && ((ay > y) != (by > y)) && (x < ((bx - ax) * (y - ay) / (by - ay) + ax))) inside = !inside;

                    if (!skipDist)
                    {
                        float distSq = GetSegDistSq(x, y, ax, ay, bx, by);
                        if (distSq < minDistSq)
                        {
                            minDistSq = distSq;
                            cell.NsX1 = ax; cell.NsY1 = ay; cell.NsX2 = bx; cell.NsY2 = by;

                            // the point is already close enough to the outline that this cell
                            // can't possibly contain a better label position ¡ª stop scanning
                            if (minDistSq <= thresholdSq) return maxD;
                        }
                    }

                    bx = ax;
                    by = ay;
                }

            }

            ringStart = ringEnd;
        }

        return minDistSq == 0f ? 0f : (inside ? 1f : -1f) * MathF.Sqrt(minDistSq);
    }

    //    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //    private static Cell CreateCell<TPolygon, TPoint>(float x, float y, float h, TPolygon polygon)
    //        where TPolygon : struct, IPolygon<TPoint>
    //        where TPoint : struct, IPoint
    //    {
    //        float d = PointToPolygonDist<TPolygon, TPoint>(x, y, polygon);
    //        return new Cell(x, y, h, d);
    //    }
    //    
    //    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //    private static float PointToPolygonDist<TPolygon, TPoint>(float x, float y, TPolygon polygon)
    //        where TPolygon : struct, IPolygon<TPoint>
    //        where TPoint : struct, IPoint
    //    {
    //        bool inside = false;
    //        float minDistSq = float.PositiveInfinity;
    //    
    //        int ringCount = polygon.RingCount;
    //        for (int r = 0; r < ringCount; r++)
    //        {
    //            ReadOnlySpan<TPoint> ring = polygon.GetRing(r);
    //            int len = ring.Length;
    //            if (len == 0) continue;
    //    
    //            TPoint b = ring[len - 1];
    //            for (int i = 0; i < len; i++)
    //            {
    //                TPoint a = ring[i];
    //    
    //                if ((a.Y > y) != (b.Y > y) &&
    //                    (x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X))
    //                {
    //                    inside = !inside;
    //                }
    //    
    //                float distSq = GetSegDistSq(x, y, a.X, a.Y, b.X, b.Y);
    //                if (distSq < minDistSq)
    //                {
    //                    minDistSq = distSq;
    //                }
    //    
    //                b = a;
    //            }
    //        }
    //    
    //        return minDistSq == 0f ? 0f : (inside ? 1f : -1f) * MathF.Sqrt(minDistSq);
    //    }

    // precompute one bounding box per block of K consecutive edges (over both
    // endpoints of every edge in it) so the distance scan can skip whole blocks in
    // O(1). The block layout mirrors the flattened coords/ringEnds and is re-derived
    // in the scan, so only the bboxes need storing: a flat [minX,minY,maxX,maxY] run
    // per block, sized upfront from the ring lengths.
    private static float[] BuildBlocks(float[] coords, List<int> ringEnds)
    {
        const int stride = K * 2;
        int numBlocks = 0;
        int ringStart = 0;

        for (int r = 0; r < ringEnds.Count; r++)
        {
            numBlocks += (int)MathF.Ceiling(1.0f * (ringEnds[r] - ringStart) / stride);
            ringStart = ringEnds[r];
        }

        float[] blocks = new float[numBlocks * 4];
        int g = 0;
        ringStart = 0;
        for (int r = 0; r < ringEnds.Count; r++)
        {
            int ringEnd = ringEnds[r];
            for (int s = ringStart; s < ringEnd; s += stride, g += 4)
            {
                int end = (s + stride) < ringEnd ? s + stride : ringEnd;
                int prev = s == ringStart ? ringEnd - 2 : s - 2;

                float minX = coords[prev], minY = coords[prev + 1];
                float maxX = minX, maxY = minY;
                for (int i = s; i < end; i += 2)
                {
                    float px = coords[i], py = coords[i + 1];
                    if (px < minX) minX = px; else if (px > maxX) maxX = px;
                    if (py < minY) minY = py; else if (py > maxY) maxY = py;
                }
                blocks[g] = minX; blocks[g + 1] = minY; blocks[g + 2] = maxX; blocks[g + 3] = maxY;
            }
            ringStart = ringEnd;
        }

        return blocks;
    }

    // get polygon centroid (over the outer ring, coords[0..ringEnds[0]))
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Cell GetCentroidCell(float[] coords, List<int> ringEnds, float[] blocks)
    {
        float area = 0;
        float x = 0;
        float y = 0;
        int end = ringEnds[0];

        for (int i = 0, j = end - 2; i < end; j = i, i += 2)
        {
            float ax = coords[i];
            float ay = coords[i + 1];
            float bx = coords[j];
            float by = coords[j + 1];
            float f = ax * by - bx * ay;
            x += (ax + bx) * f;
            y += (ay + by) * f;
            area += f * 3;
        }

        Cell centroid = CreateCell(x / area, y / area, 0, coords, ringEnds, blocks, float.NegativeInfinity, null);
        if (area == 0 || centroid.D < 0) return CreateCell(coords[0], coords[1], 0, coords, ringEnds, blocks, float.NegativeInfinity, null);
        return centroid;
    }

    //    // get polygon centroid (over the outer ring, coords[0..ringEnds[0]))
    //    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //    private static Cell GetCentroidCell<TPolygon, TPoint>(TPolygon polygon)
    //        where TPolygon : struct, IPolygon<TPoint>
    //        where TPoint : struct, IPoint
    //    {
    //        float area = 0;
    //        float x = 0;
    //        float y = 0;
    //        ReadOnlySpan<TPoint> points = polygon.GetRing(0);
    //        int len = points.Length;
    //        if (len == 0) return new Cell(0, 0, 0, 0);
    //    
    //        TPoint b = points[len - 1];
    //        for (int i = 0; i < len; i++)
    //        {
    //            TPoint a = points[i];
    //            float f = a.X * b.Y - b.X * a.Y;
    //            x += (a.X + b.X) * f;
    //            y += (a.Y + b.Y) * f;
    //            area += f * 3.0f;
    //            b = a;
    //        }
    //    
    //        if (area == 0)
    //        {
    //            TPoint first = points[0];
    //            return CreateCell<TPolygon, TPoint>(first.X, first.Y, 0, polygon);
    //        }
    //    
    //        float cx = x / area;
    //        float cy = y / area;
    //        Cell centroid = CreateCell<TPolygon, TPoint>(cx, cy, 0, polygon);
    //        if (centroid.D < 0)
    //        {
    //            TPoint first = points[0];
    //            return CreateCell<TPolygon, TPoint>(first.X, first.Y, 0, polygon);
    //        }
    //    
    //        return centroid;
    //    }

    // get squared distance from a point to a segment
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float GetSegDistSq(float px, float py, float x, float y, float bx, float by)
    {
        float dx = bx - x;
        float dy = bx - y;

        if (dx != 0 || dy != 0)
        {
            float t = ((px - x) * dx + (py - y) * dy) / (dx * dx + dy * dy);

            if (t > 1)
            {
                x = bx;
                y = by;
            }
            else if (t > 0)
            {
                x += dx * t;
                y += dy * t;
            }
        }

        dx = px - x;
        dy = py - y;

        return dx * dx + dy * dy;
    }
}
