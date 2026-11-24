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
        public float BaseY = -80f;     // bottom of the side walls (hidden: the edges already sink into the dark)

        float[] _h;      // heights, row 0 = north
        float[] _water;  // water level, NaN where dry
        // World extent covered by the grid. It reaches past the drawn map so the edges can fade out.
        float _minX, _maxX, _minZ, _maxZ;

        public NevishkTerrain(float width, float depth)
        {
            Width = width; Depth = depth;
        }

        public bool Load(string resource = "nevishk_terrain")
        {
            var asset = Resources.Load<TextAsset>(resource);
            if (asset == null) { Debug.LogWarning("[Nevishk] Terrain data missing: Resources/" + resource); return false; }
            var b = asset.bytes;
            if (b.Length < 12 || b[0] != 'N' || b[1] != 'V' || b[2] != 'T')
            {
                Debug.LogError("[Nevishk] nevishk_terrain.bytes has an unexpected format.");
                return false;
            }
            GridW = BitConverter.ToInt32(b, 4);
            GridH = BitConverter.ToInt32(b, 8);
            int o = 12;
            if (b[3] == '2')
            {
                _minX = BitConverter.ToSingle(b, 12); _maxX = BitConverter.ToSingle(b, 16);
                _minZ = BitConverter.ToSingle(b, 20); _maxZ = BitConverter.ToSingle(b, 24);
                o = 28;
            }
            else
            {
                _minX = -Width / 2; _maxX = Width / 2; _minZ = -Depth / 2; _maxZ = Depth / 2;
            }
            int n = GridW * GridH;
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
        public float HeightAtUV(float u, float v) { ToGrid(u, v, out float gu, out float gv); return Sample(_h, gu, gv); }

        /// Water surface at a UV, or NaN where there is no water.
        public float WaterAtUV(float u, float v)
        {
            ToGrid(u, v, out float gu, out float gv);
            int i = Mathf.Clamp(Mathf.RoundToInt(gu * (GridW - 1)), 0, GridW - 1);
            int j = Mathf.Clamp(Mathf.RoundToInt(gv * (GridH - 1)), 0, GridH - 1);
            return _water[j * GridW + i];
        }

        /// Map UV (0..1 over the drawn map, origin top-left) → grid UV (0..1 over the whole terrain).
        void ToGrid(float u, float v, out float gu, out float gv)
        {
            float x = (u - 0.5f) * Width, z = (0.5f - v) * Depth;
            gu = (x - _minX) / (_maxX - _minX);
            gv = (_maxZ - z) / (_maxZ - _minZ);
        }

        /// Top surface (water if present, else ground).
        public float SurfaceAtUV(float u, float v)
        {
            float g = HeightAtUV(u, v), w = WaterAtUV(u, v);
            return float.IsNaN(w) ? g : Mathf.Max(g, w);
        }

        public float HeightAtWorld(Vector3 p) => HeightAtUV(p.x / Width + 0.5f, 0.5f - p.z / Depth);

        /// The terrain's full extent in world space (larger than the drawn map).
        public Rect WorldRect => Rect.MinMaxRect(_minX, _minZ, _maxX, _maxZ);

        public float WaterAtWorld(Vector3 p) => WaterAtUV(p.x / Width + 0.5f, 0.5f - p.z / Depth);

        /// Shape the ground to a path before the meshes are built: within halfWidth the ground is set to
        /// the path height (minus bedDepth), blending back to the natural ground over `falloff`.
        /// Segments whose ends are flagged in `skip` (bridges, tunnels) are left untouched.
        public void FlattenAlong(IList<Vector3> path, IList<bool> skip, float halfWidth, float falloff, float bedDepth = 0.3f)
        {
            int n = GridW * GridH;
            var best = new float[n]; var target = new float[n];
            for (int q = 0; q < n; q++) best[q] = float.MaxValue;
            float cx = (_maxX - _minX) / (GridW - 1), cz = (_maxZ - _minZ) / (GridH - 1);
            float reach = halfWidth + falloff;
            for (int k = 0; k + 1 < path.Count; k++)
            {
                if (skip[k] || skip[k + 1]) continue;
                Vector3 a = path[k], b = path[k + 1];
                int i0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach - _minX) / cx));
                int i1 = Mathf.Min(GridW - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + reach - _minX) / cx));
                int j0 = Mathf.Max(0, Mathf.FloorToInt((_maxZ - Mathf.Max(a.z, b.z) - reach) / cz));
                int j1 = Mathf.Min(GridH - 1, Mathf.CeilToInt((_maxZ - Mathf.Min(a.z, b.z) + reach) / cz));
                Vector2 A = new Vector2(a.x, a.z), AB = new Vector2(b.x - a.x, b.z - a.z);
                float len2 = Mathf.Max(AB.sqrMagnitude, 1e-6f);
                for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    var P = new Vector2(X(i), Z(j));
                    float t = Mathf.Clamp01(Vector2.Dot(P - A, AB) / len2);
                    float d = Vector2.Distance(P, A + AB * t);
                    int idx = j * GridW + i;
                    if (d < best[idx]) { best[idx] = d; target[idx] = Mathf.Lerp(a.y, b.y, t) - bedDepth; }
                }
            }
            for (int q = 0; q < n; q++)
            {
                if (best[q] >= reach) continue;
                float w = best[q] <= halfWidth ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (best[q] - halfWidth) / falloff);
                _h[q] = Mathf.Lerp(_h[q], target[q], w);
            }
        }

        float Sample(float[] a, float u, float v)
        {
            float x = Mathf.Clamp01(u) * (GridW - 1), y = Mathf.Clamp01(v) * (GridH - 1);
            int x0 = Mathf.Min((int)x, GridW - 2), y0 = Mathf.Min((int)y, GridH - 2);
            float fx = x - x0, fy = y - y0;
            float h00 = a[y0 * GridW + x0], h10 = a[y0 * GridW + x0 + 1];
            float h01 = a[(y0 + 1) * GridW + x0], h11 = a[(y0 + 1) * GridW + x0 + 1];
            return Mathf.Lerp(Mathf.Lerp(h00, h10, fx), Mathf.Lerp(h01, h11, fx), fy);
        }

        float X(int i) => Mathf.Lerp(_minX, _maxX, i / (float)(GridW - 1));
        float Z(int j) => Mathf.Lerp(_maxZ, _minZ, j / (float)(GridH - 1));
        Vector3 Vertex(int i, int j) => new Vector3(X(i), _h[j * GridW + i], Z(j));

        Vector3 Normal(int i, int j)
        {
            float cx = (_maxX - _minX) / (GridW - 1), cz = (_maxZ - _minZ) / (GridH - 1);
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
            // Water surfaces are flat sheets at their water level that reach a few cells *under* the banks.
            // The visible shoreline is then where the terrain dips below the sheet, which follows the smooth
            // banks instead of the grid's square cells.
            int n = GridW * GridH;
            var level = new float[n];
            for (int q = 0; q < n; q++) level[q] = _water[q];
            for (int pass = 0; pass < 4; pass++)
            {
                var grown = (float[])level.Clone();
                for (int j = 0; j < GridH; j++)
                for (int i = 0; i < GridW; i++)
                {
                    int k = j * GridW + i;
                    if (!float.IsNaN(level[k])) continue;
                    float found = float.NaN;
                    if (i > 0 && !float.IsNaN(level[k - 1])) found = level[k - 1];
                    else if (i < GridW - 1 && !float.IsNaN(level[k + 1])) found = level[k + 1];
                    else if (j > 0 && !float.IsNaN(level[k - GridW])) found = level[k - GridW];
                    else if (j < GridH - 1 && !float.IsNaN(level[k + GridW])) found = level[k + GridW];
                    // Only spread under banks, never into a deeper hollow (the ravine) where it would show.
                    if (!float.IsNaN(found) && _h[k] > found - 2f) grown[k] = found;
                }
                level = grown;
            }

            var v = new List<Vector3>(); var tri = new List<int>();
            float cx = (_maxX - _minX) / (GridW - 1), cz = (_maxZ - _minZ) / (GridH - 1);
            for (int j = 0; j < GridH - 1; j++)
            for (int i = 0; i < GridW - 1; i++)
            {
                float lvl = float.NaN;
                for (int q = 0; q < 4 && float.IsNaN(lvl); q++) lvl = level[(j + (q >> 1)) * GridW + i + (q & 1)];
                if (float.IsNaN(lvl)) continue;
                float x0 = X(i) - 0.2f, x1 = x0 + cx + 0.4f;
                float z0 = Z(j) + 0.2f, z1 = z0 - cz - 0.4f;
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
