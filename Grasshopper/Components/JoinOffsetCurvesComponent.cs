using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Ant.GrasshopperPlugIn.DataTypes;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.Components
{
    public class JoinOffsetCurvesComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the AddUserDataComponent class.
        /// </summary>
        public JoinOffsetCurvesComponent()
          : base("Join offset curves", "JOC", "Join offset curves", "Ant", "Geometry")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curves", "Cs", "Starting curves", GH_ParamAccess.list);
            pManager.AddPlaneParameter("Plane", "P", "Plane of offset", GH_ParamAccess.item);
            pManager.AddNumberParameter("Offsets", "Os", "Offsets for each curve", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Offsetted", "Os", "The offsetteds curves", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Curve> curves = new List<Curve>();
            Plane plane = Plane.Unset;
            List<double> offsets = new List<double>();

            if (!DA.GetDataList(0, curves))
                return;
            if (!DA.GetData(1, ref plane))
                return;
            if (!DA.GetDataList(2, offsets))
                return;

            if (curves.Count != offsets.Count)
                throw new NotSupportedException("Curves and offsets not of same number");

            List<Curve> offsetteds;
            offsetteds = new List<Curve>();
            for (int i = 0; i < curves.Count; i++)
            {
                if (System.Math.Abs(offsets[i]) > 0.0001)
                {
                    Curve[] buffer;
                    buffer = curves[i].Offset(plane, offsets[i], Rhino.RhinoDoc.ActiveDoc.ModelAbsoluteTolerance, CurveOffsetCornerStyle.Sharp);
                    offsetteds.Add(buffer[0]);
                }
                else
                {
                    offsetteds.Add(curves[i]);
                }
            }

            List<Curve> result = new List<Curve>();
            for (int i = 0; i < offsetteds.Count; i++)
            {
                Curve modifying;
                modifying = offsetteds[i];
                foreach (bool isIni in new bool[] { true, false })
                {
                    List<Curve> touched;
                    List<bool> isOtherIni;
                    Point3d originalP;
                    originalP = isIni ? curves[i].PointAtStart : curves[i].PointAtEnd;
                    touched = new List<Curve>();
                    isOtherIni = new List<bool>();
                    for (int j = 0; j < curves.Count; j++)
                    {
                        if (i != j)
                        {
                            if (curves[j].PointAtStart.DistanceTo(originalP) < 0.1)
                            {
                                touched.Add(offsetteds[j]);
                                isOtherIni.Add(true);
                            }
                            else if (curves[j].PointAtEnd.DistanceTo(originalP) < 0.1)
                            {
                                touched.Add(offsetteds[j]);
                                isOtherIni.Add(false);
                            }
                        }
                    }

                    if (touched.Count > 0)
                    {
                        Curve bestCurve;
                        
                        bestCurve = null;
                        for (int j = 0; j < touched.Count; j++)
                        {
                            Vector3d normalOther, normal;

                            normal = (isIni ? modifying.PointAtStart : modifying.PointAtEnd) -
                                    (isIni ? modifying.PointAtLength(0.1) : modifying.PointAtLength(0.99));

                            normalOther = (isOtherIni[j] ? touched[j].PointAtStart : touched[j].PointAtEnd) -
                                    (isIni ? touched[j].PointAtLength(0.1) : touched[j].PointAtLength(0.99));
                            normal.Unitize();
                            normalOther.Unitize();
                            if (normal.IsParallelTo(normalOther, 0.01)== 0)
                            {
                                Curve[] prova;
                                prova = Curve.CreateFilletCurves(modifying, isIni ? modifying.PointAtNormalizedLength(0.4) : modifying.PointAtNormalizedLength(0.6),
                                                                 touched[j], isOtherIni[j] ? touched[j].PointAtNormalizedLength(0.4) : touched[j].PointAtNormalizedLength(0.6), 0,
                                                                 false, true, false,
                                                                 Rhino.RhinoDoc.ActiveDoc.ModelAbsoluteTolerance,
                                                                 0.01);
                                foreach (Curve c in prova)
                                {
                                    if (bestCurve == null || bestCurve.GetLength() < c.GetLength())
                                    {
                                        if ((isIni && c.PointAtEnd.DistanceTo(modifying.PointAtEnd) < 0.1) ||
                                            ((!isIni) && c.PointAtStart.DistanceTo(modifying.PointAtStart) < 0.1))
                                        {
                                            bestCurve = c;
                                        }
                                    }
                                }
                            }
                        }
                        if (bestCurve!=null) modifying = bestCurve;
                    }
                }
                result.Add(modifying);
            }

            DA.SetDataList(0, result);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.JoinOffsetCurvesIcon;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("7D83246F-053E-4BAA-8622-DE926D257B39");
    }
}