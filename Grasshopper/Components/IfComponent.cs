using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.Components
{
    public class IfComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the IfComponent class.
        /// </summary>
        public IfComponent()
          : base("If", "IF", "Description", "Ant", "Workflow")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Value 1", "V1", "The first expression value", GH_ParamAccess.item);
            pManager.AddGenericParameter("Value 2", "V2", "The second expression value", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Condition", "C", "The condition", GH_ParamAccess.item);
            Param_Integer condition = (Param_Integer)pManager[2];
            condition.AddNamedValue("<", 0);
            condition.AddNamedValue("<=", 1);
            condition.AddNamedValue("<>", 2);
            condition.AddNamedValue("=", 3);
            condition.AddNamedValue(">=", 4);
            condition.AddNamedValue(">", 5);
            pManager.AddGenericParameter("True", "T", "The value to returns if the condition is true", GH_ParamAccess.item);
            pManager.AddGenericParameter("False", "F", "The value to returns if the condition is false", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Output", "O", "The output", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            IGH_Goo v1 = null;
            IGH_Goo v2 = null;
            int condition = 0;
            IGH_Goo ifTrue = null;
            IGH_Goo ifFalse = null;

            if (!DA.GetData(0, ref v1))
                return;
            if (!DA.GetData(1, ref v2))
                return;
            if (!DA.GetData(2, ref condition))
                return;
            if (!DA.GetData(3, ref ifTrue))
                return;
            if (!DA.GetData(4, ref ifFalse))
                return;

            try
            {
                int comp = 0;
                if (Compare(v1, v2, ref comp))
                {
                    bool result = false;
                    switch (condition)
                    {
                        case 0: // <
                            if (comp < 0)
                                result = true;
                            break;
                        case 1: // <=
                            if (comp <= 0)
                                result = true;
                            break;
                        case 2: // <>
                            if (comp != 0)
                                result = true;
                            break;
                        case 3: // =
                            if (comp == 0)
                                result = true;
                            break;
                        case 4: // >=
                            if (comp >= 0)
                                result = true;
                            break;
                        case 5: // >
                            if (comp > 0)
                                result = true;
                            break;
                    }
                    if (result)
                        DA.SetData(0, ifTrue);
                    else
                        DA.SetData(0, ifFalse);
                }
            }
            catch (Exception e)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
            }
        }

        protected bool Compare(IGH_Goo v1, IGH_Goo v2, ref int result)
        {
            if (v1 == null || v2 == null)
                return false;

            object value1, value2;

            PropertyInfo[] properties = v1.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo valueProp = properties.SingleOrDefault(p => p.Name == "Value");
            if (valueProp == null)
                return false;
            value1 = valueProp.GetValue(v1);

            properties = v2.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            valueProp = properties.SingleOrDefault(p => p.Name == "Value");
            if (valueProp == null)
                return false;
            value2 = valueProp.GetValue(v2);

            if (value1 is IComparable)
            {
                result = ((IComparable)value1).CompareTo(value2);
                return true;
            }
            else if (value2 is IComparable)
            {
                result = ((IComparable)value2).CompareTo(value1);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.IfIcon;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("c3733bd0-6c6a-4636-96ea-7cf53be6db57"); 
    }
}