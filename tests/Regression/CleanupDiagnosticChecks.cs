using System;
using System.Linq;
using Ant.GrasshopperPlugIn.Components;
using Ant.GrasshopperPlugIn.DataTypes;
using Ant.GrasshopperPlugIn.Helpers;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

static class CleanupDiagnosticChecks
{
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static Point3d P(double x,double y,double z=0) => new Point3d(x,y,z);
    static Brep Rect() => Brep.CreateFromCornerPoints(P(0,0),P(10,0),P(10,10),P(0,10),1E-7);
    static Brep Hole()
    {
        using var outer=new Rectangle3d(Plane.WorldXY,new Interval(0,10),new Interval(0,10)).ToNurbsCurve();
        using var inner=new Rectangle3d(Plane.WorldXY,new Interval(4,6),new Interval(4,6)).ToNurbsCurve();
        return Brep.CreatePlanarBreps(new[]{outer,inner},1E-7).Single();
    }
    static Mesh Triangle(Point3d a,Point3d b,Point3d c)
    { var mesh=new Mesh(); mesh.Vertices.UseDoublePrecisionVertices=true; mesh.Vertices.Add(a); mesh.Vertices.Add(b); mesh.Vertices.Add(c); mesh.Faces.AddFace(0,1,2); return mesh; }
    static void Solve(GH_Document doc,GH_Component component)
    { GH_Document.EnableSolutions=true; doc.Enabled=true; component.ExpireSolution(false); doc.NewSolution(false); }

