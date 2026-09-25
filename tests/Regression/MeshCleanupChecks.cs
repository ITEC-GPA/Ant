using System;
using System.Linq;
using Ant.GrasshopperPlugIn.Components;
using Ant.GrasshopperPlugIn.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

internal static class MeshCleanupChecks
{
    internal static void Run(Action<string, Action> check)
    {
        check("clean mesh: merge nearby seams without changing the inputs", () =>
        {
            using var left = Quad(0, 1);
            using var right = Quad(1.00005, 2);
            var before = right.Vertices.ToPoint3dArray();
            var result = MeshCleanup.Clean(new[] { left, right }, 0.0001, 0, 0);
            using var mesh = result.Mesh;
            Assert(mesh.IsValid && mesh.Faces.Count == 2 && mesh.Vertices.Count == 6, "Seam vertices were not merged");
            Assert(mesh.Faces.QuadCount == 2, "Healthy quads were triangulated");
            Assert(Enumerable.Range(0, mesh.TopologyEdges.Count).Count(i => mesh.TopologyEdges.GetConnectedFaces(i).Length == 2) == 1, "Meshes do not share a topological edge");
            Assert(left.Vertices.Count == 4 && right.Vertices.ToPoint3dArray().SequenceEqual(before), "Input mesh changed");
        });
        check("clean mesh: duplicate reversed faces and unused vertices", () =>
        {
            using var original = Triangle(new Point3d(0,0,0), new Point3d(1,0,0), new Point3d(0,1,0));
            original.Vertices.Add(50,50,50);
            using var reversed = original.DuplicateMesh();
            reversed.Flip(true, true, true);
            var result = MeshCleanup.Clean(new[] { original, reversed }, 0, 0, 0);
            using var mesh = result.Mesh;
            Assert(mesh.IsValid && mesh.Faces.Count == 1 && mesh.Vertices.Count == 3, "Duplicate face or unused vertex survived");
        });
        check("clean mesh: remove collinear faces and repair a collapsed quad", () =>
        {
            using var line = Triangle(new Point3d(0,0,0), new Point3d(1,0,0), new Point3d(2,0,0));
            using var quad = new Mesh();
            quad.Vertices.Add(0,0,0); quad.Vertices.Add(0,0,0); quad.Vertices.Add(1,0,0); quad.Vertices.Add(0,1,0);
            quad.Faces.AddFace(0,1,2,3);
            var result = MeshCleanup.Clean(new[] { line, quad }, 0, 0, 0);
            using var mesh = result.Mesh;
            Assert(mesh.IsValid && mesh.Faces.Count == 1 && mesh.Faces.TriangleCount == 1, "Degenerate geometry was not repaired");
        });
        check("clean mesh: face area uses squared model units", () =>
        {
            using var big = Quad(0, 1);
            using var small = Triangle(new Point3d(3,0,0), new Point3d(3.01,0,0), new Point3d(3,0.01,0));
            var result = MeshCleanup.Clean(new[] { big, small }, 0, 0, 0.0001);
            using var mesh = result.Mesh;
            Assert(mesh.IsValid && mesh.Faces.Count == 1 && mesh.Faces[0].IsQuad, "Wrong faces removed by area");
        });
        check("clean mesh: short edges collapse with adjacent-face repair", () =>
        {
            using var input = new Mesh();
            input.Vertices.Add(0,0,0); input.Vertices.Add(0.01,0,0); input.Vertices.Add(1,1,0); input.Vertices.Add(0,1,0);
            input.Faces.AddFace(0,1,2); input.Faces.AddFace(0,2,3);
            var result = MeshCleanup.Clean(new[] { input }, 0, 0.02, 0);
            using var mesh = result.Mesh;
            Assert(mesh.IsValid && mesh.Faces.Count > 0, "Collapse destroyed healthy adjacent geometry");
            Assert(Enumerable.Range(0,mesh.TopologyEdges.Count).All(i=>mesh.TopologyEdges.EdgeLine(i).Length >= 0.02), "Short edge survived");
        });
        check("clean mesh: zero thresholds preserve close but distinct points", () =>
        {
            using var a = Quad(0,1); using var b = Quad(1.00005,2);
            var result = MeshCleanup.Clean(new[] { a,b }, 0,0,0);
            using var mesh=result.Mesh;
            Assert(mesh.Vertices.Count==8 && mesh.Faces.Count==2,"Zero VT still snapped distinct vertices");
        });
        check("clean mesh: invalid parameters fail without changing inputs", () =>
        {
            using var input=Quad(0,1);
            foreach(var values in new[]{new[]{-1.0,0,0},new[]{0.0,double.NaN,0},new[]{0.0,0,double.PositiveInfinity}})
            {
                bool rejected=false;
                try { MeshCleanup.Clean(new[]{input},values[0],values[1],values[2]).Mesh.Dispose(); }
                catch(ArgumentOutOfRangeException) { rejected=true; }
                Assert(rejected,"Invalid tolerance accepted");
            }
            Assert(input.Vertices.Count==4 && input.Faces.Count==1,"Input mutated on failure");
        });
        check("clean mesh: all faces removed returns an empty result and warning", () =>
        {
            using var input=Quad(0,1);
            var result=MeshCleanup.Clean(new Mesh[]{null,input},0,0,2);
            using var mesh=result.Mesh;
            Assert(mesh.Faces.Count==0 && mesh.Vertices.Count==0 && result.Warnings.Count>0,"Empty result not handled");
        });
        check("clean mesh: Grasshopper component accepts a list and returns mesh/report", () =>
        {
            using var doc=new GH_Document();
            var component=new CleanMergeMeshComponent(); component.CreateAttributes(); doc.AddObject(component,false);
            var param=(Param_Mesh)component.Params.Input[0];
            param.PersistentData.Append(new GH_Mesh(Quad(0,1)));
            param.PersistentData.Append(new GH_Mesh(Quad(1,2)));
            GH_Document.EnableSolutions=true; doc.Enabled=true; component.ExpireSolution(false); doc.NewSolution(false);
            Assert(component.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0,"Component reported an error");
            var output=component.Params.Output[0].VolatileData.AllData(true).OfType<GH_Mesh>().Single().Value;
            Assert(output.IsValid && output.Faces.Count==2 && output.Vertices.Count==6,"Incorrect list result");
            Assert(component.Params.Output[1].VolatileDataCount==1,"Missing cleanup report");
        });
    }

    private static void Assert(bool value,string message) { if(!value) throw new Exception(message); }
    private static Mesh Quad(double x0,double x1)
    {
        var mesh=new Mesh(); mesh.Vertices.UseDoublePrecisionVertices=true;
        mesh.Vertices.Add(x0,0,0); mesh.Vertices.Add(x1,0,0); mesh.Vertices.Add(x1,1,0); mesh.Vertices.Add(x0,1,0);
        mesh.Faces.AddFace(0,1,2,3); return mesh;
    }
    private static Mesh Triangle(Point3d a,Point3d b,Point3d c)
    {
        var mesh=new Mesh(); mesh.Vertices.UseDoublePrecisionVertices=true;
        mesh.Vertices.Add(a); mesh.Vertices.Add(b); mesh.Vertices.Add(c); mesh.Faces.AddFace(0,1,2); return mesh;
    }
}
