using System;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.DataTypes
{
    public abstract class MeshRefinement<T> : IGH_GeometricGoo //IGH_PreviewData
    {
        protected T _geometry;
        protected double _size;
        protected BoundingBox _boundingBox;
        protected Guid _referenceID;

        public T Geometry => _geometry;

        public double Size => _size;

        public MeshRefinement(T geomerty, double size)
        {
            _geometry = geomerty;
            _size = size;
            // _boundingBox;
            _referenceID = Guid.Empty;
        }

        public BoundingBox Boundingbox => _boundingBox;

        public Guid ReferenceID 
        { 
            get => _referenceID; 
            set => _referenceID = value; 
        }

        public bool IsReferencedGeometry => _referenceID != Guid.Empty;

        public bool IsGeometryLoaded => false;

        public bool IsValid => true;    // (_geometry is Point && _size != 0) || (_geometry is LineCurve && _size != 0) || (_geometry is PolyCurve && _size != 0) || (_geometry is PolylineCurve && _size != 0);

        public string IsValidWhyNot => "";

        public string TypeName => "MeshRefinement";

        public string TypeDescription => "MeshRefinement";

        public BoundingBox ClippingBox => _boundingBox;

        public bool CastFrom(object source)
        {
            return false;
        }

        public virtual bool CastTo<V>(out V target)
        {
            target = default(V);
            return false;
        }

        public void ClearCaches()
        {
            return;
        }

        public virtual void DrawViewportMeshes(GH_PreviewMeshArgs args)
        {
        }

        public virtual void DrawViewportWires(GH_PreviewWireArgs args)
        {
            throw new NotImplementedException();
        }

        public virtual IGH_Goo Duplicate()
        {
            throw new NotImplementedException();
        }

        public virtual IGH_GeometricGoo DuplicateGeometry()
        {
            throw new NotImplementedException();
        }

        public IGH_GooProxy EmitProxy()
        {
            return null;
        }

        public BoundingBox GetBoundingBox(Transform xform)
        {
            BoundingBox boundingbox = new BoundingBox(_boundingBox.Min, _boundingBox.Max);
            boundingbox.Transform(xform);
            return boundingbox;
        }

        public bool LoadGeometry()
        {
            return false;
        }

        public bool LoadGeometry(RhinoDoc doc)
        {
            return false;
        }

        public IGH_GeometricGoo Morph(SpaceMorph xmorph)
        {
            throw new NotImplementedException();
        }

        public virtual bool Read(GH_IReader reader)
        {
            return true;
        }

        public object ScriptVariable()
        {
            return this;
        }

        public IGH_GeometricGoo Transform(Transform xform)
        {
            throw new NotImplementedException();
        }

        public virtual bool Write(GH_IWriter writer)
        {
            return true;
        }

        public override string ToString()
        {
            return $"Mesh Refinement:\r\nSize: {_size}\r\nPosition: {_geometry}";
        }
    }
}
