using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Ant.GrasshopperPlugIn.Helpers;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.Components
{
    public abstract class MeshToolComponent : GH_Component
    {
        protected MeshToolComponent(string name, string nickname, string description) : base(name, nickname, description, "Ant", "Mesh") { }
        protected abstract string IconKey { get; }
        protected override Bitmap Icon => (Bitmap)Properties.Resources.ResourceManager.GetObject(IconKey);
        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected sealed override void SolveInstance(IGH_DataAccess data)
        {
            Mesh mesh = null; if (!data.GetData(0, ref mesh)) return;
            try { SolveMesh(data, mesh); }
            catch (Exception e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
        }
        protected abstract void SolveMesh(IGH_DataAccess data, Mesh mesh);
        protected static double DefaultTolerance => Rhino.RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;
        protected static void MeshInput(GH_InputParamManager p) => p.AddMeshParameter("Mesh", "M", "Input mesh. Indices are zero-based and refer to this mesh. Graft M to keep per-mesh lists separate.", GH_ParamAccess.item);
        protected static GH_Path MapPath(IGH_DataAccess data) => data.ParameterTargetPath(0).AppendElement(data.Iteration);
        protected static void Limit(double value, string name, double minimum = 0)
        {
            if (!double.IsFinite(value) || value < minimum) throw new ArgumentException(name + " must be finite and at least " + minimum + ".");
        }
    }

    public sealed class MeshQualityComponent : MeshToolComponent
    {
        public MeshQualityComponent() : base("Mesh Quality", "MQ", "Inspect areas, edge ratios, angles and quad warpage; select questionable faces without editing the mesh.") { }
        protected override string IconKey => "MeshQualityIcon";
        public override Guid ComponentGuid => new Guid("b701c172-6bf7-447a-a570-73c23152a541");
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            MeshInput(p);
            p.AddNumberParameter("Maximum edge ratio", "ER", "Flag faces whose longest/shortest edge ratio exceeds this limit. This is an edge ratio, not a solver-specific FEM quality metric.", GH_ParamAccess.item, 10);
            p.AddNumberParameter("Minimum angle", "A", "Flag faces with a corner angle below this value, in degrees (0 to 180).", GH_ParamAccess.item, 10);
            p.AddNumberParameter("Maximum warpage", "W", "Maximum quad vertex distance to the plane of the other three vertices, in model units. Default: document tolerance.", GH_ParamAccess.item); p[3].Optional = true;
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddNumberParameter("Areas", "A", "Per-face area in squared model units; quads use the AC diagonal.", GH_ParamAccess.list);
            p.AddNumberParameter("Edge ratios", "ER", "Longest/shortest edge per face. Infinity for collapsed edges.", GH_ParamAccess.list);
            p.AddNumberParameter("Minimum angles", "MA", "Smallest corner angle per face in degrees.", GH_ParamAccess.list);
            p.AddNumberParameter("Warpage", "W", "Maximum vertex-to-opposite-plane distance; zero for triangles.", GH_ParamAccess.list);
            p.AddIntegerParameter("Flagged face indices", "F", "Original indices of degenerate, concave, folded or threshold-failing faces. Connect to Extract Mesh Faces.", GH_ParamAccess.list);
            p.AddGeometryParameter("Flagged geometry", "D", "Flagged faces, or outlines/points for degenerate faces.", GH_ParamAccess.list);
            p.AddTextParameter("Report", "I", "Counts and thresholds. Geometry checks do not replace solver-specific element validation.", GH_ParamAccess.item);
        }
        protected override void SolveMesh(IGH_DataAccess data, Mesh mesh)
        {
            MeshTools.Validate(mesh, false);
            double ratio = 10, angle = 10, warp = DefaultTolerance;
            data.GetData(1, ref ratio); data.GetData(2, ref angle); data.GetData(3, ref warp);
            Limit(ratio, "Edge ratio", 1); Limit(angle, "Minimum angle"); Limit(warp, "Warpage");
            if (angle > 180) throw new ArgumentException("Minimum angle must not exceed 180 degrees.");
            var metrics = Enumerable.Range(0, mesh.Faces.Count).Select(i => MeshTools.Metrics(mesh, i)).ToArray();
            var bad = Enumerable.Range(0, metrics.Length).Where(i => metrics[i].Degenerate || metrics[i].EdgeRatio > ratio || metrics[i].MinimumAngle < angle || metrics[i].Warpage > warp).ToArray();
            var diagnostics = new CleanupDiagnostics(); diagnostics.AddFaces(CleanupDiagnosticKind.Unresolved, mesh, bad, "Mesh Quality", "Face failed quality checks");
            data.SetDataList(0, metrics.Select(m => m.Area)); data.SetDataList(1, metrics.Select(m => m.EdgeRatio));
            data.SetDataList(2, metrics.Select(m => m.MinimumAngle)); data.SetDataList(3, metrics.Select(m => m.Warpage));
            data.SetDataList(4, bad); data.SetDataList(5, diagnostics.Unresolved);
            data.SetData(6, FormattableString.Invariant($"Faces: {metrics.Length}; flagged: {bad.Length}; max edge ratio: {ratio:G6}; min angle: {angle:G6} deg; max warpage: {warp:G6} model units."));
        }
    }

    public sealed class MeshTopologyComponent : MeshToolComponent
    {
        public MeshTopologyComponent() : base("Mesh Topology", "MT", "Inspect vertex/face connectivity, unique edges, naked borders and non-manifold edges.") { }
        protected override string IconKey => "MeshTopologyIcon";
        public override Guid ComponentGuid => new Guid("acbfae52-d8ac-45b5-a056-8750f034e2f7");
        protected override void RegisterInputParams(GH_InputParamManager p) => MeshInput(p);
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddPointParameter("Vertices", "V", "Raw mesh vertices in original index order.", GH_ParamAccess.list);
            p.AddIntegerParameter("Face vertices", "FV", "Raw vertex indices, one branch per face; triangles contain three indices.", GH_ParamAccess.tree);
            p.AddPointParameter("Topology vertices", "TV", "Topology vertices, shared by coincident unwelded vertices.", GH_ParamAccess.list);
            p.AddLineParameter("Edges", "E", "Unique topology edges in index order.", GH_ParamAccess.list);
            p.AddIntegerParameter("Edge vertices", "EV", "Topology vertex indices, one branch per edge.", GH_ParamAccess.tree);
            p.AddIntegerParameter("Edge faces", "EF", "Adjacent face indices, one branch per edge.", GH_ParamAccess.tree);
            p.AddLineParameter("Naked edges", "N", "Edges with exactly one adjacent face.", GH_ParamAccess.list);
            p.AddLineParameter("Non-manifold edges", "NM", "Edges with more than two adjacent faces.", GH_ParamAccess.list);
            p.AddCurveParameter("Boundary loops", "B", "Joined naked boundary polylines returned by Rhino.", GH_ParamAccess.list);
            p.AddTextParameter("Report", "I", "Topology counts; the edge lists classify edge incidence only.", GH_ParamAccess.item);
        }
        protected override void SolveMesh(IGH_DataAccess data, Mesh mesh)
        {
            MeshTools.Validate(mesh); var root = MapPath(data);
            var fv = new DataTree<int>(); var ev = new DataTree<int>(); var ef = new DataTree<int>();
            var edges = new List<Line>(); var naked = new List<Line>(); var nonManifold = new List<Line>();
            for (int f = 0; f < mesh.Faces.Count; f++) fv.AddRange(MeshTools.VertexIds(mesh.Faces[f]), root.AppendElement(f));
            for (int e = 0; e < mesh.TopologyEdges.Count; e++)
            {
                var line = mesh.TopologyEdges.EdgeLine(e); var pair = mesh.TopologyEdges.GetTopologyVertices(e); var adjacent = mesh.TopologyEdges.GetConnectedFaces(e);
                edges.Add(line); ev.AddRange(new[] { pair.I, pair.J }, root.AppendElement(e)); ef.AddRange(adjacent, root.AppendElement(e));
                if (adjacent.Length == 1) naked.Add(line); else if (adjacent.Length > 2) nonManifold.Add(line);
            }
            var loops = mesh.Faces.Count == 0 ? Array.Empty<Polyline>() : mesh.GetNakedEdges() ?? Array.Empty<Polyline>();
            data.SetDataList(0, mesh.Vertices.ToPoint3dArray()); data.SetDataTree(1, fv);
            data.SetDataList(2, Enumerable.Range(0, mesh.TopologyVertices.Count)
                .Select(i => mesh.Vertices.Point3dAt(mesh.TopologyVertices.MeshVertexIndices(i)[0])));
            data.SetDataList(3, edges); data.SetDataTree(4, ev); data.SetDataTree(5, ef);
            data.SetDataList(6, naked); data.SetDataList(7, nonManifold); data.SetDataList(8, loops.Select(p => new PolylineCurve(p)));
            data.SetData(9, $"Vertices: {mesh.Vertices.Count}; topology vertices: {mesh.TopologyVertices.Count}; faces: {mesh.Faces.Count}; edges: {edges.Count}; naked: {naked.Count}; non-manifold edges: {nonManifold.Count}; closed: {mesh.IsClosed}.");
        }
    }

    public sealed class MeshPartsComponent : MeshToolComponent
    {
        public MeshPartsComponent() : base("Mesh Parts", "MP", "Split a mesh into parts connected through topology edges, with original face and vertex indices.") { }
        protected override string IconKey => "MeshPartsIcon";
        public override Guid ComponentGuid => new Guid("9dcc5ab0-f98d-476c-b16d-7d52d9e5ff30");
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            MeshInput(p); p.AddBooleanParameter("Largest first", "S", "Sort parts by descending area. Equal areas retain original face order.", GH_ParamAccess.item, true);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Parts", "M", "One part per branch, ordered as requested.", GH_ParamAccess.tree);
            p.AddIntegerParameter("Source faces", "F", "Original face indices in each output mesh's face order.", GH_ParamAccess.tree);
            p.AddIntegerParameter("Source vertices", "V", "Original vertex indices in each output mesh's vertex order.", GH_ParamAccess.tree);
            p.AddNumberParameter("Areas", "A", "Area of each part in squared model units.", GH_ParamAccess.tree);
            p.AddTextParameter("Report", "I", "Connectivity and extraction notes.", GH_ParamAccess.item);
        }
        protected override void SolveMesh(IGH_DataAccess data, Mesh mesh)
        {
            bool sort = true; data.GetData(1, ref sort);
            var groups = MeshTools.ConnectedFaces(mesh).Select(g => (faces: g, area: g.Sum(i => MeshTools.FaceArea(mesh, i)))).ToList();
            if (sort) groups = groups.OrderByDescending(g => g.area).ToList();
            var parts = new DataTree<GH_Mesh>(); var faces = new DataTree<int>(); var vertices = new DataTree<int>(); var areas = new DataTree<double>();
            for (int i = 0; i < groups.Count; i++)
            {
                var part = MeshTools.Extract(mesh, groups[i].faces, false); var path = MapPath(data).AppendElement(i);
                parts.Add(new GH_Mesh(part.Mesh), path); faces.AddRange(part.Faces, path); vertices.AddRange(part.Vertices, path); areas.Add(groups[i].area, path);
            }
            data.SetDataTree(0, parts); data.SetDataTree(1, faces); data.SetDataTree(2, vertices); data.SetDataTree(3, areas);
            data.SetData(4, $"Parts: {groups.Count}. Connectivity uses shared topology edges; a vertex-only contact remains separate. Colors, UVs and user strings are retained; n-gon groups and orphan vertices are omitted.");
        }
    }

    public sealed class MeshExtractFacesComponent : MeshToolComponent
    {
        public MeshExtractFacesComponent() : base("Extract Mesh Faces", "MEF", "Separate selected faces from the remainder, preserving maps back to original face and vertex indices.") { }
        protected override string IconKey => "MeshExtractFacesIcon";
        public override Guid ComponentGuid => new Guid("2d827e95-ff8b-46ce-a8bd-efc19a53c454");
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            MeshInput(p); p.AddIntegerParameter("Face indices", "F", "Zero-based input face indices; duplicates are ignored. Empty selects nothing.", GH_ParamAccess.list); p[1].Optional = true;
            p.AddBooleanParameter("Invert", "I", "Invert the face selection.", GH_ParamAccess.item, false);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Selected", "S", "Selected faces. Face order follows the original mesh.", GH_ParamAccess.item);
            p.AddMeshParameter("Remainder", "R", "Unselected faces.", GH_ParamAccess.item);
            p.AddIntegerParameter("Selected source faces", "SF", "Original face index for every selected face.", GH_ParamAccess.list);
            p.AddIntegerParameter("Remainder source faces", "RF", "Original face index for every remaining face.", GH_ParamAccess.list);
            p.AddIntegerParameter("Selected source vertices", "SV", "Original vertex index for every selected mesh vertex.", GH_ParamAccess.list);
            p.AddIntegerParameter("Remainder source vertices", "RV", "Original vertex index for every remaining mesh vertex.", GH_ParamAccess.list);
            p.AddTextParameter("Report", "L", "Extraction counts and attribute notes.", GH_ParamAccess.item);
        }
        protected override void SolveMesh(IGH_DataAccess data, Mesh mesh)
        {
            MeshTools.Validate(mesh, false); var ids = new List<int>(); data.GetDataList(1, ids); bool invert = false; data.GetData(2, ref invert);
            if (ids.Any(i => i < 0 || i >= mesh.Faces.Count)) throw new ArgumentException("A face index is outside the input mesh. No partial selection was returned.");
            var chosen = new HashSet<int>(ids);
            var selected = Enumerable.Range(0, mesh.Faces.Count).Where(i => chosen.Contains(i) != invert).ToArray();
            var other = Enumerable.Range(0, mesh.Faces.Count).Where(i => chosen.Contains(i) == invert).ToArray();
            var a = MeshTools.Extract(mesh, selected, false); MeshTools.Selection b;
            try { b = MeshTools.Extract(mesh, other, false); } catch { a.Mesh.Dispose(); throw; }
            data.SetData(0, a.Mesh); data.SetData(1, b.Mesh); data.SetDataList(2, a.Faces); data.SetDataList(3, b.Faces);
            data.SetDataList(4, a.Vertices); data.SetDataList(5, b.Vertices);
            data.SetData(6, $"Selected: {a.Faces.Length}; remainder: {b.Faces.Length}. Colors, UVs and user strings retained; n-gon groups and orphan vertices omitted. Input unchanged.");
        }
    }

    public sealed class MeshNormalsComponent : MeshToolComponent
    {
        public MeshNormalsComponent() : base("Orient Mesh Normals", "MN", "Unify adjacent face orientation, orient each closed part outward and optionally flip the result.") { }
        protected override string IconKey => "MeshNormalsIcon";
        public override Guid ComponentGuid => new Guid("3da2ac11-2de9-49d4-8fbc-950db9bdaef2");
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            MeshInput(p); p.AddBooleanParameter("Outward", "O", "Orient each closed part outward independently. Open surfaces have no automatic outside.", GH_ParamAccess.item, true);
            p.AddBooleanParameter("Flip", "F", "Flip all faces after unification/outward orientation.", GH_ParamAccess.item, false);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Mesh", "M", "Mesh with updated orientation. Original vertex and face indices are retained.", GH_ParamAccess.item);
            p.AddVectorParameter("Face normals", "N", "Unit face normals in face index order.", GH_ParamAccess.list);
            p.AddTextParameter("Report", "I", "Orientation changes and parts with no defined outside.", GH_ParamAccess.item);
        }
        protected override void SolveMesh(IGH_DataAccess data, Mesh mesh)
        {
            bool outward = true, flip = false; data.GetData(1, ref outward); data.GetData(2, ref flip);
            var result = MeshTools.Orient(mesh, outward, flip);
            data.SetData(0, result.mesh); data.SetDataList(1, Enumerable.Range(0, result.mesh.Faces.Count).Select(i => MeshTools.FaceNormal(result.mesh, i))); data.SetData(2, result.report);
        }
    }

    public sealed class MeshTriangulateComponent : MeshToolComponent
    {
        public MeshTriangulateComponent() : base("Triangulate Mesh", "MTR", "Split quads along the shortest valid diagonal, retaining source face indices and all original vertex indices.") { }
        protected override string IconKey => "MeshTriangulateIcon";
        public override Guid ComponentGuid => new Guid("a67c2695-cbb8-491e-a9b5-c6063ee7fbe4");
        protected override void RegisterInputParams(GH_InputParamManager p) => MeshInput(p);
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Mesh", "M", "Triangulated copy; original vertices and their attributes are retained.", GH_ParamAccess.item);
            p.AddIntegerParameter("Source faces", "F", "Input face index for each output triangle; a quad produces two entries.", GH_ParamAccess.list);
            p.AddTextParameter("Report", "I", "Counts and mapping notes.", GH_ParamAccess.item);
        }
        protected override void SolveMesh(IGH_DataAccess data, Mesh mesh)
        {
            var result = MeshTools.Triangulate(mesh); data.SetData(0, result.mesh); data.SetDataList(1, result.sourceFaces);
            data.SetData(2, $"Faces: {mesh.Faces.Count} -> {result.mesh.Faces.Count}; split quads: {mesh.Faces.QuadCount}. Vertex indices retained; n-gon grouping cleared.");
        }
    }

    public abstract class MeshMatchComponent : GH_Component
    {
        protected abstract bool MatchFaces { get; }
        protected MeshMatchComponent(string name, string nickname, string description) : base(name, nickname, description, "Ant", "Mesh") { }
        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        protected override Bitmap Icon => (Bitmap)Properties.Resources.ResourceManager.GetObject(MatchFaces ? "MeshMatchFacesIcon" : "MeshMatchNodesIcon");
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddMeshParameter("Meshes", "M", "Meshes to search, in list order; indices are local to this list/branch.", GH_ParamAccess.list);
            p.AddPointParameter("Points", "P", "Query points. Outputs preserve the same order, including unmatched entries.", GH_ParamAccess.list);
            p.AddNumberParameter("Maximum distance", "D", "Search radius in model units. Default: document tolerance. Zero accepts exact matches only.", GH_ParamAccess.item); p[2].Optional = true;
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddIntegerParameter("Mesh indices", "M", "Matched input mesh index; -1 when unmatched.", GH_ParamAccess.list);
            p.AddIntegerParameter("Element indices", MatchFaces ? "F" : "V", "Matched face or raw vertex index in the selected mesh; -1 when unmatched.", GH_ParamAccess.list);
            p.AddPointParameter("Closest points", "P", "Closest points; null for unmatched queries.", GH_ParamAccess.list);
            p.AddNumberParameter("Distances", "D", "Euclidean distances in model units; NaN when unmatched.", GH_ParamAccess.list);
            p.AddBooleanParameter("Matched", "OK", "Whether each query found a match inside the distance limit.", GH_ParamAccess.list);
            p.AddTextParameter("Report", "I", "Search method and unmatched count.", GH_ParamAccess.item);
        }
        protected override void SolveInstance(IGH_DataAccess data)
        {
            var meshes = new List<Mesh>(); var points = new List<Point3d>();
            if (!data.GetDataList(0, meshes) || !data.GetDataList(1, points)) return;
            double distance = Rhino.RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001; data.GetData(2, ref distance);
            try
            {
                var result = MeshTools.MatchPoints(meshes, points, distance, MatchFaces);
                data.SetDataList(0, result.Select(r => r.Mesh)); data.SetDataList(1, result.Select(r => r.Element));
                data.SetDataList(2, result.Select(r => r.Mesh < 0 ? null : new GH_Point(r.Point)));
                data.SetDataList(3, result.Select(r => r.Distance)); data.SetDataList(4, result.Select(r => r.Mesh >= 0));
                data.SetData(5, $"Queries: {result.Length}; unmatched: {result.Count(r => r.Mesh < 0)}. " + (MatchFaces ? "RTree mesh candidates + Rhino closest point on actual faces (not centroids)." : "RTree vertex matching; distance ties choose the first mesh/vertex."));
            }
            catch (Exception e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
        }
    }

    public sealed class MeshMatchNodesComponent : MeshMatchComponent
    {
        public MeshMatchNodesComponent() : base("Match Mesh Nodes", "MMN", "Match points to the closest mesh vertices within a distance limit using an RTree.") { }
        protected override bool MatchFaces => false;
        public override Guid ComponentGuid => new Guid("384a3a7c-eccd-41de-bb05-721baf451df2");
    }

    public sealed class MeshMatchFacesComponent : MeshMatchComponent
    {
        public MeshMatchFacesComponent() : base("Match Mesh Faces", "MMF", "Find the closest point on mesh faces within a distance limit and return original mesh/face indices.") { }
        protected override bool MatchFaces => true;
        public override Guid ComponentGuid => new Guid("669167c6-9a0a-451e-9404-602d6a1b1274");
    }
}
