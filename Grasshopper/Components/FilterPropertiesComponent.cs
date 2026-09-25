using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Rhino.Geometry;
using Rhino.DocObjects;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

namespace Ant.GrasshopperPlugIn.Components
{
    public class FilterPropertiesComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the FilterPropertiesComponent class.
        /// </summary>
        public FilterPropertiesComponent()
          : base("Filter Properties", "FP", "Filter the given geometries by Properties value", "Ant", "Query")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometries", "G", "The geometries to filter", GH_ParamAccess.list);
            int i = pManager.AddTextParameter("Name", "N", "The geometry name", GH_ParamAccess.item);
            pManager[i].Optional = true;
            i = pManager.AddTextParameter("Layer Name", "LN", "The geometry layer", GH_ParamAccess.item);
            pManager[i].Optional = true;
            i = pManager.AddIntegerParameter("Object Type", "OT", "The geometry type", GH_ParamAccess.item);
            pManager[i].Optional = true;
            Param_Integer type = (Param_Integer)pManager[i];
            type.AddNamedValue("Brep", (int)ObjectType.Brep);
            type.AddNamedValue("Curve", (int)ObjectType.Curve);
            type.AddNamedValue("Mesh", (int)ObjectType.Mesh);
            type.AddNamedValue("Point", (int)ObjectType.Point);
            type.AddNamedValue("Surface", (int)ObjectType.Surface);
            i = pManager.AddColourParameter("Color", "C", "The geometry color", GH_ParamAccess.item);
            pManager[i].Optional = true;
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
            List<IGH_GeometricGoo> input_geoms = new List<IGH_GeometricGoo>();
            string name = "";
            string layername = "";
            int type = 0;
            Color color = Color.Empty;

            if (!DA.GetDataList(0, input_geoms))
                return;
            bool is_name_provided = DA.GetData(1, ref name);
            bool is_layer_provided = DA.GetData(2, ref layername);
            bool is_type_provided = DA.GetData(3, ref type);
            bool is_color_provided = DA.GetData(4, ref color);

