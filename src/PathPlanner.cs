using System;
using System.Collections.Generic;

namespace CawPlus
{
    internal sealed class FootPath
    {
        public string Kind;                                  // Promenade, Trail, Park
        public List<float[]> Points = new List<float[]>();  // metres
        public bool Closed;                                  // last point joins the first (park loop)
    }

    /// <summary>
    /// Footpaths after Riverview's (decoded from its .world, library/riverview_roads.json): a network of their own that
    /// never joins the roads (248 connections, all path to path), mostly 10-85 m from a road and almost never alongside
    /// one (3%). Three kinds: promenades along the river or shore near town, winding trails through the middle of
    /// neighbourhoods, and a loop in the park. Paths stop a few metres short of any road they meet and carry on beyond it.
    /// </summary>
    internal static class PathPlanner
    {
        private const float Cell = 8f;
        private const float ShoreOffset = 14f;   // metres inland from the water's edge
        private const float RoadGap = 7f;        // paths stop this far from a road centre line
        private const float MaxSlope = 0.28f;

        private sealed class Ctx
        {
            public Grid H, Water, Road; public int N; public float Sea; public Random Rng;
            public bool[] Blocked;   // planning cells a path may not use: the world's lots (#136)
            public float[] Avoid;    // 0..1 per cell: objects already there (trees, bushes, rocks); paths prefer the gaps
            public List<float[]> All = new List<float[]>(); // every path point so far, to keep paths apart
        }

        /// <summary>
        /// Footpaths for roads that are already built (the Footpaths tool, run after Build roads): the roads come in as
        /// polylines in metres; the town centre is where they are densest. 'targets' = the Like world's footpath figures
        /// (km, distance from roads p10 / p50 / p90, piece length; worlds.txt column 33), 'amount' scales the length.
        /// Several layouts are tried and the one closest to the targets kept; 'checks' says how close.
        /// </summary>
        public static List<FootPath> PlanOnRoads(Grid h, List<List<float[]>> roads, float sea, int seed, float[] targets, float amount,
                                                 int tries, out string checks)
        { return PlanOnRoads(h, roads, sea, seed, targets, amount, tries, null, out checks); }

        /// <summary>As above; 'blockers' = circles (x, z, radius, hard; metres on the map) paths keep out of: the world's
        /// lots (hard = 1: never crossed) and the trees, bushes and rocks already placed (hard = 0: routes prefer the gaps)
        /// (the tester, 2026-10-25: paths ran through lots and over trees). Every path point is then pushed clear of them all.</summary>
        public static List<FootPath> PlanOnRoads(Grid h, List<List<float[]>> roads, float sea, int seed, float[] targets, float amount,
                                                 int tries, List<float[]> blockers, out string checks)
        {
            float worldSize = (h.W - 1) * Cell;
            float wantKm = targets != null && targets.Length > 0 && targets[0] > 0 ? targets[0] * amount : 3f * amount;
            // the Like world's footpaths are measured on 2048 m maps (most): scale the length with the land area
            wantKm *= worldSize * worldSize / (2048f * 2048f);
            float p10 = targets != null && targets.Length > 3 && targets[1] > 0 ? targets[1] : 30, p50 = targets != null && targets.Length > 3 && targets[2] > 0 ? targets[2] : 40,
                  p90 = targets != null && targets.Length > 3 && targets[3] > 0 ? targets[3] : 200;
            List<FootPath> best = null; double bestScore = double.MaxValue; string bestNote = "";
            for (int t = 0; t < Math.Max(1, tries); t++)
            {
                RoadNet net = new RoadNet();
                foreach (List<float[]> r in roads) { Road rd = new Road(); rd.Kind = RoadKind.Street; rd.Points = r; net.Roads.Add(rd); }
                net.Centre = DensestPoint(roads, worldSize);
                net.TownRadius = 150;
                RoadSettings s = new RoadSettings();
                s.Seed = seed + t * 31; s.SeaLevel = sea; s.FootpathKm = wantKm; s.PathBlockers = blockers;
                s.PathRoadMin = Math.Max(10, p10); s.PathRoadMax = Math.Max(s.PathRoadMin + 40, p90);
                Plan(h, net, s);
                float km = net.PathKm, med = MedianRoadDistance(net, h.W);
                double score = Math.Abs(km / Math.Max(0.05f, wantKm) - 1) + 0.5 * Math.Abs(med / Math.Max(5f, p50) - 1);
                if (score < bestScore)
                {
                    bestScore = score; best = net.Paths;
                    bestNote = string.Format("Footpath checks: length {0:F1} of {1:F1} km wanted; distance from roads {2:F0} m (world type {3:F0} m); {4} paths, {5} loose ends led on",
                        km, wantKm, med, p50, net.Paths.Count, net.PathEndsJoined);
                }
            }
            int pushed = 0;
            if (best != null && blockers != null) foreach (FootPath fp in best) pushed += PushClear(fp.Points, blockers);
            checks = bestNote + (pushed > 0 ? string.Format("; {0} path point(s) moved clear of lots and objects", pushed) : "");
            return best ?? new List<FootPath>();
        }

