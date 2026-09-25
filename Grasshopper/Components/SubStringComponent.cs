using System;
using Grasshopper.Kernel;

namespace Ant.GrasshopperPlugIn.Components
{
    public class SubStringComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the SubStringComponent class.
        /// </summary>
        public SubStringComponent()
          : base("SubString", "SS", "Extracts the characters from a string until the first occurrence of the delimiter",
              "Ant", "Utilities")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Text", "T", "The original text string", GH_ParamAccess.item);
            pManager.AddTextParameter("Delimiter", "D", "The delimiter of the substring to extract", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Result", "R", "The resulting substring", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string text = "";
            string delimiter = "";
            if (!DA.GetData(0, ref text))
                return;
            if (!DA.GetData(1, ref delimiter))
                return;

            string result = text;
            int del_pos = text.IndexOf(delimiter);
            if (del_pos >= 0)
                result = text.Substring(0, del_pos);

            DA.SetData(0, result);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.SubStringIcon;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("a15f6534-84bf-41a1-a771-bafe1c6ba4b8"); 
    }
}