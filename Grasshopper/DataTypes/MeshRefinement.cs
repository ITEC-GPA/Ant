using System;
using System.Linq;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.DataTypes
{
    public abstract class MeshRefinement<T> : IGH_GeometricGoo
    {
        protected T _geometry;
        protected double _size;
        protected Guid _referenceID;
        private string _readError;

        protected MeshRefinement(T geometry, double size) { _geometry = geometry; _size = size; }
        public T Geometry => _geometry;
        public double Size => _size;
        public Guid ReferenceID { get => _referenceID; set => _referenceID = value; }
        public bool IsReferencedGeometry => _referenceID != Guid.Empty;
        public bool IsGeometryLoaded => GeometryValid(_geometry);
        public bool IsValid => _readError == null && IsGeometryLoaded && double.IsFinite(_size) && _size > 0;
        public string IsValidWhyNot => _readError ?? (!IsGeometryLoaded ? "Missing or invalid refinement geometry." :
            !double.IsFinite(_size) || _size <= 0 ? "Mesh size must be finite and greater than zero." : string.Empty);
        public string TypeName => "MeshRefinement";
        public string TypeDescription => "Geometry with a local mesh size";
        public BoundingBox Boundingbox
        {
            get
            {
                var box = GeometryBounds(_geometry);
                if (box.IsValid && double.IsFinite(_size) && _size > 0) box.Inflate(_size);
                return box;
            }
        }
        public BoundingBox ClippingBox => Boundingbox;
        public bool CastFrom(object source) => false; // Geometry alone cannot supply a mesh size.
        public virtual bool CastTo<V>(out V target) { target = default; return false; }
        protected static bool Cast<V>(object value, out V target)
        {
            if (value is V converted) { target = converted; return true; }
            target = default; return false;
        }
        public void ClearCaches() { } // Geometry is an owned snapshot, not a document-object cache.
        public bool LoadGeometry() => IsGeometryLoaded;
        public bool LoadGeometry(RhinoDoc doc) => IsGeometryLoaded;
        public IGH_GooProxy EmitProxy() => null;
        public object ScriptVariable() => this;
        public virtual void DrawViewportMeshes(GH_PreviewMeshArgs args) { }
        public virtual void DrawViewportWires(GH_PreviewWireArgs args) { }
        protected abstract MeshRefinement<T> Create(T geometry, double size);
        protected abstract T ReadGeometry(GH_IReader reader);
        protected abstract bool WriteGeometry(GH_IWriter writer);

        public IGH_Goo Duplicate() => DuplicateGeometry();
        public IGH_GeometricGoo DuplicateGeometry()
        {
            var copy = Create(CopyGeometry(), _size);
            copy._referenceID = _referenceID;
            copy._readError = _readError;
            return copy;
        }
        public BoundingBox GetBoundingBox(Transform xform)
        {
            var box = Boundingbox;
            if (box.IsValid && xform.IsValid) box.Transform(xform);
            return box;
        }
        public IGH_GeometricGoo Transform(Transform xform)
        {
            if (!IsValid || !xform.IsValid) return null;
            object transformed;
            switch (_geometry)
            {
                case Point3d point: point.Transform(xform); transformed = point; break;
                case Line line: if (!line.Transform(xform)) return null; transformed = line; break;
                case Polyline polyline:
                    var poly = new Polyline(polyline); poly.Transform(xform); transformed = poly; break;
                case GeometryBase geometry:
                    var copy = geometry.Duplicate();
                    if (copy == null || !copy.Transform(xform)) { copy?.Dispose(); return null; }
                    transformed = copy; break;
                default: return null;
            }
            // Mesh size remains an explicit value in model units. A moved copy has no document reference.
            return CreateTransformed(transformed);
        }
        public IGH_GeometricGoo Morph(SpaceMorph morph)
        {
            if (!IsValid || morph == null) return null;
            object transformed;
            switch (_geometry)
            {
                case Point3d point: transformed = morph.MorphPoint(point); break;
                case Line line: transformed = new Line(morph.MorphPoint(line.From), morph.MorphPoint(line.To)); break;
                case Polyline polyline: transformed = new Polyline(polyline.Select(morph.MorphPoint)); break;
                case PolylineCurve curve:
                    transformed = new PolylineCurve(Enumerable.Range(0, curve.PointCount).Select(i => morph.MorphPoint(curve.Point(i)))); break;
                case GeometryBase geometry:
                    var copy = geometry.Duplicate();
                    if (copy == null || !morph.Morph(copy)) { copy?.Dispose(); return null; }
                    transformed = copy; break;
                default: return null;
            }
            return CreateTransformed(transformed);
        }
        private IGH_GeometricGoo CreateTransformed(object geometry)
        {
            if (geometry is T typed && GeometryValid(typed)) return Create(typed, _size);
            (geometry as IDisposable)?.Dispose();
            return null;
        }
        private T CopyGeometry()
        {
            if (_geometry is Polyline polyline) return (T)(object)new Polyline(polyline);
            if (_geometry is GeometryBase geometry) return (T)(object)geometry.Duplicate();
            return _geometry; // Point and line are value types.
        }
        private static bool GeometryValid(T geometry) => geometry switch
        {
            Point3d point => point.IsValid,
            Line line => line.IsValid,
            Polyline polyline => polyline.IsValid,
            GeometryBase value => value.IsValid,
            _ => false
        };
        private static BoundingBox GeometryBounds(T geometry) => geometry switch
        {
            Point3d point when point.IsValid => new BoundingBox(point, point),
            Line line when line.IsValid => line.BoundingBox,
            Polyline polyline when polyline.IsValid => polyline.BoundingBox,
            GeometryBase value when value.IsValid => value.GetBoundingBox(true),
            _ => BoundingBox.Empty
        };
        public bool Read(GH_IReader reader)
        {
            _geometry = default; _size = 0; _referenceID = Guid.Empty; _readError = null;
            try
            {
                var geometry = ReadGeometry(reader);
                double size = reader.GetDouble("Size");
                if (!GeometryValid(geometry) || !double.IsFinite(size) || size <= 0)
                {
                    (geometry as IDisposable)?.Dispose();
                    throw new ArgumentException("Invalid refinement geometry or mesh size in archive.");
                }
                _referenceID = reader.ItemExists("ReferenceID") ? reader.GetGuid("ReferenceID") : Guid.Empty;
                _geometry = geometry; _size = size;
                return true;
            }
            catch (Exception e)
            {
                _readError = e.Message;
                reader.AddMessage("Mesh Refinement: " + e.Message, GH_Message_Type.error);
                return false;
            }
        }
        public bool Write(GH_IWriter writer)
        {
            if (!IsValid) { writer.AddMessage(IsValidWhyNot, GH_Message_Type.error); return false; }
            try
            {
                if (!WriteGeometry(writer)) return false;
                writer.SetDouble("Size", _size); writer.SetGuid("ReferenceID", _referenceID);
                return true;
            }
            catch (Exception e) { writer.AddMessage("Mesh Refinement: " + e.Message, GH_Message_Type.error); return false; }
        }
        public override string ToString() => IsValid ? $"Mesh Refinement: Size={_size}, Geometry={_geometry}" : "Invalid Mesh Refinement: " + IsValidWhyNot;
    }
}
