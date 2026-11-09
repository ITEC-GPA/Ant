using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Ant.GrasshopperPlugIn.DataTypes;
using Rhino;
using Rhino.Collections;

namespace Ant.GrasshopperPlugIn.Helpers
{
    internal static class UserDataStorage
    {
        // Use precisely the typed values supported by Rhino's archive dictionary.
        // Retain Value unwrapping for existing GH goo and third-party wrappers.
        private static readonly MethodInfo[] Setters = typeof(ArchivableDictionary).GetMethods()
            .Where(m => m.Name == "Set" && m.GetParameters().Length == 2 &&
                m.GetParameters()[0].ParameterType == typeof(string)).ToArray();

        internal static bool TrySet(ArchivableDictionary dictionary, string key, object wrapped, out string error)
        {
            error = null;
            try
            {
                if (string.IsNullOrEmpty(key)) throw new ArgumentException("A non-empty key is required.");
                if (wrapped == null) throw new ArgumentException("The value is null.");
                var property = wrapped.GetType().GetProperty("Value");
                var value = property != null && property.GetIndexParameters().Length == 0
                    ? property.GetValue(wrapped) : wrapped;
                if (value == null) throw new ArgumentException("The value is null.");
                var setter = Setters.FirstOrDefault(m => m.GetParameters()[1].ParameterType == value.GetType())
                    ?? Setters.FirstOrDefault(m => m.GetParameters()[1].ParameterType.IsInstanceOfType(value));
                if (setter == null) throw new ArgumentException("Unsupported value type: " + value.GetType().FullName);
                if (setter.Invoke(dictionary, new[] { (object)key, value }) is bool stored && !stored)
                    throw new InvalidOperationException("Rhino rejected the value.");
                return true;
            }
            catch (Exception e)
            {
                error = "User data '" + (key ?? "<null>") + "': " + (e.InnerException ?? e).Message;
                return false;
            }
        }

        internal static void Update(RhinoDoc document, IEnumerable<ReferendedGeometryAttribute> attributes, Action<string> warn)
        {
            if (document == null) { warn("No Rhino document is available."); return; }
            var valid = new List<ReferendedGeometryAttribute>();
            foreach (var attribute in attributes)
            {
                if (attribute == null || attribute.ReferenceID == Guid.Empty)
                    warn("User data: missing geometry reference.");
                else valid.Add(attribute);
            }
            uint undo = document.BeginUndoRecord("Ant: update geometry user data");
            try
            {
                foreach (var group in valid.GroupBy(a => a.ReferenceID))
                {
                    var obj = document.Objects.FindId(group.Key);
                    if (obj == null) { warn("Referenced object no longer exists: " + group.Key); continue; }
                    using var geometry = obj.Geometry.Duplicate();
                    bool changed = false;
                    foreach (var attribute in group)
                    {
                        if (TrySet(geometry.UserDictionary, attribute.Key, attribute.Value, out var error)) changed = true;
                        else warn(error);
                    }
                    // Replacing a copy gives Rhino an undoable change; invalid values never mutate the original.
                    if (changed && !document.Objects.Replace(group.Key, geometry, false))
                        warn("Could not update referenced object: " + group.Key);
                }
            }
            finally { if (undo != 0) document.EndUndoRecord(undo); }
        }
    }
}

