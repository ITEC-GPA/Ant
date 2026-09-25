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

            Dictionary<int, int> idRhinoIndexMap = new Dictionary<int, int>();
            GPC.Geometry.Meshes.MeshVertex[] vertices = mesh.GetVertices();
            GPC.Geometry.Meshes.MeshFace[] faces = mesh.GetFaces();

            Dictionary<int, List<int>> indexMap = mesh.Vertices.GetElementIdMap();

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
                        idRhinoIndexMap[mesh.Vertices.GetElementByIndex(indexMap[faces[f].A].FirstOrDefault()).Id],
                        idRhinoIndexMap[mesh.Vertices.GetElementByIndex(indexMap[faces[f].B].FirstOrDefault()).Id],
                        idRhinoIndexMap[mesh.Vertices.GetElementByIndex(indexMap[faces[f].C].FirstOrDefault()).Id],
                        idRhinoIndexMap[mesh.Vertices.GetElementByIndex(indexMap[faces[f].D].FirstOrDefault()).Id]
                    );
                }
                else
                {
                    rhinoMesh.Faces.AddFace(
                        idRhinoIndexMap[mesh.Vertices.GetElementByIndex(indexMap[faces[f].A].FirstOrDefault()).Id],
                        idRhinoIndexMap[mesh.Vertices.GetElementByIndex(indexMap[faces[f].B].FirstOrDefault()).Id],
                        idRhinoIndexMap[mesh.Vertices.GetElementByIndex(indexMap[faces[f].C].FirstOrDefault()).Id]
                    );
                }
            }

            rhinoMesh.Compact();

            return rhinoMesh;
        }
    }
}
