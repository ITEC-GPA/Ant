using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ant.GrasshopperPlugIn.DataTypes;
using Ant.GrasshopperPlugIn.Helpers;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;

internal static class UserDataChecks
{
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static void Run(Action<string, Action> check)
    {
        check("user data: typed values retain their archive types", () =>
        {
            using var point = new LineCurve(Point3d.Origin, new Point3d(1,0,0));
            foreach (var pair in new (string, object, object)[] {
                ("integer", new GH_Integer(7), 7), ("number", new GH_Number(2.5), 2.5),
                ("text", new GH_String("steel"), "steel"), ("bool", new GH_Boolean(true), true),
                ("point", new GH_Point(new Point3d(1,2,3)), new Point3d(1,2,3)) })
            {
                Require(UserDataStorage.TrySet(point.UserDictionary, pair.Item1, pair.Item2, out var error), error);
                Require(Equals(point.UserDictionary[pair.Item1], pair.Item3), "Value/type changed");
            }
            using var doc = RhinoDoc.CreateHeadless(null);
            var id = doc.Objects.AddCurve(point);
            string path = Path.Combine(AppContext.BaseDirectory, "ant-user-data-roundtrip-" + Guid.NewGuid() + ".3dm");
            Require(doc.WriteFile(path, new Rhino.FileIO.FileWriteOptions { WriteUserData = true, SuppressDialogBoxes = true }), "Write failed");
            using var reopened = RhinoDoc.OpenHeadless(path);
            Require(reopened != null, "Reopen failed");
            var saved = reopened.Objects.FindId(id);
            Require(saved != null && saved.Geometry.UserDictionary.ContainsKey("integer"), "Saved dictionary lost");
            Require((int)saved.Geometry.UserDictionary["integer"] == 7, "Saved type/value changed");        });
        check("user data: invalid values do not overwrite stored data", () =>
        {
            using var point = new LineCurve(Point3d.Origin, new Point3d(1,0,0)); point.UserDictionary.Set("key", 42);
            foreach (var value in new object[] { null, new GH_String((string)null), new object() })
                Require(!UserDataStorage.TrySet(point.UserDictionary, "key", value, out var error) && error.Contains("key"), "Invalid value accepted");
            Require(!UserDataStorage.TrySet(point.UserDictionary, null, new GH_Integer(1), out _), "Null key accepted");
            Require((int)point.UserDictionary["key"] == 42, "Existing value changed");
        });
        check("user data: duplicate keys preserve last-value precedence", () =>
        {
            using var point = new LineCurve(Point3d.Origin, new Point3d(1,0,0));
            UserDataStorage.TrySet(point.UserDictionary, "key", new GH_Integer(1), out _);
            UserDataStorage.TrySet(point.UserDictionary, "key", new GH_Integer(2), out _);
            Require((int)point.UserDictionary["key"] == 2, "Precedence changed");
        });
        check("user data: missing document and references report warnings", () =>
        {
            var warnings = new List<string>();
            UserDataStorage.Update(null, Array.Empty<ReferendedGeometryAttribute>(), warnings.Add);
            using var doc = RhinoDoc.CreateHeadless(null);
            UserDataStorage.Update(doc, new[] { null, new ReferendedGeometryAttribute(Guid.NewGuid(), "key", new GH_Integer(1)) }, warnings.Add);
            Require(warnings.Count == 3, "Missing reference diagnostics lost");
        });
        check("user data: repeated updates preserve object identity and other keys", () =>
        {
            using var doc = RhinoDoc.CreateHeadless(null);
            using var point = new LineCurve(new Point3d(1,2,3), new Point3d(4,5,6)); point.UserDictionary.Set("original", "keep");
            var id = doc.Objects.Add(point); var warnings = new List<string>();
            for (int i = 1; i <= 2; i++)
                UserDataStorage.Update(doc, new[] {new ReferendedGeometryAttribute(id, "key", new GH_Integer(i))}, warnings.Add);
            var obj = doc.Objects.FindId(id);
            Require(warnings.Count == 0 && obj != null, "Update failed");
            Require((int)obj.Geometry.UserDictionary["key"] == 2 && (string)obj.Geometry.UserDictionary["original"] == "keep", "Data lost");
            Require(!point.UserDictionary.ContainsKey("key"), "Input mutated");
        });
        check("user data: update is undoable", () =>
        {
            using var doc = RhinoDoc.CreateHeadless(null); doc.UndoRecordingEnabled = true;
            var id = doc.Objects.AddPoint(Point3d.Origin);
            UserDataStorage.Update(doc, new[] {new ReferendedGeometryAttribute(id, "key", new GH_Integer(5))}, message => throw new Exception(message));
            Require(doc.Undo(), "Undo failed");
            Require(!doc.Objects.FindId(id).Geometry.UserDictionary.ContainsKey("key"), "Undo did not restore original");
        });
    }
}






