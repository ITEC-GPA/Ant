using System;
using Rhino;
using Grasshopper.Kernel;
using System.Reflection;

namespace Ant.GrasshopperPlugIn
{
    public class Loader : GH_AssemblyPriority
    {
        //private IDisposable _sentry;

        public override GH_LoadingInstruction PriorityLoad()
        {
            var server = Grasshopper.Instances.ComponentServer;
            server.AddCategoryShortName("Ant", "Ant");
            server.AddCategorySymbolName("Ant", 'A');
            server.AddCategoryIcon("Ant", Properties.Resources.PlugInTabIcon);
            RhinoApp.Closing += RhinoApp_Closing;
            Gpc.PluginChecks.PluginBuildCheck.Start(typeof(Loader).Assembly, "Ant");

            return GH_LoadingInstruction.Proceed;
        }

        private void RhinoApp_Closing(object sender, EventArgs e)
        {
            //_sentry?.Dispose();
        }
    }
}
