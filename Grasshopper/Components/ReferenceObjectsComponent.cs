using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using Rhino.Geometry;
using Rhino.DocObjects;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Parameters;
using Ant.GrasshopperPlugIn.Attributes;
using Ant.GrasshopperPlugIn.Helpers;

// In order to load the result of this wizard, you will also need to
// add the output bin/ folder of this project to the list of loaded
// folder in Grasshopper.
// You can use the _GrasshopperDeveloperSettings Rhino command for that.

namespace Ant.GrasshopperPlugIn.Components
{
    public class ReferenceObjectsComponent : GH_Component
    {
        /// <summary>
        /// Each implementation of GH_Component must provide a public 
        /// constructor without any arguments.
        /// Category represents the Tab in which the component will appear, 
        /// Subcategory the panel. If you use non-existing tab or panel names, 
        /// new tabs/panels will automatically be created.
        /// </summary>
        public ReferenceObjectsComponent()
          : base("Reference Objects", "RO", "Returns the Rhino objects matching the selection criteria", "Ant", "Reference")
        {
        }

        public override void CreateAttributes()
        {
            ComponentOneButtonAttributes attr = new ComponentOneButtonAttributes(this, "Update");
            attr.ButtonPressed += () =>
            {
                ExpireSolution(true);
            };
            m_attributes = attr;
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            int i;
            i = pManager.AddTextParameter("Name", "N", "The name the entities to select", GH_ParamAccess.item);
            pManager[i].Optional = true;
            i = pManager.AddTextParameter("Layer Name", "LN", "The name of the layer where lives the entities", GH_ParamAccess.item);
            pManager[i].Optional = true;
            i = pManager.AddIntegerParameter("Object Type", "OT", "The type of the object of the entities", GH_ParamAccess.item);
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
            pManager.AddGeometryParameter("Geometries", "G", "The selected geometries", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string name = "";
            string layername = "";
            int type = 0;
            Color color = Color.Empty;

            bool is_name_provided = DA.GetData(0, ref name);
            bool is_layer_provided = DA.GetData(1, ref layername);
            bool is_type_provided = DA.GetData(2, ref type);
            bool is_color_provided = DA.GetData(3, ref color);

            if (!is_name_provided && !is_layer_provided && !is_type_provided && !is_color_provided)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "At least one condition must be provided");
                return;
            }

            Rhino.RhinoDoc doc = Rhino.RhinoDoc.ActiveDoc;
            List<IGH_GeometricGoo> geometries = new List<IGH_GeometricGoo>();

            if (is_name_provided && name != "")
            {
                if (!name.Contains('*'))
                    geometries.AddRange(doc.Objects.Where(g => doc.Objects.Find(g.Id).Name == name).Select(o => GeometryReference.Get(o)));
                else if (name.EndsWith("*"))
                    geometries.AddRange(doc.Objects.Where(g => doc.Objects.Find(g.Id).Name.
                        StartsWith(name.Substring(0, name.Length - 1))).Select(o => GeometryReference.Get(o)));
                else if (name.StartsWith("*"))
                    geometries.AddRange(doc.Objects.Where(g => doc.Objects.Find(g.Id).Name.
                        EndsWith(name.Substring(1))).Select(o => GeometryReference.Get(o)));
                else
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid wildcard on geometry name");
            }

            if (is_layer_provided && layername != "")
            {
                IEnumerable<int> layers = null;
                if (!layername.Contains('*'))
                    layers = doc.Layers.Where(l => l.FullPath == layername).Select(l => l.Index);
                else if (layername.EndsWith("*"))
                    layers = doc.Layers.Where(l => l.FullPath.StartsWith(layername.Substring(0, layername.Length - 1))).Select(l => l.Index);
                else if (layername.StartsWith("*"))
                    layers = doc.Layers.Where(l => l.FullPath.EndsWith(layername.Substring(1))).Select(l => l.Index);
                else
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid wildcard on layer name");

                if (geometries.Count > 0)
                {
                    geometries = geometries.Where(o => layers.Contains(doc.Objects.Find(o.ReferenceID).Attributes.LayerIndex)).ToList();
                }
                else
                {
                    geometries.AddRange(doc.Objects.Where(o => o.ObjectType != ObjectType.InstanceReference && 
                        layers.Contains(doc.Objects.Find(o.Id).Attributes.LayerIndex)).Select(o => GeometryReference.Get(o)));

                    geometries.AddRange(doc.Objects.Where(o => o.ObjectType == ObjectType.InstanceReference &&
                        o.GetSubObjects().Any(so => layers.Contains(doc.Objects.Find(so.Id).Attributes.LayerIndex))).
                        SelectMany(io => GeometryReference.GetSubByLayerIndex((InstanceObject)io, layers.ToArray())));

                }
            }

            if (type != 0)
            {
                if (geometries.Count > 0)
                {
                    geometries = geometries.Where(g => doc.Objects.Find(g.ReferenceID).ObjectType == (ObjectType)type).ToList();
                }
                else
                {
                     geometries.AddRange(doc.Objects.Where(o => o.ObjectType == (ObjectType)type).Select(o => GeometryReference.Get(o)));
                }
            }

            if (is_color_provided)
            {
                if (geometries.Count > 0)
                {
                    geometries = geometries.Where(g =>
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
                    geometries.AddRange(doc.Objects.Where(obj =>
                    {
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
                    }).Select(o => GeometryReference.Get(o)));
                }
            }

            Message = string.Format("{0} objects", geometries.Count);

            DA.SetDataList(0, geometries);
        }

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// </summary>
        protected override Bitmap Icon => Properties.Resources.ReferenceObjectsIcon;

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("0bf26727-f76b-4cd9-a24c-fdaf3cbbd45e"); 
    }
}
