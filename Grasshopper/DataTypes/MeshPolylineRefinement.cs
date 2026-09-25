using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.DataTypes
{
    public class MeshPolylineRefinement : MeshRefinement<Polyline>, IGH_PreviewData
    {
        public MeshPolylineRefinement() : this(null, 1) { }
        public MeshPolylineRefinement(Polyline polyline, double size) : base(polyline, size) { }
        protected override MeshRefinement<Polyline> Create(Polyline geometry, double size) => new MeshPolylineRefinement(geometry, size);
        public override void DrawViewportWires(GH_PreviewWireArgs args) { if (IsValid) args.Pipeline.DrawPolyline(_geometry, args.Color); }
        protected override Polyline ReadGeometry(GH_IReader reader) => MeshRefinementArchive.ReadPolyline(reader);
        protected override bool WriteGeometry(GH_IWriter writer) => MeshRefinementArchive.WritePolyline(writer, _geometry);
        public override bool CastTo<V>(out V target)
        {
            target = default;
            if (!IsValid) return false;
            if (typeof(V).IsAssignableFrom(typeof(GH_Curve))) return Cast(new GH_Curve(new PolylineCurve(_geometry)), out target);
            if (typeof(V).IsAssignableFrom(typeof(Polyline))) return Cast(new Polyline(_geometry), out target);
            if (typeof(V).IsAssignableFrom(typeof(PolylineCurve))) return Cast(new PolylineCurve(_geometry), out target);
            return false;
        }
    }
}
