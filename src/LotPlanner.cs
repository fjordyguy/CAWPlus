using System;
using System.Collections.Generic;
using System.Globalization;

namespace CawPlus
{
    /// <summary>
    /// A planned lot, as CAW stores one (tools/lots.py, measured on EA's lots): (X, Z) is a corner, the width runs along U
    /// and the depth along V = (-U.z, U.x), so V points away from the road the lot fronts. Metres on the terrain map.
    /// </summary>
    internal sealed class PlannedLot
    {
        public float X, Z, Ux, Uz;
        public int Width, Depth;   // width = frontage along the road, depth = back from it
        public bool Community;
        public string Kind;        // community lots: beach, fishing, park, pool, cemetery, civic, venue or other (LotKind)
        public float Vx { get { return -Uz; } }
        public float Vz { get { return Ux; } }
        public float[] Centre { get { return new[] { X + Ux * Width / 2f + Vx * Depth / 2f, Z + Uz * Width / 2f + Vz * Depth / 2f }; } }
    }

    /// <summary>
    /// A world's lots, measured from its Lots/*.lot files against its roads and heightmap (worlds.txt columns 34-35):
    /// count, community lots, share fronting a road, front edge to road centre line, gap to the next lot along the road
    /// (10th/50th/90th percentile), share of neighbouring pads more than 2 m apart in height (terraces), and the size mix
    /// as frontage x depth.
    /// </summary>
    internal sealed class LotTargets
    {
        public int Count, Community;
        public float OnRoad = 0.9f, Setback = 6f, Gap10 = 2f, Gap50 = 12f, Gap90 = 75f, StepShare;
        public readonly List<int[]> Sizes = new List<int[]>(); // frontage, depth, count
        public List<LotKind> Kinds; // community lots by kind (worlds.txt column 41); null = the largest lots, no kinds

        public static LotTargets Parse(string figures, string sizes)
        {
            if (string.IsNullOrEmpty(figures)) return null;
            string[] f = figures.Split(',');
            if (f.Length < 8) return null;
            LotTargets t = new LotTargets();
            CultureInfo ic = CultureInfo.InvariantCulture;
            t.Count = int.Parse(f[0], ic); t.Community = int.Parse(f[1], ic);
            t.OnRoad = float.Parse(f[2], ic); t.Setback = float.Parse(f[3], ic);
            t.Gap10 = float.Parse(f[4], ic); t.Gap50 = float.Parse(f[5], ic); t.Gap90 = float.Parse(f[6], ic);
            t.StepShare = float.Parse(f[7], ic);
            if (!string.IsNullOrEmpty(sizes))
                foreach (string s in sizes.Split(';'))
                {
                    string[] a = s.Split(':'); if (a.Length != 2) continue;
                    string[] wd = a[0].Split('x'); if (wd.Length != 2) continue;
                    t.Sizes.Add(new[] { int.Parse(wd[0], ic), int.Parse(wd[1], ic), int.Parse(a[1], ic) });
                }
            if (t.Sizes.Count == 0) t.Sizes.Add(new[] { 30, 30, 1 });
            return t;
        }

        /// <summary>The median of each figure over several worlds, and their size mixes added together; null if none.</summary>
        public static LotTargets Typical(List<LotTargets> all)
        {
            if (all.Count == 0) return null;
            LotTargets t = new LotTargets();
            t.Count = (int)Median(all, delegate (LotTargets a) { return a.Count; });
            t.Community = (int)Median(all, delegate (LotTargets a) { return a.Community; });
            t.OnRoad = Median(all, delegate (LotTargets a) { return a.OnRoad; });
            t.Setback = Median(all, delegate (LotTargets a) { return a.Setback; });
            t.Gap10 = Median(all, delegate (LotTargets a) { return a.Gap10; });
            t.Gap50 = Median(all, delegate (LotTargets a) { return a.Gap50; });
            t.Gap90 = Median(all, delegate (LotTargets a) { return a.Gap90; });
            t.StepShare = Median(all, delegate (LotTargets a) { return a.StepShare; });
            Dictionary<string, int[]> sizes = new Dictionary<string, int[]>();
            foreach (LotTargets a in all)
                foreach (int[] z in a.Sizes)
                {
                    string k = z[0] + "x" + z[1];
                    int[] v; if (!sizes.TryGetValue(k, out v)) { v = new[] { z[0], z[1], 0 }; sizes[k] = v; }
                    v[2] += z[2];
                }
            t.Sizes.AddRange(sizes.Values);
            List<List<LotKind>> kinds = new List<List<LotKind>>();
            foreach (LotTargets a in all) if (a.Kinds != null) kinds.Add(a.Kinds);
            t.Kinds = LotKind.Typical(kinds);
            return t;
        }

        private delegate float Field(LotTargets t);

        private static float Median(List<LotTargets> all, Field f)
        {
            List<float> v = new List<float>();
            foreach (LotTargets t in all) v.Add(f(t));
            v.Sort();
            return v[v.Count / 2];
        }

        /// <summary>Mean frontage plus the median gap: metres of road side one lot takes.</summary>
        public float FrontagePerLot()
        {
            float sum = 0; int n = 0;
            foreach (int[] s in Sizes) { sum += s[0] * s[2]; n += s[2]; }
            return (n > 0 ? sum / n : 30) + Gap50;
        }
    }

    /// <summary>
    /// One kind of community lot in a world (tools/lottypes.py, from EA's lot names): how many, the median distance from
    /// the town core as a ratio of the median lot's (civic buildings 0.35-1.0, beaches 1-3.4: the coast is away from the
    /// core) and the median area (m2).
    /// </summary>
    internal sealed class LotKind
    {
        public string Name;
        public int Count;
        public float CoreRatio = 1, Area = 1600;

        /// <summary>A beach lot's middle is at most this far from the water (m).</summary>
        public const float BeachReach = 60f;

        /// <summary>The kinds in the order they are given out: those that need a place (water, the core) first.</summary>
        public static readonly string[] Order = { "beach", "fishing", "civic", "cemetery", "pool", "park", "venue", "other" };

        /// <summary>'type:count:core ratio:water m:area m2;...' (worlds.txt column 41; water m is not used); null if empty.</summary>
        public static List<LotKind> Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            CultureInfo ic = CultureInfo.InvariantCulture;
            List<LotKind> l = new List<LotKind>();
            foreach (string e in s.Split(';'))
            {
                string[] f = e.Split(':');
                if (f.Length < 5) continue;
                LotKind k = new LotKind();
                k.Name = f[0].Trim(); k.Count = int.Parse(f[1], ic); k.CoreRatio = float.Parse(f[2], ic);
                k.Area = float.Parse(f[4], ic); // f[3]: median distance to water, not used
                l.Add(k);
            }
            return l.Count > 0 ? l : null;
        }

        /// <summary>Over several worlds: counts added together (so the shares are the worlds' mix), the medians of the rest.</summary>
        public static List<LotKind> Typical(List<List<LotKind>> all)
        {
            if (all.Count == 0) return null;
            List<LotKind> l = new List<LotKind>();
            foreach (string n in Order)
            {
                List<float> core = new List<float>(), area = new List<float>();
                int count = 0;
                foreach (List<LotKind> w in all)
                    foreach (LotKind k in w)
                        if (k.Name == n) { count += k.Count; core.Add(k.CoreRatio); area.Add(k.Area); }
                if (count == 0) continue;
                core.Sort(); area.Sort();
                LotKind t = new LotKind();
                t.Name = n; t.Count = count; t.CoreRatio = core[core.Count / 2];
                t.Area = area[area.Count / 2];
                l.Add(t);
            }
            return l;
        }

        /// <summary>
        /// Gives 'community' of the lots a kind, in the world's mix (largest remainder), each kind to the lots that suit it
        /// best: beaches within 60 m of water (fishing spots 120 m; a kind with no such lot left becomes a park), then by
        /// distance from the centre against the world's ratio (of this plan's median lot distance) and by size.
        /// Without kinds: the largest lots, as before (v0.43).
        /// </summary>
        public static void Assign(List<PlannedLot> lots, int community, List<LotKind> kinds, Grid h, float cell, float sea, float[] centre, float[] wd = null)
        {
            foreach (PlannedLot l in lots) { l.Community = false; l.Kind = null; }
            community = Math.Min(community, lots.Count);
            if (community <= 0) return;
            if (kinds == null || kinds.Count == 0 || centre == null)
            {
                List<PlannedLot> bySize = new List<PlannedLot>(lots);
                bySize.Sort(delegate (PlannedLot a, PlannedLot b) { return (b.Width * b.Depth).CompareTo(a.Width * a.Depth); });
                for (int k = 0; k < community; k++) bySize[k].Community = true;
                return;
            }
            Dictionary<string, int> quota = Quotas(kinds, community);
            // water distance and centre distance per lot
            if (wd == null) wd = WaterDistance(h, cell, sea);
            int n = lots.Count;
            float[] dc = new float[n], dw = new float[n];
            List<float> all = new List<float>();
            for (int i = 0; i < n; i++)
            {
                float[] c = lots[i].Centre;
                dc[i] = Dist(c, centre); all.Add(dc[i]);
                dw[i] = At(wd, h, cell, c);
            }
            all.Sort();
            float spread = Math.Max(50f, all[all.Count / 2]);
            bool[] used = new bool[n];
            int toPark = 0;
            foreach (string name in Order)
            {
                LotKind k = null;
                foreach (LotKind q in kinds) if (q.Name == name) k = q;
                int want; if (k == null || !quota.TryGetValue(name, out want)) want = 0;
                if (name == "park") want += toPark;
                if (want <= 0) continue;
                if (k == null) { k = new LotKind(); k.Name = name; k.CoreRatio = 1; }
                float near = name == "beach" ? BeachReach : name == "fishing" ? 120f : float.MaxValue;
                List<KeyValuePair<int, double>> cand = new List<KeyValuePair<int, double>>();
                for (int i = 0; i < n; i++)
                {
                    if (used[i] || dw[i] > near) continue;
                    PlannedLot l = lots[i];
                    double sc = Math.Abs(Math.Log((dc[i] / spread + 0.1) / (k.CoreRatio + 0.1)))
                              + 0.5 * Math.Abs(Math.Log(Math.Max(100f, l.Width * l.Depth) / Math.Max(100f, k.Area)));
                    if (near < float.MaxValue) sc += dw[i] / near;
                    cand.Add(new KeyValuePair<int, double>(i, sc));
                }
                cand.Sort(delegate (KeyValuePair<int, double> a, KeyValuePair<int, double> b) { return a.Value.CompareTo(b.Value); });
                int got = 0;
                for (int c = 0; c < cand.Count && got < want; c++, got++) { used[cand[c].Key] = true; lots[cand[c].Key].Community = true; lots[cand[c].Key].Kind = name; }
                if (got < want && name != "park" && name != "venue" && name != "other") toPark += want - got;
            }
        }

