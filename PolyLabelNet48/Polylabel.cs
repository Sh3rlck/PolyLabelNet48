// Ported to .NET Framework 4.8 / C# 7.3 from https://github.com/oberbichler/PolylabelNet
// Original algorithm: Mapbox Polylabel (Copyright (c) 2016 Mapbox). PolylabelNet by Thomas Oberbichler.
// Licensed under the ISC License.

using System;
using System.Runtime.CompilerServices;

namespace PolyLabelNet
{
    /// <summary>
    /// Provides methods for finding the pole of inaccessibility of a polygon.
    /// </summary>
    public static class Polylabel
    {
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

            // Take centroid as the first best guess
            Cell bestCell = GetCentroidCell<TPolygon, TPoint>(polygon);

            // Second guess: bounding box centroid
            Cell bboxCell = CreateCell<TPolygon, TPoint>(minX + width / 2.0f, minY + height / 2.0f, 0, polygon);
            if (bboxCell.D > bestCell.D) bestCell = bboxCell;

            int numProbes = 2;

            var PotentiallyQueue = (float x, float y, float h, TPolygon polygon) =>
            {
                Cell cell = CreateCell<TPolygon, TPoint>(x, y, h, polygon);
                numProbes++;
                if (cell.Max > bestCell.D + precision)
                {
                    cellQueue.Enqueue(cell);
                }

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

                    PotentiallyQueue(x + initialH, y + initialH, initialH, polygon);
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

                PotentiallyQueue(cell.X - h, cell.Y - h, h, polygon);
                PotentiallyQueue(cell.X + h, cell.Y - h, h, polygon);
                PotentiallyQueue(cell.X - h, cell.Y + h, h, polygon);
                PotentiallyQueue(cell.X + h, cell.Y + h, h, polygon);
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
        private static Cell CreateCell<TPolygon, TPoint>(float x, float y, float h, TPolygon polygon)
            where TPolygon : struct, IPolygon<TPoint>
            where TPoint : struct, IPoint
        {
            float d = PointToPolygonDist<TPolygon, TPoint>(x, y, polygon);
            return new Cell(x, y, h, d);
        }

        // signed distance from point to polygon outline (negative if point is outside)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float PointToPolygonDist<TPolygon, TPoint>(float x, float y, TPolygon polygon)
            where TPolygon : struct, IPolygon<TPoint>
            where TPoint : struct, IPoint
        {
            bool inside = false;
            float minDistSq = float.PositiveInfinity;

            int ringCount = polygon.RingCount;
            for (int r = 0; r < ringCount; r++)
            {
                TPoint[] ring = polygon.GetRing(r);
                int len = ring.Length;
                if (len == 0) continue;

                TPoint b = ring[len - 1];
                for (int i = 0; i < len; i++)
                {
                    TPoint a = ring[i];

                    if ((a.Y > y) != (b.Y > y) &&
                        (x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X))
                    {
                        inside = !inside;
                    }

                    float distSq = GetSegDistSq(x, y, a, b);
                    if (distSq < minDistSq)
                    {
                        minDistSq = distSq;
                    }

                    b = a;
                }
            }

            return minDistSq == 0 ? 0 : (inside ? 1 : -1) * MathF.Sqrt(minDistSq);
        }

        // get polygon centroid
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Cell GetCentroidCell<TPolygon, TPoint>(TPolygon polygon)
            where TPolygon : struct, IPolygon<TPoint>
            where TPoint : struct, IPoint
        {
            float area = 0;
            float x = 0;
            float y = 0;
            TPoint[] points = polygon.GetRing(0);
            int len = points.Length;
            if (len == 0) return new Cell(0, 0, 0, 0);

            TPoint b = points[len - 1];
            for (int i = 0; i < len; i++)
            {
                TPoint a = points[i];
                float f = a.X * b.Y - b.X * a.Y;
                x += (a.X + b.X) * f;
                y += (a.Y + b.Y) * f;
                area += f * 3.0f;
                b = a;
            }

            if (area == 0)
            {
                TPoint first = points[0];
                return CreateCell<TPolygon, TPoint>(first.X, first.Y, 0, polygon);
            }

            float cx = x / area;
            float cy = y / area;
            Cell centroid = CreateCell<TPolygon, TPoint>(cx, cy, 0, polygon);
            if (centroid.D < 0)
            {
                TPoint first = points[0];
                return CreateCell<TPolygon, TPoint>(first.X, first.Y, 0, polygon);
            }

            return centroid;
        }

        // get squared distance from a point to a segment
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float GetSegDistSq<TPoint>(float px, float py, in TPoint a, in TPoint b)
            where TPoint : struct, IPoint
        {
            float x = a.X;
            float y = a.Y;
            float dx = b.X - x;
            float dy = b.Y - y;

            if (dx != 0 || dy != 0)
            {
                float t = ((px - x) * dx + (py - y) * dy) / (dx * dx + dy * dy);

                if (t > 1)
                {
                    x = b.X;
                    y = b.Y;
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
}