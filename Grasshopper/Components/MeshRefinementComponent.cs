using System;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Ant.GrasshopperPlugIn.DataTypes;
using Grasshopper.Kernel.Types;
using System.Collections.Generic;
using System.Linq;

namespace Ant.GrasshopperPlugIn.Components
{
    public class MeshRefinementComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MeshRefinementComponent class.
        /// </summary>
        public MeshRefinementComponent()
          : base("Mesh Refinement", "MR", "Define a refinement", "Ant", "Mesh")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "G", "The geometry to which to attach the data", GH_ParamAccess.item);
            pManager.AddNumberParameter("Size", "S", "The size of the mesh", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Refinement", "MR", "The mesh refinement to give to the mesh component", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            //Point3d point = Point3d.Unset;
            //if (!DA.GetData(0, ref point))
            //    return;
            IGH_GeometricGoo geometry = null;
            if (!DA.GetData(0, ref geometry))
                return;
            double size = 0;
            if (!DA.GetData(1, ref size))
                return;

            if (geometry is GH_Point point)
            {
                DA.SetDataList(0, new List<MeshPointRefinement>() { new MeshPointRefinement(point.Value, size) });
                return;
            }
            else if (geometry is LineCurve line)
            {
                DA.SetDataList(0, new List<MeshLineRefinement>() { new MeshLineRefinement(line.Line, size) });
                return;
            }
            else if (geometry is GH_Curve curve)
            {
                if (curve.Value.TryGetPolyline(out Polyline polycurvegh))
                {
                    List<MeshLineRefinement> list = (polycurvegh.GetSegments().Select(linegh => new MeshLineRefinement(linegh, size))).ToList();
                    DA.SetDataList(0, list);
                    return;
                }
                else
                {
                    var pppl = MeshComponent.ToPolylineCurve(curve.Value, size / 1000, size);
                    if (pppl != null)
                    {
                        List<MeshLineRefinement> list = (pppl.ToPolyline().GetSegments().Select(linegh => new MeshLineRefinement(linegh, size))).ToList();
                        DA.SetDataList(0, list);
                        return;
                    }
                }
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Fail to convert planar curve to polyline");

            }
            else if (geometry is LineCurve gh_line)
            {
                DA.SetDataList(0, new List<MeshLineRefinement>() { new MeshLineRefinement(gh_line.Line, size) });
                return;
            }
            else if (geometry is Polyline polyline)
            {
                List<MeshLineRefinement> list = new List<MeshLineRefinement>();
                Line[] listOfLine = polyline.GetSegments();
                list.AddRange(listOfLine.Select(l => new MeshLineRefinement(l, size)));
                DA.SetDataList(0, list);
                return;
            }
            else if (geometry is PolyCurve polycurve)
            {
				List<MeshLineRefinement> list = new List<MeshLineRefinement>();
				polycurve.TryGetPolyline(out Polyline polylineOut);
				list.AddRange(polylineOut.GetSegments().Select(l => new MeshLineRefinement(l, size)));
				DA.SetDataList(0, list);
				return;
			}
            else if (geometry is PolylineCurve polylinecurve)
            {
				List<MeshLineRefinement> list = new List<MeshLineRefinement>();
				polylinecurve.TryGetPolyline(out Polyline polylineOut);
				list.AddRange(polylineOut.GetSegments().Select(l => new MeshLineRefinement(l, size)));
				DA.SetDataList(0, list);
				return;
			}
            /*else if (geometry is Brep surface)
            {
                Curve cv = surface.Loops[0].To3dCurve();
                cv.TryGetPolyline(out Polyline pline, out double[] parameters);

                DA.SetData(0, new MeshPolylineRefinement(pline, size));
                return;
            }*/
            else if (geometry is GH_Brep brep)
            {
                Curve cv = brep.Value.Loops[0].To3dCurve();
                cv.TryGetPolyline(out Polyline pline, out double[] parameters);

                DA.SetDataList(0, new List<MeshPolylineRefinement>() { new MeshPolylineRefinement(pline, size) });
                return;
            }
            else if (geometry is GH_Surface surface)
            {
                DA.SetDataList(0, new List<MeshSurfaceRefinement>() { new MeshSurfaceRefinement(surface.Value, size) });
                return;
            }

            //DA.SetData(0, new MeshRefinement(point, size));
            // Errore
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Geometry not supported");
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.MeshRefinementIcon;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("8de166c1-85c1-4026-8d3f-0a3c6da70bd3");


        public override GH_Exposure Exposure => GH_Exposure.secondary;
    }
}