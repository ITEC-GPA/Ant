using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.Helpers
{
    internal static class MeshCleanup
    {
        internal sealed class Result
        {
            public Mesh Mesh { get; } = new Mesh();
            public List<string> Warnings { get; } = new List<string>();
            public string Report { get; set; }
            public CleanupDiagnostics Diagnostics { get; } = new CleanupDiagnostics();
        }

        internal static Result Clean(IEnumerable<Mesh> sources, double vertexTolerance, double edgeLength, double faceArea)
        {
            Validate(vertexTolerance, "Vertex tolerance");
            Validate(edgeLength, "Minimum edge length");
            Validate(faceArea, "Minimum face area");
            if (sources == null) throw new ArgumentNullException(nameof(sources));

            var result = new Result();
            var mesh = result.Mesh;
            mesh.Vertices.UseDoublePrecisionVertices = true;
            int inputs = 0, skipped = 0, inputVertices = 0, inputFaces = 0;
            int invalid = 0, degenerate = 0, repaired = 0, duplicate = 0, small = 0, merged = 0, collapsed = 0;
            try
            {
                foreach (var source in sources)
                {
                    inputs++;
                    if (source == null) { skipped++; result.Diagnostics.Add(CleanupDiagnosticKind.Unresolved, null, $"M[{inputs - 1}]", "Null mesh"); continue; }
                    inputVertices += source.Vertices.Count;
                    inputFaces += source.Faces.Count;
                    using var copy = source.DuplicateMesh();

                    // Sanitize indices and non-finite coordinates before invoking native topology operations.
                    var badFaces = new List<int>();
                    for (int i = 0; i < copy.Faces.Count; i++)
                    {
                        var f = copy.Faces[i];
                        if (!ValidVertex(copy, f.A) || !ValidVertex(copy, f.B) || !ValidVertex(copy, f.C) || !ValidVertex(copy, f.D))
                            badFaces.Add(i);
                    }
                    result.Diagnostics.AddFaces(CleanupDiagnosticKind.Discarded, copy, badFaces, $"M[{inputs - 1}]", "Invalid face indices or coordinates");
                    invalid += copy.Faces.DeleteFaces(badFaces, false);
                    CaptureUnusedVertices(copy, result.Diagnostics, $"M[{inputs - 1}]");
                    copy.Vertices.CullUnused();
                    if (copy.Faces.Count == 0) { skipped++; continue; }
                    mesh.Append(copy);
                }

                merged += CombineVertices(mesh);
                if (vertexTolerance > 0 && mesh.Vertices.Count > 0)
                {
                    SnapToRepresentatives(mesh, vertexTolerance, result.Diagnostics);
                    merged += CombineVertices(mesh);
                }
                RemoveDegenerateFaces(mesh, ref degenerate, ref repaired, result.Diagnostics);
                duplicate += RemoveDuplicateFaces(mesh, result.Diagnostics);
                mesh.Vertices.CullUnused();

                if (edgeLength > 0 && mesh.Faces.Count > 0)
                {
                    // Rhino updates adjacent faces while collapsing edges instead of just deleting those faces.
                    using var beforeCollapse = mesh.DuplicateMesh();
                    int count = mesh.CollapseFacesByEdgeLength(false, edgeLength);
                    if (count >= 0) collapsed += count;
                    else result.Warnings.Add(count == -2
                        ? "Short-edge collapse was limited because it would remove the entire mesh. Reduce EL."
                        : "Rhino could not complete short-edge collapse on this topology.");
                    if (count != 0) result.Diagnostics.MeshDelta(beforeCollapse, mesh, "Short-edge collapse");
                    merged += CombineVertices(mesh);
                    RemoveDegenerateFaces(mesh, ref degenerate, ref repaired, result.Diagnostics);
                }

                var smallFaces = new List<int>();
                for (int i = 0; i < mesh.Faces.Count; i++)
                {
                    double area = FaceArea(mesh, mesh.Faces[i]);
                    if (!double.IsFinite(area)) throw new InvalidOperationException("Mesh coordinates are too large to compute a finite face area.");
                    if (area <= faceArea) smallFaces.Add(i);
                }
                result.Diagnostics.AddFaces(CleanupDiagnosticKind.Discarded, mesh, smallFaces, "Area filter", "Face at or below FA");
                small += mesh.Faces.DeleteFaces(smallFaces, false);
                duplicate += RemoveDuplicateFaces(mesh, result.Diagnostics);
                CaptureUnusedVertices(mesh, result.Diagnostics, "Final vertex cleanup");
                int unused = mesh.Vertices.CullUnused();
                mesh.Compact();

                if (mesh.Faces.Count > 0)
                {
                    using var beforeNormals = mesh.DuplicateMesh();
                    int normalsChanged = mesh.UnifyNormals();
                    if (normalsChanged > 0) result.Diagnostics.MeshDelta(beforeNormals, mesh, "Normal unification", false);
                    mesh.FaceNormals.ComputeFaceNormals();
                    mesh.Normals.ComputeNormals();
                    if (!mesh.IsValid)
                        throw new InvalidOperationException("Cleanup could not produce a valid mesh. Check the input topology or reduce the tolerances.");
                    int shortEdges = 0, nonManifold = 0;
                    for (int i = 0; i < mesh.TopologyEdges.Count; i++)
                    {
                        var edge = mesh.TopologyEdges.EdgeLine(i);
                        if (edgeLength > 0 && edge.Length < edgeLength)
                        {
                            shortEdges++;
                            result.Diagnostics.AddLine(CleanupDiagnosticKind.Unresolved, edge, $"Output edge {i}", "Edge remains shorter than EL");
                        }
                        if (mesh.TopologyEdges.GetConnectedFaces(i).Length > 2)
                        {
                            nonManifold++;
                            result.Diagnostics.AddLine(CleanupDiagnosticKind.Unresolved, edge, $"Output edge {i}", "Non-manifold edge: more than two adjacent faces");
                        }
                    }
                    if (shortEdges > 0) result.Warnings.Add($"{shortEdges} edges remain shorter than EL after collapse.");
                    if (nonManifold > 0) result.Warnings.Add($"{nonManifold} non-manifold edges remain (more than two adjacent faces).");
                }
                else result.Warnings.Add("No faces remain after cleanup. Check the input meshes and tolerances.");
                if (skipped > 0) result.Warnings.Add($"Skipped {skipped} null meshes or meshes without usable faces.");
                if (merged > 0 && mesh.Faces.Count > 0)
                    result.Diagnostics.Add(CleanupDiagnosticKind.Changed, mesh, "Mesh merge", "Mesh topology updated by merged vertices");

                result.Report = string.Format(CultureInfo.InvariantCulture,
                    "Meshes: {0}; skipped: {1}\nVertices: {2} -> {3}; merged: {4}\nFaces: {5} -> {6}\n" +
                    "Removed invalid faces: {7}; degenerate: {8}; duplicates: {9}; small: {10}\n" +
                    "Repaired zero-area quads: {11}; collapsed edges: {12}; final unused vertices removed: {13}\n" +
                    "VT: {14:G6}; EL: {15:G6} (model units); FA: {16:G6} (squared model units)",
                    inputs, skipped, inputVertices, mesh.Vertices.Count, merged, inputFaces, mesh.Faces.Count,
                    invalid, degenerate, duplicate, small, repaired, collapsed, unused, vertexTolerance, edgeLength, faceArea);
                result.Report += "\n" + string.Join("\n", result.Diagnostics.Messages);
                return result;
            }
            catch (Exception exception)
            {
                result.Diagnostics.Add(CleanupDiagnosticKind.Unresolved, mesh, "Cleanup", exception.Message);
                result.Diagnostics.AttachFailure(exception);
                mesh.Dispose();
                throw;
            }
        }

        private static void Validate(double value, string name)
        {
            if (!double.IsFinite(value) || value < 0)
                throw new ArgumentOutOfRangeException(name, name + " must be finite and non-negative.");
        }

        private static bool ValidVertex(Mesh mesh, int index) => index >= 0 && index < mesh.Vertices.Count && mesh.Vertices.Point3dAt(index).IsValid;

        private static int CombineVertices(Mesh mesh)
        {
            int before = mesh.Vertices.Count;
            mesh.Vertices.CombineIdentical(true, true);
            return before - mesh.Vertices.Count;
        }

        private static void SnapToRepresentatives(Mesh mesh, double tolerance, CleanupDiagnostics diagnostics)
        {
            var points = mesh.Vertices.ToPoint3dArray();
            using var tree = new RTree();
            var moved = new HashSet<int>();
            for (int i = 0; i < points.Length; i++)
            {
                int nearest = -1;
                double distance = tolerance;
                var point = points[i];
                tree.Search(new Sphere(point, tolerance), (sender, args) =>
                {
                    double candidate = point.DistanceTo(points[args.Id]);
                    if (candidate <= tolerance && (nearest < 0 || candidate < distance || (candidate == distance && args.Id < nearest)))
                    {
                        nearest = args.Id;
                        distance = candidate;
                    }
                });
                if (nearest < 0) tree.Insert(point, i);
                else
                {
                    mesh.Vertices.SetVertex(i, points[nearest]);
                    if (point != points[nearest]) moved.Add(i);
                }
                // Only fixed representatives are indexed: proximity chains cannot drag a point beyond VT.
            }
            diagnostics.AddFaces(CleanupDiagnosticKind.Changed, mesh, Enumerable.Range(0, mesh.Faces.Count).Where(i =>
            {
                var f = mesh.Faces[i]; return moved.Contains(f.A) || moved.Contains(f.B) || moved.Contains(f.C) || moved.Contains(f.D);
            }), "Vertex matching", "Face vertices moved within VT");
        }

        private static void RemoveDegenerateFaces(Mesh mesh, ref int removed, ref int repaired, CleanupDiagnostics diagnostics)
        {
            using var before = mesh.DuplicateMesh();
            int previous = removed;
            removed += mesh.Faces.CullDegenerateFaces();
            int fixedFaces = 0;
            removed += mesh.Faces.RemoveZeroAreaFaces(ref fixedFaces);
            repaired += fixedFaces;
            if (removed != previous || fixedFaces > 0 || before.Faces.QuadCount != mesh.Faces.QuadCount)
                diagnostics.MeshDelta(before, mesh, "Degenerate-face cleanup");
        }

        private static int RemoveDuplicateFaces(Mesh mesh, CleanupDiagnostics diagnostics)
        {
            var indices = mesh.Faces.GetDuplicateFaces();
            diagnostics.AddFaces(CleanupDiagnosticKind.Discarded, mesh, indices, "Duplicate-face cleanup", "Duplicate face");
            return mesh.Faces.DeleteFaces(indices, false);
        }

        private static void CaptureUnusedVertices(Mesh mesh, CleanupDiagnostics diagnostics, string stage)
        {
            var used = new HashSet<int>();
            foreach (var f in mesh.Faces) { used.Add(f.A); used.Add(f.B); used.Add(f.C); used.Add(f.D); }
            for (int i = 0; i < mesh.Vertices.Count; i++)
                if (!used.Contains(i))
                {
                    using var point = new Rhino.Geometry.Point(mesh.Vertices.Point3dAt(i));
                    diagnostics.Add(CleanupDiagnosticKind.Discarded, point, $"{stage}, vertex {i}", "Unused vertex");
                }
        }

        private static double FaceArea(Mesh mesh, MeshFace face)
        {
            var a = mesh.Vertices.Point3dAt(face.A);
            var b = mesh.Vertices.Point3dAt(face.B);
            var c = mesh.Vertices.Point3dAt(face.C);
            double area = Vector3d.CrossProduct(b - a, c - a).Length * 0.5;
            if (face.IsQuad)
                area += Vector3d.CrossProduct(c - a, mesh.Vertices.Point3dAt(face.D) - a).Length * 0.5;
            return area;
        }
    }
}
