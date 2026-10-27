using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CawPlus
{
    /// <summary>What a square of the World Builder's sketch holds.</summary>
    public enum SketchTile : byte { Grass = 0, Hills, Mountains, Forest, Lake, Sea, River, Road, Bridge, Town, Houses, Park, Beach }

    /// <summary>
    /// The World Builder's sketch (#173, the tester: "fill in squares ... and when you click create it transforms it into a world,
    /// smoothing out the landscape and placing things down"): N x N squares over the map, each one SketchTile. Build turns it
    /// into heights (each kind of ground has its own height and roughness; square edges are warped and blended so no square
    /// shows), rivers carved along the river squares, lakes (at sea level, or a raised pond when the land round them is
    /// high), and Plan turns it into the plan Generate Roads and Auto-Place read (town centre, houses, park, forest, and the
    /// drawn roads as main roads).
    /// </summary>
    internal sealed class WorldSketch
    {
        public const int DefaultN = 32;
        public readonly int N;
        public readonly byte[] T;
        public int Seed = 1;
        public float MountainHeight = 160;  // metres above the sea, the tallest peaks in a big mountain area
        public float HillHeight = 40;       // metres, hills
        public float[] Level;               // per square: metres the flat ground (grass, town, forest) stands above the sea; null = all low

        public static readonly string[] Names = { "Grass", "Hills", "Mountains", "Forest", "Lake", "Sea", "River", "Road", "Bridge", "Town centre", "Houses", "Park", "Beach" };

        public WorldSketch(int n) { N = n; T = new byte[n * n]; }

        public SketchTile this[int x, int y]
        {
            get { return (SketchTile)T[y * N + x]; }
            set { T[y * N + x] = (byte)value; }
        }

        public bool Any
        {
            get
            {
                foreach (byte b in T) if (b != 0) return true;
                if (Level != null) foreach (float l in Level) if (l >= 0.5f) return true;
                return false;
            }
        }

        /// <summary>Whether a square of this kind stands at its ground level (flat land: grass, town, houses, park, roads, forest).</summary>
        public static bool TakesLevel(SketchTile t)
        {
            Ground g = GroundOf(t);
            return g == Ground.Low || g == Ground.Town || g == Ground.Forest;
        }

        /// <summary>
        /// A sketch of an existing map (heights in metres, 'cell' metres apart, rows = map y): each square the kind of ground
        /// it mostly is: sea (water joined to the map edge), lake (other water), beach (low land by the sea), mountains, hills
        /// or grass by its height above the sea and how steep it is. Mountain and hill heights are set from the map.
        /// </summary>
        public static WorldSketch FromHeights(Grid h, float cell, float sea, int n)
        {
            WorldSketch s = new WorldSketch(n);
            float worldM = (h.W - 1) * cell, tileM = worldM / n;
            float[] mean = new float[n * n], wet = new float[n * n], steep = new float[n * n];
            int step = Math.Max(1, (int)(tileM / cell / 6));
            for (int ty = 0; ty < n; ty++)
                for (int tx = 0; tx < n; tx++)
                {
                    int x0 = (int)(tx * tileM / cell), x1 = Math.Min(h.W - 2, (int)((tx + 1) * tileM / cell)), y0 = (int)(ty * tileM / cell), y1 = Math.Min(h.H - 2, (int)((ty + 1) * tileM / cell));
                    double sum = 0; int cnt = 0, w = 0; float sl = 0;
                    for (int y = y0; y <= y1; y += step)
                        for (int x = x0; x <= x1; x += step)
                        {
                            float v = h[x, y]; sum += v; cnt++;
                            if (v < sea) w++;
                            float gx = (h[x + 1, y] - v) / cell, gy = (h[x, y + 1] - v) / cell;
                            sl += (float)Math.Sqrt(gx * gx + gy * gy);
                        }
                    int i = ty * n + tx;
                    mean[i] = cnt > 0 ? (float)(sum / cnt) - sea : 0; wet[i] = cnt > 0 ? (float)w / cnt : 0; steep[i] = cnt > 0 ? sl / cnt : 0;
                }
            // water joined to the map edge is sea, the rest lakes
            bool[] isWet = new bool[n * n], seaSq = new bool[n * n];
            for (int i = 0; i < isWet.Length; i++) isWet[i] = wet[i] > 0.5f;
            Stack<int> st = new Stack<int>();
            for (int i = 0; i < isWet.Length; i++) { int x = i % n, y = i / n; if (isWet[i] && (x == 0 || y == 0 || x == n - 1 || y == n - 1)) { seaSq[i] = true; st.Push(i); } }
            while (st.Count > 0)
            {
                int i = st.Pop(), x = i % n, y = i / n;
                int[] nb = { x > 0 ? i - 1 : -1, x < n - 1 ? i + 1 : -1, y > 0 ? i - n : -1, y < n - 1 ? i + n : -1 };
                foreach (int b in nb) if (b >= 0 && isWet[b] && !seaSq[b]) { seaSq[b] = true; st.Push(b); }
            }
            float top = 0;
            foreach (float v in mean) top = Math.Max(top, v);
            for (int i = 0; i < mean.Length; i++)
            {
                SketchTile t;
                if (isWet[i]) t = seaSq[i] ? SketchTile.Sea : SketchTile.Lake;
                else if (steep[i] < 0.12f) t = SketchTile.Grass; // flat ground at any height (its level is kept below)
                else if (mean[i] > Math.Max(60, top * 0.45f) || (mean[i] > 35 && steep[i] > 0.45f)) t = SketchTile.Mountains;
                else t = SketchTile.Hills;
                s.T[i] = (byte)t;
            }
            for (int i = 0; i < mean.Length; i++)
            {
                if (s.T[i] != (byte)SketchTile.Grass || mean[i] > 3) continue;
                int x = i % n, y = i / n;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && ny >= 0 && nx < n && ny < n && seaSq[ny * n + nx]) s.T[i] = (byte)SketchTile.Beach;
                    }
            }
            // flat ground keeps its height above the sea (less the few metres Build adds back)
            s.Level = new float[n * n];
            for (int i = 0; i < mean.Length; i++) s.Level[i] = Math.Max(0, mean[i] - 4);
            s.MountainHeight = Math.Max(30, Math.Min(500, top));
            List<float> hills = new List<float>();
            for (int i = 0; i < mean.Length; i++) if (s.T[i] == (byte)SketchTile.Hills) hills.Add(mean[i]);
            hills.Sort();
            s.HillHeight = hills.Count > 0 ? Math.Max(10, Math.Min(120, hills[hills.Count * 9 / 10] * 1.3f)) : 40;
            return s;
        }

        // ---------- saving ----------

        private const string Letters = "ghmflswrbtupe"; // one letter per SketchTile, in order

        public string ToText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "sketch {0} seed={1} mountains={2:0} hills={3:0}\n", N, Seed, MountainHeight, HillHeight);
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++) sb.Append(Letters[T[y * N + x]]);
                sb.Append('\n');
            }
            if (Level != null)
            {
                sb.Append("levels\n");
                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++) sb.Append(x > 0 ? "," : "").Append(((int)Math.Round(Level[y * N + x])).ToString(CultureInfo.InvariantCulture));
                    sb.Append('\n');
                }
            }
            return sb.ToString();
        }

        /// <summary>A sketch from ToText's text, or null if it isn't one.</summary>
        public static WorldSketch FromText(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string[] lines = text.Replace("\r", "").Split('\n');
            string[] head = lines[0].Split(' ');
            int n;
            if (head.Length < 2 || head[0] != "sketch" || !int.TryParse(head[1], out n) || n < 4 || n > 128 || lines.Length < n + 1) return null;
            WorldSketch s = new WorldSketch(n);
            for (int i = 2; i < head.Length; i++)
            {
                string[] kv = head[i].Split('=');
                float v;
                if (kv.Length != 2 || !float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) continue;
                if (kv[0] == "seed") s.Seed = (int)v;
                else if (kv[0] == "mountains") s.MountainHeight = Math.Max(30, Math.Min(700, v));
                else if (kv[0] == "hills") s.HillHeight = Math.Max(5, Math.Min(150, v));
            }
            for (int y = 0; y < n; y++)
            {
                string row = lines[y + 1];
                for (int x = 0; x < n && x < row.Length; x++)
                {
                    int k = Letters.IndexOf(row[x]);
                    s.T[y * n + x] = (byte)(k < 0 ? 0 : k);
                }
            }
            if (lines.Length >= 2 * n + 2 && lines[n + 1].Trim() == "levels")
            {
                s.Level = new float[n * n];
                for (int y = 0; y < n; y++)
                {
                    string[] v = lines[n + 2 + y].Split(',');
                    for (int x = 0; x < n && x < v.Length; x++)
                    {
                        float f;
                        if (float.TryParse(v[x], NumberStyles.Float, CultureInfo.InvariantCulture, out f)) s.Level[y * n + x] = Math.Max(0, Math.Min(700, f));
                    }
                }
            }
            return s;
        }

        // ---------- noise (a function of position in metres, so the 8 m grid and the full map agree) ----------

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        private static float Value(float x, float y, int seed)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed), c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
            return (a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy;
        }

        /// <summary>Fractal noise 0..1 at (x, y) metres, largest features 'size' metres.</summary>
        private static float Fbm(float x, float y, float size, int octaves, int seed)
        {
            float sum = 0, amp = 1, norm = 0, f = 1 / size;
            for (int o = 0; o < octaves; o++)
            {
                sum += Value(x * f, y * f, seed + o * 101) * amp;
                norm += amp; amp *= 0.5f; f *= 2.03f;
            }
            return sum / norm;
        }

        /// <summary>Ridged noise 0..1 (sharp crests), largest features 'size' metres.</summary>
        private static float Ridged(float x, float y, float size, int octaves, int seed)
        {
            float sum = 0, amp = 1, norm = 0, f = 1 / size;
            for (int o = 0; o < octaves; o++)
            {
                float v = 1 - Math.Abs(Value(x * f, y * f, seed + o * 131) * 2 - 1);
                sum += v * v * amp;
                norm += amp; amp *= 0.5f; f *= 2.07f;
            }
            return sum / norm;
        }

        // ---------- the ground classes the squares blend between ----------

        private enum Ground { Sea, Lake, Low, Town, Beach, Forest, Hills, Mountains }
        private const int Grounds = 8;

        private static Ground GroundOf(SketchTile t)
        {
            switch (t)
            {
                case SketchTile.Sea: return Ground.Sea;
                case SketchTile.Lake: return Ground.Lake;
                case SketchTile.Town: case SketchTile.Houses: case SketchTile.Park: case SketchTile.Road: return Ground.Town;
                case SketchTile.Beach: return Ground.Beach;
                case SketchTile.Forest: return Ground.Forest;
                case SketchTile.Hills: return Ground.Hills;
                case SketchTile.Mountains: return Ground.Mountains;
                default: return Ground.Low; // grass, river, bridge (the water comes from the river or what it crosses)
            }
        }

        /// <summary>The square under (x, y) metres after the edge wander (so square edges become irregular).</summary>
        private int TileAt(float x, float y, float tileM)
        {
            float wx = x + (Fbm(x, y, tileM * 1.6f, 3, Seed + 7) - 0.5f) * tileM * 0.9f;
            float wy = y + (Fbm(x, y, tileM * 1.6f, 3, Seed + 11) - 0.5f) * tileM * 0.9f;
            int tx = Grid.Clampi((int)Math.Floor(wx / tileM), 0, N - 1), ty = Grid.Clampi((int)Math.Floor(wy / tileM), 0, N - 1);
            return ty * N + tx;
        }

        // ---------- build ----------

        /// <summary>
        /// Heights for a 'size' x 'size' map of a 'worldSize' metre world with the sea at 'sea' metres (full = also the fine
        /// detail and erosion of the full map). The 8 m planning grid is worked out first; the full map is that grid
        /// interpolated, with detail, the river channels and erosion on top.
        /// </summary>
        public TerrainGen.Result Build(int size, float worldSize, float sea, bool full)
        {
            float tileM = worldSize / N;
            int cn = (int)(worldSize / TerrainGen.CoarseCell) + 1;
            float cc = worldSize / (cn - 1);
            TerrainGen.Result r = new TerrainGen.Result();

            // 1. which square every planning cell belongs to, and how deep it lies inside a mountain area (in squares)
            int[] tileOf = new int[cn * cn];
            for (int y = 0; y < cn; y++)
                for (int x = 0; x < cn; x++) tileOf[y * cn + x] = TileAt(x * cc, y * cc, tileM);
            float[] mountDepth = DepthInside(SketchTile.Mountains);
            float[] hillDepth = DepthInside(SketchTile.Hills);

            // 2. ground weights, blurred over ~half a square so the kinds of ground blend into each other
            Grid[] w = new Grid[Grounds];
            for (int k = 0; k < Grounds; k++) w[k] = new Grid(cn, cn);
            for (int i = 0; i < tileOf.Length; i++) w[(int)GroundOf((SketchTile)T[tileOf[i]])].D[i] = 1;
            float blur = Math.Max(24f, tileM * 0.42f) / cc;
            for (int k = 0; k < Grounds; k++) w[k] = w[k].Blur(k == (int)Ground.Mountains || k == (int)Ground.Hills ? blur * 1.3f : blur);

            // 3. lakes: at sea level, unless the land round them is high: then a raised pond just under its rim
            int[] lakeOf; List<int[]> lakes = Regions(SketchTile.Lake, out lakeOf);
            float[] lakeLevel = new float[lakes.Count];
            Grid c = new Grid(cn, cn);
            Heights(c, cc, tileOf, w, mountDepth, hillDepth, sea, tileM, null, null);
            for (int L = 0; L < lakes.Count; L++)
            {
                List<float> rim = new List<float>();
                for (int i = 0; i < c.D.Length; i++)
                {
                    float lw = w[(int)Ground.Lake].D[i];
                    if (lw > 0.08f && lw < 0.3f && lakeOf[tileOf[i]] == L) rim.Add(c.D[i]);
                }
                rim.Sort();
                float low = rim.Count > 0 ? rim[rim.Count / 20] : sea;
                lakeLevel[L] = low > sea + 6 ? low - 1.5f : sea;
            }
            Heights(c, cc, tileOf, w, mountDepth, hillDepth, sea, tileM, lakeOf, lakeLevel);
            for (int L = 0; L < lakes.Count; L++)
            {
                if (lakeLevel[L] <= sea) continue;
                double sx = 0, sy = 0; foreach (int t in lakes[L]) { sx += (t % N + 0.5) * tileM; sy += (t / N + 0.5) * tileM; }
                float cx = (float)(sx / lakes[L].Length), cy = (float)(sy / lakes[L].Length);
                // the pond floods out from its deepest point near the middle
                int best = -1; float lo = float.MaxValue;
                for (int y = 0; y < cn; y++)
                    for (int x = 0; x < cn; x++)
                    {
                        int i = y * cn + x;
                        if (lakeOf[tileOf[i]] != L || w[(int)Ground.Lake].D[i] < 0.7f) continue;
                        float d = (x * cc - cx) * (x * cc - cx) + (y * cc - cy) * (y * cc - cy);
                        if (c.D[i] + d * 1e-6f < lo) { lo = c.D[i] + d * 1e-6f; best = i; }
                    }
                if (best >= 0) r.Ponds.Add(new[] { (best % cn) * cc, (best / cn) * cc, (float)Math.Sqrt(lakes[L].Length) * tileM * 0.5f, lakeLevel[L] });
            }

            // 4. rivers along the river squares (and the bridges over them), carved to just under the sea
            r.Rivers = Lines(new[] { SketchTile.River }, new[] { SketchTile.Bridge }, worldSize, true);
            foreach (List<float[]> river in r.Rivers) IntoWater(river, c, cc, sea, tileM, lakeOf, lakeLevel);
            foreach (List<float[]> river in r.Rivers) Carve(c, cc, river, sea, tileM);
            Grid lakeW = w[(int)Ground.Lake];
            TerrainGen.InPond wetAt = delegate (float x, float z) { return lakeW.At(x / cc, z / cc) > 0.45f; };
            foreach (float[] q in r.Ponds) TerrainGen.FitPond(c, cc, q[0], q[1], q[2] * 1.6f + tileM, q[3], wetAt);
            r.Coarse = c;

            // 5. the full map: the planning grid interpolated, plus detail where the ground is rough
            Grid h;
            if (size == cn) h = c.Clone();
            else
            {
                h = new Grid(size, size);
                float k = (cn - 1f) / (size - 1f);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++) h[x, y] = TerrainGen.SampleCubic(c, x * k, y * k);
            }
            float cell = worldSize / (size - 1);
            if (full)
            {
                float k = (cn - 1f) / (size - 1f);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float mx = x * k, my = y * k, px = x * cell, py = y * cell;
                        float rough = 3.2f * w[(int)Ground.Mountains].At(mx, my) + 1.2f * w[(int)Ground.Hills].At(mx, my) + 0.7f * w[(int)Ground.Forest].At(mx, my)
                                    + 0.35f * w[(int)Ground.Low].At(mx, my) + 0.12f * w[(int)Ground.Town].At(mx, my);
                        if (h[x, y] > sea) h[x, y] += (Fbm(px, py, 36, 3, Seed + 51) - 0.5f) * 2 * rough;
                    }
                foreach (List<float[]> river in r.Rivers) Carve(h, cell, river, sea, tileM);
                TerrainGen.ThermalErosion(h, cell, 36, 3);
                GenSettings gs = new GenSettings { Seed = Seed, SeaLevel = sea, Erosion = 35 };
                TerrainGen.Gullies(h, cell, gs);
            }
            // raised lakes: natural banks, and no water leaking out down a gully (CAW floods every connected dip)
            foreach (float[] q in r.Ponds) TerrainGen.FitPond(h, cell, q[0], q[1], q[2] * 1.6f + tileM, q[3], wetAt);
            for (int i = 0; i < h.D.Length; i++) if (h.D[i] < 0) h.D[i] = 0;
            r.Heights = h; r.Cell = cell;

            // where the town belongs: the middle of the town-centre squares (else the houses)
            r.TownHint = Middle(SketchTile.Town, tileM) ?? Middle(SketchTile.Houses, tileM);
            return r;
        }

        private void Heights(Grid c, float cc, int[] tileOf, Grid[] w, float[] mountDepth, float[] hillDepth, float sea, float tileM, int[] lakeOf, float[] lakeLevel)
        {
            int cn = c.W;
            for (int y = 0; y < cn; y++)
                for (int x = 0; x < cn; x++)
                {
                    int i = y * cn + x;
                    float px = x * cc, py = y * cc;
                    // how deep inside a mountain or hill area this point lies (squares, interpolated)
                    float md = SampleTiles(mountDepth, px, py, tileM), hd = SampleTiles(hillDepth, px, py, tileM);
                    float lakeH = sea - 4;
                    if (lakeOf != null)
                    {
                        int L = lakeOf[tileOf[i]];
                        if (L < 0) L = NearestLake(lakeOf, px, py, tileM);
                        if (L >= 0) lakeH = lakeLevel[L] - 4;
                    }
                    float lv = Level != null ? SampleTiles(Level, px, py, tileM) : 0;
                    float[] H = new float[Grounds];
                    H[(int)Ground.Sea] = sea - 7 - 5 * Fbm(px, py, 400, 2, Seed + 3);
                    H[(int)Ground.Lake] = lakeH;
                    H[(int)Ground.Low] = sea + lv + 2.5f + 3.5f * Fbm(px, py, 160, 3, Seed + 13);
                    H[(int)Ground.Town] = sea + lv + 3.5f + 1.2f * Fbm(px, py, 220, 2, Seed + 17);
                    H[(int)Ground.Beach] = sea + 0.9f + 0.5f * Fbm(px, py, 90, 2, Seed + 19);
                    H[(int)Ground.Forest] = sea + lv + 5 + 10 * Fbm(px, py, 180, 3, Seed + 23);
                    float hillUp = Math.Min(1, 0.45f + hd * tileM / 64f * 0.3f);
                    H[(int)Ground.Hills] = sea + 6 + HillHeight * hillUp * (0.3f + 0.7f * Fbm(px, py, 240, 4, Seed + 29));
                    float mountUp = Math.Min(1, 0.3f + md * tileM / 64f * 0.28f);
                    H[(int)Ground.Mountains] = sea + 20 + MountainHeight * mountUp * (0.45f + 0.55f * Ridged(px, py, 520, 5, Seed + 31))
                                               + HillHeight * 0.4f * Fbm(px, py, 200, 3, Seed + 37);
                    float sum = 0, v = 0;
                    for (int k = 0; k < Grounds; k++) { float wk = w[k].D[i]; sum += wk; v += wk * H[k]; }
                    c.D[i] = sum > 1e-4f ? v / sum : sea + 3;
                }
        }

        private int NearestLake(int[] lakeOf, float px, float py, float tileM)
        {
            int tx = Grid.Clampi((int)(px / tileM), 0, N - 1), ty = Grid.Clampi((int)(py / tileM), 0, N - 1);
            for (int r = 1; r <= 2; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = tx + dx, y = ty + dy;
                        if (x >= 0 && y >= 0 && x < N && y < N && lakeOf[y * N + x] >= 0) return lakeOf[y * N + x];
                    }
            return -1;
        }

        /// <summary>Bilinear value of a per-square field at (px, py) metres (square centres at (i + 0.5) x tileM).</summary>
        private float SampleTiles(float[] f, float px, float py, float tileM)
        {
            float u = px / tileM - 0.5f, v = py / tileM - 0.5f;
            int x0 = Grid.Clampi((int)Math.Floor(u), 0, N - 1), y0 = Grid.Clampi((int)Math.Floor(v), 0, N - 1);
            int x1 = Math.Min(N - 1, x0 + 1), y1 = Math.Min(N - 1, y0 + 1);
            float fx = Math.Max(0, Math.Min(1, u - x0)), fy = Math.Max(0, Math.Min(1, v - y0));
            float a = f[y0 * N + x0] + (f[y0 * N + x1] - f[y0 * N + x0]) * fx, b = f[y1 * N + x0] + (f[y1 * N + x1] - f[y1 * N + x0]) * fx;
            return a + (b - a) * fy;
        }

        /// <summary>For each square of kind 't': how many squares in from the edge of its area (1 = an edge square); 0 elsewhere.</summary>
        private float[] DepthInside(SketchTile t)
        {
            float[] d = new float[N * N];
            for (int i = 0; i < d.Length; i++) d[i] = T[i] == (byte)t ? 1000 : 0;
            for (int pass = 0; pass < 2; pass++)
                for (int k = 0; k < d.Length; k++)
                {
                    int i = pass == 0 ? k : d.Length - 1 - k, x = i % N, y = i / N;
                    if (d[i] == 0) continue;
                    float m = d[i];
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            float step = dx != 0 && dy != 0 ? 1.4f : 1f;
                            if (nx < 0 || ny < 0 || nx >= N || ny >= N) { m = Math.Min(m, 2.5f); continue; } // the map edge: half in
                            m = Math.Min(m, d[ny * N + nx] + step);
                        }
                    d[i] = m;
                }
            return d;
        }

        /// <summary>Connected areas (4-neighbour) of kind 't'; regionOf[square] = its area's index or -1.</summary>
        private List<int[]> Regions(SketchTile t, out int[] regionOf)
        {
            regionOf = new int[N * N];
            for (int i = 0; i < regionOf.Length; i++) regionOf[i] = -1;
            List<int[]> list = new List<int[]>();
            Stack<int> st = new Stack<int>();
            for (int i0 = 0; i0 < T.Length; i0++)
            {
                if (T[i0] != (byte)t || regionOf[i0] >= 0) continue;
                List<int> comp = new List<int>();
                regionOf[i0] = list.Count; st.Push(i0);
                while (st.Count > 0)
                {
                    int a = st.Pop(); comp.Add(a);
                    int ax = a % N, ay = a / N;
                    int[] nb = { ax > 0 ? a - 1 : -1, ax < N - 1 ? a + 1 : -1, ay > 0 ? a - N : -1, ay < N - 1 ? a + N : -1 };
                    foreach (int b in nb)
                        if (b >= 0 && T[b] == (byte)t && regionOf[b] < 0) { regionOf[b] = list.Count; st.Push(b); }
                }
                list.Add(comp.ToArray());
            }
            return list;
        }

        private float[] Middle(SketchTile t, float tileM)
        {
            double sx = 0, sy = 0; int n = 0;
            for (int i = 0; i < T.Length; i++) if (T[i] == (byte)t) { sx += (i % N + 0.5) * tileM; sy += (i / N + 0.5) * tileM; n++; }
            return n == 0 ? null : new[] { (float)(sx / n), (float)(sy / n) };
        }

        /// <summary>
        /// Carves a river channel along 'river' (metres): bed just under the sea in the middle, banks easing out over
        /// ~22 m, so the water is CAW's sea water winding through the land.
        /// </summary>
        /// <summary>
        /// A river end in a sea or lake square carries straight on until the ground is under that water: the shore wanders
        /// and blurs up to about half a square past the square's edge, so the square's middle can still be land (the tester's
        /// 2026-10-26 test: rivers always ended in grass before the sea).
        /// </summary>
        private void IntoWater(List<float[]> river, Grid c, float cc, float sea, float tileM, int[] lakeOf, float[] lakeLevel)
        {
            if (river.Count < 2) return;
            for (int e = 0; e < 2; e++)
            {
                float[] end = e == 0 ? river[0] : river[river.Count - 1], prev = e == 0 ? river[Math.Min(3, river.Count - 1)] : river[Math.Max(0, river.Count - 4)];
                int tx = Grid.Clampi((int)(end[0] / tileM), 0, N - 1), ty = Grid.Clampi((int)(end[1] / tileM), 0, N - 1);
                SketchTile k = this[tx, ty];
                if (k != SketchTile.Sea && k != SketchTile.Lake) continue;
                float level = k == SketchTile.Lake && lakeOf != null && lakeOf[ty * N + tx] >= 0 ? lakeLevel[lakeOf[ty * N + tx]] : sea;
                float dx = end[0] - prev[0], dy = end[1] - prev[1], L = (float)Math.Sqrt(dx * dx + dy * dy);
                if (L < 1e-3f) continue;
                dx /= L; dy /= L;
                float worldSize = (c.W - 1) * cc;
                for (float s = 8; s <= Math.Max(160, tileM * 2.5f); s += 8)
                {
                    float x = end[0] + dx * s, y = end[1] + dy * s;
                    if (x < 0 || y < 0 || x > worldSize || y > worldSize) break;
                    float[] q = { x, y };
                    if (e == 0) river.Insert(0, q); else river.Add(q);
                    if (TerrainGen.Sample(c, x / cc, y / cc) < level - 1.5f) break;
                }
            }
        }

        private static void Carve(Grid g, float cell, List<float[]> river, float sea, float tileM)
        {
            float half0 = Math.Max(6, Math.Min(14, tileM * 0.16f)), bank = 22, along = 0;
            for (int s = 1; s < river.Count; s++)
            {
                along += (float)Math.Sqrt((river[s][0] - river[s - 1][0]) * (river[s][0] - river[s - 1][0]) + (river[s][1] - river[s - 1][1]) * (river[s][1] - river[s - 1][1]));
                float half = half0 * (0.75f + 0.5f * Fbm(along, 3.7f, 220, 2, 977));
                float ax = river[s - 1][0], ay = river[s - 1][1], bx = river[s][0], by = river[s][1];
                float reach = half + bank + cell;
                int x0 = Math.Max(0, (int)((Math.Min(ax, bx) - reach) / cell)), x1 = Math.Min(g.W - 1, (int)((Math.Max(ax, bx) + reach) / cell) + 1);
                int y0 = Math.Max(0, (int)((Math.Min(ay, by) - reach) / cell)), y1 = Math.Min(g.H - 1, (int)((Math.Max(ay, by) + reach) / cell) + 1);
                float dx = bx - ax, dy = by - ay, L2 = Math.Max(1e-4f, dx * dx + dy * dy);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x * cell, py = y * cell;
                        float t = Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / L2));
                        float qx = ax + dx * t - px, qy = ay + dy * t - py, d = (float)Math.Sqrt(qx * qx + qy * qy);
                        if (d > half + bank) continue;
                        float bed = sea - 2.2f * (1 - Math.Min(1, d / half) * Math.Min(1, d / half)) - 0.3f;
                        float u = Math.Max(0, (d - half) / bank), e = u * u * (3 - 2 * u);
                        float want = d <= half ? bed : sea + 0.8f + (g[x, y] - (sea + 0.8f)) * e; // banks start just above the water
                        if (want < g[x, y]) g[x, y] = want;
                    }
            }
        }

        // ---------- roads, rivers as lines ----------

        /// <summary>
        /// The lines through connected squares of the kinds 'main' (+ 'also', squares that join them, e.g. bridges), from
        /// square centre to square centre, ends at the map edge carried out to the edge, rounded with Chaikin. A square where
        /// three or more lines meet is a junction; each stretch between junctions and ends is one line.
        /// </summary>
        public List<List<float[]>> Lines(SketchTile[] main, SketchTile[] also, float worldSize, bool needMain)
        {
            return Lines(main, also, worldSize, needMain, Array.IndexOf(main, SketchTile.Road) >= 0);
        }

        /// <summary>As Lines; 'tidy' (roads): straight runs and 45-degree diagonals with corners of a standard radius, instead of
        /// the natural winding rivers get.</summary>
        public List<List<float[]>> Lines(SketchTile[] main, SketchTile[] also, float worldSize, bool needMain, bool tidy)
        {
            float tileM = worldSize / N;
            bool[] on = new bool[N * N];
            for (int i = 0; i < T.Length; i++)
                on[i] = Array.IndexOf(main, (SketchTile)T[i]) >= 0 || Array.IndexOf(also, (SketchTile)T[i]) >= 0;
            // a road painted straight across a river square crosses it on a bridge
            if (Array.IndexOf(main, SketchTile.Road) >= 0)
                for (int i = 0; i < T.Length; i++)
                {
                    if (T[i] != (byte)SketchTile.River) continue;
                    int x = i % N, y = i / N;
                    bool across = (IsRoad(x - 1, y) && IsRoad(x + 1, y)) || (IsRoad(x, y - 1) && IsRoad(x, y + 1)) ||
                                  (IsRoad(x - 1, y - 1) && IsRoad(x + 1, y + 1)) || (IsRoad(x + 1, y - 1) && IsRoad(x - 1, y + 1));
                    if (across) on[i] = true;
                }
            // a bridge belongs to rivers only next to a river square, to roads only next to a road square
            if (needMain)
                for (int i = 0; i < T.Length; i++)
                    if (on[i] && Array.IndexOf(main, (SketchTile)T[i]) < 0 && !NextTo(i, main)) on[i] = false;
            bool[] painted = (bool[])on.Clone();
            Thin(on);
            List<int>[] nb = new List<int>[N * N];
            for (int i = 0; i < T.Length; i++)
            {
                if (!on[i]) continue;
                nb[i] = new List<int>();
                int x = i % N, y = i / N;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= N || ny >= N || !on[ny * N + nx]) continue;
                        // diagonal steps only where the corner isn't already joined through a side neighbour
                        if (dx != 0 && dy != 0 && (on[y * N + nx] || on[ny * N + x])) continue;
                        nb[i].Add(ny * N + nx);
                    }
            }
            List<List<float[]>> lines = new List<List<float[]>>();
            Dictionary<long, bool> used = new Dictionary<long, bool>();
            Predicate<int> node = delegate (int i) { return nb[i].Count != 2; };
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < T.Length; i++)
                {
                    if (!on[i] || (pass == 0 && !node(i))) continue;
                    foreach (int first in nb[i])
                    {
                        long key = Edge(i, first);
                        if (used.ContainsKey(key)) continue;
                        List<int> chain = new List<int> { i };
                        int prev = i, cur = first;
                        used[key] = true;
                        while (true)
                        {
                            chain.Add(cur);
                            if (node(cur) || cur == i) break;
                            int next = nb[cur][0] == prev ? nb[cur][1] : nb[cur][0];
                            long k2 = Edge(cur, next);
                            if (used.ContainsKey(k2)) break;
                            used[k2] = true; prev = cur; cur = next;
                        }
                        lines.Add(ToLine(chain, tileM, worldSize, nb, tidy, painted));
                    }
                }
            // lone squares
            for (int i = 0; i < T.Length; i++)
                if (on[i] && nb[i].Count == 0 && Array.IndexOf(main, (SketchTile)T[i]) >= 0)
                {
                    float cx = (i % N + 0.5f) * tileM, cy = (i / N + 0.5f) * tileM;
                    lines.Add(new List<float[]> { new[] { cx - tileM * 0.45f, cy }, new[] { cx + tileM * 0.45f, cy } });
                }
            return lines;
        }

        /// <summary>
        /// Zhang-Suen thinning: a road or river painted several squares wide becomes its one-square centre line, so a thick
        /// stroke is still one road (not several side by side).
        /// </summary>
        private void Thin(bool[] on)
        {
            bool changed = true;
            List<int> del = new List<int>();
            while (changed)
            {
                changed = false;
                for (int step = 0; step < 2; step++)
                {
                    del.Clear();
                    for (int y = 0; y < N; y++)
                        for (int x = 0; x < N; x++)
                        {
                            if (!on[y * N + x]) continue;
                            // neighbours p2..p9 clockwise from north (outside the map counts as empty)
                            bool[] q = { At(on, x, y - 1), At(on, x + 1, y - 1), At(on, x + 1, y), At(on, x + 1, y + 1), At(on, x, y + 1), At(on, x - 1, y + 1), At(on, x - 1, y), At(on, x - 1, y - 1) };
                            int b = 0, a = 0;
                            for (int k = 0; k < 8; k++) { if (q[k]) b++; if (!q[k] && q[(k + 1) % 8]) a++; }
                            if (b < 2 || b > 6 || a != 1) continue;
                            if (step == 0 ? (q[0] && q[2] && q[4]) || (q[2] && q[4] && q[6]) : (q[0] && q[2] && q[6]) || (q[0] && q[4] && q[6])) continue;
                            del.Add(y * N + x);
                        }
                    foreach (int i in del) on[i] = false;
                    if (del.Count > 0) changed = true;
                }
            }
        }

        private bool IsRoad(int x, int y)
        {
            return x >= 0 && y >= 0 && x < N && y < N && (T[y * N + x] == (byte)SketchTile.Road || T[y * N + x] == (byte)SketchTile.Bridge);
        }

        private bool At(bool[] on, int x, int y) { return x >= 0 && y >= 0 && x < N && y < N && on[y * N + x]; }

        private bool NextTo(int i, SketchTile[] kinds)
        {
            int x = i % N, y = i / N;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((dx != 0 || dy != 0) && nx >= 0 && ny >= 0 && nx < N && ny < N && Array.IndexOf(kinds, (SketchTile)T[ny * N + nx]) >= 0) return true;
                }
            return false;
        }

        private static long Edge(int a, int b) { return a < b ? (long)a * 100000 + b : (long)b * 100000 + a; }

        private List<float[]> ToLine(List<int> chain, float tileM, float worldSize, List<int>[] nb, bool tidy, bool[] painted)
        {
            // thinning eats a wide stroke's ends back from where it was painted (the tester: a wide river never reached the
            // sea): a free end carries on through its own painted squares to the water or the map edge it was painted to
            if (chain.Count >= 2)
            {
                if (nb[chain[chain.Count - 1]].Count == 1) chain.AddRange(Reach(chain[chain.Count - 1], chain[Math.Max(0, chain.Count - 4)], painted, chain, !tidy));
                if (nb[chain[0]].Count == 1)
                {
                    List<int> head = Reach(chain[0], chain[Math.Min(chain.Count - 1, 3)], painted, chain, !tidy);
                    head.Reverse(); chain.InsertRange(0, head);
                }
            }
            List<float[]> p = new List<float[]>();
            foreach (int t in chain) p.Add(new[] { (t % N + 0.5f) * tileM, (t / N + 0.5f) * tileM });
            // a line ending on a square at the map edge runs on out to the edge
            for (int e = 0; e < 2; e++)
            {
                int t = e == 0 ? chain[0] : chain[chain.Count - 1];
                if (nb[t] != null && nb[t].Count != 1) continue;
                int x = t % N, y = t / N;
                float[] q = e == 0 ? p[0] : p[p.Count - 1];
                float[] o = null;
                if (x == 0) o = new[] { 0f, q[1] }; else if (x == N - 1) o = new[] { worldSize, q[1] };
                else if (y == 0) o = new[] { q[0], 0f }; else if (y == N - 1) o = new[] { q[0], worldSize };
                if (o == null) continue;
                if (e == 0) p.Insert(0, o); else p.Add(o);
            }
            if (!tidy)
                for (int e = 0; e < 2; e++)
                {
                    // a river end beside the sea or a lake runs on into it (the tester: the river didn't reach the sea)
                    int t = e == 0 ? chain[0] : chain[chain.Count - 1];
                    if (nb[t] != null && nb[t].Count != 1) continue;
                    int into = WaterBeside(t);
                    if (into < 0) continue;
                    float[] q = { (into % N + 0.5f) * tileM, (into / N + 0.5f) * tileM };
                    if (e == 0) p.Insert(0, q); else p.Add(q);
                }
            if (tidy) return Fillet(Straighten(p, tileM * 0.55f), RoadRadius);
            for (int k = 0; k < 3; k++) p = TerrainGen.Chaikin(p);
            return Wind(p, tileM, chain[0]);
        }

        /// <summary>A river line given natural bends: moved sideways by smooth noise along its length (up to ~0.3 of a
        /// square, at least 12 m), easing to nothing at its ends so it still meets the sea, a lake or the map edge.</summary>
        private List<float[]> Wind(List<float[]> p, float tileM, int salt)
        {
            List<float[]> q = new List<float[]>();
            float total = 0;
            for (int i = 1; i < p.Count; i++) total += (float)Math.Sqrt((p[i][0] - p[i - 1][0]) * (p[i][0] - p[i - 1][0]) + (p[i][1] - p[i - 1][1]) * (p[i][1] - p[i - 1][1]));
            if (total < 1) return p;
            float amp = Math.Max(12, tileM * 0.3f), along = 0;
            for (int i = 0; i < p.Count; i++)
            {
                if (i > 0) along += (float)Math.Sqrt((p[i][0] - p[i - 1][0]) * (p[i][0] - p[i - 1][0]) + (p[i][1] - p[i - 1][1]) * (p[i][1] - p[i - 1][1]));
                int a = Math.Max(0, i - 1), b = Math.Min(p.Count - 1, i + 1);
                float tx = p[b][0] - p[a][0], ty = p[b][1] - p[a][1], tl = (float)Math.Sqrt(tx * tx + ty * ty);
                if (tl < 1e-3f) { q.Add(p[i]); continue; }
                float ends = Math.Min(1, Math.Min(along, total - along) / Math.Max(1, tileM));
                float off = (Fbm(along, salt * 7.31f, 170, 3, Seed + 61) - 0.5f) * 2 * amp * ends;
                q.Add(new[] { p[i][0] - ty / tl * off, p[i][1] + tx / tl * off });
            }
            return q;
        }

        /// <summary>
        /// From a line's free end (square 'end', 'back' a few squares behind it): the shortest way on through painted
        /// squares, not back along the line, to a square beside the sea or a lake ('water') or at the map edge, at most 6
        /// squares. Empty if there is none (the end stays where it is).
        /// </summary>
        private List<int> Reach(int end, int back, bool[] painted, List<int> chain, bool water)
        {
            List<int> none = new List<int>();
            int ex = end % N, ey = end / N;
            float dx = ex - back % N, dy = ey - back / N;
            if (WaterBeside(end) >= 0 && water) return none;
            if (ex == 0 || ey == 0 || ex == N - 1 || ey == N - 1) return none;
            Dictionary<int, int> from = new Dictionary<int, int>();
            Dictionary<int, bool> inChain = new Dictionary<int, bool>();
            foreach (int t in chain) inChain[t] = true;
            Queue<int> q = new Queue<int>(); q.Enqueue(end); from[end] = -1;
            Dictionary<int, int> dist = new Dictionary<int, int>(); dist[end] = 0;
            while (q.Count > 0)
            {
                int c = q.Dequeue(), cx = c % N, cy = c / N;
                if (c != end)
                {
                    bool atEdge = cx == 0 || cy == 0 || cx == N - 1 || cy == N - 1;
                    if ((water && WaterBeside(c) >= 0) || atEdge)
                    {
                        List<int> path = new List<int>();
                        for (int k = c; k != end; k = from[k]) path.Add(k);
                        path.Reverse();
                        return path;
                    }
                }
                if (dist[c] >= 6) continue;
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int nx = cx + ox, ny = cy + oy, n = ny * N + nx;
                        if ((ox == 0 && oy == 0) || nx < 0 || ny < 0 || nx >= N || ny >= N || !painted[n] || from.ContainsKey(n) || inChain.ContainsKey(n)) continue;
                        if ((nx - ex) * dx + (ny - ey) * dy <= 0) continue; // onwards, not back beside the line
                        from[n] = c; dist[n] = dist[c] + 1; q.Enqueue(n);
                    }
            }
            return none;
        }

        /// <summary>A sea or lake square beside square t (sides first), or -1.</summary>
        private int WaterBeside(int t)
        {
            int x = t % N, y = t / N, diag = -1;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= N || ny >= N) continue;
                    byte k = T[ny * N + nx];
                    if (k != (byte)SketchTile.Sea && k != (byte)SketchTile.Lake) continue;
                    if (dx == 0 || dy == 0) return ny * N + nx;
                    diag = ny * N + nx;
                }
            return diag;
        }

        /// <summary>Corner radius of drawn roads, metres (the tester: "standard values to make it very neat and tidy").</summary>
        public const float RoadRadius = 40f;

        /// <summary>Douglas-Peucker: a staircase of squares becomes one straight (often 45-degree) run; corners stay.</summary>
        private static List<float[]> Straighten(List<float[]> p, float tol)
        {
            if (p.Count < 3) return p;
            bool[] keep = new bool[p.Count];
            keep[0] = keep[p.Count - 1] = true;
            Stack<int[]> st = new Stack<int[]>(); st.Push(new[] { 0, p.Count - 1 });
            while (st.Count > 0)
            {
                int[] ab = st.Pop(); int a = ab[0], b = ab[1];
                float dx = p[b][0] - p[a][0], dy = p[b][1] - p[a][1], L = (float)Math.Sqrt(dx * dx + dy * dy);
                int far = -1; float fd = tol;
                for (int i = a + 1; i < b; i++)
                {
                    float d = L < 1e-3f ? (float)Math.Sqrt((p[i][0] - p[a][0]) * (p[i][0] - p[a][0]) + (p[i][1] - p[a][1]) * (p[i][1] - p[a][1]))
                                        : Math.Abs((p[i][0] - p[a][0]) * dy - (p[i][1] - p[a][1]) * dx) / L;
                    if (d > fd) { fd = d; far = i; }
                }
                if (far < 0) continue;
                keep[far] = true; st.Push(new[] { a, far }); st.Push(new[] { far, b });
            }
            List<float[]> o = new List<float[]>();
            for (int i = 0; i < p.Count; i++) if (keep[i]) o.Add(p[i]);
            return o;
        }

        /// <summary>Each corner rounded with an arc of radius 'r' (smaller where the straight runs beside it are short),
        /// points every ~5 m; the straight runs between stay straight.</summary>
        private static List<float[]> Fillet(List<float[]> p, float r)
        {
            if (p.Count < 3) return p;
            List<float[]> o = new List<float[]> { p[0] };
            for (int i = 1; i < p.Count - 1; i++)
            {
                float[] a = p[i - 1], b = p[i], c = p[i + 1];
                float ux = a[0] - b[0], uy = a[1] - b[1], vx = c[0] - b[0], vy = c[1] - b[1];
                float lu = (float)Math.Sqrt(ux * ux + uy * uy), lv = (float)Math.Sqrt(vx * vx + vy * vy);
                if (lu < 1e-3f || lv < 1e-3f) continue;
                ux /= lu; uy /= lu; vx /= lv; vy /= lv;
                float cos = Math.Max(-1, Math.Min(1, ux * vx + uy * vy)), ang = (float)Math.Acos(cos); // angle between the two runs
                if (ang > Math.PI - 0.02) { o.Add(b); continue; } // straight on
                float half = ang / 2, tanHalf = (float)Math.Tan(half);
                float t = r / tanHalf;                                  // from the corner to where the arc starts
                float tmax = 0.45f * Math.Min(lu, lv) * (i == 1 || i == p.Count - 2 ? 1.8f : 1f);
                t = Math.Min(t, Math.Min(tmax, Math.Min(lu, lv) * 0.8f));
                float rr = t * tanHalf;
                float[] s0 = { b[0] + ux * t, b[1] + uy * t }, s1 = { b[0] + vx * t, b[1] + vy * t };
                // arc centre along the bisector
                float bx = ux + vx, by = uy + vy, bl = (float)Math.Sqrt(bx * bx + by * by);
                if (bl < 1e-4f) { o.Add(b); continue; }
                float dc = rr / (float)Math.Sin(half);
                float cx = b[0] + bx / bl * dc, cy = b[1] + by / bl * dc;
                double a0 = Math.Atan2(s0[1] - cy, s0[0] - cx), a1 = Math.Atan2(s1[1] - cy, s1[0] - cx);
                double da = a1 - a0;
                while (da > Math.PI) da -= 2 * Math.PI;
                while (da < -Math.PI) da += 2 * Math.PI;
                int steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(da) * rr / 5));
                for (int k = 0; k <= steps; k++)
                {
                    double th = a0 + da * k / steps;
                    o.Add(new[] { cx + rr * (float)Math.Cos(th), cy + rr * (float)Math.Sin(th) });
                }
            }
            o.Add(p[p.Count - 1]);
            return o;
        }

        // ---------- the plan for Generate Roads and Auto-Place ----------

        /// <summary>
        /// What Generate Roads will lay on this land: only the drawn roads (bridges where they cross water) and the lots along
        /// them in the town centre and houses squares, with the lot figures and bridges of World type 'w'
        /// (null: no lots, EA's EP7 bridges). Null if no road is drawn.
        /// </summary>
        public RoadNet PlanRoads(TerrainGen.Result r, float worldSize, float sea, WorldPreset w)
        {
            List<float> d = new List<float>();
            foreach (List<float[]> line in Lines(new[] { SketchTile.Road }, new[] { SketchTile.Bridge }, worldSize, true))
            {
                d.Add(line.Count);
                foreach (float[] p in line) { d.Add(p[0]); d.Add(p[1]); }
            }
            if (d.Count == 0) return null;
            RoadSettings rs = new RoadSettings();
            rs.Seed = Seed; rs.SeaLevel = sea; rs.Footpaths = false;
            rs.Plan = Plan(r.Coarse.W, worldSize).ToSettings();
            rs.DrawnRoads = d.ToArray();
            rs.CentreHint = r.TownHint;
            rs.BridgeSpans = Bridges.Spans(Bridges.ForWorld(w != null ? WorldPreset.Key(w.Name) : ""));
            if (w != null) { rs.Lots = w.LotFigures; rs.LotsLand = w.Terrain; rs.LotsWorldM = w.RoadTargets != null ? w.RoadTargets.WorldM : 0; }
            return RoadPlanner.Plan(RoadSettings.WithPonds(r.Coarse, TerrainGen.CoarseCell, sea, r.Ponds), rs);
        }

        // ---------- was this sketch applied to the land the world has now? ----------

        private const string AppliedFile = "builder_applied.txt";

        /// <summary>
        /// Notes in the world's folder that the World Builder put this land in (32 x 32 heights), so Generate Roads and
        /// Auto-Place follow the sketch only while the world still has that land (unsaved worlds share one folder, and
        /// Generate Terrain may replace the land later: the tester's 2026-10-26 test, Generate Roads kept the drawn roads).
        /// </summary>
        public static void MarkApplied(string worldDir, Grid h)
        {
            StringBuilder sb = new StringBuilder();
            foreach (float v in Samples(h)) sb.Append(v.ToString("F1", CultureInfo.InvariantCulture)).Append(' ');
            File.WriteAllText(Path.Combine(worldDir, AppliedFile), sb.ToString());
        }

        /// <summary>Forgets the note (the land was replaced by something else).</summary>
        public static void ClearApplied(string worldDir)
        {
            string p = Path.Combine(worldDir, AppliedFile);
            if (File.Exists(p)) File.Delete(p);
        }

        /// <summary>True if the World Builder's land is still the world's: 85% of the 32 x 32 heights within 4 m (roads,
        /// lot pads and hand sculpting change a few).</summary>
        public static bool AppliedTo(string worldDir, Grid h)
        {
            string p = Path.Combine(worldDir, AppliedFile);
            if (!File.Exists(p)) return false;
            string[] v = File.ReadAllText(p).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            float[] now = Samples(h);
            if (v.Length != now.Length) return false;
            int same = 0;
            for (int i = 0; i < now.Length; i++)
            {
                float f;
                if (float.TryParse(v[i], NumberStyles.Float, CultureInfo.InvariantCulture, out f) && Math.Abs(f - now[i]) <= 4) same++;
            }
            return same >= 0.85f * now.Length;
        }

        private static float[] Samples(Grid h)
        {
            float[] a = new float[32 * 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++) a[y * 32 + x] = h[(int)((long)x * (h.W - 1) / 31), (int)((long)y * (h.H - 1) / 31)];
            return a;
        }

        /// <summary>
        /// The plan on an n x n planning grid (#171 codes): town centre, houses, park and forest squares as painted (square
        /// edges wander as the land's do), and the road squares' lines as main roads. Each drawn road stretch is drawn
        /// stopping 16 m short of a junction, so Generate Roads routes every stretch (it routes a stroke end to end).
        /// </summary>
        public PlanMap Plan(int n, float worldSize)
        {
            PlanMap m = new PlanMap(n);
            float cell = worldSize / (n - 1), tileM = worldSize / N;
            // road and bridge squares count as the town or houses beside them (the lots along a road stand partly on its square)
            SketchTile[] eff = new SketchTile[N * N];
            for (int i = 0; i < T.Length; i++)
            {
                eff[i] = (SketchTile)T[i];
                if (eff[i] != SketchTile.Road && eff[i] != SketchTile.Bridge) continue;
                int x0 = i % N, y0 = i / N, town = 0, houses = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = x0 + dx, y = y0 + dy;
                        if (x < 0 || y < 0 || x >= N || y >= N) continue;
                        if (T[y * N + x] == (byte)SketchTile.Town) town++;
                        else if (T[y * N + x] == (byte)SketchTile.Houses) houses++;
                    }
                if (town > 0 && town >= houses) eff[i] = SketchTile.Town;
                else if (houses > 0) eff[i] = SketchTile.Houses;
            }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    PlanArea a = PlanArea.None;
                    switch (eff[TileAt(x * cell, y * cell, tileM)])
                    {
                        case SketchTile.Town: a = PlanArea.Centre; break;
                        case SketchTile.Houses: a = PlanArea.Houses; break;
                        case SketchTile.Park: a = PlanArea.Park; break;
                        case SketchTile.Forest: a = PlanArea.Forest; break;
                    }
                    m.Codes[y * n + x] = (byte)a;
                }
            foreach (List<float[]> line in Lines(new[] { SketchTile.Road }, new[] { SketchTile.Bridge }, worldSize, true))
            {
                List<float[]> p = Trim(line, 16);
                for (int s = 1; s < p.Count; s++)
                {
                    float len = (float)Math.Sqrt((p[s][0] - p[s - 1][0]) * (p[s][0] - p[s - 1][0]) + (p[s][1] - p[s - 1][1]) * (p[s][1] - p[s - 1][1]));
                    int steps = Math.Max(1, (int)(len / (cell * 0.5f)));
                    for (int k = 0; k <= steps; k++)
                    {
                        float t = (float)k / steps;
                        int cx = (int)Math.Round((p[s - 1][0] + (p[s][0] - p[s - 1][0]) * t) / cell), cy = (int)Math.Round((p[s - 1][1] + (p[s][1] - p[s - 1][1]) * t) / cell);
                        if (cx >= 0 && cy >= 0 && cx < n && cy < n) m.Codes[cy * n + cx] = (byte)PlanArea.MainRoad;
                    }
                }
            }
            return m;
        }

        /// <summary>The line with 'cut' metres taken off each end that isn't at the map edge.</summary>
        private static List<float[]> Trim(List<float[]> p, float cut)
        {
            List<float[]> q = new List<float[]>(p);
            for (int e = 0; e < 2 && q.Count > 2; e++)
            {
                if (e == 1) q.Reverse();
                float left = cut;
                while (q.Count > 2)
                {
                    float d = (float)Math.Sqrt((q[1][0] - q[0][0]) * (q[1][0] - q[0][0]) + (q[1][1] - q[0][1]) * (q[1][1] - q[0][1]));
                    if (d > left) { float t = left / d; q[0] = new[] { q[0][0] + (q[1][0] - q[0][0]) * t, q[0][1] + (q[1][1] - q[0][1]) * t }; break; }
                    left -= d; q.RemoveAt(0);
                }
                if (e == 1) q.Reverse();
            }
            return q;
        }
    }
}
