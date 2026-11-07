using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Ant.GrasshopperPlugIn.Helpers;

namespace Ant.GrasshopperPlugIn.Components
{
    public class FilterUserDataComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the FilterUserDataComponent class.
        /// </summary>
        public FilterUserDataComponent()
          : base("Filter User Data", "FD", "Filter the given geometries by User Data", "Ant", "Query")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometries", "G", "The geometries to filter", GH_ParamAccess.list);
            pManager.AddTextParameter("Keys", "K", "The keys of the geometry user texts", GH_ParamAccess.list);
            pManager.AddGenericParameter("Values", "V", "The user texts of the geometry", GH_ParamAccess.list);
            pManager[2].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGeometryParameter("Match", "G", "The geometries matching the filter", GH_ParamAccess.list);
            pManager.AddGeometryParameter("Not Match", "G", "The other geometries", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<GeometryBase> input_geoms = new List<GeometryBase>();
            List<string> input_keys = new List<string>();
            List<object> input_vals = new List<object>();
            if (!DA.GetDataList(0, input_geoms))
                return;
            if (!DA.GetDataList(1, input_keys))
                return;
            DA.GetDataList(2, input_vals);
            List<string> errors = new List<string>();

            try
            {
                List<string> geom_keys = new List<string>();
                List<object> geom_vals = new List<object>();
                foreach (GeometryBase geometry in input_geoms)
                {
                    if (geometry == null || geometry.UserDictionary == null)
                        continue;
                    foreach (var kv in geometry.UserDictionary)
                    {
                        if (!geom_keys.Contains(kv.Key))
                            geom_keys.Add(kv.Key);
                        if (!geom_vals.Contains(kv.Value))
                            geom_vals.Add(kv.Value);
                    }
                }

                IEnumerable<IGH_Param> keys_vallists = Params.Input[1].Sources.Where(s => s.GetType() == typeof(GH_ValueList));
                foreach (GH_ValueList vallist in keys_vallists)
                {
                    if (vallist.ListMode != GH_ValueListMode.CheckList)
                        vallist.ListMode = GH_ValueListMode.CheckList;

                    ComponentHelper.SetValueList(vallist, geom_keys);
                }

                IEnumerable<IGH_Param> vals_vallists = Params.Input[2].Sources.Where(s => s.GetType() == typeof(GH_ValueList));
                foreach (GH_ValueList vallist in vals_vallists)
                {
                    if (vallist.ListMode != GH_ValueListMode.CheckList)
                        vallist.ListMode = GH_ValueListMode.CheckList;

                    ComponentHelper.SetValueList(vallist, geom_vals.Select(i => i.ToString()));
                }

                List<GeometryBase> output_geoms = new List<GeometryBase>();
                if (input_keys.Count > 0)
                {
                    foreach (GeometryBase geom in input_geoms)
                    {
                        bool match = true;
                        foreach (string key in input_keys)
                        {
                            if (!geom.UserDictionary.ContainsKey(key))
                                match = false;
                        }
                        if (match)
                            output_geoms.Add(geom);
                    }
                }

                if (input_vals.Count > 0)
                {
                    if (output_geoms.Count > 0)
                    {
                        input_geoms = new List<GeometryBase>(output_geoms);
                        output_geoms.Clear();
                    }

                    foreach (GeometryBase geom in input_geoms)
                    {
                        bool match = true;
                        foreach (string key in input_keys)
                        {
                            if (input_vals.Where(o =>
                            {
                                object v = o.GetType().GetProperty("Value").GetValue(o, null);
                                return geom.UserDictionary.ContainsKey(key) && geom.UserDictionary[key].Equals(v);
                            }).Count() == 0)
                            {
                                match = false;
                                break;
                            }
                        }
                        if (match)
                            output_geoms.Add(geom);
                    }
                }

                List<GeometryBase> match_geoms = new List<GeometryBase>();
                List<GeometryBase> not_match_geoms = new List<GeometryBase>();
                foreach (GeometryBase geom in input_geoms)
                {
                    if (output_geoms.Contains(geom))
                        match_geoms.Add(geom.Duplicate());
                    else
                        not_match_geoms.Add(geom.Duplicate());
                }

                DA.SetDataList(0, match_geoms);
                DA.SetDataList(1, not_match_geoms);
            }
            catch (Exception e)
            {
                errors.Add(string.Format("{0} {1}", e.Message, e.StackTrace));
            }

            foreach (string error in errors)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.FilterUserDataIcon;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("f8f259da-288a-422f-ab3d-557912052bd1"); 
    }
}