        /// <summary>The middle of the densest 300 m of road: the town centre for paths planned round built roads.</summary>
        private static float[] DensestPoint(List<List<float[]>> roads, float worldSize)
        {
            int g = Math.Max(1, (int)(worldSize / 100)); float[] count = new float[g * g];
            foreach (List<float[]> r in roads)
                foreach (float[] p in r)
                {
                    int x = (int)(p[0] / 100), y = (int)(p[1] / 100);
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                        { int xx = x + dx, yy = y + dy; if (xx >= 0 && yy >= 0 && xx < g && yy < g) count[yy * g + xx]++; }
                }
            int bi = 0; for (int i = 1; i < count.Length; i++) if (count[i] > count[bi]) bi = i;
            return new[] { (bi % g + 0.5f) * 100, (bi / g + 0.5f) * 100 };
        }

        private static float MedianRoadDistance(RoadNet net, int n)
        {
            bool[] road = RoadCells(net, n);
            List<float> d = new List<float>();
            foreach (FootPath fp in net.Paths)
                foreach (float[] p in fp.Points)
                {
                    int cx = (int)(p[0] / Cell), cy = (int)(p[1] / Cell); float best = 400;
                    for (int dy = -40; dy <= 40; dy += 2) for (int dx = -40; dx <= 40; dx += 2)
                        {
                            int x = cx + dx, y = cy + dy;
                            if (x < 0 || y < 0 || x >= n || y >= n || !road[y * n + x]) continue;
                            float dd = (float)Math.Sqrt(dx * dx + dy * dy) * Cell; if (dd < best) best = dd;
                        }
                    d.Add(best);
                }
            if (d.Count == 0) return 0;
            d.Sort(); return d[d.Count / 2];
        }