            if (!is_name_provided && !is_layer_provided && !is_type_provided && !is_color_provided)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "At least one condition must be provided");
                return;
            }

            Rhino.RhinoDoc doc = Rhino.RhinoDoc.ActiveDoc;
            List<IGH_GeometricGoo> output_geoms = new List<IGH_GeometricGoo>();
            
            if (is_name_provided && name != "")
            {
                if (!name.Contains('*'))
                    output_geoms = input_geoms.Where(g => doc.Objects.Find(g.ReferenceID).Name == name).ToList();
                else if (name.EndsWith("*"))
                    output_geoms = input_geoms.Where(g => doc.Objects.Find(g.ReferenceID).Name.
                        StartsWith(name.Substring(0, name.Length - 1))).ToList();
                else if (name.StartsWith("*"))
                    output_geoms = input_geoms.Where(g => doc.Objects.Find(g.ReferenceID).Name.
                        EndsWith(name.Substring(1))).ToList();
                else
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid wildcard on geometry name");
            }

            if (is_layer_provided && layername != "")
            {
                IEnumerable<Layer> layers = null;
                if (!layername.Contains('*'))
                    layers = doc.Layers.Where(l => l.FullPath == layername);
                else if (layername.EndsWith("*"))
                    layers = doc.Layers.Where(l => l.FullPath.StartsWith(layername.Substring(0, layername.Length - 1)));
                else if (layername.StartsWith("*"))
                    layers = doc.Layers.Where(l => l.FullPath.EndsWith(layername.Substring(1)));
                else
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid wildcard on layer name");

                if (output_geoms.Count > 0)
                {
                    output_geoms = output_geoms.Where(g => layers.
                        Where(l => l.Index == doc.Objects.Find(g.ReferenceID).Attributes.LayerIndex).Count() > 0).ToList();
                }
                {
                    output_geoms.AddRange(input_geoms.Where(g => layers.
                        Where(l => l.Index == doc.Objects.Find(g.ReferenceID).Attributes.LayerIndex).Count() > 0));
                }
            }

            if (is_type_provided)
            {
                if (output_geoms.Count > 0)
                {
                    output_geoms = output_geoms.Where(g => doc.Objects.Find(g.ReferenceID).ObjectType == (ObjectType)type).ToList();
                }
                else
                {
                    output_geoms.AddRange(input_geoms.Where(g => doc.Objects.Find(g.ReferenceID).ObjectType == (ObjectType)type));
                }
            }

            if (is_color_provided)
            {
                if (output_geoms.Count > 0)
                {
                    output_geoms = output_geoms.Where(g =>
                    {
                        RhinoObject obj = doc.Objects.Find(g.ReferenceID);
                        switch (obj.Attributes.ColorSource)
                        {
                            case ObjectColorSource.ColorFromLayer:
                                if (doc.Layers[obj.Attributes.LayerIndex].Color == color)
                                    return true;
                                return false;
                            case ObjectColorSource.ColorFromMaterial:
                                if (doc.Materials[obj.Attributes.MaterialIndex].AmbientColor == color ||
                                    doc.Materials[obj.Attributes.MaterialIndex].DiffuseColor == color ||
                                    doc.Materials[obj.Attributes.MaterialIndex].EmissionColor == color ||
                                    doc.Materials[obj.Attributes.MaterialIndex].ReflectionColor == color ||
                                    doc.Materials[obj.Attributes.MaterialIndex].SpecularColor == color ||
                                    doc.Materials[obj.Attributes.MaterialIndex].TransparentColor == color)
                                    return true;
                                return false;
                            case ObjectColorSource.ColorFromObject:
                                if (obj.Attributes.ObjectColor == color)
                                    return true;
                                return false;
                        }
                        return false;
                    }).ToList();
                }
                else
                {
                    output_geoms.AddRange(input_geoms.Where(g =>
                    {
                        RhinoObject obj = doc.Objects.Find(g.ReferenceID);
                        switch (obj.Attributes.ColorSource)
                        {
                            case ObjectColorSource.ColorFromLayer:
                                if (doc.Layers[obj.Attributes.LayerIndex].Color == color)
                                    return true;
                                return false;
                            case ObjectColorSource.ColorFromMaterial:
                                if (doc.Materials[obj.Attributes.MaterialIndex].AmbientColor == color ||
                                    doc.Materials[obj.Attributes.MaterialIndex].DiffuseColor == color ||
                                    doc.Materials[obj.Attributes.MaterialIndex].EmissionColor == color ||
                                    doc.Materials[obj.Attributes.MaterialIndex].ReflectionColor == color ||
                                    doc.Materials[obj.Attributes.MaterialIndex].SpecularColor == color ||
                                    doc.Materials[obj.Attributes.MaterialIndex].TransparentColor == color)
                                    return true;
                                return false;
                            case ObjectColorSource.ColorFromObject:
                                if (obj.Attributes.ObjectColor == color)
                                    return true;
                                return false;
                        }
                        return false;
                    }));
                }
            }

            List<IGH_GeometricGoo> match_geoms = new List<IGH_GeometricGoo>();
            List<IGH_GeometricGoo> not_match_geoms = new List<IGH_GeometricGoo>();
            foreach (IGH_GeometricGoo geom in input_geoms)
            {
                if (output_geoms.Contains(geom))
                    match_geoms.Add(geom.DuplicateGeometry());
                else
                    not_match_geoms.Add(geom.DuplicateGeometry());
            }

            DA.SetDataList(0, match_geoms);
            DA.SetDataList(1, not_match_geoms);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override Bitmap Icon => Properties.Resources.FilterPropertiesIcon;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("1daca6ca-833f-48f3-a779-d337e7393c1f"); 
    }
}