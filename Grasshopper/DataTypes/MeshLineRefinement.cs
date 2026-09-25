using System;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.DataTypes
{
    public class MeshLineRefinement : MeshRefinement<Line>, IGH_PreviewData
    {
        public MeshLineRefinement(Line line, double size)
            : base(line, size)
        {
            _boundingBox = new BoundingBox(line.BoundingBox.Min - new Vector3d(size, size, size), line.BoundingBox.Max + new Vector3d(size, size, size));
        }

        public override void DrawViewportWires(GH_PreviewWireArgs args)
        {            
            args.Pipeline.DrawLine(_geometry, args.Color);
        }


        public override IGH_Goo Duplicate()
        {
            return new MeshLineRefinement(_geometry, _size);
        }

        public override IGH_GeometricGoo DuplicateGeometry()
        {
            return new GH_Line(_geometry);
        }

        public override bool Read(GH_IReader reader)
        {
            try
            {
                GH_IO.Types.GH_Line line = reader.GetLine("Line");
                _geometry = new Line(_geometry.From, _geometry.To);
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
                writer.SetLine("Line", new GH_IO.Types.GH_Line(_geometry.FromX, _geometry.FromY, _geometry.FromZ, _geometry.ToX, _geometry.ToY, _geometry.ToZ));
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
                    if (typeof(T).IsAssignableFrom(typeof(GH_Line)))
                    {
                        GH_Line line = new GH_Line(_geometry);
                        target = (T)Convert.ChangeType(line, typeof(T));
                        return true;
                    }
                    if (typeof(T).IsAssignableFrom(typeof(GH_Curve)))
                    {
                        //GH_Curve line = new GH_Curve(_geometry);
                        //target = (T)Convert.ChangeType(line, typeof(T));
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
