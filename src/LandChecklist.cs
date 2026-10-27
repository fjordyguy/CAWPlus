using System;
using System.Collections.Generic;

namespace CawPlus
{
    /// <summary>
    /// The terrain feature checklist (#95) of a map, as tools/terrainreport.py writes it for each EA world
    /// (library/&lt;key&gt;_landform.json "checklist"), measured here on the generator's 8 m planning grid: sea, beaches,
    /// sea cliffs, islands, river, lakes, mountains (> 150 m), hills, plateaus / mesas, deep valleys, inland cliffs, a
    /// large flat area (> 15 ha). The world type's list says what a map of that type should have; GenerateChecked keeps
    /// the seed variation that ticks most of it ("features" check). Object features (mine, lighthouse, piers, ruins,
    /// waterfalls) are not terrain and are left out.
    /// </summary>
    internal static class LandChecklist
    {
        public static readonly string[] Names = { "sea", "beaches", "sea cliffs", "islands", "river", "lakes", "mountains (>150 m)", "hills",
                                                   "plateaus / mesas", "deep valleys", "inland cliffs", "large flat area (>15 ha)" };

        /// <summary>The world type's list from its landform JSON (null where an item is missing).</summary>
        public static bool?[] FromJson(string json)
        {
            bool?[] r = new bool?[Names.Length];
            if (string.IsNullOrEmpty(json)) return r;
            int c = json.IndexOf("\"checklist\"", StringComparison.Ordinal);
            if (c < 0) return r;
            for (int i = 0; i < Names.Length; i++)
            {
                int k = json.IndexOf("\"" + Names[i] + "\"", c, StringComparison.Ordinal);
                if (k < 0) continue;
                int colon = json.IndexOf(':', k);
                string rest = json.Substring(colon + 1, Math.Min(8, json.Length - colon - 1)).Trim();
                r[i] = rest.StartsWith("true");
            }
            return r;
        }

