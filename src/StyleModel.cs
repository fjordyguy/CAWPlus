using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CawPlus
{
    /// <summary>Per-pixel terrain features shared by style learning and generation. Units are metres and degrees.</summary>
    public sealed class TerrainFeatures
    {
        public readonly int W, H, N;
        public readonly byte[] SlopeBin, HeightBin, CurvBin, RelBin;
        public readonly int[] SlopeCount, HeightCount, CurvCount, RelCount;

        public static readonly float[] SlopeEdges = { 3, 6, 10, 15, 20, 25, 30, 35, 40, 50 };               // 11 bins
        public static readonly float[] HeightEdges = { 0, 0.5f, 1, 2, 4, 8, 16, 32, 64 };                   // 10 bins (m above water)
        public static readonly float[] CurvEdges = { -2, -1, -0.5f, -0.2f, 0.2f, 0.5f, 1, 2 };            // 9 bins (+ = hollow)
        public static readonly float[] RelEdges = { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f }; // 10 bins (0 = lowest point)

        public TerrainFeatures(Grid h, float cell, float water, Grid waterOffset)
        {
            W = h.W; H = h.H; N = W * H;
            float px = 1f / Math.Max(cell, 1e-3f);
            // Slope of a lightly smoothed surface, so the value means the same at preview and full resolution.
            Grid hs = h.Blur(1.5f * px);
            Grid broad = h.Blur(12f * px), fine = h.Blur(2f * px);
            float lo, hi; h.MinMax(out lo, out hi);
            float range = Math.Max(hi - lo, 1e-3f);
            SlopeBin = new byte[N]; HeightBin = new byte[N]; CurvBin = new byte[N]; RelBin = new byte[N];
            SlopeCount = new int[SlopeEdges.Length + 1]; HeightCount = new int[HeightEdges.Length + 1];
            CurvCount = new int[CurvEdges.Length + 1]; RelCount = new int[RelEdges.Length + 1];
            for (int y = 0; y < H; y++)
            {
                int ym = Grid.Clampi(y - 1, 0, H - 1), yp = Grid.Clampi(y + 1, 0, H - 1);
                for (int x = 0; x < W; x++)
                {
                    int xm = Grid.Clampi(x - 1, 0, W - 1), xp = Grid.Clampi(x + 1, 0, W - 1);
                    float gx = (hs[xp, y] - hs[xm, y]) / ((xp - xm) * cell);
                    float gy = (hs[x, yp] - hs[x, ym]) / ((yp - ym) * cell);
                    float slope = (float)(Math.Atan(Math.Sqrt(gx * gx + gy * gy)) * 180.0 / Math.PI);
                    int i = y * W + x;
                    SlopeBin[i] = Bin(slope, SlopeEdges);
                    HeightBin[i] = Bin(h.D[i] - water - (waterOffset != null ? waterOffset.D[i] : 0), HeightEdges);
                    CurvBin[i] = Bin(broad.D[i] - fine.D[i], CurvEdges);
                    RelBin[i] = Bin((h.D[i] - lo) / range, RelEdges);
                    SlopeCount[SlopeBin[i]]++; HeightCount[HeightBin[i]]++; CurvCount[CurvBin[i]]++; RelCount[RelBin[i]]++;
                }
            }
        }

        private static byte Bin(float v, float[] edges)
        {
            int b = 0;
            while (b < edges.Length && v >= edges[b]) b++;
            return (byte)b;
        }
    }

    /// <summary>How one kind of ground (role) is distributed in a reference world.</summary>
    public sealed class RoleStyle
    {
        public float Cover;                         // mean visible share over the world
        public float[] Slope, Height, Curv, Rel;    // mean visible share per feature bin (NaN = too few pixels)
        public float PatchMeters = 24;              // typical patch size
        public float Softness = 0.3f;               // share of covered ground that is a partial blend (0 = hard edges)
        public List<string> Textures = new List<string>();
        public List<float> Shares = new List<float>();   // each texture's visible share of the world (EA styles; empty = unknown)
    }

    /// <summary>A learned painting style, e.g. from Sunset Valley. Saved as Documents\CAWPlus\Styles\&lt;name&gt;.style.</summary>
    public sealed class StyleProfile
    {
        public string Name = "";
        public string Source = "";
        public string BaseTexture = "";   // the world's base layer texture (EA styles), and how much of the world it shows
        public float BaseShare;
        public readonly Dictionary<PaintRole, RoleStyle> Roles = new Dictionary<PaintRole, RoleStyle>();

        public static string Dir { get { return Path.Combine(Log.Dir, "Styles"); } }

        public override string ToString() { return "Style: " + Name + (FromEA ? " (EA)" : ""); }

        private static readonly string[] NotGround = { "water", "asphalt", "cement", "cobble", "concrete", "carpet", "flagstone",
            "footpath", "glass", "ice", "linoleum", "wood", "marble", "masonry", "metal", "plastic", "road", "snow", "tile" };

        /// <summary>Which role a reference layer plays, from its CAW terrain type, then its name. Null = not natural ground.</summary>
        public static PaintRole? RoleOf(string terrainType, string name)
        {
            string t = (terrainType ?? "").ToLowerInvariant();
            if (t.StartsWith("rock")) return PaintRole.Rock;
            if (t.StartsWith("dirt") || t.StartsWith("mud") || t.StartsWith("mulch") || t.StartsWith("gravel")) return PaintRole.Dirt;
            if (t.StartsWith("sand")) return PaintRole.Shore;
            if (t.StartsWith("grass")) return PaintRole.Meadow;
            // Water and built surfaces (pond beds, paving, roads, snow...) are never natural ground, whatever the
            // texture is called (Barnacle Bay's "pond_rocks" is Water_Knees, "dirt_black_base" Asphalt01).
            foreach (string hard in NotGround) if (t.StartsWith(hard)) return null;
            // Untyped ("Default") layers go by name; paving and the like are not ground (Aurora Skies' Ground_Pavement).
            string n = (name ?? "").ToLowerInvariant();
            if (TextureAnalyzer.IsUnnatural(name)) return null;
            foreach (PaintRole r in PaintEngine.Roles) if (TextureLibrary.Matches(r, n)) return r;
            return null;
        }

        /// <summary>
        /// Learns a style from a world: 'layers' are the exported blend maps (index 1..n, CAW stacks higher indices on
        /// top), all resampled to the height grid's size.
        /// </summary>
        public static StyleProfile Learn(string name, string source, Grid h, float cell, float water, Grid waterOffset,
                                         IList<Grid> layers, IList<string> names, IList<string> types)
        {
            TerrainFeatures f = new TerrainFeatures(h, cell, water, waterOffset);
            int N = f.N;
            // Visible share of each layer: its opacity times whatever the layers above leave uncovered.
            Dictionary<PaintRole, Grid> share = new Dictionary<PaintRole, Grid>();
            StyleProfile p = new StyleProfile();
            p.Name = name; p.Source = source;
            float[] remaining = new float[N];
            for (int i = 0; i < N; i++) remaining[i] = 1;
            for (int li = layers.Count - 1; li >= 1; li--)
            {
                Grid a = layers[li];
                if (a == null) continue;
                PaintRole? role = RoleOf(types[li], names[li]);
                Grid s = null;
                if (role.HasValue)
                {
                    if (!share.TryGetValue(role.Value, out s)) { s = new Grid(f.W, f.H); share[role.Value] = s; }
                    RoleStyle rs;
                    if (!p.Roles.TryGetValue(role.Value, out rs)) { rs = new RoleStyle(); p.Roles[role.Value] = rs; }
                    rs.Textures.Add(names[li]);
                }
                for (int i = 0; i < N; i++)
                {
                    float c = a.D[i] * remaining[i];
                    if (s != null) s.D[i] = Math.Min(1, s.D[i] + c);
                    remaining[i] -= c;
                }
            }

            foreach (KeyValuePair<PaintRole, Grid> kv in share)
            {
                RoleStyle rs = p.Roles[kv.Key];
                Grid s = kv.Value;
                double sum = 0; int covered = 0, partial = 0;
                for (int i = 0; i < N; i++)
                {
                    float v = s.D[i]; sum += v;
                    if (v > 0.05f) { covered++; if (v < 0.9f) partial++; }
                }
                rs.Cover = (float)(sum / N);
                rs.Softness = covered > 0 ? (float)partial / covered : 0.3f;
                rs.Slope = BinMeans(s, f.SlopeBin, f.SlopeCount);
                rs.Height = BinMeans(s, f.HeightBin, f.HeightCount);
                rs.Curv = BinMeans(s, f.CurvBin, f.CurvCount);
                rs.Rel = BinMeans(s, f.RelBin, f.RelCount);
                rs.PatchMeters = PatchSize(s) * cell;
            }
            // A role whose layers are (almost) never painted says nothing about where it goes: leave it to the
            // built-in rules instead of painting none of it (Riverview has rock and sand layers it never uses).
            List<PaintRole> unused = new List<PaintRole>();
            foreach (KeyValuePair<PaintRole, RoleStyle> kv in p.Roles) if (kv.Value.Cover < 0.001f) unused.Add(kv.Key);
            foreach (PaintRole r in unused) p.Roles.Remove(r);
            return p;
        }

        private static float[] BinMeans(Grid s, byte[] bins, int[] counts)
        {
            double[] sum = new double[counts.Length];
            for (int i = 0; i < bins.Length; i++) sum[bins[i]] += s.D[i];
            float[] m = new float[counts.Length];
            for (int b = 0; b < m.Length; b++) m[b] = counts[b] >= 100 ? (float)(sum[b] / counts[b]) : float.NaN;
            return m;
        }

        /// <summary>Distance (pixels) at which the pattern's autocorrelation falls to 0.5, averaged over x and y.</summary>
        private static float PatchSize(Grid s)
        {
            double mean = 0; for (int i = 0; i < s.D.Length; i++) mean += s.D[i]; mean /= s.D.Length;
            double var0 = 0; for (int i = 0; i < s.D.Length; i++) { double d = s.D[i] - mean; var0 += d * d; }
            if (var0 < 1e-9) return 8;
            float total = 0;
            for (int axis = 0; axis < 2; axis++)
            {
                int lag = 1;
                for (; lag < 128; lag++)
                {
                    double acc = 0;
                    int dx = axis == 0 ? lag : 0, dy = axis == 1 ? lag : 0;
                    for (int y = 0; y + dy < s.H; y += 2)
                        for (int x = 0; x + dx < s.W; x += 2)
                            acc += (s[x, y] - mean) * (s[x + dx, y + dy] - mean);
                    acc *= 4; // stride-2 sampling
                    if (acc / var0 < 0.5) break;
                }
                total += lag;
            }
            return Math.Max(2, total / 2);
        }

        // ---------- save / load (plain text, invariant culture) ----------

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, ToText());
        }

        /// <summary>The style as the plain text Save writes (and Parse reads); CAWPlusHelper gets the style this way.</summary>
        public string ToText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("name=" + Name);
            sb.AppendLine("source=" + Source);
            if (BaseTexture.Length > 0) sb.AppendLine("base=" + BaseTexture + "|" + F(BaseShare));
            foreach (KeyValuePair<PaintRole, RoleStyle> kv in Roles)
            {
                RoleStyle r = kv.Value;
                sb.AppendLine("role=" + kv.Key);
                sb.AppendLine("cover=" + F(r.Cover));
                sb.AppendLine("patch=" + F(r.PatchMeters));
                sb.AppendLine("soft=" + F(r.Softness));
                sb.AppendLine("slope=" + Join(r.Slope));
                sb.AppendLine("height=" + Join(r.Height));
                sb.AppendLine("curv=" + Join(r.Curv));
                sb.AppendLine("rel=" + Join(r.Rel));
                sb.AppendLine("textures=" + string.Join("|", r.Textures.ToArray()));
                if (r.Shares.Count == r.Textures.Count && r.Shares.Count > 0)
                {
                    string[] sh = new string[r.Shares.Count];
                    for (int i = 0; i < sh.Length; i++) sh[i] = F(r.Shares[i]);
                    sb.AppendLine("shares=" + string.Join("|", sh));
                }
            }
            return sb.ToString();
        }

        public static StyleProfile Load(string path)
        {
            return Parse(Path.GetFileNameWithoutExtension(path), File.ReadAllLines(path));
        }

        public static StyleProfile Parse(string name, string[] lines)
        {
            StyleProfile p = new StyleProfile();
            p.Name = name;
            RoleStyle cur = null;
            foreach (string raw in lines)
            {
                int eq = raw.IndexOf('=');
                if (eq <= 0) continue;
                string k = raw.Substring(0, eq).Trim(), v = raw.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "name": p.Name = v; break;
                    case "source": p.Source = v; break;
                    case "role": cur = new RoleStyle(); p.Roles[(PaintRole)Enum.Parse(typeof(PaintRole), v)] = cur; break;
                    case "cover": cur.Cover = P(v); break;
                    case "patch": cur.PatchMeters = P(v); break;
                    case "soft": cur.Softness = P(v); break;
                    case "slope": cur.Slope = Split(v); break;
                    case "height": cur.Height = Split(v); break;
                    case "curv": cur.Curv = Split(v); break;
                    case "rel": cur.Rel = Split(v); break;
                    case "textures": cur.Textures = new List<string>(v.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)); break;
                    case "shares":
                        cur.Shares = new List<float>();
                        foreach (string t in v.Split('|')) cur.Shares.Add(P(t));
                        break;
                    case "base":
                        {
                            int bar = v.LastIndexOf('|');
                            p.BaseTexture = bar > 0 ? v.Substring(0, bar) : v;
                            if (bar > 0) p.BaseShare = P(v.Substring(bar + 1));
                        }
                        break;
                }
            }
            return p;
        }

        /// <summary>True for the styles learned offline from EA worlds' own paint (library/*.style, built in).</summary>
        public bool FromEA { get { return Source.StartsWith("EA world", StringComparison.OrdinalIgnoreCase); } }

        /// <summary>Built-in EA styles (library/*.style), then the user's own (Documents\CAWPlus\Styles), which
        /// replace a built-in one of the same name.</summary>
        public static List<StyleProfile> LoadAll()
        {
            List<StyleProfile> list = new List<StyleProfile>();
            foreach (KeyValuePair<string, string[]> kv in Embedded.Files(".style"))
            {
                try { list.Add(Parse(Path.GetFileNameWithoutExtension(kv.Key), kv.Value)); }
                catch (Exception ex) { Log.Error("Loading built-in style " + kv.Key + " failed", ex); }
            }
            if (Directory.Exists(Dir))
                foreach (string f in Directory.GetFiles(Dir, "*.style"))
                {
                    try
                    {
                        StyleProfile p = Load(f);
                        list.RemoveAll(delegate (StyleProfile q) { return string.Equals(q.Name, p.Name, StringComparison.OrdinalIgnoreCase); });
                        list.Add(p);
                    }
                    catch (Exception ex) { Log.Error("Loading style " + f + " failed", ex); }
                }
            list.Sort(delegate (StyleProfile a, StyleProfile b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            return list;
        }

        private static string F(float v) { return float.IsNaN(v) ? "nan" : v.ToString("R", CultureInfo.InvariantCulture); }
        private static float P(string s) { return s == "nan" ? float.NaN : float.Parse(s, CultureInfo.InvariantCulture); }
        private static string Join(float[] a) { string[] s = new string[a.Length]; for (int i = 0; i < a.Length; i++) s[i] = F(a[i]); return string.Join(",", s); }
        private static float[] Split(string v) { string[] s = v.Split(','); float[] a = new float[s.Length]; for (int i = 0; i < s.Length; i++) a[i] = P(s[i]); return a; }
    }

    /// <summary>
    /// Generates paint that follows a learned style, in several passes: generate, measure where each kind of ground
    /// ended up, compare with the reference world, correct, repeat.
    /// </summary>
    public sealed class StyledGenerator
    {
        public const int MaxPasses = 6;

        private readonly StyleProfile style;
        private readonly TerrainFeatures f;
        private readonly float cell; // metres per pixel
        private readonly Dictionary<PaintRole, Grid> noise = new Dictionary<PaintRole, Grid>();
        private readonly Dictionary<PaintRole, float[][]> gains = new Dictionary<PaintRole, float[][]>();
        public float Match;      // 0..100
        public int Passes;

        public StyledGenerator(StyleProfile style, Grid h, float cell, float water, Grid waterOffset, int seed, ICollection<PaintRole> roles)
        {
            this.style = style;
            this.cell = Math.Max(cell, 1e-3f);
            f = new TerrainFeatures(h, cell, water, waterOffset);
            float px = 1f / Math.Max(cell, 1e-3f);
            Random rng = new Random(seed);
            Grid shape = null;
            foreach (PaintRole r in PaintEngine.Roles)
            {
                if (!style.Roles.ContainsKey(r) || !roles.Contains(r)) { rng.Next(); continue; }
                float patch = Math.Max(4f, style.Roles[r].PatchMeters);
                Grid n = Grid.Fbm(h.W, h.H, patch * 1.5f * px, 4, new Random(rng.Next()));
                // fine detail inside the learned patches (5 m and 2.5 m), so edges are intricate, not smooth blobs;
                // equalised again, so coverage stays exact
                Grid fineN = Grid.Fbm(h.W, h.H, 5f * px, 3, new Random(rng.Next()));
                Grid edgeN = Grid.Fbm(h.W, h.H, 2.5f * px, 2, new Random(rng.Next()));
                Equalize(n); Equalize(fineN); Equalize(edgeN);
                for (int i = 0; i < n.D.Length; i++) n.D[i] = 0.7f * n.D[i] + 0.2f * fineN.D[i] + 0.1f * edgeN.D[i];
                // follow the land's small shapes (gullies, hollows, ridges): roles the style finds more in hollows are
                // drawn into them, roles found on ridges onto those (Bridgewood's and EA's paint follows every gully;
                // the tester's v0.40 test: Auto-Paint looked like blobs laid over the land)
                float pref = HollowPreference(style.Roles[r]);
                if (Math.Abs(pref) > 0.05f)
                {
                    if (shape == null) shape = FineShape(h, cell);
                    float w = Math.Min(ShapeWeight, ShapeWeight * Math.Abs(pref) * 2);
                    for (int i = 0; i < n.D.Length; i++)
                        n.D[i] = (1 - w) * n.D[i] + w * (pref > 0 ? 1 - shape.D[i] : shape.D[i]);
                }
                Equalize(n);
                noise[r] = n;
                gains[r] = new[] { Ones(TerrainFeatures.SlopeEdges.Length + 1), Ones(TerrainFeatures.HeightEdges.Length + 1),
                                   Ones(TerrainFeatures.CurvEdges.Length + 1) };
            }
        }

        /// <summary>Seeds this generator's corrections from an earlier fit (e.g. the preview), so full resolution starts close.</summary>
        public void CopyGainsFrom(StyledGenerator other)
        {
            if (other == null) return;
            foreach (KeyValuePair<PaintRole, float[][]> kv in other.gains)
                if (gains.ContainsKey(kv.Key))
                    for (int t = 0; t < 3; t++) Array.Copy(kv.Value[t], gains[kv.Key][t], kv.Value[t].Length);
        }

        /// <summary>The corrections as text (role, then the slope / height / hollow gains), to pass to CAWPlusHelper.</summary>
        public string GainsText()
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<PaintRole, float[][]> kv in gains)
            {
                sb.Append(kv.Key);
                foreach (float[] a in kv.Value)
                {
                    sb.Append(';');
                    for (int i = 0; i < a.Length; i++) { if (i > 0) sb.Append(','); sb.Append(a[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture)); }
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        public void SetGains(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (string line in text.Split('\n'))
            {
                string[] p = line.Split(';');
                if (p.Length != 4) continue;
                PaintRole r;
                try { r = (PaintRole)Enum.Parse(typeof(PaintRole), p[0]); } catch { continue; }
                float[][] g;
                if (!gains.TryGetValue(r, out g)) continue;
                for (int t = 0; t < 3; t++)
                {
                    string[] v = p[t + 1].Split(',');
                    if (v.Length != g[t].Length) continue;
                    for (int i = 0; i < v.Length; i++) g[t][i] = float.Parse(v[i], System.Globalization.CultureInfo.InvariantCulture);
                }
            }
        }

        private static float[] Ones(int n) { float[] a = new float[n]; for (int i = 0; i < n; i++) a[i] = 1; return a; }

        /// <summary>Histogram-equalise to a uniform 0..1 distribution, so "paint where noise &lt; p" covers share p.</summary>
        public const float ShapeWeight = 0.4f; // how strongly paint follows the land's small hollows and ridges (0-1)

        /// <summary>
        /// Hollows (1) to ridges (0) at a ~3 m scale, equalised: the small gullies and spurs a painter follows.
        /// </summary>
        private static Grid FineShape(Grid h, float cell)
        {
            Grid sm = h.Blur(1.5f / cell);
            int k = Math.Max(1, (int)Math.Round(3f / cell));
            Grid c = new Grid(h.W, h.H);
            for (int y = 0; y < h.H; y++)
                for (int x = 0; x < h.W; x++)
                {
                    int x0 = Math.Max(0, x - k), x1 = Math.Min(h.W - 1, x + k), y0 = Math.Max(0, y - k), y1 = Math.Min(h.H - 1, y + k);
                    c[x, y] = sm[x0, y] + sm[x1, y] + sm[x, y0] + sm[x, y1] - 4 * sm[x, y]; // + = hollow
                }
            // to 0..1 around the typical size (mean absolute value), then equalised
            double mad = 0; foreach (float v in c.D) mad += Math.Abs(v);
            mad = Math.Max(1e-6, mad / c.D.Length);
            for (int i = 0; i < c.D.Length; i++) c.D[i] = (float)Math.Max(0, Math.Min(0.9999, 0.5 + c.D[i] / (8 * mad)));
            Equalize(c);
            return c;
        }

        /// <summary>-1 (found on ridges) .. +1 (found in hollows), from the style's share per curvature bin.</summary>
        private static float HollowPreference(RoleStyle rs)
        {
            if (rs.Curv == null || rs.Curv.Length < 9) return 0;
            double hollow = 0, ridge = 0; int nh = 0, nr = 0;
            for (int b = 0; b < rs.Curv.Length; b++)
            {
                float v = rs.Curv[b];
                if (float.IsNaN(v)) continue;
                if (b >= 5) { hollow += v; nh++; } else if (b <= 3) { ridge += v; nr++; }
            }
            if (nh == 0 || nr == 0) return 0;
            double a = hollow / nh + 1e-4, c = ridge / nr + 1e-4;
            return (float)Math.Max(-1, Math.Min(1, Math.Log(a / c)));
        }

        private static void Equalize(Grid g)
        {
            const int B = 4096;
            int[] hist = new int[B];
            for (int i = 0; i < g.D.Length; i++) hist[Math.Min(B - 1, (int)(g.D[i] * B))]++;
            float[] cdf = new float[B];
            long acc = 0;
            for (int b = 0; b < B; b++) { acc += hist[b]; cdf[b] = (float)acc / g.D.Length; }
            for (int i = 0; i < g.D.Length; i++) g.D[i] = cdf[Math.Min(B - 1, (int)(g.D[i] * B))];
        }

        private static float Ratio(float[] curve, int bin, float cover)
        {
            if (curve == null || bin >= curve.Length || float.IsNaN(curve[bin]) || cover <= 1e-5f) return 1;
            return Math.Min(8f, curve[bin] / cover);
        }

        /// <summary>One generation with the current corrections. 'amount' = user slider per role (1 = as learned).</summary>
        public Dictionary<PaintRole, Grid> Generate(Dictionary<PaintRole, float> amount, float softness)
        {
            Dictionary<PaintRole, Grid> masks = new Dictionary<PaintRole, Grid>();
            foreach (PaintRole r in PaintEngine.Roles)
            {
                Grid m = new Grid(f.W, f.H);
                masks[r] = m;
                if (!noise.ContainsKey(r)) continue;
                RoleStyle rs = style.Roles[r];
                float[][] g = gains[r];
                float k = amount.ContainsKey(r) ? amount[r] : 1f;
                float w = Math.Max(0.04f, Math.Min(1f, rs.Softness * softness)); // width of the partial-blend band
                float invW = 1f / w;
                float[] lut = ShiftTable(w);
                Grid n = noise[r];
                // the wanted share per pixel comes from slope / height / hollow bins, so it jumps where the ground crosses
                // a bin edge; smoothed over ~6 m first, or the paint gets hard outlines along the terrain's contours
                // (the tester's v0.40 test: blocky edges even at full softness)
                // (worked out on a ~4 m grid and interpolated: it is smooth, and this runs every pass at full size)
                int st = Math.Max(1, (int)(4f / cell));
                int cw = (f.W - 1) / st + 1, ch = (f.H - 1) / st + 1;
                Grid pw = new Grid(cw, ch);
                for (int y = 0; y < ch; y++)
                    for (int x = 0; x < cw; x++)
                    {
                        int i = y * st * f.W + x * st;
                        int sb = f.SlopeBin[i], hb = f.HeightBin[i], cb = f.CurvBin[i];
                        float p = rs.Cover * Ratio(rs.Slope, sb, rs.Cover) * Ratio(rs.Height, hb, rs.Cover)
                                  * Ratio(rs.Curv, cb, rs.Cover) * Ratio(rs.Rel, f.RelBin[i], rs.Cover)
                                  * g[0][sb] * g[1][hb] * g[2][cb] * k;
                        pw[x, y] = p > 1 ? 1 : p;
                    }
                pw = pw.Blur(BinSmoothMeters / (cell * st));
                for (int y = 0; y < f.H; y++)
                    for (int x = 0; x < f.W; x++)
                    {
                        int i = y * f.W + x;
                        float pv = st == 1 ? pw.D[i] : pw.At((float)x / st, (float)y / st);
                        float v = (lut[(int)(pv * (LutSize - 1) + 0.5f)] - n.D[i]) * invW + 0.5f;
                        m.D[i] = v < 0 ? 0 : (v > 1 ? 1 : v);
                    }
                // feathered edges in metres (a brush's soft falloff), wider for soft styles and with the Softness slider;
                // a blur keeps the texture's total coverage
                float feather = FeatherMeters * softness * (0.4f + rs.Softness);
                if (feather / cell >= 0.5f) masks[r] = m.Blur(feather / cell);
            }
            return masks;
        }

        private const int LutSize = 1025;
        public const float BinSmoothMeters = 6f;  // smoothing of the wanted share (sigma, m)
        public const float FeatherMeters = 1.2f;  // edge feather at Softness 50 for a fully soft style (sigma, m)
        private static readonly Dictionary<int, float[]> lutCache = new Dictionary<int, float[]>();

        /// <summary>
        /// For blend width w, maps a wanted coverage p to the threshold t such that, with n uniform on 0..1,
        /// the mean of clamp((t - n)/w + 0.5) is exactly p. Without this, soft edges add paint where p = 0.
        /// </summary>
        private static float[] ShiftTable(float w)
        {
            int key = (int)(w * 1000);
            float[] lut;
            if (lutCache.TryGetValue(key, out lut)) return lut;
            // mean(t) is increasing in t; sample it on a grid of t, then invert.
            const int T = 2049;
            float tLo = -w / 2, tHi = 1 + w / 2;
            float[] ts = new float[T], means = new float[T];
            for (int j = 0; j < T; j++)
            {
                float t = tLo + (tHi - tLo) * j / (T - 1);
                ts[j] = t; means[j] = MeanCoverage(t, w);
            }
            lut = new float[LutSize];
            int k = 0;
            for (int i = 0; i < LutSize; i++)
            {
                float p = (float)i / (LutSize - 1);
                while (k < T - 2 && means[k + 1] < p) k++;
                float m0 = means[k], m1 = means[k + 1];
                float a = m1 > m0 ? (p - m0) / (m1 - m0) : 0;
                lut[i] = ts[k] + (ts[k + 1] - ts[k]) * Math.Max(0, Math.Min(1, a));
            }
            lutCache[key] = lut;
            return lut;
        }

        /// <summary>Exact integral over n in 0..1 of clamp((t - n)/w + 0.5, 0, 1).</summary>
        private static float MeanCoverage(float t, float w)
        {
            // fully painted for n < a, linear ramp for a..b, nothing above b
            double a = t - w / 2.0, b = t + w / 2.0;
            double full = Math.Max(0, Math.Min(1, a));
            double lo = Math.Max(0, a), hi = Math.Min(1, b);
            double ramp = 0;
            if (hi > lo)
            {
                // integral of (b - n)/w over [lo, hi]
                ramp = ((b - lo) * (b - lo) - (b - hi) * (b - hi)) / (2 * w);
            }
            return (float)(full + ramp);
        }

        /// <summary>
        /// Generates in passes, correcting each role's share per slope / height / hollow bin towards the reference
        /// world after each pass. Returns the final masks; Match is the similarity in percent.
        /// </summary>
        // how each role's overall share of the map is matched to the reference world's (the band shares alone give more
        // or less of a texture where the terrain's mix of slopes and heights differs): 0 = not, 1 = a global gain step
        // after the band corrections, 2 = band targets scaled by the measured cover each pass. tools/Fidelity, all 22 EA
        // styles, seeds 7-8: 0 = 83.0 %, 1 (step ^0.6) = 86.3 %, 2 (^0.5) = 84.6 %, 2 (^0.3) = 83.5 %.
        public int CoverMode = 1;
        public double CoverPower = 0.6;
        public bool MatchCover { get { return CoverMode != 0; } set { CoverMode = value ? 2 : 0; } }

        public Dictionary<PaintRole, Grid> Fit(Dictionary<PaintRole, float> amount, float softness, int passes, ProgressFn progress)
        {
            Dictionary<PaintRole, Grid> masks = null;
            // MatchCover: each role's band targets are scaled so that, on this terrain's own mix of slopes and heights,
            // the role covers as much of the map as in the reference world (the band shares alone give more or less of a
            // texture where the terrain differs). The scale follows the measured cover each pass; one goal for all
            // three corrections, so they don't pull against each other.
            Dictionary<PaintRole, float> coverScale = new Dictionary<PaintRole, float>();
            for (int pass = 1; pass <= passes; pass++)
            {
                if (progress != null) progress("Style pass " + pass + " of " + passes, 100 * (pass - 1) / passes);
                masks = Generate(amount, softness);
                Dictionary<PaintRole, Grid> vis = PaintEngine.VisibleShares(masks);
                Match = Score(vis, amount);
                Passes = pass;
                if (pass == passes || Match > 97f) break;
                foreach (PaintRole r in new List<PaintRole>(gains.Keys))
                {
                    RoleStyle rs = style.Roles[r];
                    float k = amount.ContainsKey(r) ? amount[r] : 1f, cs;
                    if (!coverScale.TryGetValue(r, out cs)) cs = 1f;
                    float coverRatio = 1;
                    if (CoverMode != 0 && rs.Cover > 0)
                    {
                        double sum = 0; foreach (float v in vis[r].D) sum += v;
                        float actual = (float)(sum / vis[r].D.Length), want = Math.Min(1f, rs.Cover * k);
                        coverRatio = (want + 0.005f) / (actual + 0.005f);
                    }
                    if (CoverMode == 2)
                    {
                        cs = Math.Max(0.5f, Math.Min(2f, cs * (float)Math.Pow(coverRatio, CoverPower)));
                        coverScale[r] = cs;
                        k *= cs;
                    }
                    Correct(gains[r][0], rs.Slope, vis[r], f.SlopeBin, f.SlopeCount, k);
                    Correct(gains[r][1], rs.Height, vis[r], f.HeightBin, f.HeightCount, k);
                    Correct(gains[r][2], rs.Curv, vis[r], f.CurvBin, f.CurvCount, k);
                    if (CoverMode == 1)
                    {
                        float g = (float)Math.Pow(coverRatio, CoverPower);
                        float[] a = gains[r][0];
                        for (int b = 0; b < a.Length; b++) a[b] = Math.Max(0.1f, Math.Min(10f, a[b] * g));
                    }
                }
            }
            return masks;
        }

        // Each pass corrects the same shortfall through three gain arrays (slope, height, hollow) that multiply, so the
        // combined step is ratio^(3 x StepPower). 0.7 each (ratio^2.1) overshot and oscillated when two large roles
        // compete (Starlight Shores: meadow 15% -> 44% -> 7% -> 78%); 0.3 each (ratio^0.9) settles.
        private const double StepPower = 0.3;


        private static void Correct(float[] gain, float[] target, Grid vis, byte[] bins, int[] counts, float k)
        {
            double[] sum = new double[counts.Length];
            for (int i = 0; i < bins.Length; i++) sum[bins[i]] += vis.D[i];
            for (int b = 0; b < gain.Length; b++)
            {
                if (counts[b] < 50 || target == null || b >= target.Length || float.IsNaN(target[b])) continue;
                float actual = (float)(sum[b] / counts[b]), want = Math.Min(1f, target[b] * k);
                float ratio = (want + 0.005f) / (actual + 0.005f);
                gain[b] = Math.Max(0.1f, Math.Min(10f, gain[b] * (float)Math.Pow(ratio, StepPower)));
            }
        }

        /// <summary>100 = every role's share in every slope and height band equals the reference world's.</summary>
        private float Score(Dictionary<PaintRole, Grid> vis, Dictionary<PaintRole, float> amount)
        {
            double err = 0, total = 0;
            foreach (PaintRole r in noise.Keys)
            {
                RoleStyle rs = style.Roles[r];
                float k = amount.ContainsKey(r) ? amount[r] : 1f;
                Accumulate(rs.Slope, vis[r], f.SlopeBin, f.SlopeCount, k, ref err, ref total);
                Accumulate(rs.Height, vis[r], f.HeightBin, f.HeightCount, k, ref err, ref total);
            }
            return total <= 0 ? 0 : (float)(100 * Math.Max(0, 1 - err / total));
        }

        private static void Accumulate(float[] target, Grid vis, byte[] bins, int[] counts, float k, ref double err, ref double total)
        {
            if (target == null) return;
            double[] sum = new double[counts.Length];
            for (int i = 0; i < bins.Length; i++) sum[bins[i]] += vis.D[i];
            for (int b = 0; b < counts.Length && b < target.Length; b++)
            {
                if (counts[b] < 50 || float.IsNaN(target[b])) continue;
                double actual = sum[b] / counts[b], want = Math.Min(1.0, target[b] * k);
                err += counts[b] * Math.Abs(actual - want);
                total += counts[b] * Math.Max(actual, want);
            }
        }
    }
}
