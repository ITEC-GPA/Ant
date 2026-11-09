using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.DocObjects;
using Ant.GrasshopperPlugIn.Attributes;
using Ant.GrasshopperPlugIn.DataTypes;

namespace Ant.GrasshopperPlugIn.Components
{
    public class UpdateGeometryUserDataComponent : GH_Component
    {
        protected bool _bake;

        /// <summary>
        /// Initializes a new instance of the UpdateGeometryUserDataComponent class.
        /// </summary>
        public UpdateGeometryUserDataComponent()
          : base("Update Geometry User Data", "UUD", "Update the user data on a Rhino referenced geometry", "Ant", "Data")
        {
            _bake = false;
        }

        public override void CreateAttributes()
        {
            ComponentOneButtonAttributes attr = new ComponentOneButtonAttributes(this, "Update");
            attr.ButtonPressed += () =>
            {
                _bake = true;
                ExpireSolution(true);
            };
            m_attributes = attr;
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Referenced Geometry attributes", "A", "The referenced Geometry attributes to update", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!_bake) return;
            try
            {
                var attrs = new List<ReferendedGeometryAttribute>();
                if (!DA.GetDataList(0, attrs)) return;
                Helpers.UserDataStorage.Update(Rhino.RhinoDoc.ActiveDoc, attrs,
                    message => AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, message));
            }
            finally { _bake = false; }
        }
        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.UpdateGeometryUserDataIcon;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("640f970a-5473-4feb-be06-82165507952a"); 
    }
}