    internal static void Run(Action<string,Action> check)
    {
        check("diagnostics: healthy input produces no false change markers",()=>
        {
            using var s=Rect(); var r=PlanarGeometryCleanup.Clean(new[]{s},new IGH_GeometricGoo[]{new GH_Point(P(2,2))},null,0.01,0.02,0.001,false);
            Assert(r.Diagnostics.Changed.Count==0 && r.Diagnostics.Discarded.Count==0 && r.Diagnostics.Unresolved.Count==0,"Unchanged geometry marked as modified");
            using var m=Triangle(P(0,0),P(1,0),P(0,1)); var clean=MeshCleanup.Clean(new[]{m},0,0,0);
            Assert(clean.Diagnostics.Changed.Count==0 && clean.Diagnostics.Discarded.Count==0 && clean.Diagnostics.Unresolved.Count==0,"Healthy mesh marked as modified");
        });
        check("diagnostics: projection records modified output and source labels",()=>
        {
            using var s=Rect(); var r=PlanarGeometryCleanup.Clean(new[]{s},new IGH_GeometricGoo[]{new GH_Point(P(2,2,0.01)),new GH_Line(new Line(P(1,0.01),P(9,0.01)))},null,0.01,0.02,0.001,false);
            Assert(r.Diagnostics.Changed.Count==2 && r.Diagnostics.Unresolved.Count==0,"Missing changed geometry");
            Assert(r.Diagnostics.Changed.OfType<Rhino.Geometry.Point>().Single().Location.Z==0,"Original instead of projected point displayed");
            Assert(r.Diagnostics.Changed.All(g=>g.GetUserString("Ant.Diagnostic.Source").StartsWith("E[")),"Missing source labels");
            Assert(r.Log.Any(l=>l.StartsWith("C[0]")),"No link from diagnostic geometry to log");
            r.Diagnostics.Changed[0].Transform(Transform.Translation(50,0,0));
            Assert(r.Embedded.OfType<GH_Point>().Single().Value.X==2,"Diagnostic output aliases primary output");
        });
        check("diagnostics: hole clipping exposes only the discarded curve portions",()=>
        {
            using var s=Hole(); var line=new GH_Line(new Line(P(-1,5),P(11,5)));
            var r=PlanarGeometryCleanup.Clean(new[]{s},new IGH_GeometricGoo[]{line},null,0.01,0.02,0.001,false);
            var pieces=r.Diagnostics.Discarded.OfType<Curve>().ToList();
            Assert(pieces.Count==3 && Math.Abs(pieces.Sum(c=>c.GetLength())-4)<1E-7,"Clipped portions incorrect");
            Assert(r.Diagnostics.Changed.Count==2 && r.Diagnostics.Unresolved.Count==0,"Valid retained portions misclassified");
            Assert(line.Value.From==P(-1,5),"Input line mutated");
        });
        check("diagnostics: duplicate faces, points and short curves are discarded",()=>
        {
            using var s=Rect(); var r=PlanarGeometryCleanup.Clean(new[]{s,s},new IGH_GeometricGoo[]{new GH_Point(P(2,2)),new GH_Point(P(2,2)),new GH_Line(new Line(P(1,1),P(1.001,1)))},null,0.01,0.02,0.001,false);
            Assert(r.Diagnostics.Discarded.OfType<Brep>().Count()==1 && r.Diagnostics.Discarded.OfType<Rhino.Geometry.Point>().Count()==1 && r.Diagnostics.Discarded.OfType<Curve>().Count()==1,"Missing duplicate/short diagnostics");
            Assert(r.Diagnostics.Unresolved.Count==0,"Intentional short-segment removal marked unresolved");
        });
        check("diagnostics: rejected constraints keep their original position",()=>
        {
            using var s=Rect(); var r=PlanarGeometryCleanup.Clean(new[]{s},new IGH_GeometricGoo[]{new GH_Point(P(30,30,5))},new IGH_GeometricGoo[]{new MeshPointRefinement(P(3,3),double.NaN)},0.01,0.02,0.001,false);
            Assert(r.Diagnostics.Unresolved.Count==2 && r.Diagnostics.Changed.Count==0 && r.Diagnostics.Discarded.Count==0,"Unresolved inputs not isolated");
            Assert(r.Diagnostics.Unresolved.OfType<Rhino.Geometry.Point>().Any(p=>p.Location==P(30,30,5)),"Unresolved point moved");
        });
        check("diagnostics: clipped refinement regions expose removed area",()=>
        {
            using var s=Hole(); using var region=Brep.CreateFromCornerPoints(P(2,2),P(8,2),P(8,8),P(2,8),1E-7);
            var r=PlanarGeometryCleanup.Clean(new[]{s},null,new IGH_GeometricGoo[]{new MeshSurfaceRefinement(region,0.25)},0.01,0.02,0.001,false);
            var removed=r.Diagnostics.Discarded.OfType<Brep>().ToList();
            Assert(removed.Count==1 && Math.Abs(AreaMassProperties.Compute(removed[0]).Area-4)<1E-7,"Missing exact excluded region");
            Assert(r.Diagnostics.Changed.OfType<Brep>().Single().Loops.Count==2 && r.Refinements.Count==1,"Diagnostic computation changed primary refinement output");
        });
        check("diagnostics: fatal geometry failure retains only unresolved geometry",()=>
        {
            using var valid=Rect(); using var invalid=new Sphere(P(30,30),2).ToBrep();
            try { PlanarGeometryCleanup.Clean(new[]{valid,valid,invalid},null,null,0.01,0.02,0.001,false); throw new Exception("Invalid surface accepted"); }
            catch(ArgumentException e)
            {
                var d=e.Data[CleanupDiagnostics.ExceptionKey] as CleanupDiagnostics;
                Assert(d!=null && d.Unresolved.Count==1 && d.Changed.Count==0 && d.Discarded.Count==0,"Failed operation left misleading committed diagnostics");
            }
        });
        check("diagnostics: small mesh faces and orphan points are drawable",()=>
        {
            using var big=Triangle(P(0,0),P(1,0),P(0,1)); using var tiny=Triangle(P(3,0),P(3.01,0),P(3,0.01));
            tiny.Vertices.Add(8,8,8); var r=MeshCleanup.Clean(new[]{big,tiny},0,0,0.0001);
            Assert(r.Diagnostics.Discarded.OfType<Mesh>().Single().Faces.Count==1,"Missing discarded small face");
            Assert(r.Diagnostics.Discarded.OfType<Rhino.Geometry.Point>().Any(p=>p.Location==P(8,8,8)),"Missing orphan point");
            Assert(r.Mesh.Faces.Count==1 && r.Diagnostics.Discarded.All(g=>g.IsValid),"Diagnostics changed result or are invalid for preview");
        });
        check("diagnostics: degenerate faces use valid outlines and repaired replacements",()=>
        {
            using var line=Triangle(P(0,0),P(1,0),P(2,0)); using var quad=new Mesh();
            quad.Vertices.Add(0,0,0); quad.Vertices.Add(0,0,0); quad.Vertices.Add(1,0,0); quad.Vertices.Add(0,1,0); quad.Faces.AddFace(0,1,2,3);
            var r=MeshCleanup.Clean(new[]{line,quad},0,0,0);
            Assert(r.Diagnostics.Discarded.OfType<Curve>().Any() && r.Diagnostics.Discarded.All(g=>g.IsValid),"Degenerate outline unavailable");
            Assert(r.Diagnostics.Changed.OfType<Mesh>().Any(m=>m.Faces.TriangleCount>0),"Repaired face missing from C");
        });
        check("diagnostics: remaining non-manifold edges are exposed",()=>
        {
            using var mesh=new Mesh(); mesh.Vertices.AddVertices(new[]{P(0,0),P(1,0),P(0,1),P(0,-1),P(0,0,1)});
            mesh.Faces.AddFace(0,1,2); mesh.Faces.AddFace(1,0,3); mesh.Faces.AddFace(0,1,4);
            var r=MeshCleanup.Clean(new[]{mesh},0,0,0);
            Assert(r.Diagnostics.Unresolved.OfType<Curve>().Any(c=>Math.Abs(c.GetLength()-1)<1E-7),"Non-manifold edge not shown");
            Assert(r.Report.Contains("non-manifold",StringComparison.OrdinalIgnoreCase),"Defect absent from report");
        });
        check("diagnostics: GH output order, failure outputs and far-away preview bounds",()=>
        {
            using var doc=new GH_Document(); var cg=new CleanGeometryComponent(); cg.CreateAttributes(); doc.AddObject(cg,false);
            Assert(cg.Params.Output.Select(p=>p.NickName).SequenceEqual(new[]{"S","E","R","L","C","D","U"}),"Changed existing CG port order");
            ((Param_Brep)cg.Params.Input[0]).PersistentData.Append(new GH_Brep(Rect()));
            ((Param_Geometry)cg.Params.Input[1]).PersistentData.Append(new GH_Point(P(200,200,5))); Solve(doc,cg);
            Assert(cg.Params.Output[6].VolatileDataCount==1 && cg.ClippingBox.Contains(P(200,200,5)),"Unresolved output clipped from viewport");
            ((Param_Brep)cg.Params.Input[0]).PersistentData.Clear(); ((Param_Brep)cg.Params.Input[0]).PersistentData.Append(new GH_Brep(new Sphere(P(0,0),2).ToBrep())); cg.Params.Input[0].ExpireSolution(false); Solve(doc,cg);
            Assert(cg.Params.Output[0].VolatileDataCount==0 && cg.Params.Output[6].VolatileDataCount==1 && cg.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count>0,"Failure diagnostic output missing or stale result retained: S="+cg.Params.Output[0].VolatileDataCount+", U="+cg.Params.Output[6].VolatileDataCount+", errors="+string.Join(";",cg.RuntimeMessages(GH_RuntimeMessageLevel.Error))+", log="+string.Join(";",cg.Params.Output[3].VolatileData.AllData(true)));
            Assert(cg.IsPreviewCapable,"Diagnostic-only error state is not previewable");
            var cm=new CleanMergeMeshComponent(); Assert(cm.Params.Output.Select(p=>p.NickName).SequenceEqual(new[]{"M","I","C","D","U"}),"Changed CMM port order");
        });
        check("diagnostics: saved components with original ports acquire appended outputs",()=>
        {
            foreach(var component in new GH_Component[]{new CleanGeometryComponent(),new CleanMergeMeshComponent(),new LegacyCleanGeometryComponent()})
            {
                component.CreateAttributes(); int original=component is CleanGeometryComponent?4:component is CleanMergeMeshComponent?2:3;
                foreach(var p in component.Params.Output.Skip(original).ToArray()) component.Params.UnregisterOutputParameter(p);
                component.Params.OnParametersChanged(); var archive=new GH_LooseChunk("OldComponent"); Assert(component.Write(archive),"Old-layout archive write failed");
                var restored=(GH_Component)Activator.CreateInstance(component.GetType()); restored.CreateAttributes(); Assert(restored.Read(archive),"Old-layout component read failed");
                Assert(restored.Params.Output.Count==original+3,"New outputs missing after loading an existing GH definition");
            }
        });
    }
}
