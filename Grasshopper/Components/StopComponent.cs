using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using GH_IO.Serialization;
using Rhino.Geometry;
using Ant.GrasshopperPlugIn.Attributes;

namespace Ant.GrasshopperPlugIn.Components
{
    public class StopComponent : GH_Component
    {
        protected bool _go = false;

        /// <summary>
        /// Initializes a new instance of the StopComponent class.
        /// </summary>
        public StopComponent()
          : base("Stop", "Ant", "Stop the execution", "Ant", "Workflow")
        {
            _go = false;
        }

        public override void CreateAttributes()
        {
            RadioButtonAttributes attr = new RadioButtonAttributes(this, OnRadioButtonClicked, 0, new[] { "Stop", "Go" });
            m_attributes = attr;
        }

        protected void OnRadioButtonClicked(RadioButtonAttributes attr, int i)
        {
            if (i == 0)
                _go = false;
            else
                _go = true;
            ExpireSolution(true);
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

            if (_go)
                DA.SetData(0, data);
        }

        public override bool Read(GH_IReader reader)
        {
            bool success = base.Read(reader);

            _go = reader.GetBoolean("Go");

            return success;
        }

        public override bool Write(GH_IWriter writer)
        {
            bool success = base.Write(writer);

            writer.SetBoolean("Go", _go);

            return success;
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.StopIcon;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("0de21576-cebd-4937-9af8-fa98ced2ca80"); 
    }
}