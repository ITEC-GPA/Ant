using System;
using System.Collections.Generic;
using System.Linq;
using Ant.GrasshopperPlugIn.Components;
using Ant.GrasshopperPlugIn.DataTypes;
using Ant.GrasshopperPlugIn.Helpers;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using GMesh = GPC.Geometry.Meshes.GMesh.GMesh;

static class PlanarGeometryCleanupChecks
{
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static Point3d P(double x, double y, double z = 0) => new Point3d(x, y, z);
    static GH_Line L(double x1, double y1, double x2, double y2, double z = 0) => new GH_Line(new Line(P(x1,y1,z), P(x2,y2,z)));
    static Brep Rect(double x = 0, double y = 0, double width = 10, double height = 10, double z = 0) =>
        Brep.CreateFromCornerPoints(P(x,y,z), P(x+width,y,z), P(x+width,y+height,z), P(x,y+height,z), 1E-7);
    static Brep Hole(double outer = 10, double innerMin = 4, double innerMax = 6)
    {
        using var a = new Rectangle3d(Plane.WorldXY, new Interval(0,outer),new Interval(0,outer)).ToNurbsCurve();
        using var b = new Rectangle3d(Plane.WorldXY, new Interval(innerMin,innerMax),new Interval(innerMin,innerMax)).ToNurbsCurve();
        return Brep.CreatePlanarBreps(new[] { a,b },1E-7).Single();
    }
    static PlanarGeometryCleanup.Result Clean(List<Brep> s, List<IGH_GeometricGoo> e = null,
        List<IGH_GeometricGoo> r = null, bool union = false) => PlanarGeometryCleanup.Clean(s,e,r,0.01,0.02,0.001,union);

