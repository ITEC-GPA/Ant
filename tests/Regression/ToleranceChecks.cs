using System;
using Ant.GrasshopperPlugIn.Helpers;
using Rhino;
using Rhino.Geometry;

internal static class ToleranceChecks
{
    // IEEE754 values frozen from the pre-1.1.5 expressions, not calculated by the implementation under test.
    static readonly (double Tolerance, UnitSystem Units, long[] Bits)[] Golden = {
            (1E-05, UnitSystem.None, new long[] { 0x4024000000000000L, 0x3E7AD7F29ABCAF48L, 0x3FB999999999999AL, 0x4039000000000000L, 0x3EE4F8B588E368F0L, 0x3E45798EE2308C3AL, 0x3F1A36E2EB1C432DL, 0x3EE4F8B588E368F1L }),
            (0.1, UnitSystem.None, new long[] { 0x3F50624DD2F1A9FBL, 0x3E7AD7F29ABCAF47L, 0x408F400000000002L, 0x3F647AE147AE147AL, 0x3EE4F8B588E368EFL, 0x3E45798EE2308C39L, 0x3F1A36E2EB1C432DL, 0x3FB999999999999AL }),
            (100, UnitSystem.None, new long[] { 0x3EB0C6F7A0B5ED8DL, 0x3E7AD7F29ABCAF48L, 0x412E848000000000L, 0x3EC4F8B588E368F0L, 0x3EE4F8B588E368F0L, 0x3E45798EE2308C3AL, 0x3F1A36E2EB1C432DL, 0x4059000000000000L }),
            (1E-05, UnitSystem.Millimeters, new long[] { 0x4024000000000000L, 0x3E7AD7F29ABCAF48L, 0x3FB999999999999AL, 0x4039000000000000L, 0x3EE4F8B588E368F0L, 0x3E45798EE2308C3AL, 0x3F1A36E2EB1C432DL, 0x3EE4F8B588E368F1L }),
            (0.1, UnitSystem.Millimeters, new long[] { 0x3F50624DD2F1A9FBL, 0x3E7AD7F29ABCAF47L, 0x408F400000000002L, 0x3F647AE147AE147AL, 0x3EE4F8B588E368EFL, 0x3E45798EE2308C39L, 0x3F1A36E2EB1C432DL, 0x3FB999999999999AL }),
            (100, UnitSystem.Millimeters, new long[] { 0x3EB0C6F7A0B5ED8DL, 0x3E7AD7F29ABCAF48L, 0x412E848000000000L, 0x3EC4F8B588E368F0L, 0x3EE4F8B588E368F0L, 0x3E45798EE2308C3AL, 0x3F1A36E2EB1C432DL, 0x4059000000000000L }),
            (1E-05, UnitSystem.Meters, new long[] { 0x40C387FFFFFFFFFFL, 0x3E7AD7F29ABCAF48L, 0x3F1A36E2EB1C432EL, 0x40D869FFFFFFFFFFL, 0x3EE4F8B588E368F0L, 0x3E45798EE2308C3AL, 0x3F1A36E2EB1C432DL, 0x3E45798EE2308C3AL }),
            (0.1, UnitSystem.Meters, new long[] { 0x3FEFFFFFFFFFFFFFL, 0x3E7AD7F29ABCAF48L, 0x3FF0000000000001L, 0x4003FFFFFFFFFFFFL, 0x3EE4F8B588E368F0L, 0x3E45798EE2308C3AL, 0x3F1A36E2EB1C432DL, 0x3F1A36E2EB1C432DL }),
            (100, UnitSystem.Meters, new long[] { 0x3F50624DD2F1A9FBL, 0x3E7AD7F29ABCAF47L, 0x408F400000000002L, 0x3F647AE147AE147AL, 0x3EE4F8B588E368EFL, 0x3E45798EE2308C39L, 0x3F1A36E2EB1C432DL, 0x3FB999999999999AL }),
    };
    internal static void Run(Action<string, Action> check)
    {
        foreach(var units in new[] { UnitSystem.None, UnitSystem.Millimeters, UnitSystem.Meters })
            check("tolerances: historical values bit-identical in " + units, () => {
                foreach(var row in Golden)
                {
                    if(row.Units != units) continue;
                    var t = new MeshToleranceSettings(row.Tolerance, 2.5, units);
                    var values = new[] { t.GeometryScale,t.Calculation,t.MeshScale,t.MeshSize,t.MeshMatch,t.GeometryAndMeshMatch,t.Projection,t.RhinoMatch };
                    for(int i=0;i<values.Length;i++)
                        if(BitConverter.DoubleToInt64Bits(values[i]) != row.Bits[i]) throw new Exception("Tolerance arithmetic changed: " + units + " / " + row.Tolerance + " / " + i);
                }
            });
        foreach(double z in new[] {0.00005, 0.0002})
            check("tolerances: fixed projection boundary z=" + z, () => {
                using var surface = Brep.CreateFromCornerPoints(new Point3d(0,0,0),new Point3d(10,0,0),new Point3d(10,10,0),new Point3d(0,10,0),1e-5);
                foreach(var units in new[] {UnitSystem.Millimeters,UnitSystem.Meters})
                {
                    var t = new MeshToleranceSettings(0.1, 2.5, units);
                    bool accepted = MeshInputGeometry.TryProjectLine(surface,new Line(new Point3d(1,1,z),new Point3d(9,1,z)),t.Projection,out var projected);
                    if(accepted != (z < 0.0001)) throw new Exception("Projection acceptance changed");
                    if(accepted && (projected.From.Z != 0 || projected.To.Z != 0)) throw new Exception("Projection is not on the surface");
                }
            });
    }
}