        public static void Plan(Grid h, RoadNet net, RoadSettings s)
        {
            if (net.Centre == null) return;
            Ctx c = new Ctx();
            c.H = h; c.N = h.W; c.Sea = s.SeaLevel; c.Rng = new Random(s.Seed * 7919 + 3);
            c.Water = Distance(h, delegate (int i) { return h.D[i] <= s.SeaLevel + 0.5f; });
            c.Blocked = BlockedCells(c.N, s.PathBlockers, true);
            c.Avoid = AvoidCells(c.N, s.PathBlockers);
            bool[] road = RoadCells(net, c.N);
            c.Road = Distance(h, delegate (int i) { return road[i]; });
            float worldSize = (c.N - 1) * Cell;

            // 1. promenades along the water near town
            float zone = Math.Max(net.TownRadius * 1.6f, worldSize * 0.28f);
            for (int k = 0; k < 4; k++)
            {
                float[] seed = ShoreSeed(c, net.Centre, zone * 0.8f);
                if (seed == null) break;
                List<float[]> back = Follow(c, seed, -1, net.Centre, zone), fwd = Follow(c, seed, 1, net.Centre, zone);
                back.Reverse();
                List<float[]> line = back; line.AddRange(fwd.GetRange(1, fwd.Count - 1));
                AddPieces(c, net, line, "Promenade", 80);
            }

            // 2. a winding trail through the middle of each neighbourhood (not the farm lanes)
            for (int k = 0; k < net.HoodCentres.Count; k++)
            {
                if (net.Hoods[k] == HoodStyle.Rural) continue;
                float[] hub = net.HoodCentres[k];
                double th = c.Rng.NextDouble() * Math.PI;
                float[] a = Reach(c, hub, th), b = Reach(c, hub, th + Math.PI + (c.Rng.NextDouble() - 0.5) * 0.8);
                if (a == null || b == null || Dist(a, b) < 80) continue;
                List<float[]> t = Route(c, a, b);
                if (t != null) AddPieces(c, net, Wander(t), "Trail", 60);
            }

            // 3. a loop round the park
            foreach (LotSite site in net.Sites)
            {
                if (site.Label != "Park") continue;
                List<float[]> ring = new List<float[]>();
                double ph = c.Rng.NextDouble() * 6.3;
                bool ok = true;
                for (int i = 0; i < 16 && ok; i++)
                {
                    double a = i * Math.PI * 2 / 16;
                    float r = 22 * (float)(1 + 0.18 * Math.Sin(2 * a + ph));
                    float[] p = { site.P[0] + r * (float)Math.Cos(a), site.P[1] + r * (float)Math.Sin(a) };
                    ok = Usable(c, p) && At(c.Road, p) > RoadGap + 3;
                    ring.Add(p);
                }
                if (!ok) continue;
                FootPath fp = new FootPath(); fp.Kind = "Park"; fp.Closed = true; fp.Points = ring;
                net.Paths.Add(fp); c.All.AddRange(ring);
            }
            // 3b. as much footpath as the Like world has (EA: 0-9 km): more trails between points 30-200 m off the roads,
            // apart from the paths already there, until the target is reached
            if (s.FootpathKm > 0)
            {
                float km = Km(net);
                for (int tries = 0; tries < 400 && km < s.FootpathKm; tries++)
                {
                    float[] a = { (float)c.Rng.NextDouble() * worldSize, (float)c.Rng.NextDouble() * worldSize };
                    float ra = At(c.Road, a);
                    if (!Usable(c, a) || ra < s.PathRoadMin || ra > s.PathRoadMax || Near(c.All, a, 60)) continue;
                    double th = c.Rng.NextDouble() * Math.PI * 2; float len = 150 + (float)c.Rng.NextDouble() * 300;
                    float[] b = { a[0] + len * (float)Math.Cos(th), a[1] + len * (float)Math.Sin(th) };
                    if (!Usable(c, b) || At(c.Road, b) < s.PathRoadMin || Near(c.All, b, 60)) continue;
                    List<float[]> t = Route(c, a, b);
                    if (t == null) continue;
                    int before = net.Paths.Count;
                    AddPieces(c, net, Wander(t), "Trail", 60);
                    for (int k = before; k < net.Paths.Count; k++) km += Km(net.Paths[k].Points);
                }
            }
            // 4. no paths ending in the middle of nowhere: each loose end is led to the nearest road verge or path
            net.PathEndsJoined = ConnectEnds(c, net);
            net.PathKm = Km(net);
        }

        /// <summary>
        /// Leads every path end that isn't at a road to the nearest road verge (where Sims step onto the pavement) or
        /// another path, within ~200 m of walking, over usable ground. Ends that can't be joined stay as they are.
        /// Returns how many ends were joined.
        /// </summary>
        private static int ConnectEnds(Ctx c, RoadNet net)
        {
            int n = c.N, joined = 0;
            for (int pi = 0; pi < net.Paths.Count; pi++)
            {
                FootPath fp = net.Paths[pi];
                if (fp.Closed || fp.Points.Count < 2) continue;
                // the other paths, as grid cells
                bool[] other = new bool[n * n];
                for (int q = 0; q < net.Paths.Count; q++)
                {
                    if (q == pi) continue;
                    List<float[]> o = net.Paths[q].Points;
                    for (int k = 0; k + 1 < o.Count; k++)
                    {
                        int steps = Math.Max(1, (int)(Dist(o[k], o[k + 1]) / 4));
                        for (int t = 0; t <= steps; t++)
                        {
                            float u = (float)t / steps;
                            other[Idx(c, new[] { o[k][0] + (o[k + 1][0] - o[k][0]) * u, o[k][1] + (o[k + 1][1] - o[k][1]) * u })] = true;
                        }
                    }
                }
                for (int e = 0; e < 2; e++)
                {
                    float[] end = e == 0 ? fp.Points[0] : fp.Points[fp.Points.Count - 1];
                    if (At(c.Road, end) <= RoadGap + 3) continue;              // already at a road
                    if (other[Idx(c, end)]) continue;                          // already on another path
                    List<float[]> link = RouteToNearest(c, end, other, 200f);
                    if (link == null || link.Count < 2) continue;
                    link = Simplify(TerrainGen.Chaikin(link), 1.5f, 24f);
                    if (e == 0) { link.Reverse(); link.RemoveAt(link.Count - 1); fp.Points.InsertRange(0, link); }
                    else { link.RemoveAt(0); fp.Points.AddRange(link); }
                    c.All.AddRange(link);
                    joined++;
                }
            }
            return joined;
        }

