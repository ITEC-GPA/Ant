using Rhino;

namespace Ant.GrasshopperPlugIn.Helpers
{
    // Historical Planar Mesh policy. Do not simplify/reorder the arithmetic:
    // the separate metres adjustment and fixed projection tolerance are compatibility rules.
    internal readonly struct MeshToleranceSettings
    {
        internal double GeometryScale { get; }
        internal double Calculation { get; }
        internal double MeshScale { get; }
        internal double MeshSize { get; }
        internal double MeshMatch { get; }
        internal double GeometryAndMeshMatch { get; }
        internal double Projection { get; }
        internal double RhinoMatch { get; }

        internal MeshToleranceSettings(double tolerance, double size, UnitSystem units)
        {
            tolerance /= 1000;
            if (units == UnitSystem.Meters) tolerance /= 1000;
            double occTolerance = 1E-7;
            GeometryScale = occTolerance / tolerance;
            Calculation = tolerance * GeometryScale;
            MeshScale = 1 / GeometryScale;
            MeshSize = size * GeometryScale;
            MeshMatch = tolerance * GeometryScale * 100;
            GeometryAndMeshMatch = tolerance * GeometryScale / 10;
            Projection = 1E-4;
            RhinoMatch = tolerance * 1000;
        }
    }
}
