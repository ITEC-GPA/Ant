using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.Components
{
    public class SetUserDataComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the SetUserDataComponent class.
        /// </summary>
        public SetUserDataComponent()
          : base("Set User Data", "SUD", "Attach user data to the geometry", "Ant", "Data")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Geometry", "G", "The geometry to which to attach the data", GH_ParamAccess.item);
            pManager.AddTextParameter("Keys", "K", "The key for accessing the user data attached", GH_ParamAccess.list);
            pManager.AddGenericParameter("Values", "V", "The data to attach to the geometry", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "G", "The geometry with the data attached", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            object input = null;
            List<string> keys = new List<string>();
            List<IGH_Goo> values = new List<IGH_Goo>();
            if (!DA.GetData(0, ref input))
                return;
            if (!DA.GetDataList(1, keys))
                return;
            if (!DA.GetDataList(2, values))
                return;

            GeometryBase geometry = GH_Convert.ToGeometryBase(input);
            if (geometry == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid geometry.");
                return;
            }

            if (keys.Count != values.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The number of keys and the number of values must be equal.");
                return;
            }

            GeometryBase new_geometry = geometry.Duplicate();
            for (int i = 0; i < keys.Count; i++)
            {
                try
                {                    
                    new_geometry.UserDictionary.Set(keys[i], (dynamic)values[i].GetType().GetProperty("Value").GetValue(values[i], null));
                }
                catch (Exception e)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Format("Can't store the {0} type", values[i].GetType()));
                    System.Diagnostics.Debug.WriteLine(e.Message);
                }
            }

            DA.SetData(0, new_geometry);
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.SetUserDataIcon;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("cb5b6f70-bd45-4ca1-b787-d783f9eceef1"); 
    }
}