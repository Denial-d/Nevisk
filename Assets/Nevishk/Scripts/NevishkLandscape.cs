using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nevishk
{
    /// <summary>
    /// The rest of the country around the city: a coarse procedural landscape that continues the detailed
    /// terrain to the horizon — rolling steppe and fields, woods, the eastern mountain range, the Мырла and
    /// its branches flowing on from where they leave the city, scattered villages and the roads between them.
    /// Nothing here is named: villages and towns are left for the lore to define.
    /// </summary>
    public class NevishkLandscape
    {
        // Extent of the outer landscape (world units, city at the origin) and its grid spacing.
        public float HalfX = 4200f, HalfZ = 3800f, Cell = 25f;
        const int TexSize = 1024;
        const int Seed = 1984;

        readonly NevishkTerrain _inner;
        readonly Rect _rect;                 // the detailed terrain's extent
        readonly System.Random _rng = new System.Random(Seed);
        int _nx, _nz;
        float[] _h, _riverD, _riverW;        // per grid vertex: height, distance to the nearest river, its half-width
        readonly List<List<Vector3>> _rivers = new List<List<Vector3>>();
        readonly List<Vector3> _villages = new List<Vector3>();
        readonly List<(Vector2 a, Vector2 b)> _roads = new List<(Vector2, Vector2)>();

        static readonly Color Steppe   = new Color(0.45f, 0.43f, 0.31f);   // = inner steppe #7D7A5C × TerrainTint, so the seam disappears
        static readonly Color Rock     = new Color(0.47f, 0.45f, 0.42f);
        static readonly Color Forest   = new Color(0.20f, 0.27f, 0.18f);
        static readonly Color Mud      = new Color(0.23f, 0.23f, 0.20f);
        static readonly Color Road     = new Color(0.64f, 0.60f, 0.51f);
        static readonly Color[] Fields =
        {
            new Color(0.71f, 0.64f, 0.42f),   // wheat stubble
            new Color(0.44f, 0.49f, 0.28f),   // green crop
            new Color(0.42f, 0.35f, 0.27f),   // ploughed
            new Color(0.55f, 0.53f, 0.38f),   // fallow
            new Color(0.62f, 0.57f, 0.30f),   // sunflower / maize
            new Color(0.38f, 0.44f, 0.30f),   // pasture
        };

        public NevishkLandscape(NevishkTerrain inner)
        {
            _inner = inner;
            _rect = inner.WorldRect;
        }

        // ------------------------------------------------------------------ build

        public void Build(Transform parent, Func<Color, float, Material> mat, Material groundTemplate, List<RiverStart> riverStarts)
        {
            var root = new GameObject("Countryside").transform;
            root.SetParent(parent, false);

            _nx = Mathf.CeilToInt(HalfX * 2 / Cell) + 1;
            _nz = Mathf.CeilToInt(HalfZ * 2 / Cell) + 1;
            TraceRivers(riverStarts);
            ComputeHeights();
            PlaceVillages();

            var tex = PaintTexture();
            var groundMat = groundTemplate != null ? new Material(groundTemplate) : mat(Color.white, 0.03f);
            groundMat = new Material(groundMat);
            groundMat.SetTexture("_BaseMap", tex); groundMat.mainTexture = tex;
            groundMat.SetColor("_BaseColor", Color.white);
            groundMat.SetFloat("_Smoothness", 0.03f);
            foreach (var m in GroundTiles(80)) AddMesh(root, m.name, m, groundMat);

            var water = mat(NevishkTheme.Water, 0.9f);
            foreach (var r in _rivers) AddMesh(root, "River", RiverMesh(r), water);

            BuildTrees(root, mat(new Color(0.20f, 0.30f, 0.20f), 0.05f));
            BuildVillageHouses(root, mat);
        }

        public struct RiverStart { public Vector3 pos; public Vector3 dir; public float width; }

        // ------------------------------------------------------------------ terrain

        float X(int i) => -HalfX + i * Cell;
        float Z(int j) => HalfZ - j * Cell;

        float DistOutside(float x, float z)
        {
            float dx = Mathf.Max(_rect.xMin - x, 0f, x - _rect.xMax);
            float dz = Mathf.Max(_rect.yMin - z, 0f, z - _rect.yMax);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        float EdgeHeight(float x, float z)
        {
            var p = new Vector3(Mathf.Clamp(x, _rect.xMin + 1f, _rect.xMax - 1f), 0f, Mathf.Clamp(z, _rect.yMin + 1f, _rect.yMax - 1f));
            return _inner.HeightAtWorld(p);
        }

        float LowestInner(float x, float z)
        {
            float lo = float.MaxValue, r = Cell;
            for (int dz = -2; dz <= 2; dz++)
            for (int dx = -2; dx <= 2; dx++)
                lo = Mathf.Min(lo, EdgeHeight(x + dx * r * 0.5f, z + dz * r * 0.5f));
            return lo;
        }

        static float Fbm(float x, float z, int oct)
        {
            float sum = 0, amp = 1, norm = 0, f = 1;
            for (int o = 0; o < oct; o++) { sum += Mathf.PerlinNoise(x * f + 31.7f * o, z * f - 12.9f * o) * amp; norm += amp; amp *= 0.5f; f *= 2.03f; }
            return sum / norm;   // ~0..1, centred near 0.5
        }

        /// The country's shape, far from the city.
        static float Procedural(float x, float z)
        {
            float hills = 10f + 55f * (Fbm(x * 0.0011f + 100f, z * 0.0011f + 100f, 5) - 0.32f);
            // The eastern range continues from the mountains drawn on the map.
            float east = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(450f, 1100f, x));
            float ridged = 1f - Mathf.Abs(2f * Fbm(x * 0.0028f, z * 0.0028f, 4) - 1f);
            float range = east * 165f * (0.35f + 0.65f * ridged) * (0.75f + 0.25f * Mathf.Sin(z * 0.0012f));
            // Distant uplands all round, so the horizon has a silhouette.
            float r = Mathf.Sqrt(x * x * 0.8f + z * z);
            float far = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1800f, 3600f, r)) * 85f * Fbm(x * 0.0018f + 7f, z * 0.0018f - 3f, 4);
            return Mathf.Max(hills, 2f) + range + far;
        }

        void ComputeHeights()
        {
            int n = _nx * _nz;
            _h = new float[n];
            for (int j = 0; j < _nz; j++)
            for (int i = 0; i < _nx; i++)
            {
                float x = X(i), z = Z(j), d = DistOutside(x, z);
                int k = j * _nx + i;
                float edge = EdgeHeight(x, z);
                if (d <= 0f)
                {
                    // Under the detailed terrain. Near its edge, sit just below it (lowest point around this
                    // vertex), so the cell straddling the edge doesn't dip into a visible trench; deeper in, drop away.
                    float inside = Mathf.Min(Mathf.Min(x - _rect.xMin, _rect.xMax - x), Mathf.Min(z - _rect.yMin, _rect.yMax - z));
                    _h[k] = inside < Cell * 2f ? LowestInner(x, z) - 0.5f : edge - 4f;
                    continue;
                }
                float t = Mathf.SmoothStep(0f, 1f, d / 520f);
                float h = Mathf.Lerp(edge, Procedural(x, z), t);
                // Carve river valleys.
                float rd = _riverD[k], rw = _riverW[k];
                if (rd < rw + 160f)
                {
                    float bed = -4.5f;
                    float v = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((rd - rw) / 160f));
                    h = Mathf.Lerp(bed, h, rd < rw ? 0f : v);
                }
                _h[k] = h;
            }
        }

        public float HeightAt(float x, float z)
        {
            float gx = Mathf.Clamp((x + HalfX) / Cell, 0, _nx - 1.001f), gz = Mathf.Clamp((HalfZ - z) / Cell, 0, _nz - 1.001f);
            int i = (int)gx, j = (int)gz; float fx = gx - i, fz = gz - j;
            float a = _h[j * _nx + i], b = _h[j * _nx + i + 1], c = _h[(j + 1) * _nx + i], d = _h[(j + 1) * _nx + i + 1];
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
        }

        float RiverDistAt(float x, float z)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt((x + HalfX) / Cell), 0, _nx - 1), j = Mathf.Clamp(Mathf.RoundToInt((HalfZ - z) / Cell), 0, _nz - 1);
            return _riverD[j * _nx + i] - _riverW[j * _nx + i];
        }

        float Slope(float x, float z)
        {
            float e = Cell;
            return new Vector2(HeightAt(x + e, z) - HeightAt(x - e, z), HeightAt(x, z + e) - HeightAt(x, z - e)).magnitude / (2 * e);
        }

        // ------------------------------------------------------------------ rivers

        void TraceRivers(List<RiverStart> starts)
        {
            int n = _nx * _nz;
            _riverD = new float[n]; _riverW = new float[n];
            for (int k = 0; k < n; k++) { _riverD[k] = 1e9f; _riverW[k] = 0f; }
            foreach (var s in starts)
            {
                var pts = new List<Vector3>();
                Vector3 p = s.pos - s.dir * 12f;   // start a little inside the detailed terrain so the water joins up
                float baseAng = Mathf.Atan2(s.dir.z, s.dir.x), ang = baseAng;
                while (Mathf.Abs(p.x) < HalfX + 200f && Mathf.Abs(p.z) < HalfZ + 200f)
                {
                    pts.Add(p);
                    ang += (float)(_rng.NextDouble() - 0.5) * 0.35f + (baseAng - ang) * 0.06f;   // meander
                    p += new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 22f;
                }
                _rivers.Add(pts);
                // Rasterise distance to this river onto the grid (bounding box per segment).
                float along = 0f;
                for (int q = 0; q + 1 < pts.Count; q++)
                {
                    Vector3 a = pts[q], b = pts[q + 1];
                    float halfW = s.width * 0.5f + 10f * Mathf.Clamp01(along / 1800f);   // widens downstream
                    along += 22f;
                    float reach = halfW + 170f;
                    int i0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach + HalfX) / Cell));
                    int i1 = Mathf.Min(_nx - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + reach + HalfX) / Cell));
                    int j0 = Mathf.Max(0, Mathf.FloorToInt((HalfZ - Mathf.Max(a.z, b.z) - reach) / Cell));
                    int j1 = Mathf.Min(_nz - 1, Mathf.CeilToInt((HalfZ - Mathf.Min(a.z, b.z) + reach) / Cell));
                    Vector2 A = new Vector2(a.x, a.z), AB = new Vector2(b.x - a.x, b.z - a.z);
                    for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                    {
                        var P = new Vector2(X(i), Z(j));
                        float t = Mathf.Clamp01(Vector2.Dot(P - A, AB) / AB.sqrMagnitude);
                        float d = Vector2.Distance(P, A + AB * t);
                        int k = j * _nx + i;
                        if (d - halfW < _riverD[k] - _riverW[k]) { _riverD[k] = d; _riverW[k] = halfW; }
                    }
                }
            }
        }

        Mesh RiverMesh(List<Vector3> pts)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            float along = 0f;
            for (int q = 0; q < pts.Count; q++)
            {
                Vector3 a = pts[Mathf.Max(q - 1, 0)], b = pts[Mathf.Min(q + 1, pts.Count - 1)];
                Vector3 dir = (b - a); dir.y = 0; dir.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, dir);
                float halfW = 7f + 10f * Mathf.Clamp01(along / 1800f) + 4f;   // a little wider than the carved bed
                along += 22f;
                Vector3 c = new Vector3(pts[q].x, 0f, pts[q].z);
                v.Add(c - right * halfW); v.Add(c + right * halfW);
                if (q > 0)
                {
                    int i = v.Count - 4;
                    // left(a)→right(b) across, so the face points up: (a0, a1, b1), (a0, b1, b0)
                    t.Add(i); t.Add(i + 2); t.Add(i + 3);
                    t.Add(i); t.Add(i + 3); t.Add(i + 1);
                }
            }
            var m = new Mesh { name = "River", indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ villages and roads

        void PlaceVillages()
        {
            var cands = new List<Vector3>();
            for (int tries = 0; tries < 6000 && cands.Count < 42; tries++)
            {
                float x = (float)(_rng.NextDouble() * 2 - 1) * (HalfX - 300f);
                float z = (float)(_rng.NextDouble() * 2 - 1) * (HalfZ - 300f);
                float d = DistOutside(x, z);
                if (d < 220f || d > 3200f) continue;
                float h = HeightAt(x, z);
                if (h > 70f || h < 1f || Slope(x, z) > 0.12f || IsForest(x, z) || RiverDistAt(x, z) < 50f) continue;
                bool spaced = true;
                foreach (var c in cands) if ((new Vector2(c.x - x, c.z - z)).sqrMagnitude < 300f * 300f) { spaced = false; break; }
                if (!spaced) continue;
                cands.Add(new Vector3(x, h, z));
            }
            _villages.AddRange(cands);

            // Road network: each village joins the nearest place closer to the city (another village or the city edge).
            foreach (var v in _villages)
            {
                Vector2 pv = new Vector2(v.x, v.z);
                float myD = DistOutside(v.x, v.z);
                Vector2 target = new Vector2(Mathf.Clamp(v.x, _rect.xMin, _rect.xMax), Mathf.Clamp(v.z, _rect.yMin, _rect.yMax));
                float best = myD;
                foreach (var o in _villages)
                {
                    float od = DistOutside(o.x, o.z);
                    if (od >= myD) continue;
                    float dist = Vector2.Distance(pv, new Vector2(o.x, o.z));
                    if (dist < best) { best = dist; target = new Vector2(o.x, o.z); }
                }
                _roads.Add((pv, target));
            }
        }

        bool IsForest(float x, float z) => Mathf.PerlinNoise(x * 0.0026f + 50f, z * 0.0026f + 50f) > 0.64f && DistOutside(x, z) > 120f;

        void BuildVillageHouses(Transform parent, Func<Color, float, Material> mat)
        {
            var walls = new List<CombineInstance>(); var roofs = new List<CombineInstance>(); var spires = new List<CombineInstance>();
            Mesh cube = NevishkBuildings.CubeMesh, gable = NevishkBuildings.GableMesh, cone = NevishkBuildings.SpireMesh;
            for (int vi = 0; vi < _villages.Count; vi++)
            {
                var v = _villages[vi];
                bool town = vi < 3;                                  // the first few, nearest the city, are larger
                int houses = town ? 34 : 6 + _rng.Next(12);
                float spread = town ? 95f : 55f;
                for (int hI = 0; hI < houses; hI++)
                {
                    float a = (float)_rng.NextDouble() * Mathf.PI * 2, r = Mathf.Sqrt((float)_rng.NextDouble()) * spread;
                    float x = v.x + Mathf.Cos(a) * r, z = v.z + Mathf.Sin(a) * r;
                    if (RiverDistAt(x, z) < 15f) continue;
                    float yaw = (float)(_rng.Next(4) * 90 + _rng.NextDouble() * 12 - 6);
                    bool block = town && hI % 6 == 0;
                    float w = block ? 16f : 7f + (float)_rng.NextDouble() * 4f, d = block ? 9f : 5f + (float)_rng.NextDouble() * 2.5f;
                    float hh = block ? 14f : 4.5f + (float)_rng.NextDouble();
                    float g = HeightAt(x, z) - 0.6f;
                    var rot = Quaternion.Euler(0, yaw, 0);
                    walls.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(new Vector3(x, g + hh / 2, z), rot, new Vector3(w, hh, d)) });
                    if (!block)
                        roofs.Add(new CombineInstance { mesh = gable, transform = Matrix4x4.TRS(new Vector3(x, g + hh, z), rot * Quaternion.Euler(0, 90, 0), new Vector3(d + 0.8f, 3f, w + 0.8f)) });
                }
                if (town || vi % 3 == 0)
                {
                    // Church: nave and a spire.
                    float g = HeightAt(v.x, v.z) - 0.6f;
                    walls.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(new Vector3(v.x, g + 4f, v.z), Quaternion.identity, new Vector3(7f, 8f, 13f)) });
                    walls.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(new Vector3(v.x, g + 8f, v.z - 7f), Quaternion.identity, new Vector3(4f, 16f, 4f)) });
                    spires.Add(new CombineInstance { mesh = cone, transform = Matrix4x4.TRS(new Vector3(v.x, g + 16f, v.z - 7f), Quaternion.identity, new Vector3(3.6f, 8f, 3.6f)) });
                }
            }
            AddCombined(parent, "Village walls", walls, mat(new Color(0.78f, 0.75f, 0.68f), 0.05f));
            AddCombined(parent, "Village roofs", roofs, mat(new Color(0.42f, 0.24f, 0.19f), 0.05f));
            AddCombined(parent, "Village spires", spires, mat(new Color(0.36f, 0.45f, 0.40f), 0.3f));
        }

        void BuildTrees(Transform parent, Material m)
        {
            var parts = new List<CombineInstance>();
            Mesh cone = NevishkBuildings.SpireMesh;
            for (int tries = 0; tries < 30000 && parts.Count < 5000; tries++)
            {
                float x = (float)(_rng.NextDouble() * 2 - 1) * HalfX, z = (float)(_rng.NextDouble() * 2 - 1) * HalfZ;
                if (!IsForest(x, z)) continue;
                float h = HeightAt(x, z);
                if (h > 95f || RiverDistAt(x, z) < 20f) continue;
                float s = 0.8f + (float)_rng.NextDouble() * 0.7f;
                parts.Add(new CombineInstance { mesh = cone, transform = Matrix4x4.TRS(new Vector3(x, h - 0.5f, z), Quaternion.Euler(0, (float)_rng.NextDouble() * 360f, 0), new Vector3(8f * s, 18f * s, 8f * s)) });
            }
            AddCombined(parent, "Woods", parts, m);
        }

        // ------------------------------------------------------------------ ground texture

        Texture2D PaintTexture()
        {
            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, anisoLevel = 4, name = "Countryside" };
            var px = new Color32[TexSize * TexSize];
            for (int y = 0; y < TexSize; y++)
            for (int x = 0; x < TexSize; x++)
            {
                float wx = -HalfX + (x + 0.5f) / TexSize * HalfX * 2;
                float wz = -HalfZ + (y + 0.5f) / TexSize * HalfZ * 2;
                px[y * TexSize + x] = GroundColour(wx, wz);
            }
            // Country roads.
            foreach (var (a, b) in _roads) PaintLine(px, a, b, Road, 1.6f, true);
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        Color GroundColour(float x, float z)
        {
            float d = DistOutside(x, z);
            float h = HeightAt(x, z);
            // Field patchwork in a slowly turning grid.
            float ang = (Mathf.PerlinNoise(x * 0.0004f + 9f, z * 0.0004f + 9f) - 0.5f) * 1.6f;
            float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
            float fu = (x * cs - z * sn) / 85f, fv = (x * sn + z * cs) / 130f;
            int cu = Mathf.FloorToInt(fu), cv = Mathf.FloorToInt(fv);
            uint hash = (uint)(cu * 73856093 ^ cv * 19349663 ^ Seed);
            Color field = Fields[hash % (uint)Fields.Length];
            float edge = Mathf.Min(Mathf.Min(fu - cu, 1 - (fu - cu)), Mathf.Min(fv - cv, 1 - (fv - cv)));
            if (edge < 0.05f) field = Color.Lerp(field, new Color(0.30f, 0.33f, 0.24f), 0.6f);   // hedgerows

            // Near the city the farmland peters out field by field into the steppe the detailed terrain is
            // painted with: each field has its own threshold, and the reach wobbles, so there is no straight band.
            float reach = d + (Mathf.PerlinNoise(x * 0.004f + 3f, z * 0.004f - 5f) - 0.5f) * 260f;
            float t = Mathf.InverseLerp(40f, 520f, reach);
            float thr = 0.15f + 0.8f * ((hash >> 8) % 1000u) / 1000f;
            float farmed = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(thr - 0.06f, thr + 0.06f, t));
            // The steppe itself gets the same faint mottling as the detailed terrain, growing in from its edge.
            float mottle = (Mathf.PerlinNoise(x * 0.035f + 11f, z * 0.035f) - 0.5f) * 0.10f * Mathf.Clamp01(d / 60f);
            Color steppe = Steppe * (1f + mottle);
            Color c = Color.Lerp(steppe, field, farmed);
            c *= 0.92f + 0.16f * Mathf.PerlinNoise(x * 0.02f, z * 0.02f) * Mathf.Clamp01(d / 120f) + 0.08f * (1f - Mathf.Clamp01(d / 120f));
            if (IsForest(x, z)) c = Color.Lerp(c, Forest, Mathf.Clamp01((d - 120f) / 150f));
            // Mountains and steep ground go to rock.
            float rock = Mathf.Max(Mathf.InverseLerp(65f, 110f, h), Mathf.InverseLerp(0.35f, 0.7f, Slope(x, z)));
            c = Color.Lerp(c, Rock, rock);
            if (RiverDistAt(x, z) < 12f) c = Mud;
            c.a = 1f;
            return c;
        }

        void PaintLine(Color32[] px, Vector2 a, Vector2 b, Color col, float widthPx, bool fadeNearCity = false)
        {
            Vector2 A = ToTex(a), B = ToTex(b);
            int steps = Mathf.CeilToInt(Vector2.Distance(A, B) * 2f);
            for (int s = 0; s <= steps; s++)
            {
                // A gentle wobble so roads aren't ruler-straight.
                float t = s / (float)Mathf.Max(steps, 1);
                Vector2 p = Vector2.Lerp(A, B, t) + Vector2.Perpendicular((B - A).normalized) * Mathf.Sin(t * 9f + A.x) * 6f * Mathf.Sin(t * Mathf.PI);
                // Dirt tracks thin out and vanish into the steppe before they reach the detailed terrain.
                float strength = 1f;
                if (fadeNearCity)
                {
                    Vector2 w = Vector2.Lerp(a, b, t);
                    strength = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(30f, 260f, DistOutside(w.x, w.y)));
                    if (strength <= 0.01f) continue;
                }
                int r = Mathf.CeilToInt(widthPx);
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = Mathf.RoundToInt(p.x) + dx, y = Mathf.RoundToInt(p.y) + dy;
                    if (x < 0 || y < 0 || x >= TexSize || y >= TexSize || dx * dx + dy * dy > widthPx * widthPx) continue;
                    px[y * TexSize + x] = strength >= 0.99f ? (Color32)col : (Color32)Color.Lerp(px[y * TexSize + x], col, strength);
                }
            }
        }

        Vector2 ToTex(Vector2 w) => new Vector2((w.x + HalfX) / (HalfX * 2) * TexSize, (w.y + HalfZ) / (HalfZ * 2) * TexSize);

        // ------------------------------------------------------------------ meshes

        List<Mesh> GroundTiles(int tile)
        {
            var list = new List<Mesh>();
            for (int ty = 0; ty < _nz - 1; ty += tile)
            for (int tx = 0; tx < _nx - 1; tx += tile)
            {
                int w = Mathf.Min(tile, _nx - 1 - tx) + 1, hgt = Mathf.Min(tile, _nz - 1 - ty) + 1;
                var v = new Vector3[w * hgt]; var uv = new Vector2[w * hgt]; var nrm = new Vector3[w * hgt];
                for (int y = 0; y < hgt; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = tx + x, j = ty + y;
                    v[y * w + x] = new Vector3(X(i), _h[j * _nx + i], Z(j));
                    uv[y * w + x] = new Vector2(i / (float)(_nx - 1), 1f - j / (float)(_nz - 1));
                    // Central differences on the whole grid, so neighbouring tiles light identically at their seams.
                    float hl = _h[j * _nx + Mathf.Max(i - 1, 0)], hr = _h[j * _nx + Mathf.Min(i + 1, _nx - 1)];
                    float hn = _h[Mathf.Max(j - 1, 0) * _nx + i], hs = _h[Mathf.Min(j + 1, _nz - 1) * _nx + i];
                    nrm[y * w + x] = new Vector3(hl - hr, 2f * Cell, hs - hn).normalized;   // z decreases with j
                }
                var t = new int[(w - 1) * (hgt - 1) * 6]; int q = 0;
                for (int y = 0; y < hgt - 1; y++)
                for (int x = 0; x < w - 1; x++)
                {
                    int a = y * w + x, b = a + 1, c = a + w, d = c + 1;   // a=NW b=NE c=SW d=SE
                    t[q++] = a; t[q++] = b; t[q++] = c; t[q++] = b; t[q++] = d; t[q++] = c;
                }
                var m = new Mesh { name = $"Countryside {tx},{ty}" };
                m.vertices = v; m.uv = uv; m.triangles = t; m.normals = nrm;
                m.RecalculateBounds();
                list.Add(m);
            }
            return list;
        }

        static void AddMesh(Transform parent, string name, Mesh mesh, Material mat)
        {
            if (mesh.vertexCount == 0) return;
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;   // far away: no shadow cost
        }

        static void AddCombined(Transform parent, string name, List<CombineInstance> parts, Material m)
        {
            if (parts.Count == 0) return;
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(parts.ToArray(), true, true);
            mesh.RecalculateBounds();
            AddMesh(parent, name, mesh, m);
        }
    }
}
