using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nevishk
{
    /// <summary>
    /// The zone around Невишк: drifting fog walls that ring the city just outside its districts, and layers of
    /// murk lying over the countryside beyond them. Inside the ring the air stays clear, so every district reads;
    /// outside, the rest of the country sinks into mist with only hilltops and the eastern range showing.
    /// Uses the Nevishk/Fog shader (Resources/Shaders); does nothing if it's missing.
    /// </summary>
    public static class NevishkFog
    {
        public static readonly Color MistColour = new Color(0.34f, 0.36f, 0.35f);

        /// <param name="box">The drawn city (the map image's extent) in world XZ.</param>
        /// <param name="outer">How far out the countryside reaches (half extents), for the murk layers.</param>
        public static void Build(Transform parent, Rect box, Vector2 outer, bool reducedMotion)
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
            Wall(root, shader, noise, speed, c, hx, hz, corner, 0f, 120f, 0.62f, new Vector4(7f, 3f, -5f, 4f));
            Wall(root, shader, noise, speed, c, hx, hz, corner, 45f, 145f, 0.52f, new Vector4(-6f, 2.5f, 4f, 3.5f));
            Wall(root, shader, noise, speed, c, hx, hz, corner, 100f, 175f, 0.48f, new Vector4(5f, 2f, -3f, 3f));

            // Murk over the country: stacked sheets, clear inside the ring, thickening outward.
            Sheet(root, shader, noise, speed, c, hx, hz, corner, outer, 14f, 0.40f, new Vector4(6f, 2f, -4f, 3f));
            Sheet(root, shader, noise, speed, c, hx, hz, corner, outer, 32f, 0.34f, new Vector4(-5f, 3f, 3f, -2f));
            Sheet(root, shader, noise, speed, c, hx, hz, corner, outer, 56f, 0.28f, new Vector4(4f, -3f, -6f, 2f));
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

        static void Wall(Transform root, Shader shader, Texture2D noise, float speed, Vector2 c, float hx, float hz, float r,
                         float offset, float height, float density, Vector4 flow)
        {
            var ring = Ring(c, hx + offset, hz + offset, r + offset, 14f);
            const int rows = 8;
            const float bottom = -30f;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var col = new List<Color>(); var tri = new List<int>();
            float along = 0f;
            for (int i = 0; i <= ring.Count; i++)
            {
                var (p, nrm) = ring[i % ring.Count];
                if (i > 0) along += Vector2.Distance(ring[i - 1].p, p);
                for (int y = 0; y < rows; y++)
                {
                    float t = y / (float)(rows - 1);
                    float h = Mathf.Lerp(bottom, height, t);
                    v.Add(new Vector3(p.x, h, p.y));
                    n.Add(new Vector3(nrm.x, 0f, nrm.y));
                    uv.Add(new Vector2(along, h));
                    float up = Mathf.Clamp01(h / height);
                    col.Add(new Color(1, 1, 1, Mathf.Pow(1f - up, 1.4f)));   // dense at the foot, gone at the top
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

        static void Sheet(Transform root, Shader shader, Texture2D noise, float speed, Vector2 c, float hx, float hz, float r,
                          Vector2 outer, float y, float density, Vector4 flow)
        {
            const float cell = 110f;
            int nx = Mathf.CeilToInt(outer.x * 2f / cell) + 1, nz = Mathf.CeilToInt(outer.y * 2f / cell) + 1;
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
                alpha[j * nx + i] = a;
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
