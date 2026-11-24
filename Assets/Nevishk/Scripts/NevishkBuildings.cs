using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nevishk
{
    /// <summary>
    /// Low-poly 3D versions of the buildings drawn on the base map (factories, manor, chapel, …),
    /// assembled from boxes, gable roofs, cylinders and spires. One builder per drawn icon style.
    /// Sizes are in world units (1 unit = 1 base-map pixel); the footprint comes from the drawn icon.
    /// </summary>
    public static class NevishkBuildings
    {
        // Palette: muted, so the buildings sit in the dusk grade with the rest of the city.
        static readonly Color Brick     = new Color(0.55f, 0.36f, 0.30f);
        static readonly Color Concrete  = new Color(0.62f, 0.60f, 0.56f);
        static readonly Color DarkConc  = new Color(0.40f, 0.39f, 0.37f);
        static readonly Color RoofDark  = new Color(0.25f, 0.24f, 0.23f);
        static readonly Color RoofRed   = new Color(0.45f, 0.23f, 0.18f);
        static readonly Color Whitewash = new Color(0.82f, 0.79f, 0.72f);
        static readonly Color Cream     = new Color(0.86f, 0.82f, 0.70f);
        static readonly Color Patina    = new Color(0.36f, 0.45f, 0.40f);
        static readonly Color Glass     = new Color(0.40f, 0.48f, 0.55f);
        static readonly Color Rail      = new Color(0.18f, 0.17f, 0.16f);
        static readonly Color Gilt      = new Color(0.85f, 0.70f, 0.30f);

        public struct Result { public GameObject root; public float height; public float radius; }

        /// <param name="mat">Returns a shared material for a colour and smoothness.</param>
        public static Result Build(string type, Transform parent, Vector3 basePos, float w, float d, float yaw,
                                   Func<Color, float, Material> mat, int seed)
        {
            var root = new GameObject("Building · " + type).transform;
            root.SetParent(parent, false);
            root.position = basePos;
            root.rotation = Quaternion.Euler(0f, yaw, 0f);
            var kit = new Kit(root, mat, new System.Random(seed));
            float h = type switch
            {
                "factory"  => Factory(kit, w, d),
                "houses"   => Houses(kit, w, d),
                "manor"    => Manor(kit, w, d),
                "pair"     => Pair(kit, w, d),
                "depot"    => Depot(kit, w, d),
                "slabs"    => Slabs(kit, w, d),
                "ruin"     => Ruin(kit, w, d),
                "shed"     => Shed(kit, w, d),
                "spillway" => Spillway(kit, w, d),
                "gallery"  => Gallery(kit, w, d),
                "towers"   => Towers(kit, w, d),
                "chapel"   => Chapel(kit, w, d),
                _          => kit.Box(Vector3.zero, new Vector3(w, 8, d), Concrete),
            };
            return new Result { root = root.gameObject, height = h, radius = Mathf.Max(w, d) * 0.6f };
        }

        // ------------------------------------------------------------------ building types
        // Each returns the building's top height above its base.

        static float Factory(Kit k, float w, float d)
        {
            float hallW = w * 0.68f, hallH = 9f;
            k.Box(new Vector3(-w * 0.16f, 0, 0), new Vector3(hallW, hallH, d), Brick);
            // Saw-tooth roof: a row of small gables across the hall.
            int teeth = Mathf.Max(3, Mathf.RoundToInt(hallW / 6f));
            float tw = hallW / teeth;
            for (int i = 0; i < teeth; i++)
                k.Gable(new Vector3(-w * 0.16f - hallW / 2 + tw * (i + 0.5f), hallH, 0), new Vector3(tw, 3.2f, d), RoofDark, alongZ: true);
            k.Box(new Vector3(w * 0.34f, 0, -d * 0.15f), new Vector3(w * 0.3f, 6f, d * 0.6f), Concrete);
            k.Box(new Vector3(w * 0.34f, 6f, -d * 0.15f), new Vector3(w * 0.32f, 0.6f, d * 0.64f), RoofDark);
            // Chimneys, like on the drawing.
            k.Cylinder(new Vector3(w * 0.28f, 0, d * 0.28f), 1.5f, 26f, Brick);
            k.Cylinder(new Vector3(w * 0.40f, 0, d * 0.28f), 1.2f, 21f, Brick);
            return 26f;
        }

        static float Houses(Kit k, float w, float d)
        {
            // Village of small whitewashed houses with red roofs.
            var spots = new List<Vector2>();
            for (int tries = 0; spots.Count < 6 && tries < 60; tries++)
            {
                var p = new Vector2(k.Range(-w / 2 + 3, w / 2 - 3), k.Range(-d / 2 + 3, d / 2 - 3));
                bool clear = true;
                foreach (var q in spots) if ((p - q).sqrMagnitude < 36f) clear = false;
                if (clear) spots.Add(p);
            }
            foreach (var p in spots)
            {
                float hw = k.Range(4f, 6f), hd = k.Range(3.5f, 4.5f), hh = k.Range(3f, 4f);
                bool turn = k.Range(0, 1) > 0.5f;
                var size = turn ? new Vector3(hd, hh, hw) : new Vector3(hw, hh, hd);
                k.Box(new Vector3(p.x, 0, p.y), size, Whitewash);
                k.Gable(new Vector3(p.x, hh, p.y), new Vector3(size.x + 0.6f, 2.4f, size.z + 0.6f), RoofRed, alongZ: turn);
            }
            return 7f;
        }

        static float Manor(Kit k, float w, float d)
        {
            float cw = w * 0.42f, ch = 11f;
            k.Box(Vector3.zero, new Vector3(cw, ch, d * 0.65f), Cream);
            k.Gable(new Vector3(0, ch, 0), new Vector3(cw + 1, 4.5f, d * 0.65f + 1), Patina, alongZ: false);
            foreach (float s in new[] { -1f, 1f })
            {
                k.Box(new Vector3(s * (cw / 2 + w * 0.14f), 0, d * 0.08f), new Vector3(w * 0.28f, 7.5f, d * 0.5f), Cream);
                k.Gable(new Vector3(s * (cw / 2 + w * 0.14f), 7.5f, d * 0.08f), new Vector3(w * 0.28f + 0.6f, 3f, d * 0.5f + 0.6f), Patina, alongZ: false);
            }
            // Portico: columns and a pediment on the south front.
            float front = -d * 0.325f - 3f;
            for (int i = 0; i < 4; i++)
                k.Cylinder(new Vector3(-cw * 0.3f + cw * 0.2f * i, 0, front), 0.55f, 8f, Whitewash);
            k.Box(new Vector3(0, 8f, front + 0.5f), new Vector3(cw * 0.75f, 1f, 4f), Whitewash);
            k.Gable(new Vector3(0, 9f, front + 0.5f), new Vector3(cw * 0.75f, 2.5f, 4f), Whitewash, alongZ: false);
            return ch + 4.5f;
        }

        static float Pair(Kit k, float w, float d)
        {
            k.Box(new Vector3(-w * 0.22f, 0, 0), new Vector3(w * 0.42f, 14f, d * 0.8f), Concrete);
            k.Box(new Vector3(-w * 0.22f, 14f, 0), new Vector3(w * 0.44f, 0.6f, d * 0.82f), RoofDark);
            k.Box(new Vector3(w * 0.25f, 0, d * 0.05f), new Vector3(w * 0.38f, 9f, d * 0.9f), Brick);
            k.Gable(new Vector3(w * 0.25f, 9f, d * 0.05f), new Vector3(w * 0.4f, 3f, d * 0.92f), RoofDark, alongZ: true);
            return 14.6f;
        }

        static float Depot(Kit k, float w, float d)
        {
            float hh = 6f;
            k.Box(Vector3.zero, new Vector3(w, hh, d), Brick);
            // Arched roof: a cylinder lying along the shed, half sunk into it.
            k.CylinderLying(new Vector3(0, hh, 0), d * 0.5f, w, DarkConc);
            // Tracks running out of the doors.
            foreach (float s in new[] { -0.25f, 0.25f })
                k.Box(new Vector3(w * 0.5f + 8f, 0, d * s), new Vector3(16f, 0.4f, 1.6f), Rail);
            return hh + d * 0.5f;
        }

        static float Slabs(Kit k, float w, float d)
        {
            // Безмолвные плиты: 5–7 storey panel blocks, abandoned, one broken.
            float[] hs = { 18f, 21f, 15f, 19f };
            for (int i = 0; i < hs.Length; i++)
            {
                float x = -w / 2 + w * (i + 0.5f) / hs.Length;
                float z = (i % 2 == 0 ? -1 : 1) * d * 0.18f;
                if (i == 2)
                {
                    // Collapsed corner: two stepped pieces.
                    k.Box(new Vector3(x, 0, z), new Vector3(w / hs.Length * 0.8f, hs[i], 5f), DarkConc);
                    k.Box(new Vector3(x - 1.5f, hs[i], z), new Vector3(w / hs.Length * 0.45f, 4f, 5f), DarkConc);
                }
                else k.Box(new Vector3(x, 0, z), new Vector3(w / hs.Length * 0.8f, hs[i], 5f), DarkConc);
            }
            return 21f;
        }

        static float Ruin(Kit k, float w, float d)
        {
            // Roofless walls with gaps, and a broken chimney.
            k.Box(new Vector3(0, 0, -d / 2 + 0.6f), new Vector3(w, 6f, 1.2f), DarkConc);
            k.Box(new Vector3(-w / 2 + 0.6f, 0, 0), new Vector3(1.2f, 4.5f, d), DarkConc);
            k.Box(new Vector3(w / 2 - 0.6f, 0, d * 0.2f), new Vector3(1.2f, 7f, d * 0.6f), DarkConc);
            k.Box(new Vector3(-w * 0.15f, 0, d / 2 - 0.6f), new Vector3(w * 0.6f, 3f, 1.2f), DarkConc);
            var chimney = k.Cylinder(new Vector3(w * 0.25f, 0, d * 0.25f), 1.3f, 14f, Brick);
            chimney.transform.localRotation = Quaternion.Euler(6f, 0, 4f);   // leaning
            return 14f;
        }

        static float Shed(Kit k, float w, float d)
        {
            k.Box(Vector3.zero, new Vector3(w, 4f, d), DarkConc);
            k.Box(new Vector3(-w * 0.15f, 4f, 0), new Vector3(w * 0.7f, 0.5f, d + 0.6f), RoofDark);   // roof half fallen in
            return 4.5f;
        }

        static float Spillway(Kit k, float w, float d)
        {
            // Concrete spillway at the ravine's edge, stepped toward the drop.
            for (int i = 0; i < 4; i++)
                k.Box(new Vector3(0, 0, -d / 2 + d * (i + 0.5f) / 4f), new Vector3(w, 10f - i * 2.2f, d / 4f), Concrete);
            k.Box(new Vector3(0, 10f, -d / 2 + d / 8f), new Vector3(w + 2f, 0.8f, 2f), DarkConc);
            return 11f;
        }

        static float Gallery(Kit k, float w, float d)
        {
            float hh = 9f;
            k.Box(new Vector3(0, 0, d * 0.1f), new Vector3(w * 0.85f, hh, d * 0.7f), Cream);
            k.Box(new Vector3(0, hh, d * 0.05f), new Vector3(w * 0.9f, 1.2f, d * 0.9f), Whitewash);   // flat cornice
            float front = -d * 0.25f - 2.5f;
            for (int i = 0; i < 6; i++)
                k.Cylinder(new Vector3(-w * 0.35f + w * 0.14f * i, 0, front), 0.6f, hh, Whitewash);
            k.Gable(new Vector3(0, hh + 1.2f, front + 2f), new Vector3(w * 0.8f, 3f, 5f), Whitewash, alongZ: false);
            return hh + 4.2f;
        }

        static float Towers(Kit k, float w, float d)
        {
            float[] hs = { 44f, 52f, 38f };
            for (int i = 0; i < 3; i++)
            {
                var p = new Vector3(-w * 0.3f + w * 0.3f * i, 0, (i - 1) * d * 0.15f);
                k.Box(p, new Vector3(6.5f, hs[i], 6.5f), Glass, 0.75f);
                k.Box(p + Vector3.up * hs[i], new Vector3(5f, 2f, 5f), DarkConc);
            }
            return 54f;
        }

        static float Chapel(Kit k, float w, float d)
        {
            k.Box(new Vector3(0, 0, d * 0.1f), new Vector3(w * 0.6f, 6f, d * 0.7f), Whitewash);
            k.Gable(new Vector3(0, 6f, d * 0.1f), new Vector3(w * 0.6f + 0.6f, 4f, d * 0.7f + 0.6f), RoofDark, alongZ: true);
            var tower = new Vector3(0, 0, -d * 0.32f);
            k.Box(tower, new Vector3(3.6f, 13f, 3.6f), Whitewash);
            k.Spire(tower + Vector3.up * 13f, 2.6f, 6f, Patina);
            // Cross on top.
            k.Box(tower + Vector3.up * 19f, new Vector3(0.35f, 3f, 0.35f), Gilt, 0.6f);
            k.Box(tower + Vector3.up * 20.6f, new Vector3(1.6f, 0.35f, 0.35f), Gilt, 0.6f);
            return 22f;
        }

        // ------------------------------------------------------------------ kit

        class Kit
        {
            readonly Transform _root;
            readonly Func<Color, float, Material> _mat;
            readonly System.Random _rng;
            public Kit(Transform root, Func<Color, float, Material> mat, System.Random rng) { _root = root; _mat = mat; _rng = rng; }

            public float Range(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

            /// Box standing on y = bottom.y (bottom centre at the given point).
            public float Box(Vector3 bottom, Vector3 size, Color c, float smooth = 0.1f)
            {
                Part(UnitCube, bottom + Vector3.up * size.y / 2, size, Quaternion.identity, c, smooth);
                return bottom.y + size.y;
            }

            /// Gable roof (triangular prism) sitting on y = bottom.y. alongZ: ridge runs along z.
            public void Gable(Vector3 bottom, Vector3 size, Color c, bool alongZ)
            {
                var rot = alongZ ? Quaternion.identity : Quaternion.Euler(0, 90, 0);
                var s = alongZ ? size : new Vector3(size.z, size.y, size.x);
                Part(PrismMesh, bottom, s, rot, c, 0.1f);
            }

            public GameObject Cylinder(Vector3 bottom, float radius, float height, Color c)
                => Part(UnitCylinder, bottom + Vector3.up * height / 2, new Vector3(radius * 2, height / 2, radius * 2), Quaternion.identity, c, 0.1f);

            /// Cylinder lying along x with its axis at the given point (for arched roofs).
            public void CylinderLying(Vector3 axis, float radius, float length, Color c)
                => Part(UnitCylinder, axis, new Vector3(radius * 2, length / 2, radius * 2), Quaternion.Euler(0, 0, 90), c, 0.1f);

            public void Spire(Vector3 bottom, float radius, float height, Color c)
                => Part(ConeMesh, bottom, new Vector3(radius * 2, height, radius * 2), Quaternion.identity, c, 0.3f);

            GameObject Part(Mesh mesh, Vector3 localPos, Vector3 scale, Quaternion rot, Color c, float smooth)
            {
                var go = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(_root, false);
                go.transform.localPosition = localPos;
                go.transform.localRotation = rot;
                go.transform.localScale = scale;
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                go.GetComponent<MeshRenderer>().sharedMaterial = _mat(c, smooth);
                return go;
            }
        }

        // ------------------------------------------------------------------ meshes

        static Mesh _cube, _cyl, _prism, _cone;
        /// Shared meshes for other builders (countryside villages, woods).
        public static Mesh CubeMesh => UnitCube;
        public static Mesh GableMesh => PrismMesh;
        public static Mesh SpireMesh => ConeMesh;
        static Mesh UnitCube => _cube != null ? _cube : (_cube = Primitive(PrimitiveType.Cube));
        static Mesh UnitCylinder => _cyl != null ? _cyl : (_cyl = Primitive(PrimitiveType.Cylinder));

        static Mesh Primitive(PrimitiveType t)
        {
            var go = GameObject.CreatePrimitive(t);
            var m = go.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.Destroy(go);
            return m;
        }

        /// Unit gable prism: 1 wide (x), 1 tall (y, apex), 1 long (z), base centred on the origin. Flat-shaded.
        static Mesh PrismMesh
        {
            get
            {
                if (_prism != null) return _prism;
                Vector3 l0 = new Vector3(-0.5f, 0, -0.5f), r0 = new Vector3(0.5f, 0, -0.5f), t0 = new Vector3(0, 1, -0.5f);
                Vector3 l1 = new Vector3(-0.5f, 0, 0.5f), r1 = new Vector3(0.5f, 0, 0.5f), t1 = new Vector3(0, 1, 0.5f);
                var v = new List<Vector3>(); var tri = new List<int>();
                void Tri(Vector3 a, Vector3 b, Vector3 c) { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); tri.Add(i); tri.Add(i + 1); tri.Add(i + 2); }
                void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Tri(a, b, c); Tri(a, c, d); }
                Tri(l0, t0, r0);                 // front gable (faces −z)
                Tri(r1, t1, l1);                 // back gable (faces +z)
                Quad(l0, l1, t1, t0);            // left slope
                Quad(r0, t0, t1, r1);            // right slope
                Quad(l0, r0, r1, l1);            // bottom
                _prism = new Mesh { name = "Gable" };
                _prism.SetVertices(v); _prism.SetTriangles(tri, 0);
                _prism.RecalculateNormals(); _prism.RecalculateBounds();
                return _prism;
            }
        }

        /// Unit cone (radius 0.5, height 1, base at the origin), 10 sides, flat-shaded.
        static Mesh ConeMesh
        {
            get
            {
                if (_cone != null) return _cone;
                var v = new List<Vector3>(); var tri = new List<int>();
                const int sides = 10;
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i * Mathf.PI * 2 / sides, a1 = (i + 1) * Mathf.PI * 2 / sides;
                    var p0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0, Mathf.Sin(a0) * 0.5f);
                    var p1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0, Mathf.Sin(a1) * 0.5f);
                    int k = v.Count; v.Add(p0); v.Add(Vector3.up); v.Add(p1);
                    tri.Add(k); tri.Add(k + 1); tri.Add(k + 2);
                }
                _cone = new Mesh { name = "Spire" };
                _cone.SetVertices(v); _cone.SetTriangles(tri, 0);
                _cone.RecalculateNormals(); _cone.RecalculateBounds();
                return _cone;
            }
        }
    }
}
