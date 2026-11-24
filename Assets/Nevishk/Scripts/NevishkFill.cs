using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nevishk
{
    /// <summary>
    /// The everyday city between the landmarks: панельки with window grids, private houses with pitched roofs,
    /// rows of garages and, in Новый центр, glass high-rises. Each district has its own façade palette.
    /// Geometry is written straight into a few big meshes (one per material) with UVs in metres, so the façade
    /// textures tile per window and per floor whatever the building's size.
    /// </summary>
    public class NevishkFill
    {
        public enum Kind { Panel, Tower, House, Garages }

        const float Bay = 3.0f;          // one window bay, m
        const float Floor = 2.8f;        // one storey, m
        const int TileBays = 4;          // the façade texture holds 4 × 4 windows, so lit ones don't repeat every bay

        static Texture2D _facade, _garage;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _facade = null; _garage = null; }

        // Façade colours per district (multiplied with the near-white textures).
        static readonly Dictionary<string, Color[]> Palettes = new Dictionary<string, Color[]>
        {
            { "novye_kamyshi",      new[] { C(0.86f, 0.82f, 0.72f), C(0.80f, 0.84f, 0.78f), C(0.88f, 0.78f, 0.66f) } },
            { "krasnyi_elektrovoi", new[] { C(0.74f, 0.46f, 0.39f), C(0.80f, 0.62f, 0.52f), C(0.66f, 0.60f, 0.56f) } },
            { "baltag",             new[] { C(0.64f, 0.64f, 0.54f), C(0.71f, 0.69f, 0.60f), C(0.56f, 0.58f, 0.53f) } },
            { "mednye_luzhi",       new[] { C(0.80f, 0.62f, 0.45f), C(0.86f, 0.74f, 0.58f), C(0.72f, 0.58f, 0.48f) } },
            { "ushchelye",          new[] { C(0.60f, 0.64f, 0.62f), C(0.68f, 0.68f, 0.64f), C(0.56f, 0.58f, 0.56f) } },
            { "nevishk_tech",       new[] { C(0.70f, 0.80f, 0.88f), C(0.85f, 0.88f, 0.90f), C(0.62f, 0.72f, 0.80f) } },
            { "loom_of_echoes",     new[] { C(0.86f, 0.70f, 0.74f), C(0.82f, 0.78f, 0.80f), C(0.74f, 0.64f, 0.70f) } },
            { "novyi_tsentr",       new[] { C(0.80f, 0.82f, 0.84f), C(0.70f, 0.76f, 0.82f), C(0.86f, 0.84f, 0.80f) } },
            { "imperial",           new[] { C(0.90f, 0.84f, 0.66f), C(0.88f, 0.78f, 0.62f), C(0.84f, 0.82f, 0.74f) } },
        };
        static readonly Color[] RoofColours = { C(0.47f, 0.23f, 0.19f), C(0.36f, 0.39f, 0.41f), C(0.30f, 0.42f, 0.36f), C(0.52f, 0.36f, 0.24f) };
        static readonly Color GlassTint = C(0.55f, 0.64f, 0.72f);
        static readonly Color GarageTint = C(0.78f, 0.76f, 0.72f);
        static Color C(float r, float g, float b) => new Color(r, g, b);

        // One mesh builder per material key.
        class Builder
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> t = new List<int>();
            public Texture2D tex; public Color tint;
        }
        readonly Dictionary<string, Builder> _builders = new Dictionary<string, Builder>();
        readonly Dictionary<Color, List<CombineInstance>> _roofs = new Dictionary<Color, List<CombineInstance>>();

        Builder For(string key, Texture2D tex, Color tint)
        {
            if (!_builders.TryGetValue(key, out var b)) { b = new Builder { tex = tex, tint = tint }; _builders[key] = b; }
            return b;
        }

        /// Which kind of building a filler block becomes.
        public static Kind Classify(string district, float w, float d, float h, int seed)
        {
            if (h > 28f) return Kind.Tower;
            if (district == "novye_kamyshi") return Kind.House;
            if (h <= 7.5f)
            {
                bool industrial = district == "baltag" || district == "krasnyi_elektrovoi" || district == "nevishk_tech"
                               || district == "mednye_luzhi" || district == "imperial";
                return industrial && (seed % 2 == 0) ? Kind.Garages : Kind.House;
            }
            return Kind.Panel;
        }

        /// Adds one building standing on `ground` (its lowest corner), centred at `c` (XZ), footprint w × d, yaw degrees.
        public void Add(string district, Kind kind, Vector3 c, float ground, float w, float d, float h, float yaw, int seed)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var pal = Palettes.TryGetValue(district ?? "", out var p) ? p : new[] { NevishkTheme.Concrete };
            Color tint = pal[(seed / 3) % pal.Length];
            switch (kind)
            {
                case Kind.Panel:
                {
                    // Snap the height to whole storeys; a plinth floor is part of the first.
                    float floors = Mathf.Max(3, Mathf.Round(h / Floor));
                    float hh = floors * Floor + 0.6f;
                    var b = For("panel " + district + " " + tint, Facade, tint);
                    Box(b, c, ground, w, d, hh, rot, Bay * TileBays, Floor * TileBays, 0.6f);
                    // Lift machine room on the roof.
                    Box(b, c + rot * new Vector3(w * 0.15f, 0f, 0f), ground + hh, Mathf.Min(3.2f, w * 0.4f), Mathf.Min(2.6f, d * 0.4f), 2.4f, rot, 0f, 0f, 0f);
                    break;
                }
                case Kind.Tower:
                {
                    float floors = Mathf.Round(h / 3.4f);
                    float hh = floors * 3.4f;
                    var b = For("tower", Facade, GlassTint);
                    Box(b, c, ground, w, d, hh, rot, Bay * TileBays * 0.8f, 3.4f * TileBays, 0f);
                    // Setback crown.
                    Box(b, c, ground + hh, w * 0.7f, d * 0.7f, 4f, rot, Bay * TileBays * 0.8f, 3.4f * TileBays, 0f);
                    break;
                }
                case Kind.House:
                {
                    // A one- or two-storey house in a yard: a bit smaller than the block, with a gable roof.
                    float hw = w * 0.82f, hd = d * 0.82f;
                    float walls = h > 5.5f ? 5.8f : 3.4f;
                    var b = For("house " + district + " " + tint, Facade, tint);
                    Box(b, c, ground, hw, hd, walls, rot, Bay * TileBays * 0.9f, (walls > 4f ? 2.9f : 3.4f) * TileBays, 0f);
                    Color roof = RoofColours[(seed / 7) % RoofColours.Length];
                    if (!_roofs.TryGetValue(roof, out var list)) _roofs[roof] = list = new List<CombineInstance>();
                    // Ridge along the longer side.
                    var ridge = hw >= hd ? rot * Quaternion.Euler(0, 90, 0) : rot;
                    float span = Mathf.Min(hw, hd), len = Mathf.Max(hw, hd);
                    list.Add(new CombineInstance
                    {
                        mesh = NevishkBuildings.GableMesh,
                        transform = Matrix4x4.TRS(new Vector3(c.x, ground + walls, c.z), ridge, new Vector3(span + 0.7f, Mathf.Clamp(span * 0.45f, 1.6f, 3.2f), len + 0.7f))
                    });
                    break;
                }
                case Kind.Garages:
                {
                    // A garage cooperative: a low row of boxes, doors every 3 m.
                    var b = For("garages", Garage, GarageTint);
                    float len = Mathf.Max(w, d) * 1.1f, depth = Mathf.Clamp(Mathf.Min(w, d), 5f, 6.5f);
                    var r2 = w >= d ? rot : rot * Quaternion.Euler(0, 90, 0);
                    Box(b, c, ground, len, depth, 2.7f, r2, 3f, 2.7f, 0f);
                    break;
                }
            }
        }

        /// A box from `ground` up to `ground + h`. Wall UVs are in metres / (uRepeat, vRepeat) so façade textures tile
        /// by bay and floor; with uRepeat = 0 every face samples the texture's plain corner (roofs, machine rooms).
        /// `plinth` metres at the bottom also use the plain corner.
        static void Box(Builder b, Vector3 c, float ground, float w, float d, float h, Quaternion rot, float uRepeat, float vRepeat, float plinth)
        {
            float x = w / 2f, z = d / 2f;
            Vector3 P(float px, float py, float pz) => new Vector3(c.x, 0f, c.z) + rot * new Vector3(px, 0f, pz) + Vector3.up * py;
            Vector2 plain = new Vector2(0.01f, 0.01f);
            // Four walls: (start corner, end corner, outward normal, width)
            var walls = new (Vector3 a, Vector3 e, Vector3 nrm, float len)[]
            {
                (new Vector3(-x, 0, -z), new Vector3(x, 0, -z), Vector3.back, w),
                (new Vector3(x, 0, -z), new Vector3(x, 0, z), Vector3.right, d),
                (new Vector3(x, 0, z), new Vector3(-x, 0, z), Vector3.forward, w),
                (new Vector3(-x, 0, z), new Vector3(-x, 0, -z), Vector3.left, d),
            };
            float bottom = ground, top = ground + h, split = Mathf.Min(ground + plinth, top);
            foreach (var wl in walls)
            {
                Vector3 n = rot * wl.nrm;
                if (plinth > 0f) Quad(b, P(wl.a.x, bottom, wl.a.z), P(wl.e.x, bottom, wl.e.z), P(wl.e.x, split, wl.e.z), P(wl.a.x, split, wl.a.z), n, plain, plain, plain, plain);
                if (uRepeat <= 0f)
                {
                    Quad(b, P(wl.a.x, split, wl.a.z), P(wl.e.x, split, wl.e.z), P(wl.e.x, top, wl.e.z), P(wl.a.x, top, wl.a.z), n, plain, plain, plain, plain);
                    continue;
                }
                // Start each wall on a whole bay so windows line up at the corners; start floors at the plinth.
                float u1 = wl.len / uRepeat, v1 = (top - split) / vRepeat;
                Quad(b, P(wl.a.x, split, wl.a.z), P(wl.e.x, split, wl.e.z), P(wl.e.x, top, wl.e.z), P(wl.a.x, top, wl.a.z), n,
                     new Vector2(0, 0), new Vector2(u1, 0), new Vector2(u1, v1), new Vector2(0, v1));
            }
            // Roof (flat, plain).
            Quad(b, P(-x, top, -z), P(-x, top, z), P(x, top, z), P(x, top, -z), Vector3.up, plain, plain, plain, plain);
        }

        static void Quad(Builder b, Vector3 a, Vector3 c1, Vector3 c2, Vector3 c3, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int k = b.v.Count;
            b.v.Add(a); b.v.Add(c1); b.v.Add(c2); b.v.Add(c3);
            for (int i = 0; i < 4; i++) b.n.Add(n);
            b.uv.Add(ua); b.uv.Add(ub); b.uv.Add(uc); b.uv.Add(ud);
            // Wind so the face points along n. Unity's front face is the one whose normal is cross(b - a, c - a),
            // so (a, c1, c2) faces n exactly when that cross product points along n.
            if (Vector3.Dot(Vector3.Cross(c1 - a, c2 - a), n) >= 0f) { b.t.Add(k); b.t.Add(k + 1); b.t.Add(k + 2); b.t.Add(k); b.t.Add(k + 2); b.t.Add(k + 3); }
            else { b.t.Add(k); b.t.Add(k + 2); b.t.Add(k + 1); b.t.Add(k); b.t.Add(k + 3); b.t.Add(k + 2); }
        }

        /// Creates the meshes. `mat(colour)` gives a lit material for plain colours.
        public void Finish(Transform parent, System.Func<Color, Material> mat)
        {
            var root = new GameObject("City fill").transform;
            root.SetParent(parent, false);
            foreach (var kv in _builders)
            {
                var b = kv.Value;
                var mesh = new Mesh { name = kv.Key, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(b.v); mesh.SetNormals(b.n); mesh.SetUVs(0, b.uv); mesh.SetTriangles(b.t, 0);
                mesh.RecalculateBounds();
                var m = new Material(mat(b.tint)) { name = kv.Key };
                m.SetTexture("_BaseMap", b.tex); m.mainTexture = b.tex;
                m.SetFloat("_Smoothness", kv.Key == "tower" ? 0.6f : 0.12f);
                Add(root, kv.Key, mesh, m);
            }
            foreach (var kv in _roofs)
            {
                var mesh = new Mesh { name = "Roofs", indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                mesh.RecalculateBounds();
                Add(root, "Roofs", mesh, mat(kv.Key));
            }
        }

        static void Add(Transform root, string name, Mesh mesh, Material m)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        // ------------------------------------------------------------------ textures

        /// 4 × 4 windows: plain light panels with seams, dark glass, a few warm lit windows and curtains.
        /// The bottom-left pixel block is plain wall (roofs and plinths sample it).
        static Texture2D Facade
        {
            get
            {
                if (_facade != null) return _facade;
                const int cell = 32, size = cell * TileBays;
                var px = new Color32[size * size];
                var rng = new System.Random(4);
                Color wall = new Color(0.93f, 0.92f, 0.89f), seam = new Color(0.74f, 0.73f, 0.70f);
                Color glass = new Color(0.17f, 0.19f, 0.22f), lit = new Color(0.98f, 0.82f, 0.50f), curtain = new Color(0.55f, 0.47f, 0.42f);
                for (int fy = 0; fy < TileBays; fy++)
                for (int bx = 0; bx < TileBays; bx++)
                {
                    double roll = rng.NextDouble();
                    Color win = roll < 0.16 ? lit : roll < 0.30 ? curtain : glass;
                    bool balcony = rng.NextDouble() < 0.3;
                    for (int y = 0; y < cell; y++)
                    for (int x = 0; x < cell; x++)
                    {
                        Color col = wall;
                        if (y == 0 || x == 0) col = seam;                                    // panel joints
                        else if (x >= 9 && x <= 23 && y >= 10 && y <= 25) col = win;          // window
                        if (balcony && y >= 6 && y <= 9 && x >= 6 && x <= 26) col = seam;    // balcony slab
                        if (bx == 0 && fy == 0 && x < 4 && y < 4) col = wall;                 // the plain corner
                        px[(fy * cell + y) * size + bx * cell + x] = col;
                    }
                }
                _facade = Make("Façade", size, size, px);
                return _facade;
            }
        }

        /// One garage: rendered wall with a rusty metal door, ribbed.
        static Texture2D Garage
        {
            get
            {
                if (_garage != null) return _garage;
                const int w = 48, h = 32;
                var px = new Color32[w * h];
                Color wall = new Color(0.82f, 0.80f, 0.76f), door = new Color(0.48f, 0.36f, 0.30f), rib = new Color(0.40f, 0.30f, 0.26f);
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color col = wall;
                    if (x >= 6 && x <= 41 && y < 26) col = (x % 4 == 0) ? rib : door;
                    if (x < 4 && y < 4) col = wall;
                    px[y * w + x] = col;
                }
                _garage = Make("Garage", w, h, px);
                return _garage;
            }
        }

        static Texture2D Make(string name, int w, int h, Color32[] px)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }
    }
}
