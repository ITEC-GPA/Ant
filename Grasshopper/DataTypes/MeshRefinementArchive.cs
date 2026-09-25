using System;
using GH_IO.Serialization;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.DataTypes
{
    internal static class MeshRefinementArchive
    {
        internal static bool WritePolyline(GH_IWriter writer, Polyline polyline)
        {
            writer.SetInt32("PolylineVersion", 1);
            writer.SetInt32("PointCount", polyline.Count);
            for (int i = 0; i < polyline.Count; i++)
            {
                var point = polyline[i];
                writer.SetPoint3D("Point", i, new GH_IO.Types.GH_Point3D(point.X, point.Y, point.Z));
            }
            return true;
        }

        internal static Polyline ReadPolyline(GH_IReader reader)
        {
            if (!reader.ItemExists("PointCount"))
                throw new ArgumentException("This legacy polyline refinement has no complete vertex list. Recreate it from the original curve with Mesh Refinement.");
            if (reader.ItemExists("PolylineVersion") && reader.GetInt32("PolylineVersion") != 1)
                throw new ArgumentException("Unsupported polyline refinement archive version.");
            int count = reader.GetInt32("PointCount");
            if (count < 2 || count > reader.ItemCount)
                throw new ArgumentException("Invalid polyline vertex count in archive.");
            var polyline = new Polyline(count);
            for (int i = 0; i < count; i++)
            {
                if (!reader.ItemExists("Point", i)) throw new ArgumentException($"Polyline vertex {i} is missing from the archive.");
                var point = reader.GetPoint3D("Point", i);
                polyline.Add(new Point3d(point.x, point.y, point.z));
            }
            return polyline;
        }
    }
}
