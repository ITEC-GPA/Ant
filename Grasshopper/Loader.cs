using Grasshopper.Kernel;

namespace Ant.GrasshopperPlugIn
{
    public class Loader : GH_AssemblyPriority
    {
        public override GH_LoadingInstruction PriorityLoad()
        {
            var server = Grasshopper.Instances.ComponentServer;
            server.AddCategoryShortName("Ant", "Ant");
            server.AddCategorySymbolName("Ant", 'A');
            server.AddCategoryIcon("Ant", Properties.Resources.PlugInTabIcon);
            Gpc.PluginChecks.PluginBuildCheck.Start(typeof(Loader).Assembly, "Ant");

            return GH_LoadingInstruction.Proceed;
        }
    }
}