        /// <summary>The checklist of heights 'h' (rows = z, 'cell' metres) with the sea at 'sea'.</summary>
        public static bool[] Measure(Grid h, float cell, float sea)
        {
            int W = h.W, H = h.H, N = W * H;
            bool[] r = new bool[Names.Length];
            bool[] land = new bool[N];
            int landN = 0;
            for (int i = 0; i < N; i++) { land[i] = h.D[i] >= sea + 0.3f; if (land[i]) landN++; }
            if (landN == 0) return r;
            float px = 1f / cell, cellHa = cell * cell / 1e4f;
            // slopes (degrees) of the lightly smoothed ground
            Grid hs = h.Blur(Math.Max(0.5f, 4f * px));
            float[] slope = new float[N];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int xm = Math.Max(0, x - 1), xp = Math.Min(W - 1, x + 1), ym = Math.Max(0, y - 1), yp = Math.Min(H - 1, y + 1);
                    float gx = (hs[xp, y] - hs[xm, y]) / ((xp - xm) * cell), gy = (hs[x, yp] - hs[x, ym]) / ((yp - ym) * cell);
                    slope[y * W + x] = (float)(Math.Atan(Math.Sqrt(gx * gx + gy * gy)) * 180 / Math.PI);
                }
            // water bodies: sea (joined to the edge), lakes, narrow rivers
            int[] wl = Label(delegate (int i) { return !land[i]; }, W, H);
            HashSet<int> edgeIds = new HashSet<int>();
            for (int x = 0; x < W; x++) { if (wl[x] > 0) edgeIds.Add(wl[x]); if (wl[(H - 1) * W + x] > 0) edgeIds.Add(wl[(H - 1) * W + x]); }
            for (int y = 0; y < H; y++) { if (wl[y * W] > 0) edgeIds.Add(wl[y * W]); if (wl[y * W + W - 1] > 0) edgeIds.Add(wl[y * W + W - 1]); }
            Dictionary<int, int> wsize = Sizes(wl);
            int seaN = 0, lakes = 0;
            foreach (KeyValuePair<int, int> e in wsize)
            {
                if (edgeIds.Contains(e.Key)) seaN += e.Value;
                else if (e.Value * cellHa >= 0.5f) lakes++;
            }
            r[0] = seaN > 0.02f * N;
            r[5] = lakes > 0;
            bool[] seaMask = new bool[N];
            for (int i = 0; i < N; i++) seaMask[i] = wl[i] > 0 && edgeIds.Contains(wl[i]);
            float[] dLand = Distance(delegate (int i) { return land[i]; }, W, H, cell);    // in water: metres to land
            float[] dSea = Distance(delegate (int i) { return seaMask[i]; }, W, H, cell);  // metres to the sea
            int narrow = 0, rr = Math.Max(1, (int)(30 / cell));
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    if (land[i]) continue;
                    float mx = 0;
                    for (int yy = Math.Max(0, y - rr); yy <= Math.Min(H - 1, y + rr); yy++)
                        for (int xx = Math.Max(0, x - rr); xx <= Math.Min(W - 1, x + rr); xx++) mx = Math.Max(mx, dLand[yy * W + xx]);
                    if (mx < 30) narrow++;
                }
            r[4] = narrow > 0.003f * N;
            // the coast: beaches (low, gentle land by the sea) and cliffs (a steep face above it), as shares of the shore
            int shore = 0, beach = 0, cliff = 0;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    if (!land[i] || dSea[i] > cell * 1.5f) continue;
                    shore++;
                    bool b = false, c = false;
                    int r3 = Math.Max(1, (int)Math.Round(24 / cell));
                    for (int yy = Math.Max(0, y - r3); yy <= Math.Min(H - 1, y + r3) && !(b && c); yy++)
                        for (int xx = Math.Max(0, x - r3); xx <= Math.Min(W - 1, x + r3); xx++)
                        {
                            int j = yy * W + xx;
                            if (!land[j]) continue;
                            if (dSea[j] <= 30 && h.D[j] - sea < 2.5f && slope[j] < 8) b = true;
                            if (dSea[j] <= 24 && slope[j] > 33 && h.D[j] - sea > 3) c = true;
                        }
                    if (b) beach++; if (c) cliff++;
                }
            r[1] = shore > 0 && beach > 0.2f * shore;
            r[2] = shore > 0 && cliff > 0.4f * shore;
            // islands: three or more pieces of land of a hectare or more
            int[] ll = Label(delegate (int i) { return land[i]; }, W, H);
            int isl = 0; foreach (KeyValuePair<int, int> e in Sizes(ll)) if (e.Value * cellHa >= 1) isl++;
            r[3] = isl >= 3;
            float top = float.MinValue; for (int i = 0; i < N; i++) if (land[i]) top = Math.Max(top, h.D[i]);
            r[6] = top - sea > 150;
            // landforms by relief round each point (60 m and 400 m, sea counted at sea level)
            Grid hc = new Grid(W, H);
            for (int i = 0; i < N; i++) hc.D[i] = Math.Max(h.D[i], sea);
            Grid m60 = hc.Blur(25f * px), m400 = hc.Blur(160f * px);
            int hills = 0, plateau = 0;
            bool[] deep = new bool[N];
            for (int i = 0; i < N; i++)
            {
                if (!land[i]) continue;
                float small = (hc.D[i] - m60.D[i]) / 4f, large = (hc.D[i] - m400.D[i]) / 15f;
                bool flat = slope[i] < 5;
                bool ridge = small > 1 || (large > 1 && small > 0.5f);
                bool upper = small > 0.5f && small <= 1 && !flat;
                if (ridge || upper) hills++;
                if (slope[i] < 8 && large > 0.8f && small > -0.5f && !ridge) plateau++;
                deep[i] = large < -1.5f;
            }
            r[7] = hills > 0.1f * landN;
            r[8] = plateau > 0.03f * landN;
            int[] dl = Label(delegate (int i) { return deep[i]; }, W, H);
            int valleys = 0; foreach (KeyValuePair<int, int> e in Sizes(dl)) if (e.Value * cellHa >= 5) valleys++;
            r[9] = valleys > 0;
            // inland cliffs: three or more steep faces (over 40 deg on the 8 m grid) of 0.2 ha or more
            int[] cl = Label(delegate (int i) { return land[i] && slope[i] > 40; }, W, H);
            int faces = 0; foreach (KeyValuePair<int, int> e in Sizes(cl)) if (e.Value * cellHa >= 0.2f) faces++;
            r[10] = faces >= 3;
            // a large flat area: ground under 10 % over 30 m, 15 ha or more in one piece
            Grid h30 = h.Blur(12f * px);
            bool[] play = new bool[N];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    if (!land[i] || h.D[i] - sea < 0.5f) continue;
                    int xm = Math.Max(0, x - 1), xp = Math.Min(W - 1, x + 1), ym = Math.Max(0, y - 1), yp = Math.Min(H - 1, y + 1);
                    float gx = (h30[xp, y] - h30[xm, y]) / ((xp - xm) * cell), gy = (h30[x, yp] - h30[x, ym]) / ((yp - ym) * cell);
                    play[i] = Math.Sqrt(gx * gx + gy * gy) < 0.10;
                }
            int[] pl = Label(delegate (int i) { return play[i]; }, W, H);
            float biggest = 0; foreach (KeyValuePair<int, int> e in Sizes(pl)) biggest = Math.Max(biggest, e.Value * cellHa);
            r[11] = biggest > 15;
            return r;
        }

        /// <summary>The features check: the share of the world type's list this map matches, passed at 0.8; 'missed' names
        /// the items it has that the map lacks (or the reverse, marked "extra").</summary>
        public static float Score(bool?[] want, bool[] got, out string missed)
        {
            int n = 0, ok = 0; missed = "";
            for (int i = 0; i < Names.Length; i++)
            {
                if (!want[i].HasValue) continue;
                n++;
                if (want[i].Value == got[i]) ok++;
                else missed += (missed.Length > 0 ? ", " : "") + (got[i] ? "extra " : "no ") + Names[i];
            }
            return n > 0 ? (float)ok / n : 1;
        }

        // ---------- helpers ----------

        internal delegate bool Mask(int i);

        private static int[] Label(Mask m, int W, int H)
        {
            int[] lab = new int[W * H]; int next = 0;
            Stack<int> st = new Stack<int>();
            for (int s = 0; s < lab.Length; s++)
            {
                if (lab[s] != 0 || !m(s)) continue;
                next++; lab[s] = next; st.Push(s);
                while (st.Count > 0)
                {
                    int i = st.Pop(), x = i % W, y = i / W;
                    if (x > 0 && lab[i - 1] == 0 && m(i - 1)) { lab[i - 1] = next; st.Push(i - 1); }
                    if (x < W - 1 && lab[i + 1] == 0 && m(i + 1)) { lab[i + 1] = next; st.Push(i + 1); }
                    if (y > 0 && lab[i - W] == 0 && m(i - W)) { lab[i - W] = next; st.Push(i - W); }
                    if (y < H - 1 && lab[i + W] == 0 && m(i + W)) { lab[i + W] = next; st.Push(i + W); }
                }
            }
            return lab;
        }

        private static Dictionary<int, int> Sizes(int[] lab)
        {
            Dictionary<int, int> s = new Dictionary<int, int>();
            foreach (int l in lab) { if (l == 0) continue; int v; s.TryGetValue(l, out v); s[l] = v + 1; }
            return s;
        }

        /// <summary>Metres to the nearest cell where 'm' holds (chamfer 3-4), 0 on such cells.</summary>
        internal static float[] Distance(Mask m, int W, int H, float cell)
        {
            float[] d = new float[W * H];
            for (int i = 0; i < d.Length; i++) d[i] = m(i) ? 0 : 1e9f;
            float d1 = cell, d2 = cell * 1.4142f;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x; float v = d[i];
                    if (x > 0) v = Math.Min(v, d[i - 1] + d1);
                    if (y > 0) { v = Math.Min(v, d[i - W] + d1); if (x > 0) v = Math.Min(v, d[i - W - 1] + d2); if (x < W - 1) v = Math.Min(v, d[i - W + 1] + d2); }
                    d[i] = v;
                }
            for (int y = H - 1; y >= 0; y--)
                for (int x = W - 1; x >= 0; x--)
                {
                    int i = y * W + x; float v = d[i];
                    if (x < W - 1) v = Math.Min(v, d[i + 1] + d1);
                    if (y < H - 1) { v = Math.Min(v, d[i + W] + d1); if (x < W - 1) v = Math.Min(v, d[i + W + 1] + d2); if (x > 0) v = Math.Min(v, d[i + W - 1] + d2); }
                    d[i] = v;
                }
            return d;
        }
    }
}
