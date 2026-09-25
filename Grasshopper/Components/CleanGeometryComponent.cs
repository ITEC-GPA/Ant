using System;
using System.Collections.Generic;
using System.Linq;
using Ant.GrasshopperPlugIn.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.Components
{
    public class CleanGeometryComponent : GH_Component
    {
        public CleanGeometryComponent() : base("Clean Geometry", "CG", "Clean planar surfaces, project and merge embedded geometry and preserve mesh refinements before Planar Mesh.", "Ant", "Mesh") { }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddBrepParameter("Surfaces", "S", "Planar surfaces to clean. Each branch is processed separately; flatten S/E/R together to process the entire model.", GH_ParamAccess.list);
            p.AddGeometryParameter("Embedded", "E", "Points, lines, curves and planar regions to project, clip and merge.", GH_ParamAccess.list); p[1].Optional = true;
            p.AddGenericParameter("Refinements", "R", "Mesh Refinement objects. Local mesh sizes are preserved; coincident refinements use the smallest size.", GH_ParamAccess.list); p[2].Optional = true;
            p.AddNumberParameter("Merge tolerance", "MT", "Vertex/endpoint matching and minimum segment length, in model units. Default: Rhino document absolute tolerance.", GH_ParamAccess.item); p[3].Optional = true;
            p.AddNumberParameter("Projection tolerance", "PT", "Maximum distance for projecting onto a surface or snapping onto its border, in model units. Default: MT.", GH_ParamAccess.item); p[4].Optional = true;
            p.AddNumberParameter("Curve tolerance", "CT", "Maximum chord deviation when approximating curved boundaries and constraints, in model units. Default: MT / 10.", GH_ParamAccess.item); p[5].Optional = true;
            p.AddBooleanParameter("Union coplanar surfaces", "U", "Boolean union of coplanar surfaces. Removes internal panel boundaries. False keeps separate panels; duplicate faces are always removed.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBrepParameter("Surfaces", "S", "Cleaned planar faces, with holes preserved. Connect to Planar Mesh S.", GH_ParamAccess.list);
            p.AddGeometryParameter("Embedded", "E", "Projected, clipped and merged geometry. Parts outside the surfaces are excluded and logged. Connect to Planar Mesh E.", GH_ParamAccess.list);
            p.AddGenericParameter("Refinements", "R", "Cleaned refinements, including their mesh sizes. Connect to Planar Mesh R.", GH_ParamAccess.list);
            p.AddTextParameter("Log", "L", "Tolerances, counts, projections, clipping and unresolved input geometry.", GH_ParamAccess.list);
            foreach (var parameter in CleanupDiagnostics.CreateOutputs()) p.AddParameter(parameter);
        }

        protected override void SolveInstance(IGH_DataAccess data)
        {
            var surfaces = new List<Brep>(); var embedded = new List<IGH_GeometricGoo>(); var refinements = new List<IGH_GeometricGoo>();
            if (!data.GetDataList(0, surfaces)) return;
            data.GetDataList(1, embedded); data.GetDataList(2, refinements);
            double merge = Rhino.RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;
            data.GetData(3, ref merge);
            double projection = merge, curve = merge / 10;
            data.GetData(4, ref projection); data.GetData(5, ref curve);
            bool union = false; data.GetData(6, ref union);
            try
            {
                var result = PlanarGeometryCleanup.Clean(surfaces, embedded, refinements, merge, projection, curve, union);
                data.SetDataList(0, result.Surfaces); data.SetDataList(1, result.Embedded);
                data.SetDataList(2, result.Refinements); data.SetDataList(3, result.Log);
                CleanupDiagnostics.SetOutputs(data, 4, result.Diagnostics);
                foreach (var warning in result.Warnings) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
            }
            catch (Exception e)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
                var diagnostics = e.Data[CleanupDiagnostics.ExceptionKey] as CleanupDiagnostics ?? new CleanupDiagnostics();
                if (diagnostics.Messages.Count == 0)
                    for (int i = 0; i < surfaces.Count; i++) diagnostics.Add(CleanupDiagnosticKind.Unresolved, surfaces[i], $"S[{i}]", e.Message);
                CleanupDiagnostics.SetOutputs(data, 4, diagnostics);
                data.SetDataList(3, new[] { "ERROR: " + e.Message }.Concat(diagnostics.Messages));
            }
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.CleanGeometryIcon;
        public override BoundingBox ClippingBox => CleanupDiagnostics.PreviewBounds(base.ClippingBox, Params.Output, 4);
        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (!Hidden && !Locked) CleanupDiagnostics.Draw(args, Params.Output, 4);
        }
        public override Guid ComponentGuid => new Guid("a8c3cb44-8ca2-4cc2-956a-50cf7d94910c");
        public override GH_Exposure Exposure => GH_Exposure.primary;
    }

    // Keep the original GUID and port order so existing GH files are not silently rewired.
    public sealed class LegacyCleanGeometryComponent : GH_Component
    {
        public LegacyCleanGeometryComponent() : base("CleanGeometry (Legacy)", "CG", "Legacy ports; use Clean Geometry for S/E/R and separate tolerances.", "Ant", "Mesh") { }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddBrepParameter("Surfaces", "S", "Planar surfaces.", GH_ParamAccess.list);
            p.AddGeometryParameter("Embedded", "E", "Embedded geometry.", GH_ParamAccess.list); p[1].Optional = true;
            p.AddNumberParameter("Tolerance", "T", "Matching tolerance in model units.", GH_ParamAccess.item, 0.1);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBrepParameter("Surfaces", "S", "Cleaned surfaces.", GH_ParamAccess.list);
            p.AddGeometryParameter("Embedded", "E", "Cleaned embedded geometry.", GH_ParamAccess.list);
            p.AddTextParameter("Info", "I", "Cleanup log.", GH_ParamAccess.list);
            foreach (var parameter in CleanupDiagnostics.CreateOutputs()) p.AddParameter(parameter);
        }
        protected override void SolveInstance(IGH_DataAccess data)
        {
            var surfaces = new List<Brep>(); var embedded = new List<IGH_GeometricGoo>(); double tolerance = 0.1;
            if (!data.GetDataList(0, surfaces)) return;
            data.GetDataList(1, embedded); data.GetData(2, ref tolerance);
            try
            {
                var result = PlanarGeometryCleanup.Clean(surfaces, embedded, null, tolerance, tolerance, tolerance / 10, false);
                data.SetDataList(0, result.Surfaces); data.SetDataList(1, result.Embedded); data.SetDataList(2, result.Log);
                CleanupDiagnostics.SetOutputs(data, 3, result.Diagnostics);
                foreach (var warning in result.Warnings) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
            }
            catch (Exception e)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
                var diagnostics = e.Data[CleanupDiagnostics.ExceptionKey] as CleanupDiagnostics ?? new CleanupDiagnostics();
                if (diagnostics.Messages.Count == 0)
                    for (int i = 0; i < surfaces.Count; i++) diagnostics.Add(CleanupDiagnosticKind.Unresolved, surfaces[i], $"S[{i}]", e.Message);
                CleanupDiagnostics.SetOutputs(data, 3, diagnostics);
                data.SetDataList(2, new[] { "ERROR: " + e.Message }.Concat(diagnostics.Messages));
            }
        }
        protected override System.Drawing.Bitmap Icon => Properties.Resources.CleanGeometryIcon;
        public override BoundingBox ClippingBox => CleanupDiagnostics.PreviewBounds(base.ClippingBox, Params.Output, 3);
        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (!Hidden && !Locked) CleanupDiagnostics.Draw(args, Params.Output, 3);
        }
        public override Guid ComponentGuid => new Guid("169774c8-75df-45fb-a79e-e8830a020c06");
        public override GH_Exposure Exposure => GH_Exposure.hidden;
    }
}
