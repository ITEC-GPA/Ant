using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
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
            try
            {
                //_referenceID = reader.GetGuid("ReferenceID");
                //_key = reader.GetString("Key");

                //byte[] ba = reader.GetByteArray("Value");                
                //BinaryFormatter bf = new BinaryFormatter();
                //using (MemoryStream ms = new MemoryStream())
                //{
                //    ms.Write(ba, 0, ba.Length);
                //    ms.Seek(0, SeekOrigin.Begin);
                //    _value = bf.Deserialize(ms);
                //}
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("ReferendedGeometryAttribute.Read: {0}", e.Message);
                return false;
            }
            return true;
        }

        public object ScriptVariable()
        {
            return null;
        }

        public bool Write(GH_IWriter writer)
        {
            try
            {
                //writer.SetGuid("ReferenceID", _referenceID);
                //writer.SetString("Key", _key);

                //BinaryFormatter bf = new BinaryFormatter();
                //using (MemoryStream ms = new MemoryStream())
                //{
                //    bf.Serialize(ms, _value);
                //    writer.SetByteArray("Value", ms.ToArray());
                //}
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("ReferendedGeometryAttribute.Write: {0}", e.Message);
                return false;
            }
            return true;
        }
    }
}
