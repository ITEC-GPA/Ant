using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.DataTypes
{
    public class MeshPolylineCurveRefinement : MeshRefinement<PolylineCurve>, IGH_PreviewData
    {
        public MeshPolylineCurveRefinement() : this(null, 1) { }
        public MeshPolylineCurveRefinement(PolylineCurve curve, double size) : base(curve, size) { }
        protected override MeshRefinement<PolylineCurve> Create(PolylineCurve geometry, double size) => new MeshPolylineCurveRefinement(geometry, size);
        public override void DrawViewportWires(GH_PreviewWireArgs args) { if (IsValid) args.Pipeline.DrawCurve(_geometry, args.Color); }
        protected override PolylineCurve ReadGeometry(GH_IReader reader) => new PolylineCurve(MeshRefinementArchive.ReadPolyline(reader));
        protected override bool WriteGeometry(GH_IWriter writer) => _geometry.TryGetPolyline(out Polyline poly) && MeshRefinementArchive.WritePolyline(writer, poly);
        public override bool CastTo<V>(out V target)
        {
            target = default;
            if (!IsValid) return false;
            if (typeof(V).IsAssignableFrom(typeof(GH_Curve))) return Cast(new GH_Curve(_geometry.DuplicateCurve()), out target);
            if (typeof(V).IsAssignableFrom(typeof(PolylineCurve))) return Cast(_geometry.DuplicateCurve(), out target);
            return typeof(V).IsAssignableFrom(typeof(Polyline)) && _geometry.TryGetPolyline(out Polyline poly) && Cast(poly, out target);
        }
    }
}
