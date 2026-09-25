using System;
using System.Linq;
using Ant.GrasshopperPlugIn.DataTypes;
using Ant.GrasshopperPlugIn.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.Components
{
    public class MeshRefinementComponent : GH_Component
    {
        public MeshRefinementComponent() : base("Mesh Refinement", "MR", "Attach a local mesh size to points, curves or planar regions.", "Ant", "Mesh") { }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGeometryParameter("Geometry", "G", "Point, curve or planar region to refine.", GH_ParamAccess.item);
            p.AddNumberParameter("Size", "S", "Positive local mesh size.", GH_ParamAccess.item);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p) => p.AddGenericParameter("Refinement", "MR", "Connect to Clean Geometry R or Planar Mesh R.", GH_ParamAccess.list);
        protected override void SolveInstance(IGH_DataAccess data)
        {
            IGH_GeometricGoo geometry = null; double size = 0;
            if (!data.GetData(0, ref geometry) || !data.GetData(1, ref size)) return;
            if (!double.IsFinite(size) || size <= 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Size must be finite and greater than zero."); return; }
            try
            {
                switch (geometry)
                {
                    case GH_Point point: data.SetDataList(0, new[] { new MeshPointRefinement(point.Value, size) }); return;
                    case GH_Line line: data.SetDataList(0, new[] { new MeshLineRefinement(line.Value, size) }); return;
                    case GH_Curve curve:
                        var poly = PlanarGeometryCleanup.Approximate(curve.Value, size / 1000);
                        data.SetDataList(0, poly.GetSegments().Select(l => new MeshLineRefinement(l, size))); return;
                    case GH_Surface surface: SetRegion(surface.Value); return;
                    case GH_Brep brep: SetRegion(brep.Value); return;
                    default: throw new ArgumentException("Geometry not supported. Use points, curves or planar surfaces.");
                }
                void SetRegion(Brep region)
                {
                    if (region == null || !region.IsValid || region.Faces.Any(f => !f.IsPlanar(1E-4))) throw new ArgumentException("Refinement surfaces must be valid and planar.");
                    data.SetDataList(0, new[] { new MeshSurfaceRefinement(region.DuplicateBrep(), size) });
                }
            }
            catch (Exception e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
        }
        protected override System.Drawing.Bitmap Icon => Properties.Resources.MeshRefinementIcon;
        public override Guid ComponentGuid => new Guid("8de166c1-85c1-4026-8d3f-0a3c6da70bd3");
        public override GH_Exposure Exposure => GH_Exposure.secondary;
    }
}
