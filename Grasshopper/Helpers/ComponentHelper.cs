using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grasshopper.Kernel.Special;

namespace Ant.GrasshopperPlugIn.Helpers
{
    public static class ComponentHelper
    {
        /// <summary>
        /// Setup a Value List componet with the given keys
        /// </summary>
        /// <param name="vallist">The Value List component</param>
        /// <param name="keys">The keys to putting in</param>
        public static void SetValueList(GH_ValueList vallist, IEnumerable<string> keys)
        {
            IEnumerable<GH_ValueListItem> ex_keys = vallist.ListItems.Where(i => keys.Contains(i.Name));
            if (ex_keys.Count() != vallist.ListItems.Count)
            {
                List<GH_ValueListItem> selected = vallist.SelectedItems;

                vallist.ListItems.Clear();
                foreach (string k in keys)
                    vallist.ListItems.Add(new GH_ValueListItem(k, $"\"{k}\""));

                foreach (GH_ValueListItem sv in selected)
                {
                    GH_ValueListItem ev = vallist.ListItems.SingleOrDefault(i => i.Name == sv.Name);
                    if (ev != null)
                    {
                        int i = vallist.ListItems.IndexOf(ev);
                        if (i != -1)
                            vallist.SelectItem(i);
                    }
                }
            }
        }
    }
}
