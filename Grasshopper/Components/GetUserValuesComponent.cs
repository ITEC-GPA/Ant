using System;
using System.Linq;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Rhino.Geometry;
using Ant.GrasshopperPlugIn.Helpers;

namespace Ant.GrasshopperPlugIn.Components
{
    public class GetUserValuesComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the GetUserTextComponent class.
        /// </summary>
        public GetUserValuesComponent()
          : base("Get User Values", "GUV", "Retrieve the user values attached to the geometry and key", "Ant", "Data")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "G", "The geometry where to extract the user texts", GH_ParamAccess.item);
            pManager.AddTextParameter("Keys", "K", "The keys of the geometry user texts", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Values", "V", "The user texts of the geometry", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GeometryBase geometry = null;
            List<string> keys = new List<string>();

            if (!DA.GetData(0, ref geometry))
                return;
            if (!DA.GetDataList(1, keys))
                return;

            List<string> geom_keys = new List<string>();
            foreach (var kv in geometry.UserDictionary)
            {
                if (!geom_keys.Contains(kv.Key))
                    geom_keys.Add(kv.Key);
            }

            IEnumerable<IGH_Param> keys_vallists = Params.Input[1].Sources.Where(s => s.GetType() == typeof(GH_ValueList));
            foreach (GH_ValueList vallist in keys_vallists)
            {
                if (vallist.ListMode != GH_ValueListMode.CheckList)
                    vallist.ListMode = GH_ValueListMode.CheckList;

                ComponentHelper.SetValueList(vallist, geom_keys);
            }

            List<object> values = new List<object>();

            foreach (string key in keys)
            {
                if (geometry.UserDictionary.ContainsKey(key))
                    values.Add(geometry.UserDictionary[key]);
            }
            
            DA.SetDataList(0, values);
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        /// <summary>
        /// Provides an Icon for the component.
        ///// </summary>        
        protected override System.Drawing.Bitmap Icon => Properties.Resources.GetUserValuesIcon;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("c7fbe1ed-b517-4894-89ef-e862c94374c8");
    }
}
