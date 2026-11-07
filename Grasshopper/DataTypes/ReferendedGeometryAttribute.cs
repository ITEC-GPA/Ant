using System;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;

namespace Ant.GrasshopperPlugIn.DataTypes
{
    public class ReferendedGeometryAttribute : IGH_Goo
    {
        protected Guid _referenceID;
        protected string _key;
        protected object _value;

        public Guid ReferenceID => _referenceID;

        public string Key => _key;

        public object Value => _value;

        public ReferendedGeometryAttribute(Guid referenceID, string key, object value)
        {
            _referenceID = referenceID;
            _key = key;
            _value = value;
        }

        public bool IsValid => _referenceID != Guid.Empty && _key != string.Empty;

        public string IsValidWhyNot
        {
            get
            {
                string reason = "";
                if (_referenceID == Guid.Empty)
                    reason += "Invalid Geometry ";
                if (_key == string.Empty)
                    reason += "Invalid Key";
                return reason;
            }
        }

        public string TypeName => GetType().ToString();

        public string TypeDescription => "Referenced geometry attribute";

        public bool CastFrom(object source)
        {
            return false;
        }

        public bool CastTo<T>(out T target)
        {
            target = default(T);
            return false;
        }

        public IGH_Goo Duplicate()
        {
            return new ReferendedGeometryAttribute(_referenceID, _key, _value);
        }

        public IGH_GooProxy EmitProxy()
        {
            return null;
        }

        public bool Read(GH_IReader reader)
        {
            // Preserve the existing no-op archive behavior; attribute persistence is not implemented.
            return true;
        }

        public object ScriptVariable()
        {
            return null;
        }

        public bool Write(GH_IWriter writer)
        {
            // Preserve the existing no-op archive behavior; attribute persistence is not implemented.
            return true;
        }
    }
}
