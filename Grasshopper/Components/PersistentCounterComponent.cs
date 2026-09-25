using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ant.GrasshopperPlugIn.Attributes;
using Grasshopper.Kernel;

namespace Ant.GrasshopperPlugIn.Components
{
    public class PersistentCounterComponent : GH_Component
    {

        private int _value;
        private int _startValue;

        private bool _run;


        public PersistentCounterComponent()
          : base("Persistent Counter", "PSC", "Increment the value every time the button is pressed",
              "Ant", "Utilities")
        {

        }
        
        public override void CreateAttributes()
        {
            ComponentOneButtonAttributes attr = new ComponentOneButtonAttributes(this, "Increment");
            attr.ButtonPressed += () =>
            {
                _run = true;
                ExpireSolution(true);
            };
            m_attributes = attr;
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("Start value", "SV", "First number of the series", GH_ParamAccess.item, 0);

            pManager.AddIntegerParameter("Step", "S", "The increment amount", GH_ParamAccess.item, 1);

            pManager.AddBooleanParameter("Reset", "R", "Reset", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;

            pManager.AddBooleanParameter("Increment", "Inc", "Increment", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter("Value", "V", "The resulting value", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int startingValue = 0;
            int step = 0;
            bool reset = false;
            bool increment = false;

            if (!DA.GetData(0, ref startingValue))
                return;
            if (!DA.GetData(1, ref step))
                return;

            DA.GetData(2, ref reset);
            DA.GetData(3, ref increment);



            if (increment)
            {
                _run = true;
            }


            if (_startValue != startingValue)
            {
                _startValue = startingValue;
                _value = startingValue;
            }

            if (_run)
            {
                _value = _value + step;

                _run = false;
            }

            if (reset)
            {
                _value = startingValue;
            }


            DA.SetData(0, _value);
        }



        protected override System.Drawing.Bitmap Icon => Properties.Resources.PersistentCounterIcon;

        public override Guid ComponentGuid => new Guid("856C8300-2A7D-47FD-9EAC-593E99EB9484");

    }
}
