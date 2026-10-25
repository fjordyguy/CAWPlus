using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CawPlus
{
    /// <summary>One kind of object a reference world uses, and the ground it stands on.</summary>
    internal sealed class RefObject
    {
        public string Name;
        public PlaceKind Kind;
        public int Count;
        public string Instance = ""; // catalog key instance (hex), when known
        public string Layer = "";    // the World Layer the reference world keeps it in
        // share of this object's instances per terrain band (sums to 1 per array)
        public float[] Slope = new float[PlacementEngine.SlopeBands.Length + 1];
        public float[] Rel = new float[PlacementEngine.RelBands.Length + 1];
        public float[] Water = new float[PlacementEngine.WaterBands.Length + 1];
        public bool HasBands;        // the shares above were measured (learned in CAW, or tools/placement.py)
    }

    /// <summary>How a reference world arranges one kind of object (tools/placement.py; NaN = not measured).</summary>
    internal sealed class KindShape
    {
        public float PerHa = float.NaN, NnMedian = float.NaN, Within15 = float.NaN, GroupMean = float.NaN, GroupP90 = float.NaN;
        public float RoadP10 = float.NaN, RoadP50 = float.NaN, RoadShare15 = float.NaN;
        public float[] Road;     // share of this kind per PlacementEngine.RoadBands band (null = not measured)
    }

    /// <summary>
    /// What a reference world (e.g. Sunset Valley, opened in CAW) has placed outside its lots: which trees, bushes and
    /// rocks, how many per hectare of land, and where. Saved as Documents\CAWPlus\Library\&lt;world&gt;.objects.
    /// Auto-Place uses it as an allow-list, for item weights and habitat, and as the density target.
    /// </summary>
    internal sealed class ObjectLibrary
    {
        public string Name = "";
        public float LandHectares;
        public readonly List<RefObject> Items = new List<RefObject>();
        // per hectare of land, for each PlaceKind
        public readonly float[] PerHectare = new float[PlaceSettings.Kinds];
        public readonly Dictionary<PlaceKind, float> Spacing = new Dictionary<PlaceKind, float>(); // median metres, from .props
        // land share per terrain band, to turn instance shares into "how much more likely here than average"
        public float[] LandSlope, LandRel, LandWater, LandRoad;
        // how each kind is arranged, and where the streetlights are (built-in worlds: tools/placement.py)
        public readonly Dictionary<PlaceKind, KindShape> Shape = new Dictionary<PlaceKind, KindShape>();
        public float LightSpacing = float.NaN, LightOffset = float.NaN, LitShare = float.NaN, LightEveryM = float.NaN;
        // a library made before forests were decoded (no "cluster tree" item: EA's forests are stored as cluster
        // transforms in the layer files, #66): its tree count is far too low to aim for, so the Trees sliders decide
        public bool TreeCountIncomplete;
        // road signs by role (tools/signrates.py): count, median metres from the centre line, median metres to a junction
        public float RoadKm = float.NaN;
        /// <summary>EA waterfall sites in this world (falls pieces within 60 m grouped) and its most used waterfall variant (1-3); from falls= (#158).</summary>
        public int FallSites = -1, FallVariant = 1;
        public readonly Dictionary<PlacementEngine.SignRole, float[]> SignStats = new Dictionary<PlacementEngine.SignRole, float[]>();

        /// <summary>
        /// Metres of lit road per streetlight (the world's lights over the length of road it lights), NaN if not measured.
        /// Not the nearest-light distance: where EA stands lights in opposite pairs (Riverview) that is just the road's width.
        /// </summary>
        public float LightEvery()
        {
            if (!float.IsNaN(LightEveryM) && LightEveryM > 0) return LightEveryM;
            KindShape sh = ShapeOf(PlaceKind.Streetlight);
            if (sh == null || float.IsNaN(sh.PerHa) || sh.PerHa <= 0 || LandHectares <= 0 || float.IsNaN(RoadKm) || RoadKm <= 0) return float.NaN;
            float lit = float.IsNaN(LitShare) ? 1f : Math.Max(0.05f, LitShare);
            return RoadKm * lit * 1000f / (sh.PerHa * LandHectares);
        }

        /// <summary>Share of streetlights with another within 15 m: lights standing in opposite pairs (0 if not measured).</summary>
        public float LightPairs()
        {
            KindShape sh = ShapeOf(PlaceKind.Streetlight);
            return sh == null || float.IsNaN(sh.Within15) ? 0 : Math.Max(0, Math.Min(1, sh.Within15));
        }

        /// <summary>The world's signs of this role per km of road (NaN = not measured).</summary>
        public float SignsPerKm(PlacementEngine.SignRole r)
        {
            if (float.IsNaN(RoadKm) || RoadKm <= 0 || SignStats.Count == 0) return float.NaN;
            float[] v; return SignStats.TryGetValue(r, out v) ? v[0] / RoadKm : 0;
        }

        /// <summary>
        /// The kind as a whole (all its items' measured bands, by count), for where to put it at all; null when the
        /// world has fewer than 10 measured examples or no land bands.
        /// </summary>
        public RefObject KindHabitat(PlaceKind k)
        {
            if (LandSlope == null) return null;
            RefObject h = new RefObject(); h.Kind = k; h.HasBands = true;
            foreach (RefObject r in Items)
            {
                if (r.Kind != k || !r.HasBands) continue;
                h.Count += r.Count;
                Add(h.Slope, r.Slope, r.Count); Add(h.Rel, r.Rel, r.Count); Add(h.Water, r.Water, r.Count);
            }
            if (h.Count < 10) return null;
            Normalize(h.Slope); Normalize(h.Rel); Normalize(h.Water);
            return h;
        }

        private static void Add(float[] to, float[] a, int w) { for (int i = 0; i < to.Length && i < a.Length; i++) to[i] += a[i] * w; }

        /// <summary>True if the world has any object of this kind.</summary>
        public bool Uses(PlaceKind k)
        {
            foreach (RefObject r in Items) if (r.Kind == k && r.Count > 0) return true;
            return false;
        }

        public KindShape ShapeOf(PlaceKind k) { KindShape sh; return Shape.TryGetValue(k, out sh) ? sh : null; }

        public static string Dir { get { return Path.Combine(Log.Dir, "Library"); } }
        public override string ToString() { return "Reference: " + Name; }

        public static ObjectLibrary Learn(string name, PlacementEngine.Maps m, float cell, float scale,
                                          List<object[]> objects, float waterLevel, Grid h, Grid waterOffset)
        {
            ObjectLibrary lib = new ObjectLibrary();
            lib.Name = name;
            int W = m.Slope.W, H = m.Slope.H;
            // land = not under water
            int land = 0;
            lib.LandSlope = new float[PlacementEngine.SlopeBands.Length + 1];
            lib.LandRel = new float[PlacementEngine.RelBands.Length + 1];
            lib.LandWater = new float[PlacementEngine.WaterBands.Length + 1];
            for (int i = 0; i < W * H; i++)
            {
                float wl = waterLevel + (waterOffset != null ? waterOffset.D[i] : 0);
                if (h.D[i] < wl) continue;
                land++;
                lib.LandSlope[PlacementEngine.Band(m.Slope.D[i], PlacementEngine.SlopeBands)]++;
                lib.LandRel[PlacementEngine.Band(m.Rel.D[i], PlacementEngine.RelBands)]++;
                lib.LandWater[PlacementEngine.Band(m.NearWater.D[i], PlacementEngine.WaterBands)]++;
            }
            Normalize(lib.LandSlope); Normalize(lib.LandRel); Normalize(lib.LandWater);
            lib.LandHectares = land * cell * cell / 10000f;

            Dictionary<string, RefObject> byName = new Dictionary<string, RefObject>(StringComparer.OrdinalIgnoreCase);
            foreach (object[] o in objects)
            {
                string n = (string)o[0];
                if (string.IsNullOrEmpty(n)) continue;
                Caw.CatalogItem c = new Caw.CatalogItem(); c.Name = n; c.Path = "";
                PlaceKind kind = PlacementEngine.Classify(c).Kind;
                if (kind == PlaceKind.Other) continue;
                // o[1], o[2] are map coordinates here; sample the working grid
                int x = Math.Min(W - 1, Math.Max(0, (int)((float)o[1] / scale))), y = Math.Min(H - 1, Math.Max(0, (int)((float)o[2] / scale)));
                int i = y * W + x;
                RefObject r;
                if (!byName.TryGetValue(n, out r)) { r = new RefObject(); r.Name = n; r.Kind = kind; byName[n] = r; lib.Items.Add(r); }
                r.Count++;
                r.Slope[PlacementEngine.Band(m.Slope.D[i], PlacementEngine.SlopeBands)]++;
                r.Rel[PlacementEngine.Band(m.Rel.D[i], PlacementEngine.RelBands)]++;
                r.Water[PlacementEngine.Band(m.NearWater.D[i], PlacementEngine.WaterBands)]++;
                lib.PerHectare[(int)kind]++;
            }
            foreach (RefObject r in lib.Items) { Normalize(r.Slope); Normalize(r.Rel); Normalize(r.Water); r.HasBands = true; }
            for (int k = 0; k < lib.PerHectare.Length; k++) lib.PerHectare[k] = lib.LandHectares > 0 ? lib.PerHectare[k] / lib.LandHectares : 0;
            lib.Items.Sort(delegate (RefObject a, RefObject b) { return b.Count.CompareTo(a.Count); });
            return lib;
        }

        private static void Normalize(float[] a)
        {
            float s = 0; foreach (float v in a) s += v;
            if (s > 0) for (int i = 0; i < a.Length; i++) a[i] /= s;
        }

        /// <summary>How much more (or less) likely this object is on this spot than on average land.</summary>
        public float Preference(RefObject r, int slopeBand, int relBand, int waterBand)
        {
            return Ratio(r.Slope, LandSlope, slopeBand) * Ratio(r.Rel, LandRel, relBand) * Ratio(r.Water, LandWater, waterBand);
        }

        /// <summary>How much more likely a kind is at this distance from a road than land on average is (1 = not measured).</summary>
        public float RoadPreference(KindShape sh, int roadBand)
        {
            return sh == null || sh.Road == null ? 1 : Ratio(sh.Road, LandRoad, roadBand);
        }

        private static float Ratio(float[] obj, float[] land, int b)
        {
            if (land == null || b >= land.Length || land[b] < 1e-4f) return 1;
            // a little smoothing so bands with no examples aren't forbidden outright
            return Math.Min(6f, (obj[b] + 0.02f) / (land[b] + 0.02f));
        }

        /// <summary>By catalog key instance first (exact), then by name.</summary>
        public RefObject Find(string catalogName, string instanceHex)
        {
            if (!string.IsNullOrEmpty(instanceHex))
                foreach (RefObject r in Items)
                    if (r.Instance.Length > 0 && string.Equals(r.Instance.TrimStart('0'), instanceHex.TrimStart('0'), StringComparison.OrdinalIgnoreCase)) return r;
            string key = Bracket(catalogName);
            foreach (RefObject r in Items)
            {
                if (string.Equals(r.Name, catalogName, StringComparison.OrdinalIgnoreCase)) return r;
                if (key != null && string.Equals(Bracket(r.Name), key, StringComparison.OrdinalIgnoreCase)) return r;
            }
            return null;
        }

        private static string Bracket(string s)
        {
            int a = s.LastIndexOf('('), b = s.LastIndexOf(')');
            if (a < 0 || b <= a) return null;
            string k = s.Substring(a + 1, b - a - 1);
            int u = k.LastIndexOf('_'); // "trollEP7)_132" style instance suffixes
            return u > 0 && u > k.Length - 5 ? k.Substring(0, u) : k;
        }

        // ---------- save / load ----------

        public void Save(string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("name=" + Name);
            sb.AppendLine("land_ha=" + F(LandHectares));
            sb.AppendLine("per_ha=" + Join(PerHectare));
            sb.AppendLine("land_slope=" + Join(LandSlope));
            sb.AppendLine("land_rel=" + Join(LandRel));
            sb.AppendLine("land_water=" + Join(LandWater));
            foreach (RefObject r in Items)
                sb.AppendLine("item=" + r.Kind + "|" + r.Count + "|" + Join(r.Slope) + "|" + Join(r.Rel) + "|" + Join(r.Water) + "|" + r.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString());
        }

        public static ObjectLibrary Load(string path)
        {
            ObjectLibrary lib = new ObjectLibrary();
            lib.Name = Path.GetFileNameWithoutExtension(path);
            foreach (string raw in File.ReadAllLines(path))
            {
                int eq = raw.IndexOf('=');
                if (eq <= 0) continue;
                string k = raw.Substring(0, eq), v = raw.Substring(eq + 1);
                switch (k)
                {
                    case "name": lib.Name = v; break;
                    case "land_ha": lib.LandHectares = P(v); break;
                    case "per_ha": float[] ph = Split(v); Array.Copy(ph, lib.PerHectare, Math.Min(lib.PerHectare.Length, ph.Length)); break;
                    case "land_slope": lib.LandSlope = Split(v); break;
                    case "land_rel": lib.LandRel = Split(v); break;
                    case "land_water": lib.LandWater = Split(v); break;
                    case "item":
                        string[] p = v.Split(new[] { '|' }, 6);
                        if (p.Length < 6) break;
                        RefObject r = new RefObject();
                        r.Kind = (PlaceKind)Enum.Parse(typeof(PlaceKind), p[0]); r.Count = int.Parse(p[1], CultureInfo.InvariantCulture);
                        r.Slope = Split(p[2]); r.Rel = Split(p[3]); r.Water = Split(p[4]); r.Name = p[5]; r.HasBands = true;
                        lib.Items.Add(r);
                        break;
                }
            }
            return lib;
        }

        /// <summary>
        /// Reads a bundled reference world (library/*.props, made by tools/worldstats.py from a world's layer files):
        /// name=, area_ha=, layer=Kind|Layer|count|median spacing, item=Kind|Layer|count|instance hex|name; and, from
        /// tools/placement.py (the world's objects measured against its own heightmap and roads):
        /// land=slope|rel|water|road shares, bands=instance|slope|rel|water shares (per item),
        /// kind=Kind|per_ha|nn_median|share_within_15m|group_mean|group_p90|road_p10|road_p50|road_share_15m|road shares,
        /// lights=spacing|offset|lit_share|lit_junction_m|unlit_junction_m|metres of lit road per light. land= and kind= end with road-distance
        /// shares (PlacementEngine.RoadBands).
        /// Counts give the allow-list, weights and densities; bands give each item's habitat; kind and lights lines
        /// give the arrangement (road setback, groups, which roads are lit).
        /// </summary>
        /// <summary>Learned placement scores (#96): per kind ("Tree", "Bush", "Rock", "Rock0".."Rock3") and feature (slope,
        /// hollow, above_sea, water, road, path, lot, trees15) the band edges and the likelihood ratio per band.</summary>
        public Dictionary<string, Dictionary<string, float[][]>> Scores = new Dictionary<string, Dictionary<string, float[][]>>();

        /// <summary>The ratio for one feature value, 1 if the library has none.</summary>
        public float ScoreRatio(Dictionary<string, float[][]> kind, string feature, float value)
        {
            float[][] t;
            if (kind == null || float.IsNaN(value) || !kind.TryGetValue(feature, out t)) return 1;
            int b = 0; while (b < t[0].Length && value >= t[0][b]) b++;
            return b < t[1].Length ? t[1][b] : 1;
        }

        public static ObjectLibrary LoadProps(string path) { return LoadProps(Path.GetFileNameWithoutExtension(path), File.ReadAllLines(path)); }

        public static ObjectLibrary LoadProps(string name, string[] lines)
        {
            ObjectLibrary lib = new ObjectLibrary();
            lib.Name = name;
            lib.TreeCountIncomplete = true;
            float area = 0;
            float[] counts = new float[PlaceSettings.Kinds];
            Dictionary<string, float[][]> bands = new Dictionary<string, float[][]>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in lines)
            {
                int eq = raw.IndexOf('=');
                if (eq <= 0) continue;
                string k = raw.Substring(0, eq), v = raw.Substring(eq + 1);
                if (k == "land")
                {
                    string[] p = v.Split('|');
                    if (p.Length >= 3) { lib.LandSlope = Split(p[0]); lib.LandRel = Split(p[1]); lib.LandWater = Split(p[2]); }
                    if (p.Length >= 4) lib.LandRoad = Split(p[3]);
                    continue;
                }
                if (k == "bands")
                {
                    string[] p = v.Split('|');
                    if (p.Length >= 4) bands[p[0]] = new[] { Split(p[1]), Split(p[2]), Split(p[3]) };
                    continue;
                }
                if (k == "kind")
                {
                    string[] p = v.Split('|');
                    if (p.Length < 9) continue;
                    KindShape sh = new KindShape();
                    sh.PerHa = Pn(p[1]); sh.NnMedian = Pn(p[2]); sh.Within15 = Pn(p[3]); sh.GroupMean = Pn(p[4]); sh.GroupP90 = Pn(p[5]);
                    sh.RoadP10 = Pn(p[6]); sh.RoadP50 = Pn(p[7]); sh.RoadShare15 = Pn(p[8]);
                    if (p.Length >= 10 && p[9].Length > 0) sh.Road = Split(p[9]);
                    lib.Shape[(PlaceKind)Enum.Parse(typeof(PlaceKind), p[0])] = sh;
                    continue;
                }
                if (k == "roadkm") { lib.RoadKm = Pn(v); continue; }
                if (k == "falls")
                {
                    string[] p = v.Split('|'); int n, var;
                    if (p.Length >= 1 && int.TryParse(p[0], out n)) lib.FallSites = n;
                    if (p.Length >= 2 && int.TryParse(p[1], out var)) lib.FallVariant = var;
                    continue;
                }
                if (k == "score")
                {
                    // learned placement score (#96, tools/placescore.py): kind|feature|band edges|likelihood ratio per band
                    string[] p = v.Split('|');
                    if (p.Length < 4) continue;
                    Dictionary<string, float[][]> f;
                    if (!lib.Scores.TryGetValue(p[0], out f)) { f = new Dictionary<string, float[][]>(); lib.Scores[p[0]] = f; }
                    f[p[1]] = new[] { Split(p[2]), Split(p[3]) };
                    continue;
                }
                if (k == "sign")
                {
                    string[] p = v.Split('|');
                    if (p.Length < 4) continue;
                    PlacementEngine.SignRole role = PlacementEngine.RoleOfSign(p[0]);
                    float n = Pn(p[1]), off = Pn(p[2]), toJ = Pn(p[3]);
                    float[] st; if (!lib.SignStats.TryGetValue(role, out st)) lib.SignStats[role] = st = new float[] { 0, 0, 0 };
                    float n0 = st[0]; st[0] += n;
                    st[1] = (st[1] * n0 + (float.IsNaN(off) ? 6.3f : off) * n) / st[0];
                    st[2] = (st[2] * n0 + (float.IsNaN(toJ) ? 0 : toJ) * n) / st[0];
                    continue;
                }
                if (k == "lights")
                {
                    string[] p = v.Split('|');
                    if (p.Length >= 3) { lib.LightSpacing = Pn(p[0]); lib.LightOffset = Pn(p[1]); lib.LitShare = Pn(p[2]); }
                    if (p.Length >= 6) lib.LightEveryM = Pn(p[5]);
                    continue;
                }
                if (k == "name") lib.Name = v;
                else if (k == "area_ha") area = P(v);
                else if (k == "layer")
                {
                    string[] p = v.Split('|');
                    if (p.Length >= 4 && p[3].Length > 0) lib.Spacing[(PlaceKind)Enum.Parse(typeof(PlaceKind), p[0])] = P(p[3]);
                }
                else if (k == "item")
                {
                    string[] p = v.Split(new[] { '|' }, 5);
                    if (p.Length < 5) continue;
                    PlaceKind kind = (PlaceKind)Enum.Parse(typeof(PlaceKind), p[0]);
                    int count = int.Parse(p[2], CultureInfo.InvariantCulture);
                    counts[(int)kind] += count;
                    // the same object in several of the world's layers: one entry with the total count
                    RefObject r = null;
                    foreach (RefObject q in lib.Items) if (q.Kind == kind && q.Instance == p[3]) { r = q; break; }
                    if (r != null) { r.Count += count; continue; }
                    r = new RefObject();
                    r.Kind = kind; r.Layer = p[1];
                    r.Count = count; r.Instance = p[3]; r.Name = p[4].Length > 0 ? p[4] : p[3];
                    lib.Items.Add(r);
                }
            }
            lib.LandHectares = area;
            for (int i = 0; i < counts.Length; i++) lib.PerHectare[i] = area > 0 ? counts[i] / area : 0;
            // measured objects per hectare of land (the area_ha line is the extent of the objects, sea included)
            foreach (KeyValuePair<PlaceKind, KindShape> kv in lib.Shape)
                if (!float.IsNaN(kv.Value.PerHa)) lib.PerHectare[(int)kv.Key] = kv.Value.PerHa;
            foreach (RefObject r in lib.Items)
                if (r.Kind == PlaceKind.Tree && r.Instance == "0000000000000000") lib.TreeCountIncomplete = false;
            // a kind with only a handful of examples says little about how it's arranged: keep only its density
            foreach (KeyValuePair<PlaceKind, KindShape> kv in lib.Shape)
            {
                int n = 0;
                foreach (RefObject r in lib.Items) if (r.Kind == kv.Key) n += r.Count;
                if (n >= 10) continue;
                KindShape sh = kv.Value;
                sh.NnMedian = sh.Within15 = sh.GroupMean = sh.GroupP90 = sh.RoadP10 = sh.RoadP50 = sh.RoadShare15 = float.NaN;
                sh.Road = null;
            }
            if (lib.LandSlope != null)
                foreach (RefObject r in lib.Items)
                {
                    float[][] b;
                    if (!bands.TryGetValue(r.Instance, out b)) continue;
                    r.Slope = b[0]; r.Rel = b[1]; r.Water = b[2]; r.HasBands = true;
                }
            return lib;
        }

        /// <summary>Bundled reference worlds (Library folder next to CAWPlus.exe) and ones learned in CAW (Documents\CAWPlus\Library).</summary>
        private static bool loggedOnce;

        public static List<ObjectLibrary> LoadAll()
        {
            List<ObjectLibrary> list = new List<ObjectLibrary>();
            // built into the exe first; a Library folder next to it (or your own) replaces one of the same name
            foreach (KeyValuePair<string, string[]> e in Embedded.Files(".props"))
            {
                try { list.Add(LoadProps(Path.GetFileNameWithoutExtension(e.Key), e.Value)); }
                catch (Exception ex) { Log.Error("Loading built-in reference world " + e.Key + " failed", ex); }
            }
            int builtIn = list.Count;
            string bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Library");
            foreach (string dir in new[] { bundled, Dir })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string f in Directory.GetFiles(dir, "*.props"))
                {
                    try
                    {
                        ObjectLibrary l = LoadProps(f);
                        list.RemoveAll(delegate (ObjectLibrary q) { return string.Equals(q.Name, l.Name, StringComparison.OrdinalIgnoreCase); });
                        list.Add(l);
                    }
                    catch (Exception ex) { Log.Error("Loading reference world " + f + " failed", ex); }
                }
                foreach (string f in Directory.GetFiles(dir, "*.objects"))
                {
                    try { list.Add(Load(f)); } catch (Exception ex) { Log.Error("Loading library " + f + " failed", ex); }
                }
            }
            list.Sort(delegate (ObjectLibrary a, ObjectLibrary b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            if (!loggedOnce)
            {
                loggedOnce = true;
                List<string> names = new List<string>();
                foreach (ObjectLibrary l in list) names.Add(l.Name);
                Log.Info("Reference worlds: " + list.Count + " (" + builtIn + " built in; folder " + bundled + (Directory.Exists(bundled) ? "" : " not found") + "): " + string.Join(", ", names.ToArray()));
            }
            return list;
        }

        private static string F(float v) { return v.ToString("R", CultureInfo.InvariantCulture); }
        private static float P(string s) { return float.Parse(s, CultureInfo.InvariantCulture); }
        private static float Pn(string s) { return s.Length == 0 ? float.NaN : P(s); }
        private static string Join(float[] a) { string[] s = new string[a.Length]; for (int i = 0; i < a.Length; i++) s[i] = F(a[i]); return string.Join(",", s); }
        private static float[] Split(string v) { string[] s = v.Split(','); float[] a = new float[s.Length]; for (int i = 0; i < s.Length; i++) a[i] = P(s[i]); return a; }
    }
}
