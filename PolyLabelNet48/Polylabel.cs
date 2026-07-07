// Ported to .NET Framework 4.8 / C# 7.3 from https://github.com/oberbichler/PolylabelNet
// Original algorithm: Mapbox Polylabel (Copyright (c) 2016 Mapbox). PolylabelNet by Thomas Oberbichler.
// Licensed under the ISC License.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace PolyLabelNet
{
    /// <summary>
    /// Provides methods for finding the pole of inaccessibility of a polygon.
    /// </summary>
    public static class Polylabel
    {
        // number of consecutive edges grouped under a single bounding box for block-skip
        private const int K = 32;
        private const float Math_SQRT2 = 1.4142135623730951f;

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
        /// Supports completely custom third-party polygon types with zero runtime overhead.
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
            return RunCore<TPolygon, TPoint, TinyCellQueue>(polygon, new TinyCellQueue(), precision, debug);
        }

        internal static PolylabelResult RunCore<TPolygon, TPoint, TCellQueue>(
            TPolygon polygon, TCellQueue cellQueue, float precision, bool debug)
            where TPolygon : struct, IPolygon<TPoint>
            where TPoint : struct, IPoint
            where TCellQueue : ICellQueue
        {
            int ringCount = polygon.RingCount;
            if (ringCount == 0)
            {
                return new PolylabelResult(new Point(0, 0), 0);
            }

            TPoint[] outerRing = polygon.GetRing(0);
            if (outerRing.Length == 0)
            {
                return new PolylabelResult(new Point(0, 0), 0);
            }

            // Find the bounding box of the outer ring
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
            float cellSize = MathF.Max(precision, MathF.Min(width, height));

            if (cellSize == precision)
            {
                return new PolylabelResult(new Point(minX, minY), 0);
            }

            // flatten the polygon rings into a single contiguous coordinate buffer for
            // cache-friendly, pointer-chase-free access in the hot distance loop
            int numPoints = 0;
            for (int r = 0; r < ringCount; r++) numPoints += polygon.GetRing(r).Length;

            float[] coords = new float[numPoints * 2];
            int[] ringEnds = []; // end offset into coords for each ring (start = previous end, or 0)
            int c = 0;
            List<int> tmpRingEnds = new List<int>();
            for (int r = 0; r < ringCount; r++) {
                TPoint[] ring = polygon.GetRing(r);
                for (int i = 0; i < ring.Length; i++)
                {
                    coords[c++] = ring[i].X;
                    coords[c++] = ring[i].Y;
                }
                tmpRingEnds.Add(c);
            }
            ringEnds = tmpRingEnds.ToArray();

            float[] blocks = buildBlocks(coords, ringEnds);

            // Take centroid as the first best guess
            Cell bestCell = GetCentroidCell(coords, ringEnds, blocks);

            // Second guess: bounding box centroid
            Cell bboxCell = CreateCell(minX + width / 2.0f, minY + height / 2.0f, 0, coords, ringEnds, blocks, float.NegativeInfinity, null);
            if (bboxCell.D > bestCell.D) bestCell = bboxCell;

            int numProbes = 2;

            var PotentiallyQueue = (float x, float y, float h, Cell seed) =>
            {
                // a cell is only useful if it can beat the best (d > bestCell.d) or is
                // worth subdividing (max = d + h·√2 > bestCell.d + precision). Both fail
                // once d ≤ threshold, so the distance scan can bail there early.
                float threshold = bestCell.D - Math.Max(0, h * Math_SQRT2 - precision);
                Cell cell = CreateCell(x, y, h, coords, ringEnds, blocks, threshold, seed);
                numProbes++;
                if (cell.Max > bestCell.D + precision) cellQueue.Enqueue(cell);

                // update the best cell if we found a better one
                if (cell.D > bestCell.D)
                {
                    bestCell = cell;
                    if (debug)
                    {
                        Console.WriteLine($"found best {MathF.Round(1e4f * cell.D) / 1e4} after {numProbes} probes");
                    }
                }
            };

            // Cover polygon with initial cells
            float initialH = cellSize / 2.0f;
            for (float x = minX; x < maxX; x += cellSize)
            {
                for (float y = minY; y < maxY; y += cellSize)
                {
                    // PotentiallyQueue<TPolygon, TPoint, TCellQueue>(x + initialH, y + initialH, initialH, polygon, ref numProbes, ref bestCell, cellQueue, precision, debug);

                    PotentiallyQueue(x + initialH, y + initialH, initialH, null);
                }
            }

            // Main queue processing loop
            while (cellQueue.Count > 0)
            {
                // pick the most promising cell from the queue
                Cell cell = cellQueue.Dequeue();

                // Do not drill down further if there's no chance of a better solution
                if (cell.Max - bestCell.D <= precision) break;

                // Split the cell into four child cells
                float h = cell.H / 2.0f;
                // PotentiallyQueue<TPolygon, TPoint, TCellQueue>(cell.X - h, cell.Y - h, h, polygon, ref numProbes, ref bestCell, cellQueue, precision, debug);
                // PotentiallyQueue<TPolygon, TPoint, TCellQueue>(cell.X + h, cell.Y - h, h, polygon, ref numProbes, ref bestCell, cellQueue, precision, debug);
                // PotentiallyQueue<TPolygon, TPoint, TCellQueue>(cell.X - h, cell.Y + h, h, polygon, ref numProbes, ref bestCell, cellQueue, precision, debug);
                // PotentiallyQueue<TPolygon, TPoint, TCellQueue>(cell.X + h, cell.Y + h, h, polygon, ref numProbes, ref bestCell, cellQueue, precision, debug);

                PotentiallyQueue(cell.X - h, cell.Y - h, h, cell);
                PotentiallyQueue(cell.X + h, cell.Y - h, h, cell);
                PotentiallyQueue(cell.X - h, cell.Y + h, h, cell);
                PotentiallyQueue(cell.X + h, cell.Y + h, h, cell);
            }

            if (debug)
            {
                Console.WriteLine($"num probes: {numProbes}\nbest distance: {bestCell.D}");
            }

            return new PolylabelResult(new Point(bestCell.X, bestCell.Y), bestCell.D);
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
        //     where TCellQueue : ICellQueue
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
        //             Console.WriteLine($"found best {MathF.Round(1e4f * cell.D) / 1e4} after {numProbes} probes");
        //         }
        //     }
        // }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Cell CreateCell(float x, float y, float h, float[] coords, int[] ringEnds, float[] blocks, float maxD, Cell seed)
        {
            Cell cell = new Cell();
            cell.X = x; // cell center x
            cell.Y = y; // cell center y
            cell.H = h; // half the cell size
            // nsx1..nsy2 hold the nearest segment found below, so child cells can seed
            // their scan with it (a child is almost always nearest to the same segment)
            cell.NsX1 = 0; cell.NsY1 = 0; cell.NsX2 = 0; cell.NsY2 = 0;
            cell.D = PointToPolygonDist(cell, coords, ringEnds, blocks, maxD, seed); // distance from cell center to polygon
            cell.Max = cell.D + h * Math_SQRT2; // max distance to polygon within a cell  //Math.SQRT2
            return cell;
        }

        // signed distance from cell center to polygon outline (negative if outside),
        // also recording the nearest segment on the cell. maxD is a distance threshold:
        // if a partial result proves the center is no farther than maxD from the outline,
        // the scan bails out early and returns maxD, since the caller has already
        // determined such a cell can't beat the best. seed is the parent cell (or null);
        // its nearest segment is checked first so boundary cells reach the early-out
        // threshold without scanning the whole outline.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float PointToPolygonDist(Cell cell, float[] coords, int[] ringEnds, float[] blocks, float maxD, Cell seed)
        {
            float x = cell.X;
            float y = cell.Y;
            bool inside = false;
            float minDistSq = float.PositiveInfinity;
            float thresholdSq = maxD > 0 ? maxD * maxD : -1;

            if (seed != null)
            {
                cell.NsX1 = seed.NsX1; cell.NsY1 = seed.NsY1; cell.NsX2 = seed.NsX2; cell.NsY2 = seed.NsY2;
                minDistSq = GetSegDistSq(x, y, seed.NsX1, seed.NsY1, seed.NsX2, seed.NsY2);
                if (minDistSq <= thresholdSq) return maxD;
            }

            int stride = K * 2;
            int numRings = ringEnds.Length;
            int g = 0;// running block index into bboxes
            int ringStart = 0;

            for (int r = 0; r < numRings; r++)
            {
                int ringEnd = ringEnds[r];

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
                    float dx = x < bminX ? bminX - x : (x > bmaxX ? x - bmaxX : 0);
                    float dy = y < bminY ? bminY - y : (y > bmaxY ? y - bmaxY : 0);
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
                                // can't possibly contain a better label position — stop scanning
                                if (minDistSq <= thresholdSq) return maxD;
                            }
                        }

                        bx = ax;
                        by = ay;
                    }
                }
                ringStart = ringEnd;
            }

            return minDistSq == 0 ? 0 : (inside ? 1 : -1) * MathF.Sqrt(minDistSq);
        }

        // precompute one bounding box per block of K consecutive edges (over both
        // endpoints of every edge in it) so the distance scan can skip whole blocks in
        // O(1). The block layout mirrors the flattened coords/ringEnds and is re-derived
        // in the scan, so only the bboxes need storing: a flat [minX,minY,maxX,maxY] run
        // per block, sized upfront from the ring lengths.
        private static float[] buildBlocks(float[] coords, int[] ringEnds)
        {
            int stride = K * 2;
            int numBlocks = 0;
            int ringStart = 0;
            for (int r = 0; r < ringEnds.Length; r++)
            {
                numBlocks += (int)MathF.Ceiling(1f * (ringEnds[r] - ringStart) / stride);
                ringStart = ringEnds[r];
            }

            float[] blocks = new float[numBlocks * 4];
            int g = 0;
            ringStart = 0;
            for (int r = 0; r < ringEnds.Length; r++)
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

        // get polygon centroid
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Cell GetCentroidCell(float[] coords, int[] ringEnds, float[] blocks)
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
                area += f * 3.0f;
            }

            if (area == 0)
            {
                return CreateCell(coords[0], coords[1], 0, coords, ringEnds, blocks, float.NegativeInfinity, null);
            }

            float cx = x / area;
            float cy = y / area;
            Cell centroid = CreateCell(cx, cy, 0, coords, ringEnds, blocks, float.NegativeInfinity, null);
            if (centroid.D < 0)
            {
                return CreateCell(coords[0], coords[1], 0, coords, ringEnds, blocks, float.NegativeInfinity, null);
            }

            return centroid;
        }

        // get squared distance from a point to a segment
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float GetSegDistSq(float px, float py, float x, float y, float bx, float by)
        {
            float dx = bx - x;
            float dy = by - y;

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

        //====================================================================

        // [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // private static Cell CreateCell<TPolygon, TPoint>(float x, float y, float h, TPolygon polygon)
        //     where TPolygon : struct, IPolygon<TPoint>
        //     where TPoint : struct, IPoint
        // {
        //     float d = PointToPolygonDist<TPolygon, TPoint>(x, y, polygon);
        //     return new Cell(x, y, h, d);
        // }

        // // signed distance from point to polygon outline (negative if point is outside)
        // [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // private static float PointToPolygonDist<TPolygon, TPoint>(float x, float y, TPolygon polygon)
        //     where TPolygon : struct, IPolygon<TPoint>
        //     where TPoint : struct, IPoint
        // {
        //     bool inside = false;
        //     float minDistSq = float.PositiveInfinity;
        // 
        //     int ringCount = polygon.RingCount;
        //     for (int r = 0; r < ringCount; r++)
        //     {
        //         TPoint[] ring = polygon.GetRing(r);
        //         int len = ring.Length;
        //         if (len == 0) continue;
        // 
        //         TPoint b = ring[len - 1];
        //         for (int i = 0; i < len; i++)
        //         {
        //             TPoint a = ring[i];
        // 
        //             if ((a.Y > y) != (b.Y > y) &&
        //                 (x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X))
        //             {
        //                 inside = !inside;
        //             }
        // 
        //             float distSq = GetSegDistSq(x, y, a.X, a.Y, b.X, b.Y);
        //             // float distSq = GetSegDistSq(x, y, a, b);
        //             if (distSq < minDistSq)
        //             {
        //                 minDistSq = distSq;
        //             }
        // 
        //             b = a;
        //         }
        //     }
        // 
        //     return minDistSq == 0 ? 0 : (inside ? 1 : -1) * MathF.Sqrt(minDistSq);
        // }

        // // get polygon centroid
        // [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // private static Cell GetCentroidCell<TPolygon, TPoint>(TPolygon polygon)
        //     where TPolygon : struct, IPolygon<TPoint>
        //     where TPoint : struct, IPoint
        // {
        //     float area = 0;
        //     float x = 0;
        //     float y = 0;
        //     TPoint[] points = polygon.GetRing(0);
        //     int len = points.Length;
        //     if (len == 0) return new Cell(0, 0, 0, 0);
        // 
        //     TPoint b = points[len - 1];
        //     for (int i = 0; i < len; i++)
        //     {
        //         TPoint a = points[i];
        //         float f = a.X * b.Y - b.X * a.Y;
        //         x += (a.X + b.X) * f;
        //         y += (a.Y + b.Y) * f;
        //         area += f * 3.0f;
        //         b = a;
        //     }
        // 
        //     if (area == 0)
        //     {
        //         TPoint first = points[0];
        //         return CreateCell<TPolygon, TPoint>(first.X, first.Y, 0, polygon);
        //     }
        // 
        //     float cx = x / area;
        //     float cy = y / area;
        //     Cell centroid = CreateCell<TPolygon, TPoint>(cx, cy, 0, polygon);
        //     if (centroid.D < 0)
        //     {
        //         TPoint first = points[0];
        //         return CreateCell<TPolygon, TPoint>(first.X, first.Y, 0, polygon);
        //     }
        // 
        //     return centroid;
        // }

        // // get squared distance from a point to a segment
        // [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // private static float GetSegDistSq<TPoint>(float px, float py, in TPoint a, in TPoint b)
        //     where TPoint : struct, IPoint
        // {
        //     float x = a.X;
        //     float y = a.Y;
        //     float dx = b.X - x;
        //     float dy = b.Y - y;
        // 
        //     if (dx != 0 || dy != 0)
        //     {
        //         float t = ((px - x) * dx + (py - y) * dy) / (dx * dx + dy * dy);
        // 
        //         if (t > 1)
        //         {
        //             x = b.X;
        //             y = b.Y;
        //         }
        //         else if (t > 0)
        //         {
        //             x += dx * t;
        //             y += dy * t;
        //         }
        //     }
        // 
        //     dx = px - x;
        //     dy = py - y;
        // 
        //     return dx * dx + dy * dy;
        // }

    }
}