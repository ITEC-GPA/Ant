using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Ant.GrasshopperPlugIn.DataTypes;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.Helpers
{
    internal enum CleanupDiagnosticKind { Changed, Discarded, Unresolved }

    internal sealed class CleanupDiagnostics
    {
        internal const string ExceptionKey = "Ant.CleanupDiagnostics";
        internal List<GeometryBase> Changed { get; } = new List<GeometryBase>();
        internal List<GeometryBase> Discarded { get; } = new List<GeometryBase>();
        internal List<GeometryBase> Unresolved { get; } = new List<GeometryBase>();
        internal List<string> Messages { get; } = new List<string>();
        internal static readonly Color ChangedColor = Color.FromArgb(224, 153, 35);
        internal static readonly Color DiscardedColor = Color.FromArgb(214, 65, 65);
        internal static readonly Color UnresolvedColor = Color.FromArgb(160, 65, 196);

        internal void Add(CleanupDiagnosticKind kind, GeometryBase geometry, string source, string reason)
        {
            var list = kind == CleanupDiagnosticKind.Changed ? Changed : kind == CleanupDiagnosticKind.Discarded ? Discarded : Unresolved;
            string port = kind == CleanupDiagnosticKind.Changed ? "C" : kind == CleanupDiagnosticKind.Discarded ? "D" : "U";
            if (geometry == null || !geometry.IsValid)
            {
                int previousCount = list.Count;
                // Invalid topology may still contain useful finite locations or valid boundary curves.
                if (geometry is Mesh invalidMesh)
                {
                    foreach (var point in invalidMesh.Vertices.ToPoint3dArray().Where(p => p.IsValid).Distinct())
                    { using var marker = new Rhino.Geometry.Point(point); Add(kind, marker, source, reason + "; available vertex of invalid mesh"); }
                }
                else if (geometry is Brep invalidBrep)
                {
                    foreach (var edge in invalidBrep.Edges)
                    { using var curve = edge.DuplicateCurve(); if (curve != null && curve.IsValid) Add(kind, curve, source, reason + "; available boundary of invalid Brep"); }
                }
                Messages.Add($"{port}: {source}: {reason} " + (list.Count > previousCount
                    ? "(invalid source; available portions are shown)." : "(no valid geometry available for preview)."));
                return;
            }
            var copy = geometry.Duplicate();
            copy.SetUserString("Ant.Diagnostic.Source", source);
            copy.SetUserString("Ant.Diagnostic.Reason", reason);
            copy.SetUserString("Ant.Diagnostic.Kind", kind.ToString());
            Messages.Add($"{port}[{list.Count}] {source}: {reason}");
            list.Add(copy);
        }

        internal void AddGoo(CleanupDiagnosticKind kind, IGH_GeometricGoo goo, string source, string reason)
        {
            using var geometry = Snapshot(goo);
            Add(kind, geometry, source, reason);
        }

        internal void AddLine(CleanupDiagnosticKind kind, Line line, string source, string reason)
        {
            using GeometryBase geometry = line.IsValid ? (GeometryBase)new LineCurve(line) : line.From.IsValid ? new Rhino.Geometry.Point(line.From) : null;
            Add(kind, geometry, source, reason);
        }

        private static GeometryBase Snapshot(IGH_GeometricGoo goo) => goo switch
        {
            MeshPointRefinement p => new Rhino.Geometry.Point(p.Geometry),
            MeshLineRefinement l => new LineCurve(l.Geometry),
            MeshPolylineRefinement p when p.Geometry != null => new PolylineCurve(p.Geometry),
            MeshPolylineCurveRefinement p => p.Geometry?.DuplicateCurve(),
            MeshSurfaceRefinement s => s.Geometry?.DuplicateBrep(),
            GH_Point p => new Rhino.Geometry.Point(p.Value),
            GH_Line l => new LineCurve(l.Value),
            GH_Curve c => c.Value?.DuplicateCurve(),
            GH_Brep b => b.Value?.DuplicateBrep(),
            GH_Surface s => s.Value?.DuplicateBrep(),
            GH_Mesh m => m.Value?.DuplicateMesh(),
            _ => null
        };

        // Face indices here refer to the named cleanup stage, not a persistent source-to-output map.
        internal void AddFaces(CleanupDiagnosticKind kind, Mesh mesh, IEnumerable<int> faceIndices, string stage, string reason)
        {
            var indices = faceIndices.Distinct().ToList();
            using var patch = new Mesh(); patch.Vertices.UseDoublePrecisionVertices = true;
            foreach (int index in indices)
            {
                var face = mesh.Faces[index];
                var ids = face.IsTriangle ? new[] { face.A, face.B, face.C } : new[] { face.A, face.B, face.C, face.D };
                var points = ids.Where(i => i >= 0 && i < mesh.Vertices.Count).Select(mesh.Vertices.Point3dAt).Where(p => p.IsValid).ToList();
                bool drawableFace = points.Count == ids.Length && points.Distinct().Count() == ids.Length &&
                    Vector3d.CrossProduct(points[1] - points[0], points[2] - points[0]).Length > 0;
                if (drawableFace)
                {
                    int offset = patch.Vertices.Count; patch.Vertices.AddVertices(points);
                    if (points.Count == 3) patch.Faces.AddFace(offset, offset + 1, offset + 2);
                    else patch.Faces.AddFace(offset, offset + 1, offset + 2, offset + 3);
                }
                else
                {
                    var distinct = points.Distinct().ToList();
                    if (distinct.Count > 2) distinct.Add(distinct[0]);
                    using GeometryBase outline = distinct.Count > 1 ? (GeometryBase)new PolylineCurve(distinct) :
                        distinct.Count == 1 ? new Rhino.Geometry.Point(distinct[0]) : null;
                    Add(kind, outline, $"{stage}, face {index}", reason + "; drawable part of invalid/degenerate face");
                }
            }
            if (patch.Faces.Count > 0)
            {
                patch.Normals.ComputeNormals();
                string ids = string.Join(",", indices.Take(24)) + (indices.Count > 24 ? ",..." : "");
                Add(kind, patch, $"{stage}, faces [{ids}]", $"{reason}; {indices.Count} face(s)");
            }
        }

        internal void MeshDelta(Mesh before, Mesh after, string stage, bool includeDiscarded = true)
        {
            var beforeKeys = Enumerable.Range(0, before.Faces.Count).Select(i => FaceKey(before, i)).ToArray();
            var afterKeys = Enumerable.Range(0, after.Faces.Count).Select(i => FaceKey(after, i)).ToArray();
            List<int> Missing(FaceSignature[] source, FaceSignature[] target)
            {
                var counts = new Dictionary<FaceSignature, int>(target.Length);
                foreach (var key in target) { counts.TryGetValue(key, out int count); counts[key] = count + 1; }
                var missing = new List<int>();
                for (int i = 0; i < source.Length; i++)
                {
                    var key = source[i];
                    if (counts.TryGetValue(key, out int count) && count > 0) counts[key] = count - 1;
                    else missing.Add(i);
                }
                return missing;
            }
            if (includeDiscarded) AddFaces(CleanupDiagnosticKind.Discarded, before, Missing(beforeKeys, afterKeys), stage, "Face removed or replaced during this operation");
            AddFaces(CleanupDiagnosticKind.Changed, after, Missing(afterKeys, beforeKeys), stage, "New/repaired face after this operation");
        }

        private readonly record struct FaceSignature(Point3d A, Point3d B, Point3d C, Point3d D, bool Quad);

        private static FaceSignature FaceKey(Mesh mesh, int index)
        {
            var f = mesh.Faces[index];
            Span<Point3d> points = stackalloc Point3d[4];
            points[0] = mesh.Vertices.Point3dAt(f.A); points[1] = mesh.Vertices.Point3dAt(f.B);
            points[2] = mesh.Vertices.Point3dAt(f.C); points[3] = mesh.Vertices.Point3dAt(f.D);
            int count = f.IsTriangle ? 3 : 4, first = 0;
            for (int start = 1; start < count; start++)
                for (int offset = 0; offset < count; offset++)
                {
                    var a = points[(start + offset) % count]; var b = points[(first + offset) % count];
                    int order = a.X.CompareTo(b.X); if (order == 0) order = a.Y.CompareTo(b.Y); if (order == 0) order = a.Z.CompareTo(b.Z);
                    if (order < 0) { first = start; break; }
                    if (order > 0) break;
                }
            // Typed keys avoid coordinate formatting and multiple per-face string allocations.
            return new FaceSignature(points[first], points[(first + 1) % count], points[(first + 2) % count],
                f.IsQuad ? points[(first + 3) % count] : Point3d.Origin, f.IsQuad);
        }

        internal void AttachFailure(Exception exception)
        {
            foreach (var geometry in Changed.Concat(Discarded)) geometry.Dispose();
            Changed.Clear(); Discarded.Clear();
            Messages.RemoveAll(m => m.StartsWith("C[") || m.StartsWith("D[") || m.StartsWith("C:") || m.StartsWith("D:"));
            exception.Data[ExceptionKey] = this;
        }

        internal static void SetOutputs(IGH_DataAccess data, int first, CleanupDiagnostics diagnostics)
        {
            data.SetDataList(first, diagnostics.Changed);
            data.SetDataList(first + 1, diagnostics.Discarded);
            data.SetDataList(first + 2, diagnostics.Unresolved);
        }

        internal static IEnumerable<IGH_Param> CreateOutputs()
        {
            // Suppress the standard green preview of these ports; the component draws diagnostic colors.
            yield return new Param_Geometry { Name = "Changed", NickName = "C", Description = "Modified/replacement geometry after each cleanup operation. Orange preview; see C[index] in the log.", Access = GH_ParamAccess.list, Hidden = true };
            yield return new Param_Geometry { Name = "Discarded", NickName = "D", Description = "Removed geometry and clipped portions, at the stage where they were removed. Red preview; see D[index] in the log.", Access = GH_ParamAccess.list, Hidden = true };
            yield return new Param_Geometry { Name = "Unresolved", NickName = "U", Description = "Unresolved input geometry or remaining defects. Purple preview; see U[index] in the log.", Access = GH_ParamAccess.list, Hidden = true };
        }

        internal static BoundingBox PreviewBounds(BoundingBox box, IList<IGH_Param> outputs, int first)
        {
            foreach (var goo in outputs.Skip(first).SelectMany(p => p.VolatileData.AllData(true)).OfType<IGH_GeometricGoo>()) box.Union(goo.Boundingbox);
            return box;
        }

        internal static void Draw(IGH_PreviewArgs args, IList<IGH_Param> outputs, int first)
        {
            var colors = new[] { ChangedColor, DiscardedColor, UnresolvedColor };
            for (int i = 0; i < 3; i++)
                foreach (var goo in outputs[first + i].VolatileData.AllData(true))
                {
                    var color = colors[i];
                    switch (goo)
                    {
                        case GH_Point p: args.Display.DrawPoint(p.Value, Rhino.Display.PointStyle.X, 5, color); break;
                        case GH_Line l: args.Display.DrawLine(l.Value, color, 2); break;
                        case GH_Curve c: args.Display.DrawCurve(c.Value, color, 2); break;
                        case GH_Brep b: args.Display.DrawBrepWires(b.Value, color, 2); break;
                        case GH_Surface s: args.Display.DrawBrepWires(s.Value, color, 2); break;
                        case GH_Mesh m: args.Display.DrawMeshWires(m.Value, color, 2); break;
                    }
                }
        }
    }
}