        /// <summary>Cheapest walk from 'start' to a road verge (just outside RoadGap) or a cell in 'other', up to maxLen metres.</summary>
        private static List<float[]> RouteToNearest(Ctx c, float[] start, bool[] other, float maxLen)
        {
            int n = c.N, s = Idx(c, start);
            float[] cost = new float[n * n]; int[] from = new int[n * n];
            for (int i = 0; i < cost.Length; i++) { cost[i] = float.MaxValue; from[i] = -1; }
            List<KeyValuePair<float, int>> heap = new List<KeyValuePair<float, int>>();
            float[] len = new float[n * n];
            cost[s] = 0; Push(heap, 0, s);
            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 }, dy = { 0, 0, 1, -1, 1, -1, 1, -1 };
            int goal = -1, pops = 0;
            while (heap.Count > 0 && pops++ < 40000)
            {
                KeyValuePair<float, int> top = Pop(heap);
                int i = top.Value;
                if (top.Key > cost[i]) continue;
                float rd = c.Road.D[i];
                if (i != s && ((rd >= RoadGap - 1 && rd <= RoadGap + 2.5f) || other[i])) { goal = i; break; }
                if (len[i] > maxLen) continue;
                int x = i % n, y = i / n;
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + dx[k], ny = y + dy[k];
                    if (nx < 1 || ny < 1 || nx >= n - 1 || ny >= n - 1) continue;
                    int j = ny * n + nx;
                    if (c.H.D[j] <= c.Sea + 0.5f || c.Road.D[j] < RoadGap - 1 || (c.Blocked != null && c.Blocked[j])) continue;
                    if (!Inside(c, new[] { nx * Cell, ny * Cell })) continue;
                    float l = k < 4 ? Cell : Cell * 1.414f;
                    float grade = Math.Abs(c.H.D[j] - c.H.D[i]) / l;
                    if (grade > MaxSlope) continue;
                    float nc = cost[i] + l * (1 + 40 * grade * grade) * (c.Avoid != null ? 1 + 3 * c.Avoid[j] : 1);
                    if (nc < cost[j]) { cost[j] = nc; from[j] = i; len[j] = len[i] + l; Push(heap, nc, j); }
                }
            }
            if (goal < 0) return null;
            List<float[]> path = new List<float[]>();
            for (int i = goal; i >= 0; i = from[i]) { path.Add(new[] { (i % n) * Cell, (i / n) * Cell }); if (i == s) break; }
            path.Reverse();
            path[0] = start;
            return path;
        }

        /// <summary>Splits a line where it nears a road or leaves usable ground; keeps pieces of at least minLen metres.</summary>
        private static void AddPieces(Ctx c, RoadNet net, List<float[]> line, string kind, float minLen)
        {
            List<float[]> cur = new List<float[]>();
            for (int i = 0; i <= line.Count; i++)
            {
                bool good = i < line.Count && Usable(c, line[i]) && At(c.Road, line[i]) > RoadGap;
                if (good) { cur.Add(line[i]); continue; }
                if (Length(cur) >= minLen)
                {
                    List<float[]> p = Simplify(TerrainGen.Chaikin(TerrainGen.Chaikin(cur)), 1.5f, 24f);
                    FootPath fp = new FootPath(); fp.Kind = kind; fp.Points = p;
                    net.Paths.Add(fp); c.All.AddRange(p);
                }
                cur = new List<float[]>();
            }
        }

        /// <summary>A shore point near town, not near a path already made.</summary>
        private static float[] ShoreSeed(Ctx c, float[] centre, float zone)
        {
            float[] best = null; float bd = float.MaxValue;
            int n = c.N;
            for (int y = 2; y < n - 2; y++)
                for (int x = 2; x < n - 2; x++)
                {
                    float w = c.Water[x, y];
                    if (Math.Abs(w - ShoreOffset) > 4 || c.Road[x, y] < RoadGap + 6) continue;
                    float[] p = { x * Cell, y * Cell };
                    float d = Dist(p, centre);
                    if (d > zone || d >= bd || !Usable(c, p) || Near(c, p, 70)) continue;
                    bd = d; best = p;
                }
            return best;
        }

        /// <summary>Walks along the line ShoreOffset metres from the water, in direction 'sign', inside the zone.</summary>
        private static List<float[]> Follow(Ctx c, float[] start, int sign, float[] centre, float zone)
        {
            List<float[]> pts = new List<float[]>(); pts.Add(start);
            float[] p = start, prevDir = null;
            float len = 0;
            for (int step = 0; step < 500; step++)
            {
                float[] g = Grad(c.Water, p);
                if (g == null) break;
                float[] t = { -g[1] * sign, g[0] * sign };
                if (prevDir != null && t[0] * prevDir[0] + t[1] * prevDir[1] < 0) { t[0] = -t[0]; t[1] = -t[1]; }
                float[] q = { p[0] + t[0] * 6, p[1] + t[1] * 6 };
                float err = ShoreOffset - At(c.Water, q);
                float corr = Math.Max(-3, Math.Min(3, err * 0.8f));
                float[] g2 = Grad(c.Water, q);
                if (g2 != null) { q[0] += g2[0] * corr; q[1] += g2[1] * corr; }
                if (!Inside(c, q) || Dist(q, centre) > zone || Math.Abs(At(c.Water, q) - ShoreOffset) > 8) break;
                if (len > 60 && Dist(q, start) < 10) { pts.Add(start); break; }   // went all the way round
                if (Near(c, q, 30)) break;                                           // another path is here
                prevDir = new[] { (q[0] - p[0]) / 6, (q[1] - p[1]) / 6 };
                pts.Add(q); len += 6; p = q;
            }
            return pts;
        }

        /// <summary>From the neighbourhood hub along angle th until 14 m short of a road (inside its loop).</summary>
        private static float[] Reach(Ctx c, float[] hub, double th)
        {
            float dx = (float)Math.Cos(th), dy = (float)Math.Sin(th);
            float[] last = null;
            for (float r = 0; r < 260; r += 4)
            {
                float[] p = { hub[0] + dx * r, hub[1] + dy * r };
                if (!Usable(c, p)) break;
                if (At(c.Road, p) < 14) { if (r > 30) break; else continue; } // next to the hub's own road: keep going
                last = p;
            }
            return last;
        }

        /// <summary>Cheapest walk from a to b on the grid: gentle ground, away from roads, never on water.</summary>
        private static List<float[]> Route(Ctx c, float[] a, float[] b)
        {
            int n = c.N;
            int s = Idx(c, a), goal = Idx(c, b);
            float[] cost = new float[n * n]; int[] from = new int[n * n];
            for (int i = 0; i < cost.Length; i++) { cost[i] = float.MaxValue; from[i] = -1; }
            List<KeyValuePair<float, int>> heap = new List<KeyValuePair<float, int>>();
            cost[s] = 0; Push(heap, 0, s);
            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 }, dy = { 0, 0, 1, -1, 1, -1, 1, -1 };
            int pops = 0;
            while (heap.Count > 0 && pops++ < 60000)
            {
                KeyValuePair<float, int> top = Pop(heap);
                int i = top.Value;
                if (i == goal) break;
                if (top.Key > cost[i] + HeurM(i, goal, n)) continue;
                int x = i % n, y = i / n;
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + dx[k], ny = y + dy[k];
                    if (nx < 1 || ny < 1 || nx >= n - 1 || ny >= n - 1) continue;
                    int j = ny * n + nx;
                    if (c.H.D[j] <= c.Sea + 0.5f || (c.Blocked != null && c.Blocked[j])) continue;
                    float len = k < 4 ? Cell : Cell * 1.414f;
                    float grade = Math.Abs(c.H.D[j] - c.H.D[i]) / len;
                    if (grade > MaxSlope) continue;
                    float rd = c.Road.D[j];
                    float step = len * (1 + 40 * grade * grade + (rd < RoadGap + 4 ? 30 : rd < 20 ? 2 : 0)) * (c.Avoid != null ? 1 + 3 * c.Avoid[j] : 1);
                    float nc = cost[i] + step;
                    if (nc < cost[j]) { cost[j] = nc; from[j] = i; Push(heap, nc + HeurM(j, goal, n), j); }
                }
            }
            if (from[goal] < 0) return null;
            List<float[]> path = new List<float[]>();
            for (int i = goal; i >= 0; i = from[i]) { path.Add(new[] { (i % n) * Cell, (i / n) * Cell }); if (i == s) break; }
            path.Reverse();
            path[0] = a; path[path.Count - 1] = b;
            return path;
        }

        /// <summary>Takes the grid out of a route and lets it meander a little, like EA's park trails.</summary>
        private static List<float[]> Wander(List<float[]> p)
        {
            p = Simplify(p, 6f, 48f);
            List<float[]> r = new List<float[]>();
            for (int i = 0; i + 1 < p.Count; i++)
            {
                float L = Dist(p[i], p[i + 1]);
                int steps = Math.Max(1, (int)(L / 6));
                for (int k = 0; k < steps; k++) { float t = (float)k / steps; r.Add(new[] { p[i][0] + (p[i + 1][0] - p[i][0]) * t, p[i][1] + (p[i + 1][1] - p[i][1]) * t }); }
            }
            r.Add(p[p.Count - 1]);
            float along = 0, total = Length(r);
            double ph = p[0][0] * 0.013 + p[0][1] * 0.007; // varies per trail
            List<float[]> o = new List<float[]>();
            for (int i = 0; i < r.Count; i++)
            {
                if (i > 0) along += Dist(r[i], r[i - 1]);
                float[] d = Dir(r[Math.Max(0, i - 1)], r[Math.Min(r.Count - 1, i + 1)]);
                float off = (float)(8 * Math.Sin(along / 41.0 + ph) + 4 * Math.Sin(along / 15.0 + 1.7 + ph));
                float fade = Math.Max(0, Math.Min(1, Math.Min(along, total - along) / 20)); // ends stay put
                o.Add(new[] { r[i][0] - d[1] * off * fade, r[i][1] + d[0] * off * fade });
            }
            return o;
        }

        // ---------- fields ----------

        private delegate bool CellTest(int i);

        /// <summary>Distance in metres to the nearest cell passing 'src' (two-pass chamfer).</summary>
        private static Grid Distance(Grid h, CellTest src)
        {
            int n = h.W;
            Grid d = new Grid(n, n);
            for (int i = 0; i < n * n; i++) d.D[i] = src(i) ? 0 : 1e6f;
            float a = Cell, b = Cell * 1.414f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float v = d[x, y];
                    if (x > 0) v = Math.Min(v, d[x - 1, y] + a);
                    if (y > 0) v = Math.Min(v, d[x, y - 1] + a);
                    if (x > 0 && y > 0) v = Math.Min(v, d[x - 1, y - 1] + b);
                    if (x < n - 1 && y > 0) v = Math.Min(v, d[x + 1, y - 1] + b);
                    d[x, y] = v;
                }
            for (int y = n - 1; y >= 0; y--)
                for (int x = n - 1; x >= 0; x--)
                {
                    float v = d[x, y];
                    if (x < n - 1) v = Math.Min(v, d[x + 1, y] + a);
                    if (y < n - 1) v = Math.Min(v, d[x, y + 1] + a);
                    if (x < n - 1 && y < n - 1) v = Math.Min(v, d[x + 1, y + 1] + b);
                    if (x > 0 && y < n - 1) v = Math.Min(v, d[x - 1, y + 1] + b);
                    d[x, y] = v;
                }
            return d;
        }

        private static bool[] RoadCells(RoadNet net, int n)
        {
            bool[] r = new bool[n * n];
            foreach (Road rd in net.Roads)
                for (int i = 0; i + 1 < rd.Points.Count; i++)
                {
                    float[] a = rd.Points[i], b = rd.Points[i + 1];
                    int steps = Math.Max(1, (int)(Dist(a, b) / 2));
                    for (int k = 0; k <= steps; k++)
                    {
                        float t = (float)k / steps;
                        int x = (int)Math.Round((a[0] + (b[0] - a[0]) * t) / Cell), y = (int)Math.Round((a[1] + (b[1] - a[1]) * t) / Cell);
                        if (x >= 0 && y >= 0 && x < n && y < n) r[y * n + x] = true;
                    }
                }
            return r;
        }

        // ---------- helpers ----------

        /// <summary>Planning cells (Cell m) whose centre lies inside a hard blocker circle (a lot: 4th value 1) widened by
        /// half a cell.</summary>
        private static bool[] BlockedCells(int n, List<float[]> blockers, bool hard)
        {
            if (blockers == null || blockers.Count == 0) return null;
            bool[] b = new bool[n * n];
            foreach (float[] o in blockers)
            {
                if ((o.Length > 3 && o[3] > 0) != hard) continue;
                float r = o[2] + Cell * 0.5f;
                int x0 = Math.Max(0, (int)((o[0] - r) / Cell)), x1 = Math.Min(n - 1, (int)((o[0] + r) / Cell) + 1);
                int y0 = Math.Max(0, (int)((o[1] - r) / Cell)), y1 = Math.Min(n - 1, (int)((o[1] + r) / Cell) + 1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        if ((x * Cell - o[0]) * (x * Cell - o[0]) + (y * Cell - o[1]) * (y * Cell - o[1]) < r * r) b[y * n + x] = true;
            }
            return b;
        }

        /// <summary>Per planning cell, how crowded it is with soft blockers (objects: 4th value 0): 0.5 per object near its
        /// centre, at most 1. Routes cost up to 4x through it, so paths take the gaps through woods (ChatGPT's review, #136).</summary>
        private static float[] AvoidCells(int n, List<float[]> blockers)
        {
            if (blockers == null || blockers.Count == 0) return null;
            float[] a = new float[n * n];
            foreach (float[] o in blockers)
            {
                if (o.Length > 3 && o[3] > 0) continue;
                float r = o[2] + Cell * 0.5f;
                int x0 = Math.Max(0, (int)((o[0] - r) / Cell)), x1 = Math.Min(n - 1, (int)((o[0] + r) / Cell) + 1);
                int y0 = Math.Max(0, (int)((o[1] - r) / Cell)), y1 = Math.Min(n - 1, (int)((o[1] + r) / Cell) + 1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        if ((x * Cell - o[0]) * (x * Cell - o[0]) + (y * Cell - o[1]) * (y * Cell - o[1]) < r * r) a[y * n + x] = Math.Min(1f, a[y * n + x] + 0.5f);
            }
            return a;
        }

        /// <summary>Pushes every path point out of the blocker circles (plus 1 m); returns how many moved.</summary>
        private static int PushClear(List<float[]> pts, List<float[]> blockers)
        {
            int moved = 0;
            for (int k = 0; k < pts.Count; k++)
            {
                float[] p = pts[k];
                for (int pass = 0; pass < 3; pass++)
                {
                    bool any = false;
                    foreach (float[] o in blockers)
                    {
                        float dx = p[0] - o[0], dz = p[1] - o[1], r = o[2] + 1f, d2 = dx * dx + dz * dz;
                        if (d2 >= r * r) continue;
                        float d = (float)Math.Sqrt(d2);
                        if (d < 1e-3f) { dx = 1; dz = 0; d = 1; }
                        p = new[] { o[0] + dx / d * r, o[1] + dz / d * r }; any = true;
                    }
                    if (!any) break;
                    if (pass == 0) moved++;
                }
                pts[k] = p;
            }
            return moved;
        }

        private static bool Usable(Ctx c, float[] p)
        {
            if (!Inside(c, p) || TerrainGen.Sample(c.H, p[0] / Cell, p[1] / Cell) <= c.Sea + 0.5f) return false;
            if (c.Blocked != null)
            {
                int bx = (int)Math.Round(p[0] / Cell), by = (int)Math.Round(p[1] / Cell);
                if (bx >= 0 && by >= 0 && bx < c.N && by < c.N && c.Blocked[by * c.N + bx]) return false;
            }
            float[] g = { TerrainGen.Sample(c.H, p[0] / Cell + 1, p[1] / Cell) - TerrainGen.Sample(c.H, p[0] / Cell - 1, p[1] / Cell),
                          TerrainGen.Sample(c.H, p[0] / Cell, p[1] / Cell + 1) - TerrainGen.Sample(c.H, p[0] / Cell, p[1] / Cell - 1) };
            return Math.Sqrt(g[0] * g[0] + g[1] * g[1]) / (2 * Cell) < MaxSlope;
        }

        private static bool Near(Ctx c, float[] p, float r)
        {
            foreach (float[] q in c.All) if (Sq(q[0] - p[0]) + Sq(q[1] - p[1]) < r * r) return true;
            return false;
        }

        private const float Rim = 60f; // no paths under the distant terrain at the map's edge

        private static bool Inside(Ctx c, float[] p) { float m = (c.N - 1) * Cell - Rim; return p[0] > Rim && p[1] > Rim && p[0] < m && p[1] < m; }
        private static float At(Grid g, float[] p) { return TerrainGen.Sample(g, p[0] / Cell, p[1] / Cell); }

        private static float[] Grad(Grid g, float[] p)
        {
            float gx = At(g, new[] { p[0] + 4, p[1] }) - At(g, new[] { p[0] - 4, p[1] });
            float gy = At(g, new[] { p[0], p[1] + 4 }) - At(g, new[] { p[0], p[1] - 4 });
            float l = (float)Math.Sqrt(gx * gx + gy * gy);
            return l < 1e-4f ? null : new[] { gx / l, gy / l };
        }

        private static int Idx(Ctx c, float[] p)
        {
            int x = Math.Max(0, Math.Min(c.N - 1, (int)Math.Round(p[0] / Cell))), y = Math.Max(0, Math.Min(c.N - 1, (int)Math.Round(p[1] / Cell)));
            return y * c.N + x;
        }

        private static float HeurM(int a, int b, int n) { return (float)Math.Sqrt(Sq(a % n - b % n) + Sq(a / n - b / n)) * Cell; }

        private static void Push(List<KeyValuePair<float, int>> h, float k, int v)
        {
            h.Add(new KeyValuePair<float, int>(k, v));
            int i = h.Count - 1;
            while (i > 0) { int p = (i - 1) / 2; if (h[p].Key <= h[i].Key) break; KeyValuePair<float, int> t = h[p]; h[p] = h[i]; h[i] = t; i = p; }
        }

        private static KeyValuePair<float, int> Pop(List<KeyValuePair<float, int>> h)
        {
            KeyValuePair<float, int> top = h[0];
            h[0] = h[h.Count - 1]; h.RemoveAt(h.Count - 1);
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, m = i;
                if (l < h.Count && h[l].Key < h[m].Key) m = l;
                if (r < h.Count && h[r].Key < h[m].Key) m = r;
                if (m == i) break;
                KeyValuePair<float, int> t = h[m]; h[m] = h[i]; h[i] = t; i = m;
            }
            return top;
        }

        /// <summary>Douglas-Peucker to 'tol' metres, keeping a point at least every maxLen metres.</summary>
        private static List<float[]> Simplify(List<float[]> p, float tol, float maxLen)
        {
            if (p.Count < 3) return new List<float[]>(p);
            bool[] keep = new bool[p.Count]; keep[0] = keep[p.Count - 1] = true;
            Dp(p, 0, p.Count - 1, tol, keep);
            List<float[]> o = new List<float[]>(); o.Add(p[0]);
            for (int i = 1; i < p.Count; i++)
                if (keep[i] || (i + 1 < p.Count && Dist(o[o.Count - 1], p[i + 1]) > maxLen)) o.Add(p[i]);
            return o;
        }

        private static void Dp(List<float[]> p, int a, int b, float tol, bool[] keep)
        {
            if (b <= a + 1) return;
            float best = -1; int bi = -1;
            for (int i = a + 1; i < b; i++)
            {
                float d = SegDist(p[i], p[a], p[b]);
                if (d > best) { best = d; bi = i; }
            }
            if (best > tol) { keep[bi] = true; Dp(p, a, bi, tol, keep); Dp(p, bi, b, tol, keep); }
        }

        private static float SegDist(float[] p, float[] a, float[] b)
        {
            float dx = b[0] - a[0], dy = b[1] - a[1], L = dx * dx + dy * dy;
            float t = L < 1e-6f ? 0 : Math.Max(0, Math.Min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / L));
            return (float)Math.Sqrt(Sq(p[0] - a[0] - t * dx) + Sq(p[1] - a[1] - t * dy));
        }

        private static float Length(List<float[]> p) { float l = 0; for (int i = 1; i < p.Count; i++) l += Dist(p[i], p[i - 1]); return l; }
        private static float Km(RoadNet net) { float k = 0; foreach (FootPath p in net.Paths) k += Km(p.Points); return k; }

        private static float Km(List<float[]> p) { float k = 0; for (int i = 1; i < p.Count; i++) k += Dist(p[i], p[i - 1]) / 1000f; return k; }

        private static bool Near(List<float[]> pts, float[] q, float r)
        {
            foreach (float[] p in pts) if (Sq(p[0] - q[0]) + Sq(p[1] - q[1]) < r * r) return true;
            return false;
        }

        private static float Dist(float[] a, float[] b) { return (float)Math.Sqrt(Sq(a[0] - b[0]) + Sq(a[1] - b[1])); }
        private static float[] Dir(float[] a, float[] b) { float l = Math.Max(1e-4f, Dist(a, b)); return new[] { (b[0] - a[0]) / l, (b[1] - a[1]) / l }; }
        private static float Sq(float v) { return v * v; }
    }
}
