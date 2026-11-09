using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Ant.GrasshopperPlugIn.Components;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using GMesh = GPC.Geometry.Meshes.GMesh.GMesh;

static class Program
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool SetDllDirectory(string path);
    static int passed;
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static void Check(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }

    [STAThread]
    static int Main()
    {
        string system = Environment.GetEnvironmentVariable("RHINO_SYSTEM") ?? @"C:\Program Files\Rhino 8\System";
        string gh = Path.GetFullPath(Path.Combine(system, "..", "Plug-ins", "Grasshopper"));
        SetDllDirectory(system);
        Environment.SetEnvironmentVariable("PATH", Environment.GetEnvironmentVariable("PATH") + ";" + system);
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (var folder in new[] { Path.Combine(system,"netcore"), system, gh, AppContext.BaseDirectory })
            {
                var path = Path.Combine(folder, name.Name + ".dll");
                if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        // A GHA is a managed assembly with a Grasshopper-specific extension.
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "Ant.Grasshopper.gha"));
        try { return Run(); }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run()
    {
        using var core = new Rhino.Runtime.InProcess.RhinoCore(new[] { "/NOSPLASH", "/NOTEMPLATE" }, Rhino.Runtime.InProcess.WindowStyle.NoWindow);
        if (Environment.GetEnvironmentVariable("ANT_CLEANUP_BENCHMARK") == "1") return CleanupBenchmark.Run();
        Check("multiface Brep preserves both faces and their holes", () =>
        {
            using var first = Rectangle(0);
            using var outer = new Rectangle3d(Plane.WorldXY, new Interval(20,30), new Interval(0,10)).ToNurbsCurve();
            using var inner = new Rectangle3d(Plane.WorldXY, new Interval(23,27), new Interval(3,7)).ToNurbsCurve();
            using var second = Brep.CreatePlanarBreps(new[] { outer, inner }, 1E-5).Single();
            using var multi = first.DuplicateBrep(); multi.Append(second);
            var result = new Probe().Prepare(new() { multi }, new());
            Assert(result.errors.Count == 0, string.Join(";",result.errors));
            Assert(result.shapes.Count == 2, "A face was lost");
            Assert(!result.shapes[0].HasHoles && result.shapes[1].Holes.Length == 1, "Hole attached to wrong face");
        });
        Check("polyline on second face is not consumed by the first", () =>
        {
            using var first=Rectangle(0); using var second=Rectangle(20);
            using var curve=new PolylineCurve(new[] { new Point3d(21,1,0),new Point3d(22,2,0),new Point3d(24,2,0) });
            var r=new Probe().Prepare(new(){first,second},new(){new GH_Curve(curve)});
            Assert(r.errors.Count==0 && r.warnings.Count==0,"Unexpected preparation diagnostics");
            Assert(r.embedded.Count==1 && r.embedded.ContainsKey(r.shapes[1]) && r.embedded[r.shapes[1]].Length==2,"Curve lost or attached to wrong face");
        });
        Check("partial curve projection does not leave partial constraints", () =>
        {
            using var first=Rectangle(0); using var second=Rectangle(20);
            using var curve=new PolylineCurve(new[]{new Point3d(1,1,0),new Point3d(2,2,0),new Point3d(25,2,0)});
            var r=new Probe().Prepare(new(){first,second},new(){new GH_Curve(curve)});
            Assert(r.embedded.Count==0 && r.warnings.Count==1,"Partial embedded geometry was committed");
        });
        Check("preparation errors abort generation without returning a partial mesh", () =>
        {
            using var first=Rectangle(0);
            var r=new Probe().Compute(new(){first,null}, null);
            Assert(r.Value.Count==0 && r.Errors.Any(e=>e.Contains("Surface 1")),"Invalid input was not rejected");
            Assert(r.Exceptions==null || r.Exceptions.Count==0,"Generator was called after invalid preparation");
        });
        Check("curved faces are rejected", () =>
        {
            using var sphere=new Sphere(Point3d.Origin,5).ToBrep();
            var r=new Probe().Prepare(new(){sphere},new());
            Assert(r.errors.Count>0 && r.shapes.Count==0,"Nonplanar face accepted");
        });
        Check("native GMesh generation and Rhino mesh conversion", () =>
        {
            using var first=Rectangle(0);
            var r=new Probe().Compute(new(){first},new GMesh.GMeshGenerateOptions { MeshSize=2, Recombine=false, Transfinite=false });
            Assert(r.Errors.Count==0,string.Join(";",r.Errors));
            Assert(r.Value.Count==1 && r.Value[0].Value.IsValid && r.Value[0].Value.Faces.Count>0,"Invalid output mesh");
        });
        foreach(bool tasks in new[]{false,true})
            Check("Run processes both data branches, tasks="+tasks,()=>CheckBranches(tasks));
        ToleranceChecks.Run(Check);
        UserDataChecks.Run(Check);
        MeshCleanupChecks.Run(Check);
        PlanarGeometryCleanupChecks.Run(Check);
        RefinementChecks.Run(Check);
        CleanupDiagnosticChecks.Run(Check);
        MeshToolsChecks.Run(Check);
        Console.WriteLine($"{passed} regression checks passed.");
        return 0;
    }

    static Brep Rectangle(double x) => Brep.CreateFromCornerPoints(new Point3d(x,0,0),new Point3d(x+10,0,0),new Point3d(x+10,10,0),new Point3d(x,10,0),1E-5);
    static void CheckBranches(bool tasks)
    {
        using var doc=new GH_Document();
        var component=new Probe(); component.CreateAttributes(); component.UseTasks=tasks;
        doc.AddObject(component,false);
        var input=(Param_Brep)component.Params.Input[0];
        input.PersistentData.Append(new GH_Brep(Rectangle(0)),new GH_Path(0));
        input.PersistentData.Append(new GH_Brep(Rectangle(20)),new GH_Path(1));
        ((Param_Number)component.Params.Input[8]).PersistentData.Append(new GH_Number(2));
        GH_Document.EnableSolutions = true;
        doc.Enabled = true;
        component.ExpireSolution(false);
        component.RequestRun();
        doc.NewSolution(false);
        Console.WriteLine($"Branch result: paths={component.Params.Output[0].VolatileData.PathCount}, count={component.Params.Output[0].VolatileDataCount}, phase={component.Phase}, run={component.RunRequested}");
        foreach (var message in component.RuntimeMessages(GH_RuntimeMessageLevel.Error)) Console.WriteLine("ERROR " + message);
        foreach (var message in component.RuntimeMessages(GH_RuntimeMessageLevel.Warning)) Console.WriteLine("WARNING " + message);
        Assert(component.Params.Output[0].VolatileData.PathCount==2,"Second branch missing");
        Assert(component.Params.Output[0].VolatileDataCount==2,"Wrong mesh count");
        Assert(!component.RunRequested,"Run was not reset");
        Assert(component.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0,"Grasshopper reported errors");
    }

    sealed class Probe : MeshComponent
    {
        public void RequestRun() => _run=true;
        public bool RunRequested => _run;
        public (List<GPC.Geometry.Shape> shapes,Dictionary<GPC.Geometry.Shape,GPC.Geometry.GeometryBase[]> embedded,List<string> errors,List<string> warnings) Prepare(List<Brep> surfaces,List<IGH_GeometricGoo> constraints)
        {
            var errors=new List<string>(); var warnings=new List<string>();
            var r=PreProcessMesh(surfaces,constraints,new(),1E-4,1E-7,0.001,2,ref errors,ref warnings);
            return(r.shapes,r.embeddedGeometries,errors,warnings);
        }
        public SolveResults Compute(List<Brep> surfaces,GMesh.GMeshGenerateOptions options) => ComputeMesh(surfaces,new(),new(),options,1E-4,1E-7,0.001,2);
    }
}

