using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.Helpers
{
    // Broad-phase searches only: callers still perform exact distance/containment tests.
    // Sorted IDs preserve insertion-order tie breaking independently of RTree traversal.
    internal sealed class BoundsIndex : IDisposable
    {
        readonly RTree tree = new RTree();
        readonly List<int> unknown = new List<int>();
        internal void Add(BoundingBox box, int id)
        {
            if (!box.IsValid || !tree.Insert(box, id)) unknown.Add(id);
        }
        internal List<int> Search(BoundingBox box, double tolerance = 0)
        {
            var hits = new List<int>(unknown);
            if (box.IsValid)
            {
                box.Inflate(tolerance);
                tree.Search(box, (_, e) => hits.Add(e.Id));
            }
            hits.Sort();
            return hits;
        }
        internal void SearchSphere(Point3d point, double radius, Action<RTreeEventArgs> visit) =>
            tree.Search(new Sphere(point, radius), (_, e) => visit(e));
        public void Dispose() => tree.Dispose();
    }

    internal sealed class PointIndex : IDisposable
    {
        readonly BoundsIndex index = new BoundsIndex();
        internal List<Point3d> Points { get; } = new List<Point3d>();
        internal PointIndex(IEnumerable<Point3d> points = null)
        {
            if (points != null) foreach (var p in points) Add(p);
        }
        internal int Add(Point3d point)
        {
            int id = Points.Count;
            Points.Add(point);
            index.Add(new BoundingBox(point, point), id);
            return id;
        }
        internal List<int> Search(Point3d point, double tolerance) => index.Search(new BoundingBox(point, point), tolerance);
        internal List<int> Search(BoundingBox box, double tolerance) => index.Search(box, tolerance);
        internal int Nearest(Point3d point, double tolerance, out double distance)
        {
            int best = -1; double bestDistance = tolerance;
            void Consider(int id)
            {
                double d = point.DistanceTo(Points[id]);
                if (d <= tolerance && (best < 0 || d < bestDistance || (d == bestDistance && id < best))) { best = id; bestDistance = d; }
            }
            if (tolerance == 0) foreach (int id in Search(point, tolerance)) Consider(id);
            else index.SearchSphere(point, tolerance, e =>
            {
                Consider(e.Id);
                if (best >= 0) e.SearchSphere = new Sphere(point, Math.Min(tolerance, Math.Max(1E-12, Math.BitIncrement(bestDistance))));
            });
            distance = bestDistance;
            return best;
        }
        public void Dispose() => index.Dispose();
    }

    internal sealed class BoundaryIndex : IDisposable
    {
        internal PointIndex Vertices { get; } = new PointIndex();
        readonly BoundsIndex edges = new BoundsIndex();
        readonly List<Line> lines = new List<Line>();
        internal BoundaryIndex(IEnumerable<Polyline> loops = null)
        {
            if (loops != null) Add(loops);
        }
        internal void Add(IEnumerable<Polyline> loops)
        {
            foreach (var loop in loops)
            {
                foreach (var point in loop) Vertices.Add(point);
                foreach (var line in loop.GetSegments()) { edges.Add(line.BoundingBox, lines.Count); lines.Add(line); }
            }
        }
        internal bool OnBoundary(Point3d point, double tolerance)
        {
            foreach (int id in edges.Search(new BoundingBox(point, point), tolerance))
                if (lines[id].DistanceTo(point, true) <= tolerance) return true;
            return false;
        }
        internal Point3d Nearest(Point3d point, double tolerance, Plane plane, double epsilon)
        {
            var best = point; double distance = tolerance; bool found = false;
            foreach (int id in Vertices.Search(point, tolerance))
            {
                var p = Vertices.Points[id]; double d = point.DistanceTo(p);
                if (Math.Abs(plane.DistanceTo(p)) <= epsilon && d <= distance) { best = p; distance = d; found = true; }
            }
            if (found) return best;
            foreach (int id in edges.Search(new BoundingBox(point, point), tolerance))
            {
                var p = lines[id].ClosestPoint(point, true); double d = point.DistanceTo(p);
                if (Math.Abs(plane.DistanceTo(p)) <= epsilon && d <= distance) { best = p; distance = d; }
            }
            return best;
        }
        public void Dispose() { Vertices.Dispose(); edges.Dispose(); }
    }
}
