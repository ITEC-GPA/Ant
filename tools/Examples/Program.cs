using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Ant.GrasshopperPlugIn.Components;
using GH_IO.Serialization;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

internal static class Program
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path);
    internal static string GhFolder;
    static string systemFolder;
    [STAThread]
    static int Main(string[] args)
    {
        string system=Environment.GetEnvironmentVariable("RHINO_SYSTEM")??@"C:\Program Files\Rhino 8\System";
        systemFolder=system;
        GhFolder=Path.GetFullPath(Path.Combine(system,"..","Plug-ins","Grasshopper"));
        SetDllDirectory(system);Environment.SetEnvironmentVariable("PATH",Environment.GetEnvironmentVariable("PATH")+";"+system);
        AssemblyLoadContext.Default.Resolving+=ResolveAssembly;
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory,"Ant.Grasshopper.gha"));
        try
        {
            string output=args.Length>0?Path.GetFullPath(args[0]):Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","..","examples"));
            // Keep all Rhino/GH generic type constraints out of the entry-point type.
            return (int)Assembly.GetExecutingAssembly().GetType("ExampleBuilder").GetMethod("Run").Invoke(null,new object[]{output});
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    static Assembly ResolveAssembly(AssemblyLoadContext context,AssemblyName name)
    {
        foreach(var folder in new[]{Path.Combine(systemFolder,"netcore"),systemFolder,GhFolder,Path.Combine(GhFolder,"Components"),AppContext.BaseDirectory})
        {var path=Path.Combine(folder,name.Name+".dll");if(File.Exists(path))return context.LoadFromAssemblyPath(path);}
        return null;
    }
}

