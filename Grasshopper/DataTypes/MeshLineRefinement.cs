using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.DataTypes
{
    public class MeshLineRefinement : MeshRefinement<Line>, IGH_PreviewData
    {
        public MeshLineRefinement() : this(Line.Unset, 1) { }
        public MeshLineRefinement(Line line, double size) : base(line, size) { }
        protected override MeshRefinement<Line> Create(Line geometry, double size) => new MeshLineRefinement(geometry, size);
        public override void DrawViewportWires(GH_PreviewWireArgs args) { if (IsValid) args.Pipeline.DrawLine(_geometry, args.Color); }
        protected override Line ReadGeometry(GH_IReader reader)
        {
            var line = reader.GetLine("Line"); return new Line(line.A.x, line.A.y, line.A.z, line.B.x, line.B.y, line.B.z);
        }
        protected override bool WriteGeometry(GH_IWriter writer)
        {
            writer.SetLine("Line", new GH_IO.Types.GH_Line(_geometry.FromX, _geometry.FromY, _geometry.FromZ, _geometry.ToX, _geometry.ToY, _geometry.ToZ)); return true;
        }
        public override bool CastTo<V>(out V target)
        {
            target = default;
            if (!IsValid) return false;
            if (Cast(new GH_Line(_geometry), out target) || Cast(_geometry, out target)) return true;
            if (typeof(V).IsAssignableFrom(typeof(GH_Curve))) return Cast(new GH_Curve(new LineCurve(_geometry)), out target);
            if (typeof(V).IsAssignableFrom(typeof(LineCurve))) return Cast(new LineCurve(_geometry), out target);
            return false;
        }
    }
}
