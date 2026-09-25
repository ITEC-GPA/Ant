using System;
using System.Collections.Generic;
using Ant.GrasshopperPlugIn.Helpers;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.Components
{
    public class CleanMergeMeshComponent : GH_Component
    {
        public CleanMergeMeshComponent()
            : base("Clean and Merge Mesh", "CMM", "Merge meshes and clean duplicate vertices, short edges and small or degenerate faces.", "Ant", "Mesh")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Meshes", "M", "Meshes to merge and clean. Inputs are not modified. Flatten this input to merge all branches.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Vertex tolerance", "VT", "Merge vertices within this distance, in model units. Default: Rhino document absolute tolerance. Zero merges only exactly coincident vertices.", GH_ParamAccess.item);
            pManager[1].Optional = true;
            pManager.AddNumberParameter("Minimum edge length", "EL", "Collapse edges shorter than this length, in model units. Default: vertex tolerance. Zero disables short-edge collapse.", GH_ParamAccess.item);
            pManager[2].Optional = true;
            pManager.AddNumberParameter("Minimum face area", "FA", "Remove faces at or below this area, in squared model units. Default: vertex tolerance squared. Removing faces may leave open boundaries. Zero removes only degenerate faces.", GH_ParamAccess.item);
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "The merged, cleaned mesh. Disconnected parts remain in the same mesh; this is not a Boolean union.", GH_ParamAccess.item);
            pManager.AddTextParameter("Report", "I", "Cleanup counts and the tolerances used.", GH_ParamAccess.item);
            foreach (var parameter in CleanupDiagnostics.CreateOutputs()) pManager.AddParameter(parameter);
        }

        protected override void SolveInstance(IGH_DataAccess data)
        {
            var meshes = new List<Mesh>();
            if (!data.GetDataList(0, meshes)) return;
            double vertexTolerance = Rhino.RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;
            data.GetData(1, ref vertexTolerance);
            double edgeLength = vertexTolerance;
            data.GetData(2, ref edgeLength);
            double faceArea = vertexTolerance * vertexTolerance;
            data.GetData(3, ref faceArea);

            try
            {
                var result = MeshCleanup.Clean(meshes, vertexTolerance, edgeLength, faceArea);
                foreach (string warning in result.Warnings)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
                data.SetData(0, result.Mesh);
                data.SetData(1, result.Report);
                CleanupDiagnostics.SetOutputs(data, 2, result.Diagnostics);
            }
            catch (Exception e)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
                var diagnostics = e.Data[CleanupDiagnostics.ExceptionKey] as CleanupDiagnostics ?? new CleanupDiagnostics();
                if (diagnostics.Messages.Count == 0)
                    for (int i = 0; i < meshes.Count; i++) diagnostics.Add(CleanupDiagnosticKind.Unresolved, meshes[i], $"M[{i}]", e.Message);
                CleanupDiagnostics.SetOutputs(data, 2, diagnostics);
                data.SetData(1, "ERROR: " + e.Message + "\n" + string.Join("\n", diagnostics.Messages));
            }
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.CleanMergeMeshIcon;
        public override BoundingBox ClippingBox => CleanupDiagnostics.PreviewBounds(base.ClippingBox, Params.Output, 2);
        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (!Hidden && !Locked) CleanupDiagnostics.Draw(args, Params.Output, 2);
        }
        public override Guid ComponentGuid => new Guid("a82014df-38ed-47e3-903a-c1ab80b55e40");
        public override GH_Exposure Exposure => GH_Exposure.primary;
    }
}
