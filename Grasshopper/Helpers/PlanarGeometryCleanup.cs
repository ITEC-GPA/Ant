using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Ant.GrasshopperPlugIn.DataTypes;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace Ant.GrasshopperPlugIn.Helpers
{
    // All distances are in model units. Geometry supplied by the caller is never edited.
    internal static class PlanarGeometryCleanup
    {
        internal sealed class Result
        {
            public List<Brep> Surfaces { get; } = new List<Brep>();
            public List<IGH_GeometricGoo> Embedded { get; } = new List<IGH_GeometricGoo>();
            public List<IGH_GeometricGoo> Refinements { get; } = new List<IGH_GeometricGoo>();
            public List<string> Log { get; } = new List<string>();
            public List<string> Warnings { get; } = new List<string>();
            public CleanupDiagnostics Diagnostics { get; } = new CleanupDiagnostics();
            public void Warn(string message) { Warnings.Add(message); Log.Add("WARNING: " + message); }
        }

        sealed class Face : IDisposable
        {
            public Plane Plane;
            public List<Polyline> Loops = new List<Polyline>();
            public Brep Brep;
            public string Label;
            public bool Changed;
            public void Dispose() => Brep?.Dispose();
        }

        sealed class Constraint
        {
            public Point3d? Point;
            public Line? Line;
            public Brep Region;
            public double? Size;
            public Face Face;
            public string Source;
            public bool Changed;
        }

        internal static Result Clean(IList<Brep> surfaces, IList<IGH_GeometricGoo> embedded,
            IList<IGH_GeometricGoo> refinements, double mergeTolerance, double projectionTolerance,
            double curveTolerance, bool unionCoplanar)
        {
            Validate(mergeTolerance, nameof(mergeTolerance));
            Validate(projectionTolerance, nameof(projectionTolerance));
            Validate(curveTolerance, nameof(curveTolerance));
            if (surfaces == null || surfaces.Count == 0) throw new ArgumentException("No surfaces to clean.");
            var clock = Stopwatch.StartNew();
            var result = new Result();
            var faces = new List<Face>();
            var constraints = new List<Constraint>();
            // Numerical tests must not use the much larger user snapping distance: that fills holes.
            double epsilon = Math.Max(1E-9, Math.Min(1E-6, Math.Min(mergeTolerance, curveTolerance) * 1E-3));
            int moved = 0, removed = 0, duplicateFaces = 0;
            GeometryBase activeGeometry = null;
            string activeLabel = "Surfaces";
            try
            {
                for (int i = 0; i < surfaces.Count; i++)
                {
                    var brep = surfaces[i];
                    activeGeometry = brep; activeLabel = $"S[{i}]";
                    if (brep == null || !brep.IsValid || brep.Faces.Count == 0)
                        throw new ArgumentException($"S[{i}] is null or invalid. No partial surface set was returned.");
                    foreach (var source in brep.Faces)
                    {
                        string label = $"S[{i}], face {source.FaceIndex}";
                        activeLabel = label;
                        if (!source.TryGetPlane(out Plane plane, 1E-6))
                            throw new ArgumentException(label + " is not planar.");
                        var face = new Face { Plane = plane, Label = label };
                        faces.Add(face); // Owned even if a later operation fails.
                        foreach (var loop in source.Loops)
                        {
                            using var curve = loop.To3dCurve();
                            if (!curve.TryGetPolyline(out Polyline _)) face.Changed = true;
                            var poly = Approximate(curve, curveTolerance);
                            for (int p = 0; p < poly.Count; p++)
                            {
                                var projected = plane.ClosestPoint(poly[p]);
                                if (poly[p].DistanceTo(projected) > epsilon) face.Changed = true;
                                poly[p] = projected;
                            }
                            face.Loops.Add(poly);
                        }
                        // Only parallel planes are aligned, against a fixed earlier representative.
                        var anchor = faces.Take(faces.Count - 1).FirstOrDefault(f =>
                            Math.Abs(f.Plane.Normal * plane.Normal) > 1 - 1E-12 &&
                            face.Loops.SelectMany(l => l).All(p => Math.Abs(f.Plane.DistanceTo(p)) <= projectionTolerance));
                        if (anchor != null) face.Plane = anchor.Plane;
                        foreach (var loop in face.Loops)
                        {
                            for (int p = 0; p < loop.Count; p++)
                            {
                                var point = face.Plane.ClosestPoint(loop[p]);
                                // Snap only to earlier faces. Never snap a hole onto its own outer loop.
                                var snap = NearestBoundary(point, faces.Take(faces.Count - 1), mergeTolerance, face.Plane, epsilon);
                                if (loop[p].DistanceTo(snap) > epsilon) { moved++; face.Changed = true; }
                                loop[p] = snap;
                            }
                            int previousCount = loop.Count;
                            RemoveShortSegments(loop, mergeTolerance, ref removed, result.Diagnostics, label);
                            if (loop.Count != previousCount) face.Changed = true;
                        }
                        Rebuild(face, epsilon);
                        if (faces.Take(faces.Count - 1).Any(f => SameRegion(f.Brep, face.Brep, epsilon)))
                        {
                            using var original = source.DuplicateFace(false);
                            result.Diagnostics.Add(CleanupDiagnosticKind.Discarded, original, label, "Duplicate planar face after matching");
                            faces.RemoveAt(faces.Count - 1); face.Dispose(); duplicateFaces++;
                        }
                    }
                }

                // Insert T-junctions in both boundaries, even when their vertex subdivisions differ.
                var vertices = faces.SelectMany(f => f.Loops).SelectMany(l => l).ToArray();
                foreach (var face in faces)
                {
                    activeGeometry = face.Brep; activeLabel = face.Label;
                    for (int l = 0; l < face.Loops.Count; l++)
                    {
                        var split = SplitBoundary(face.Loops[l], vertices, epsilon);
                        if (split.Count != face.Loops[l].Count) face.Changed = true;
                        face.Loops[l] = split;
                    }
                    Rebuild(face, epsilon);
                    activeGeometry = face.Brep;
                }
                if (unionCoplanar) UnionFaces(faces, epsilon, result);
                activeGeometry = null; activeLabel = "Constraint cleanup";

                void ReadInputs(IList<IGH_GeometricGoo> inputs, bool refinement)
                {
                    if (inputs == null) return;
                    for (int i = 0; i < inputs.Count; i++)
                    {
                        string label = (refinement ? "R" : "E") + $"[{i}]";
                        var item = inputs[i];
                        int before = constraints.Count;
                        double? size = null;
                        Point3d? point = null;
                        Curve curve = null;
                        Brep region = null;
                        bool ownsCurve = false, ownsRegion = false;
                        var discarded = new List<(GeometryBase geometry, string reason)>();
                        try
                        {
                            switch (item)
                            {
                                case MeshPointRefinement p: point = p.Geometry; size = p.Size; break;
                                case MeshLineRefinement l: curve = new LineCurve(l.Geometry); ownsCurve = true; size = l.Size; break;
                                case MeshPolylineRefinement p:
                                    curve = new PolylineCurve(p.Geometry); ownsCurve = true; size = p.Size;
                                    if (curve.IsClosed)
                                    {
                                        var regions = Brep.CreatePlanarBreps(curve, epsilon);
                                        if (regions == null || regions.Length != 1) { DisposeAll(regions); throw new ArgumentException("Invalid closed refinement region."); }
                                        region = regions[0]; ownsRegion = true;
                                    }
                                    break;
                                case MeshPolylineCurveRefinement p: curve = p.Geometry; size = p.Size; break;
                                case MeshSurfaceRefinement s: region = s.Geometry; size = s.Size; break;
                                case GH_Point p when !refinement: point = p.Value; break;
                                case GH_Line l when !refinement: curve = new LineCurve(l.Value); ownsCurve = true; break;
                                case GH_Curve c when !refinement: curve = c.Value; break;
                                case GH_Brep b when !refinement: region = b.Value; break;
                                case GH_Surface s when !refinement: region = s.Value; break;
                                default: throw new ArgumentException(refinement ? "Expected a Mesh Refinement object." : "Supported geometry: points, lines, curves and planar Breps.");
                            }
                            if (refinement && (!size.HasValue || !double.IsFinite(size.Value) || size.Value <= 0))
                                throw new ArgumentException("Refinement size must be finite and positive.");
                            if (!refinement) size = null;
                            if (point.HasValue)
                            {
                                var candidates = faces.Select(f => (face: f, point: ProjectPoint(point.Value, f, projectionTolerance, epsilon)))
                                    .Where(c => c.point.HasValue).OrderBy(c => c.point.Value.DistanceTo(point.Value)).ToList();
                                if (candidates.Count > 0) constraints.Add(new Constraint { Point = candidates[0].point, Face = candidates[0].face, Size = size,
                                    Changed = point.Value.DistanceTo(candidates[0].point.Value) > epsilon });
                            }
                            else if (region != null)
                            {
                                AddRegion(region, size, faces, constraints, projectionTolerance, curveTolerance, epsilon);
                                var parts = constraints.Skip(before).ToList();
                                bool changedRegion = parts.Count != 1 || !LinearRegion(region) || !SameRegion(region, parts[0].Region, epsilon);
                                foreach (var part in parts) part.Changed = changedRegion;
                                using var originalArea = AreaMassProperties.Compute(region);
                                double retainedArea = 0;
                                foreach (var part in constraints.Skip(before))
                                { using var area = AreaMassProperties.Compute(part.Region); retainedArea += area?.Area ?? 0; }
                                if (originalArea != null && originalArea.Area - retainedArea > Math.Max(epsilon * epsilon, originalArea.Area * 1E-8))
                                {
                                    result.Warn($"{label}: planar region clipped; excluded area {originalArea.Area - retainedArea:G6} in squared model units.");
                                    if (parts.Count > 0)
                                    {
                                        try
                                        {
                                            foreach (var piece in RegionRemainder(region, parts, projectionTolerance, epsilon)) discarded.Add((piece, "Region portion excluded by clipping"));
                                        }
                                        catch (ArgumentException e)
                                        {
                                            result.Warn(label + ": " + e.Message + " Cleaned output is retained.");
                                            result.Diagnostics.Add(CleanupDiagnosticKind.Unresolved, region, label, "Diagnostic difference unavailable; cleaned output is retained");
                                        }
                                    }
                                }
                            }
                            else
                            {
                                var poly = Approximate(curve, curveTolerance);
                                bool approximated = !curve.TryGetPolyline(out Polyline _);
                                double total = 0, kept = 0;
                                foreach (var segment in poly.GetSegments())
                                {
                                    if (segment.Length <= mergeTolerance)
                                    {
                                        removed++;
                                        discarded.Add((segment.IsValid ? (GeometryBase)new LineCurve(segment) : new Rhino.Geometry.Point(segment.From), "Short/degenerate input segment"));
                                        continue;
                                    }
                                    total += segment.Length;
                                    // Partition one segment across faces and holes. All retained pieces lie on a face.
                                    var covered = new List<Interval>();
                                    foreach (var face in faces.OrderBy(f => Math.Abs(f.Plane.DistanceTo(segment.PointAt(0.5)))))
                                    {
                                        if (Math.Abs(face.Plane.DistanceTo(segment.From)) > projectionTolerance ||
                                            Math.Abs(face.Plane.DistanceTo(segment.To)) > projectionTolerance) continue;
                                        var a = ProjectAndSnap(segment.From, face, projectionTolerance, epsilon);
                                        var b = ProjectAndSnap(segment.To, face, projectionTolerance, epsilon);
                                        var projected = new Line(a, b);
                                        if (!projected.IsValid || projected.Length <= mergeTolerance) continue;
                                        foreach (var range in Clip(projected, face, epsilon))
                                        {
                                            foreach (var remaining in Subtract(range, covered))
                                            {
                                                var piece = new Line(projected.PointAt(remaining.T0), projected.PointAt(remaining.T1));
                                                if (piece.Length <= mergeTolerance)
                                                { removed++; discarded.Add((new LineCurve(segment.PointAt(remaining.T0), segment.PointAt(remaining.T1)), "Clipped segment shorter than MT")); continue; }
                                                constraints.Add(new Constraint { Line = piece, Face = face, Size = size,
                                                    Changed = approximated || !SameLine(piece, segment, epsilon) });
                                                kept += segment.Length * remaining.Length;
                                            }
                                            covered.Add(range);
                                        }
                                    }
                                    foreach (var missing in Subtract(new Interval(0, 1), covered))
                                        if (missing.Length * segment.Length > epsilon)
                                            discarded.Add((new LineCurve(segment.PointAt(missing.T0), segment.PointAt(missing.T1)), "Curve portion outside host faces, inside holes or beyond PT"));
                                }
                                if (total - kept > mergeTolerance)
                                    result.Warn($"{label}: {total - kept:G6} model units of curve outside the surfaces, inside holes or beyond projection tolerance were excluded.");
                            }
                            foreach (var output in constraints.Skip(before)) output.Source = label;
                            if (constraints.Count == before)
                            {
                                result.Warn(label + ": no usable geometry on the cleaned surfaces; excluded.");
                                bool onlyShort = discarded.Count > 0 && discarded.All(d => d.reason == "Short/degenerate input segment");
                                if (onlyShort) foreach (var part in discarded) result.Diagnostics.Add(CleanupDiagnosticKind.Discarded, part.geometry, label, part.reason);
                                else result.Diagnostics.AddGoo(CleanupDiagnosticKind.Unresolved, item, label, "No usable projection or intersection with the cleaned surfaces");
                            }
                            else
                            {
                                foreach (var part in discarded) result.Diagnostics.Add(CleanupDiagnosticKind.Discarded, part.geometry, label, part.reason);
                                result.Log.Add($"{label}: {constraints.Count - before} projected/clipped part(s)" + (size.HasValue ? $", mesh size {size:G6}." : "."));
                            }
                        }
                        catch (ArgumentException e)
                        {
                            // A multi-face input is committed atomically, including region refinements.
                            foreach (var partial in constraints.Skip(before)) partial.Region?.Dispose();
                            constraints.RemoveRange(before, constraints.Count - before);
                            result.Warn(label + ": " + e.Message + " Entire input item excluded.");
                            result.Diagnostics.AddGoo(CleanupDiagnosticKind.Unresolved, item, label, e.Message);
                        }
                        finally { foreach (var part in discarded) part.geometry?.Dispose(); if (ownsCurve) curve?.Dispose(); if (ownsRegion) region?.Dispose(); }
                    }
                }

                ReadInputs(embedded, false);
                ReadInputs(refinements, true);
                NormalizeConstraints(constraints, mergeTolerance, epsilon, ref moved, ref removed, result.Diagnostics);
                foreach (var item in constraints)
                {
                    IGH_GeometricGoo goo;
                    if (item.Point.HasValue) goo = item.Size.HasValue ? (IGH_GeometricGoo)new MeshPointRefinement(item.Point.Value, item.Size.Value) : new GH_Point(item.Point.Value);
                    else if (item.Line.HasValue) goo = item.Size.HasValue ? (IGH_GeometricGoo)new MeshLineRefinement(item.Line.Value, item.Size.Value) : new GH_Line(item.Line.Value);
                    else goo = item.Size.HasValue ? (IGH_GeometricGoo)new MeshSurfaceRefinement(item.Region.DuplicateBrep(), item.Size.Value) : new GH_Brep(item.Region.DuplicateBrep());
                    (item.Size.HasValue ? result.Refinements : result.Embedded).Add(goo);
                    if (item.Changed) result.Diagnostics.AddGoo(CleanupDiagnosticKind.Changed, goo, item.Source, "Projected, snapped, clipped, split or refinement size consolidated");
                }
                result.Surfaces.AddRange(faces.Select(f => f.Brep.DuplicateBrep()));
                foreach (var face in faces.Where(f => f.Changed)) result.Diagnostics.Add(CleanupDiagnosticKind.Changed, face.Brep, face.Label, "Surface boundary approximated, matched, split or unioned");
                result.Log.Insert(0, $"Model units: merge={mergeTolerance:G6}, projection={projectionTolerance:G6}, curve deviation={curveTolerance:G6}; coplanar union={unionCoplanar}.");
                result.Log.Add($"Surfaces: {surfaces.Count} input Breps -> {result.Surfaces.Count} planar faces; duplicate faces removed: {duplicateFaces}.");
                result.Log.Add($"Snapped vertices: {moved}; short/duplicate constraint parts or boundary segments removed: {removed}.");
                result.Log.Add($"Output: E={result.Embedded.Count}, R={result.Refinements.Count}. Coincident refinements use the smallest mesh size. Elapsed: {clock.Elapsed.TotalMilliseconds:F0} ms.");
                result.Log.AddRange(result.Diagnostics.Messages);
                return result;
            }
            catch (Exception exception)
            {
                result.Diagnostics.Add(CleanupDiagnosticKind.Unresolved, activeGeometry, activeLabel, exception.Message);
                result.Diagnostics.AttachFailure(exception);
                throw;
            }
            finally
            {
                foreach (var face in faces) face.Dispose();
                foreach (var item in constraints) item.Region?.Dispose();
            }
        }

        static void Validate(double value, string name)
        {
            if (!double.IsFinite(value) || value <= 0) throw new ArgumentException(name + " must be finite and greater than zero.");
        }

        internal static Polyline Approximate(Curve curve, double tolerance)
        {
            if (curve == null || !curve.IsValid) throw new ArgumentException("Null or invalid curve.");
            if (curve.TryGetPolyline(out Polyline poly)) return new Polyline(poly);
            using var approximation = curve.ToPolyline(tolerance, Math.PI / 36, 0, 0);
            if (approximation == null || !approximation.TryGetPolyline(out poly)) throw new ArgumentException("Curve approximation failed.");
            return new Polyline(poly);
        }

        static void RemoveShortSegments(Polyline poly, double tolerance, ref int removed, CleanupDiagnostics diagnostics, string source)
        {
            if (!poly.IsClosed) throw new ArgumentException("An open surface boundary cannot be repaired automatically.");
            poly.RemoveAt(poly.Count - 1);
            for (int i = poly.Count - 1; i >= 0 && poly.Count > 2; i--)
                if (poly[i].DistanceTo(poly[(i + 1) % poly.Count]) <= tolerance)
                {
                    diagnostics.AddLine(CleanupDiagnosticKind.Discarded, new Line(poly[i], poly[(i + 1) % poly.Count]), source, "Short boundary segment removed");
                    poly.RemoveAt(i); removed++;
                }
            if (poly.Count < 3) throw new ArgumentException("A surface boundary or hole collapses at this merge tolerance. Reduce it.");
            poly.Add(poly[0]);
        }

        static void Rebuild(Face face, double epsilon)
        {
            var curves = face.Loops.Select(l => new PolylineCurve(l)).ToArray();
            try
            {
                foreach (var curve in curves)
                    if (Intersection.CurveSelf(curve, epsilon)?.Count > 0)
                        throw new ArgumentException(face.Label + ": snapping creates a self-intersecting boundary. Reduce the tolerances.");
                var breps = Brep.CreatePlanarBreps(curves, epsilon);
                if (breps == null || breps.Length != 1 || !breps[0].IsValid || breps[0].Loops.Count != face.Loops.Count)
                {
                    DisposeAll(breps);
                    throw new ArgumentException(face.Label + ": cleanup changes boundary/hole topology. Reduce the tolerances.");
                }
                face.Brep?.Dispose(); face.Brep = breps[0];
            }
            finally { DisposeAll(curves); }
        }

        static Point3d NearestBoundary(Point3d point, IEnumerable<Face> faces, double tolerance, Plane plane, double epsilon)
        {
            var best = point;
            double distance = tolerance;
            // Vertices take precedence over projections onto edges.
            bool vertexFound = false;
            foreach (var face in faces)
                foreach (var p in face.Loops.SelectMany(l => l))
                    if (Math.Abs(plane.DistanceTo(p)) <= epsilon && point.DistanceTo(p) <= distance)
                    { best = p; distance = point.DistanceTo(p); vertexFound = true; }
            if (vertexFound) return best;
            foreach (var face in faces)
                foreach (var line in face.Loops.SelectMany(l => l.GetSegments()))
                {
                    var p = line.ClosestPoint(point, true);
                    if (Math.Abs(plane.DistanceTo(p)) <= epsilon && point.DistanceTo(p) <= distance)
                    { best = p; distance = point.DistanceTo(p); }
                }
            return best;
        }

        static Polyline SplitBoundary(Polyline poly, IEnumerable<Point3d> vertices, double epsilon)
        {
            var output = new Polyline();
            foreach (var line in poly.GetSegments())
            {
                output.Add(line.From);
                var cuts = vertices.Select(p => (point: p, t: line.ClosestParameter(p)))
                    .Where(c => c.t > epsilon / line.Length && c.t < 1 - epsilon / line.Length && line.PointAt(c.t).DistanceTo(c.point) <= epsilon)
                    .OrderBy(c => c.t);
                foreach (var cut in cuts) if (output.Last.DistanceTo(cut.point) > epsilon) output.Add(cut.point);
            }
            output.Add(output[0]); return output;
        }

        static bool Contains(Face face, Point3d point, double epsilon) =>
            Math.Abs(face.Plane.DistanceTo(point)) <= epsilon && face.Brep.Faces[0].ClosestPoint(point, out double u, out double v) &&
            face.Brep.Faces[0].PointAt(u, v).DistanceTo(point) <= epsilon &&
            face.Brep.Faces[0].IsPointOnFace(u, v, epsilon) != PointFaceRelation.Exterior;

        static Point3d SnapOnFace(Point3d point, Face face, double tolerance, double epsilon) =>
            NearestBoundary(point, new[] { face }, tolerance, face.Plane, epsilon);

        static Point3d ProjectAndSnap(Point3d original, Face face, double tolerance, double epsilon)
        {
            var projected = face.Plane.ClosestPoint(original);
            var snapped = SnapOnFace(projected, face, tolerance, epsilon);
            return original.DistanceTo(snapped) <= tolerance ? snapped : projected;
        }

        static Point3d? ProjectPoint(Point3d point, Face face, double tolerance, double epsilon)
        {
            if (!point.IsValid || Math.Abs(face.Plane.DistanceTo(point)) > tolerance) return null;
            var p = ProjectAndSnap(point, face, tolerance, epsilon);
            return p.DistanceTo(point) <= tolerance && Contains(face, p, epsilon) ? p : (Point3d?)null;
        }

        static IEnumerable<Interval> Clip(Line line, Face face, double epsilon)
        {
            using var curve = new LineCurve(line);
            var cuts = new List<double> { 0, 1 };
            foreach (var boundary in face.Loops)
            {
                using var edge = new PolylineCurve(boundary);
                var events = Intersection.CurveCurve(curve, edge, epsilon, epsilon);
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
                double a = Math.Max(0, cuts[i - 1]), b = Math.Min(1, cuts[i]);
                if ((b - a) * line.Length > epsilon && Contains(face, line.PointAt((a + b) / 2), epsilon)) yield return new Interval(a, b);
            }
        }

        static IEnumerable<Interval> Subtract(Interval interval, List<Interval> covered)
        {
            double start = interval.T0;
            foreach (var other in covered.OrderBy(i => i.T0))
            {
                if (other.T1 <= start || other.T0 >= interval.T1) continue;
                if (other.T0 > start) yield return new Interval(start, other.T0);
                start = Math.Max(start, other.T1);
            }
            if (start < interval.T1) yield return new Interval(start, interval.T1);
        }

        static void UnionFaces(List<Face> faces, double epsilon, Result result)
        {
            var pending = faces.ToList();
            var replacement = new List<Face>();
            try
            {
                while (pending.Count > 0)
                {
                    var first = pending[0];
                    var group = pending.Where(f => Math.Abs(first.Plane.Normal * f.Plane.Normal) > 1 - 1E-12 &&
                        f.Loops.SelectMany(l => l).All(p => Math.Abs(first.Plane.DistanceTo(p)) <= epsilon)).ToList();
                    foreach (var face in group) pending.Remove(face);
                    var union = Brep.CreatePlanarUnion(group.Select(f => f.Brep), first.Plane, epsilon);
                    if (union == null || union.Length == 0) throw new ArgumentException("Coplanar surface union failed; no partial result was returned.");
                    foreach (var brep in union)
                    {
                        var face = new Face { Brep = brep, Plane = first.Plane, Label = "Union: " + string.Join("; ", group.Select(f => f.Label)),
                            Changed = group.Count != union.Length || group.Any(f => f.Changed) || !group.Any(f => SameRegion(f.Brep, brep, epsilon)) };
                        replacement.Add(face);
                        foreach (var loop in brep.Loops) { using var c = loop.To3dCurve(); face.Loops.Add(Approximate(c, epsilon)); }
                    }
                }
                result.Log.Add($"Coplanar union: {faces.Count} -> {replacement.Count} face(s). Internal panel boundaries were removed.");
                foreach (var face in faces) face.Dispose();
                faces.Clear(); faces.AddRange(replacement); replacement.Clear();
            }
            finally { foreach (var face in replacement) face.Dispose(); }
        }

        static void AddRegion(Brep source, double? size, List<Face> faces, List<Constraint> constraints,
            double projectionTolerance, double curveTolerance, double epsilon)
        {
            if (!source.IsValid) throw new ArgumentException("Invalid planar region.");
            foreach (var sourceFace in source.Faces)
            {
                if (!sourceFace.TryGetPlane(out Plane plane, 1E-6)) throw new ArgumentException("Region must be planar.");
                foreach (var target in faces)
                {
                    if (Math.Abs(plane.Normal * target.Plane.Normal) < 1 - 1E-12) continue;
                    var region = new Face { Plane = target.Plane, Label = "Embedded/refinement region" };
                    using (region)
                    {
                        bool fits = true;
                        foreach (var loop in sourceFace.Loops)
                        {
                            using var curve = loop.To3dCurve();
                            var poly = Approximate(curve, curveTolerance);
                            if (poly.Any(p => Math.Abs(target.Plane.DistanceTo(p)) > projectionTolerance)) { fits = false; break; }
                            for (int i = 0; i < poly.Count; i++) poly[i] = ProjectAndSnap(poly[i], target, projectionTolerance, epsilon);
                            region.Loops.Add(poly);
                        }
                        if (!fits) continue;
                        Rebuild(region, epsilon);
                        var intersection = Brep.CreatePlanarIntersection(region.Brep, target.Brep, target.Plane, epsilon);
                        if (intersection == null) throw new ArgumentException("Planar region clipping failed.");
                        foreach (var piece in intersection) constraints.Add(new Constraint { Region = piece, Size = size, Face = target });
                    }
                }
            }
        }

        static bool LinearRegion(Brep region)
        {
            foreach (var loop in region.Loops) { using var curve = loop.To3dCurve(); if (!curve.TryGetPolyline(out Polyline _)) return false; }
            return true;
        }

        static List<Brep> RegionRemainder(Brep source, List<Constraint> retained, double projectionTolerance, double epsilon)
        {
            var remainder = new List<Brep>();
            try
            {
                foreach (var face in source.Faces)
                {
                    if (!face.TryGetPlane(out Plane plane, epsilon)) continue;
                    var pieces = new List<Brep> { face.DuplicateFace(false) };
                    try
                    {
                        foreach (var item in retained.Where(c => Math.Abs(c.Face.Plane.Normal * plane.Normal) > 1 - 1E-12 &&
                            Math.Abs(plane.DistanceTo(c.Face.Plane.Origin)) <= projectionTolerance))
                        {
                            using var projected = item.Region.DuplicateBrep();
                            projected.Transform(Transform.PlanarProjection(plane));
                            var next = new List<Brep>();
                            try
                            {
                                foreach (var piece in pieces)
                                {
                                    var difference = Brep.CreatePlanarDifference(piece, projected, plane, epsilon);
                                    if (difference == null) throw new ArgumentException("Could not compute the excluded region for diagnostics.");
                                    next.AddRange(difference);
                                }
                                foreach (var piece in pieces) piece.Dispose();
                                pieces = next; next = null;
                            }
                            finally { if (next != null) foreach (var piece in next) piece.Dispose(); }
                        }
                        remainder.AddRange(pieces); pieces.Clear();
                    }
                    finally { foreach (var piece in pieces) piece.Dispose(); }
                }
                return remainder;
            }
            catch { foreach (var piece in remainder) piece.Dispose(); throw; }
        }

        static void NormalizeConstraints(List<Constraint> items, double tolerance, double epsilon, ref int moved, ref int removed, CleanupDiagnostics diagnostics)
        {
            // Give nearly collinear overlapping lines the same carrier before splitting overlaps.
            var carriers = new List<Line>();
            foreach (var item in items.Where(i => i.Line.HasValue))
            {
                var line = item.Line.Value;
                foreach (var carrier in carriers)
                {
                    var a = carrier.ClosestPoint(line.From, false); var b = carrier.ClosestPoint(line.To, false);
                    double ta = carrier.ClosestParameter(a), tb = carrier.ClosestParameter(b);
                    if (Math.Max(ta, tb) < 0 || Math.Min(ta, tb) > 1 || line.From.DistanceTo(a) > tolerance || line.To.DistanceTo(b) > tolerance) continue;
                    var candidate = new Line(a, b);
                    if (!candidate.IsValid || candidate.Length <= tolerance || !CanMove(line.From, a, item.Face) || !CanMove(line.To, b, item.Face)) continue;
                    if (Clip(candidate, item.Face, epsilon).Sum(r => r.Length) < 1 - epsilon / candidate.Length) continue;
                    if (line.From.DistanceTo(a) > epsilon) moved++;
                    if (line.To.DistanceTo(b) > epsilon) moved++;
                    if (!SameLine(line, candidate, epsilon)) item.Changed = true;
                    item.Line = candidate; break;
                }
                carriers.Add(item.Line.Value);
            }
            bool CanMove(Point3d original, Point3d candidate, Face face)
            {
                if (!Contains(face, candidate, epsilon)) return false;
                // A point already on a border can only move along the border.
                bool wasOnBorder = face.Loops.SelectMany(l => l.GetSegments()).Any(l => l.DistanceTo(original, true) <= epsilon);
                return !wasOnBorder || face.Loops.SelectMany(l => l.GetSegments()).Any(l => l.DistanceTo(candidate, true) <= epsilon);
            }
            var representatives = new List<Point3d>();
            Point3d Snap(Point3d point, Face face, Line? ownLine = null)
            {
                var candidates = representatives.Where(p => p.DistanceTo(point) <= tolerance && CanMove(point, p, face)).OrderBy(p => p.DistanceTo(point));
                foreach (var p in candidates) return p;
                var best = point; double distance = tolerance;
                foreach (var line in carriers)
                {
                    if (ownLine.HasValue && SameLine(ownLine.Value, line, epsilon)) continue;
                    var candidate = line.ClosestPoint(point, true);
                    if (point.DistanceTo(candidate) <= distance && CanMove(point, candidate, face))
                    { best = candidate; distance = point.DistanceTo(candidate); }
                }
                representatives.Add(best); return best;
            }
            // Boundary points first; interior constraints cannot pull a boundary point off its edge.
            foreach (var face in items.Select(i => i.Face).Distinct()) representatives.AddRange(face.Loops.SelectMany(l => l));
            foreach (var item in items)
            {
                if (item.Point.HasValue)
                {
                    var p = Snap(item.Point.Value, item.Face);
                    if (p.DistanceTo(item.Point.Value) > epsilon) { moved++; item.Changed = true; }
                    item.Point = p;
                }
                else if (item.Line.HasValue)
                {
                    var line = item.Line.Value;
                    var a = Snap(line.From, item.Face, line); var b = Snap(line.To, item.Face, line);
                    var candidate = new Line(a, b);
                    // Endpoint snapping must not push a segment across a hole or outside a concavity.
                    if (candidate.IsValid && Clip(candidate, item.Face, epsilon).Sum(r => r.Length) >= 1 - epsilon / candidate.Length)
                    {
                        if (a.DistanceTo(line.From) > epsilon) moved++;
                        if (b.DistanceTo(line.To) > epsilon) moved++;
                        if (!SameLine(line, candidate, epsilon)) item.Changed = true;
                        item.Line = candidate;
                    }
                }
            }

            // Split crossings, T-junctions and collinear overlaps, then deduplicate the resulting pieces.
            var lines = items.Where(i => i.Line.HasValue).ToList();
            var cuts = lines.Select(i => new List<double> { 0, 1 }).ToArray();
            using (var tree = new RTree())
            {
                for (int i = 0; i < lines.Count; i++) tree.Insert(lines[i].Line.Value.BoundingBox, i);
                for (int i = 0; i < lines.Count; i++)
                {
                    var a = lines[i].Line.Value;
                    var box = a.BoundingBox; box.Inflate(tolerance);
                    var neighbours = new List<int>(); tree.Search(box, (s, e) => { if (e.Id > i) neighbours.Add(e.Id); });
                    foreach (int j in neighbours)
                    {
                        using var ca = new LineCurve(a); using var cb = new LineCurve(lines[j].Line.Value);
                        var hits = Intersection.CurveCurve(ca, cb, epsilon, epsilon);
                        if (hits == null) continue;
                        foreach (var hit in hits)
                        {
                            cuts[i].Add(ca.Domain.NormalizedParameterAt(hit.ParameterA)); cuts[j].Add(cb.Domain.NormalizedParameterAt(hit.ParameterB));
                            if (hit.IsOverlap) { cuts[i].Add(ca.Domain.NormalizedParameterAt(hit.OverlapA.T1)); cuts[j].Add(cb.Domain.NormalizedParameterAt(hit.OverlapB.T1)); }
                        }
                    }
                    // Points on a line are also mesh vertices.
                    foreach (var point in items.Where(c => c.Point.HasValue))
                    {
                        double t = a.ClosestParameter(point.Point.Value);
                        if (t > 0 && t < 1 && a.PointAt(t).DistanceTo(point.Point.Value) <= epsilon) cuts[i].Add(t);
                    }
                }
            }
            items.RemoveAll(i => i.Line.HasValue);
            for (int i = 0; i < lines.Count; i++)
            {
                cuts[i].Sort(); var line = lines[i].Line.Value;
                for (int k = 1; k < cuts[i].Count; k++)
                {
                    var part = new Line(line.PointAt(cuts[i][k - 1]), line.PointAt(cuts[i][k]));
                    if (part.Length <= epsilon) continue;
                    if (part.Length <= tolerance)
                    {
                        removed++;
                        diagnostics.AddLine(CleanupDiagnosticKind.Discarded, part, lines[i].Source, "Segment shorter than MT after intersection splitting");
                        continue;
                    }
                    items.Add(new Constraint { Line = part, Size = lines[i].Size, Face = lines[i].Face, Source = lines[i].Source,
                        Changed = lines[i].Changed || !SameLine(part, line, epsilon) });
                }
            }
            var unique = new List<Constraint>();
            foreach (var item in items)
            {
                var existing = unique.FirstOrDefault(c => SameConstraint(c, item, epsilon));
                if (existing == null) unique.Add(item);
                else
                {
                    if (item.Size.HasValue)
                    {
                        double size = Math.Min(existing.Size ?? double.PositiveInfinity, item.Size.Value);
                        if (existing.Size != size) existing.Changed = true;
                        existing.Size = size;
                    }
                    if (item.Line.HasValue) diagnostics.AddLine(CleanupDiagnosticKind.Discarded, item.Line.Value, item.Source, "Duplicate/overlapping constraint after cleanup");
                    else if (item.Point.HasValue)
                    {
                        using var point = new Rhino.Geometry.Point(item.Point.Value);
                        diagnostics.Add(CleanupDiagnosticKind.Discarded, point, item.Source, "Duplicate point after cleanup");
                    }
                    else diagnostics.Add(CleanupDiagnosticKind.Discarded, item.Region, item.Source, "Duplicate region after cleanup");
                    item.Region?.Dispose(); removed++;
                }
            }
            items.Clear(); items.AddRange(unique);
        }

        static bool SameConstraint(Constraint a, Constraint b, double epsilon)
        {
            if (a.Point.HasValue && b.Point.HasValue) return a.Point.Value.DistanceTo(b.Point.Value) <= epsilon;
            if (a.Line.HasValue && b.Line.HasValue) return SameLine(a.Line.Value, b.Line.Value, epsilon);
            return a.Region != null && b.Region != null && SameRegion(a.Region, b.Region, epsilon);
        }

        static bool SameLine(Line a, Line b, double epsilon) =>
            (a.From.DistanceTo(b.From) <= epsilon && a.To.DistanceTo(b.To) <= epsilon) ||
            (a.From.DistanceTo(b.To) <= epsilon && a.To.DistanceTo(b.From) <= epsilon);

        static bool SameRegion(Brep a, Brep b, double epsilon)
        {
            if (a.Loops.Count != b.Loops.Count) return false;
            var linesA = new List<Line>(); var linesB = new List<Line>();
            foreach (var loop in a.Loops) { using var c = loop.To3dCurve(); linesA.AddRange(Approximate(c, epsilon).GetSegments()); }
            foreach (var loop in b.Loops) { using var c = loop.To3dCurve(); linesB.AddRange(Approximate(c, epsilon).GetSegments()); }
            // Equivalent boundaries may have different collinear subdivisions.
            bool Covered(Line line, List<Line> boundaries)
            {
                var covered = new List<Interval>();
                foreach (var other in boundaries)
                {
                    double a = line.ClosestParameter(other.From), b = line.ClosestParameter(other.To);
                    if (line.PointAt(a).DistanceTo(other.From) > epsilon || line.PointAt(b).DistanceTo(other.To) > epsilon) continue;
                    covered.Add(new Interval(Math.Max(0, Math.Min(a, b)), Math.Min(1, Math.Max(a, b))));
                }
                return Subtract(new Interval(0,1), covered.Where(i => i.T1 >= i.T0).ToList()).Sum(i => i.Length) * line.Length <= epsilon;
            }
            return linesA.All(l => Covered(l, linesB)) && linesB.All(l => Covered(l, linesA));
        }

        static void DisposeAll<T>(IEnumerable<T> values) where T : IDisposable
        {
            if (values != null) foreach (var value in values) value?.Dispose();
        }
    }
}
