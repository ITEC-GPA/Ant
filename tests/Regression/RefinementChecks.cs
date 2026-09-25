using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Ant.GrasshopperPlugIn.DataTypes;
using Ant.GrasshopperPlugIn.Helpers;
using GH_IO.Serialization;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

static class RefinementChecks
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static Point3d P(double x, double y, double z = 0) => new Point3d(x,y,z);
    static Polyline Poly(bool closed = true) => new Polyline(closed
        ? new[]{P(1,1),P(8,1),P(7,7),P(1,6),P(1,1)}
        : new[]{P(1,1),P(8,1),P(7,7),P(1,6)});
    static Brep Region()
    {
        using var outer = new Rectangle3d(Plane.WorldXY,new Interval(0,10),new Interval(0,10)).ToNurbsCurve();
        using var inner = new Rectangle3d(Plane.WorldXY,new Interval(4,6),new Interval(4,6)).ToNurbsCurve();
        return Brep.CreatePlanarBreps(new[]{outer,inner},1E-7).Single();
    }
    static List<IGH_GeometricGoo> Samples(double size = 0.25) => new()
    {
        new MeshPointRefinement(P(2,3,4),size),
        new MeshLineRefinement(new Line(P(1,2,3),P(7,8,9)),size),
        new MeshPolylineRefinement(Poly(),size),
        new MeshPolylineCurveRefinement(new PolylineCurve(Poly(false)),size),
        new MeshSurfaceRefinement(Region(),size)
    };
    static double Size(IGH_GeometricGoo goo) => (double)goo.GetType().GetProperty("Size").GetValue(goo);
    static IGH_GeometricGoo Empty(IGH_GeometricGoo goo) => (IGH_GeometricGoo)Activator.CreateInstance(goo.GetType());
    static void Same(IGH_GeometricGoo source, IGH_GeometricGoo copy)
    {
        Assert(copy != null && source.GetType()==copy.GetType() && copy.IsValid,"Refinement type or validity lost");
        Assert(Size(source)==Size(copy) && source.ReferenceID==copy.ReferenceID,"Refinement size or reference ID lost");
        Assert(source.Boundingbox.Min.DistanceTo(copy.Boundingbox.Min)<1E-7 && source.Boundingbox.Max.DistanceTo(copy.Boundingbox.Max)<1E-7,"Geometry bounds changed");
        if (source is MeshPolylineRefinement a && copy is MeshPolylineRefinement b) Assert(a.Geometry.SequenceEqual(b.Geometry),"Polyline order, endpoint or closure lost");
        if (source is MeshPolylineCurveRefinement c && copy is MeshPolylineCurveRefinement d)
        { c.Geometry.TryGetPolyline(out var x); d.Geometry.TryGetPolyline(out var y); Assert(x.SequenceEqual(y),"PolylineCurve points lost"); }
        if (source is MeshSurfaceRefinement e && copy is MeshSurfaceRefinement f) Assert(e.Geometry.Loops.Count==f.Geometry.Loops.Count,"Region hole lost");
    }

    internal static void Run(Action<string,Action> check)
    {
        foreach (bool binary in new[]{false,true})
            check("refinement: complete "+(binary?"binary":"XML")+" archive round-trip for every type",()=>
            {
                foreach (var source in Samples())
                {
                    source.ReferenceID=Guid.NewGuid();
                    var writer=new GH_Archive(); Assert(writer.AppendObject(source,"R"),"Archive write failed");
                    var reader=new GH_Archive();
                    Assert(binary ? reader.Deserialize_Binary(writer.Serialize_Binary()) : reader.Deserialize_Xml(writer.Serialize_Xml()),"Archive load failed");
                    var copy=Empty(source); Assert(reader.ExtractObject(copy,"R"),"Archive extraction failed for "+source.GetType().Name);
                    Same(source,copy);
                }
            });
        check("refinement: Grasshopper persistent generic parameter preserves custom types",()=>
        {
            // Seed an isolated server: the normal getter loads every user-installed GHA and its UI.
            var serverField=typeof(Grasshopper.Instances).GetFields(BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)
                .Single(f=>f.FieldType==typeof(Grasshopper.Kernel.GH_ComponentServer));
            serverField.SetValue(null,Activator.CreateInstance(typeof(Grasshopper.Kernel.GH_ComponentServer),true));
            var server=Grasshopper.Instances.ComponentServer;
            var assembly=typeof(MeshPointRefinement).Assembly;
            var parse=server.GetType().GetMethod("ParseGHA",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,
                null,new[]{typeof(Assembly),typeof(string),typeof(Grasshopper.Kernel.GH_LoadingMechanism)},null);
            parse.Invoke(server,new object[]{assembly,assembly.Location,Grasshopper.Kernel.GH_LoadingMechanism.Direct});
            var source=new Param_GenericObject(); source.CreateAttributes(); foreach(var item in Samples()) source.PersistentData.Append(item);
            var archive=new GH_Archive(); Assert(archive.AppendObject(source,"Parameter"),"Parameter write failed");
            var loaded=new GH_Archive(); Assert(loaded.Deserialize_Binary(archive.Serialize_Binary()),"Parameter archive load failed");
            var copy=new Param_GenericObject(); copy.CreateAttributes(); Assert(loaded.ExtractObject(copy,"Parameter"),"Parameter read failed");
            var a=source.PersistentData.AllData(true).ToList(); var b=copy.PersistentData.AllData(true).ToList();
            Assert(a.Count==b.Count,"Persistent items lost");
            for(int i=0;i<a.Count;i++) Same((IGH_GeometricGoo)a[i],(IGH_GeometricGoo)b[i]);
        });
        check("refinement: copies retain type, size, reference and independent geometry",()=>
        {
            foreach(var source in Samples())
            {
                source.ReferenceID=Guid.NewGuid(); Same(source,source.DuplicateGeometry()); Same(source,(IGH_GeometricGoo)source.Duplicate());
            }
            var poly=new MeshPolylineRefinement(Poly(),0.25); var copy=(MeshPolylineRefinement)poly.DuplicateGeometry();
            copy.Geometry[0]=P(100,100); Assert(poly.Geometry[0]==P(1,1),"Polyline copy shares mutable points");
            var curve=new MeshPolylineCurveRefinement(new PolylineCurve(Poly(false)),0.25); var curveCopy=(MeshPolylineCurveRefinement)curve.DuplicateGeometry();
            curveCopy.Geometry.Transform(Transform.Translation(5,0,0)); Assert(curve.Geometry.PointAtStart==P(1,1),"Curve copy shares native geometry");
            var region=new MeshSurfaceRefinement(Region(),0.25); var regionCopy=(MeshSurfaceRefinement)region.DuplicateGeometry();
            regionCopy.Geometry.Transform(Transform.Translation(5,0,0)); Assert(region.Geometry.GetBoundingBox(true).Min.X==0,"Region copy shares native geometry");
        });
        check("refinement: transforms preserve mesh sizes without modifying inputs",()=>
        {
            var translation=Transform.Translation(11,12,13);
            foreach(var source in Samples())
            {
                source.ReferenceID=Guid.NewGuid(); var before=source.Boundingbox;
                var output=source.Transform(translation);
                Assert(output!=null && output.GetType()==source.GetType() && output.IsValid,"Transform failed");
                Assert(Size(output)==Size(source) && output.ReferenceID==Guid.Empty,"Transform changed mesh size or retained stale reference");
                Assert(output.Boundingbox.Center.DistanceTo(before.Center+new Vector3d(11,12,13))<1E-7,"Incorrect transformed geometry");
                Assert(source.Boundingbox.Center==before.Center,"Transform modified original");
                Assert(Size(source.Transform(Transform.Scale(Point3d.Origin,2)))==Size(source),"Scaling changed explicit mesh size");
            }
            var line=new MeshLineRefinement(new Line(P(0,0),P(1,0)),1);
            Assert(line.Transform(Transform.Scale(Point3d.Origin,0))==null,"Degenerate transformed line accepted");
        });
        check("refinement: morphing returns independent refinements and preserves sizes",()=>
        {
            foreach(var source in Samples())
            {
                var before=source.Boundingbox; var output=source.Morph(new TranslationMorph());
                Assert(output!=null && output.GetType()==source.GetType() && output.IsValid && Size(output)==Size(source),"Morph lost type or size");
                Assert(output.Boundingbox.Center.DistanceTo(before.Center+new Vector3d(2,3,4))<1E-6,"Morph produced wrong position");
                Assert(source.Boundingbox.Center==before.Center,"Morph modified input");
            }
        });
        check("refinement: validity, loaded state and bounds reflect stored data",()=>
        {
            foreach(var source in Samples())
            {
                Assert(source.IsValid && source.IsGeometryLoaded && source.LoadGeometry(),"Loaded valid refinement marked invalid");
                var empty=Empty(source); Assert(!empty.IsValid && !empty.IsGeometryLoaded && !string.IsNullOrWhiteSpace(empty.IsValidWhyNot),"Empty refinement marked valid");
                Assert(!empty.Write(new GH_LooseChunk("Invalid")),"Invalid refinement was saved");
            }
            foreach(double size in new[]{0.0,-1.0,double.NaN,double.PositiveInfinity})
                foreach(var source in Samples(size)) Assert(!source.IsValid && source.IsValidWhyNot.Contains("size"),"Invalid size accepted");
            var poly=new MeshPolylineRefinement(Poly(),0.25);
            Assert(poly.Boundingbox.Min.X==0.75 && poly.Boundingbox.Min.Y==0.75 && poly.Boundingbox.Max.X==8.25,"Polyline minimum bounds calculated from maximum point");
        });
        check("refinement: geometry casts return supported independent geometry",()=>
        {
            var poly=new MeshPolylineRefinement(Poly(),0.25);
            Assert(poly.CastTo<GH_Curve>(out var curve) && curve.Value.IsClosed,"Polyline curve cast failed");
            curve.Value.Transform(Transform.Translation(20,0,0)); Assert(poly.Geometry[0]==P(1,1),"Cast shares mutable geometry");
            Assert(poly.CastTo<Polyline>(out var raw) && raw.Count==5,"Polyline raw cast failed");
            Assert(!poly.CastTo<GH_Point>(out var point) && point==null,"Polyline incorrectly casts to a point");
            var pc=new MeshPolylineCurveRefinement(new PolylineCurve(Poly(false)),0.25);
            Assert(pc.CastTo<GH_Curve>(out var c) && c.Value!=null,"PolylineCurve cast failed");
            Assert(new MeshSurfaceRefinement(Region(),0.25).CastTo<GH_Brep>(out var b) && b.Value.Loops.Count==2,"Surface cast lost hole");
            Assert(new MeshLineRefinement(new Line(P(1,1),P(2,2)),0.25).CastTo<GH_Curve>(out var l) && l.Value!=null,"Line cast returned null");
        });
        check("refinement: incomplete legacy and malformed archives fail explicitly",()=>
        {
            var legacy=new GH_LooseChunk("Legacy"); legacy.SetPoint3D("Point",new GH_IO.Types.GH_Point3D(1,2,3)); legacy.SetDouble("Size",0.5);
            foreach(var target in new IGH_GeometricGoo[]{new MeshPolylineRefinement(Poly(),0.25),new MeshPolylineCurveRefinement(new PolylineCurve(Poly()),0.25)})
            {
                Assert(!target.Read(legacy) && !target.IsValid && target.IsValidWhyNot.Contains("legacy"),"Incomplete legacy data silently read");
            }
            var bad=new GH_LooseChunk("Bad"); bad.SetInt32("PointCount",int.MaxValue); bad.SetDouble("Size",0.25);
            Assert(!new MeshPolylineRefinement().Read(bad),"Corrupted count accepted");
            var missing=new GH_LooseChunk("Missing"); missing.SetInt32("PointCount",3); missing.SetDouble("Size",0.25); missing.SetInt32("Other",0);
            Assert(!new MeshPolylineCurveRefinement().Read(missing),"Missing points accepted");
        });
        check("refinement: archived open and closed polylines remain usable by Clean Geometry",()=>
        {
            using var host=Brep.CreateFromCornerPoints(P(0,0),P(10,0),P(10,10),P(0,10),1E-7);
            foreach(bool closed in new[]{false,true})
            {
                var source=new MeshPolylineRefinement(Poly(closed),0.35); var chunk=new GH_LooseChunk("R"); source.Write(chunk);
                var restored=new MeshPolylineRefinement(); Assert(restored.Read(chunk),"Polyline restoration failed");
                var result=PlanarGeometryCleanup.Clean(new[]{host},null,new IGH_GeometricGoo[]{restored},0.01,0.02,0.001,false);
                Assert(result.Warnings.Count==0 && result.Refinements.Count>0,"Clean Geometry rejected archived refinement");
                Assert(result.Refinements.All(r=>Size(r)==0.35),"Clean Geometry lost archived size");
                Assert(closed ? result.Refinements.Single() is MeshSurfaceRefinement : result.Refinements.All(r=>r is MeshLineRefinement),"Open/closed semantics changed");
            }
        });
        check("mesh input: curves and refinements cannot bridge holes between valid endpoints",()=>
        {
            using var host=Region(); using var crossing=new LineCurve(P(1,5),P(9,5));
            Assert(!MeshInputGeometry.TryProjectCurve(host,crossing,1E-4,1E-7,1,out var segments) && segments.Count==0,"Curve bridges the host hole");
            Assert(!MeshInputGeometry.TryProjectRefinement(host,new MeshLineRefinement(new Line(P(1,5),P(9,5)),0.25),1E-4,1E-7,1,out _,out _),"Line refinement bridges hole");
            var poly=new MeshPolylineRefinement(new Polyline(new[]{P(1,2),P(1,5),P(9,5)}),0.25);
            Assert(!MeshInputGeometry.TryProjectRefinement(host,poly,1E-4,1E-7,1,out _,out _),"Polyline refinement partially committed through hole");
        });
        check("mesh input: regions cannot cover missing host area",()=>
        {
            using var host=Region();
            using var solid=Brep.CreateFromCornerPoints(P(2,2),P(8,2),P(8,8),P(2,8),1E-7);
            Assert(!MeshInputGeometry.TryProjectRegion(host,solid,1E-4,1E-7,out _),"Region silently fills host hole");
            using var matching=Region(); Assert(MeshInputGeometry.TryProjectRegion(host,matching,1E-4,1E-7,out var regions) && regions.Single().HasHoles,"Matching region with hole rejected");
        });
    }
    sealed class TranslationMorph : SpaceMorph
    {
        public override Point3d MorphPoint(Point3d point) => point+new Vector3d(2,3,4);
    }
}
