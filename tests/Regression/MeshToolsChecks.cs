using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using Ant.GrasshopperPlugIn.Components;
using Ant.GrasshopperPlugIn.Helpers;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

internal static class MeshToolsChecks
{
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static bool Near(double a, double b) => Math.Abs(a - b) < 1E-8;
    static Point3d P(double x, double y, double z = 0) => new Point3d(x, y, z);
    static Mesh Quad(double x = 0, double width = 1)
    {
        var mesh = new Mesh(); mesh.Vertices.UseDoublePrecisionVertices = true;
        mesh.Vertices.AddVertices(new[] { P(x,0), P(x+width,0), P(x+width,1), P(x,1) }); mesh.Faces.AddFace(0,1,2,3); return mesh;
    }
    static void Solve(GH_Document doc, GH_Component component)
    {
        GH_Document.EnableSolutions = true; doc.Enabled = true; component.ExpireSolution(false); doc.NewSolution(false);
        Assert(component.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 0, component.Name + ": " + string.Join(";", component.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
    }
    internal static void Run(Action<string, Action> check)
    {
        check("mesh conversion: sparse vertex IDs retain double precision", () =>
        {
            var source=new GPC.Geometry.Meshes.Mesh();
            var points=new[]{P(1000000.0001,0),P(1000000.0002,0),P(1000000.0001,0.0001)};
            var ids=new[]{90,10,500};
            for(int i=0;i<3;i++)source.Vertices.Add(new GPC.Geometry.Meshes.MeshVertex(new GPC.Geometry.Point3d(points[i].X,points[i].Y,points[i].Z)),ids[i]);
            source.Faces.Add(new GPC.Geometry.Meshes.MeshFace(90,10,500));
            using var mesh=source.ConvertToRhinoMesh();
            Assert(mesh.Vertices.UseDoublePrecisionVertices && mesh.Vertices.ToPoint3dArray().SequenceEqual(points),"Conversion lost coordinate precision or changed vertex order");
            Assert(MeshTools.VertexIds(mesh.Faces[0]).SequenceEqual(new[]{0,1,2}),"Sparse source IDs incorrectly mapped");
        });
        check("mesh diagnostics: typed face keys preserve rotation, multiplicity and winding", () =>
        {
            using var before=Quad();before.Faces.AddFace(0,1,2,3);
            using var after=Quad();after.Faces.SetFace(0,1,2,3,0);
            var delta=new CleanupDiagnostics();delta.MeshDelta(before,after,"Duplicate replacement");
            Assert(delta.Changed.Count==0 && delta.Discarded.OfType<Mesh>().Sum(m=>m.Faces.Count)==1,"Rotations or duplicate multiplicity compared incorrectly");
            using var reversed=Quad();reversed.Flip(true,true,true);using var original=Quad();var flip=new CleanupDiagnostics();flip.MeshDelta(original,reversed,"Winding");
            Assert(flip.Changed.Count==1 && flip.Discarded.Count==1,"Reversed face not recognized");
            foreach(var g in delta.Changed.Concat(delta.Discarded).Concat(flip.Changed).Concat(flip.Discarded))g.Dispose();
        });
        check("mesh tools: quality metrics for triangles and regular/stretched quads", () =>
        {
            using var regular = Quad(); var q = MeshTools.Metrics(regular,0);
            Assert(Near(q.Area,1) && Near(q.EdgeRatio,1) && Near(q.MinimumAngle,90) && q.Warpage==0 && !q.Degenerate, "Wrong square quality");
            using var thin = Quad(0,100); Assert(Near(MeshTools.Metrics(thin,0).EdgeRatio,100), "Wrong edge ratio");
            using var tri = new Mesh(); tri.Vertices.AddVertices(new[] {P(0,0),P(3,0),P(0,4)}); tri.Faces.AddFace(0,1,2);
            var t = MeshTools.Metrics(tri,0); Assert(Near(t.Area,6) && Near(t.EdgeRatio,5.0/3) && t.MinimumAngle>36 && t.MinimumAngle<37, "Wrong triangle quality");
        });
        check("mesh tools: folded, concave, collapsed and warped faces are detected", () =>
        {
            using var concave=Quad(); concave.Vertices.SetVertex(2,P(0.2,0.2)); Assert(MeshTools.Metrics(concave,0).Degenerate,"Concave quad accepted");
            using var collapsed=Quad(); collapsed.Vertices.SetVertex(1,P(0,0)); Assert(MeshTools.Metrics(collapsed,0).Degenerate,"Collapsed edge accepted");
            using var warped=Quad(); warped.Vertices.SetVertex(2,P(1,1,0.1)); Assert(MeshTools.Metrics(warped,0).Warpage>0.09,"Warped quad considered planar");
        });
        check("mesh tools: extraction preserves source maps, colors, UVs and user strings", () =>
        {
            using var mesh=Quad(); using var other=Quad(2); mesh.Append(other); mesh.SetUserString("material","steel");
            for(int i=0;i<mesh.Vertices.Count;i++){mesh.VertexColors.Add(Color.FromArgb(i,2*i,3*i));mesh.TextureCoordinates.Add(i,2*i);}
            var result=MeshTools.Extract(mesh,new[]{1,1}); using var output=result.Mesh;
            Assert(result.Faces.SequenceEqual(new[]{1}) && result.Vertices.SequenceEqual(new[]{4,5,6,7}),"Wrong source map");
            Assert(output.VertexColors[2]==mesh.VertexColors[6] && output.TextureCoordinates[2]==mesh.TextureCoordinates[6] && output.GetUserString("material")=="steel","Attributes lost");
            Assert(mesh.Vertices.Count==8 && mesh.Faces.Count==2 && output.Faces.Count==1,"Input changed");
            output.Vertices.SetVertex(0,P(100,100)); Assert(mesh.Vertices.Point3dAt(4)==P(2,0),"Output aliases input");
        });
        check("mesh tools: empty and invalid extraction requests", () =>
        {
            using var mesh=Quad(); using var empty=MeshTools.Extract(mesh,Array.Empty<int>()).Mesh;
            Assert(empty.Faces.Count==0 && empty.Vertices.Count==0,"Empty selection should be empty");
            foreach(int id in new[]{-1,1}){bool failed=false;try{MeshTools.Extract(mesh,new[]{id}).Mesh.Dispose();}catch(ArgumentOutOfRangeException){failed=true;}Assert(failed,"Invalid index accepted");}
        });
        check("mesh tools: connected parts join edge seams but not vertex-only contacts", () =>
        {
            using var mesh=Quad(); using var adjacent=Quad(1); using var remote=Quad(10); mesh.Append(adjacent);mesh.Append(remote);
            var groups=MeshTools.ConnectedFaces(mesh); Assert(groups.Count==2 && groups[0].SequenceEqual(new[]{0,1}) && groups[1].Single()==2,"Edge connectivity failed across unwelded seam");
            using var touch=Quad(); using var corner=Quad(1); corner.Translate(0,1,0);touch.Append(corner);
            Assert(MeshTools.ConnectedFaces(touch).Count==2,"Vertex-only contact joined two parts");
        });
        check("mesh tools: triangulation preserves area, orientation and source face mapping", () =>
        {
            using var input=Quad(); input.SetUserString("panel","A"); var result=MeshTools.Triangulate(input); using var mesh=result.mesh;
            Assert(mesh.Faces.TriangleCount==2 && result.sourceFaces.SequenceEqual(new[]{0,0}) && mesh.Vertices.Count==4,"Wrong triangle/source mapping");
            Assert(Near(Enumerable.Range(0,2).Sum(i=>MeshTools.Metrics(mesh,i).Area),1) && Enumerable.Range(0,2).All(i=>MeshTools.FaceNormal(mesh,i).Z>0.99),"Area or orientation changed");
            Assert(input.Faces.QuadCount==1 && mesh.GetUserString("panel")=="A","Input changed or metadata lost");
            using var concave=Quad(); concave.Vertices.SetVertex(2,P(0.2,0.2)); var c=MeshTools.Triangulate(concave);using var split=c.mesh;
            Assert(Near(Enumerable.Range(0,2).Sum(i=>MeshTools.Metrics(split,i).Area),0.2),"Concave quad triangulated outside boundary");
        });
        check("mesh tools: normals orient disconnected closed shells independently", () =>
        {
            using var source=Mesh.CreateFromBox(new BoundingBox(P(0,0,0),P(1,1,1)),1,1,1);
            using var other=Mesh.CreateFromBox(new BoundingBox(P(3,0,0),P(4,1,1)),1,1,1); other.Flip(true,true,true); source.Append(other);
            var before=source.Faces.ToArray(); var result=MeshTools.Orient(source,true,false); using var oriented=result.mesh;
            foreach(var group in MeshTools.ConnectedFaces(oriented)){using var part=MeshTools.Extract(oriented,group).Mesh;Assert(part.SolidOrientation()==1,"A closed shell is still inward");}
            Assert(source.Faces.SequenceEqual(before),"Source orientation changed");
            var inverted=MeshTools.Orient(oriented,true,true);using var flipped=inverted.mesh;
            foreach(var group in MeshTools.ConnectedFaces(flipped)){using var part=MeshTools.Extract(flipped,group).Mesh;Assert(part.SolidOrientation()==-1,"Final flip ignored");}
        });
        check("mesh tools: open surfaces report no automatic outside", () =>
        {
            using var source=Quad();var result=MeshTools.Orient(source,true,true);using var mesh=result.mesh;
            Assert(MeshTools.FaceNormal(mesh,0).Z<0 && result.report.Contains("open parts with no outside: 1"),"Open orientation handling incorrect");
        });
        check("mesh tools: RTree node matches agree with exhaustive search, including ties", () =>
        {
            using var mesh=new Mesh();mesh.Vertices.UseDoublePrecisionVertices=true;var random=new Random(842);
            for(int i=0;i<1200;i++)mesh.Vertices.Add(random.NextDouble()*100,random.NextDouble()*100,0);
            mesh.Vertices.Add(-1,0,0);mesh.Vertices.Add(1,0,0);
            var points=Enumerable.Range(0,80).Select(_=>P(random.NextDouble()*100,random.NextDouble()*100)).Concat(new[]{P(0,0)}).ToArray();
            var result=MeshTools.MatchPoints(new[]{mesh},points,5,false);
            for(int p=0;p<points.Length;p++)
            {
                var candidates=Enumerable.Range(0,mesh.Vertices.Count).Select(i=>(id:i,d:points[p].DistanceTo(mesh.Vertices.Point3dAt(i)))).Where(c=>c.d<=5).OrderBy(c=>c.d).ThenBy(c=>c.id).ToArray();
                Assert(result[p].Element==(candidates.Length>0?candidates[0].id:-1),"Indexed nearest disagrees with exhaustive search at query "+p);
            }
            using var a=Quad();using var b=Quad();var tie=MeshTools.MatchPoints(new[]{a,b},new[]{P(0,0),P(100,100)},0,false);
            Assert(tie[0].Mesh==0 && tie[0].Element==0 && tie[1].Mesh==-1 && double.IsNaN(tie[1].Distance),"Exact/tie/unmatched semantics changed");
        });
        check("mesh tools: face matching uses real faces, enforces distance and retains IDs", () =>
        {
            using var a=Quad(0,100); using var b=Quad(200);
            var result=MeshTools.MatchPoints(new[]{a,b},new[]{P(0.1,0.1,0.01),P(200.5,0.5,0.02),P(300,0)},0.03,true);
            Assert(result[0].Mesh==0 && result[0].Element==0 && Near(result[0].Distance,0.01),"Large face missed near a corner");
            Assert(result[1].Mesh==1 && result[1].Element==0 && result[2].Mesh==-1,"Wrong mesh index or radius ignored");
            var exact=MeshTools.MatchPoints(new[]{a},new[]{P(0.5,0.5),P(0.5,0.5,1E-6)},0,true);
            Assert(exact[0].Mesh==0 && exact[1].Mesh==-1,"Zero-distance face match accepted an off-surface point");
        });
        check("mesh tools: invalid tolerances and invalid geometry fail without mutation", () =>
        {
            using var source=Quad();foreach(double limit in new[]{-1,double.NaN,double.PositiveInfinity})
            {bool failed=false;try{MeshTools.MatchPoints(new[]{source},new[]{P(0,0)},limit,false);}catch(ArgumentOutOfRangeException){failed=true;}Assert(failed,"Invalid distance accepted");}
            Assert(source.Faces.Count==1 && source.Vertices.Count==4,"Input changed on failure");
        });
        check("mesh tools: component GUIDs and all 36 icons are unique/valid resources", () =>
        {
            var components=typeof(MeshComponent).Assembly.GetTypes().Where(t=>!t.IsAbstract && typeof(GH_Component).IsAssignableFrom(t)).Select(t=>(GH_Component)Activator.CreateInstance(t)).ToArray();
            Assert(components.Select(c=>c.ComponentGuid).Distinct().Count()==components.Length,"Duplicate component GUID");
            var resources=Ant.GrasshopperPlugIn.Properties.Resources.ResourceManager.GetResourceSet(System.Globalization.CultureInfo.InvariantCulture,true,true);
            int icons=0;foreach(System.Collections.DictionaryEntry entry in resources)
            {if(entry.Value is Bitmap icon){icons++;Assert(icon.Width==24 && icon.Height==24,"Wrong embedded icon size: "+entry.Key);}}
            Assert(icons==36,"Not all 36 icons are embedded");
            foreach(var c in NewComponents())
            {
                var bitmap=(Bitmap)c.GetType().GetProperty("Icon",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(c);
                Assert(bitmap!=null && bitmap.Width==24 && bitmap.Height==24,"Wrong icon dimensions for "+c.Name);
                var archive=new GH_LooseChunk("Component");c.CreateAttributes();Assert(c.Write(archive),"Cannot save "+c.Name);
                var restored=(GH_Component)Activator.CreateInstance(c.GetType());restored.CreateAttributes();Assert(restored.Read(archive),"Cannot load "+c.Name);
            }
        });
        check("mesh tools: all eight GH components solve and matching retains null positions", () =>
        {
            foreach(var c in NewComponents())
            {
                using var doc=new GH_Document();c.CreateAttributes();doc.AddObject(c,false);
                ((Param_Mesh)c.Params.Input[0]).PersistentData.Append(new GH_Mesh(Quad()));
                if(c is MeshMatchComponent)
                {((Param_Point)c.Params.Input[1]).PersistentData.Append(new GH_Point(P(0,0)));((Param_Point)c.Params.Input[1]).PersistentData.Append(new GH_Point(P(200,200)));((Param_Point)c.Params.Input[1]).PersistentData.Append(new GH_Point(P(1,1)));}
                if(c is MeshExtractFacesComponent)((Param_Integer)c.Params.Input[1]).PersistentData.Append(new GH_Integer(0));
                Solve(doc,c);
                Assert(c.Params.Output[0].VolatileDataCount>0,"Missing primary output for "+c.Name);
                if(c is MeshMatchComponent)
                {
                    var indices=c.Params.Output[0].VolatileData.AllData(true).OfType<GH_Integer>().Select(v=>v.Value).ToArray();
                    Assert(indices.SequenceEqual(new[]{0,-1,0}) && c.Params.Output[2].VolatileDataCount==3,"Unmatched placeholder lost query alignment");
                }
            }
        });
        check("mesh tools: part maps use separate branches for multiple input meshes", () =>
        {
            using var doc=new GH_Document();var c=new MeshPartsComponent();c.CreateAttributes();doc.AddObject(c,false);
            var param=(Param_Mesh)c.Params.Input[0];param.PersistentData.Append(new GH_Mesh(Quad()),new GH_Path(4));param.PersistentData.Append(new GH_Mesh(Quad(3)),new GH_Path(4));param.PersistentData.Append(new GH_Mesh(Quad(8)),new GH_Path(9));
            Solve(doc,c);Assert(c.Params.Output[0].VolatileDataCount==3 && c.Params.Output[0].VolatileData.PathCount==3,"Part meshes mixed across iterations");
            Assert(c.Params.Output[1].VolatileData.Paths.SequenceEqual(c.Params.Output[0].VolatileData.Paths),"Face maps do not share mesh branches");
        });
        check("mesh tools: Planar Mesh old archives gain timings and retain task preference", () =>
        {
            var c=new MeshComponent();Assert(!c.UseTasks,"New instances should default to serial solving");c.CreateAttributes();c.UseTasks=true;
            c.Params.UnregisterOutputParameter(c.Params.Output[1]);c.Params.OnParametersChanged();var archive=new GH_LooseChunk("OldPlanarMesh");Assert(c.Write(archive),"Could not save old layout");
            var restored=new MeshComponent();restored.CreateAttributes();Assert(restored.Read(archive),"Could not restore old layout");
            Assert(restored.Params.Output.Count==2 && restored.UseTasks,"Old layout or task choice changed");
        });
        check("spatial cleanup: distant panels retain nearby projected constraints", () =>
        {
            var surfaces=Enumerable.Range(0,12).Select(i=>Brep.CreateFromCornerPoints(P(i*3,0),P(i*3+2,0),P(i*3+2,2),P(i*3,2),1E-7)).ToList();
            var embedded=Enumerable.Range(0,12).Select(i=>(IGH_GeometricGoo)new GH_Point(P(i*3+1,1,0.0005))).ToList();
            try
            {
                var result=PlanarGeometryCleanup.Clean(surfaces,embedded,Array.Empty<IGH_GeometricGoo>(),0.001,0.001,0.001,false);
                Assert(result.Surfaces.Count==12 && result.Embedded.Count==12,"Host RTree dropped valid distant panels or projections");
                foreach(var s in result.Surfaces)s.Dispose();foreach(var g in result.Diagnostics.Changed.Concat(result.Diagnostics.Discarded).Concat(result.Diagnostics.Unresolved))g.Dispose();
            }
            finally{foreach(var s in surfaces)s.Dispose();}
        });
    }

    static GH_Component[] NewComponents() => new GH_Component[]{new MeshQualityComponent(),new MeshTopologyComponent(),new MeshPartsComponent(),new MeshExtractFacesComponent(),new MeshNormalsComponent(),new MeshTriangulateComponent(),new MeshMatchNodesComponent(),new MeshMatchFacesComponent()};
}
