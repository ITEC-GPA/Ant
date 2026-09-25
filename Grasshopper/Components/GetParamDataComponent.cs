using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.Components
{
    public class GetParamDataComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the GetParamDataComponent class.
        /// </summary>
        public GetParamDataComponent()
          : base("Get Param Data", "PD", "Get the data of the connected param", "Ant", "Utilities")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Source", "S", "The source param", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Name", "N", "The param name", GH_ParamAccess.list);
            pManager.AddTextParameter("NickName", "NN", "The param nick name", GH_ParamAccess.list);
            pManager.AddTextParameter("Description", "D", "The param description", GH_ParamAccess.list);
            pManager.AddTextParameter("TypeName", "TN", "The param type name", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<string> names = new List<string>();
            List<string> nick_names = new List<string>();
            List<string> descriptions = new List<string>();
            List<string> type_names = new List<string>();
            foreach (IGH_Param param in Params.Input[0].Sources)
            {
                names.Add(param.Name);
                nick_names.Add(param.NickName);
                descriptions.Add(param.Description);
                type_names.Add(param.TypeName);
            }
            DA.SetDataList("Name", names);
            DA.SetDataList("NickName", nick_names);
            DA.SetDataList("Description", descriptions);
            DA.SetDataList("TypeName", type_names);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.GetParamDataIcon;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("096edee0-e780-4fff-95e8-a81f0baf2bf7"); 
    }
}