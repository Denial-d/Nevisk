using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nevishk
{
    /// <summary>
    /// The railway (ЖД) traced from the base map, laid over the terrain:
    /// ballast bed, sleepers and two rails on a smoothed grade; a raised embankment where it runs along
    /// the river, a bridge on piers where it crosses it, and tunnels where the mountains rise above it.
    /// Usage: Plan() → FlattenTerrain() (before the terrain meshes are built) → Build().
    /// </summary>
    public class NevishkRailroad
    {
        public const float Step = 2f;              // sample spacing along the line
        public const float BedHalfWidth = 4.5f;    // ground shaped to the track within this distance
        const float Gauge = 2.4f;                  // exaggerated so the track reads from the overview
        const float MaxRailHeight = 30f;           // above this the line goes into a tunnel
        const float TunnelCover = 9f;              // ground this far above the rails = tunnel
        const float ShortCrossing = 40f;           // water runs shorter than this get a bridge, longer an embankment
        const float MaxGrade = 0.05f;              // 5 % (exaggerated, like the rest of the map's vertical scale)
        const float MaxFill = 6f;                  // higher than this above the ground → viaduct on piers

        public readonly List<Vector3> P = new List<Vector3>();     // rail-base points (world)
        public readonly List<Vector3> Right = new List<Vector3>(); // unit vector to the right of travel
        public readonly List<bool> Tunnel = new List<bool>();
        public readonly List<bool> Bridge = new List<bool>();
        readonly List<float> _ground = new List<float>();

        // ------------------------------------------------------------------ planning

        public static NevishkRailroad Plan(NevishkTerrain terrain, IList<Vector2> uvs, float mapWidth, float mapDepth)
        {
            var r = new NevishkRailroad();
            // Resample the traced polyline at even spacing in world XZ.
            var pts = new List<Vector2>();
            foreach (var uv in uvs) pts.Add(new Vector2((uv.x - 0.5f) * mapWidth, (0.5f - uv.y) * mapDepth));
            var xz = Resample(pts, Step);
            int n = xz.Count;

            var ground = new float[n]; var water = new float[n]; var wet = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var p = new Vector3(xz[i].x, 0, xz[i].y);
                ground[i] = terrain.HeightAtWorld(p);
                water[i] = terrain.WaterAtWorld(p);
                wet[i] = !float.IsNaN(water[i]);
            }

            // Classify water runs: short = crossing (bridge), long = running along the river (embankment).
            var shortWet = new bool[n];
            for (int i = 0; i < n;)
            {
                if (!wet[i]) { i++; continue; }
                int j = i; while (j < n && wet[j]) j++;
                if ((j - i) * Step <= ShortCrossing) for (int k = i; k < j; k++) shortWet[k] = true;
                i = j;
            }

            // Desired level: the ground, lifted clear of the water, smoothed over ~50 m, capped,
            // then limited to MaxGrade (forward/backward passes) while staying clear of the water.
            var clear = new float[n]; var raw = new float[n];
            for (int i = 0; i < n; i++)
            {
                clear[i] = wet[i] ? water[i] + (shortWet[i] ? 4.5f : 2.5f) : float.MinValue;
                raw[i] = Mathf.Max(ground[i], clear[i]);
            }
            var level = Gaussian(raw, 25f);
            for (int i = 0; i < n; i++) level[i] = Mathf.Min(level[i], MaxRailHeight);
            float dmax = MaxGrade * Step;
            for (int pass = 0; pass < 6; pass++)
            {
                for (int i = 0; i < n; i++) level[i] = Mathf.Max(level[i], clear[i]);
                for (int i = 1; i < n; i++) level[i] = Mathf.Clamp(level[i], level[i - 1] - dmax, level[i - 1] + dmax);
                for (int i = n - 2; i >= 0; i--) level[i] = Mathf.Clamp(level[i], level[i + 1] - dmax, level[i + 1] + dmax);
            }

            for (int i = 0; i < n; i++)
            {
                var p = new Vector3(xz[i].x, level[i], xz[i].y);
                r.P.Add(p);
                r._ground.Add(ground[i]);
                r.Tunnel.Add(ground[i] - level[i] > TunnelCover);
                // Bridge: river crossings; dry ground more than MaxFill below; or running along the river
                // but well above the water (otherwise it's an embankment beside the river).
                r.Bridge.Add(shortWet[i] || (!wet[i] && level[i] - ground[i] > MaxFill)
                             || (wet[i] && level[i] - water[i] > MaxFill + 2f));
            }
            // Smooth the tunnel/bridge flags so they don't flicker sample to sample.
            Dilate(r.Tunnel, 2); Dilate(r.Bridge, 2);
            for (int i = 0; i < n; i++)
            {
                Vector3 a = r.P[Mathf.Max(i - 1, 0)], b = r.P[Mathf.Min(i + 1, n - 1)];
                Vector3 t = new Vector3(b.x - a.x, 0, b.z - a.z).normalized;
                r.Right.Add(Vector3.Cross(Vector3.up, t));
            }
            return r;
        }

        /// Cut and fill the ground along the track (not under bridges or in tunnels).
        public void FlattenTerrain(NevishkTerrain terrain)
        {
            var skip = new List<bool>(P.Count);
            for (int i = 0; i < P.Count; i++) skip.Add(Tunnel[i] || Bridge[i]);
            terrain.FlattenAlong(P, skip, BedHalfWidth, 8f, 0.2f);
        }

        /// Horizontal distance from a world point to the line.
        public float DistanceXZ(Vector3 w)
        {
            float best = float.MaxValue;
            var q = new Vector2(w.x, w.z);
            for (int i = 0; i + 1 < P.Count; i++)
            {
                Vector2 a = new Vector2(P[i].x, P[i].z), ab = new Vector2(P[i + 1].x - a.x, P[i + 1].z - a.y);
                float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                best = Mathf.Min(best, Vector2.Distance(q, a + ab * t));
            }
            return best;
        }

        // ------------------------------------------------------------------ meshes

        static readonly Color Ballast = new Color(0.33f, 0.31f, 0.28f);
        static readonly Color Sleeper = new Color(0.27f, 0.20f, 0.15f);
        static readonly Color Steel   = new Color(0.58f, 0.58f, 0.60f);
        static readonly Color Girder  = new Color(0.22f, 0.23f, 0.24f);
        static readonly Color Pier    = new Color(0.52f, 0.50f, 0.47f);
        static readonly Color Portal  = new Color(0.45f, 0.43f, 0.40f);

        public void Build(Transform parent, Func<Color, float, Material> mat, Mesh cube)
        {
            var root = new GameObject("Railway (ЖД)").transform;
            root.SetParent(parent, false);

            var bed = new MeshBuilder(); var rails = new MeshBuilder(); var girders = new MeshBuilder();
            var sleepers = new List<CombineInstance>(); var piers = new List<CombineInstance>();

            // Visible runs = everything outside tunnels.
            for (int i = 0; i < P.Count;)
            {
                if (Tunnel[i]) { i++; continue; }
                int j = i; while (j < P.Count && !Tunnel[j]) j++;
                if (j - i >= 2)
                {
                    // Ballast: trapezoid bed, 5.6 wide on top, flaring to 9 at the bottom, going 1.6 into the ground.
                    bed.Beam(P, Right, i, j, 0f, 2.8f, 4.5f, 0.8f, -1.6f);
                    // Two rails.
                    foreach (float side in new[] { -Gauge / 2, Gauge / 2 })
                        rails.Beam(P, Right, i, j, side, 0.2f, 0.2f, 1.45f, 0.95f);
                    // Sleepers every 1.8 units.
                    float acc = 0f;
                    for (int k = i; k < j - 1; k++)
                    {
                        float seg = Vector3.Distance(P[k], P[k + 1]);
                        for (; acc < seg; acc += 1.8f)
                        {
                            Vector3 p = Vector3.Lerp(P[k], P[k + 1], acc / seg);
                            Vector3 fwd = Vector3.Cross(Right[k], Vector3.up);
                            sleepers.Add(new CombineInstance
                            {
                                mesh = cube,
                                transform = Matrix4x4.TRS(p + Vector3.up * 0.95f, Quaternion.LookRotation(fwd), new Vector3(Gauge + 1.8f, 0.3f, 0.8f))
                            });
                        }
                        acc -= seg;
                    }
                }
                i = j;
            }

            // Bridges: side girders and piers down to the river bed / ground.
            for (int i = 0; i < P.Count;)
            {
                if (!Bridge[i] || Tunnel[i]) { i++; continue; }
                int j = i; while (j < P.Count && Bridge[j] && !Tunnel[j]) j++;
                int a = Mathf.Max(i - 2, 0), b = Mathf.Min(j + 2, P.Count);
                foreach (float side in new[] { -3.4f, 3.4f })
                    girders.Beam(P, Right, a, b, side, 0.35f, 0.35f, 1.6f, -1.8f);
                for (int k = a; k < b; k += 6)
                {
                    float top = P[k].y - 1.6f, bottom = Mathf.Min(_ground[k], P[k].y - 4f) - 2f;
                    Vector3 fwd = Vector3.Cross(Right[k], Vector3.up);
                    piers.Add(new CombineInstance
                    {
                        mesh = cube,
                        transform = Matrix4x4.TRS(new Vector3(P[k].x, (top + bottom) / 2, P[k].z), Quaternion.LookRotation(fwd),
                                                  new Vector3(7.5f, top - bottom, 2.2f))
                    });
                }
                i = j;
            }

            // Tunnel portals where the line enters/leaves the hills.
            for (int i = 1; i < P.Count; i++)
            {
                if (Tunnel[i] == Tunnel[i - 1]) continue;
                int k = Tunnel[i] ? i : i - 1;   // the first/last sample inside the tunnel
                Vector3 fwd = Vector3.Cross(Right[k], Vector3.up);
                var rot = Quaternion.LookRotation(fwd);
                var portal = new List<CombineInstance>
                {
                    new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(P[k] + Vector3.up * 5f, rot, new Vector3(13f, 11f, 3f)) },
                };
                AddCombined(root, "Tunnel portal", portal, mat(Portal, 0.05f));
                var mouth = new List<CombineInstance>
                {
                    new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(P[k] + Vector3.up * 4f - fwd * (Tunnel[i] ? 0.6f : -0.6f), rot, new Vector3(8f, 7.5f, 3.4f)) },
                };
                AddCombined(root, "Tunnel mouth", mouth, mat(new Color(0.03f, 0.03f, 0.03f), 0f));
            }

            Add(root, "Ballast", bed.ToMesh(), mat(Ballast, 0.02f));
            Add(root, "Rails", rails.ToMesh(), mat(Steel, 0.75f));
            Add(root, "Bridge girders", girders.ToMesh(), mat(Girder, 0.3f));
            AddCombined(root, "Sleepers", sleepers, mat(Sleeper, 0.05f));
            AddCombined(root, "Bridge piers", piers, mat(Pier, 0.05f));
        }

        static void Add(Transform parent, string name, Mesh mesh, Material m)
        {
            if (mesh.vertexCount == 0) return;
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        static void AddCombined(Transform parent, string name, List<CombineInstance> parts, Material m)
        {
            if (parts.Count == 0) return;
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(parts.ToArray(), true, true);
            mesh.RecalculateBounds();
            Add(parent, name, mesh, m);
        }

        // ------------------------------------------------------------------ helpers

        static List<Vector2> Resample(List<Vector2> pts, float step)
        {
            var outp = new List<Vector2> { pts[0] };
            float carry = 0f;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                float len = Vector2.Distance(a, b);
                for (float d = step - carry; d <= len; d += step) outp.Add(Vector2.Lerp(a, b, d / len));
                carry = (carry + len) % step;
            }
            return outp;
        }

        static float[] Gaussian(float[] v, float sigma)
        {
            int r = Mathf.CeilToInt(sigma * 2.5f), n = v.Length;
            var w = new float[2 * r + 1]; float sum = 0;
            for (int k = -r; k <= r; k++) { w[k + r] = Mathf.Exp(-k * k / (2 * sigma * sigma)); sum += w[k + r]; }
            var o = new float[n];
            for (int i = 0; i < n; i++)
            {
                float acc = 0;
                for (int k = -r; k <= r; k++) acc += v[Mathf.Clamp(i + k, 0, n - 1)] * w[k + r];
                o[i] = acc / sum;
            }
            return o;
        }

        static void Dilate(List<bool> flags, int r)
        {
            var src = flags.ToArray();
            for (int i = 0; i < src.Length; i++)
            {
                if (src[i]) continue;
                for (int k = -r; k <= r; k++)
                {
                    int j = i + k;
                    if (j >= 0 && j < src.Length && src[j]) { flags[i] = true; break; }
                }
            }
        }

        /// Builds ribbon "beams" along the path: a trapezoid cross-section swept from sample i to j.
        class MeshBuilder
        {
            readonly List<Vector3> _v = new List<Vector3>();
            readonly List<int> _t = new List<int>();

            /// offset: sideways from the centre line; halfTop/halfBottom: half widths; yTop/yBottom: heights above the path.
            public void Beam(List<Vector3> p, List<Vector3> right, int i, int j, float offset, float halfTop, float halfBottom, float yTop, float yBottom)
            {
                int count = j - i; if (count < 2) return;
                var LB = new Vector3[count]; var LT = new Vector3[count]; var RT = new Vector3[count]; var RB = new Vector3[count];
                for (int k = 0; k < count; k++)
                {
                    Vector3 c = p[i + k] + right[i + k] * offset, r = right[i + k];
                    LB[k] = c - r * halfBottom + Vector3.up * yBottom;
                    LT[k] = c - r * halfTop + Vector3.up * yTop;
                    RT[k] = c + r * halfTop + Vector3.up * yTop;
                    RB[k] = c + r * halfBottom + Vector3.up * yBottom;
                }
                Strip(LB, LT);   // left side, faces left
                Strip(LT, RT);   // top, faces up
                Strip(RT, RB);   // right side, faces right
            }

            // Quad strip between two rails of points; for travel direction t the face normal is cross(t, b - a).
            void Strip(Vector3[] a, Vector3[] b)
            {
                int start = _v.Count, n = a.Length;
                for (int k = 0; k < n; k++) { _v.Add(a[k]); _v.Add(b[k]); }
                for (int k = 0; k + 1 < n; k++)
                {
                    int a0 = start + k * 2, b0 = a0 + 1, a1 = a0 + 2, b1 = a0 + 3;
                    _t.Add(a0); _t.Add(a1); _t.Add(b1);
                    _t.Add(a0); _t.Add(b1); _t.Add(b0);
                }
            }

            public Mesh ToMesh()
            {
                var m = new Mesh { name = "Rail beam", indexFormat = IndexFormat.UInt32 };
                m.SetVertices(_v); m.SetTriangles(_t, 0);
                m.RecalculateNormals(); m.RecalculateBounds();
                return m;
            }
        }
    }
}