        /// <summary>'community' lots shared out in the world's mix of kinds (largest remainder).</summary>
        public static Dictionary<string, int> Quotas(List<LotKind> kinds, int community)
        {
            int total = 0; foreach (LotKind k in kinds) total += k.Count;
            Dictionary<string, int> quota = new Dictionary<string, int>();
            List<KeyValuePair<string, double>> rest = new List<KeyValuePair<string, double>>();
            int given = 0;
            foreach (LotKind k in kinds)
            {
                double q = (double)community * k.Count / Math.Max(1, total);
                quota[k.Name] = (int)q; given += (int)q;
                rest.Add(new KeyValuePair<string, double>(k.Name, q - (int)q));
            }
            rest.Sort(delegate (KeyValuePair<string, double> a, KeyValuePair<string, double> b) { return b.Value.CompareTo(a.Value); });
            for (int i = 0; given < community && i < rest.Count; i++, given++) quota[rest[i].Key]++;
            return quota;
        }

        /// <summary>Water distance (m) at a lot's middle.</summary>
        public static float At(float[] wd, Grid h, float cell, float[] c)
        {
            int x = Math.Max(0, Math.Min(h.W - 1, (int)Math.Round(c[0] / cell))), z = Math.Max(0, Math.Min(h.H - 1, (int)Math.Round(c[1] / cell)));
            return wd[z * h.W + x];
        }

        /// <summary>Distance (m) from every cell to the nearest cell below the sea (+ 0.2 m): sea, lakes and rivers.</summary>
        public static float[] WaterDistance(Grid h, float cell, float sea)
        {
            int w = h.W, hh = h.H, N = w * hh;
            float[] d = new float[N];
            for (int i = 0; i < N; i++) d[i] = h.D[i] < sea + 0.2f ? 0 : 1e9f;
            float d1 = cell, d2 = cell * 1.4142f;
            for (int y = 0; y < hh; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x; float v = d[i];
                    if (x > 0) v = Math.Min(v, d[i - 1] + d1);
                    if (y > 0) { v = Math.Min(v, d[i - w] + d1); if (x > 0) v = Math.Min(v, d[i - w - 1] + d2); if (x < w - 1) v = Math.Min(v, d[i - w + 1] + d2); }
                    d[i] = v;
                }
            for (int y = hh - 1; y >= 0; y--)
                for (int x = w - 1; x >= 0; x--)
                {
                    int i = y * w + x; float v = d[i];
                    if (x < w - 1) v = Math.Min(v, d[i + 1] + d1);
                    if (y < hh - 1) { v = Math.Min(v, d[i + w] + d1); if (x < w - 1) v = Math.Min(v, d[i + w + 1] + d2); if (x > 0) v = Math.Min(v, d[i + w - 1] + d2); }
                    d[i] = v;
                }
            return d;
        }