    internal static void Run(Action<string,Action> check)
    {
        check("geometry: project to actual cleaned surface and snap nearby border", () =>
        {
            using var surface = Rect(); var line = L(2,0.005,8,0.005,0.01);
            var result = Clean(new(){surface},new(){line,new GH_Point(P(2,2,0.01))});
            var cleaned = result.Embedded.OfType<GH_Line>().Single().Value;
            Assert(cleaned.From.DistanceTo(P(2,0))<1E-8 && cleaned.To.DistanceTo(P(8,0))<1E-8,"Line did not snap to border");
            Assert(result.Embedded.OfType<GH_Point>().Single().Value.Z==0,"Point not projected");
            Assert(line.Value.FromZ==0.01 && line.Value.FromY==0.005,"Input geometry was mutated");
            Assert(result.Warnings.Count==0 && result.Log.Count>0,"Unexpected warning or missing report");
        });
        check("geometry: duplicate faces/vertices/lines and degenerate constraints", () =>
        {
            using var s = Rect();
            var result=Clean(new(){s,s},new(){L(1,1,3,1),L(3,1,1,1),L(1,3,1.001,3),new GH_Point(P(4,4)),new GH_Point(P(4.005,4))});
            Assert(result.Surfaces.Count==1 && result.Embedded.Count==2,"Duplicates or short line retained");
            Assert(s.IsValid && Math.Abs(AreaMassProperties.Compute(s).Area-100)<1E-7,"Input surface changed");
        });
        check("geometry: hole clipping never bridges a void", () =>
        {
            using var s=Hole(); var result=Clean(new(){s},new(){L(-1,5,11,5)});
            var lines=result.Embedded.OfType<GH_Line>().Select(l=>l.Value).ToList();
            Assert(result.Surfaces.Single().Loops.Count==2 && lines.Count==2,"Lost hole or incorrect clipping: loops="+result.Surfaces.Single().Loops.Count+", lines="+string.Join(";",lines)+", log="+string.Join(";",result.Log));
            Assert(Math.Abs(lines.Sum(l=>l.Length)-8)<1E-7,"Wrong retained length");
            Assert(lines.All(l=>l.PointAt(0.5).X<4 || l.PointAt(0.5).X>6),"Line inside hole");
            Assert(result.Warnings.Any(w=>w.Contains("excluded")),"Clipping not reported");
        });
        check("geometry: duplicates with different edge subdivisions", () =>
        {
            using var a=Rect(); using var curve=new PolylineCurve(new[]{P(0,0),P(5,0),P(10,0),P(10,10),P(0,10),P(0,0)});
            using var b=Brep.CreatePlanarBreps(curve,1E-7).Single();
            Assert(Clean(new(){a,b}).Surfaces.Count==1,"Differently subdivided duplicate face retained");
        });
        check("geometry: near T junctions and near overlapping constraints", () =>
        {
            using var s=Rect();
            var r=Clean(new(){s},new(){L(1,5,9,5),L(4,5.005,4,8),L(3,5.006,7,5.006),new GH_Point(P(6,5.004))});
            Assert(r.Embedded.OfType<GH_Line>().All(l=>Math.Abs(l.Value.FromY-5)<1E-8 || Math.Abs(l.Value.ToY-5)<1E-8),"Near lines did not align");
            Assert(r.Embedded.OfType<GH_Point>().Single().Value.DistanceTo(P(6,5))<1E-8,"Near point did not snap to line");
            Assert(Math.Abs(r.Embedded.OfType<GH_Line>().Sum(l=>l.Value.Length)-11)<1E-7,"Overlapping length duplicated or T junction disconnected");
        });
        check("geometry: projection obeys its Euclidean distance limit", () =>
        {
            using var s=Rect(); var source=P(5,0.019,0.019);
            var r=Clean(new(){s},new(){new GH_Point(source)});
            Assert(r.Embedded.OfType<GH_Point>().Single().Value.DistanceTo(source)<=0.02,"Projection exceeded PT");
        });
        check("geometry: gaps and near parallel planes become shared panel boundaries", () =>
        {
            using var a=Rect(); using var b=Rect(10.005,0,10,10,0.005);
            var result=Clean(new(){a,b},new(){L(2,5,18,5)});
            Assert(result.Surfaces.Count==2 && result.Embedded.Count==2,"Panels were lost or line did not split");
            Assert(result.Surfaces[1].Vertices.All(v=>Math.Abs(v.Location.Z)<1E-8),"Plane not aligned");
            Assert(result.Surfaces[1].Vertices.Any(v=>v.Location.DistanceTo(P(10,0))<1E-8),"Gap did not close");
            var prepared=new Probe().Prepare(result);
            Assert(prepared.errors.Count==0 && prepared.warnings.Count==0,"Clean outputs not accepted by Planar Mesh");
            Assert(prepared.embedded.Count==2,"Constraint not attached to both panels");
        });
        check("geometry: optional coplanar union and holes", () =>
        {
            using var a=Hole(); using var b=Rect(10);
            var result=Clean(new(){a,b},union:true);
            Assert(result.Surfaces.Count==1 && result.Surfaces[0].Loops.Count==2,"Union lost a hole or failed to join");
            Assert(Math.Abs(AreaMassProperties.Compute(result.Surfaces[0]).Area-196)<1E-6,"Union area incorrect");
        });
        check("geometry: angled planes remain planar", () =>
        {
            using var a=Rect(); using var b=Rect(); b.Transform(Transform.Rotation(Math.PI/2,Vector3d.XAxis,Point3d.Origin));
            var result=Clean(new(){a,b});
            Assert(result.Surfaces.Count==2 && result.Surfaces.All(s=>s.Faces[0].IsPlanar(1E-8)),"Nonparallel panel was flattened");
        });
        check("geometry: overlapping line refinements retain the finest size", () =>
        {
            using var s=Rect();
            var result=Clean(new(){s},new(){L(1,2,9,2)},new(){new MeshLineRefinement(new Line(P(3,2),P(7,2)),0.3),new MeshLineRefinement(new Line(P(7,2),P(3,2)),0.2)});
            Assert(result.Refinements.Count==1 && ((MeshLineRefinement)result.Refinements[0]).Size==0.2,"Refinement size lost or duplicate retained");
            Assert(result.Embedded.Count==2,"Overlapping embedded line not split around refinement");
            Assert(Math.Abs(result.Embedded.Cast<GH_Line>().Sum(l=>l.Value.Length)-4)<1E-8,"Bad overlap normalization");
            var p=new Probe().Prepare(result); Assert(p.errors.Count==0 && p.sizes.Values.Single()==0.2,"Downstream size was lost");
        });
        check("geometry: crossing lines produce shared intersection vertices", () =>
        {
            using var s=Rect(); var result=Clean(new(){s},new(){L(1,5,9,5),L(5,1,5,9)});
            Assert(result.Embedded.Count==4 && result.Embedded.Cast<GH_Line>().All(l=>l.Value.From.DistanceTo(P(5,5))<1E-8 || l.Value.To.DistanceTo(P(5,5))<1E-8),"Crossing not split");
        });
        check("geometry: unmatched and invalid refinements are reported", () =>
        {
            using var s=Rect(); var result=Clean(new(){s},new(){new GH_Point(P(5,5,2))},new(){new MeshPointRefinement(P(5,5),double.NaN)});
            Assert(result.Embedded.Count==0 && result.Refinements.Count==0 && result.Warnings.Count==2,"Unmatched input silently ignored");
        });
        check("geometry: curved boundaries use the curve deviation tolerance", () =>
        {
            using var circle=new Circle(Plane.WorldXY,5).ToNurbsCurve(); using var s=Brep.CreatePlanarBreps(circle,1E-6).Single();
            var result=Clean(new(){s});
            Assert(result.Surfaces.Single().IsValid,"Bad circular boundary");
            Assert(Math.Abs(AreaMassProperties.Compute(result.Surfaces[0]).Area-Math.PI*25)<0.05,"Approximation exceeds requested accuracy");
        });
        check("geometry: invalid surfaces and tolerance fail without partial output", () =>
        {
            using var s=Rect(); using var sphere=new Sphere(Point3d.Origin,1).ToBrep();
            foreach (var tol in new[]{0.0,-1.0,double.NaN,double.PositiveInfinity})
            { bool failed=false; try { PlanarGeometryCleanup.Clean(new[]{s},null,null,tol,0.01,0.001,false); } catch(ArgumentException){failed=true;} Assert(failed,"Invalid tolerance accepted"); }
            bool bad=false; try { Clean(new(){s,sphere}); } catch(ArgumentException){bad=true;} Assert(bad,"Partial nonplanar input accepted");
        });
        check("geometry: region refinements preserve holes and reach Planar Mesh", () =>
        {
            using var s=Rect(-1,-1,12,12); using var region=Hole();
            var result=Clean(new(){s},r:new(){new MeshSurfaceRefinement(region,0.4)});
            var refinement=(MeshSurfaceRefinement)result.Refinements.Single();
            Assert(refinement.Size==0.4 && refinement.Geometry.Loops.Count==2,"Region size or hole lost");
            var p=new Probe().Prepare(result);
            Assert(p.errors.Count==0 && p.warnings.Count==0 && p.sizes.Values.Single()==0.4,"Planar Mesh dropped surface refinement");
            var shape=p.embedded.Values.Single().OfType<GPC.Geometry.Shape>().Single();
            Assert(shape.HasHoles && shape.Holes.Length==1,"Planar Mesh lost refinement hole");
        });
        check("geometry: region clipping preserves the host hole", () =>
        {
            using var host=Hole(); using var region=Rect(2,2,6,6);
            var result=Clean(new(){host},r:new(){new MeshSurfaceRefinement(region,0.2)});
            var output=(MeshSurfaceRefinement)result.Refinements.Single();
            Assert(output.Geometry.Loops.Count==2 && Math.Abs(AreaMassProperties.Compute(output.Geometry).Area-32)<1E-7,"Refinement fills host hole");
        });
        check("geometry: refinement duplication and serialization keep size and position", () =>
        {
            var line=new MeshLineRefinement(new Line(P(1,2,3),P(4,5,6)),0.2);
            var archive=new GH_LooseChunk("refinement"); Assert(line.Write(archive),"Line write failed");
            var copy=new MeshLineRefinement(Line.Unset,1); Assert(copy.Read(archive),"Line read failed");
            Assert(copy.Geometry.From==line.Geometry.From && copy.Geometry.To==line.Geometry.To && copy.Size==line.Size,"Line serialization lost coordinates");
            Assert(line.DuplicateGeometry() is MeshLineRefinement && new MeshPointRefinement(P(1,2),0.2).DuplicateGeometry() is MeshPointRefinement,"Duplication erased size/type");
            using var hole=Hole(); var region=new MeshSurfaceRefinement(hole,0.3); var chunk=new GH_LooseChunk("region");
            Assert(region.Write(chunk),"Region write failed"); var other=new MeshSurfaceRefinement(); Assert(other.Read(chunk),"Region read failed");
            Assert(other.Geometry.Loops.Count==2 && other.Size==0.3,"Region serialization lost hole/size");
        });
        check("geometry: new component ports, legacy GUID and actual GH solution", () =>
        {
            using var doc=new GH_Document(); var c=new CleanGeometryComponent(); c.CreateAttributes(); doc.AddObject(c,false);
            Assert(c.Params.Input.Take(3).Select(p=>p.NickName).SequenceEqual(new[]{"S","E","R"}),"Wrong input order");
            Assert(c.Params.Output.Take(4).Select(p=>p.NickName).SequenceEqual(new[]{"S","E","R","L"}),"Wrong output order");
            ((Param_Brep)c.Params.Input[0]).PersistentData.Append(new GH_Brep(Rect()));
            ((Param_GenericObject)c.Params.Input[2]).PersistentData.Append(new MeshPointRefinement(P(4,4),0.25));
            GH_Document.EnableSolutions=true; doc.Enabled=true; c.ExpireSolution(false); doc.NewSolution(false);
            Assert(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0,"GH component failed");
            Assert(c.Params.Output[0].VolatileDataCount==1 && c.Params.Output[2].VolatileDataCount==1 && c.Params.Output[3].VolatileDataCount>0,"GH outputs missing");
            var old=new LegacyCleanGeometryComponent();
            Assert(old.ComponentGuid==new Guid("169774c8-75df-45fb-a79e-e8830a020c06") && old.Params.Input[2] is Param_Number && old.Params.Output[2] is Param_String,"Old definitions no longer compatible");
        });
        check("geometry: Clean Geometry -> native GMesh with local point/line/region sizes", () =>
        {
            using var host=Rect(); using var region=Rect(3,3,4,4);
            var result=Clean(new(){host},new(){L(1,1,9,1,0.005)},new(){new MeshPointRefinement(P(2,2,0.005),0.2),new MeshLineRefinement(new Line(P(1,2),P(1,8)),0.4),new MeshSurfaceRefinement(region,0.5)});
            var output=new Probe().Compute(result);
            Assert(output.Errors.Count==0,string.Join(";",output.Errors));
            Assert(output.Value.Count>0 && output.Value.All(m=>m.Value.IsValid && m.Value.Faces.Count>0),"Native mesher failed on cleaned input");
        });
        check("geometry: Mesh Refinement dispatches lines and regions into Clean Geometry", () =>
        {
            foreach (bool region in new[]{false,true})
            {
                using var doc=new GH_Document(); var mr=new MeshRefinementComponent(); var clean=new CleanGeometryComponent();
                mr.CreateAttributes(); clean.CreateAttributes(); doc.AddObject(mr,false); doc.AddObject(clean,false);
                ((Param_Geometry)mr.Params.Input[0]).PersistentData.Append(region ? (IGH_GeometricGoo)new GH_Brep(Rect(2,2,3,3)) : L(2,2,5,2));
                ((Param_Number)mr.Params.Input[1]).PersistentData.Append(new GH_Number(0.3));
                ((Param_Brep)clean.Params.Input[0]).PersistentData.Append(new GH_Brep(Rect()));
                clean.Params.Input[2].AddSource(mr.Params.Output[0]);
                GH_Document.EnableSolutions=true; doc.Enabled=true; mr.ExpireSolution(false); clean.ExpireSolution(false); doc.NewSolution(false);
                Assert(mr.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0 && clean.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0,"Refinement component failed");
                Assert(clean.Params.Output[2].VolatileDataCount==1,"Refinement lost in GH wire conversion");
            }
        });
    }

    sealed class Probe : MeshComponent
    {
        public (Dictionary<GPC.Geometry.Shape,GPC.Geometry.GeometryBase[]> embedded,Dictionary<GPC.Geometry.GeometryBase,double> sizes,List<string> errors,List<string> warnings) Prepare(PlanarGeometryCleanup.Result result)
        {
            var errors=new List<string>(); var warnings=new List<string>();
            var p=PreProcessMesh(result.Surfaces,result.Embedded,result.Refinements,1E-4,1E-7,0.001,1,ref errors,ref warnings);
            return(p.embeddedGeometries,p.embeddedGeometriesMeshSize,errors,warnings);
        }
        public SolveResults Compute(PlanarGeometryCleanup.Result result) => ComputeMesh(result.Surfaces,result.Embedded,result.Refinements,
            new GMesh.GMeshGenerateOptions { MeshSize=1,Recombine=false,Transfinite=false },1E-4,1E-7,0.001,1);
    }
}
