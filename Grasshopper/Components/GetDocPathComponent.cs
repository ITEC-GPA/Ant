using System;
using System.Collections.Generic;
using System.IO;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.Components
{
    public class GetDocPathComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the GetDocNameComponent class.
        /// </summary>
        public GetDocPathComponent()
          : base("Get Document Path", "GDP", "Get the full path of the GH document", "Ant", "Utilities")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Extension", "E", "Change the file extension with this", GH_ParamAccess.item);
            pManager[0].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Path", "P", "The document full path", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string ext = "";
            bool hasExtension = DA.GetData(0, ref ext);

            GH_Document doc = OnPingDocument();
            string path = doc.FilePath;

            if (hasExtension)
            {
                path = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path));
                if (ext != string.Empty)
                    path += $".{ext}";
            }

            DA.SetData(0, path);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.GetDocPathIcon;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("38d14574-74cd-47aa-acfc-f725bdf57481"); 
    }
}