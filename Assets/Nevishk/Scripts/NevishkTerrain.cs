using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nevishk
{
    /// <summary>
    /// 3D ground for the city, built from Resources/nevishk_terrain.bytes (heights + water levels,
    /// generated from the base map by Tools/nevishk/build_terrain.py):
    /// raised districts, the river Мырла in the channels, the Ущелье ravine, hills and the eastern mountains.
    /// Produces a diorama slab (terrain tiles + side walls) and flat water meshes.
    /// </summary>
    public class NevishkTerrain
    {
        public int GridW { get; private set; }
        public int GridH { get; private set; }
        public float Width { get; }
        public float Depth { get; }
        public float BaseY = -80f;     // bottom of the diorama slab

        float[] _h;      // heights, row 0 = top (north) of the map
        float[] _water;  // water level, NaN where dry

        public NevishkTerrain(float width, float depth)
        {
            Width = width; Depth = depth;
        }

        public bool Load(string resource = "nevishk_terrain")
        {
            var asset = Resources.Load<TextAsset>(resource);
            if (asset == null) { Debug.LogWarning("[Nevishk] Terrain data missing: Resources/" + resource); return false; }
            var b = asset.bytes;
            if (b.Length < 12 || b[0] != 'N' || b[1] != 'V' || b[2] != 'T' || b[3] != 'R')
            {
                Debug.LogError("[Nevishk] nevishk_terrain.bytes has an unexpected format.");
                return false;
            }
            GridW = BitConverter.ToInt32(b, 4);
            GridH = BitConverter.ToInt32(b, 8);
            int n = GridW * GridH, o = 12;
            _h = new float[n]; _water = new float[n];
            for (int i = 0; i < n; i++) _h[i] = BitConverter.ToInt16(b, o + i * 2) / 10f;
            o += n * 2;
            for (int i = 0; i < n; i++)
            {
                short q = BitConverter.ToInt16(b, o + i * 2);
                _water[i] = q == short.MinValue ? float.NaN : q / 10f;
            }
            return true;
        }

        // ------------------------------------------------------------------ sampling

        /// Ground height at a map UV (0..1, origin top-left, like the lore data).
        public float HeightAtUV(float u, float v) => Sample(_h, u, v);

        /// Water surface at a UV, or NaN where there is no water.
        public float WaterAtUV(float u, float v)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt(u * (GridW - 1)), 0, GridW - 1);
            int j = Mathf.Clamp(Mathf.RoundToInt(v * (GridH - 1)), 0, GridH - 1);
            return _water[j * GridW + i];
        }

        /// Top surface (water if present, else ground).
        public float SurfaceAtUV(float u, float v)
        {
            float g = HeightAtUV(u, v), w = WaterAtUV(u, v);
            return float.IsNaN(w) ? g : Mathf.Max(g, w);
        }

        public float HeightAtWorld(Vector3 p) => HeightAtUV(p.x / Width + 0.5f, 0.5f - p.z / Depth);

        float Sample(float[] a, float u, float v)
        {
            float x = Mathf.Clamp01(u) * (GridW - 1), y = Mathf.Clamp01(v) * (GridH - 1);
            int x0 = Mathf.Min((int)x, GridW - 2), y0 = Mathf.Min((int)y, GridH - 2);
            float fx = x - x0, fy = y - y0;
            float h00 = a[y0 * GridW + x0], h10 = a[y0 * GridW + x0 + 1];
            float h01 = a[(y0 + 1) * GridW + x0], h11 = a[(y0 + 1) * GridW + x0 + 1];
            return Mathf.Lerp(Mathf.Lerp(h00, h10, fx), Mathf.Lerp(h01, h11, fx), fy);
        }

        Vector3 Vertex(int i, int j) =>
            new Vector3((i / (float)(GridW - 1) - 0.5f) * Width, _h[j * GridW + i], (0.5f - j / (float)(GridH - 1)) * Depth);

        Vector3 Normal(int i, int j)
        {
            float cx = Width / (GridW - 1), cz = Depth / (GridH - 1);
            float hl = _h[j * GridW + Mathf.Max(i - 1, 0)], hr = _h[j * GridW + Mathf.Min(i + 1, GridW - 1)];
            float hu = _h[Mathf.Max(j - 1, 0) * GridW + i], hd = _h[Mathf.Min(j + 1, GridH - 1) * GridW + i];
            float dhdx = (hr - hl) / (2f * cx);
            float dhdz = (hu - hd) / (2f * cz);   // row index grows southward (−z)
            return new Vector3(-dhdx, 1f, -dhdz).normalized;
        }

        // ------------------------------------------------------------------ meshes

        /// Ground tiles (seamless: normals come from the heightmap, not per tile).
        public List<Mesh> BuildGroundTiles(int tile = 128)
        {
            var meshes = new List<Mesh>();
            for (int ty = 0; ty < GridH - 1; ty += tile)
            for (int tx = 0; tx < GridW - 1; tx += tile)
            {
                int w = Mathf.Min(tile, GridW - 1 - tx) + 1, hgt = Mathf.Min(tile, GridH - 1 - ty) + 1;
                var verts = new Vector3[w * hgt]; var norms = new Vector3[w * hgt]; var uvs = new Vector2[w * hgt];
                for (int y = 0; y < hgt; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = tx + x, j = ty + y, k = y * w + x;
                    verts[k] = Vertex(i, j);
                    norms[k] = Normal(i, j);
                    uvs[k] = new Vector2(i / (float)(GridW - 1), 1f - j / (float)(GridH - 1));
                }
                var tris = new int[(w - 1) * (hgt - 1) * 6];
                int t = 0;
                for (int y = 0; y < hgt - 1; y++)
                for (int x = 0; x < w - 1; x++)
                {
                    int a = y * w + x, b = a + 1, c = a + w, d = c + 1;   // a=NW b=NE c=SW d=SE
                    tris[t++] = a; tris[t++] = b; tris[t++] = c;
                    tris[t++] = b; tris[t++] = d; tris[t++] = c;
                }
                var m = new Mesh { name = $"Terrain {tx},{ty}" };
                if (verts.Length > 65000) m.indexFormat = IndexFormat.UInt32;
                m.vertices = verts; m.normals = norms; m.uv = uvs; m.triangles = tris;
                m.RecalculateBounds();
                meshes.Add(m);
            }
            return meshes;
        }

        /// Vertical walls around the edge of the slab, down to BaseY.
        public Mesh BuildSkirt()
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
            void Edge(Func<int, Vector3> top, int count, Vector3 outward)
            {
                for (int s = 0; s < count - 1; s++)
                {
                    Vector3 a = top(s), b = top(s + 1);
                    int k = v.Count;
                    v.Add(a); v.Add(b); v.Add(new Vector3(a.x, BaseY, a.z)); v.Add(new Vector3(b.x, BaseY, b.z));
                    for (int q = 0; q < 4; q++) n.Add(outward);
                    // Wind so the face points outward.
                    Vector3 face = Vector3.Cross(b - a, v[k + 2] - a);
                    if (Vector3.Dot(face, outward) >= 0) { tri.AddRange(new[] { k, k + 1, k + 2, k + 1, k + 3, k + 2 }); }
                    else { tri.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 }); }
                }
            }
            Edge(i => Vertex(i, 0), GridW, Vector3.forward);                 // north
            Edge(i => Vertex(i, GridH - 1), GridW, Vector3.back);            // south
            Edge(j => Vertex(0, j), GridH, Vector3.left);                    // west
            Edge(j => Vertex(GridW - 1, j), GridH, Vector3.right);           // east
            var m = new Mesh { name = "Terrain Skirt", indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(tri, 0); m.RecalculateBounds();
            return m;
        }

        /// Flat water surfaces: one quad per wet cell, merged.
        public Mesh BuildWater()
        {
            var v = new List<Vector3>(); var tri = new List<int>();
            float cx = Width / (GridW - 1), cz = Depth / (GridH - 1);
            for (int j = 0; j < GridH - 1; j++)
            for (int i = 0; i < GridW - 1; i++)
            {
                float lvl = 0f; int wet = 0;
                for (int q = 0; q < 4; q++)
                {
                    float w = _water[(j + (q >> 1)) * GridW + i + (q & 1)];
                    if (!float.IsNaN(w)) { lvl += w; wet++; }
                }
                if (wet == 0) continue;
                lvl /= wet;
                // Grow each quad slightly so neighbours overlap and leave no cracks.
                float x0 = (i / (float)(GridW - 1) - 0.5f) * Width - 0.3f, x1 = x0 + cx + 0.6f;
                float z0 = (0.5f - j / (float)(GridH - 1)) * Depth + 0.3f, z1 = z0 - cz - 0.6f;
                int k = v.Count;
                v.Add(new Vector3(x0, lvl, z0)); v.Add(new Vector3(x1, lvl, z0));
                v.Add(new Vector3(x0, lvl, z1)); v.Add(new Vector3(x1, lvl, z1));
                tri.Add(k); tri.Add(k + 1); tri.Add(k + 2); tri.Add(k + 1); tri.Add(k + 3); tri.Add(k + 2);
            }
            var m = new Mesh { name = "Water", indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v); m.SetTriangles(tri, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
