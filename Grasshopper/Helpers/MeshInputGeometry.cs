using System;
using System.Collections.Generic;
using System.Linq;
using Ant.GrasshopperPlugIn.DataTypes;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace Ant.GrasshopperPlugIn.Helpers
{
    internal static class MeshInputGeometry
    {
        internal static bool TryProjectLine(Brep target, Line line, double tolerance, out Line projected)
        {
            projected = Line.Unset;
            if (!line.IsValid || !target.ClosestPoint(line.From, out Point3d a, out _, out _, out _, tolerance, out _) ||
                !target.ClosestPoint(line.To, out Point3d b, out _, out _, out _, tolerance, out _) ||
                a.DistanceTo(line.From) > tolerance || b.DistanceTo(line.To) > tolerance) return false;
            var candidate = new Line(a, b);
            if (!SegmentOnFace(target, candidate, tolerance)) return false;
            projected = candidate; return true;
        }

        // Endpoints alone do not establish containment on trimmed or concave surfaces.
        private static bool SegmentOnFace(Brep target, Line line, double tolerance)
        {
            if (!line.IsValid) return false;
            double epsilon = Math.Max(1E-9, Math.Min(1E-7, tolerance * 1E-3));
            using var curve = new LineCurve(line);
            var cuts = new List<double> { 0, 1 };
            foreach (var loop in target.Loops)
            {
                using var boundary = loop.To3dCurve();
                var events = Intersection.CurveCurve(curve, boundary, epsilon, epsilon);
                if (events == null) continue;
                foreach (var hit in events)
                {
                    cuts.Add(curve.Domain.NormalizedParameterAt(hit.ParameterA));
                    if (hit.IsOverlap) cuts.Add(curve.Domain.NormalizedParameterAt(hit.OverlapA.T1));
                }
            }
            cuts.Sort();
            for (int i = 1; i < cuts.Count; i++)
            {
                if (cuts[i] - cuts[i - 1] <= 1E-12) continue;
                var point = line.PointAt((cuts[i] + cuts[i - 1]) / 2);
                if (!target.Faces.Any(f => f.ClosestPoint(point, out double u, out double v) &&
                    f.PointAt(u, v).DistanceTo(point) <= epsilon && f.IsPointOnFace(u, v, epsilon) != PointFaceRelation.Exterior)) return false;
            }
            return true;
        }

        internal static bool TryProjectRegion(Brep target, Brep region, double tolerance, double curveTolerance,
            out List<GPC.Geometry.Shape> shapes)
        {
            shapes = new List<GPC.Geometry.Shape>();
            if (region == null || !region.IsValid) return false;
            var staged = new List<GPC.Geometry.Shape>();
            foreach (var face in region.Faces)
            {
                if (!face.IsPlanar(1E-4)) return false;
                GPC.Geometry.Polygon3d outer = null;
                var holes = new List<GPC.Geometry.Polygon3d>();
                var projectedLoops = new List<Curve>();
                try
                {
                    foreach (var loop in face.Loops)
                    {
                        using var curve = loop.To3dCurve();
                        var poly = PlanarGeometryCleanup.Approximate(curve, curveTolerance);
                        var projected = new Polyline();
                        var polygon = new GPC.Geometry.Polygon3d();
                        for (int i = 0; i < poly.Count - 1; i++)
                        {
                            if (!target.ClosestPoint(poly[i], out Point3d p, out _, out _, out _, tolerance, out _) ||
                                p.DistanceTo(poly[i]) > tolerance) return false;
                            polygon.Add(p.X, p.Y, p.Z); projected.Add(p);
                        }
                        if (projected.Count < 3) return false;
                        projected.Add(projected[0]);
                        if (projected.GetSegments().Any(l => !SegmentOnFace(target, l, tolerance))) return false;
                        projectedLoops.Add(new PolylineCurve(projected));
                        if (loop.LoopType == BrepLoopType.Outer) outer = polygon; else holes.Add(polygon);
                    }
                    if (outer == null || outer.Count < 3 || !RegionOnFace(target, projectedLoops, tolerance)) return false;
                    staged.Add(new GPC.Geometry.Shape(outer, holes.Count == 0 ? null : holes.ToArray()));
                }
                finally { foreach (var loop in projectedLoops) loop.Dispose(); }
            }
            shapes = staged;
            return shapes.Count > 0;
        }

        private static bool RegionOnFace(Brep target, List<Curve> loops, double tolerance)
        {
            double epsilon = Math.Max(1E-9, Math.Min(1E-7, tolerance * 1E-3));
            if (target.Faces.Count != 1 || !target.Faces[0].TryGetPlane(out Plane plane, 1E-4)) return false;
            var regions = Brep.CreatePlanarBreps(loops, epsilon);
            if (regions == null) return false;
            try
            {
                if (regions.Length != 1 || regions[0].Loops.Count != loops.Count) return false;
                using var area = AreaMassProperties.Compute(regions[0]);
                if (area == null || area.Area <= 0) return false;
                var intersection = Brep.CreatePlanarIntersection(regions[0], target, plane, epsilon);
                if (intersection == null || intersection.Length == 0) return false;
                double retainedArea = 0;
                try
                {
                    foreach (var piece in intersection) { using var part = AreaMassProperties.Compute(piece); retainedArea += part?.Area ?? 0; }
                }
                finally { foreach (var piece in intersection) piece.Dispose(); }
                return Math.Abs(area.Area - retainedArea) <= Math.Max(epsilon * epsilon, area.Area * 1E-12);
            }
            finally { foreach (var region in regions) region.Dispose(); }
        }

        internal static bool TryProjectRefinement(Brep target, IGH_GeometricGoo refinement, double tolerance,
            double curveTolerance, double size, out List<GPC.Geometry.GeometryBase> geometries, out double localSize)
        {
            geometries = new List<GPC.Geometry.GeometryBase>();
            localSize = 0;
            Point3d ConvertPoint(Point3d point)
            {
                return target.ClosestPoint(point, out Point3d p, out _, out _, out _, tolerance, out _) && p.DistanceTo(point) <= tolerance ? p : Point3d.Unset;
            }
            GPC.Geometry.Point3d Gpc(Point3d point) => new GPC.Geometry.Point3d(point.X, point.Y, point.Z);
            switch (refinement)
            {
                case MeshPointRefinement point:
                    localSize = point.Size;
                    var p = ConvertPoint(point.Geometry);
                    if (!p.IsValid) return false;
                    geometries.Add(Gpc(p)); break;
                case MeshLineRefinement line:
                    localSize = line.Size;
                    if (!TryProjectLine(target, line.Geometry, tolerance, out var segment)) return false;
                    geometries.Add(new GPC.Geometry.Line3d(Gpc(segment.From), Gpc(segment.To))); break;
                case MeshSurfaceRefinement surface:
                    localSize = surface.Size;
                    if (!TryProjectRegion(target, surface.Geometry, tolerance, curveTolerance, out var regions)) return false;
                    geometries.AddRange(regions); break;
                case MeshPolylineRefinement poly:
                    localSize = poly.Size;
                    var projected = poly.Geometry.Select(ConvertPoint).ToList();
                    if (projected.Any(pt => !pt.IsValid)) return false;
                    for (int i = 1; i < projected.Count; i++)
                        if (!SegmentOnFace(target, new Line(projected[i - 1], projected[i]), tolerance)) return false;
                    if (poly.Geometry.IsClosed)
                    {
                        using var boundary = new PolylineCurve(projected);
                        if (!RegionOnFace(target, new List<Curve> { boundary }, tolerance)) return false;
                        geometries.Add(new GPC.Geometry.Polygon3d(projected.Take(projected.Count - 1).Select(Gpc).ToArray()));
                    }
                    else
                        for (int i = 1; i < projected.Count; i++) geometries.Add(new GPC.Geometry.Line3d(Gpc(projected[i - 1]), Gpc(projected[i])));
                    break;
                case MeshPolylineCurveRefinement curve:
                    localSize = curve.Size;
                    if (!TryProjectCurve(target, curve.Geometry, tolerance, curveTolerance, size, out var segments)) return false;
                    geometries.AddRange(segments.Select(l => new GPC.Geometry.Line3d(Gpc(l.From), Gpc(l.To)))); break;
                default: return false;
            }
            if (!double.IsFinite(localSize) || localSize <= 0) throw new ArgumentException("Refinement size must be finite and positive.");
            return geometries.Count > 0;
        }

        // The caller owns the duplicates, including those created before a failure.
        internal static void SplitFaces(IEnumerable<Brep> surfaces, List<Brep> faces)
        {
            int index = 0;
            foreach (var brep in surfaces)
            {
                if (brep == null || !brep.IsValid || brep.Faces.Count == 0)
                    throw new ArgumentException($"Surface {index} is null or invalid.");

                foreach (var face in brep.Faces)
                {
                    if (!face.IsPlanar(1E-4))
                        throw new ArgumentException($"Surface {index}, face {face.FaceIndex} is not planar.");
                    var copy = face.DuplicateFace(false);
                    if (copy == null)
                        throw new InvalidOperationException($"Cannot duplicate surface {index}, face {face.FaceIndex}.");
                    faces.Add(copy);
                }
                index++;
            }
        }

        internal static bool TryProjectCurve(Brep surface, Curve curve, double embedTolerance,
            double curveTolerance, double size, out List<Line> segments)
        {
            segments = new List<Line>();
            if (curve == null || !curve.IsValid)
                throw new ArgumentException("An embedded curve is null or invalid.");

            if (!curve.TryGetPolyline(out Polyline polyline))
            {
                using var approximation = Components.MeshComponent.ToPolylineCurve(curve, curveTolerance, size);
                if (approximation == null || !approximation.TryGetPolyline(out polyline))
                    throw new ArgumentException("Cannot convert an embedded curve to a polyline.");
            }

            // Stage every projected point before committing any segment to the model.
            var projected = new Point3d[polyline.Count];
            for (int i = 0; i < polyline.Count; i++)
            {
                if (!surface.ClosestPoint(polyline[i], out projected[i], out _, out _, out _, embedTolerance, out _) ||
                    projected[i].DistanceTo(polyline[i]) > embedTolerance)
                    return false;
            }
            for (int i = 1; i < projected.Length; i++)
            {
                var segment = new Line(projected[i - 1], projected[i]);
                if (!SegmentOnFace(surface, segment, embedTolerance))
                {
                    segments.Clear();
                    return false;
                }
                segments.Add(segment);
            }
            return segments.Count > 0;
        }
    }
}
