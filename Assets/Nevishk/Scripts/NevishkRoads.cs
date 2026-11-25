using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nevishk
{
    /// <summary>
    /// The city's key roads, routed over the terrain at runtime: they link the district centres (a minimum spanning
    /// tree plus each district's nearest neighbours, so there are some loops) and run out to the country roads.
    /// Rivers are crossed only on the bridges; steep ground, drawn buildings and the alleys are avoided; the railway
    /// is crossed at level crossings. Later roads prefer to merge into earlier ones, so the network shares trunks.
    /// </summary>
    public class NevishkRoads
    {
        public const float HalfWidth = 3.4f;

        readonly NevishkTerrain _t;
        readonly Rect _rect;
        readonly int _nx, _nz;
        readonly float _cell;
        float[] _cost;                 // per cell; +inf = impassable
        bool[] _road;                  // cells already carrying a road
        public readonly List<List<Vector3>> Roads = new List<List<Vector3>>();

        public NevishkRoads(NevishkTerrain t, float cell = 5f)
        {
            _t = t; _rect = t.WorldRect; _cell = cell;
            _nx = Mathf.FloorToInt(_rect.width / cell) + 1;
            _nz = Mathf.FloorToInt(_rect.height / cell) + 1;
        }

        Vector3 World(int i, int j) => new Vector3(_rect.xMin + i * _cell, 0f, _rect.yMin + j * _cell);
        int Index(Vector3 w) => Mathf.Clamp(Mathf.RoundToInt((w.z - _rect.yMin) / _cell), 0, _nz - 1) * _nx
                              + Mathf.Clamp(Mathf.RoundToInt((w.x - _rect.xMin) / _cell), 0, _nx - 1);

        /// <param name="bridges">Bridge decks (end points, deck width): the only way across water.</param>
        /// <param name="blocked">True where a road must not go (drawn buildings, alleys).</param>
        /// <param name="nearRail">True next to the railway (crossable, at a cost).</param>
        public void Plan(List<Vector3> districts, List<Vector3> exits, List<(Vector3 a, Vector3 b, float width)> bridges,
                         Func<Vector3, bool> blocked, Func<Vector3, bool> nearRail)
        {
            int n = _nx * _nz;
            _cost = new float[n]; _road = new bool[n];
            for (int j = 0; j < _nz; j++)
            for (int i = 0; i < _nx; i++)
            {
                var w = World(i, j);
                int k = j * _nx + i;
                if (!float.IsNaN(_t.WaterAtWorld(w)) || blocked(w)) { _cost[k] = float.PositiveInfinity; continue; }
                float h = _t.HeightAtWorld(w);
                float sx = _t.HeightAtWorld(w + new Vector3(_cell, 0, 0)) - _t.HeightAtWorld(w - new Vector3(_cell, 0, 0));
                float sz = _t.HeightAtWorld(w + new Vector3(0, 0, _cell)) - _t.HeightAtWorld(w - new Vector3(0, 0, _cell));
                float slope = Mathf.Sqrt(sx * sx + sz * sz) / (2f * _cell);
                _cost[k] = 1f + slope * slope * 400f + (nearRail(w) ? 12f : 0f) + (h < 0.3f ? 6f : 0f);
            }
            // Bridge decks: cheap and passable whatever is underneath.
            foreach (var (a, b, width) in bridges)
            {
                float len = Vector3.Distance(new Vector3(a.x, 0, a.z), new Vector3(b.x, 0, b.z));
                int steps = Mathf.CeilToInt(len / (_cell * 0.5f)) + 1;
                for (int s = 0; s <= steps; s++)
                {
                    var p = Vector3.Lerp(a, b, s / (float)steps);
                    _cost[Index(p)] = 0.7f;
                }
            }

            // Snap every node to the nearest passable cell.
            var nodes = new List<int>();
            foreach (var d in districts) nodes.Add(Snap(Index(d)));
            int districtCount = nodes.Count;
            foreach (var e in exits) nodes.Add(Snap(Index(e)));

            // All-pairs costs from one Dijkstra per district.
            var dist = new float[districtCount][];
            for (int a = 0; a < districtCount; a++) dist[a] = Dijkstra(nodes[a]);
            var edges = new List<(int a, int b)>();
            // Minimum spanning tree (Prim) over the districts.
            var inTree = new bool[districtCount]; inTree[0] = true;
            for (int added = 1; added < districtCount; added++)
            {
                float best = float.PositiveInfinity; int ba = -1, bb = -1;
                for (int a = 0; a < districtCount; a++) if (inTree[a])
                    for (int b = 0; b < districtCount; b++) if (!inTree[b] && dist[a][nodes[b]] < best) { best = dist[a][nodes[b]]; ba = a; bb = b; }
                if (ba < 0) break;   // an island no bridge reaches
                inTree[bb] = true; edges.Add((ba, bb));
            }
            // Plus each district's two nearest neighbours, for a few loops.
            for (int a = 0; a < districtCount; a++)
            {
                var order = new List<int>();
                for (int b = 0; b < districtCount; b++) if (b != a && !float.IsInfinity(dist[a][nodes[b]])) order.Add(b);
                order.Sort((x, y) => dist[a][nodes[x]].CompareTo(dist[a][nodes[y]]));
                for (int q = 0; q < Mathf.Min(2, order.Count); q++)
                {
                    int b = order[q];
                    if (!edges.Contains((a, b)) && !edges.Contains((b, a))) edges.Add((a, b));
                }
            }
            // Exits join their nearest district.
            for (int e = districtCount; e < nodes.Count; e++)
            {
                int best = -1; float bc = float.PositiveInfinity;
                for (int a = 0; a < districtCount; a++) if (dist[a][nodes[e]] < bc) { bc = dist[a][nodes[e]]; best = a; }
                if (best >= 0) edges.Add((best, e));
            }
            // Shortest first, so trunks form early and later roads merge into them.
            edges.Sort((x, y) => Euclid(nodes[x.a], nodes[x.b]).CompareTo(Euclid(nodes[y.a], nodes[y.b])));

            foreach (var (a, b) in edges)
            {
                var path = AStar(nodes[a], nodes[b]);
                if (path == null) continue;
                // Keep only the stretches not already on a road (plus one cell of overlap, so they join).
                var run = new List<int>();
                for (int q = 0; q < path.Count; q++)
                {
                    bool onRoad = _road[path[q]];
                    if (!onRoad) { if (run.Count == 0 && q > 0) run.Add(path[q - 1]); run.Add(path[q]); }
                    else if (run.Count > 0) { run.Add(path[q]); Emit(run); run = new List<int>(); }
                }
                if (run.Count > 1) Emit(run);
                foreach (var c in path) _road[c] = true;
            }
        }

        float Euclid(int a, int b) => Vector2.Distance(new Vector2(a % _nx, a / _nx), new Vector2(b % _nx, b / _nx));

        int Snap(int k)
        {
            if (!float.IsInfinity(_cost[k])) return k;
            int ci = k % _nx, cj = k / _nx;
            for (int r = 1; r < 40; r++)
                for (int dj = -r; dj <= r; dj++)
                for (int di = -r; di <= r; di++)
                {
                    if (Mathf.Max(Mathf.Abs(di), Mathf.Abs(dj)) != r) continue;
                    int i = ci + di, j = cj + dj;
                    if (i < 0 || j < 0 || i >= _nx || j >= _nz) continue;
                    if (!float.IsInfinity(_cost[j * _nx + i])) return j * _nx + i;
                }
            return k;
        }

        static readonly int[] DI = { 1, -1, 0, 0, 1, 1, -1, -1 }, DJ = { 0, 0, 1, -1, 1, -1, 1, -1 };
        static readonly float[] DL = { 1, 1, 1, 1, 1.4142f, 1.4142f, 1.4142f, 1.4142f };

        float Step(int from, int to, int dir)
        {
            float c = (_cost[from] + _cost[to]) * 0.5f;
            if (_road[to]) c *= 0.35f;                 // merge into existing roads
            return c * DL[dir];
        }

        float[] Dijkstra(int start)
        {
            var d = new float[_cost.Length];
            for (int k = 0; k < d.Length; k++) d[k] = float.PositiveInfinity;
            var heap = new MinHeap(); d[start] = 0; heap.Push(start, 0);
            while (heap.Count > 0)
            {
                var (k, dk) = heap.Pop();
                if (dk > d[k]) continue;
                int i = k % _nx, j = k / _nx;
                for (int q = 0; q < 8; q++)
                {
                    int ii = i + DI[q], jj = j + DJ[q];
                    if (ii < 0 || jj < 0 || ii >= _nx || jj >= _nz) continue;
                    int nk = jj * _nx + ii;
                    if (float.IsInfinity(_cost[nk])) continue;
                    float nd = dk + (_cost[k] + _cost[nk]) * 0.5f * DL[q];
                    if (nd < d[nk]) { d[nk] = nd; heap.Push(nk, nd); }
                }
            }
            return d;
        }

        List<int> AStar(int start, int goal)
        {
            var g = new Dictionary<int, float> { [start] = 0f };
            var parent = new Dictionary<int, int>();
            var heap = new MinHeap(); heap.Push(start, 0f);
            int gi = goal % _nx, gj = goal / _nx;
            float H(int k) => 0.35f * Vector2.Distance(new Vector2(k % _nx, k / _nx), new Vector2(gi, gj));   // admissible with road discount
            while (heap.Count > 0)
            {
                var (k, _) = heap.Pop();
                if (k == goal)
                {
                    var path = new List<int> { k };
                    while (parent.TryGetValue(k, out k)) path.Add(k);
                    path.Reverse();
                    return path;
                }
                float gk = g[k];
                int i = k % _nx, j = k / _nx;
                for (int q = 0; q < 8; q++)
                {
                    int ii = i + DI[q], jj = j + DJ[q];
                    if (ii < 0 || jj < 0 || ii >= _nx || jj >= _nz) continue;
                    int nk = jj * _nx + ii;
                    if (float.IsInfinity(_cost[nk])) continue;
                    float ng = gk + Step(k, nk, q);
                    if (!g.TryGetValue(nk, out var old) || ng < old) { g[nk] = ng; parent[nk] = k; heap.Push(nk, ng + H(nk)); }
                }
            }
            return null;
        }

        /// Cells → a smooth polyline (Chaikin, ends kept), resampled about every 4 m.
        void Emit(List<int> cells)
        {
            if (cells.Count < 2) return;
            var pts = new List<Vector3>();
            foreach (var c in cells) pts.Add(World(c % _nx, c / _nx));
            for (int it = 0; it < 3; it++)
            {
                var s = new List<Vector3> { pts[0] };
                for (int q = 0; q + 1 < pts.Count; q++)
                {
                    s.Add(Vector3.Lerp(pts[q], pts[q + 1], 0.25f));
                    s.Add(Vector3.Lerp(pts[q], pts[q + 1], 0.75f));
                }
                s.Add(pts[pts.Count - 1]);
                pts = s;
            }
            var res = new List<Vector3> { pts[0] };
            float acc = 0f;
            for (int q = 1; q < pts.Count; q++)
            {
                acc += Vector3.Distance(pts[q - 1], pts[q]);
                if (acc >= 4f || q == pts.Count - 1) { res.Add(pts[q]); acc = 0f; }
            }
            Roads.Add(res);
        }

        Dictionary<long, List<(Vector2 a, Vector2 b)>> _segGrid;
        const float SegCell = 20f;

        /// Exact distance (XZ) from w to the nearest road centreline; +inf if there are no roads near.
        public float DistanceTo(Vector3 w)
        {
            if (_segGrid == null)
            {
                _segGrid = new Dictionary<long, List<(Vector2, Vector2)>>();
                foreach (var road in Roads)
                    for (int q = 0; q + 1 < road.Count; q++)
                    {
                        Vector2 a = new Vector2(road[q].x, road[q].z), b = new Vector2(road[q + 1].x, road[q + 1].z);
                        int i0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) / SegCell), i1 = Mathf.FloorToInt(Mathf.Max(a.x, b.x) / SegCell);
                        int j0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y) / SegCell), j1 = Mathf.FloorToInt(Mathf.Max(a.y, b.y) / SegCell);
                        for (int j = j0; j <= j1; j++)
                        for (int i = i0; i <= i1; i++)
                        {
                            long key = ((long)i << 32) ^ (uint)j;
                            if (!_segGrid.TryGetValue(key, out var list)) _segGrid[key] = list = new List<(Vector2, Vector2)>();
                            list.Add((a, b));
                        }
                    }
            }
            Vector2 p = new Vector2(w.x, w.z);
            int ci = Mathf.FloorToInt(p.x / SegCell), cj = Mathf.FloorToInt(p.y / SegCell);
            float best = float.PositiveInfinity;
            for (int dj = -1; dj <= 1; dj++)
            for (int di = -1; di <= 1; di++)
            {
                if (!_segGrid.TryGetValue(((long)(ci + di) << 32) ^ (uint)(cj + dj), out var list)) continue;
                foreach (var (a, b) in list)
                {
                    Vector2 ab = b - a; float tt = ab.sqrMagnitude < 1e-4f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                    best = Mathf.Min(best, Vector2.Distance(p, a + ab * tt));
                }
            }
            return best;
        }

        /// Distance (XZ) from w to the nearest road, capped at `cap`.
        public bool Near(Vector3 w, float margin)
        {
            if (_road == null) return false;
            int r = Mathf.CeilToInt((margin + HalfWidth) / _cell) + 1;
            int ci = Mathf.RoundToInt((w.x - _rect.xMin) / _cell), cj = Mathf.RoundToInt((w.z - _rect.yMin) / _cell);
            for (int dj = -r; dj <= r; dj++)
            for (int di = -r; di <= r; di++)
            {
                int i = ci + di, j = cj + dj;
                if (i < 0 || j < 0 || i >= _nx || j >= _nz || !_road[j * _nx + i]) continue;
                var p = World(i, j);
                if (new Vector2(p.x - w.x, p.z - w.z).magnitude < margin + HalfWidth + _cell * 0.5f) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ mesh

        /// Asphalt ribbons draped over the ground (each edge sampled separately); skipped over water, where the
        /// bridge decks carry the road.
        public const float VergeWidth = 2.4f;

        public void Build(Transform parent, Material asphalt, Material line, Material verge)
        {
            var root = new GameObject("Roads").transform;
            root.SetParent(parent, false);
            var v = new List<Vector3>(); var t = new List<int>();
            var lv = new List<Vector3>(); var lt = new List<int>();
            var gv = new List<Vector3>(); var gt = new List<int>();
            foreach (var road in Roads)
            {
                int prevL = -1, prevLine = -1, prevG = -1; float along = 0f;
                for (int q = 0; q < road.Count; q++)
                {
                    Vector3 a = road[Mathf.Max(q - 1, 0)], b = road[Mathf.Min(q + 1, road.Count - 1)];
                    Vector3 dir = b - a; dir.y = 0; dir.Normalize();
                    Vector3 right = Vector3.Cross(Vector3.up, dir);
                    Vector3 c = road[q];
                    if (q > 0) along += Vector3.Distance(road[q - 1], c);
                    bool wet = !float.IsNaN(_t.WaterAtWorld(c));
                    if (wet) { prevL = -1; prevLine = -1; prevG = -1; continue; }
                    Vector3 l = c - right * HalfWidth, r = c + right * HalfWidth;
                    l.y = _t.HeightAtWorld(l) + 0.22f; r.y = _t.HeightAtWorld(r) + 0.22f;
                    float cy = Mathf.Max(_t.HeightAtWorld(c) + 0.22f, (l.y + r.y) * 0.5f);
                    l.y = Mathf.Max(l.y, cy - 0.6f); r.y = Mathf.Max(r.y, cy - 0.6f);
                    int k = v.Count; v.Add(l); v.Add(r);
                    if (prevL >= 0)
                    {
                        t.Add(prevL); t.Add(k); t.Add(k + 1);
                        t.Add(prevL); t.Add(k + 1); t.Add(prevL + 1);
                    }
                    prevL = k;

                    // Grass verges either side (just below the asphalt, so crossings stay grey).
                    Vector3 ol = c - right * (HalfWidth + VergeWidth), or = c + right * (HalfWidth + VergeWidth);
                    ol.y = _t.HeightAtWorld(ol) + 0.16f; or.y = _t.HeightAtWorld(or) + 0.16f;
                    Vector3 il = l + Vector3.down * 0.05f, ir = r + Vector3.down * 0.05f;
                    int g = gv.Count; gv.Add(ol); gv.Add(il); gv.Add(ir); gv.Add(or);
                    if (prevG >= 0)
                    {
                        gt.Add(prevG); gt.Add(g); gt.Add(g + 1); gt.Add(prevG); gt.Add(g + 1); gt.Add(prevG + 1);
                        gt.Add(prevG + 2); gt.Add(g + 2); gt.Add(g + 3); gt.Add(prevG + 2); gt.Add(g + 3); gt.Add(prevG + 3);
                    }
                    prevG = g;

                    // Dashed centre line: 3 m dash, 4 m gap.
                    bool dash = (along % 7f) < 3f;
                    if (dash)
                    {
                        Vector3 m = new Vector3(c.x, cy + 0.03f, c.z);
                        int lk = lv.Count; lv.Add(m - right * 0.12f); lv.Add(m + right * 0.12f);
                        if (prevLine >= 0) { lt.Add(prevLine); lt.Add(lk); lt.Add(lk + 1); lt.Add(prevLine); lt.Add(lk + 1); lt.Add(prevLine + 1); }
                        prevLine = lk;
                    }
                    else prevLine = -1;
                }
            }
            AddMesh(root, "Asphalt", v, t, asphalt);
            if (verge != null) AddMesh(root, "Verges", gv, gt, verge);
            AddMesh(root, "Road markings", lv, lt, line);
        }

        static void AddMesh(Transform root, string name, List<Vector3> v, List<int> t, Material m)
        {
            if (t.Count == 0) return;
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetTriangles(t, 0);
            var normals = new Vector3[v.Count]; for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            mesh.normals = normals; mesh.RecalculateBounds();
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// Binary min-heap of (cell, priority).
        class MinHeap
        {
            readonly List<(int k, float p)> _h = new List<(int, float)>();
            public int Count => _h.Count;
            public void Push(int k, float p)
            {
                _h.Add((k, p)); int i = _h.Count - 1;
                while (i > 0) { int up = (i - 1) / 2; if (_h[up].p <= _h[i].p) break; (_h[up], _h[i]) = (_h[i], _h[up]); i = up; }
            }
            public (int, float) Pop()
            {
                var top = _h[0]; var last = _h[_h.Count - 1]; _h.RemoveAt(_h.Count - 1);
                if (_h.Count > 0)
                {
                    _h[0] = last; int i = 0;
                    while (true)
                    {
                        int l = 2 * i + 1, r = l + 1, m = i;
                        if (l < _h.Count && _h[l].p < _h[m].p) m = l;
                        if (r < _h.Count && _h[r].p < _h[m].p) m = r;
                        if (m == i) break;
                        (_h[m], _h[i]) = (_h[i], _h[m]); i = m;
                    }
                }
                return top;
            }
        }
    }
}
