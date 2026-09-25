using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace Ant.GrasshopperPlugIn
{
    public class AntInfo : GH_AssemblyInfo
    {
        public override string Name
        {
            get
            {
                return "Ant";
            }
        }
        public override Bitmap Icon
        {
            get
            {
                //Return a 24x24 pixel bitmap to represent this GHA library.
                return null;
            }
        }
        public override string Description
        {
            get
            {
                //Return a short string describing the purpose of this GHA library.
                return "Drawing Utilities";
            }
        }
        public override Guid Id
        {
            get
            {
                return new Guid("dcc8234f-adcc-40e6-874f-d759c7479526");
            }
        }

        public override string AuthorName
        {
            get
            {
                //Return a string identifying you or your company.
                return "Gabriele Pacini";
            }
        }
        public override string AuthorContact
        {
            get
            {
                //Return a string representing your preferred contact details.
                return "gabrielepacini6293@gmail.com";
            }
        }
    }
}
