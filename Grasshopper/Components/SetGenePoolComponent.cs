using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Ant.GrasshopperPlugIn.Attributes;

namespace Ant.GrasshopperPlugIn.Components
{
    public class SetGenePoolComponent : GH_Component
    {
        protected bool _run;

        /// <summary>
        /// Initializes a new instance of the SetGenePoolComponent class.
        /// </summary>
        public SetGenePoolComponent()
          : base("Set Gene Pool", "SGP", "Set a gene pool values", "Ant", "Utilities")
        {
            _run = false;
        }

        public override void CreateAttributes()
        {
            ComponentOneButtonAttributes attr = new ComponentOneButtonAttributes(this, "Set");
            attr.ButtonPressed += () =>
            {
                _run = true;
                ExpireSolution(true);
            };
            m_attributes = attr;
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Gene Pool", "GP", "The Gene Pool to set", GH_ParamAccess.list);
            pManager.AddInterval2DParameter("Domain", "D", "The domain of the gene pool sliders", GH_ParamAccess.item);
            pManager.AddNumberParameter("Values", "v", "The default values od the gene pool sliders", GH_ParamAccess.list);
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
            List<double> pool = new List<double>();
            UVInterval domain = new UVInterval();
            List<double> values = new List<double>();

            if (!DA.GetDataList(0, pool))
                return;
            if (!DA.GetData(1, ref domain))
                return;
            if (!DA.GetDataList(2, values))
                return;

            if (values.Count(v => v < domain.U.Min || v > domain.U.Max) > 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The values must be between the minimum and the maximum");
                _run = false;
                return;
            }

            if (_run)
            {
                IEnumerable<dynamic> pools = Params.Input[0].Sources.Where(s => s.GetType().ToString() == "GalapagosComponents.GalapagosGeneListObject");
                foreach (dynamic gp in pools)
                {
                    gp.Count = values.Count;
                    gp.Minimum = (decimal)domain.U.Min;
                    gp.Maximum = (decimal)domain.U.Max;
                    for (int i = 0; i < values.Count; i++)
                        gp.set_NormalisedValue(i, (decimal)((values[i] - domain.U.Min) / (domain.U.Max - domain.U.Min)));
                    gp.ExpireSolution(true);
                }
                _run = false;
            }
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.SetGenePoolIcon;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("2482af1b-d6c3-490b-8e14-8c6fd78491c7"); 
    }
}