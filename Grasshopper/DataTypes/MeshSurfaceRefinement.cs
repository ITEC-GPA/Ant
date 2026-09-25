using System;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.DataTypes
{
    public class MeshSurfaceRefinement : MeshRefinement<Brep>, IGH_PreviewData
    {
        public MeshSurfaceRefinement() : this(null, 1) { }
        public MeshSurfaceRefinement(Brep surface, double size) : base(surface, size) { }
        protected override MeshRefinement<Brep> Create(Brep geometry, double size) => new MeshSurfaceRefinement(geometry, size);
        public override void DrawViewportWires(GH_PreviewWireArgs args) { if (IsValid) args.Pipeline.DrawBrepWires(_geometry, args.Color, args.Thickness); }
        public override void DrawViewportMeshes(GH_PreviewMeshArgs args) { if (IsValid) args.Pipeline.DrawBrepShaded(_geometry, args.Material); }
        protected override bool WriteGeometry(GH_IWriter writer) => new GH_Brep(_geometry).Write(writer.CreateChunk("Region"));
        protected override Brep ReadGeometry(GH_IReader reader)
        {
            var goo = new GH_Brep(); var chunk = reader.FindChunk("Region");
            if (chunk == null || !goo.Read(chunk)) throw new ArgumentException("Missing or invalid refinement region in archive.");
            return goo.Value;
        }
        public override bool CastTo<V>(out V target)
        {
            target = default;
            if (!IsValid) return false;
            if (typeof(V).IsAssignableFrom(typeof(GH_Brep))) return Cast(new GH_Brep(_geometry.DuplicateBrep()), out target);
            if (typeof(V).IsAssignableFrom(typeof(Brep))) return Cast(_geometry.DuplicateBrep(), out target);
            return false;
        }
    }
}
