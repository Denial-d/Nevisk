using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nevishk
{
    /// <summary>
    /// The zone around Невишк. Inside a ring just outside the drawn city the air stays clear, so every district
    /// reads; at the ring a tall bank of rising mist cuts the city off, and beyond it the rest of the country lies
    /// under drifting murk with only hilltops and the eastern range showing.
    /// Normally a raymarched volume (Nevishk/VolumeFog); on hardware without 3D textures it falls back to the
    /// older layered sheets (Nevishk/Fog).
    /// </summary>
    public static class NevishkFog
    {
        public static readonly Color MistColour = new Color(0.58f, 0.61f, 0.60f);   // pale grey-green, lighter than the land so it reads as mist

        /// <param name="box">The drawn city (the map image's extent) in world XZ.</param>
        /// <param name="outer">How far out the countryside reaches (half extents), for the murk layers.</param>
        /// <param name="ground">Ground height at world (x, z), city terrain and countryside alike.</param>
        public static void Build(Transform parent, Rect box, Vector2 outer, Func<float, float, float> ground, bool reducedMotion)
        {
            var c0 = box.center;
            float rhx = box.width / 2f + 90f, rhz = box.height / 2f + 95f, rCorner = 160f;
            var volume = Shader.Find("Nevishk/VolumeFog");
            if (volume != null && volume.isSupported && SystemInfo.supports3DTextures)
            {
                BuildVolume(parent, volume, c0, rhx, rhz, rCorner, outer, ground, reducedMotion);
                return;
            }
            Debug.Log("[Nevishk] Volumetric fog not supported here; using layered fog.");
            BuildLayers(parent, box, outer, ground, reducedMotion);
        }

        // ------------------------------------------------------------------ volumetric

        static void BuildVolume(Transform parent, Shader shader, Vector2 c, float hx, float hz, float corner,
                                Vector2 outer, Func<float, float, float> ground, bool reducedMotion)
        {
            const float top = 260f, bottom = -60f;
            var grid = GroundGrid(ground, outer, SheetCell);

            var m = new Material(shader) { name = "Volume fog", renderQueue = (int)RenderQueue.Transparent - 50 };
            m.SetColor("_Color", new Color(0.40f, 0.43f, 0.42f));
            m.SetColor("_LitColor", new Color(0.70f, 0.72f, 0.69f));
            m.SetFloat("_Density", 0.016f);
            m.SetTexture("_Noise3D", Noise3D(64));
            m.SetFloat("_NoiseScale", 1f / 260f);
            m.SetVector("_Wind", new Vector4(5f, 0f, 2f, 3f));
            m.SetFloat("_Speed", reducedMotion ? 0f : 1f);
            m.SetTexture("_Ground", GroundTexture(grid.smooth, grid.nx, grid.nz));
            // Texel (i, j) holds the ground at (-outer.x + i*cell, -outer.y + j*cell); sample at texel centres.
            m.SetVector("_GroundST", new Vector4(1f / (SheetCell * grid.nx), 1f / (SheetCell * grid.nz),
                                                 (outer.x / SheetCell + 0.5f) / grid.nx, (outer.y / SheetCell + 0.5f) / grid.nz));
            // An oval round the drawn city: semi-axes chosen so the map's corners sit just inside it.
            m.SetVector("_Ring", new Vector4(c.x, c.y, (hx - 90f) * 1.41f, (hz - 95f) * 1.62f));
            m.SetFloat("_RingCorner", 120f);   // how far the edge wanders in and out (m)
            m.SetVector("_Extent", new Vector4(outer.x, outer.y, top, bottom));
            m.SetFloat("_Height", 45f);
            m.SetFloat("_WallHeight", 140f);

            var go = new GameObject("Fog Zone", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, (top + bottom) / 2f, 0f);
            go.transform.localScale = new Vector3(outer.x * 2f, top - bottom, outer.y * 2f);
            go.GetComponent<MeshFilter>().sharedMesh = NevishkBuildings.CubeMesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            Debug.Log("[Nevishk] Fog zone: volumetric.");
        }

        static Texture2D GroundTexture(float[] h, int nx, int nz)
        {
            bool half = SystemInfo.SupportsTextureFormat(TextureFormat.RHalf);
            var tex = new Texture2D(nx, nz, half ? TextureFormat.RHalf : TextureFormat.RFloat, false, true)
            { name = "Fog ground", wrapMode = TextureWrapMode.Clamp, filterMode = half ? FilterMode.Bilinear : FilterMode.Point };
            if (half)
            {
                var data = new ushort[h.Length];
                for (int k = 0; k < h.Length; k++) data[k] = Mathf.FloatToHalf(h[k]);
                tex.SetPixelData(data, 0);
            }
            else tex.SetPixelData(h, 0);
            tex.Apply(false, true);
            return tex;
        }

        /// Tileable 3D fractal value noise.
        static Texture3D Noise3D(int size)
        {
            const int L = 16;
            var rng = new System.Random(1931);
            var lattice = new float[L * L * L];
            for (int i = 0; i < lattice.Length; i++) lattice[i] = (float)rng.NextDouble();
            float Lat(int x, int y, int z, int period) =>
                lattice[(((z % period) + period) % period) * L * L + (((y % period) + period) % period) * L + (((x % period) + period) % period)];
            float Value(float x, float y, float z, int period)
            {
                int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y), z0 = Mathf.FloorToInt(z);
                float fx = x - x0, fy = y - y0, fz = z - z0;
                fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
                float a = Mathf.Lerp(Lat(x0, y0, z0, period), Lat(x0 + 1, y0, z0, period), fx);
                float b = Mathf.Lerp(Lat(x0, y0 + 1, z0, period), Lat(x0 + 1, y0 + 1, z0, period), fx);
                float cc = Mathf.Lerp(Lat(x0, y0, z0 + 1, period), Lat(x0 + 1, y0, z0 + 1, period), fx);
                float d = Mathf.Lerp(Lat(x0, y0 + 1, z0 + 1, period), Lat(x0 + 1, y0 + 1, z0 + 1, period), fx);
                return Mathf.Lerp(Mathf.Lerp(a, b, fy), Mathf.Lerp(cc, d, fy), fz);
            }
            var px = new Color32[size * size * size];
            for (int z = 0; z < size; z++)
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float sum = 0f, amp = 0.5f, norm = 0f;
                for (int period = 4; period <= L; period *= 2)
                {
                    float k = period / (float)size;
                    sum += Value(x * k, y * k, z * k, period) * amp;
                    norm += amp; amp *= 0.5f;
                }
                byte v = (byte)Mathf.Clamp(Mathf.RoundToInt(sum / norm * 255f), 0, 255);
                px[(z * size + y) * size + x] = new Color32(v, v, v, 255);
            }
            var tex = new Texture3D(size, size, size, TextureFormat.RGBA32, false)
            { name = "Fog noise 3D", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        // ------------------------------------------------------------------ layered fallback

        static void BuildLayers(Transform parent, Rect box, Vector2 outer, Func<float, float, float> ground, bool reducedMotion)
        {
            var shader = Shader.Find("Nevishk/Fog");
            if (shader == null || !shader.isSupported)
            {
                Debug.LogWarning("[Nevishk] Fog shader Resources/Shaders/NevishkFog.shader missing or unsupported; no fog zone.");
                return;
            }
            var root = new GameObject("Fog Zone").transform;
            root.SetParent(parent, false);
            var noise = NoiseTexture(256);
            float speed = reducedMotion ? 0f : 1f;

            // The ring: a rounded rectangle a little outside the drawn map, so it stands in the empty steppe margin.
            var c = box.center;
            float hx = box.width / 2f + 90f, hz = box.height / 2f + 95f, corner = 160f;

            // Three walls, one behind the other, drifting in alternate directions: depth without volumetrics.
            Wall(root, shader, noise, speed, ground, c, hx, hz, corner, 0f, 110f, 0.70f, new Vector4(7f, 3f, -5f, 4f));
            Wall(root, shader, noise, speed, ground, c, hx, hz, corner, 45f, 135f, 0.60f, new Vector4(-6f, 2.5f, 4f, 3.5f));
            Wall(root, shader, noise, speed, ground, c, hx, hz, corner, 100f, 160f, 0.55f, new Vector4(5f, 2f, -3f, 3f));

            // Murk over the country: stacked sheets draped loosely over the land, clear inside the ring, thickening outward.
            var grid = GroundGrid(ground, outer, SheetCell);
            Sheet(root, shader, noise, speed, grid, c, hx, hz, corner, outer, 10f, 0.48f, new Vector4(6f, 2f, -4f, 3f));
            Sheet(root, shader, noise, speed, grid, c, hx, hz, corner, outer, 24f, 0.40f, new Vector4(-5f, 3f, 3f, -2f));
            Sheet(root, shader, noise, speed, grid, c, hx, hz, corner, outer, 42f, 0.32f, new Vector4(4f, -3f, -6f, 2f));
            Debug.Log($"[Nevishk] Fog zone: {root.childCount} layers around the city.");
        }

        const float SheetCell = 80f;

        /// Ground under the sheets: raw heights (for hiding fog that would be underground) and a broadly smoothed
        /// version the sheets follow, so they drape over the country without hugging every bump.
        static (float[] raw, float[] smooth, int nx, int nz) GroundGrid(Func<float, float, float> ground, Vector2 outer, float cell)
        {
            int nx = Mathf.CeilToInt(outer.x * 2f / cell) + 1, nz = Mathf.CeilToInt(outer.y * 2f / cell) + 1;
            var raw = new float[nx * nz];
            for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
                raw[j * nx + i] = ground(-outer.x + i * cell, -outer.y + j * cell);
            var a = (float[])raw.Clone(); var b = new float[a.Length];
            for (int pass = 0; pass < 4; pass++)
            {
                for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float sum = 0; int cnt = 0;
                    for (int dj = -2; dj <= 2; dj++)
                    for (int di = -2; di <= 2; di++)
                    {
                        int ii = i + di, jj = j + dj;
                        if (ii < 0 || jj < 0 || ii >= nx || jj >= nz) continue;
                        sum += a[jj * nx + ii]; cnt++;
                    }
                    b[j * nx + i] = sum / cnt;
                }
                (a, b) = (b, a);
            }
            return (raw, a, nx, nz);
        }

        // ------------------------------------------------------------------ geometry

        /// Signed distance from p to the rounded rectangle (negative inside).
        static float RoundedBoxDistance(Vector2 p, Vector2 c, float hx, float hz, float r)
        {
            float qx = Mathf.Abs(p.x - c.x) - (hx - r), qz = Mathf.Abs(p.y - c.y) - (hz - r);
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qz, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qz), 0f) - r;
        }

        /// Points round the rounded rectangle, about `step` metres apart, anticlockwise, with outward normals.
        static List<(Vector2 p, Vector2 n)> Ring(Vector2 c, float hx, float hz, float r, float step)
        {
            var corners = new[]
            {
                (new Vector2(hx - r, hz - r), 0f), (new Vector2(-(hx - r), hz - r), 90f),
                (new Vector2(-(hx - r), -(hz - r)), 180f), (new Vector2(hx - r, -(hz - r)), 270f)
            };
            var outline = new List<(Vector2 p, Vector2 n)>();
            foreach (var (cc, start) in corners)
                for (int a = 0; a <= 18; a++)
                {
                    float ang = (start + a * 5f) * Mathf.Deg2Rad;
                    var n = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    outline.Add((c + cc + n * r, n));
                }
            // Resample evenly (the straight sides are single long segments in `outline`).
            var pts = new List<(Vector2 p, Vector2 n)>();
            for (int i = 0; i < outline.Count; i++)
            {
                var (a, na) = outline[i];
                var (b, nb) = outline[(i + 1) % outline.Count];
                int k = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / step));
                for (int s = 0; s < k; s++)
                {
                    float t = s / (float)k;
                    pts.Add((Vector2.Lerp(a, b, t), Vector2.Lerp(na, nb, t).normalized));
                }
            }
            return pts;
        }

        static void Wall(Transform root, Shader shader, Texture2D noise, float speed, Func<float, float, float> ground,
                         Vector2 c, float hx, float hz, float r, float offset, float height, float density, Vector4 flow)
        {
            var ring = Ring(c, hx + offset, hz + offset, r + offset, 14f);
            const int rows = 10;
            // Ground along the ring, smoothed a little so the wall's foot doesn't jitter.
            var g = new float[ring.Count];
            for (int i = 0; i < ring.Count; i++) g[i] = ground(ring[i].p.x, ring[i].p.y);
            var gs = new float[g.Length];
            for (int i = 0; i < g.Length; i++)
            {
                float sum = 0; for (int k = -3; k <= 3; k++) sum += g[(i + k + g.Length) % g.Length];
                gs[i] = Mathf.Max(sum / 7f, g[i]);
            }
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var col = new List<Color>(); var tri = new List<int>();
            float along = 0f;
            for (int i = 0; i <= ring.Count; i++)
            {
                var (p, nrm) = ring[i % ring.Count];
                if (i > 0) along += Vector2.Distance(ring[i - 1].p, p);
                float foot = gs[i % ring.Count];
                for (int y = 0; y < rows; y++)
                {
                    // Rows bunched toward the ground, where the density changes fastest.
                    float t = y / (float)(rows - 1); t *= t;
                    float above = Mathf.Lerp(-8f, height, t);
                    float h = foot + above;
                    v.Add(new Vector3(p.x, h, p.y));
                    n.Add(new Vector3(nrm.x, 0f, nrm.y));
                    uv.Add(new Vector2(along, h));
                    // Fades in over the first metres above the ground (no hard line where it meets the land),
                    // dense low down, gone at the top.
                    float contact = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-2f, 14f, above));
                    float up = Mathf.Clamp01(above / height);
                    col.Add(new Color(1, 1, 1, contact * Mathf.Pow(1f - up, 1.3f)));
                }
                if (i == 0) continue;
                int a0 = (i - 1) * rows, b0 = i * rows;
                for (int y = 0; y < rows - 1; y++)
                {
                    tri.Add(a0 + y); tri.Add(b0 + y); tri.Add(b0 + y + 1);
                    tri.Add(a0 + y); tri.Add(b0 + y + 1); tri.Add(a0 + y + 1);
                }
            }
            Add(root, "Fog wall", v, n, uv, col, tri, Material(shader, noise, speed, density, flow, 0.0045f, 12f));
        }

        static void Sheet(Transform root, Shader shader, Texture2D noise, float speed, (float[] raw, float[] smooth, int nx, int nz) grid,
                          Vector2 c, float hx, float hz, float r, Vector2 outer, float lift, float density, Vector4 flow)
        {
            const float cell = SheetCell;
            int nx = grid.nx, nz = grid.nz;
            var v = new List<Vector3>(nx * nz); var n = new List<Vector3>(nx * nz); var uv = new List<Vector2>(nx * nz);
            var col = new List<Color>(nx * nz); var tri = new List<int>();
            var alpha = new float[nx * nz];
            for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                float x = -outer.x + i * cell, z = -outer.y + j * cell;
                float d = RoundedBoxDistance(new Vector2(x, z), c, hx, hz, r);
                // Clear inside the ring, thickening over the first few hundred metres, thinning at the far edge.
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(30f, 420f, d));
                float edge = Mathf.Max(Mathf.Abs(x) / outer.x, Mathf.Abs(z) / outer.y);
                a *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.82f, 1f, edge));
                // Drape over the land, but stop rising at 120 m so the high ridges stand out of the murk.
                int k = j * nx + i;
                float y = Mathf.Min(grid.smooth[k] + lift, 120f + lift * 0.5f);
                // Where the layer would run into a hillside, thin it out instead of cutting a hard line.
                a *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-4f, 10f, y - grid.raw[k]));
                alpha[k] = a;
                v.Add(new Vector3(x, y, z)); n.Add(Vector3.up); uv.Add(new Vector2(x, z)); col.Add(new Color(1, 1, 1, a));
            }
            for (int j = 0; j < nz - 1; j++)
            for (int i = 0; i < nx - 1; i++)
            {
                int a = j * nx + i, b = a + 1, cc = a + nx, d = cc + 1;
                if (alpha[a] + alpha[b] + alpha[cc] + alpha[d] < 0.004f) continue;   // the clear middle: no triangles
                tri.Add(a); tri.Add(cc); tri.Add(b);
                tri.Add(b); tri.Add(cc); tri.Add(d);
            }
            Add(root, "Fog layer", v, n, uv, col, tri, Material(shader, noise, speed, density, flow, 0.0022f, 4f));
        }

        static Material Material(Shader shader, Texture2D noise, float speed, float density, Vector4 flow, float scale, float wave)
        {
            var m = new Material(shader) { name = "Fog", renderQueue = (int)RenderQueue.Transparent - 50 };
            m.SetColor("_Color", new Color(MistColour.r, MistColour.g, MistColour.b, density));
            m.SetTexture("_NoiseTex", noise);
            m.SetFloat("_Scale", scale);
            m.SetVector("_Flow", flow);
            m.SetFloat("_Soft", 30f);
            m.SetVector("_NearFade", new Vector4(40f, 140f, 0f, 0f));
            m.SetFloat("_Wave", wave);
            m.SetFloat("_Speed", speed);
            return m;
        }

        static void Add(Transform root, string name, List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<Color> col, List<int> tri, Material m)
        {
            if (tri.Count == 0) return;
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetColors(col); mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            // The sway moves vertices up to ~12 m; pad the bounds so nothing is culled early.
            var b = mesh.bounds; b.Expand(40f); mesh.bounds = b;
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // ------------------------------------------------------------------ noise

        /// Tileable fractal value noise, so the mist never shows a seam as it drifts.
        static Texture2D NoiseTexture(int size)
        {
            var px = new Color32[size * size];
            var rng = new System.Random(77);
            var lattice = new float[64 * 64];
            for (int i = 0; i < lattice.Length; i++) lattice[i] = (float)rng.NextDouble();
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float sum = 0f, amp = 0.5f, norm = 0f;
                for (int period = 4; period <= 64; period *= 2)
                {
                    sum += Value(x * period / (float)size, y * period / (float)size, period, lattice) * amp;
                    norm += amp; amp *= 0.55f;
                }
                byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(sum / norm * 255f), 0, 255);
                px[y * size + x] = new Color32(b, b, b, 255);
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            { name = "Fog noise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        static float Value(float x, float y, int period, float[] lattice)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float L(int i, int j) => lattice[((j % period + period) % period) * 64 + ((i % period + period) % period)];
            return Mathf.Lerp(Mathf.Lerp(L(x0, y0), L(x0 + 1, y0), fx), Mathf.Lerp(L(x0, y0 + 1), L(x0 + 1, y0 + 1), fx), fy);
        }
    }
}
