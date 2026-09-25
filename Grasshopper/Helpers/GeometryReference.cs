using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grasshopper.Kernel.Types;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.Helpers
{
    public static class GeometryReference
    {
        public static IGH_GeometricGoo Get(RhinoObject obj)
        {
            switch (obj.ObjectType)
            {
                case ObjectType.Brep:
                    return new GH_Brep(obj.Id);
                case ObjectType.Curve:
                    return new GH_Curve(obj.Id);
                case ObjectType.Mesh:
                    return new GH_Mesh(obj.Id);
                case ObjectType.Point:
                    return new GH_Point(obj.Id);
                case ObjectType.Surface:
                    return new GH_Surface(obj.Id);
            }
            return null;
        }

        public static IGH_GeometricGoo[] GetSubByLayerIndex(InstanceObject block, int[] layerIndex)
        {
            List<IGH_GeometricGoo> geoms = new List<IGH_GeometricGoo>();
            foreach (RhinoObject obj in block.GetSubObjects().Where(so => layerIndex.Contains(so.Attributes.LayerIndex)))
            {
                IGH_GeometricGoo geom = null;
                switch (obj.ObjectType)
                {
                    case ObjectType.Brep:
                        geom = new GH_Brep((Brep)obj.Geometry);
                        break;
                    case ObjectType.Curve:
                        geom = new GH_Curve((Curve)obj.Geometry);
                        break;
                    case ObjectType.Mesh:
                        geom = new GH_Mesh((Mesh)obj.Geometry);
                        break;
                    //case ObjectType.Point:
                    //    geom = new GH_Point((Point)obj.Geometry.);
                    //    break;
                    case ObjectType.Surface:
                        geom = new GH_Surface((Surface)obj.Geometry);
                        break;
                }
                if (geom != null)
                    geoms.Add(geom);
            }
            return geoms.ToArray();
        }

    }
}
