using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace CawPlus
{
    /// <summary>
    /// Terrain and water statistics of one heightmap on the 8 m planning grid, the same for EA's worlds
    /// (library/&lt;key&gt;_terrain.json, measured by tools/Fidelity) and for a generated map.
    /// </summary>
    internal class TerrainProfile
    {
        public string Key;           // library file it came from (e.g. "riverview_terrain.json"), so CAWPlusHelper can load the same one
        public float Top, Relief, FlatShare, SlopeMedian, RelFine = float.NaN, RelMid = float.NaN; // fine: bumps under ~25 m; mid: ~25-120 m (metres RMS, land)
        public float[] HeightHist = new float[10], SlopeHist = new float[SlopeEdges.Length + 1];
        public float[] HeightCurve = new float[11]; // land height / top at 0, 10 ... 100 % of the land (100 % = the 99.5th percentile)
        // the land's texture, place by place: bumps under ~25 m (RMS per 64 m block) and 25-120 m relief (per 128 m block),
        // at the 25th, 50th, 75th and 90th percentile of the land's blocks. A world with rugged highland and flat lowland
        // (Aurora Skies) and one with the same average everywhere differ here, not in RelFine/RelMid. Empty = not measured.
        public float[] FineQ = new float[0], MidQ = new float[0];
        // water
        public float Water, Ocean, Lake, River, Centrality = -1, Shore, Raised, RiverFromEdge = -1; // Raised: water above the sea (EA's ponds, resource 0x707CA200)
        public int Lakes;
        public float[] EdgeWater = new float[4]; // share of each map edge under water, largest first

        // landform structure (#95): hill masses standing on an open floor (Lucky Palms' buttes) or one continuous upland
        // (Sunset Valley). Floor = the ground with everything narrower than ~500 m taken off (min then max over a 500 m
        // square); a hill mass = connected land over 15 m above it, counted from 2 ha. NaN = not measured.
        public float HillMasses = float.NaN, MassHaMedian = float.NaN, LargestMassShare = float.NaN, RaisedShare = float.NaN, FloorConnected = float.NaN;
        // feature checklist (#95, LandChecklist): Want = the world type's (library/<key>_landform.json), Got = this map's
        public bool?[] Want;
        public bool[] Got;

        public static readonly float[] SlopeEdges = { 2, 4, 7, 10, 15, 20, 25, 30, 40 };

        /// <summary>ponds: (water level, x0, z0, x1, z1) in metres; ground below the level inside the rectangle is water.</summary>
        public static void Fill(TerrainProfile p, Grid h, float cell, float sea, List<float[]> ponds)
        {
            int n = h.W, N = n * n;
            bool[] water = new bool[N];
            int nw = 0, nraised = 0;
            for (int i = 0; i < N; i++) if (h.D[i] < sea) { water[i] = true; nw++; }
            if (ponds != null)
                foreach (float[] q in ponds)
                {
                    // ground below the level inside the rectangle, as separate hollows; the lowest hollows first until the
                    // pond's own area (q[5] m2, from its record count) is filled
                    int x0 = Math.Max(0, (int)(q[1] / cell)), z0 = Math.Max(0, (int)(q[2] / cell)), x1 = Math.Min(n - 1, (int)(q[3] / cell)), z1 = Math.Min(n - 1, (int)(q[4] / cell));
                    float want = q.Length > 5 ? q[5] / (cell * cell) : float.MaxValue;
                    List<List<int>> hollows = new List<List<int>>(); List<float> lows = new List<float>();
                    bool[] seen = new bool[N];
                    for (int y = z0; y <= z1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            int s0 = y * n + x;
                            if (seen[s0] || water[s0] || h.D[s0] >= q[0]) continue;
                            List<int> hol = new List<int>(); Stack<int> st = new Stack<int>(); st.Push(s0); seen[s0] = true; float low = float.MaxValue;
                            while (st.Count > 0)
                            {
                                int i = st.Pop(); hol.Add(i); low = Math.Min(low, h.D[i]);
                                int cx2 = i % n, cy2 = i / n;
                                foreach (int j in new[] { cx2 > x0 ? i - 1 : -1, cx2 < x1 ? i + 1 : -1, cy2 > z0 ? i - n : -1, cy2 < z1 ? i + n : -1 })
                                    if (j >= 0 && !seen[j] && !water[j] && h.D[j] < q[0]) { seen[j] = true; st.Push(j); }
                            }
                            hollows.Add(hol); lows.Add(low);
                        }
                    int[] order = new int[hollows.Count]; for (int k = 0; k < order.Length; k++) order[k] = k;
                    Array.Sort(lows.ToArray(), order);
                    float filled = 0;
                    foreach (int k in order)
                    {
                        if (filled >= want) break;
                        foreach (int i in hollows[k]) { water[i] = true; nw++; nraised++; }
                        filled += hollows[k].Count;
                    }
                }
            p.Raised = (float)nraised / N;
            p.Water = (float)nw / N;

            // land: height above the sea relative to the 99.5th percentile, slopes, local relief
            List<float> land = new List<float>(), slopes = new List<float>();
            Grid broad = h.Blur(150f / cell), b24 = h.Blur(24f / cell), b120 = h.Blur(120f / cell);
            double rel = 0, fine = 0, mid = 0;
            for (int y = 1; y < n - 1; y++)
                for (int x = 1; x < n - 1; x++)
                {
                    int i = y * n + x;
                    if (water[i]) continue;
                    float gx = (h.D[i + 1] - h.D[i - 1]) / (2 * cell), gy = (h.D[i + n] - h.D[i - n]) / (2 * cell);
                    slopes.Add((float)(Math.Atan(Math.Sqrt(gx * gx + gy * gy)) * 180 / Math.PI));
                    land.Add(h.D[i] - sea);
                    double d = h.D[i] - broad.D[i]; rel += d * d;
                    double df = h.D[i] - b24.D[i], dm = b24.D[i] - b120.D[i]; fine += df * df; mid += dm * dm;
                }
            if (land.Count > 0)
            {
                land.Sort(); slopes.Sort();
                p.Top = Math.Max(1, land[(int)(land.Count * 0.995f)]);
                p.RelFine = (float)Math.Sqrt(fine / land.Count); p.RelMid = (float)Math.Sqrt(mid / land.Count);
                for (int k = 0; k <= 10; k++) p.HeightCurve[k] = Math.Max(0, land[(int)(Math.Min(0.995f, k / 10f) * (land.Count - 1))]) / p.Top;
                foreach (float v in land) p.HeightHist[Math.Min(9, Math.Max(0, (int)(v / p.Top * 10)))]++;
                foreach (float s in slopes) p.SlopeHist[Band(s, SlopeEdges)]++;
                Norm(p.HeightHist); Norm(p.SlopeHist);
                p.SlopeMedian = slopes[slopes.Count / 2];
                p.FlatShare = p.SlopeHist[0] + p.SlopeHist[1] + 0.33f * p.SlopeHist[2];
                p.Relief = (float)Math.Sqrt(rel / land.Count) / p.Top;
                p.FineQ = BlockQuantiles(h, h, b24, water, 8); p.MidQ = BlockQuantiles(h, b24, b120, water, 16);
                Structure(p, h, cell, sea);
                p.Got = LandChecklist.Measure(h, cell, sea);
            }
            if (nw == 0) return;

            // distance to land inside water, then 'width' = the largest distance within 60 m: narrow water is a river
            float[] d2 = Chamfer(water, n, cell);
            float[] wide = SlidingMax(d2, n, (int)Math.Ceiling(60 / cell));
            bool[] narrow = new bool[N];
            int nr = 0;
            for (int i = 0; i < N; i++) if (water[i] && wide[i] < 30) { narrow[i] = true; nr++; }
            p.River = (float)nr / N;
            {
                // narrow water (rivers) joined to the map edge: rivers that flow in from beyond the map, as EA's often do
                int[] rc = new int[N]; for (int i = 0; i < N; i++) rc[i] = -1;
                Stack<int> rs = new Stack<int>(); int edgeCells = 0;
                for (int s0 = 0; s0 < N; s0++)
                {
                    if (!narrow[s0] || rc[s0] >= 0) continue;
                    List<int> cells = new List<int>(); bool touches = false;
                    rc[s0] = s0; rs.Push(s0);
                    while (rs.Count > 0)
                    {
                        int i = rs.Pop(), x = i % n, y = i / n; cells.Add(i);
                        if (x <= 1 || y <= 1 || x >= n - 2 || y >= n - 2) touches = true;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx, yy = y + dy;
                                if (xx < 0 || yy < 0 || xx >= n || yy >= n) continue;
                                int j = yy * n + xx;
                                if (narrow[j] && rc[j] < 0) { rc[j] = s0; rs.Push(j); }
                            }
                    }
                    if (touches) edgeCells += cells.Count;
                }
                p.RiverFromEdge = nr > 0 ? (float)edgeCells / nr : 0;
            }
            if (Environment.GetEnvironmentVariable("CAWDBG") != null)
            {
                int[] hb = new int[8];
                for (int i = 0; i < N; i++) if (water[i]) hb[Math.Min(7, (int)(wide[i] / 10))]++;
                Console.Error.WriteLine("DBG water half-width (max dist to land within 60 m) 0-10,10-20..70+ m: " + string.Join(" ", Array.ConvertAll(hb, v => (100.0 * v / N).ToString("F2"))));
            }

            // the rest: sea (touches the map edge) or lakes (inland, >= 1 ha)
            int[] comp = new int[N];
            for (int i = 0; i < N; i++) comp[i] = -1;
            int[] stack = new int[N];
            float minLake = 10000f / (cell * cell);
            HashSet<int> edgeComps = new HashSet<int>();
            double cx = 0; int cnt = 0; float c = (n - 1) / 2f;
            for (int s = 0; s < N; s++)
            {
                if (!water[s] || narrow[s] || comp[s] >= 0) continue;
                int top = 0, area = 0; bool edge = false;
                stack[top++] = s; comp[s] = s;
                while (top > 0)
                {
                    int i = stack[--top]; area++;
                    int x = i % n, y = i / n;
                    if (x == 0 || y == 0 || x == n - 1 || y == n - 1) edge = true;
                    if (x > 0) Push(i - 1, s, water, narrow, comp, stack, ref top);
                    if (x < n - 1) Push(i + 1, s, water, narrow, comp, stack, ref top);
                    if (y > 0) Push(i - n, s, water, narrow, comp, stack, ref top);
                    if (y < n - 1) Push(i + n, s, water, narrow, comp, stack, ref top);
                }
                if (edge) { p.Ocean += (float)area / N; edgeComps.Add(s); }
                else if (area >= minLake) { p.Lake += (float)area / N; p.Lakes++; }
            }
            string dump = Environment.GetEnvironmentVariable("CAWPGM");
            if (dump != null)
            {
                // water classes for a look: land by height (dark), sea 255, lake 200, river 140
                using (FileStream f = File.Create(dump))
                {
                    byte[] hd = System.Text.Encoding.ASCII.GetBytes("P5 " + n + " " + n + " 255\n");
                    f.Write(hd, 0, hd.Length);
                    for (int i = 0; i < N; i++)
                    {
                        byte b = (byte)Math.Max(0, Math.Min(100, (h.D[i] - sea) / 2));
                        if (narrow[i]) b = 140; else if (water[i]) b = edgeComps.Contains(comp[i]) ? (byte)255 : (byte)200;
                        f.WriteByte(b);
                    }
                }
            }
            for (int i = 0; i < N; i++)
                if (water[i]) { float x = i % n - c, y = i / n - c; cx += Math.Sqrt(x * x + y * y) / c; cnt++; }
            p.Centrality = (float)(cx / cnt);
            // shoreline length against the circle of the same water area (1 = round, larger = ragged / many pieces)
            int boundary = 0;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    if (!water[i]) continue;
                    if (x > 0 && !water[i - 1]) boundary++;
                    if (x < n - 1 && !water[i + 1]) boundary++;
                    if (y > 0 && !water[i - n]) boundary++;
                    if (y < n - 1 && !water[i + n]) boundary++;
                }
            p.Shore = (float)(boundary / (2 * Math.Sqrt(Math.PI * nw)));
            int[] e = new int[4];
            for (int k = 0; k < n; k++)
            {
                if (water[k]) e[0]++;
                if (water[(n - 1) * n + k]) e[1]++;
                if (water[k * n]) e[2]++;
                if (water[k * n + n - 1]) e[3]++;
            }
            for (int k = 0; k < 4; k++) p.EdgeWater[k] = (float)e[k] / n;
            Array.Sort(p.EdgeWater); Array.Reverse(p.EdgeWater);
        }

        static void Push(int j, int s, bool[] water, bool[] narrow, int[] comp, int[] stack, ref int top)
        {
            if (water[j] && !narrow[j] && comp[j] < 0) { comp[j] = s; stack[top++] = j; }
        }

        private static int Band(float v, float[] edges) { int b = 0; while (b < edges.Length && v >= edges[b]) b++; return b; }

        protected static void Norm(float[] a) { float s = 0; foreach (float v in a) s += v; if (s > 0) for (int i = 0; i < a.Length; i++) a[i] /= s; }

        static float[] Chamfer(bool[] water, int n, float cell)
        {
            float[] d = new float[n * n];
            const float B = 1e6f;
            for (int i = 0; i < d.Length; i++) d[i] = water[i] ? B : 0;
            float a = cell, b = cell * 1.4142f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x; if (d[i] == 0) continue;
                    if (x > 0) d[i] = Math.Min(d[i], d[i - 1] + a);
                    if (y > 0) { d[i] = Math.Min(d[i], d[i - n] + a); if (x > 0) d[i] = Math.Min(d[i], d[i - n - 1] + b); if (x < n - 1) d[i] = Math.Min(d[i], d[i - n + 1] + b); }
                }
            for (int y = n - 1; y >= 0; y--)
                for (int x = n - 1; x >= 0; x--)
                {
                    int i = y * n + x; if (d[i] == 0) continue;
                    if (x < n - 1) d[i] = Math.Min(d[i], d[i + 1] + a);
                    if (y < n - 1) { d[i] = Math.Min(d[i], d[i + n] + a); if (x < n - 1) d[i] = Math.Min(d[i], d[i + n + 1] + b); if (x > 0) d[i] = Math.Min(d[i], d[i + n - 1] + b); }
                }
            // water reaching the map edge: treat beyond the edge as more water (cap, so open sea isn't infinite)
            for (int i = 0; i < d.Length; i++) d[i] = Math.Min(d[i], 2000);
            return d;
        }

        static float[] SlidingMax(float[] v, int n, int r)
        {
            float[] t = new float[v.Length], o = new float[v.Length];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float m = 0;
                    for (int k = Math.Max(0, x - r); k <= Math.Min(n - 1, x + r); k++) m = Math.Max(m, v[y * n + k]);
                    t[y * n + x] = m;
                }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float m = 0;
                    for (int k = Math.Max(0, y - r); k <= Math.Min(n - 1, y + r); k++) m = Math.Max(m, t[k * n + x]);
                    o[y * n + x] = m;
                }
            return o;
        }

        /// <summary>How alike two maps' land textures are (FineQ, MidQ quantile by quantile), 0..1; NaN if not measured.</summary>
        public static double Texture(TerrainProfile g, TerrainProfile e)
        {
            if (e.FineQ.Length != 4 || g.FineQ.Length != 4 || e.MidQ.Length != 4 || g.MidQ.Length != 4) return double.NaN;
            double s = 0;
            for (int k = 0; k < 4; k++)
                s += Math.Min(g.FineQ[k] + 0.2, e.FineQ[k] + 0.2) / Math.Max(g.FineQ[k] + 0.2, e.FineQ[k] + 0.2)
                   + Math.Min(g.MidQ[k] + 0.5, e.MidQ[k] + 0.5) / Math.Max(g.MidQ[k] + 0.5, e.MidQ[k] + 0.5);
            return s / 8;
        }

        /// <summary>RMS of (a - b) per block of 'B' cells (blocks at least 3/4 land), at the 25/50/75/90th percentile.</summary>
        private static float[] BlockQuantiles(Grid h, Grid a, Grid b, bool[] water, int B)
        {
            int n = h.W, nb = n / B;
            List<float> v = new List<float>();
            for (int by = 0; by < nb; by++)
                for (int bx = 0; bx < nb; bx++)
                {
                    double s = 0; int land = 0;
                    for (int y = by * B; y < by * B + B; y++)
                        for (int x = bx * B; x < bx * B + B; x++)
                        {
                            int i = y * n + x;
                            if (water[i]) continue;
                            double d = a.D[i] - b.D[i]; s += d * d; land++;
                        }
                    if (land * 4 >= B * B * 3) v.Add((float)Math.Sqrt(s / land));
                }
            if (v.Count < 4) return new float[0];
            v.Sort();
            return new[] { v[v.Count / 4], v[v.Count / 2], v[v.Count * 3 / 4], v[v.Count * 9 / 10] };
        }

        /// <summary>The landform structure figures (see the fields).</summary>
        public static void Structure(TerrainProfile p, Grid h, float cell, float sea)
        {
            int n = h.W, N = n * h.H;
            float[] hc = new float[N];
            for (int i = 0; i < N; i++) hc[i] = Math.Max(h.D[i], sea);
            int r = Math.Max(1, (int)(250f / cell));
            float[] open = SlideMax(SlideMin(hc, n, h.H, r), n, h.H, r);
            bool[] raised = new bool[N], floor = new bool[N];
            int land = 0, nr = 0, nf = 0;
            for (int i = 0; i < N; i++)
            {
                if (h.D[i] <= sea) continue;
                land++;
                if (hc[i] - open[i] > 15) { raised[i] = true; nr++; } else { floor[i] = true; nf++; }
            }
            if (land == 0) return;
            float ha = cell * cell / 1e4f;
            List<int> masses = Components(raised, n, h.H);
            List<float> big = new List<float>();
            float total = 0;
            foreach (int c in masses) if (c * ha >= 2) { big.Add(c * ha); total += c * ha; }
            big.Sort();
            p.HillMasses = big.Count;
            p.MassHaMedian = big.Count > 0 ? big[big.Count / 2] : 0;
            p.LargestMassShare = big.Count > 0 ? big[big.Count - 1] / total : 0;
            p.RaisedShare = (float)nr / land;
            int fmax = 0; foreach (int c in Components(floor, n, h.H)) fmax = Math.Max(fmax, c);
            p.FloorConnected = nf > 0 ? (float)fmax / nf : 1;
        }

        /// <summary>Sizes (cells) of the 4-connected groups of true cells.</summary>
        private static List<int> Components(bool[] m, int w, int hgt)
        {
            List<int> sizes = new List<int>();
            bool[] seen = new bool[m.Length];
            Stack<int> st = new Stack<int>();
            for (int i = 0; i < m.Length; i++)
            {
                if (!m[i] || seen[i]) continue;
                int c = 0; seen[i] = true; st.Push(i);
                while (st.Count > 0)
                {
                    int j = st.Pop(); c++;
                    int x = j % w, y = j / w;
                    if (x > 0 && m[j - 1] && !seen[j - 1]) { seen[j - 1] = true; st.Push(j - 1); }
                    if (x < w - 1 && m[j + 1] && !seen[j + 1]) { seen[j + 1] = true; st.Push(j + 1); }
                    if (y > 0 && m[j - w] && !seen[j - w]) { seen[j - w] = true; st.Push(j - w); }
                    if (y < hgt - 1 && m[j + w] && !seen[j + w]) { seen[j + w] = true; st.Push(j + w); }
                }
                sizes.Add(c);
            }
            return sizes;
        }

        internal static float[] SlideMin(float[] a, int w, int hgt, int r) { return Slide(a, w, hgt, r, true); }
        internal static float[] SlideMax(float[] a, int w, int hgt, int r) { return Slide(a, w, hgt, r, false); }

        /// <summary>Minimum (or maximum) over a (2r+1)-square, rows then columns (clipped at the edges).</summary>
        private static float[] Slide(float[] a, int w, int hgt, int r, bool min)
        {
            float[] t = new float[a.Length], o = new float[a.Length];
            for (int y = 0; y < hgt; y++)
                for (int x = 0; x < w; x++)
                {
                    float v = a[y * w + x];
                    for (int k = Math.Max(0, x - r); k <= Math.Min(w - 1, x + r); k++) { float q = a[y * w + k]; if (min ? q < v : q > v) v = q; }
                    t[y * w + x] = v;
                }
            for (int y = 0; y < hgt; y++)
                for (int x = 0; x < w; x++)
                {
                    float v = t[y * w + x];
                    for (int k = Math.Max(0, y - r); k <= Math.Min(hgt - 1, y + r); k++) { float q = t[k * w + x]; if (min ? q < v : q > v) v = q; }
                    o[y * w + x] = v;
                }
            return o;
        }

        /// <summary>Reads library/&lt;key&gt;_terrain.json (flat JSON written by tools/Fidelity).</summary>
        public static TerrainProfile Parse(string text)
        {
            TerrainProfile p = new TerrainProfile();
            p.Top = Num(text, "top_m"); p.Relief = Num(text, "relief"); p.FlatShare = Num(text, "flat_share"); p.SlopeMedian = Num(text, "slope_median_deg");
            p.HeightHist = Arr(text, "height_hist"); p.SlopeHist = Arr(text, "slope_hist");
            p.Water = Num(text, "water"); p.Ocean = Num(text, "ocean"); p.Lake = Num(text, "lake"); p.River = Num(text, "river");
            p.Lakes = (int)Num(text, "lakes"); p.Shore = Num(text, "shore_raggedness");
            p.RelFine = Num(text, "rel_fine_m"); p.RelMid = Num(text, "rel_mid_m");
            p.FineQ = Arr(text, "fine_q"); p.MidQ = Arr(text, "mid_q");
            p.EdgeWater = Arr(text, "edge_water");
            ReadStructure(p, text);
            return p;
        }

        public static void ReadStructure(TerrainProfile p, string text)
        {
            if (!text.Contains("hill_masses")) return;
            p.HillMasses = Num(text, "hill_masses"); p.MassHaMedian = Num(text, "mass_ha_median"); p.LargestMassShare = Num(text, "largest_mass_share");
            p.RaisedShare = Num(text, "raised_share"); p.FloorConnected = Num(text, "floor_connected");
        }

        private static float Num(string t, string k)
        {
            Match m = Regex.Match(t, "\"" + k + "\"\\s*:\\s*(-?[0-9.eE+-]+)");
            float v;
            return m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : float.NaN;
        }

        private static float[] Arr(string t, string k)
        {
            Match m = Regex.Match(t, "\"" + k + "\"\\s*:\\s*\\[([^\\]]*)\\]");
            if (!m.Success) return new float[0];
            string[] s = m.Groups[1].Value.Split(',');
            float[] a = new float[s.Length];
            for (int i = 0; i < s.Length; i++) float.TryParse(s[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out a[i]);
            return a;
        }
    }

    /// <summary>
    /// The terrain checklist (the tester, 2026-10-21: "a pass system ... the same for terrain generation"): a generated map
    /// is measured as EA's worlds were and has to tick off nine points against the Like world's own figures.
    /// TerrainGen.GenerateChecked tries several variations of a seed and keeps the one that passes most.
    /// </summary>
    internal static class TerrainChecks
    {
        public sealed class Result
        {
            public int Passed, Total;
            public double Score;          // 0..1, how close on every check
            public int Seed;              // the variation kept
            public List<string> Failed = new List<string>();
            public string Landforms = "";  // the structure figures, this map against the world type's
            public override string ToString()
            {
                return "Terrain checks " + Passed + "/" + Total + (Failed.Count > 0 ? " (missed: " + string.Join(", ", Failed.ToArray()) + ")" : "")
                       + (Landforms.Length > 0 ? ". " + Landforms : "");
            }
        }

        /// <summary>water: the map's water share to aim for (the Like world's, or the Water slider when moved by hand).</summary>
        public static Result Check(TerrainProfile g, TerrainProfile e, float water)
        {
            Result r = new Result();
            double sum = 0;
            // 1. water amount
            Add(r, ref sum, "water amount", Sim(g.Water, water, Math.Max(0.02, 0.15 * water)), 0.01);
            // 2. sea on the same sides of the map
            if (e.EdgeWater.Length == 4)
            {
                double edge = 0; for (int k = 0; k < 4; k++) edge += Math.Abs(g.EdgeWater[k] - e.EdgeWater[k]);
                Add(r, ref sum, "sea edges", 1 - edge / 4, 0.8);
            }
            // 3. lakes, 4. rivers (only when either map has some)
            if (g.Lake > 0.002 || e.Lake > 0.002) Add(r, ref sum, "lakes", Sim(g.Lake, e.Lake, Math.Max(0.01, 0.5 * e.Lake)), 0.01);
            if (g.River > 0.002 || e.River > 0.002) Add(r, ref sum, "rivers", Sim(g.River, e.River, Math.Max(0.005, 0.5 * e.River)), 0.01);
            // 5. height of the land (99.5th percentile above the sea)
            Add(r, ref sum, "top height", Ratio(g.Top, e.Top), 0.85);
            // 6. how the land is spread over its heights (lowland / hills / peaks)
            Add(r, ref sum, "height spread", Overlap(g.HeightHist, e.HeightHist), 0.75);
            // 7. slopes (flat building land against steep ground)
            Add(r, ref sum, "slopes", Overlap(g.SlopeHist, e.SlopeHist), 0.75);
            // 8. smoothness: bumps under ~25 m and relief over 25-120 m (EA's land is smooth outside its mountains)
            if (!float.IsNaN(e.RelFine) && !float.IsNaN(e.RelMid))
                Add(r, ref sum, "smoothness", Math.Min(Ratio(g.RelFine + 0.1, e.RelFine + 0.1), Ratio(g.RelMid + 0.3, e.RelMid + 0.3)), 0.7);
            // 9b. texture: rugged and smooth ground in the same proportions (the tester: Aurora Skies too soft)
            double tex = TerrainProfile.Texture(g, e);
            if (!double.IsNaN(tex)) Add(r, ref sum, "texture", tex, 0.7);
            // 9. coastline: as clean or ragged as EA's
            if (g.Water > 0.01 && e.Water > 0.01 && g.Shore > 0 && e.Shore > 0) Add(r, ref sum, "coastline", Ratio(g.Shore, e.Shore), 0.6);
            // 10. landform structure (#95): as many separate hill masses of the same size, as much raised ground, the
            // open floor as connected (Lucky Palms: 9 buttes on one floor; Sunset Valley: one upland)
            double st = UseLandforms ? Structure(g, e) : double.NaN;
            if (!double.IsNaN(st))
            {
                Add(r, ref sum, "landforms", st, 0.6);
                r.Landforms = string.Format(CultureInfo.InvariantCulture, "Hill masses {0:F0} (world type {1:F0}), median {2:F0} ha ({3:F0}), raised ground {4:P0} ({5:P0}), open floor {6:P0} connected ({7:P0})",
                    g.HillMasses, e.HillMasses, g.MassHaMedian, e.MassHaMedian, g.RaisedShare, e.RaisedShare, g.FloorConnected, e.FloorConnected);
            }
            // 11. feature checklist (#95): beaches, sea cliffs, islands, river, lakes, mountains, hills, mesas, deep valleys,
            // inland cliffs, a large flat area: the same ones the world type has
            if (UseFeatures && e.Want != null && g.Got != null)
            {
                string missed;
                double f = LandChecklist.Score(e.Want, g.Got, out missed);
                Add(r, ref sum, "features", f, 0.8);
                if (missed.Length > 0) r.Landforms += (r.Landforms.Length > 0 ? ". " : "") + "Features: " + missed;
            }
            r.Score = r.Total > 0 ? sum / r.Total : 0;
            return r;
        }

        public static bool UseLandforms = true, UseFeatures = true; // the landform structure check (tools/Fidelity can switch it off to compare)

        /// <summary>How alike two maps' landform structure is (0..1): the worst of hill-mass count, raised ground, the
        /// largest mass's share and the open floor's connectedness. NaN when either wasn't measured.</summary>
        public static double Structure(TerrainProfile g, TerrainProfile e)
        {
            if (float.IsNaN(e.HillMasses) || float.IsNaN(g.HillMasses)) return double.NaN;
            return Math.Min(Math.Min(Ratio(g.HillMasses + 1, e.HillMasses + 1), Ratio(g.RaisedShare + 0.05, e.RaisedShare + 0.05)),
                            Math.Min(Sim(g.LargestMassShare, e.LargestMassShare, 0.35), Sim(g.FloorConnected, e.FloorConnected, 0.4)));
        }

        private static void Add(Result r, ref double sum, string name, double v, double pass)
        {
            r.Total++; sum += v;
            if (v >= pass) r.Passed++; else r.Failed.Add(name);
        }

        private static double Sim(double a, double b, double scale) { return Math.Max(0, 1 - Math.Abs(a - b) / Math.Max(1e-6, scale)); }
        private static double Ratio(double a, double b) { a = Math.Max(a, 1e-6); b = Math.Max(b, 1e-6); return Math.Min(a, b) / Math.Max(a, b); }
        private static double Overlap(float[] p, float[] q) { double s = 0; for (int i = 0; i < Math.Min(p.Length, q.Length); i++) s += Math.Min(p[i], q[i]); return s; }
    }
}
