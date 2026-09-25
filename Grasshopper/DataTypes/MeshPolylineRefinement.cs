using System;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.DataTypes
{
    public class MeshPolylineRefinement : MeshRefinement<Polyline>, IGH_PreviewData
    {
        public MeshPolylineRefinement(Polyline polyline, double size)
            : base(polyline, size) 
        {
            _boundingBox = new BoundingBox(polyline.BoundingBox.Max - new Vector3d(size, size, size), polyline.BoundingBox.Max + new Vector3d(size, size, size));            
        }
        
        public override void DrawViewportWires(GH_PreviewWireArgs args)
        {
            args.Pipeline.DrawPolyline(_geometry, args.Color);
        }

        public override IGH_Goo Duplicate()
        {
            return new MeshPolylineRefinement(_geometry, _size);
        }

        public override bool Read(GH_IReader reader)
        {
            try
            {
                Point3d[] pointsArray = new Point3d[Geometry.SegmentCount];
                for (int i = 0; i < Geometry.SegmentCount; i++)
                {
                    GH_IO.Types.GH_Point3D pt = reader.GetPoint3D("Point");
                    Point3d ptRhino = new Point3d(pt.x, pt.y, pt.z);
                    pointsArray[i] = ptRhino;
                }

                Polyline polyline = new Polyline(pointsArray);
                _geometry = polyline;
                _size = reader.GetDouble("Size");
                _referenceID = reader.GetGuid("ReferenceID");

                return true;
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("MeshRefinement.Read: {0}", e.Message);
                return false;
            }
        }

        public override bool Write(GH_IWriter writer)
        {
            try
            {
                foreach (Point3d point in Geometry)
                {
                    writer.SetPoint3D("Point", new GH_IO.Types.GH_Point3D(point.X, point.Y, point.Z));
                }
                writer.SetDouble("Size", _size);
                writer.SetGuid("ReferenceID", _referenceID);
                return true;
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("MeshRefinement.Write: {0}", e.Message);
                return false;
            }
        }

        public override bool CastTo<T>(out T target)
        {
            target = default(T);
            Point3d[] pointsArray = new Point3d[Geometry.SegmentCount];
            bool check = true;
            try
            {
                if (IsValid)
                {
                    for (int i = 0; i < Geometry.SegmentCount; i++)
                    {
                        if (typeof(T).IsAssignableFrom(typeof(GH_Point)))
                        {
                            pointsArray[i] = Geometry[i];                            
                        }
                        else
                        {
                            check = false;
                        }
                    }
                    if (check)
                    {
                        target = (T)Convert.ChangeType(pointsArray, typeof(T));
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine(e.Message);
            }
            return false;
        }
    }
}
