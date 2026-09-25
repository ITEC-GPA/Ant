using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.Components
{
    public class AutoOrientComponent : GH_Component
    {
        protected enum Orientation
        {
            Vertical,
            Horizontal
        }

        /// <summary>
        /// Initializes a new instance of the AutoOrientComponent class.
        /// </summary>
        public AutoOrientComponent()
          : base("Auto Orient", "AO", "Automatically orient a geometry", "Ant", "Utilities")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "G", "The geometry to orient", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Orientation", "O", "The orientation", GH_ParamAccess.item, 0);
            Param_Integer orient = (Param_Integer)pManager[1];
            orient.AddNamedValue(Enum.GetName(typeof(Orientation), Orientation.Vertical), (int)Orientation.Vertical);
            orient.AddNamedValue(Enum.GetName(typeof(Orientation), Orientation.Horizontal), (int)Orientation.Horizontal);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "G", "The oriented geometry", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GeometryBase geomIn = null;
            int or = 0;

            if (!DA.GetData(0, ref geomIn))
                return;
            if (!DA.GetData(1, ref or))
                return;

            Orientation orientation = (Orientation)or;
            GeometryBase geomOut = geomIn.Duplicate();

            BoundingBox bb = geomOut.GetBoundingBox(true);

            double width = bb.Max.X - bb.Min.X;
            double height = bb.Max.Y - bb.Min.Y;
            if (orientation == Orientation.Vertical && width > height || orientation == Orientation.Horizontal && height > width)
            {
                geomOut.Rotate(Math.PI / 2.0, Vector3d.ZAxis, bb.Center);
                width = bb.Max.X - bb.Min.X;
                height = bb.Max.Y - bb.Min.Y;
            }

            double angle = 0.1;
            double prev_objcv = 0;
            while (Math.Abs(angle) > 0.001)
            {
                geomOut.Rotate(angle, Vector3d.ZAxis, bb.Center);
                bb = geomOut.GetBoundingBox(true);
                double new_width = bb.Max.X - bb.Min.X;
                double new_height = bb.Max.Y - bb.Min.Y;

                if (prev_objcv == 0)
                {
                    if (orientation == Orientation.Vertical && new_width > width || 
                        orientation == Orientation.Horizontal && new_height > height)
                    {
                        angle = -angle;
                        geomOut.Rotate(angle, Vector3d.ZAxis, bb.Center);
                    }
                    prev_objcv = orientation == Orientation.Vertical ? width : height;
                    continue;
                }

                if (orientation == Orientation.Vertical && new_width > prev_objcv || 
                    orientation == Orientation.Horizontal && new_height > prev_objcv)
                    angle = -(angle / 2);
                prev_objcv = orientation == Orientation.Vertical ? new_width : new_height;
            }

            DA.SetData(0, geomOut);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.AutoOrientIcon;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("07cfa9b7-51b0-4cf6-adb5-80f2fa7bd9d2"); 
    }
}