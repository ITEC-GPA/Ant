using System;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.DataTypes
{
    public class MeshPolylineCurveRefinement : MeshRefinement<PolylineCurve>, IGH_PreviewData
    {
        public MeshPolylineCurveRefinement(PolylineCurve polylineCurve, double size)
            : base(polylineCurve, size) 
        {
            _boundingBox = new BoundingBox(polylineCurve.GetBoundingBox(true).Min - new Vector3d(size, size, size), polylineCurve.GetBoundingBox(true).Max + new Vector3d(size, size, size));            
        }
        

        public override void DrawViewportWires(GH_PreviewWireArgs args)
        {
            if (_geometry.TryGetPolyline(out Polyline poly))     
                args.Pipeline.DrawPolyline(poly, args.Color);
        }


        public override IGH_Goo Duplicate()
        {
            if (_geometry.TryGetPolyline(out Polyline poly))
                return new MeshPolylineRefinement(poly, _size);
            else
                throw new Exception();
        }

        public override bool Read(GH_IReader reader)
        {
            try
            {
                if (_geometry.TryGetPolyline(out Polyline poly))
                {
                    Point3d[] pointsArray = new Point3d[poly.SegmentCount];
                    for (int i = 0; i < poly.SegmentCount; i++)
                    {
                        GH_IO.Types.GH_Point3D pt = reader.GetPoint3D("Point");
                        Point3d ptRhino = new Point3d(pt.x, pt.y, pt.z);
                        pointsArray[i] = ptRhino;
                    }

                    Polyline polyline = new Polyline(pointsArray);
                    PolylineCurve polyCurve = new PolylineCurve(pointsArray);

                    _geometry = polyCurve;
                    _size = reader.GetDouble("Size");
                    _referenceID = reader.GetGuid("ReferenceID");

                    return true;
                }
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("MeshRefinement.Read: {0}", e.Message);
                return false;
            }
            return false;
        }

        public override bool Write(GH_IWriter writer)
        {
            try
            {
                for(int i = 0; i< Geometry.PointCount; i++)
                {
                    Point3d point = Geometry.Point(i);
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
            Point3d[] pointsArray = new Point3d[Geometry.PointCount];
            bool check = true;
            try
            {
                if (IsValid)
                {
                    for (int i = 0; i < Geometry.PointCount; i++)
                    {
                        if (typeof(T).IsAssignableFrom(typeof(GH_Point)))
                        {
                            pointsArray[i] = Geometry.Point(i);                            
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
