using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.Components
{
    public class GetUserDataComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the GetUserTextComponent class.
        /// </summary>
        public GetUserDataComponent()
          : base("Get User Data", "GUD", "Retrieve the user data attached to the geometry", "Ant", "Data")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Geometry", "G", "The geometry where to extract the user texts", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Keys", "K", "The keys of the geometry user texts", GH_ParamAccess.list);
            pManager.AddGenericParameter("Values", "V", "The user texts of the geometry", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            object input = null;
            if (!DA.GetData(0, ref input))
                return;

            GeometryBase geometry = GH_Convert.ToGeometryBase(input);
            if (geometry == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid geometry.");
                return;
            }

            List<string> keys = new List<string>();
            List<object> values = new List<object>();
            foreach (var kv in geometry.UserDictionary)
            {
                keys.Add(kv.Key);
                values.Add(kv.Value);
            }

            DA.SetDataList(0, keys);
            DA.SetDataList(1, values);
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.GetUserDataIcon;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("66937afa-540b-4be1-b4fa-dc2b99591234"); 
    }
}