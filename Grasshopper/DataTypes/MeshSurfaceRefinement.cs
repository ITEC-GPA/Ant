using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ant.GrasshopperPlugIn.DataTypes
{
    public class MeshSurfaceRefinement : MeshRefinement<Brep>
    {

        public MeshSurfaceRefinement(Brep surface, double size)
            :base(surface, size)
        {

        }
    }
}
