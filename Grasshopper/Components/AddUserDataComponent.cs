using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Ant.GrasshopperPlugIn.DataTypes;

namespace Ant.GrasshopperPlugIn.Components
{
    public class AddUserDataComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the AddUserDataComponent class.
        /// </summary>
        public AddUserDataComponent()
          : base("Add User Data", "AUD", "Add the user data to a Rhino geometry", "Ant", "Data")
        {
        }
        
        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "G", "The geometry to which to attach the data", GH_ParamAccess.item);
            pManager.AddTextParameter("Keys", "K", "The key for accessing the user data attached", GH_ParamAccess.list);
            pManager.AddGenericParameter("Values", "V", "The data to attach to the geometry", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Referenced Geometry attributes", "A", "The referenced Geometry attributes to update", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            IGH_GeometricGoo geometry = null;
            List<string> keys = new List<string>();
            List<IGH_Goo> values = new List<IGH_Goo>();

            if (!DA.GetData(0, ref geometry))
                return;
            if (!DA.GetDataList(1, keys))
                return;
            if (!DA.GetDataList(2, values))
                return;

            if (!geometry.IsReferencedGeometry)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "This component works only with referenced geometies. Use the Bake User Data component instead.");
                return;
            }

            if (keys.Count != values.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The number of keys and the number of values must be equal.");
                return;
            }

            List<ReferendedGeometryAttribute> attrs = new List<ReferendedGeometryAttribute>();
            for (int i = 0; i < keys.Count; i++)
            {
                attrs.Add(new ReferendedGeometryAttribute(geometry.ReferenceID, keys[i], values[i]));
            }

            DA.SetDataList(0, attrs);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.AddUserDataIcon;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("30f79d12-71c4-4743-9fdc-b5bb86794c4e");     }
}