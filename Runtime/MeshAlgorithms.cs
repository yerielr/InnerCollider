using UnityEngine;
using System.Collections.Generic;

namespace MeshColliderTools
{
    /// <summary>
    /// Stateless mesh manipulation algorithms. No editor dependencies — safe to call
    /// from runtime code or editor tools.
    /// </summary>
    public static class MeshAlgorithms
    {
        // 1/4096 ≈ 0.24 mm — fine enough for any game mesh, coarse enough to tolerate
        // floating-point drift. Vector3Int keys avoid the float equality trap.
        private const float k_PosPrecision = 4096f;

        /// <summary>
        /// Returns a new Mesh containing both the original triangles and a flipped copy.
        /// Triangle count doubles; each side has independent, correctly-oriented normals.
        /// </summary>
        public static Mesh BuildDoubleSidedMesh(Mesh source)
        {
            if (source == null) throw new System.ArgumentNullException(nameof(source));

            int       vc         = source.vertexCount;
            Vector3[] srcVerts   = source.vertices;
            Vector3[] srcNormals = EnsureNormals(source);
            Vector2[] srcUVs     = source.uv;
            int[]     srcTris    = source.triangles;
            bool      hasUVs     = srcUVs != null && srcUVs.Length == vc;

            Vector3[] verts   = new Vector3[vc * 2];
            Vector3[] normals = new Vector3[vc * 2];
            Vector2[] uvs     = new Vector2[vc * 2];

            for (int i = 0; i < vc; i++)
            {
                verts[i]        = verts[vc + i]   = srcVerts[i];
                normals[i]      = srcNormals[i];
                normals[vc + i] = -srcNormals[i];  // flipped for inside face
                uvs[i]          = uvs[vc + i] = hasUVs ? srcUVs[i] : Vector2.zero;
            }

            int   tc      = srcTris.Length;
            int[] newTris = new int[tc * 2];

            for (int i = 0; i < tc; i += 3)
            {
                newTris[i]         = srcTris[i];
                newTris[i + 1]     = srcTris[i + 1];
                newTris[i + 2]     = srcTris[i + 2];

                // Flip winding (swap verts 0 & 2), offset into second vertex block.
                newTris[tc + i]     = vc + srcTris[i + 2];
                newTris[tc + i + 1] = vc + srcTris[i + 1];
                newTris[tc + i + 2] = vc + srcTris[i];
            }

            var result = new Mesh
            {
                vertices  = verts,
                normals   = normals,
                uv        = uvs,
                triangles = newTris
            };
            result.RecalculateBounds();
            return result;
        }

        /// <summary>Returns a fully independent copy of the mesh.</summary>
        public static Mesh DeepCopy(Mesh src)
        {
            if (src == null) return null;
            var copy = new Mesh
            {
                name         = src.name,
                vertices     = src.vertices,
                normals      = src.normals,
                tangents     = src.tangents,
                uv           = src.uv,
                uv2          = src.uv2,
                colors32     = src.colors32,
                subMeshCount = src.subMeshCount
            };
            for (int i = 0; i < src.subMeshCount; i++)
                copy.SetTriangles(src.GetTriangles(i), i);
            copy.RecalculateBounds();
            return copy;
        }

        // ─────────────────────────────────────────────────────────────────────────

        private static Vector3[] EnsureNormals(Mesh source)
        {
            var n = source.normals;
            if (n != null && n.Length == source.vertexCount) return n;

            var tmp = new Mesh { vertices = source.vertices, triangles = source.triangles };
            tmp.RecalculateNormals();
            n = tmp.normals;
            Object.DestroyImmediate(tmp);
            return n;
        }
    }
}
