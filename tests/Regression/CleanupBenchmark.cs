using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Ant.GrasshopperPlugIn.Helpers;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

internal static class CleanupBenchmark
{
    internal static int Run()
    {
        // Runs against either the baseline GHA or the candidate GHA in an isolated test directory.
        var surfaces=new List<Brep>();var embedded=new List<IGH_GeometricGoo>();
        for(int i=0;i<36;i++)
        {
            double x=(i%6)*12,y=(i/6)*12;
            surfaces.Add(Brep.CreateFromCornerPoints(new Point3d(x,y,0),new Point3d(x+10,y,0),new Point3d(x+10,y+10,0),new Point3d(x,y+10,0),1E-7));
            for(int j=1;j<=6;j++)embedded.Add(new GH_Line(new Line(new Point3d(x+1,y+j,0.0001),new Point3d(x+9,y+j,0.0001))));
        }
        void Clean()
        {
            var r=PlanarGeometryCleanup.Clean(surfaces,embedded,Array.Empty<IGH_GeometricGoo>(),0.001,0.001,0.001,false);
            if(r.Surfaces.Count!=36 || r.Embedded.Count!=216)throw new Exception("Benchmark output changed");
            foreach(var s in r.Surfaces)s.Dispose();
            foreach(var g in r.Diagnostics.Changed.Concat(r.Diagnostics.Discarded).Concat(r.Diagnostics.Unresolved))g.Dispose();
        }
        try
        {
            Clean();var times=new List<double>();var allocations=new List<long>();
            for(int i=0;i<3;i++)
            {
                GC.Collect();GC.WaitForPendingFinalizers();long start=GC.GetAllocatedBytesForCurrentThread();var timer=Stopwatch.StartNew();
                Clean();timer.Stop();times.Add(timer.Elapsed.TotalMilliseconds);allocations.Add(GC.GetAllocatedBytesForCurrentThread()-start);
            }
            times.Sort();allocations.Sort();Console.WriteLine(FormattableString.Invariant($"BENCH clean geometry: 36 panels, 216 projected lines; median {times[1]:F2} ms; managed allocations {allocations[1]} bytes; output 36 surfaces / 216 lines."));
            return 0;
        }
        finally{foreach(var s in surfaces)s.Dispose();}
    }
}
