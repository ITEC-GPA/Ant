using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rhino.Geometry;

namespace Ant.GrasshopperPlugIn.Helpers
{
    internal static class MeshExtensions
    {
        public static Mesh ConvertToRhinoMesh(this GPC.Geometry.Meshes.Mesh mesh)
        {
            Mesh rhinoMesh = new Mesh();
            rhinoMesh.Vertices.UseDoublePrecisionVertices = true;

            Dictionary<int, int> idRhinoIndexMap = new Dictionary<int, int>();
            GPC.Geometry.Meshes.MeshVertex[] vertices = mesh.GetVertices();
            GPC.Geometry.Meshes.MeshFace[] faces = mesh.GetFaces();

            try
            {
            for (int v = 0; v < vertices.Length; v++)
            {
                int tag = rhinoMesh.Vertices.Add(vertices[v].Point.X, vertices[v].Point.Y, vertices[v].Point.Z);
                idRhinoIndexMap.Add(vertices[v].Id, tag);
            }

            for (int f = 0; f < faces.Length; f++)
            {
                if (faces[f].IsQuad)
                {
                    rhinoMesh.Faces.AddFace(
                        idRhinoIndexMap[faces[f].A], idRhinoIndexMap[faces[f].B],
                        idRhinoIndexMap[faces[f].C], idRhinoIndexMap[faces[f].D]
                    );
                }
                else
                {
                    rhinoMesh.Faces.AddFace(
                        idRhinoIndexMap[faces[f].A], idRhinoIndexMap[faces[f].B], idRhinoIndexMap[faces[f].C]
                    );
                }
            }

            rhinoMesh.Compact();

            return rhinoMesh;
            }
            catch { rhinoMesh.Dispose(); throw; }
        }
    }
}
