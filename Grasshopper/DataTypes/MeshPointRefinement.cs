using System;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.DataTypes
{
    public class MeshPointRefinement : MeshRefinement<Point3d>, IGH_PreviewData
    {
        public MeshPointRefinement(Point3d point, double size)
            : base(point, size)
        {
            _boundingBox = new BoundingBox(point - new Vector3d(size, size, size), point + new Vector3d(size, size, size));
        }

        public override void DrawViewportWires(GH_PreviewWireArgs args)
        {            
            args.Pipeline.DrawPoint(_geometry, args.Color);
            Sphere sphere = new Sphere(_geometry, _size);
            args.Pipeline.DrawSphere(sphere, args.Color);
        }

        public override IGH_Goo Duplicate()
        {
            return new MeshPointRefinement(_geometry, _size);
        }

        public override IGH_GeometricGoo DuplicateGeometry()
        {
            return new GH_Point(_geometry);
        }

        public override bool Read(GH_IReader reader)
        {
            try
            {
                GH_IO.Types.GH_Point3D point = reader.GetPoint3D("Point");
                _geometry = new Point3d(point.x, point.y, point.z);
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
                writer.SetPoint3D("Point", new GH_IO.Types.GH_Point3D(_geometry.X, _geometry.Y, _geometry.Z));
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
            try
            {
                if (IsValid)
                {
                    if (typeof(T).IsAssignableFrom(typeof(GH_Point)))
                    {
                        GH_Point point = new GH_Point(_geometry);
                        target = (T)Convert.ChangeType(point, typeof(T));
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
