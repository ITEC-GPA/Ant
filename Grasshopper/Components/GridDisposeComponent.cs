using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.Components
{
    public class GridDisposeComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the GridDisposeComponent class.
        /// </summary>
        public GridDisposeComponent()
          : base("Grid Dispose", "GD", "Dispose the given geometries to a grid", "Ant", "Utilities")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometries", "G", "The geometry to dispose in a grid", GH_ParamAccess.list);
            int i;
            i = pManager.AddIntegerParameter("Columns", "C", "The number of the column of the grid to generate", GH_ParamAccess.item);
            pManager[i].Optional = true;
            i = pManager.AddIntegerParameter("Rows", "R", "The number of the rows of the grid to generate", GH_ParamAccess.item);
            pManager[i].Optional = true;
            pManager.AddPointParameter("Point", "P", "The lower left corner of the grid to generate", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometries", "G", "The geometry to disposed in the grid", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<IGH_GeometricGoo> input_geoms = new List<IGH_GeometricGoo>();
            int max_cols = 0;
            int max_rows = 0;
            Point3d insert = Point3d.Unset;

            if (!DA.GetDataList(0, input_geoms))
                return;
            bool is_maxcols_provided = DA.GetData(1, ref max_cols);
            bool is_maxrows_provided = DA.GetData(2, ref max_rows);
            if (!DA.GetData(3, ref insert))
                return;

            if (!is_maxcols_provided && !is_maxrows_provided)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Please provide the maximum number of the column or the maximum number of the rows of the grid");
                return;
            }

            if (max_cols > 0 && max_rows > 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Please provide only the maximum number of the columns or of the rows. Not both.");
                return;
            }

            Vector2d cell_size = new Vector2d(0, 0);
            foreach (IGH_GeometricGoo geom in input_geoms)
            {
                double cx = geom.Boundingbox.Max.X - geom.Boundingbox.Min.X;
                double cy = geom.Boundingbox.Max.Y - geom.Boundingbox.Min.Y;
                cell_size.X = Math.Max(cell_size.X, cx);
                cell_size.Y = Math.Max(cell_size.Y, cy);
            }
            cell_size.X *= 1.1;
            cell_size.Y *= 1.1;

            List<IGH_GeometricGoo> output_geoms = new List<IGH_GeometricGoo>();
            int cur_x = 0, cur_y = 0;
            foreach (IGH_GeometricGoo geom in input_geoms)
            {
                Point3d cell_center = new Point3d(insert.X + (cur_x * cell_size.X) + (cell_size.X / 2), 
                    insert.Y + (cur_y * cell_size.Y) + (cell_size.Y / 2), 0);
                Transform t = Transform.Translation(cell_center - geom.Boundingbox.Center);
                IGH_GeometricGoo new_geom = geom.DuplicateGeometry();
                new_geom.Transform(t);
                output_geoms.Add(new_geom);

                if (max_cols > 0)
                {
                    cur_x++;
                    if (cur_x >= max_cols)
                    {
                        cur_x = 0;
                        cur_y++;
                    }
                }
                if (max_rows > 0)
                {
                    cur_y++;
                    if (cur_y >= max_rows)
                    {
                        cur_y = 0;
                        cur_x++;
                    }
                }
            }

            DA.SetDataList(0, output_geoms);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.GridDisposeIcon;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("b0e93379-ed3a-40e9-99d9-dd8f83d2baeb"); 
    }
}