        private static float Dist(float[] a, float[] b) { float dx = a[0] - b[0], dz = a[1] - b[1]; return (float)Math.Sqrt(dx * dx + dz * dz); }
    }

    /// <summary>
    /// Places lots along planned roads the way EA's worlds have them (measured on 22 worlds, tools/lots.py and the lot
    /// study, 2026-10-22): the front edge 6 m from the road's centre line, lots side by side with the world's own gaps
    /// (median 5-15 m in towns), frontage x depth from its own size mix, town roads first. A lot needs land under all of
    /// it, no road or other lot across it, keeps off the map rim, and the ground under it may rise only so much (EA cuts
    /// pads into hillsides: steeper on terraced worlds such as Shang Simla, Isla Paradiso).
    /// </summary>
    internal static class LotPlanner
    {
        private const float Rim = 70f;       // metres kept clear at the map edge (under the distant terrain)
        private const float RoadClear = 4.5f; // half a road: no other road nearer the lot than this

        /// <summary>Beach lots of their own on the shore when the road lots don't reach the water. Off by default: they don't
        /// front a road (EA's do), so the share of lots on roads drops (Fidelity roads -0.55 on 8 coastal worlds).</summary>
        public static bool ShoreBeaches = false;

        /// <summary>The Like world's lot count scaled by how much land this map has against it (0.6-1.3 x).</summary>
        public static int Wanted(LotTargets t, TerrainProfile ea, float eaWorldM, Grid h, float cell, float sea)
        {
            if (t == null) return 0;
            int land = 0; foreach (float v in h.D) if (v > sea + 0.5f) land++;
            float ours = land * cell * cell;
            float theirs = ea != null && eaWorldM > 0 ? (1 - ea.Water) * eaWorldM * eaWorldM : ours;
            return (int)Math.Round(t.Count * Math.Max(0.6f, Math.Min(1.3f, ours / Math.Max(1f, theirs))));
        }

        public static List<PlannedLot> Place(Grid h, float cell, RoadNet net, float sea, LotTargets t, int want, int seed, bool roadsOnly = false, TownShape shape = null,
                                             float largest = 0)
        {
            List<PlannedLot> lots = new List<PlannedLot>();
            if (t == null || want <= 0 || net.Roads.Count == 0) return lots;
            Random rng = new Random(seed * 7349 + 11);
            float world = (h.W - 1) * cell;
            // the size bag: the world's mix, drawn in a shuffled order
            List<int[]> bag = new List<int[]>();
            foreach (int[] s in t.Sizes) for (int k = 0; k < s[2]; k++) bag.Add(new[] { s[0], s[1] });
            for (int k = bag.Count - 1; k > 0; k--) { int j = rng.Next(k + 1); int[] tmp = bag[k]; bag[k] = bag[j]; bag[j] = tmp; }
            int next = 0;
            // the world's sizes, shallowest first (one of each)
            List<int[]> shallow = new List<int[]>();
            foreach (int[] sz in t.Sizes) { bool seen = false; foreach (int[] q in shallow) if (q[0] == sz[0] && q[1] == sz[1]) seen = true; if (!seen) shallow.Add(new[] { sz[0], sz[1] }); }
            shallow.Sort(delegate (int[] a, int[] b) { return a[1] != b[1] ? a[1].CompareTo(b[1]) : a[0].CompareTo(b[0]); });
            if (shallow.Count > 4) shallow.RemoveRange(4, shallow.Count - 4);
            // road points on a 16 m hash, to keep lots off other roads
            Dictionary<long, List<float[]>> hash = new Dictionary<long, List<float[]>>();
            foreach (Road r in net.Roads)
                for (int k = 1; k < r.Points.Count; k++)
                {
                    float[] a = r.Points[k - 1], b = r.Points[k];
                    float len = (float)Math.Sqrt(Sq(b[0] - a[0]) + Sq(b[1] - a[1]));
                    for (float d = 0; d <= len; d += 2f)
                    {
                        float[] q = { a[0] + (b[0] - a[0]) * d / Math.Max(len, 1e-3f), a[1] + (b[1] - a[1]) * d / Math.Max(len, 1e-3f) };
                        long key = Key(q[0], q[1]);
                        List<float[]> l; if (!hash.TryGetValue(key, out l)) { l = new List<float[]>(); hash[key] = l; }
                        l.Add(q);
                    }
                }
            // keep lots clear of the end caps CAW Plus puts on dead ends (the tester: an end piece was laid on a lot): a ring
            // of points 9 m round every dead end, so with RoadClear nothing within ~13 m (junction corners keep their
            // corner lots, as EA's do: the roads already keep lots 4.5 m off)
            List<float[]> ends = new List<float[]>();
            List<float[]> all = new List<float[]>();
            foreach (Road r in net.Roads) if (r.Points.Count > 1) { all.Add(r.Points[0]); all.Add(r.Points[r.Points.Count - 1]); }
            for (int a = 0; a < all.Count; a++)
            {
                int near = 0; // an end shared with another road is a junction or a pass-through, not a cap
                for (int b = 0; b < all.Count; b++) if (b != a && Dist(all[a], all[b]) < 3f) near++;
                foreach (float[] j in net.Junctions) if (Dist(all[a], j) < 3f) near++; // a T on another road's middle
                if (near == 0) ends.Add(all[a]);
            }
            foreach (float[] e in ends)
                for (int k = 0; k < 12; k++)
                {
                    double ang = k * Math.PI / 6;
                    float[] q = { e[0] + 9f * (float)Math.Cos(ang), e[1] + 9f * (float)Math.Sin(ang) };
                    long key = Key(q[0], q[1]);
                    List<float[]> l; if (!hash.TryGetValue(key, out l)) { l = new List<float[]>(); hash[key] = l; }
                    l.Add(q);
                }
            // town roads first: the ones nearest the centre
            float[] centre = net.Centre ?? new[] { world / 2, world / 2 };
            List<Road> order = new List<Road>(net.Roads);
            order.Sort(delegate (Road a, Road b) { return MidDist(a, centre).CompareTo(MidDist(b, centre)); });
            float maxRise = 5f + 25f * t.StepShare; // metres the ground may rise across a lot (EA cuts pads into slopes)
            // 1. every road, both sides, with the world's own gaps; if that leaves the world short, again on steeper
            // ground (EA cuts pads into hillsides: Midnight Hollow, Lucky Palms' mesas)
            int onRoadWant = (int)Math.Round(want * Math.Max(0.1f, Math.Min(1f, t.OnRoad)));
            Dictionary<PlannedLot, int> roadOf = new Dictionary<PlannedLot, int>(); // which road each lot fronts
            for (int pass = 0; pass < 2; pass++)
            {
            if (pass == 1) { if (lots.Count >= onRoadWant) break; maxRise *= 2f; }
            foreach (Road r in order)
            {
                List<float[]> p = Resample(r.Points, 2f);
                if (p.Count < 6) continue;
                float total = (p.Count - 1) * 2f;
                for (int side = -1; side <= 1; side += 2)
                {
                    float s = 10f + (float)rng.NextDouble() * 6f;
                    while (s < total - 10f && lots.Count < want * (shape != null ? 8 : 4))
                    {
                        int[] size = bag[next % bag.Count];
                        PlannedLot lot = Fit(h, cell, world, sea, p, s, side, size[0], size[1], t.Setback, maxRise, hash, lots);
                        // between close town roads a deep lot backs onto the next road: try the world's shallower sizes
                        // (EA's town blocks hold their small lots)
                        for (int k = 0; lot == null && k < shallow.Count && shallow[k][1] < size[1]; k++)
                        {
                            lot = Fit(h, cell, world, sea, p, s, side, shallow[k][0], shallow[k][1], t.Setback, maxRise, hash, lots);
                            if (lot != null) size = shallow[k];
                        }
                        if (lot != null) { lots.Add(lot); roadOf[lot] = order.IndexOf(r); next++; s += size[0] + Gap(t, rng); }
                        else s += 4f;
                    }
                }
            }
            }
            // 1b. beaches (EA's front a road on the water side, median 0-46 m from the water): the candidates nearest the
            // water are kept for them through the thinning
            float share = t.Count > 0 ? (float)t.Community / t.Count : 0.3f;
            float[] wd = t.Kinds != null && !roadsOnly ? LotKind.WaterDistance(h, cell, sea) : null;
            List<PlannedLot> keepBeach = new List<PlannedLot>();
            if (wd != null)
            {
                int beaches;
                if (LotKind.Quotas(t.Kinds, (int)Math.Round(want * share)).TryGetValue("beach", out beaches) && beaches > 0)
                {
                    List<KeyValuePair<PlannedLot, float>> wet = new List<KeyValuePair<PlannedLot, float>>();
                    foreach (PlannedLot l in lots) { float d = LotKind.At(wd, h, cell, l.Centre); if (d <= LotKind.BeachReach) wet.Add(new KeyValuePair<PlannedLot, float>(l, d + Dist(l.Centre, centre) / 20f)); }
                    wet.Sort(delegate (KeyValuePair<PlannedLot, float> a, KeyValuePair<PlannedLot, float> b) { return a.Value.CompareTo(b.Value); });
                    for (int k = 0; k < wet.Count && keepBeach.Count < beaches; k++) keepBeach.Add(wet[k].Key);
                    foreach (PlannedLot l in keepBeach) lots.Remove(l);
                }
            }
            // 2. more than the world has on its roads: thin, the far-out roads first (EA's lots gather in town but reach
            // out along the roads; 6-31 per km)
            int onRoad = onRoadWant - keepBeach.Count;
            List<PlannedLot> pool = new List<PlannedLot>(lots);
            if (lots.Count > onRoad && shape != null) lots = Spread(lots, roadOf, onRoad, shape, centre, rng, keepBeach);
            else if (lots.Count > onRoad)
            {
                float far = 1;
                foreach (PlannedLot l in lots) far = Math.Max(far, Dist(l.Centre, centre));
                float[] score = new float[lots.Count];
                for (int k = 0; k < lots.Count; k++) score[k] = Dist(lots[k].Centre, centre) / far + (float)rng.NextDouble() * 0.6f;
                int[] idx = new int[lots.Count];
                for (int k = 0; k < idx.Length; k++) idx[k] = k;
                Array.Sort(score, idx);
                List<PlannedLot> keep = new List<PlannedLot>();
                for (int k = 0; k < onRoad; k++) keep.Add(lots[idx[k]]);
                lots = keep;
            }
            lots.AddRange(keepBeach);
            // 3. lots off the roads (Isla Paradiso's islets, Shang Simla's and Al Simhara's hillside lots reached on foot):
            // the world's off-road share, on land at least 15 m from a road, near the town's other lots, facing downhill
            int offRoad = want - lots.Count;
            if (roadsOnly) offRoad = 0;
            else if (t.OnRoad < 0.8f) offRoad = Math.Max(offRoad, (int)Math.Round(want * (1 - t.OnRoad)));
            else offRoad = (int)Math.Round(want * (1 - t.OnRoad)) + Math.Max(0, onRoad - lots.Count) / 4;
            if (offRoad > 0) OffRoad(h, cell, world, sea, t, bag, ref next, offRoad, maxRise, hash, lots, centre, rng, shape);
            // space between neighbourhoods, after the lots off the roads (they are put near the others and joined them up)
            if (shape != null)
            {
                List<PlannedLot> fixedLots = new List<PlannedLot>(keepBeach);
                foreach (PlannedLot l in lots) if (!pool.Contains(l) && !fixedLots.Contains(l)) pool.Add(l);
                foreach (PlannedLot l in keepBeach) lots.Remove(l);
                Separate(lots, pool, fixedLots, largest, centre);
                lots.AddRange(keepBeach);
            }
            // community lots: by kind where the world's are known (beaches by the water, civic buildings in town...),
            // else the largest, as EA's parks, beaches and shops are
            int community = (int)Math.Round(lots.Count * share);
            if (wd != null)
            {
                // beaches still short (the roads keep 24 m back from the water, so the lots along them may not reach it):
                // lots of their own on the shore near a road make up the shortfall
                int beaches; Dictionary<string, int> q = LotKind.Quotas(t.Kinds, community);
                if (ShoreBeaches && q.TryGetValue("beach", out beaches) && beaches > 0)
                {
                    int near = 0; foreach (PlannedLot l in lots) if (LotKind.At(wd, h, cell, l.Centre) <= LotKind.BeachReach) near++;
                    if (near < beaches)
                    {
                        float area = 1200; foreach (LotKind k in t.Kinds) if (k.Name == "beach") area = k.Area;
                        int before = lots.Count;
                        ShoreLots(h, cell, world, sea, wd, hash, lots, centre, beaches - near, area, rng);
                        // the count stays the world's: as many lots away from the water make room, at random
                        for (int added = lots.Count - before; added > 0; added--)
                        {
                            List<int> pick = new List<int>();
                            for (int i = 0; i < before; i++) if (LotKind.At(wd, h, cell, lots[i].Centre) > LotKind.BeachReach) pick.Add(i);
                            if (pick.Count == 0) break;
                            lots.RemoveAt(pick[rng.Next(pick.Count)]); before--;
                        }
                        community = (int)Math.Round(lots.Count * share);
                    }
                }
            }
            LotKind.Assign(lots, community, t.Kinds, h, cell, sea, centre, wd);
            return lots;
        }

        /// <summary>
        /// 'count' of the candidate lots, spread from the town centre as the Like world's are (TownShape): a tenth in each
        /// tenth of its lot spread; within a tenth whole roads are lined first (roads in random order, so a street is built
        /// up rather than one lot here and there, leaving the others as country road); a tenth short of candidates is made
        /// up from the nearest candidates left (inner tenths first), so the town stays compact.
        /// </summary>
        private static List<PlannedLot> Spread(List<PlannedLot> lots, Dictionary<PlannedLot, int> roadOf, int count, TownShape shape, float[] centre, Random rng,
                                               List<PlannedLot> kept = null)
        {
            List<PlannedLot>[] band = new List<PlannedLot>[11];
            for (int b = 0; b < 11; b++) band[b] = new List<PlannedLot>();
            foreach (PlannedLot l in lots) band[TownShape.Band(shape.LotDec, Dist(l.Centre, centre))].Add(l);
            // lots already kept (beaches) count towards their tenth and the total
            int[] have = new int[11];
            int total = count;
            if (kept != null) { foreach (PlannedLot l in kept) have[TownShape.Band(shape.LotDec, Dist(l.Centre, centre))]++; total += kept.Count; }
            Dictionary<int, double> roadKey = new Dictionary<int, double>();
            List<PlannedLot> keep = new List<PlannedLot>(), left = new List<PlannedLot>();
            for (int b = 0; b < 11; b++)
            {
                List<PlannedLot> l = band[b];
                double[] key = new double[l.Count];
                for (int k = 0; k < l.Count; k++)
                {
                    int rd; if (!roadOf.TryGetValue(l[k], out rd)) rd = -1 - k;
                    double rk; if (!roadKey.TryGetValue(rd, out rk)) { rk = rng.NextDouble(); roadKey[rd] = rk; }
                    key[k] = rk + rng.NextDouble() * 1e-6;
                }
                PlannedLot[] arr = l.ToArray(); Array.Sort(key, arr); l.Clear(); l.AddRange(arr);
                int quota = b < 10 ? Math.Max(0, (int)Math.Round(total * (b + 1) / 10f) - (int)Math.Round(total * b / 10f) - have[b]) : 0;
                for (int k = 0; k < l.Count; k++) (k < quota ? keep : left).Add(l[k]);
            }
            left.Sort(delegate (PlannedLot a, PlannedLot b) { return Dist(a.Centre, centre).CompareTo(Dist(b.Centre, centre)); });
            for (int k = 0; k < left.Count && keep.Count < count; k++) keep.Add(left[k]);
            return keep;
        }

        /// <summary>
        /// Space between neighbourhoods (the tester: "like the EA ones"). EA's lots, grouped as lots within 40 m of each other,
        /// form one main neighbourhood of 40-73 % of the lots (Sunset Valley 70 %, Appaloosa Plains 57 %, Twinbrook 43 %;
        /// tools/roadlearn/hoodgaps.py) and smaller ones 47-120 m away; ours ran on as one (80-83 %). The main one is the
        /// world's share of the lots nearest the centre; the lots of the same run within 40 m of it are taken out (up to
        /// 30 % of the lots), row by row, until the rest stands apart; as many are then put back from the unused candidates
        /// at least 40 m from it, next to the outer neighbourhoods first, so the count stays. 'fixedLots' (beaches) stay.
        /// Only for worlds whose main neighbourhood holds at least half the lots: where EA's are already many small ones
        /// (Champs Les Sims, Isla Paradiso, Barnacle Bay: 16-43 %) splitting ours cost 1-3 points of road score.
        /// </summary>
        private static void Separate(List<PlannedLot> keep, List<PlannedLot> pool, List<PlannedLot> fixedLots, float largest, float[] centre)
        {
            if (largest < 0.5f || largest >= 0.9f || keep.Count < 12 || Environment.GetEnvironmentVariable("CAWNOSEP") != null) return;
            const float link = 40f;
            int n0 = keep.Count + fixedLots.Count;
            List<PlannedLot> all = new List<PlannedLot>(keep); all.AddRange(fixedLots);
            List<int> big = Biggest(all, link);
            if (big.Count <= (largest + 0.05f) * n0) return;
            big.Sort(delegate (int a, int b) { return Dist(all[a].Centre, centre).CompareTo(Dist(all[b].Centre, centre)); });
            int coreN = Math.Max(3, (int)Math.Round(largest * n0));
            List<PlannedLot> core = new List<PlannedLot>();
            for (int k = 0; k < coreN && k < big.Count; k++) core.Add(all[big[k]]);
            int removed = 0;
            for (int round = 0; round < 8; round++)
            {
                all = new List<PlannedLot>(keep); all.AddRange(fixedLots);
                big = Biggest(all, link);
                if (big.Count <= core.Count + 2) break;
                List<PlannedLot> cut = new List<PlannedLot>();
                foreach (int i in big)
                {
                    PlannedLot l = all[i];
                    if (core.Contains(l) || fixedLots.Contains(l)) continue;
                    if (GapTo(l, core) < link) cut.Add(l);
                }
                if (cut.Count == 0 || removed + cut.Count > 0.3f * n0) break;
                foreach (PlannedLot l in cut) keep.Remove(l);
                removed += cut.Count;
            }
            // put as many back, away from the main neighbourhood, next to the outer ones first
            List<KeyValuePair<PlannedLot, float>> spare = new List<KeyValuePair<PlannedLot, float>>();
            foreach (PlannedLot l in pool)
            {
                if (keep.Contains(l) || fixedLots.Contains(l) || GapTo(l, core) < link) continue;
                float near = float.MaxValue;
                foreach (PlannedLot o in keep) if (!core.Contains(o)) near = Math.Min(near, Dist(l.Centre, o.Centre));
                spare.Add(new KeyValuePair<PlannedLot, float>(l, near + Dist(l.Centre, centre) * 0.05f));
            }
            spare.Sort(delegate (KeyValuePair<PlannedLot, float> a, KeyValuePair<PlannedLot, float> b) { return a.Value.CompareTo(b.Value); });
            for (int k = 0; k < spare.Count && removed > 0; k++)
            {
                // a lot put back may join one already put back to the core: check against the core again
                if (GapTo(spare[k].Key, core) < link) continue;
                keep.Add(spare[k].Key); removed--;
            }
        }

        /// <summary>The largest group of lots within 'link' m of each other (outline to outline, by half-diagonals).</summary>
        private static List<int> Biggest(List<PlannedLot> lots, float link)
        {
            int n = lots.Count;
            int[] par = new int[n];
            for (int k = 0; k < n; k++) par[k] = k;
            for (int a = 0; a < n; a++)
                for (int b = a + 1; b < n; b++)
                    if (Gap(lots[a], lots[b]) < link) { int ra = Root(par, a), rb = Root(par, b); if (ra != rb) par[ra] = rb; }
            Dictionary<int, List<int>> g = new Dictionary<int, List<int>>();
            List<int> best = new List<int>();
            for (int k = 0; k < n; k++)
            {
                int r = Root(par, k); List<int> l;
                if (!g.TryGetValue(r, out l)) g[r] = l = new List<int>();
                l.Add(k);
                if (l.Count > best.Count) best = l;
            }
            return best;
        }

        private static float Gap(PlannedLot a, PlannedLot b)
        {
            return Dist(a.Centre, b.Centre) - (float)Math.Sqrt(Sq(a.Width) + Sq(a.Depth)) / 2f - (float)Math.Sqrt(Sq(b.Width) + Sq(b.Depth)) / 2f;
        }

        private static float GapTo(PlannedLot l, List<PlannedLot> group)
        {
            float d = float.MaxValue;
            foreach (PlannedLot o in group) if (o != l) d = Math.Min(d, Gap(l, o));
            return d;
        }

        /// <summary>A lot fronting the road at distance s along it on one side, or null if it doesn't fit there.</summary>
        private static PlannedLot Fit(Grid h, float cell, float world, float sea, List<float[]> p, float s, int side, float f, float dep,
                                      float setback, float maxRise, Dictionary<long, List<float[]>> hash, List<PlannedLot> lots)
        {
            int i = Math.Min(p.Count - 2, (int)(s / 2f));
            float tx = p[i + 1][0] - p[i][0], tz = p[i + 1][1] - p[i][1], tl = (float)Math.Sqrt(tx * tx + tz * tz);
            if (tl < 1e-3f) return null;
            tx /= tl; tz /= tl;
            // the frontage must run along a straight stretch (the tester: roads with lots on them straight, as EA's town
            // blocks are): the road within 8 deg of one direction, and no more than 1 m off a straight line, along the lot
            int j = Math.Min(p.Count - 2, i + Math.Max(1, (int)(f / 2f)));
            float sx = p[j + 1][0] - p[i][0], sz = p[j + 1][1] - p[i][1], sl = (float)Math.Sqrt(sx * sx + sz * sz);
            if (sl < 1e-3f) return null;
            for (int k = i; k <= j; k++)
            {
                float ex = p[k + 1][0] - p[k][0], ez = p[k + 1][1] - p[k][1], el = (float)Math.Sqrt(ex * ex + ez * ez);
                if (el > 1e-3f && (sx * ex + sz * ez) / (sl * el) < 0.99f) return null;
                float off = Math.Abs((p[k][0] - p[i][0]) * sz - (p[k][1] - p[i][1]) * sx) / sl;
                if (off > 1f) return null;
            }
            // U along the road, V = (-U.z, U.x) away from it: on the right side U runs with the road, on the left against it
            float ux = side > 0 ? tx : -tx, uz = side > 0 ? tz : -tz;
            float vx = -uz, vz = ux;
            float mx = p[i][0] + tx * f / 2f + vx * setback, mz = p[i][1] + tz * f / 2f + vz * setback; // middle of the front edge
            float x0 = mx - ux * f / 2f, z0 = mz - uz * f / 2f;
            // no road across or beside it (its own frontage is 6 m out, beyond the 4.5 m kept clear)
            return FitAt(h, cell, world, sea, x0, z0, ux, uz, f, dep, maxRise, hash, lots, RoadClear);
        }

        /// <summary>Lots away from the roads: candidate spots on a 12 m lattice, best first (flat, near the other lots).</summary>
        private static void OffRoad(Grid h, float cell, float world, float sea, LotTargets t, List<int[]> bag, ref int next, int count, float maxRise,
                                    Dictionary<long, List<float[]>> hash, List<PlannedLot> lots, float[] centre, Random rng, TownShape shape = null)
        {
            List<float[]> spots = new List<float[]>(); // x, z, score
            for (float z = Rim; z < world - Rim; z += 12)
                for (float x = Rim; x < world - Rim; x += 12)
                {
                    float y = TerrainGen.Sample(h, x / cell, z / cell);
                    if (y < sea + 1.5f) continue;
                    float gx = TerrainGen.Sample(h, (x + 8) / cell, z / cell) - TerrainGen.Sample(h, (x - 8) / cell, z / cell);
                    float gz = TerrainGen.Sample(h, x / cell, (z + 8) / cell) - TerrainGen.Sample(h, x / cell, (z - 8) / cell);
                    float slope = (float)Math.Sqrt(gx * gx + gz * gz) / 16f;
                    if (slope > 0.45f) continue;
                    float near = float.MaxValue;
                    foreach (PlannedLot l in lots) near = Math.Min(near, Dist(l.Centre, new[] { x, z }));
                    if (lots.Count == 0) near = Dist(centre, new[] { x, z });
                    spots.Add(new[] { x, z, slope * 2 + near / 400f + (float)rng.NextDouble() * 0.6f });
                }
            spots.Sort(delegate (float[] a, float[] b) { return a[2].CompareTo(b[2]); });
            int placed = 0;
            // with the town shape: first only where a tenth of its lot spread is still short (all lots, road ones too),
            // then anywhere
            int[] have = new int[11], quota = new int[11];
            int total = lots.Count + count;
            if (shape != null)
            {
                foreach (PlannedLot l in lots) have[TownShape.Band(shape.LotDec, Dist(l.Centre, centre))]++;
                for (int b = 0; b < 10; b++) quota[b] = (int)Math.Round(total * (b + 1) / 10f) - (int)Math.Round(total * b / 10f);
            }
            for (int pass = shape != null ? 0 : 1; pass < 2; pass++)
            foreach (float[] sp in spots)
            {
                if (placed >= count) break;
                int band = shape != null ? TownShape.Band(shape.LotDec, Dist(sp, centre)) : 0;
                if (pass == 0 && have[band] >= quota[band]) continue;
                int[] size = bag[next % bag.Count];
                // face downhill (towards the water on an island): V, away from the front, points uphill
                float gx = TerrainGen.Sample(h, (sp[0] + 8) / cell, sp[1] / cell) - TerrainGen.Sample(h, (sp[0] - 8) / cell, sp[1] / cell);
                float gz = TerrainGen.Sample(h, sp[0] / cell, (sp[1] + 8) / cell) - TerrainGen.Sample(h, sp[0] / cell, (sp[1] - 8) / cell);
                float gl = (float)Math.Sqrt(gx * gx + gz * gz);
                float vx = gl > 1e-3f ? gx / gl : 0, vz = gl > 1e-3f ? gz / gl : 1;
                float ux = vz, uz = -vx; // V = (-U.z, U.x)
                float x0 = sp[0] - ux * size[0] / 2f - vx * size[1] / 2f, z0 = sp[1] - uz * size[0] / 2f - vz * size[1] / 2f;
                PlannedLot lot = FitAt(h, cell, world, sea, x0, z0, ux, uz, size[0], size[1], maxRise, hash, lots, 15f);
                if (lot == null) continue;
                lots.Add(lot); next++; placed++; have[band]++;
            }
        }

        /// <summary>
        /// Up to 'count' lots on the shore for beaches: middles 10-50 m from the water, within 120 m of a road, ground
        /// rising at most 5 m across, the lot reaching towards the water (front edge inland), as near the town as the shore allows; square,
        /// sized like the world's beach lots (20-40 m).
        /// </summary>
        private static void ShoreLots(Grid h, float cell, float world, float sea, float[] wd, Dictionary<long, List<float[]>> hash,
                                      List<PlannedLot> lots, float[] centre, int count, float area, Random rng)
        {
            int side = (int)(Math.Round(Math.Max(20f, Math.Min(40f, (float)Math.Sqrt(area))) / 5f) * 5);
            List<float[]> spots = new List<float[]>(); // x, z, score
            for (float z = Rim; z < world - Rim; z += 8)
                for (float x = Rim; x < world - Rim; x += 8)
                {
                    float d = LotKind.At(wd, h, cell, new[] { x, z });
                    if (d < 10 || d > 50) continue;
                    float road = float.MaxValue;
                    for (long gx = (long)Math.Floor((x - 120) / 16); gx <= (long)Math.Floor((x + 120) / 16); gx++)
                        for (long gz = (long)Math.Floor((z - 120) / 16); gz <= (long)Math.Floor((z + 120) / 16); gz++)
                        {
                            List<float[]> l; if (!hash.TryGetValue(gx * 1000003L + gz, out l)) continue;
                            foreach (float[] q in l) road = Math.Min(road, Dist(q, new[] { x, z }));
                        }
                    if (road > 120) continue;
                    spots.Add(new[] { x, z, Dist(centre, new[] { x, z }) / 400f + (float)rng.NextDouble() * 0.5f });
                }
            spots.Sort(delegate (float[] a, float[] b) { return a[2].CompareTo(b[2]); });
            int placed = 0;
            foreach (float[] sp in spots)
            {
                if (placed >= count) break;
                // towards the water: down the distance field
                float gx = LotKind.At(wd, h, cell, new[] { sp[0] + 8, sp[1] }) - LotKind.At(wd, h, cell, new[] { sp[0] - 8, sp[1] });
                float gz = LotKind.At(wd, h, cell, new[] { sp[0], sp[1] + 8 }) - LotKind.At(wd, h, cell, new[] { sp[0], sp[1] - 8 });
                float gl = (float)Math.Sqrt(gx * gx + gz * gz);
                if (gl < 1e-3f) continue;
                float vx = -gx / gl, vz = -gz / gl; // V (away from the front) points to the water
                float ux = vz, uz = -vx;
                float x0 = sp[0] - ux * side / 2f - vx * side / 2f, z0 = sp[1] - uz * side / 2f - vz * side / 2f;
                PlannedLot lot = FitAt(h, cell, world, sea, x0, z0, ux, uz, side, side, 5f, hash, lots, RoadClear);
                if (lot == null) continue;
                lots.Add(lot); placed++;
            }
        }

        /// <summary>A lot at a given corner and direction if it fits: on the map, on land, ground rising no more than
        /// maxRise, no road within 'roadClear' of it, no other lot within 1 m.</summary>
        private static PlannedLot FitAt(Grid h, float cell, float world, float sea, float x0, float z0, float ux, float uz, float f, float dep,
                                        float maxRise, Dictionary<long, List<float[]>> hash, List<PlannedLot> lots, float roadClear)
        {
            float vx = -uz, vz = ux;
            float lo = float.MaxValue, hi = float.MinValue;
            int na = Math.Max(2, (int)(f / 5f)), nb = Math.Max(2, (int)(dep / 5f));
            for (int a = 0; a <= na; a++)
                for (int b = 0; b <= nb; b++)
                {
                    float x = x0 + ux * f * a / na + vx * dep * b / nb, z = z0 + uz * f * a / na + vz * dep * b / nb;
                    if (x < Rim || z < Rim || x > world - Rim || z > world - Rim) return null;
                    float y = TerrainGen.Sample(h, x / cell, z / cell);
                    if (y < sea + 1f) return null;
                    lo = Math.Min(lo, y); hi = Math.Max(hi, y);
                }
            if (hi - lo > maxRise) return null;
            float cxm = x0 + ux * f / 2 + vx * dep / 2, czm = z0 + uz * f / 2 + vz * dep / 2;
            float reach = (float)Math.Sqrt(f * f + dep * dep) / 2 + roadClear + 16;
            for (long gx = (long)Math.Floor((cxm - reach) / 16); gx <= (long)Math.Floor((cxm + reach) / 16); gx++)
                for (long gz = (long)Math.Floor((czm - reach) / 16); gz <= (long)Math.Floor((czm + reach) / 16); gz++)
                {
                    List<float[]> l; if (!hash.TryGetValue(gx * 1000003L + gz, out l)) continue;
                    foreach (float[] q in l)
                    {
                        float a = (q[0] - x0) * ux + (q[1] - z0) * uz, b = (q[0] - x0) * vx + (q[1] - z0) * vz;
                        if (a > -roadClear && a < f + roadClear && b > -roadClear && b < dep + roadClear) return null;
                    }
                }
            foreach (PlannedLot o in lots)
                if (Overlap(x0, z0, ux, uz, f, dep, o)) return null;
            PlannedLot lot = new PlannedLot();
            lot.X = x0; lot.Z = z0; lot.Ux = ux; lot.Uz = uz; lot.Width = (int)f; lot.Depth = (int)dep;
            return lot;
        }

        private static float Dist(float[] a, float[] b) { return (float)Math.Sqrt(Sq(a[0] - b[0]) + Sq(a[1] - b[1])); }

        /// <summary>Rectangles overlap (with 1 m to spare), by separating axes.</summary>
        private static bool Overlap(float x0, float z0, float ux, float uz, float w, float d, PlannedLot o)
        {
            float[][] a = Corners(x0, z0, ux, uz, w, d), b = Corners(o.X, o.Z, o.Ux, o.Uz, o.Width, o.Depth);
            float[][] axes = { new[] { ux, uz }, new[] { -uz, ux }, new[] { o.Ux, o.Uz }, new[] { -o.Uz, o.Ux } };
            foreach (float[] ax in axes)
            {
                float amin = float.MaxValue, amax = float.MinValue, bmin = float.MaxValue, bmax = float.MinValue;
                foreach (float[] c in a) { float v = c[0] * ax[0] + c[1] * ax[1]; amin = Math.Min(amin, v); amax = Math.Max(amax, v); }
                foreach (float[] c in b) { float v = c[0] * ax[0] + c[1] * ax[1]; bmin = Math.Min(bmin, v); bmax = Math.Max(bmax, v); }
                if (amax + 1 <= bmin || bmax + 1 <= amin) return false;
            }
            return true;
        }

        public static float[][] Corners(float x0, float z0, float ux, float uz, float w, float d)
        {
            float vx = -uz, vz = ux;
            return new[] { new[] { x0, z0 }, new[] { x0 + ux * w, z0 + uz * w }, new[] { x0 + ux * w + vx * d, z0 + uz * w + vz * d }, new[] { x0 + vx * d, z0 + vz * d } };
        }

        public static int KeptOffRoad; // lots kept away from any road in the last SnapToRoads

        /// <summary>
        /// Fits lots to the roads CAW actually built (the tester, 2026-10-24: lots weren't lined up with their road). Each
        /// lot (four corners, world x/z; corner 0 -> 1 is the front edge, 0 -> 3 points away from the road) is turned to
        /// the built road nearest its front, its front edge put parallel to it at 'setback' metres from the centre line,
        /// and kept only where the built road runs straight (within 1 m of a line) along the whole frontage and 1 m
        /// beyond each side. If not straight where planned, it slides along the road up to 24 m to find a straight
        /// stretch; else it is dropped. Lots that would overlap one already placed are dropped too.
        /// roads: each built road piece as its Bezier knots (x0, z0 .. x3, z3). Returns the fitted lots (null = dropped).
        /// </summary>
        public static float[][][] SnapToRoads(List<float[][]> lots, List<float[]> roads, float setback, out int moved, out int dropped)
        {
            moved = dropped = 0; KeptOffRoad = 0;
            // the built roads as short straight segments
            List<float[]> seg = new List<float[]>();
            foreach (float[] k in roads)
            {
                if (k.Length < 8) continue;
                float px = k[0], pz = k[1];
                for (int i = 1; i <= 16; i++)
                {
                    float t = i / 16f, u = 1 - t;
                    float x = u * u * u * k[0] + 3 * u * u * t * k[2] + 3 * u * t * t * k[4] + t * t * t * k[6];
                    float z = u * u * u * k[1] + 3 * u * u * t * k[3] + 3 * u * t * t * k[5] + t * t * t * k[7];
                    if ((x - px) * (x - px) + (z - pz) * (z - pz) > 1e-4f) seg.Add(new[] { px, pz, x, z });
                    px = x; pz = z;
                }
            }
            float[][][] outp = new float[lots.Count][][];
            List<float[][]> placed = new List<float[][]>();
            for (int n = 0; n < lots.Count; n++)
            {
                float[][] c = lots[n];
                float wx = c[1][0] - c[0][0], wz = c[1][1] - c[0][1];
                float w = (float)Math.Sqrt(wx * wx + wz * wz);
                float dx = c[3][0] - c[0][0], dz = c[3][1] - c[0][1];
                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                if (w < 1 || d < 1 || seg.Count == 0) { dropped++; continue; }
                float vx = dx / d, vz = dz / d;                       // away from the road
                float mx = (c[0][0] + c[1][0]) / 2, mz = (c[0][1] + c[1][1]) / 2;
                float[] best = null; float bestShift = 0; bool nearRoad = false;
                foreach (float shift in new float[] { 0, 2, -2, 4, -4, 6, -6, 8, -8, 10, -10, 12, -12, 15, -15, 18, -18, 21, -21, 24, -24 })
                {
                    float ux = wx / w, uz = wz / w;
                    float qx = mx + ux * shift - vx * setback, qz = mz + uz * shift - vz * setback; // where the road should be
                    float[] p = Nearest(seg, qx, qz);
                    if (p == null || p[4] > setback + 6) continue;        // no built road near this front
                    nearRoad = true;
                    // road direction there, oriented like the lot's front edge
                    float tx = p[5], tz = p[6];
                    if (tx * ux + tz * uz < 0) { tx = -tx; tz = -tz; }
                    // the lot must lie on the side it was planned on
                    float nx = -tz, nz = tx;
                    if (nx * vx + nz * vz < 0) { nx = -nx; nz = -nz; }
                    if (nx * vx + nz * vz < 0.7f) continue;               // the road there turns away from the lot's side
                    // straight along the frontage + 1 m each side: the built road's points there fit one line within 1 m
                    // (the planner's own test for roads that take lots), and that line gives the lot its direction
                    List<float[]> on = new List<float[]>();
                    bool straight = true;
                    for (float s = -w / 2 - 1; s <= w / 2 + 1.01f && straight; s += 2)
                    {
                        float[] q = Nearest(seg, p[0] + tx * s, p[1] + tz * s);
                        if (q == null || q[4] > 2.5f) straight = false; else on.Add(q);
                    }
                    if (straight)
                    {
                        // line through the points: direction from the first to the last, then every point within 1 m of it
                        float lx = on[on.Count - 1][0] - on[0][0], lz = on[on.Count - 1][1] - on[0][1], ll = (float)Math.Sqrt(lx * lx + lz * lz);
                        if (ll < w * 0.8f) straight = false;
                        else
                        {
                            lx /= ll; lz /= ll;
                            foreach (float[] q in on)
                                if (Math.Abs((q[0] - on[0][0]) * -lz + (q[1] - on[0][1]) * lx) > 1f) { straight = false; break; }
                            if (straight)
                            {
                                if (lx * tx + lz * tz < 0) { lx = -lx; lz = -lz; }
                                tx = lx; tz = lz; nx = -tz; nz = tx;
                                if (nx * vx + nz * vz < 0) { nx = -nx; nz = -nz; }
                                // the line's point nearest the planned front middle
                                float t0 = (p[0] - on[0][0]) * tx + (p[1] - on[0][1]) * tz;
                                float ox = 0, oz = 0; foreach (float[] q in on) { ox += q[0] - on[0][0] - tx * ((q[0] - on[0][0]) * tx + (q[1] - on[0][1]) * tz); oz += q[1] - on[0][1] - tz * ((q[0] - on[0][0]) * tx + (q[1] - on[0][1]) * tz); }
                                p = new[] { on[0][0] + tx * t0 + ox / on.Count, on[0][1] + tz * t0 + oz / on.Count };
                            }
                        }
                    }
                    if (!straight) continue;
                    float fx = p[0] + nx * setback, fz = p[1] + nz * setback; // middle of the new front edge
                    float[][] r = new float[4][];
                    r[0] = new[] { fx - tx * w / 2, fz - tz * w / 2 };
                    r[1] = new[] { fx + tx * w / 2, fz + tz * w / 2 };
                    r[2] = new[] { r[1][0] + nx * d, r[1][1] + nz * d };
                    r[3] = new[] { r[0][0] + nx * d, r[0][1] + nz * d };
                    bool clash = false;
                    foreach (float[][] o in placed) if (Overlap(o, r)) { clash = true; break; }
                    if (clash) continue;
                    // no road may come nearer the lot than BuiltRoadClear: where the road curves away beyond the frontage (or
                    // another road passes a side or the back) a corner reached into the pavement (the tester, 2026-10-25, #133)
                    if (RoadWithin(seg, r, BuiltRoadClear)) continue;
                    best = new float[0]; outp[n] = r; bestShift = shift;
                    break;
                }
                if (best == null && !nearRoad)
                {
                    // no built road anywhere near its front: a lot reached on foot (some worlds have many), kept as planned,
                    // but only if no road comes within 10 m of it (a lot beside a road must line up with it)
                    bool clash = false;
                    foreach (float[][] o in placed) if (Overlap(o, c)) { clash = true; break; }
                    foreach (float[] q in seg) if (!clash && Inside(c, q[0], q[1], 10f)) clash = true;
                    if (!clash) { outp[n] = c; placed.Add(c); KeptOffRoad++; continue; }
                }
                if (best == null) { dropped++; continue; }
                placed.Add(outp[n]);
                float ex = (outp[n][0][0] + outp[n][1][0]) / 2 - mx, ez = (outp[n][0][1] + outp[n][1][1]) / 2 - mz;
                if (ex * ex + ez * ez > 0.25f || bestShift != 0) moved++;
            }
            return outp;
        }

        /// <summary>Point (x, z) inside the quadrilateral grown by 'pad' metres.</summary>
        private static bool Inside(float[][] c, float x, float z, float pad)
        {
            for (int i = 0; i < 4; i++)
            {
                float[] p = c[i], q = c[(i + 1) % 4], r = c[(i + 2) % 4];
                float ex = q[0] - p[0], ez = q[1] - p[1], l = (float)Math.Sqrt(ex * ex + ez * ez); if (l < 1e-6f) continue;
                float nx = -ez / l, nz = ex / l;
                if ((r[0] - p[0]) * nx + (r[1] - p[1]) * nz < 0) { nx = -nx; nz = -nz; } // inward
                if ((x - p[0]) * nx + (z - p[1]) * nz < -pad) return false;
            }
            return true;
        }

        /// <summary>Nearest point on the segments: x, z, segment index, along-share, distance, unit direction x, z.</summary>
        private static float[] Nearest(List<float[]> seg, float x, float z)
        {
            float[] best = null; float bd = float.MaxValue;
            for (int i = 0; i < seg.Count; i++)
            {
                float[] q = seg[i];
                float ax = q[2] - q[0], az = q[3] - q[1], l2 = ax * ax + az * az;
                float t = Math.Max(0, Math.Min(1, ((x - q[0]) * ax + (z - q[1]) * az) / l2));
                float px = q[0] + ax * t, pz = q[1] + az * t, dd = (px - x) * (px - x) + (pz - z) * (pz - z);
                if (dd < bd) { bd = dd; float l = (float)Math.Sqrt(l2); best = new[] { px, pz, i, t, 0, ax / l, az / l }; }
            }
            if (best != null) best[4] = (float)Math.Sqrt(bd);
            return best;
        }

        /// <summary>Two convex quadrilaterals overlap by more than 0.5 m (separating axes).</summary>
        private static bool Overlap(float[][] a, float[][] b)
        {
            foreach (float[][] poly in new[] { a, b })
                for (int i = 0; i < 4; i++)
                {
                    float[] p = poly[i], q = poly[(i + 1) % 4];
                    float ax = -(q[1] - p[1]), az = q[0] - p[0];
                    float l = (float)Math.Sqrt(ax * ax + az * az); if (l < 1e-6f) continue;
                    ax /= l; az /= l;
                    float amin = float.MaxValue, amax = float.MinValue, bmin = float.MaxValue, bmax = float.MinValue;
                    foreach (float[] v in a) { float s = v[0] * ax + v[1] * az; amin = Math.Min(amin, s); amax = Math.Max(amax, s); }
                    foreach (float[] v in b) { float s = v[0] * ax + v[1] * az; bmin = Math.Min(bmin, s); bmax = Math.Max(bmax, s); }
                    if (amax <= bmin + 0.5f || bmax <= amin + 0.5f) return false;
                }
            return true;
        }

        /// <summary>A lot from four corners on the terrain map (front corners 0, 1; 3 behind 0), as a PlannedLot.</summary>
        public static PlannedLot FromCorners(float[][] c, bool community)
        {
            float dx = c[3][0] - c[0][0], dz = c[3][1] - c[0][1], d = (float)Math.Sqrt(dx * dx + dz * dz);
            float wx = c[1][0] - c[0][0], wz = c[1][1] - c[0][1], w = (float)Math.Sqrt(wx * wx + wz * wz);
            PlannedLot l = new PlannedLot();
            l.Width = (int)Math.Round(w); l.Depth = (int)Math.Round(d); l.Community = community;
            // V = (-Uz, Ux) must point away from the road (corner 0 -> 3), so U = (Vz, -Vx)
            l.Ux = dz / d; l.Uz = -dx / d;
            bool fromFirst = l.Ux * wx + l.Uz * wz > 0;
            l.X = fromFirst ? c[0][0] : c[1][0]; l.Z = fromFirst ? c[0][1] : c[1][1];
            return l;
        }

        /// <summary>A gap to the next lot drawn from the world's 10th/50th/90th percentiles.</summary>
        private static float Gap(LotTargets t, Random rng)
        {
            double u = rng.NextDouble();
            float g = u < 0.1 ? t.Gap10 * (float)(u / 0.1)
                    : u < 0.5 ? t.Gap10 + (t.Gap50 - t.Gap10) * (float)((u - 0.1) / 0.4)
                    : u < 0.9 ? t.Gap50 + (t.Gap90 - t.Gap50) * (float)((u - 0.5) / 0.4)
                    : t.Gap90 + 20 * (float)((u - 0.9) / 0.1);
            return Math.Max(1f, g);
        }

        private static float MidDist(Road r, float[] c)
        {
            float[] m = r.Points[r.Points.Count / 2];
            return Sq(m[0] - c[0]) + Sq(m[1] - c[1]);
        }

        private static List<float[]> Resample(List<float[]> p, float step)
        {
            List<float[]> o = new List<float[]>();
            if (p.Count == 0) return o;
            o.Add(p[0]);
            float carry = 0;
            for (int k = 1; k < p.Count; k++)
            {
                float[] a = p[k - 1], b = p[k];
                float len = (float)Math.Sqrt(Sq(b[0] - a[0]) + Sq(b[1] - a[1]));
                float d = step - carry;
                while (d <= len) { o.Add(new[] { a[0] + (b[0] - a[0]) * d / len, a[1] + (b[1] - a[1]) * d / len }); d += step; }
                carry = len - (d - step);
            }
            return o;
        }

        /// <summary>Metres a fitted lot keeps from any built road's centre line (CAW's road and pavement reach ~5 m; the front
        /// edge itself is at the 6 m setback).</summary>
        private const float BuiltRoadClear = 5f;

        /// <summary>Whether a road segment (x1, z1, x2, z2, ...) comes within 'clear' m of the rectangle (corners in order).</summary>
        private static bool RoadWithin(List<float[]> seg, float[][] r, float clear)
        {
            float minx = float.MaxValue, maxx = float.MinValue, minz = float.MaxValue, maxz = float.MinValue;
            foreach (float[] q in r) { minx = Math.Min(minx, q[0]); maxx = Math.Max(maxx, q[0]); minz = Math.Min(minz, q[1]); maxz = Math.Max(maxz, q[1]); }
            minx -= clear; maxx += clear; minz -= clear; maxz += clear;
            foreach (float[] sg in seg)
            {
                if (Math.Max(sg[0], sg[2]) < minx || Math.Min(sg[0], sg[2]) > maxx || Math.Max(sg[1], sg[3]) < minz || Math.Min(sg[1], sg[3]) > maxz) continue;
                // a segment end inside the lot, or the segment within 'clear' of any of the lot's edges
                if (Inside(r, sg[0], sg[1]) || Inside(r, sg[2], sg[3])) return true;
                for (int e = 0; e < 4; e++)
                {
                    float[] a = r[e], b = r[(e + 1) % 4];
                    if (SegSeg(a[0], a[1], b[0], b[1], sg[0], sg[1], sg[2], sg[3]) < clear) return true;
                }
            }
            return false;
        }

        private static bool Inside(float[][] r, float x, float z)
        {
            bool sign = false, first = true;
            for (int e = 0; e < 4; e++)
            {
                float[] a = r[e], b = r[(e + 1) % 4];
                bool s = (b[0] - a[0]) * (z - a[1]) - (b[1] - a[1]) * (x - a[0]) > 0;
                if (first) { sign = s; first = false; } else if (s != sign) return false;
            }
            return true;
        }

        /// <summary>Shortest distance between two segments (they don't cross: a crossing has an end inside the lot or
        /// passes within 0 of an edge, caught by the point distances only when near; crossings return 0).</summary>
        private static float SegSeg(float ax, float az, float bx, float bz, float cx, float cz, float dx, float dz)
        {
            float d1 = (bx - ax) * (cz - az) - (bz - az) * (cx - ax), d2 = (bx - ax) * (dz - az) - (bz - az) * (dx - ax);
            float d3 = (dx - cx) * (az - cz) - (dz - cz) * (ax - cx), d4 = (dx - cx) * (bz - cz) - (dz - cz) * (bx - cx);
            if (((d1 > 0) != (d2 > 0)) && ((d3 > 0) != (d4 > 0))) return 0;
            return Math.Min(Math.Min(PtSeg(ax, az, cx, cz, dx, dz), PtSeg(bx, bz, cx, cz, dx, dz)), Math.Min(PtSeg(cx, cz, ax, az, bx, bz), PtSeg(dx, dz, ax, az, bx, bz)));
        }

        private static float PtSeg(float px, float pz, float ax, float az, float bx, float bz)
        {
            float vx = bx - ax, vz = bz - az, l2 = vx * vx + vz * vz;
            float t = l2 > 0 ? Math.Max(0, Math.Min(1, ((px - ax) * vx + (pz - az) * vz) / l2)) : 0;
            float qx = ax + vx * t - px, qz = az + vz * t - pz;
            return (float)Math.Sqrt(qx * qx + qz * qz);
        }

        private static long Key(float x, float z) { return (long)Math.Floor(x / 16) * 1000003L + (long)Math.Floor(z / 16); }
        private static float Sq(float v) { return v * v; }

        /// <summary>
        /// Each neighbourhood on its own shelf (the tester: different neighbourhoods at different heights, as Sunset Valley and
        /// Appaloosa Plains). EA's neighbourhoods (lots within 40 m of each other) stand a median 25 m apart in level from
        /// the nearest other one in Sunset Valley, 12.5 m in Appaloosa Plains, 9.6 m in Twinbrook (tools/roadlearn/hoodgaps.py;
        /// ours were 0.4-1.4 m). The neighbourhood nearest the centre keeps its level; each other one, working outwards, stands 'step'
        /// metres (x0.6-1.2) above the nearest one already set, but never lower than its own ground already is, so hillside ones keep
        /// theirs. The land under it and round it is lifted
        /// by that much, easing out over 140-420 m (a broad rise that keeps the ground's own bumps, not a mesa). Run before the ground is smoothed under
        /// the roads (which then climb from shelf to shelf) and before the lot pads are levelled. Returns how many shelves.
        /// </summary>
        public static int HoodShelves(Grid g, float cell, List<PlannedLot> lots, float sea, float[] centre, float step, int seed)
        {
            if (lots.Count < 6 || step < 1 || centre == null) return 0;
            int n = lots.Count;
            float[][] c = new float[n][]; float[] rad = new float[n];
            for (int k = 0; k < n; k++) { c[k] = lots[k].Centre; rad[k] = (float)Math.Sqrt(Sq(lots[k].Width) + Sq(lots[k].Depth)) / 2f; }
            // neighbourhoods: lots within 40 m of each other (outline to outline, approximated by their half-diagonals)
            int[] par = new int[n];
            for (int k = 0; k < n; k++) par[k] = k;
            for (int a = 0; a < n; a++)
                for (int b = a + 1; b < n; b++)
                    if (Math.Sqrt(Sq(c[a][0] - c[b][0]) + Sq(c[a][1] - c[b][1])) < rad[a] + rad[b] + 40)
                    { int ra = Root(par, a), rb = Root(par, b); if (ra != rb) par[ra] = rb; }
            Dictionary<int, List<int>> groups = new Dictionary<int, List<int>>();
            for (int k = 0; k < n; k++) { int r = Root(par, k); List<int> l; if (!groups.TryGetValue(r, out l)) groups[r] = l = new List<int>(); l.Add(k); }
            List<List<int>> G = new List<List<int>>();
            foreach (List<int> l in groups.Values) if (l.Count >= 3) G.Add(l);
            if (G.Count < 2) return 0;
            // each one's own ground level and distance from the centre
            float[] level = new float[G.Count], dist = new float[G.Count];
            for (int q = 0; q < G.Count; q++)
            {
                List<float> hs = new List<float>(); float dmin = float.MaxValue;
                foreach (int k in G[q])
                {
                    hs.Add(TerrainGen.Sample(g, c[k][0] / cell, c[k][1] / cell));
                    dmin = Math.Min(dmin, (float)Math.Sqrt(Sq(c[k][0] - centre[0]) + Sq(c[k][1] - centre[1])));
                }
                hs.Sort(); level[q] = hs[hs.Count / 2]; dist[q] = dmin;
            }
            int core = 0;
            for (int q = 1; q < G.Count; q++) if (dist[q] < dist[core]) core = q;
            Random rng = new Random(seed * 3571 + 19);
            float[] target = new float[G.Count];
            // outward from the core: each one 'step' (x0.6-1.2) above the nearest one already set, so neighbouring
            // neighbourhoods differ by about the world's step (a rise per 250 m overshot: Sunset Valley 45 m vs EA 25)
            int[] order = new int[G.Count];
            for (int q = 0; q < G.Count; q++) order[q] = q;
            Array.Sort((float[])dist.Clone(), order);
            bool[] set = new bool[G.Count];
            target[core] = level[core]; set[core] = true;
            foreach (int q in order)
            {
                if (set[q]) continue;
                int near = core; float best = float.MaxValue;
                for (int o = 0; o < G.Count; o++)
                {
                    if (!set[o]) continue;
                    float dd = float.MaxValue;
                    foreach (int a in G[q]) foreach (int b in G[o]) dd = Math.Min(dd, Sq(c[a][0] - c[b][0]) + Sq(c[a][1] - c[b][1]));
                    if (dd < best) { best = dd; near = o; }
                }
                float rise = step * (0.6f + 0.6f * (float)rng.NextDouble());
                target[q] = Math.Max(level[q], target[near] + rise);
                target[q] = Math.Max(target[q], sea + 1f);
                set[q] = true;
            }
            // the lift: a broad rise of the land, not a mesa (a flat shelf with a bank round it drew round mounds). Per
            // neighbourhood a weight, 1 over its lots and 12 m round them, easing to 0 over 'reach' (the further it rises,
            // the further out: about 7 deg away from other neighbourhoods), its edge wandering with noise; the lift at a
            // point is the mean of the neighbourhoods' rises weighted by w^4 (the core's is 0; the land beyond them all by (1-max w)^4), so between two neighbourhoods the
            // land climbs from one level to the other, and the ground keeps its own bumps. Worked out on a grid of ~8 m
            // cells and interpolated. The lot pads are levelled afterwards.
            const float apron = 12f;
            float[] reach = new float[G.Count], up = new float[G.Count];
            for (int q = 0; q < G.Count; q++) { up[q] = target[q] - level[q]; reach[q] = Math.Min(420f, Math.Max(140f, up[q] / 0.12f + 60f)); }
            TerrainGen.Perlin pn = new TerrainGen.Perlin(seed * 7919 + 5);
            float step8 = Math.Max(cell, 8f);
            int cw = (int)Math.Ceiling((g.W - 1) * cell / step8) + 1, ch = (int)Math.Ceiling((g.H - 1) * cell / step8) + 1;
            float[] lift = new float[cw * ch];
            for (int cz = 0; cz < ch; cz++)
                for (int cx = 0; cx < cw; cx++)
                {
                    float px = cx * step8, pz = cz * step8, sw = 0, sl = 0, wmax = 0;
                    float warp = 1 + 0.35f * pn.Fbm(px / 170f, pz / 170f, 3), shift = 20f * pn.Fbm(px / 60f + 31, pz / 60f + 7, 2);
                    for (int q = 0; q < G.Count; q++)
                    {
                        float d = float.MaxValue;
                        foreach (int k in G[q]) d = Math.Min(d, (float)Math.Sqrt(Sq(px - c[k][0]) + Sq(pz - c[k][1])) - rad[k] - apron);
                        if (d > 0) d = Math.Max(0, d * warp + shift);
                        if (d >= reach[q]) continue;
                        float w = 1 - d / reach[q];
                        w = w * w * (3 - 2 * w);
                        wmax = Math.Max(wmax, w);
                        float w4 = w * w * w * w; // the nearest neighbourhood keeps its own level (a plain mean diluted it)
                        sw += w4; sl += w4 * up[q];
                    }
                    float wb = (1 - wmax) * (1 - wmax); wb *= wb; // the untouched land, beyond every neighbourhood's reach
                    lift[cz * cw + cx] = sw > 0 ? sl / (sw + wb) : 0;
                }
            for (int z = 0; z < g.H; z++)
                for (int x = 0; x < g.W; x++)
                {
                    int i = z * g.W + x;
                    float h = g.D[i];
                    if (h <= sea) continue; // water stays water
                    float fx = x * cell / step8, fz = z * cell / step8;
                    int ix = Math.Min(cw - 2, (int)fx), iz = Math.Min(ch - 2, (int)fz);
                    float tx = fx - ix, tz = fz - iz;
                    float l = (lift[iz * cw + ix] * (1 - tx) + lift[iz * cw + ix + 1] * tx) * (1 - tz)
                            + (lift[(iz + 1) * cw + ix] * (1 - tx) + lift[(iz + 1) * cw + ix + 1] * tx) * tz;
                    if (l <= 0) continue;
                    // ease the lift in from the shore, so the coast keeps its line
                    g.D[i] = h + l * Math.Min(1f, (h - sea) / 3f);
                }
            return G.Count;
        }

        private static int Root(int[] p, int a) { while (p[a] != a) { p[a] = p[p[a]]; a = p[a]; } return a; }

        /// <summary>
        /// Levels each lot's pad to the ground at its front edge (the road's height there) and grades the ground round it
        /// back into the land: a bank no steeper than ~17 deg (0.3 m per m), eased in over the first 16 m and meeting the
        /// land with a rounded edge (soft clamp), so a hillside lot sits on a terrace that blends into its surroundings
        /// (the tester, 2026-10-24/25); EA's hillside lots step up the same way (Isla Paradiso, Shang Simla, Sunset Valley).
        /// The ground under the roads (map metres, polylines; 7 m either side, fading out by 12 m) is left as it is: the
        /// banks had clamped it to each lot's pad, so a road past lots at different heights went up in steps (#133).
        /// Other lots' pads and water are left alone.
        /// </summary>
        public static void LevelPads(Grid g, float cell, List<PlannedLot> lots, float sea, List<List<float[]>> roads)
        {
            const float slope = 0.3f, minBlend = 16f, maxBlend = 100f, soft = 1.5f;
            if (lots.Count == 0) return;
            byte[] keep = RoadKeep(g, cell, roads); // 255 = the road's own ground, 0 = free to change
            float[] pads = new float[lots.Count];
            int[][] box = new int[lots.Count][];
            for (int k = 0; k < lots.Count; k++)
            {
                PlannedLot lot = lots[k];
                pads[k] = Math.Max(sea + 1f, TerrainGen.Sample(g, (lot.X + lot.Ux * lot.Width / 2f) / cell, (lot.Z + lot.Uz * lot.Width / 2f) / cell));
                float[][] c = Corners(lot.X, lot.Z, lot.Ux, lot.Uz, lot.Width, lot.Depth);
                float minx = float.MaxValue, maxx = float.MinValue, minz = float.MaxValue, maxz = float.MinValue;
                foreach (float[] q in c) { minx = Math.Min(minx, q[0]); maxx = Math.Max(maxx, q[0]); minz = Math.Min(minz, q[1]); maxz = Math.Max(maxz, q[1]); }
                box[k] = new[] { Math.Max(0, (int)((minx - maxBlend) / cell)), Math.Min(g.W - 1, (int)((maxx + maxBlend) / cell) + 1),
                                 Math.Max(0, (int)((minz - maxBlend) / cell)), Math.Min(g.H - 1, (int)((maxz + maxBlend) / cell) + 1) };
            }
            // in bands of rows (bounded memory): every lot near a point bounds its height from below and above; a point
            // inside a lot takes that pad; where two lots' bounds cross (a terrace step between them) the ground kept between the two
            const int band = 128;
            float[] lo = new float[band * g.W], hi = new float[band * g.W], inside = new float[band * g.W];
            for (int z0 = 0; z0 < g.H; z0 += band)
            {
                int z1 = Math.Min(g.H, z0 + band);
                for (int i = 0; i < lo.Length; i++) { lo[i] = float.MinValue; hi[i] = float.MaxValue; inside[i] = float.NaN; }
                bool any = false;
                for (int k = 0; k < lots.Count; k++)
                {
                    int[] bx = box[k];
                    if (bx[3] < z0 || bx[2] >= z1) continue;
                    any = true;
                    PlannedLot lot = lots[k];
                    for (int z = Math.Max(z0, bx[2]); z <= Math.Min(z1 - 1, bx[3]); z++)
                        for (int x = bx[0]; x <= bx[1]; x++)
                        {
                            float d = Outside(lot, x * cell, z * cell);
                            int i = (z - z0) * g.W + x;
                            if (d <= 0) { inside[i] = pads[k]; continue; }
                            if (d >= maxBlend) continue;
                            // flat at the pad's rim, curving to 'slope' metres per metre by minBlend out
                            float off = d < minBlend ? slope * d * d / (2 * minBlend) : slope * (d - minBlend / 2);
                            lo[i] = Math.Max(lo[i], pads[k] - off); hi[i] = Math.Min(hi[i], pads[k] + off);
                        }
                }
                if (!any) continue;
                for (int z = z0; z < z1; z++)
                    for (int x = 0; x < g.W; x++)
                    {
                        int i = (z - z0) * g.W + x;
                        float y = g[x, z];
                        if (y < sea) continue;
                        if (!float.IsNaN(inside[i])) { g[x, z] = inside[i]; continue; }
                        if (lo[i] == float.MinValue && hi[i] == float.MaxValue) continue;
                        // where two lots' bounds cross (a lower and a higher terrace), the ground between them is kept
                        // within the band the two embankments leave, which joins both continuously; the clamp is soft
                        // (rounded over ~1.5 m), so the bank meets the land without a crease
                        float t = lo[i] <= hi[i] ? SoftMax(lo[i], SoftMin(hi[i], y, soft), soft) : SoftMax(hi[i], SoftMin(lo[i], y, soft), soft);
                        float kw = keep == null ? 0 : keep[z * g.W + x] / 255f;
                        g[x, z] = t + (y - t) * kw;
                    }
            }
        }

        private static float SoftMax(float a, float b, float k) { if (a == float.MinValue) return b; if (b == float.MinValue) return a; return (a + b + (float)Math.Sqrt((a - b) * (a - b) + k * k)) / 2 - k / 2 * SoftBias(a, b, k); }
        private static float SoftMin(float a, float b, float k) { if (a == float.MaxValue) return b; if (b == float.MaxValue) return a; return (a + b - (float)Math.Sqrt((a - b) * (a - b) + k * k)) / 2 + k / 2 * SoftBias(a, b, k); }
        // the rounding's offset fades with distance, so far from the bound the ground is exactly as it was
        private static float SoftBias(float a, float b, float k) { float d = Math.Abs(a - b) / k; return 1 / (1 + d * d); }

        /// <summary>Per cell, how much of its own ground a road keeps (255 within 7 m of a road's centre line, 0 beyond 12 m).</summary>
        private static byte[] RoadKeep(Grid g, float cell, List<List<float[]>> roads)
        {
            if (roads == null || roads.Count == 0) return null;
            byte[] k = new byte[g.W * g.H];
            const float inner = 7f, outer = 12f;
            foreach (List<float[]> r in roads)
                for (int j = 1; j < r.Count; j++)
                {
                    float[] a = r[j - 1], b = r[j];
                    int x0 = Math.Max(0, (int)((Math.Min(a[0], b[0]) - outer) / cell)), x1 = Math.Min(g.W - 1, (int)((Math.Max(a[0], b[0]) + outer) / cell) + 1);
                    int z0 = Math.Max(0, (int)((Math.Min(a[1], b[1]) - outer) / cell)), z1 = Math.Min(g.H - 1, (int)((Math.Max(a[1], b[1]) + outer) / cell) + 1);
                    for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                        {
                            float d = PtSeg(x * cell, z * cell, a[0], a[1], b[0], b[1]);
                            if (d >= outer) continue;
                            byte v = d <= inner ? (byte)255 : (byte)(255 * (1 - (d - inner) / (outer - inner)));
                            int i = z * g.W + x;
                            if (v > k[i]) k[i] = v;
                        }
                }
            return k;
        }

        /// <summary>Metres outside the lot's rectangle (0 or less inside).</summary>
        private static float Outside(PlannedLot lot, float wx, float wz)
        {
            float px = wx - lot.X, pz = wz - lot.Z;
            float a = px * lot.Ux + pz * lot.Uz, b = px * lot.Vx + pz * lot.Vz;
            float dx = Math.Max(0, Math.Max(-a, a - lot.Width)), dz = Math.Max(0, Math.Max(-b, b - lot.Depth));
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

    }
}