internal static class ExampleBuilder
{
    static string ghFolder;
    static Assembly mathComponents;
    static readonly Color NoteColor=Color.FromArgb(237,245,246), LogColor=Color.FromArgb(248,243,227);
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Run(string output)
    {
        ghFolder=Program.GhFolder;
        using var core=new Rhino.Runtime.InProcess.RhinoCore(new[]{"/NOSPLASH","/NOTEMPLATE"},Rhino.Runtime.InProcess.WindowStyle.NoWindow);
        // Register only Ant and built-in components; do not load the user's other plugins.
        var serverField=typeof(Grasshopper.Instances).GetFields(BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public).Single(f=>f.FieldType==typeof(GH_ComponentServer));
        serverField.SetValue(null,Activator.CreateInstance(typeof(GH_ComponentServer),true));
        var server=Grasshopper.Instances.ComponentServer;
        var parse=server.GetType().GetMethod("ParseGHA",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,null,new[]{typeof(Assembly),typeof(string),typeof(GH_LoadingMechanism)},null);
        mathComponents=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(ghFolder,"Components","MathComponents.gha"));
        foreach(var assembly in new[]{typeof(GH_Component).Assembly,mathComponents,typeof(MeshComponent).Assembly})
            parse.Invoke(server,new object[]{assembly,assembly.Location,GH_LoadingMechanism.Direct});
        Directory.CreateDirectory(output);GH_Document.EnableSolutions=true;
        using(var doc=Planar())SaveAndVerify(doc,output,"01_Pannello_forato_mesh_planare",VerifyPlanar);
        using(var doc=Cleanup())SaveAndVerify(doc,output,"02_Pulizia_qualita_e_parti",VerifyCleanup);
        using(var doc=Matching())SaveAndVerify(doc,output,"03_Matching_e_selezione",VerifyMatching);
        Console.WriteLine("PASS: 3 GH definitions saved, reopened and recomputed; geometry and wiring assertions passed.");return 0;
    }

    static T Put<T>(GH_Document doc,T obj,float x,float y,string nickname=null) where T:IGH_DocumentObject
    {
        if(nickname!=null)obj.NickName=nickname;
        obj.CreateAttributes();obj.Attributes.Pivot=new PointF(x,y);doc.AddObject(obj,false);return obj;
    }
    static GH_Panel Panel(GH_Document doc,string text,float x,float y,float width,float height,bool log=false)
    {
        var panel=Put(doc,new GH_Panel(),x,y);panel.UserText=text;panel.Properties.Multiline=true;panel.Properties.Wrap=true;
        panel.Properties.DrawIndices=log;panel.Properties.DrawPaths=false;panel.Properties.Colour=log?LogColor:NoteColor;
        panel.Properties.Font=new Font("Segoe UI",log?6.5f:7f);panel.Properties.Alignment=GH_Panel.Alignment.Left;
        panel.Attributes.Bounds=new RectangleF(x,y,width,height);return panel;
    }
    static GH_NumberSlider Slider(GH_Document doc,string name,decimal value,decimal min,decimal max,int decimals,float x,float y)
    {
        var slider=Put(doc,new GH_NumberSlider(),x,y,name);slider.Slider.Minimum=min;slider.Slider.Maximum=max;
        slider.Slider.DecimalPlaces=decimals;slider.SetSliderValue(value);slider.Attributes.Bounds=new RectangleF(x,y,240,25);return slider;
    }
    static void Wire(IGH_Param source,GH_Component target,int input)=>target.Params.Input[input].AddSource(source);
    static void Wire(GH_Component source,int output,GH_Component target,int input)=>Wire(source.Params.Output[output],target,input);
    static void Log(GH_Document doc,GH_Component source,int output,float x,float y,float width=360,float height=160)
    {var p=Panel(doc,"",x,y,width,height,true);p.AddSource(source.Params.Output[output]);}
    static void Number(GH_Component c,int input,double value)
    {var p=(Param_Number)c.Params.Input[input];p.PersistentData.Clear();p.PersistentData.Append(new GH_Number(value));}
    static void Integer(GH_Component c,int input,int value)
    {var p=(Param_Integer)c.Params.Input[input];p.PersistentData.Clear();p.PersistentData.Append(new GH_Integer(value));}
    static void Boolean(GH_Component c,int input,bool value)
    {var p=(Param_Boolean)c.Params.Input[input];p.PersistentData.Clear();p.PersistentData.Append(new GH_Boolean(value));}
    static Point3d P(double x,double y,double z=0)=>new Point3d(x,y,z);
    static Mesh Quad(double x,double y,double width,double height)
    {var mesh=new Mesh();mesh.Vertices.UseDoublePrecisionVertices=true;mesh.Vertices.AddVertices(new[]{P(x,y),P(x+width,y),P(x+width,y+height),P(x,y+height)});mesh.Faces.AddFace(0,1,2,3);return mesh;}
    static Mesh Triangle(Point3d a,Point3d b,Point3d c)
    {var mesh=new Mesh();mesh.Vertices.UseDoublePrecisionVertices=true;mesh.Vertices.AddVertices(new[]{a,b,c});mesh.Faces.AddFace(0,1,2);return mesh;}
    static Param_Mesh MeshInput(GH_Document doc,string name,float x,float y,params Mesh[] meshes)
    {var p=Put(doc,new Param_Mesh(),x,y,name);p.Hidden=true;foreach(var mesh in meshes)p.PersistentData.Append(new GH_Mesh(mesh));return p;}
    static GH_Document Document()=>new GH_Document{Enabled=true};

    static GH_Document Planar()
    {
        var doc=Document();
        Panel(doc,"01 / PANNELLO FORATO - DALLE GEOMETRIE ALLA MESH\nAnt 1.1.0 | Rhino 8 Windows | Tutte le geometrie sono salvate in questo file.\nPremi Run su Planar Mesh. Poi modifica gli slider e premi di nuovo Run; KR abilita il ricalcolo continuo.",50,30,1420,105);
        Panel(doc,"1. GEOMETRIA\nPannello 12 x 8 con foro 2 x 2.\nLe linee e i punti sono leggermente fuori piano. Un punto e' duplicato.\nClean Geometry proietta i vincoli interni e rimuove i duplicati.",50,170,320,160);
        var surfaces=Put(doc,new Param_Brep(),190,375,"Pannello con foro");surfaces.Hidden=true;
        using(var outer=new Rectangle3d(Plane.WorldXY,new Interval(0,12),new Interval(0,8)).ToNurbsCurve())
        using(var inner=new Rectangle3d(Plane.WorldXY,new Interval(5,7),new Interval(3,5)).ToNurbsCurve())
            surfaces.PersistentData.Append(new GH_Brep(Brep.CreatePlanarBreps(new[]{outer,inner},1E-7).Single()));
        var embedded=Put(doc,new Param_Geometry(),190,430,"Linee e punti da pulire");embedded.Hidden=true;
        embedded.PersistentData.Append(new GH_Line(new Line(P(1,2,0.005),P(11,2,0.005))));
        embedded.PersistentData.Append(new GH_Line(new Line(P(1,6,0.005),P(4,6,0.005))));
        embedded.PersistentData.Append(new GH_Point(P(9,6,0.006)));embedded.PersistentData.Append(new GH_Point(P(9,6,0.006)));
        var point=Put(doc,new Param_Point(),130,650,"Centro refinement");point.Hidden=true;point.PersistentData.Append(new GH_Point(P(3,4,0.004)));
        var local=Slider(doc,"Size locale",0.25m,0.1m,1m,2,50,715);
        var refinement=Put(doc,new MeshRefinementComponent(),410,650);refinement.Hidden=true;Wire(point,refinement,0);Wire(local,refinement,1);
        var clean=Put(doc,new CleanGeometryComponent(),550,410);clean.Hidden=true;Wire(surfaces,clean,0);Wire(embedded,clean,1);Wire(refinement,0,clean,2);
        var mt=Slider(doc,"MT - merge",0.01m,0.001m,0.03m,3,50,485);Wire(mt,clean,3);
        var pt=Slider(doc,"PT - proiezione",0.02m,0.001m,0.05m,3,50,525);Wire(pt,clean,4);Number(clean,5,0.001);
        Panel(doc,"2. PULIZIA E REFINEMENT\nS / E / R di Clean Geometry vanno agli omonimi ingressi del meshatore.\nPT=0.02 recupera gli scarti dal piano. Size locale=0.25 infittisce vicino al punto.\nAttiva la preview di CG per vedere C arancione, D rosso e U viola.",425,170,350,160);
        var mesh=Put(doc,new MeshComponent(),860,410);mesh.Hidden=true;Wire(clean,0,mesh,0);Wire(clean,1,mesh,1);Wire(clean,2,mesh,2);
        Integer(mesh,3,6);Boolean(mesh,4,false);Boolean(mesh,6,false);Number(mesh,9,0.1);
        var size=Slider(doc,"Size globale",1m,0.5m,2m,2,705,655);Wire(size,mesh,8);
        var run=Put(doc,new GH_BooleanToggle(),775,710,"KR - ricalcolo continuo");run.Value=false;Wire(run,mesh,10);
        Panel(doc,"3. MESHATURA\nPremi il pulsante Run.\nL'esempio usa triangoli (Frontal-Delaunay), con Recombine e Transfinite disattivati.\nLa tolleranza di PM conserva il significato storico del plugin: non e' PT.",815,170,310,160);
        var final=Put(doc,new CleanMergeMeshComponent(),1130,430);final.Hidden=false;Wire(mesh,0,final,0);Number(final,1,0.001);Number(final,2,0);Number(final,3,0);
        var topology=Put(doc,new MeshTopologyComponent(),1340,405);topology.Hidden=true;Wire(final,0,topology,0);
        Panel(doc,"4. RISULTATO\nClean and Merge Mesh unisce i nodi coincidenti. EL=0 e FA=0 preservano gli elementi sani.\nIl foro deve restare aperto. Mesh Topology espone i bordi esterni e quelli del foro.",1160,170,310,160);
        Log(doc,clean,3,420,790,480,200);Log(doc,mesh,1,930,790,540,100);Log(doc,topology,9,930,910,540,80);
        return doc;
    }

    static GH_Document Cleanup()
    {
        var doc=Document();
        Panel(doc,"02 / PULIZIA, QUALITA' E GESTIONE DELLE PARTI\nAnt 1.1.0 | Ricalcolo automatico | Geometrie gia' incluse.\nDue pannelli hanno una fessura di 0.0005; ci sono una faccia duplicata, una degenere, un triangolo minuscolo e uno molto allungato.",50,30,1420,105);
        var left=Quad(0,0,4,4);left.Vertices.Add(20,20,0);var duplicate=left.DuplicateMesh();duplicate.Flip(true,true,true);
        var source=MeshInput(doc,"Mesh con difetti",190,350,left,Quad(4.0005,0,3.9995,4),duplicate,Quad(0,7,4,2),
            Triangle(P(10,0),P(12,0),P(10,0.03)),Triangle(P(10,2),P(10.01,2),P(10,2.01)),Triangle(P(10,4),P(11,4),P(12,4)));
        Panel(doc,"1. RIPARA\nVT=0.001 salda la fessura tra i pannelli. FA=0.0001 elimina il triangolo minuscolo.\nEL=0 disattiva il collasso degli spigoli.\nGli input sono nascosti: attiva la loro preview per confrontarli.",50,170,320,155);
        var clean=Put(doc,new CleanMergeMeshComponent(),460,340);clean.Hidden=false;Wire(source,clean,0);
        var vt=Slider(doc,"VT - vertici",0.001m,0m,0.003m,4,50,440);Wire(vt,clean,1);Number(clean,2,0);
        var area=Slider(doc,"FA - area minima",0.0001m,0m,0.05m,5,50,485);Wire(area,clean,3);
        Panel(doc,"2. CONTROLLA E SELEZIONA\nMesh Quality segnala il triangolo allungato.\nCollega F a Extract Mesh Faces: S contiene i difetti, R il resto.\nER misura il rapporto tra lo spigolo piu' lungo e il piu' corto.",610,170,390,155);
        var quality=Put(doc,new MeshQualityComponent(),700,370);quality.Hidden=true;Wire(clean,0,quality,0);Number(quality,1,10);Number(quality,2,10);Number(quality,3,0.001);
        var extract=Put(doc,new MeshExtractFacesComponent(),1060,370);extract.Hidden=true;Wire(clean,0,extract,0);Wire(quality,4,extract,1);
        Log(doc,quality,6,1140,200,330,115);Log(doc,extract,6,1140,360,330,145);
        Panel(doc,"3. SEPARA, ORIENTA, TRIANGOLA\nMesh Parts ordina per area: il pannello grande viene prima.\nOgni parte ha un ramo e le mappe agli indici originali.\nLe superfici aperte non hanno un 'esterno': Normals uniforma le facce; Flip sceglie il verso.",50,640,460,170);
        var parts=Put(doc,new MeshPartsComponent(),700,680);parts.Hidden=true;Wire(clean,0,parts,0);
        var normals=Put(doc,new MeshNormalsComponent(),1000,680);normals.Hidden=true;Wire(parts,0,normals,0);
        var triangulate=Put(doc,new MeshTriangulateComponent(),1280,680);triangulate.Hidden=true;Wire(normals,0,triangulate,0);
        var topology=Put(doc,new MeshTopologyComponent(),700,990);topology.Hidden=true;Wire(clean,0,topology,0);
        Log(doc,parts,4,550,795,350,140);Log(doc,triangulate,2,1020,795,450,140);Log(doc,topology,9,1010,975,460,105);
        Log(doc,clean,1,50,865,460,215);
        Panel(doc,"PREVIEW\nDi default vedi CMM e la diagnostica: arancione=modificato, rosso=scartato, viola=irrisolto.\nPer isolare il risultato di un altro componente, spegni la preview di CMM e attiva quella desiderata.",550,1130,920,100);
        return doc;
    }

    static GH_Component Dispatch()
    {
        foreach(var assembly in new[]{typeof(GH_Component).Assembly,mathComponents})
            foreach(var type in assembly.GetTypes().Where(t=>!t.IsAbstract && typeof(GH_Component).IsAssignableFrom(t) && t.Name.Contains("Dispatch",StringComparison.OrdinalIgnoreCase)))
                if(Activator.CreateInstance(type) is GH_Component component && component.Name=="Dispatch")return component;
        throw new InvalidOperationException("Built-in Dispatch component was not found.");
    }
    static GH_Document Matching()
    {
        var doc=Document();
        Panel(doc,"03 / MATCHING DI NODI E FACCE + ESTRAZIONE\nAnt 1.1.0 | Un'unica mesh, due isole | Gli indici delle facce valgono solo per questa mesh.\nConfronta le uscite M, V/F, D e OK. Lo stesso punto puo' appartenere a una faccia senza essere vicino a un vertice.",50,30,1420,105);
        var mesh=new Mesh();mesh.Vertices.UseDoublePrecisionVertices=true;
        for(int y=0;y<3;y++)for(int x=0;x<3;x++)mesh.Vertices.Add(x*2,y*2,0);
        for(int y=0;y<2;y++)for(int x=0;x<2;x++){int i=y*3+x;mesh.Faces.AddFace(i,i+1,i+4,i+3);}
        using(var island=Quad(10,0,2,2))mesh.Append(island);
        var source=MeshInput(doc,"Mesh di ricerca",200,340,mesh);source.Hidden=false;
        var points=Put(doc,new Param_Point(),200,455,"Punti di prova");
        foreach(var point in new[]{P(0.1,0.1,0.01),P(1.2,1.3,0.05),P(10,0,0),P(8,7,0)})points.PersistentData.Append(new GH_Point(point));
        var distance=Slider(doc,"D - distanza massima",0.2m,0m,1m,2,50,535);
        Panel(doc,"PUNTI IN ORDINE\n0: vicino a un nodo e alla faccia.\n1: interno a una faccia, lontano dai nodi.\n2: nodo esatto della seconda isola.\n3: lontano da tutte le mesh.\nCon D=0.20, MMN trova 0 e 2; MMF trova 0, 1 e 2.",50,650,390,185);
        var nodes=Put(doc,new MeshMatchNodesComponent(),620,330);nodes.Hidden=true;Wire(source,nodes,0);Wire(points,nodes,1);Wire(distance,nodes,2);
        var faces=Put(doc,new MeshMatchFacesComponent(),620,600);faces.Hidden=false;Wire(source,faces,0);Wire(points,faces,1);Wire(distance,faces,2);
        Panel(doc,"NODI / RTree\nV contiene gli indici dei vertici originali.\nDistanze entro il limite, incluso il bordo.\nUn punto non trovato restituisce -1 e OK=False.",490,170,410,100);
        Log(doc,nodes,1,770,310,140,145);Log(doc,nodes,5,950,310,520,100);
        Panel(doc,"FACCE / PUNTO PIU' VICINO\nF contiene gli indici delle facce, non dei baricentri.\nI punti di uscita sono proiettati sulla mesh.",490,445,410,100);
        Log(doc,faces,1,770,585,140,150);Log(doc,faces,4,950,585,150,150);
        var dispatch=Put(doc,Dispatch(),1180,645);Wire(faces,1,dispatch,0);Wire(faces,4,dispatch,1);
        var extract=Put(doc,new MeshExtractFacesComponent(),1360,645);extract.Hidden=true;Wire(source,extract,0);Wire(dispatch,0,extract,1);
        Panel(doc,"FILTRA PRIMA DI ESTRARRE\nDispatch riceve F come lista e OK come pattern.\nA contiene solo gli indici trovati: cosi' -1 non arriva a Extract Mesh Faces.\nGli indici duplicati selezionano la faccia una sola volta. Attiva la preview di Extract per isolare le facce.",950,805,520,170);
        Log(doc,extract,6,490,805,410,170);Log(doc,faces,5,50,870,390,105);
        return doc;
    }

    static T Component<T>(GH_Document doc) where T:GH_Component=>doc.Objects.OfType<T>().Single();
    static Mesh[] Meshes(GH_Component c,int output=0)=>c.Params.Output[output].VolatileData.AllData(true).OfType<GH_Mesh>().Select(g=>g.Value).ToArray();
    static void Assert(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void SetRun(GH_Document doc,bool run)
    {foreach(var toggle in doc.Objects.OfType<GH_BooleanToggle>()){toggle.Value=run;toggle.ExpireSolution(false);}}
    static void Solve(GH_Document doc)
    {
        doc.Enabled=true;doc.ExpireSolution();doc.NewSolution(false);
        foreach(var component in doc.Objects.OfType<GH_Component>())
        {
            var errors=component.RuntimeMessages(GH_RuntimeMessageLevel.Error);
            Assert(errors.Count==0,component.Name+": "+string.Join("; ",errors));
        }
    }
    static void VerifyPlanar(GH_Document doc)
    {
        var clean=Component<CleanGeometryComponent>(doc);var mesher=Component<MeshComponent>(doc);var final=Component<CleanMergeMeshComponent>(doc);
        Assert(clean.Params.Output[0].VolatileData.AllData(true).OfType<GH_Brep>().Single().Value.Loops.Count==2,"Planar example lost its hole.");
        var mesh=Meshes(final).Single();Assert(mesh.IsValid && mesh.Faces.Count>20,"Planar example did not mesh.");
        var closest=mesh.ClosestMeshPoint(P(6,4),0);Assert(closest!=null && closest.Point.DistanceTo(P(6,4))>0.9,"Mesher filled the hole.");
        Assert(mesher.Params.Input[0].Sources.Single()==clean.Params.Output[0] && mesher.Params.Input[1].Sources.Single()==clean.Params.Output[1] && mesher.Params.Input[2].Sources.Single()==clean.Params.Output[2],"S/E/R wiring changed.");
        Console.WriteLine($"PASS planar: {mesh.Faces.Count} faces, hole preserved, S/E/R wired.");
    }
    static void VerifyCleanup(GH_Document doc)
    {
        var clean=Component<CleanMergeMeshComponent>(doc);var mesh=Meshes(clean).Single();
        Assert(mesh.IsValid && mesh.Faces.Count==4,"Unexpected cleaned face count: "+mesh.Faces.Count);
        var ids=Component<MeshQualityComponent>(doc).Params.Output[4].VolatileData.AllData(true).OfType<GH_Integer>().Select(i=>i.Value).ToArray();
        Assert(ids.Length==1,"Expected exactly one slender face.");
        Assert(Meshes(Component<MeshExtractFacesComponent>(doc)).Single().Faces.Count==1,"Quality selection did not reach extraction.");
        var parts=Meshes(Component<MeshPartsComponent>(doc));Assert(parts.Length==3 && parts[0].Faces.Count==2,"Incorrect connected parts/order.");
        Assert(Meshes(Component<MeshTriangulateComponent>(doc)).All(m=>m.Faces.QuadCount==0),"Triangulation left quads.");
        Console.WriteLine("PASS cleanup: 4 faces, 3 parts, 1 flagged face, triangulated outputs.");
    }
    static void VerifyMatching(GH_Document doc)
    {
        bool[] Matches(GH_Component c)=>c.Params.Output[4].VolatileData.AllData(true).OfType<GH_Boolean>().Select(g=>g.Value).ToArray();
        Assert(Matches(Component<MeshMatchNodesComponent>(doc)).SequenceEqual(new[]{true,false,true,false}),"Node matching changed.");
        Assert(Matches(Component<MeshMatchFacesComponent>(doc)).SequenceEqual(new[]{true,true,true,false}),"Face matching changed.");
        Assert(Meshes(Component<MeshExtractFacesComponent>(doc)).Single().Faces.Count==2,"Dispatch failed to remove -1 or duplicate indices.");
        Console.WriteLine("PASS matching: 2 node hits, 3 face hits, 2 unique selected faces, unmatched filtered.");
    }
    static void SaveAndVerify(GH_Document doc,string output,string name,Action<GH_Document> verify)
    {
        // Keep tall component capsules clear of the instruction panels.
        if(name.StartsWith("01"))
        {
            foreach(var obj in doc.Objects)
            {
                var pivot=obj.Attributes.Pivot;
                if(obj is GH_Panel && pivot.Y<750)continue;
                obj.Attributes.Pivot=new PointF(pivot.X,pivot.Y+100);
            }
        }
        else if(name.StartsWith("02"))
        {
            Component<MeshQualityComponent>(doc).Attributes.Pivot=new PointF(700,415);
            Component<CleanMergeMeshComponent>(doc).Attributes.Pivot=new PointF(460,395);
            Component<MeshTopologyComponent>(doc).Attributes.Pivot=new PointF(700,1090);
            foreach(var panel in doc.Objects.OfType<GH_Panel>().Where(p=>p.Attributes.Pivot.Y==1130))
                panel.Attributes.Pivot=new PointF(panel.Attributes.Pivot.X,1260);
        }
        else
        {
            Component<MeshMatchNodesComponent>(doc).Attributes.Pivot=new PointF(620,365);
            Component<MeshMatchFacesComponent>(doc).Attributes.Pivot=new PointF(620,650);
            foreach(var panel in doc.Objects.OfType<GH_Panel>().Where(p=>p.Attributes.Pivot.X==770 && p.Attributes.Pivot.Y==310))
                panel.Attributes.Bounds=new RectangleF(770,310,140,120);
        }
        foreach(var obj in doc.Objects)obj.Attributes.PerformLayout();
        SetRun(doc,true);Solve(doc);verify(doc);
        var path=Path.Combine(output,name+".gh");doc.FilePath=path;
        SetRun(doc,false);
        var archive=new GH_Archive();Assert(archive.AppendObject(doc,"Definition"),"Could not serialize "+name);
        Assert(archive.WriteToFile(path,true,false),"Could not write "+path);
        var read=new GH_Archive();Assert(read.ReadFromFile(path),"Could not read "+path);
        using var restored=new GH_Document();Assert(read.ExtractObject(restored,"Definition"),"Could not restore "+name);
        Assert(restored.Objects.Count==doc.Objects.Count,"Objects lost on loading "+name);
        Assert(restored.Objects.OfType<GH_BooleanToggle>().All(t=>!t.Value),"Saved example should use the Run button.");
        SetRun(restored,true);Solve(restored);verify(restored);
        foreach(var obj in restored.Objects)obj.Attributes.PerformLayout();
        using var canvas=new GH_Canvas{Width=1540,Height=name.StartsWith("02")?1410:name.StartsWith("01")?1150:1050,Document=restored};
        canvas.CreateControl();canvas.Viewport.Size=canvas.Size;canvas.Viewport.Zoom=1;canvas.Viewport.Target=System.Drawing.Point.Empty;
        using var bitmap=canvas.GenerateHiResImageTile(canvas.Viewport,Color.WhiteSmoke);
        bitmap.Save(Path.Combine(output,name+".png"),ImageFormat.Png);
        canvas.Document=null;
        Console.WriteLine("WROTE "+path);
    }
}
