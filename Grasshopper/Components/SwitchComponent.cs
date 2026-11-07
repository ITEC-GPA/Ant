using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

// See:
// https://www.grasshopper3d.com/forum/topics/gha-developers-implementing-variable-parameters


namespace Ant.GrasshopperPlugIn.Components
{
    public class SwitchComponent : GH_Component, IGH_VariableParameterComponent
    {
        /// <summary>
        /// Initializes a new instance of the SwitchComponent class.
        /// </summary>
        public SwitchComponent()
          : base("Switch", "SW", "Route the selected input tree to the output", "Ant", "Workflow")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("Selected", "S", "The input to send to the output", GH_ParamAccess.item);
            pManager.AddGenericParameter("Input 1", "I1", "The input", GH_ParamAccess.tree);
            pManager[0].Optional = true;
            pManager.AddGenericParameter("Input 2", "I2", "The input", GH_ParamAccess.tree);
            pManager[1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Output", "O", "The output", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int index = 0;
            if (!DA.GetData(0, ref index))
                return;

            foreach (IGH_Param src in Params.Input[0].Sources)
            {
                if (src is GH_NumberSlider slider)
                {
                    slider.Slider.Minimum = 1;
                    slider.Slider.Maximum = Params.Input.Count - 1;
                }
            }

            if (index > 0 && index < Params.Input.Count)
            {
                GH_Structure<IGH_Goo> data = null;
                DA.GetDataTree(index, out data);
                DA.SetDataTree(0, data);
            }
        }

        public bool CanInsertParameter(GH_ParameterSide side, int index)
        {
            if (side == GH_ParameterSide.Input && index > 0)
                return true;
            return false;
        }

        public bool CanRemoveParameter(GH_ParameterSide side, int index)
        {
            if (side == GH_ParameterSide.Input && Params.Input.Count >= 3 && index > 0)
                return true;
            return false;
        }

        public IGH_Param CreateParameter(GH_ParameterSide side, int index)
        {
            if (side == GH_ParameterSide.Input)
            {
                Param_GenericObject new_param = new Param_GenericObject()
                {
                    Name = string.Format("Input {0}", index),
                    NickName = string.Format("I{0}", index),
                    Description = "The input",
                    Access = GH_ParamAccess.tree,
                    Optional = true
                };
                return new_param;
            }
            return null;
        }

        public bool DestroyParameter(GH_ParameterSide side, int index)
        {
            return true;
        }

        public void VariableParameterMaintenance()
        {
            for (int i = 1; i < Params.Input.Count; i++)
            {
                Params.Input[i].Name = string.Format("I{0}", i);
                Params.Input[i].NickName = string.Format("I{0}", i);
            }
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.SwitchIcon;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("7a8f906b-ee93-4c6f-8f8f-aecc8d0d250b"); 
    }
}