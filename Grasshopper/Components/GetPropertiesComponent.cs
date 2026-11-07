using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino;
using Rhino.DocObjects;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

namespace Ant.GrasshopperPlugIn.Components
{
    public class GetPropertiesComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the GetPropertiesComponent class.
        /// </summary>
        public GetPropertiesComponent()
          : base("Get Properties", "GP", "Retrieve the Name, Layer and Color properties of the given geometry", "Ant", "Query")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "G", "The geometry where to extract the properties", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Name", "N", "The Geometry name", GH_ParamAccess.item);
            pManager.AddTextParameter("Layer", "L", "The layer name of the geometry", GH_ParamAccess.item);
            pManager.AddTextParameter("FullPath", "FP", "The layer full path of the geometry", GH_ParamAccess.item);
            pManager.AddColourParameter("Color", "C", "The geometry color", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            IGH_GeometricGoo geometry = null;
            if (!DA.GetData(0, ref geometry))
                return;

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            RhinoObject obj = doc.Objects.Find(geometry.ReferenceID);
            if (obj == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid referenced geometry");
                return;
            }
            string name = obj.Name;
            Layer layer = doc.Layers[obj.Attributes.LayerIndex];
            string layer_name = layer.Name;
            string layer_fullpath = layer.FullPath;
            Color color = obj.Attributes.ObjectColor;

            DA.SetData(0, name);
            DA.SetData(1, layer_name);
            DA.SetData(2, layer_fullpath);
            DA.SetData(3, color);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override Bitmap Icon => Properties.Resources.GetPropertiesIcon;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("743f9666-a250-467a-9e77-aa7ff63987b2"); 
    }
}