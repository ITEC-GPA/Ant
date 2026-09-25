using System;
using System.Collections.Generic;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.DocObjects;
using Rhino.Geometry;
using Ant.GrasshopperPlugIn.Attributes;

namespace Ant.GrasshopperPlugIn.Components
{    
    public class BakeUserDataComponent : GH_Component
    {
        protected bool _bake;
        protected Guid _objectId;

        /// <summary>
        /// Initializes a new instance of the BakeUserDataComponent class.
        /// </summary>
        public BakeUserDataComponent()
          : base("Bake User Data", "BUD", "Bake the geometry with the user data or update the geometry user data", "Ant", "Data")
        {
            _objectId = Guid.Empty;
        }

        public override void CreateAttributes()
        {
            ComponentOneButtonAttributes attr = new ComponentOneButtonAttributes(this, "Bake");
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
            pManager.AddGeometryParameter("Geometry", "G", "The geometry", GH_ParamAccess.item);
            pManager.AddTextParameter("Layer", "L", "The layer name where to bake the geometry if it is unexisting. " +
                "The layer is created if it not exists in the document.", GH_ParamAccess.item, "");
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
            IGH_GeometricGoo geometry = null;
            string layer = "";

            if (!DA.GetData(0, ref geometry))
                return;

            if (geometry.IsReferencedGeometry)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "This component doesn't works with referenced geometies. Use the Add User Data component instead.");
                return;
            }

            if (_bake)
            {
                Rhino.RhinoDoc doc = Rhino.RhinoDoc.ActiveDoc;
                RhinoObject obj = null;
                if (_objectId != Guid.Empty)
                    obj = doc.Objects.Find(_objectId);

                if (obj == null)
                {
                    ObjectAttributes attr = new ObjectAttributes();
                    if (layer != "")
                    {
                        int idx = doc.Layers.FindByFullPath(layer, -1);

                        if (idx == -1)
                        {
                            Layer new_layer = new Layer
                            {
                                Name = layer
                            };
                            doc.Layers.Add(new_layer);
                        }
                        else
                        {
                            attr.LayerIndex = idx;
                        }
                    }

                    GeometryBase gb = GH_Convert.ToGeometryBase(geometry);
                    if (gb != null)
                        _objectId = doc.Objects.Add(gb, attr);
                }
                else
                {
                    GeometryBase gb = GH_Convert.ToGeometryBase(geometry);
                    obj.Geometry.UserDictionary.Clear();
                    obj.Geometry.UserDictionary.AddContentsFrom(gb.UserDictionary);
                    obj.CommitChanges();
                }

                _bake = false;
            }
        }

        public override bool Read(GH_IReader reader)
        {
            bool success = base.Read(reader);

            _objectId = reader.GetGuid("ObjectId");

            return success;
        }

        public override bool Write(GH_IWriter writer)
        {
            bool success = base.Write(writer);

            writer.SetGuid("ObjectId", _objectId);

            return success;
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.BakeUserDataIcon;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid=>  new Guid("eff6c2cd-ee78-45e1-9d5c-65832912621f"); 
    }
}