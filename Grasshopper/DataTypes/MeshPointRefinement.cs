using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.DataTypes
{
    public class MeshPointRefinement : MeshRefinement<Point3d>, IGH_PreviewData
    {
        public MeshPointRefinement() : this(Point3d.Unset, 1) { }
        public MeshPointRefinement(Point3d point, double size) : base(point, size) { }
        protected override MeshRefinement<Point3d> Create(Point3d geometry, double size) => new MeshPointRefinement(geometry, size);
        public override void DrawViewportWires(GH_PreviewWireArgs args)
        {
            if (!IsValid) return;
            args.Pipeline.DrawPoint(_geometry, args.Color);
            args.Pipeline.DrawSphere(new Sphere(_geometry, _size), args.Color);
        }
        protected override Point3d ReadGeometry(GH_IReader reader)
        {
            var p = reader.GetPoint3D("Point"); return new Point3d(p.x, p.y, p.z);
        }
        protected override bool WriteGeometry(GH_IWriter writer)
        {
            writer.SetPoint3D("Point", new GH_IO.Types.GH_Point3D(_geometry.X, _geometry.Y, _geometry.Z)); return true;
        }
        public override bool CastTo<V>(out V target)
        {
            target = default;
            return IsValid && (Cast(new GH_Point(_geometry), out target) || Cast(_geometry, out target));
        }
    }
}
