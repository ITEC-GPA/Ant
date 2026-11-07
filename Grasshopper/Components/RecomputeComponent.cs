using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Ant.GrasshopperPlugIn.Attributes;

namespace Ant.GrasshopperPlugIn.Components
{
    public class RecomputeComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the RecomputeComponent class.
        /// </summary>
        public RecomputeComponent()
          : base("Recompute", "R", "Force recomputation from this component", "Ant", "Workflow")
        {
        }

        public override void CreateAttributes()
        {
            ComponentOneButtonAttributes attr = new ComponentOneButtonAttributes(this, "Recompute");
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
            pManager.AddGenericParameter("Input", "I", "The input", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Output", "O", "The output", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            IGH_Goo data = null;

            if (!DA.GetData(0, ref data))
                return;

            DA.SetData(0, data);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.RecomputeIcon;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("39250fbd-01c6-4443-b411-1194ced304dc"); 
    }
}