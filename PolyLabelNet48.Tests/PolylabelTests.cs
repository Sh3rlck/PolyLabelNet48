using System;
using System.IO;
using System.Text.Json;
using Xunit;
using PolyLabelNet;

namespace PolyLabelNet.Tests
{
    public class PolylabelTests
    {
        private static Polygon LoadFixture(string filename)
        {
            string fullPath = Path.Combine(AppContext.BaseDirectory, "fixtures", filename);
            string json = File.ReadAllText(fullPath);
            float[][][] coords = JsonSerializer.Deserialize<float[][][]>(json)
                ?? throw new Exception($"Failed to deserialize {filename}");
            return new Polygon(coords);
        }

        [Fact]
        public void FindsPoleOfInaccessibilityForWater1AndPrecision1()
        {
            var water1 = LoadFixture("water1.json");
            var (point, distance) = Polylabel.Run(water1, 1.0f);

            Assert.Equal(3865.85009765625, point.X);
            Assert.Equal(2124.87841796875, point.Y);
            Assert.Equal(288.8493574779127, distance, precision: 12);
        }

        [Fact]
        public void FindsPoleOfInaccessibilityForWater1AndPrecision50()
        {
            var water1 = LoadFixture("water1.json");
            var (point, distance) = Polylabel.Run(water1, 50.0f);

            Assert.Equal(3854.296875, point.X);
            Assert.Equal(2123.828125, point.Y);
            Assert.Equal(278.5795872381558, distance, precision: 12);
        }

        [Fact]
        public void FindsPoleOfInaccessibilityForWater2AndDefaultPrecision1()
        {
            var water2 = LoadFixture("water2.json");
            var (point, distance) = Polylabel.Run(water2, 1.0f);

            Assert.Equal(3263.5, point.X);
            Assert.Equal(3263.5, point.Y);
            Assert.Equal(960.5, distance, precision: 12);
        }

        [Fact]
        public void WorksOnDegeneratePolygons()
        {
            var p1Coords = new float[][][] { new float[][] { new float[] { 0, 0 }, new float[] { 1, 0 }, new float[] { 2, 0 }, new float[] { 0, 0 } } };
            var polygon1 = new Polygon(p1Coords);
            var (point1, distance1) = Polylabel.Run(polygon1);

            Assert.Equal(0, point1.X);
            Assert.Equal(0, point1.Y);
            Assert.Equal(0, distance1);

            var p2Coords = new float[][][] { new float[][] { new float[] { 0, 0 }, new float[] { 1, 0 }, new float[] { 1, 1 }, new float[] { 1, 0 }, new float[] { 0, 0 } } };
            var polygon2 = new Polygon(p2Coords);
            var (point2, distance2) = Polylabel.Run(polygon2);

            Assert.Equal(0, point2.X);
            Assert.Equal(0, point2.Y);
            Assert.Equal(0, distance2);
        }

        [Fact]
        public void ReturnsZeroForDefaultPolygonStruct()
        {
            Polygon polygon = default;
            var (point, distance) = Polylabel.Run(polygon);

            Assert.Equal(0, point.X);
            Assert.Equal(0, point.Y);
            Assert.Equal(0, distance);
        }

        [Fact]
        public void ReturnsZeroForDefaultGenericPolygonStruct()
        {
            Polygon<Point> polygon = default;
            var (point, distance) = Polylabel.Run(polygon);

            Assert.Equal(0, point.X);
            Assert.Equal(0, point.Y);
            Assert.Equal(0, distance);
        }

        [Fact]
        public void ReturnsZeroForEmptyPolygon()
        {
            var polygon = new Polygon(Array.Empty<Point[]>());
            var (point, distance) = Polylabel.Run(polygon);

            Assert.Equal(0, point.X);
            Assert.Equal(0, point.Y);
            Assert.Equal(0, distance);
        }

        [Fact]
        public void ReturnsZeroForPolygonWithEmptyOuterRing()
        {
            var polygon = new Polygon(new Point[][] { Array.Empty<Point>() });
            var (point, distance) = Polylabel.Run(polygon);

            Assert.Equal(0, point.X);
            Assert.Equal(0, point.Y);
            Assert.Equal(0, distance);
        }

        [Fact]
        public void ReturnsZeroForSinglePointPolygon()
        {
            var coords = new float[][][] { new float[][] { new float[] { 5, 7 } } };
            var polygon = new Polygon(coords);
            var (point, distance) = Polylabel.Run(polygon);

            Assert.Equal(5, point.X);
            Assert.Equal(7, point.Y);
            Assert.Equal(0, distance);
        }

        private readonly struct CustomVector2 : IPoint
        {
            public float X => XCoord;
            public float Y => YCoord;

            public float XCoord { get; }
            public float YCoord { get; }

            public CustomVector2(float x, float y)
            {
                XCoord = x;
                YCoord = y;
            }
        }

        [Fact]
        public void WorksWithCustomPointType()
        {
            var rings = new CustomVector2[][]
            {
                new CustomVector2[]
                {
                    new CustomVector2(0, 0),
                    new CustomVector2(10, 0),
                    new CustomVector2(10, 10),
                    new CustomVector2(0, 10),
                    new CustomVector2(0, 0)
                }
            };

            var polygon = new Polygon<CustomVector2>(rings);
            var (point, distance) = Polylabel.Run(polygon, 1.0f);

            Assert.Equal(5.0, point.X);
            Assert.Equal(5.0, point.Y);
            Assert.Equal(5.0, distance);
        }

        private readonly struct Vector2Adapter : IPoint
        {
            private readonly System.Numerics.Vector2 _vector;

            public float X => _vector.X;
            public float Y => _vector.Y;

            public Vector2Adapter(System.Numerics.Vector2 vector) => _vector = vector;
        }

        [Fact]
        public void WorksWithExternalVector2()
        {
            var rings = new System.Numerics.Vector2[][]
            {
                new System.Numerics.Vector2[]
                {
                    new System.Numerics.Vector2(0, 0),
                    new System.Numerics.Vector2(10, 0),
                    new System.Numerics.Vector2(10, 10),
                    new System.Numerics.Vector2(0, 10),
                    new System.Numerics.Vector2(0, 0)
                }
            };

            var wrappedRings = Array.ConvertAll(rings,
                ring => Array.ConvertAll(ring, v => new Vector2Adapter(v)));

            var polygon = new Polygon<Vector2Adapter>(wrappedRings);
            var (point, distance) = Polylabel.Run(polygon, 1.0f);

            Assert.Equal(5.0, point.X);
            Assert.Equal(5.0, point.Y);
            Assert.Equal(5.0, distance);
        }

        private readonly struct CustomPolygon : IPolygon<Point>
        {
            private readonly Point[] _outerRing;

            public int RingCount => 1;

            public Point[] GetRing(int index) => index == 0 ? _outerRing : Array.Empty<Point>();

            public CustomPolygon(Point[] outerRing) => _outerRing = outerRing;
        }

        [Fact]
        public void WorksWithCustomPolygonType()
        {
            var outerRing = new Point[]
            {
                new Point(0, 0),
                new Point(10, 0),
                new Point(10, 10),
                new Point(0, 10),
                new Point(0, 0)
            };

            var polygon = new CustomPolygon(outerRing);
            var (point, distance) = Polylabel.Run<CustomPolygon, Point>(polygon, 1.0f);

            Assert.Equal(5.0, point.X);
            Assert.Equal(5.0, point.Y);
            Assert.Equal(5.0, distance);
        }
    }
}
