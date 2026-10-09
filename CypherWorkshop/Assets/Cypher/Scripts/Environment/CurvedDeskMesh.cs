using System.Collections.Generic;
using UnityEngine;

namespace Cypher
{
    /// <summary>
    /// Generates a curved slab (a slice of a ring) centered on this object's +Z axis.
    /// Used for the desk top, its glowing edge strips, the pedestal and the floor/ceiling light rings.
    /// The top surface sits at this object's Y; the slab extends `thickness` downward.
    /// Change the numbers in the Inspector and the mesh rebuilds live.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class CurvedDeskMesh : MonoBehaviour
    {
        [Min(0f)] public float innerRadius = 0.55f;
        [Min(0.01f)] public float outerRadius = 1.15f;
        [Range(1f, 360f)] public float arcDegrees = 160f;
        [Min(0.001f)] public float thickness = 0.04f;
        [Range(4, 256)] public int segments = 64;

        Mesh mesh;
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> tris = new List<int>();

        void OnEnable() => Rebuild();

        void OnDestroy()
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // Mesh changes aren't allowed inside OnValidate itself, so defer by one editor tick.
            UnityEditor.EditorApplication.delayCall += () => { if (this != null) Rebuild(); };
        }
#endif

        public void Rebuild()
        {
            if (outerRadius <= innerRadius) outerRadius = innerRadius + 0.01f;

            if (mesh == null)
                mesh = new Mesh { name = "CurvedDeskMesh (generated)", hideFlags = HideFlags.DontSave };

            verts.Clear(); normals.Clear(); uvs.Clear(); tris.Clear();

            float half = arcDegrees * 0.5f * Mathf.Deg2Rad;
            Vector3 top = Vector3.zero;
            Vector3 bottom = Vector3.down * thickness;

            for (int i = 0; i < segments; i++)
            {
                float a0 = Mathf.Lerp(-half, half, i / (float)segments);
                float a1 = Mathf.Lerp(-half, half, (i + 1) / (float)segments);
                Vector3 d0 = Dir(a0), d1 = Dir(a1);

                Vector3 in0 = d0 * innerRadius, in1 = d1 * innerRadius;
                Vector3 out0 = d0 * outerRadius, out1 = d1 * outerRadius;

                Quad(in0 + top, out0 + top, out1 + top, in1 + top, Vector3.up, Vector3.up, Vector3.up, Vector3.up);
                Quad(in0 + bottom, out0 + bottom, out1 + bottom, in1 + bottom, Vector3.down, Vector3.down, Vector3.down, Vector3.down);
                Quad(out0 + top, out0 + bottom, out1 + bottom, out1 + top, d0, d0, d1, d1);
                Quad(in0 + top, in0 + bottom, in1 + bottom, in1 + top, -d0, -d0, -d1, -d1);
            }

            if (arcDegrees < 359.9f)
            {
                Cap(-half, -Tangent(-half));
                Cap(half, Tangent(half));
            }

            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        void Cap(float angle, Vector3 normal)
        {
            Vector3 d = Dir(angle);
            Vector3 down = Vector3.down * thickness;
            Quad(d * innerRadius, d * outerRadius, d * outerRadius + down, d * innerRadius + down, normal, normal, normal, normal);
        }

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            normals.Add(na); normals.Add(nb); normals.Add(nc); normals.Add(nd);
            uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));

            // Unity treats a triangle as front-facing when Cross(b - a, c - a) points at the viewer.
            bool facesNormal = Vector3.Dot(Vector3.Cross(b - a, c - a), na + nb + nc + nd) >= 0f;
            if (facesNormal) tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            else tris.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
        }

        static Vector3 Dir(float a) => new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
        static Vector3 Tangent(float a) => new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a));
    }
}
