using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nevishk
{
    /// <summary>
    /// Невишк is a green city, like Кишинёв: chestnuts, limes and acacias line the streets and fill the yards
    /// between the панельки. Low-poly deciduous trees (five-sided trunk, faceted crown) batched into a few
    /// large meshes, so thousands of them cost little. Placement rules live in NevishkMap; this only builds.
    /// </summary>
    public class NevishkGreenery
    {
        static readonly Color Bark = new Color(0.30f, 0.24f, 0.19f);
        static readonly Color[] Leaves =
        {
            new Color(0.23f, 0.37f, 0.19f),   // chestnut
            new Color(0.29f, 0.43f, 0.21f),   // lime
            new Color(0.19f, 0.31f, 0.17f),   // dark elm
            new Color(0.38f, 0.45f, 0.22f),   // acacia, yellower
        };

        class Batch { public readonly List<Vector3> v = new List<Vector3>(); public readonly List<Vector3> n = new List<Vector3>(); public readonly List<int> t = new List<int>(); }
        readonly Batch _trunks = new Batch();
        readonly Batch[] _crowns = { new Batch(), new Batch(), new Batch(), new Batch() };
        readonly Dictionary<long, List<Vector2>> _placed = new Dictionary<long, List<Vector2>>();
        const float Grid = 8f;
        public int Count { get; private set; }

        static Vector3[] _ico;
        static int[] _icoFaces;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _ico = null; _icoFaces = null; }

        static long Key(int i, int j) => ((long)i << 32) ^ (uint)j;

        /// False if another tree stands within `spacing` metres.
        public bool Room(Vector3 p, float spacing)
        {
            int ci = Mathf.FloorToInt(p.x / Grid), cj = Mathf.FloorToInt(p.z / Grid);
            for (int dj = -1; dj <= 1; dj++)
            for (int di = -1; di <= 1; di++)
                if (_placed.TryGetValue(Key(ci + di, cj + dj), out var list))
                    foreach (var q in list)
                        if ((q - new Vector2(p.x, p.z)).sqrMagnitude < spacing * spacing) return false;
            return true;
        }

        /// One tree standing at p (on the ground), `size` ~1 = a mature street tree about 9 m tall.
        public void Tree(Vector3 p, float size, int seed)
        {
            var rng = new System.Random(seed);
            float yaw = (float)rng.NextDouble() * 360f;
            var rot = Quaternion.Euler(0, yaw, 0);
            float trunkH = 3.6f * size, trunkR = 0.32f * size;
            Prism(_trunks, p + Vector3.down * 0.3f, trunkR, trunkH + 0.6f, rot);

            var crown = _crowns[rng.Next(_crowns.Length)];
            float r = (2.6f + (float)rng.NextDouble() * 0.9f) * size;
            var centre = p + Vector3.up * (trunkH + r * 0.65f);
            var tilt = rot * Quaternion.Euler((float)rng.NextDouble() * 20f, 0, (float)rng.NextDouble() * 20f);
            Ico(crown, centre, new Vector3(r, r * 0.86f, r), tilt);

            int ci = Mathf.FloorToInt(p.x / Grid), cj = Mathf.FloorToInt(p.z / Grid);
            if (!_placed.TryGetValue(Key(ci, cj), out var list)) _placed[Key(ci, cj)] = list = new List<Vector2>();
            list.Add(new Vector2(p.x, p.z));
            Count++;
        }

        static void Prism(Batch b, Vector3 baseCentre, float r, float h, Quaternion rot)
        {
            const int sides = 5;
            for (int s = 0; s < sides; s++)
            {
                float a0 = s * Mathf.PI * 2f / sides, a1 = (s + 1) * Mathf.PI * 2f / sides;
                Vector3 d0 = rot * new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = rot * new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                Vector3 n = (d0 + d1).normalized;
                int k = b.v.Count;
                b.v.Add(baseCentre + d0 * r); b.v.Add(baseCentre + d1 * r);
                b.v.Add(baseCentre + d1 * r * 0.7f + Vector3.up * h); b.v.Add(baseCentre + d0 * r * 0.7f + Vector3.up * h);
                for (int q = 0; q < 4; q++) b.n.Add(n);
                // Outward-facing (Unity front face: normal = cross(b - a, c - a)).
                AddTri(b, k, k + 3, k + 2, n); AddTri(b, k, k + 2, k + 1, n);
            }
        }

        static void AddTri(Batch b, int i0, int i1, int i2, Vector3 n)
        {
            if (Vector3.Dot(Vector3.Cross(b.v[i1] - b.v[i0], b.v[i2] - b.v[i0]), n) >= 0f) { b.t.Add(i0); b.t.Add(i1); b.t.Add(i2); }
            else { b.t.Add(i0); b.t.Add(i2); b.t.Add(i1); }
        }

        /// Flat-shaded icosahedron: 20 facets read as a leafy crown at map scale.
        static void Ico(Batch b, Vector3 c, Vector3 scale, Quaternion rot)
        {
            if (_ico == null)
            {
                float t = (1f + Mathf.Sqrt(5f)) / 2f;
                _ico = new[]
                {
                    new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                    new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                    new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
                };
                for (int i = 0; i < _ico.Length; i++) _ico[i] = _ico[i].normalized;
                _icoFaces = new[] { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                                    3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
            }
            for (int f = 0; f < _icoFaces.Length; f += 3)
            {
                Vector3 a = c + rot * Vector3.Scale(_ico[_icoFaces[f]], scale);
                Vector3 bb = c + rot * Vector3.Scale(_ico[_icoFaces[f + 1]], scale);
                Vector3 cc = c + rot * Vector3.Scale(_ico[_icoFaces[f + 2]], scale);
                Vector3 centroid = (a + bb + cc) / 3f;
                Vector3 n = (centroid - c).normalized;   // facets face away from the crown's centre
                int k = b.v.Count;
                b.v.Add(a); b.v.Add(bb); b.v.Add(cc);
                b.n.Add(n); b.n.Add(n); b.n.Add(n);
                AddTri(b, k, k + 1, k + 2, n);
            }
        }

        public void Finish(Transform parent, Func<Color, Material> mat)
        {
            var root = new GameObject("Greenery").transform;
            root.SetParent(parent, false);
            Add(root, "Tree trunks", _trunks, mat(Bark));
            for (int i = 0; i < _crowns.Length; i++) Add(root, "Tree crowns", _crowns[i], mat(Leaves[i]));
        }

        static void Add(Transform root, string name, Batch b, Material m)
        {
            if (b.t.Count == 0) return;
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(b.v); mesh.SetNormals(b.n); mesh.SetTriangles(b.t, 0);
            mesh.RecalculateBounds();
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }
    }
}
