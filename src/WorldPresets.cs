using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CawPlus
{
    /// <summary>
    /// "Like world" presets for Generate Terrain: terrain sliders plus town and road settings per Sims 3 world, from
    /// Library\worlds.txt (bundled) and Documents\CAWPlus\Library\worlds.txt (your own). "measured" presets were fitted to
    /// the world's heightmap; "approximate" ones are set from what the world is known for.
    /// </summary>
    internal sealed class WorldPreset
    {
        public string Name, Source, Note;
        public CoastType Coast;
        public float Mountains, Hills, Roughness, Erosion, FlatLand, Water, MaxHeight;
        public int Rivers, DowntownBlocks, Neighbourhoods, Lots, EdgeRoads;
        public float Variety;
        public float Steepness = 40; // Max steepness slider for roads (optional 18th field; hilly worlds allow steeper roads)
        public float FootpathKm = -1; // EA's footpath length (optional 19th field; -1 = not measured)
        public float ClimbCost = 12;  // road planner's climbing cost (optional 20th field; tuned by tools/Fidelity)
        public float RoadKm;              // EA's road length; side streets fill the town up to it (optional 24th field)
        public float DeadEndShare = 0.4f; // EA's capped dead ends / junctions (optional 25th field)
        public float BuildSlope = 10;     // % slope still buildable (optional 26th field; EA land under its roads, p75)
        public float RiverWidth = 1;      // x the built-in river width (optional 27th field, tuned)
        public int Ponds;                 // ponds above the sea (optional 28th field; EA's 0x707CA200 count)
        public float PondRadius = 50;     // their typical radius in m (optional 29th field)
        public float SeaShare = -1, LakeShare = -1; // EA's sea and sea-level lake share of the map, % (optional 30th, 31st fields; measured)
        public RoadTargets RoadTargets;              // EA's town measured for the road checks (optional 32nd field)
        public TownShape Town;
        public DowntownGrid Grid;                    // EA's downtown block grid and roads out of town (optional 39th field)
        public RoadProfile Profile;                  // EA's road design profile (optional 40th field)                       // EA's town shape: lot and road spread from the core, country roads (optional 38th field)
        public TerrainProfile Terrain;
        public LotTargets LotFigures;                // EA's lots: count, frontage, gaps, size mix (optional 34th-35th fields)
        public float[] PathTargets;                  // EA's footpaths: km, distance from roads p10/p50/p90 m, piece median m (optional 33rd field)               // EA's terrain and water (library/<key>_terrain.json), for the terrain checks
        public string Region = "";      // the real-world kind of place, for the road geography check (optional 37th field; src/RoadCharacter.cs)
        public RoadCharacter Character; // its targets (region + EA's measured library/<key>_roadgeo.json)
        public string Style = "";       // world type style: dissection, terraces, softness, town (optional 36th field; GenSettings.SetStyle)
        public float CoastCorner = -1;  // sea along one edge (0) .. into a corner (100); -1 any (optional 23rd field, tuned)
        public bool InlandLakes = true; // lakes in inland hollows (optional 22nd field, 1/0; from the world's measured lakes)
        public float[] HeightCurve;   // the world's land heights at 0, 10 ... 100 % of its land (optional 21st field, measured by tools/Fidelity)
        public override string ToString() { return Name + (Source == "measured" ? "" : "  (approx.)"); }

        /// <summary>The folder holding the Library folder: CAWPlus.exe's (CAWPlusHelper sets it to its parent folder).</summary>
        public static string BaseDir = AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>The preset with this name (as LoadAll gives it), or null; CAWPlusHelper finds the world type's targets this way.</summary>
        public static WorldPreset Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (WorldPreset w in LoadAll()) if (string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase)) return w;
            return null;
        }

        public static List<WorldPreset> LoadAll()
        {
            List<WorldPreset> list = new List<WorldPreset>();
            string bundled = Path.Combine(Path.Combine(BaseDir, "Library"), "worlds.txt");
            string own = Path.Combine(Path.Combine(Log.Dir, "Library"), "worlds.txt");
            List<string[]> sources = new List<string[]>();
            // built into the exe, then the Library folder next to it, then your own file (later lines win)
            foreach (KeyValuePair<string, string[]> e in Embedded.Files("worlds.txt")) sources.Add(e.Value);
            foreach (string f in new[] { bundled, own }) if (File.Exists(f)) sources.Add(File.ReadAllLines(f));
            foreach (string[] lines in sources)
            {
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    if (line.Split('|').Length < 17) { Log.Info("worlds.txt: skipped a line with too few fields: " + line); continue; }
                    try
                    {
                        WorldPreset w = Parse(line);
                        list.RemoveAll(delegate (WorldPreset q) { return string.Equals(q.Name, w.Name, StringComparison.OrdinalIgnoreCase); }); // your own line wins
                        list.Add(w);
                    }
                    catch (Exception ex) { Log.Error("worlds.txt: bad line: " + line, ex); }
                }
            }
            // EA's measured terrain and water for the terrain checks: built in, then the Library folders
            Dictionary<string, TerrainProfile> terrain = new Dictionary<string, TerrainProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string[]> e in Embedded.Files("_terrain.json")) terrain[e.Key] = TerrainProfile.Parse(string.Join("\n", e.Value));
            foreach (string dir in new[] { Path.GetDirectoryName(bundled), Path.GetDirectoryName(own) })
                if (Directory.Exists(dir))
                    foreach (string f in Directory.GetFiles(dir, "*_terrain.json"))
                        try { terrain[Path.GetFileName(f)] = TerrainProfile.Parse(File.ReadAllText(f)); } catch (Exception ex) { Log.Error("terrain profile " + f, ex); }
            // EA's measured road character (tools/roadgeo.py) centres the region's bands on the world's own figures
            Dictionary<string, string> geo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string[]> e in Embedded.Files("_roadgeo.json")) geo[e.Key] = string.Join("\n", e.Value);
            foreach (WorldPreset w in list)
            {
                string gj;
                if (w.Region.Length > 0 && geo.TryGetValue(Key(w.Name) + "_roadgeo.json", out gj)) w.Character = RoadCharacter.For(w.Region, gj);
            }
            foreach (KeyValuePair<string, TerrainProfile> e in terrain) e.Value.Key = e.Key;
            // the feature checklist of each measured world (tools/terrainreport.py)
            Dictionary<string, string> land = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string[]> e in Embedded.Files("_landform.json")) land[e.Key] = string.Join("\n", e.Value);
            foreach (string dir in new[] { Path.GetDirectoryName(bundled), Path.GetDirectoryName(own) })
                if (Directory.Exists(dir))
                    foreach (string f in Directory.GetFiles(dir, "*_landform.json"))
                        try { land[Path.GetFileName(f)] = File.ReadAllText(f); } catch (Exception ex) { Log.Error("landform " + f, ex); }
            foreach (KeyValuePair<string, string> e in land)
            {
                TerrainProfile t;
                if (terrain.TryGetValue(e.Key.Replace("_landform.json", "_terrain.json"), out t)) t.Want = LandChecklist.FromJson(e.Value);
            }
            foreach (WorldPreset w in list)
            {
                TerrainProfile t;
                if (terrain.TryGetValue(Key(w.Name) + "_terrain.json", out t)) w.Terrain = t;
            }
            // worlds whose lots weren't sent (Riverview, Moonlight Falls, Vermont): the typical EA world's lots
            List<LotTargets> known = new List<LotTargets>();
            foreach (WorldPreset w in list) if (w.LotFigures != null) known.Add(w.LotFigures);
            LotTargets typical = LotTargets.Typical(known);
            if (typical != null) foreach (WorldPreset w in list) if (w.LotFigures == null) w.LotFigures = typical;
            // and the typical EA town shape
            List<TownShape> shapes = new List<TownShape>();
            foreach (WorldPreset w in list) if (w.Town != null) shapes.Add(w.Town);
            TownShape typicalShape = TownShape.Typical(shapes);
            if (typicalShape != null) foreach (WorldPreset w in list) if (w.Town == null) w.Town = typicalShape;
            List<DowntownGrid> grids = new List<DowntownGrid>();
            foreach (WorldPreset w in list) if (w.Grid != null) grids.Add(w.Grid);
            DowntownGrid typicalGrid = DowntownGrid.Typical(grids);
            if (typicalGrid != null) foreach (WorldPreset w in list) if (w.Grid == null) w.Grid = typicalGrid;
            List<RoadProfile> profiles = new List<RoadProfile>();
            foreach (WorldPreset w in list) if (w.Profile != null) profiles.Add(w.Profile);
            RoadProfile typicalProfile = RoadProfile.Typical(profiles);
            if (typicalProfile != null) foreach (WorldPreset w in list) if (w.Profile == null) w.Profile = typicalProfile;
            return list;
        }

        /// <summary>Library file key of a world: "Sunset Valley (base game)" -> "sunset_valley".</summary>
        public static string Key(string name) { return Short(name).Trim().ToLowerInvariant().Replace(' ', '_'); }

        /// <summary>
        /// The EA world the current generated map was modelled on ('Any EA world'), so Auto-Paint, Auto-Place and
        /// Spawners start with that world's style and objects. Null until Generate Terrain has picked one.
        /// </summary>
        public static string Inspiration;

        /// <summary>
        /// 'Any EA world': the seed picks one measured EA world and every setting comes from that world alone (no blending
        /// since v0.46: mixing two worlds' terrain caused problems). 'label' says which.
        /// </summary>
        public static WorldPreset Inspired(List<WorldPreset> all, int seed, out string label) { return Inspired(all, seed, -1, out label); }

        /// <summary>
        /// Worlds left out of 'Any EA world' (still choosable by name): the tester's own Vermont, and Lunar Lakes, whose
        /// craters and round ponds are a sci-fi one-off (the tester, 2026-10-21) that shouldn't turn up on random maps.
        /// </summary>
        public static bool NotInBlend(string name)
        {
            return name.IndexOf("the tester", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Lunar Lakes", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("custom", StringComparison.OrdinalIgnoreCase) >= 0; // non-EA worlds (Bridgewood) are picked by name only
        }

        /// <summary>As above; with a coast (CoastType value, -1 = any) the main world is one with that coast if there is
        /// one, else the coast is set on the blend (the user's coast choice stays when the seed changes).</summary>
        public static WorldPreset Inspired(List<WorldPreset> all, int seed, int coast, out string label)
        {
            List<WorldPreset> ea = new List<WorldPreset>();
            foreach (WorldPreset w in all) if (w.Source == "measured" && !NotInBlend(w.Name)) ea.Add(w);
            label = "";
            if (ea.Count == 0) return null;
            Random rng = new Random(seed * 977 + 11);
            List<WorldPreset> mains = new List<WorldPreset>();
            if (coast >= 0) foreach (WorldPreset w in ea) if ((int)w.Coast == coast) mains.Add(w);
            if (mains.Count == 0) mains = ea;
            // one world per map, no blending (the tester, 2026-10-24: a Sunlit Tides map shouldn't take Aurora Skies' hills)
            WorldPreset a = mains[rng.Next(mains.Count)], b = a;
            float t = 0;
            WorldPreset m = Parse(a.Line);
            m.Name = a.Name; m.Source = "measured";
            m.Mountains = L(a.Mountains, b.Mountains, t); m.Hills = L(a.Hills, b.Hills, t); m.Roughness = L(a.Roughness, b.Roughness, t);
            m.Erosion = L(a.Erosion, b.Erosion, t); m.FlatLand = L(a.FlatLand, b.FlatLand, t);
            m.Variety = L(a.Variety, b.Variety, t); m.Steepness = L(a.Steepness, b.Steepness, t);
            m.DowntownBlocks = (int)Math.Round(L(a.DowntownBlocks, b.DowntownBlocks, t)); m.Neighbourhoods = (int)Math.Round(L(a.Neighbourhoods, b.Neighbourhoods, t));
            m.Lots = (int)Math.Round(L(a.Lots, b.Lots, t)); m.EdgeRoads = (int)Math.Round(L(a.EdgeRoads, b.EdgeRoads, t));
            m.Region = a.Region; m.Character = a.Character; // the main world's kind of place decides the road layout's character
            m.Style = a.Style; // the main world's own look (dissection, terraces, softness, town)
            m.RiverWidth = a.RiverWidth; m.Ponds = a.Ponds; m.PondRadius = a.PondRadius;
            m.RoadTargets = a.RoadTargets; m.Terrain = a.Terrain; m.PathTargets = a.PathTargets; m.LotFigures = a.LotFigures; m.Town = a.Town; m.Grid = a.Grid; m.Profile = a.Profile; // the checks follow the main world
            m.ClimbCost = L(a.ClimbCost, b.ClimbCost, t); m.BuildSlope = L(a.BuildSlope, b.BuildSlope, t); m.DeadEndShare = L(a.DeadEndShare, b.DeadEndShare, t);
            if (a.FootpathKm >= 0 && b.FootpathKm >= 0) m.FootpathKm = L(a.FootpathKm, b.FootpathKm, t);
            if (a.RoadKm > 0 && b.RoadKm > 0) m.RoadKm = L(a.RoadKm, b.RoadKm, t);
            m.Note = "Inspired by " + Short(a.Name) + ".";
            if (coast >= 0) m.Coast = (CoastType)coast;
            label = m.Note;
            return m;
        }

        public string Line; // the worlds.txt line this preset came from

        /// <summary>The terrain profile loaded from this library file (TerrainProfile.Key), for CAWPlusHelper; null if none.</summary>
        public static TerrainProfile FindTerrain(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (WorldPreset w in LoadAll())
                if (w.Terrain != null && string.Equals(w.Terrain.Key, key, StringComparison.OrdinalIgnoreCase)) return w.Terrain;
            return null;
        }

        public static string Short(string name) { int b = name.IndexOf(" ("); return b > 0 ? name.Substring(0, b) : name; }
        private static float L(float a, float b, float t) { return a + (b - a) * t; }

        /// <summary>One worlds.txt line (see the header of library/worlds.txt).</summary>
        public static WorldPreset Parse(string line)
        {
            string[] p = line.Split('|');
            WorldPreset w = new WorldPreset();
            w.Line = line;
            w.Name = p[0]; w.Source = p[1]; w.Coast = (CoastType)Enum.Parse(typeof(CoastType), p[2], true);
            w.Mountains = F(p[3]); w.Hills = F(p[4]); w.Roughness = F(p[5]); w.Erosion = F(p[6]); w.FlatLand = F(p[7]);
            w.Water = F(p[8]); w.MaxHeight = F(p[9]); w.Rivers = (int)F(p[10]); w.DowntownBlocks = (int)F(p[11]);
            w.Neighbourhoods = (int)F(p[12]); w.Lots = (int)F(p[13]); w.EdgeRoads = (int)F(p[14]); w.Variety = F(p[15]);
            w.Note = p[16];
            if (p.Length > 17 && p[17].Trim().Length > 0) w.Steepness = F(p[17]);
            if (p.Length > 18 && p[18].Trim().Length > 0) w.FootpathKm = F(p[18]);
            if (p.Length > 19 && p[19].Trim().Length > 0) w.ClimbCost = F(p[19]);
            if (p.Length > 20 && p[20].Trim().Length > 0)
            {
                string[] c = p[20].Split(',');
                w.HeightCurve = new float[c.Length];
                for (int k = 0; k < c.Length; k++) w.HeightCurve[k] = F(c[k]);
            }
            if (p.Length > 21 && p[21].Trim().Length > 0) w.InlandLakes = F(p[21]) != 0;
            if (p.Length > 22 && p[22].Trim().Length > 0) w.CoastCorner = F(p[22]);
            if (p.Length > 23 && p[23].Trim().Length > 0) w.RoadKm = F(p[23]);
            if (p.Length > 24 && p[24].Trim().Length > 0) w.DeadEndShare = F(p[24]);
            if (p.Length > 25 && p[25].Trim().Length > 0) w.BuildSlope = F(p[25]);
            if (p.Length > 26 && p[26].Trim().Length > 0) w.RiverWidth = F(p[26]);
            if (p.Length > 27 && p[27].Trim().Length > 0) w.Ponds = (int)F(p[27]);
            if (p.Length > 28 && p[28].Trim().Length > 0) w.PondRadius = F(p[28]);
            if (p.Length > 29 && p[29].Trim().Length > 0) w.SeaShare = F(p[29]);
            if (p.Length > 30 && p[30].Trim().Length > 0) w.LakeShare = F(p[30]);
            if (p.Length > 31 && p[31].Trim().Length > 0) w.RoadTargets = RoadTargets.Parse(p[31].Trim());
            if (p.Length > 33 && p[33].Trim().Length > 0) { try { w.LotFigures = LotTargets.Parse(p[33].Trim(), p.Length > 34 ? p[34].Trim() : null); } catch (FormatException) { w.LotFigures = null; } }
            if (p.Length > 35) w.Style = p[35].Trim();
            if (p.Length > 36) { w.Region = p[36].Trim(); w.Character = RoadCharacter.For(w.Region, null); }
            if (p.Length > 37 && p[37].Trim().Length > 0) { try { w.Town = TownShape.Parse(p[37].Trim()); } catch (FormatException) { w.Town = null; } }
            if (p.Length > 38 && p[38].Trim().Length > 0) { try { w.Grid = DowntownGrid.Parse(p[38].Trim()); } catch (FormatException) { w.Grid = null; } }
            if (p.Length > 39 && p[39].Trim().Length > 0) { try { w.Profile = RoadProfile.Parse(p[39].Trim()); } catch (FormatException) { w.Profile = null; } }
            if (p.Length > 40 && p[40].Trim().Length > 0 && w.LotFigures != null) { try { w.LotFigures.Kinds = LotKind.Parse(p[40].Trim()); } catch (FormatException) { w.LotFigures.Kinds = null; } }
            if (p.Length > 32 && p[32].Trim().Length > 0)
            {
                string[] f = p[32].Split(',');
                w.PathTargets = new float[f.Length];
                for (int k = 0; k < f.Length; k++) if (!float.TryParse(f[k], NumberStyles.Float, CultureInfo.InvariantCulture, out w.PathTargets[k])) w.PathTargets[k] = -1;
            }
            return w;
        }

        private static float F(string s) { return float.Parse(s.Trim(), CultureInfo.InvariantCulture); }
    }
}
