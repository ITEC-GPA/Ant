using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.Helpers
{
    internal static class MeshTools
    {
        internal sealed class Selection
        {
            internal Mesh Mesh;
            internal int[] Faces;
            internal int[] Vertices;
        }

        internal readonly record struct FaceMetrics(double Area, double EdgeRatio, double MinimumAngle, double Warpage, bool Degenerate);

        internal static int[] VertexIds(MeshFace f) => f.IsTriangle ? new[] { f.A, f.B, f.C } : new[] { f.A, f.B, f.C, f.D };

        internal static void Validate(Mesh mesh, bool requireValid = true)
        {
            if (mesh == null) throw new ArgumentException("Mesh is null.");
            for (int i = 0; i < mesh.Vertices.Count; i++)
                if (!mesh.Vertices.Point3dAt(i).IsValid) throw new ArgumentException($"Vertex {i} has invalid coordinates. Use Clean and Merge Mesh first.");
            for (int i = 0; i < mesh.Faces.Count; i++)
                foreach (int id in VertexIds(mesh.Faces[i]))
                    if (id < 0 || id >= mesh.Vertices.Count) throw new ArgumentException($"Face {i} has an invalid vertex index. Use Clean and Merge Mesh first.");
            if (requireValid && mesh.Faces.Count > 0 && !mesh.IsValid)
                throw new ArgumentException("Mesh is invalid. Use Mesh Quality or Clean and Merge Mesh first.");
        }

        internal static Vector3d FaceNormal(Mesh mesh, int faceId)
        {
            var f = mesh.Faces[faceId]; var a = mesh.Vertices.Point3dAt(f.A);
            var normal = Vector3d.CrossProduct(mesh.Vertices.Point3dAt(f.B) - a, mesh.Vertices.Point3dAt(f.C) - a);
            if (f.IsQuad) normal += Vector3d.CrossProduct(mesh.Vertices.Point3dAt(f.C) - a, mesh.Vertices.Point3dAt(f.D) - a);
            normal.Unitize(); return normal;
        }

        internal static double FaceArea(Mesh mesh, int faceId)
        {
            var f = mesh.Faces[faceId]; var a = mesh.Vertices.Point3dAt(f.A); var c = mesh.Vertices.Point3dAt(f.C);
            double area = Vector3d.CrossProduct(mesh.Vertices.Point3dAt(f.B) - a, c - a).Length * 0.5;
            if (f.IsQuad) area += Vector3d.CrossProduct(c - a, mesh.Vertices.Point3dAt(f.D) - a).Length * 0.5;
            return area;
        }

        internal static FaceMetrics Metrics(Mesh mesh, int faceId)
        {
            var points = VertexIds(mesh.Faces[faceId]).Select(mesh.Vertices.Point3dAt).ToArray();
            int n = points.Length; var edges = new Vector3d[n];
            double min = double.PositiveInfinity, max = 0, angle = 180;
            for (int i = 0; i < n; i++)
            {
                edges[i] = points[(i + 1) % n] - points[i];
                double length = edges[i].Length; min = Math.Min(min, length); max = Math.Max(max, length);
            }
            var cross = Vector3d.CrossProduct(points[1] - points[0], points[2] - points[0]);
            double area = cross.Length * 0.5;
            if (n == 4) area += Vector3d.CrossProduct(points[2] - points[0], points[3] - points[0]).Length * 0.5;
            bool degenerate = min == 0 || area == 0 || !double.IsFinite(area);
            var normal = FaceNormal(mesh, faceId);
            for (int i = 0; i < n; i++)
            {
                var incoming = -edges[(i + n - 1) % n]; var outgoing = edges[i];
                if (!incoming.Unitize() || !outgoing.Unitize()) { angle = 0; degenerate = true; continue; }
                angle = Math.Min(angle, Math.Acos(Math.Clamp(incoming * outgoing, -1, 1)) * 180 / Math.PI);
                // Reflex, folded and collinear corners must not pass the aspect/angle filters.
                if (Vector3d.CrossProduct(-incoming, outgoing) * normal <= 0) degenerate = true;
            }
            double warp = 0;
            if (n == 4)
            {
                // Largest distance from one vertex to the plane of the other three.
                for (int i = 0; i < 4; i++)
                {
                    var a = points[(i + 1) % 4];
                    var axis = Vector3d.CrossProduct(points[(i + 2) % 4] - a, points[(i + 3) % 4] - a);
                    if (axis.Unitize()) warp = Math.Max(warp, Math.Abs((points[i] - a) * axis));
                    else degenerate = true;
                }
            }
            return new FaceMetrics(area, min > 0 ? max / min : double.PositiveInfinity, angle, warp, degenerate);
        }

        internal static Selection Extract(Mesh source, IEnumerable<int> faceIds, bool validateSource = true)
        {
            if (validateSource) Validate(source, false);
            var ids = faceIds.Distinct().OrderBy(i => i).ToArray();
            if (ids.Any(i => i < 0 || i >= source.Faces.Count)) throw new ArgumentOutOfRangeException(nameof(faceIds), "Face indices must refer to the input mesh (zero-based).");
            var vertices = ids.SelectMany(i => VertexIds(source.Faces[i])).Distinct().OrderBy(i => i).ToArray();
            var map = new Dictionary<int, int>(vertices.Length);
            var mesh = new Mesh(); mesh.Vertices.UseDoublePrecisionVertices = true;
            try
            {
                bool colors = source.VertexColors.Count == source.Vertices.Count;
                bool textures = source.TextureCoordinates.Count == source.Vertices.Count;
                bool normals = source.Normals.Count == source.Vertices.Count;
                for (int i = 0; i < vertices.Length; i++)
                {
                    int old = vertices[i]; map.Add(old, i); mesh.Vertices.Add(source.Vertices.Point3dAt(old));
                    if (colors) mesh.VertexColors.Add(source.VertexColors[old]);
                    if (textures) mesh.TextureCoordinates.Add(source.TextureCoordinates[old]);
                    if (normals) mesh.Normals.Add(source.Normals[old]);
                }
                foreach (int id in ids)
                {
                    var f = source.Faces[id];
                    if (f.IsTriangle) mesh.Faces.AddFace(map[f.A], map[f.B], map[f.C]);
                    else mesh.Faces.AddFace(map[f.A], map[f.B], map[f.C], map[f.D]);
                }
                var strings = source.GetUserStrings();
                foreach (string key in strings.AllKeys) mesh.SetUserString(key, strings[key]);
                mesh.FaceNormals.ComputeFaceNormals();
                if (!normals) mesh.Normals.ComputeNormals();
                return new Selection { Mesh = mesh, Faces = ids, Vertices = vertices };
            }
            catch { mesh.Dispose(); throw; }
        }

        internal static List<int[]> ConnectedFaces(Mesh mesh)
        {
            Validate(mesh);
            var parent = Enumerable.Range(0, mesh.Faces.Count).ToArray();
            int Root(int i)
            {
                while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
                return i;
            }
            for (int e = 0; e < mesh.TopologyEdges.Count; e++)
            {
                var connected = mesh.TopologyEdges.GetConnectedFaces(e);
                for (int i = 1; i < connected.Length; i++)
                {
                    int a = Root(connected[0]), b = Root(connected[i]);
                    if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
                }
            }
            // A vertex-only contact does not connect two surface parts.
            return Enumerable.Range(0, mesh.Faces.Count).GroupBy(Root).Select(g => g.ToArray()).ToList();
        }

        internal static (Mesh mesh, int[] sourceFaces) Triangulate(Mesh source)
        {
            Validate(source);
            var mesh = source.DuplicateMesh();
            try
            {
                mesh.Faces.Clear(); mesh.Ngons.Clear(); mesh.FaceNormals.Clear();
                var mapping = new List<int>();
                for (int i = 0; i < source.Faces.Count; i++)
                {
                    var f = source.Faces[i];
                    if (f.IsTriangle) { mesh.Faces.AddFace(f); mapping.Add(i); continue; }
                    var a = source.Vertices.Point3dAt(f.A); var b = source.Vertices.Point3dAt(f.B);
                    var c = source.Vertices.Point3dAt(f.C); var d = source.Vertices.Point3dAt(f.D);
                    var normal = FaceNormal(source, i);
                    bool ac = Vector3d.CrossProduct(b - a, c - a) * normal > 0 && Vector3d.CrossProduct(c - a, d - a) * normal > 0;
                    bool bd = Vector3d.CrossProduct(b - a, d - a) * normal > 0 && Vector3d.CrossProduct(c - b, d - b) * normal > 0;
                    if (!ac && !bd) throw new ArgumentException($"Quad {i} is folded or degenerate and cannot be triangulated safely.");
                    if (ac && (!bd || a.DistanceTo(c) <= b.DistanceTo(d)))
                    { mesh.Faces.AddFace(f.A, f.B, f.C); mesh.Faces.AddFace(f.A, f.C, f.D); }
                    else { mesh.Faces.AddFace(f.A, f.B, f.D); mesh.Faces.AddFace(f.B, f.C, f.D); }
                    mapping.Add(i); mapping.Add(i);
                }
                mesh.FaceNormals.ComputeFaceNormals(); mesh.Normals.ComputeNormals();
                return (mesh, mapping.ToArray());
            }
            catch { mesh.Dispose(); throw; }
        }

        internal static (Mesh mesh, string report) Orient(Mesh source, bool outward, bool flip)
        {
            Validate(source);
            var mesh = source.DuplicateMesh();
            try
            {
                int unified = mesh.Faces.Count > 0 ? mesh.UnifyNormals() : 0;
                int turned = 0, open = 0, unknown = 0;
                if (outward)
                    foreach (var faces in ConnectedFaces(mesh))
                    {
                        using var part = Extract(mesh, faces, false).Mesh;
                        if (!part.IsClosed) { open++; continue; }
                        int orientation = part.SolidOrientation();
                        if (orientation == 0) { unknown++; continue; }
                        if (orientation >= 0) continue;
                        foreach (int id in faces)
                        {
                            var f = mesh.Faces[id];
                            mesh.Faces.SetFace(id, f.IsTriangle ? new MeshFace(f.A, f.C, f.B) : new MeshFace(f.A, f.D, f.C, f.B));
                        }
                        turned++;
                    }
                if (flip) mesh.Flip(true, true, true);
                mesh.FaceNormals.ComputeFaceNormals(); mesh.Normals.ComputeNormals();
                return (mesh, $"Unified faces: {unified}; closed parts turned outward: {turned}; open parts with no outside: {open}; indeterminate closed parts: {unknown}; final flip: {flip}.");
            }
            catch { mesh.Dispose(); throw; }
        }

        internal readonly record struct Match(int Mesh, int Element, Point3d Point, double Distance, Vector3d Normal)
        {
            internal static Match Missing => new Match(-1, -1, Point3d.Unset, double.NaN, Vector3d.Unset);
        }

        internal static Match[] MatchPoints(IList<Mesh> meshes, IList<Point3d> points, double tolerance, bool faces)
        {
            if (!double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance), "Maximum distance must be finite and non-negative.");
            foreach (var mesh in meshes) Validate(mesh, faces);
            using var nodes = new PointIndex();
            using var bounds = new BoundsIndex();
            var sources = new List<(int mesh, int vertex)>();
            for (int m = 0; m < meshes.Count; m++)
                if (faces) { if (meshes[m].Faces.Count > 0) bounds.Add(meshes[m].GetBoundingBox(true), m); }
                else for (int v = 0; v < meshes[m].Vertices.Count; v++) { nodes.Add(meshes[m].Vertices.Point3dAt(v)); sources.Add((m, v)); }
            var result = new Match[points.Count];
            for (int p = 0; p < points.Count; p++)
            {
                result[p] = Match.Missing;
                if (!points[p].IsValid) continue;
                if (!faces)
                {
                    int id = nodes.Nearest(points[p], tolerance, out double distance);
                    if (id >= 0) result[p] = new Match(sources[id].mesh, sources[id].vertex, nodes.Points[id], distance, Vector3d.Zero);
                    continue;
                }
                foreach (int m in bounds.Search(new BoundingBox(points[p], points[p]), tolerance))
                {
                    // Rhino performs the exact closest-point search with its cached mesh spatial tree.
                    var hit = meshes[m].ClosestMeshPoint(points[p], tolerance);
                    if (hit == null) continue;
                    double distance = points[p].DistanceTo(hit.Point);
                    if (distance > tolerance || (result[p].Mesh >= 0 && distance >= result[p].Distance)) continue;
                    result[p] = new Match(m, hit.FaceIndex, hit.Point, distance, FaceNormal(meshes[m], hit.FaceIndex));
                }
            }
            return result;
        }
    }
}
