using System;
using System.Collections.Generic;
using System.IO;

namespace CawPlus
{
    /// <summary>
    /// Auto-Paint's heavy part, from the terrain to finished layer PNGs: the masks (style or built-in rules), the CAW
    /// layer opacities (with the style's other textures and base), manual edits kept, old Auto-Paint layers emptied and
    /// the per-chunk layer limit. Runs in CAWPlusHelper (64-bit, no 32-bit memory limit) or, without it, inside CAW.
    /// CAW then only imports the PNGs it lists.
    /// </summary>
    internal sealed class PaintJob
    {
        // ---- input ----
        public Grid Heights, WaterOffset;
        public float Cell;
        public PaintSettings S = new PaintSettings();
        public StyleProfile Style;                 // null: the built-in rules
        public string Gains = "";                  // the preview's style corrections (StyledGenerator.GainsText)
        public List<PaintRole> Mapped = new List<PaintRole>();             // roles with a layer, CAW's order bottom to top
        public Dictionary<PaintRole, int> LayerOf = new Dictionary<PaintRole, int>();
        public bool Variety;
        public Dictionary<PaintRole, List<KeyValuePair<int, float>>> LayersOfRole = new Dictionary<PaintRole, List<KeyValuePair<int, float>>>();
        public int BaseIdx = -1;
        public float PatchPx;
        public bool KeepEdits;
        public string State = "", Backup = "", OutDir = "", Fingerprint = "";
        public string[] Names = new string[0];     // every CAW layer's name, by index
        public int EdgeLayer = -1, ChunkLimit = 8;

        // ---- output ----
        public List<int> Written = new List<int>(); // layer_<i>.png in OutDir
        public List<int> Cleared = new List<int>(); // layer_<i>_empty.png in OutDir
        public string MatchNote = "", Kept = "";

        public void Run(ProgressFn progress)
        {
            ManualEdits.CurrentTerrain = Fingerprint;
            int count = Names.Length;
            Dictionary<PaintRole, Grid> masks;
            if (Style != null)
            {
                StyledGenerator gen = new StyledGenerator(Style, Heights, Cell, S.WaterLevel, WaterOffset, S.Seed, Mapped, S.Detail);
                gen.SetGains(Gains); // start from the corrections found for the preview
                masks = gen.Fit(Amounts(S), S.Softness / 50f, 3, progress);
                FillMissingRoles(masks, Style, Heights, Cell, S);
                MatchNote = string.Format(" Match to {0}: {1:F0}%.", Style.Name, gen.Match);
                Log.Info("Styled paint with " + Style.Name + ": match " + gen.Match.ToString("F1") + "% after " + gen.Passes + " pass(es)");
            }
            else masks = PaintEngine.Compute(Heights, Cell, S, progress);

            // every CAW layer this run writes, by index (with variety: several textures per type, and a base)
            Dictionary<int, Grid> byLayer;
            if (Variety) byLayer = PaintEngine.ToCawLayers(masks, LayersOfRole, BaseIdx, PatchPx, S.Seed);
            else
            {
                Dictionary<PaintRole, Grid> stacked = PaintEngine.ToCawStack(masks, Mapped);
                byLayer = new Dictionary<int, Grid>(); foreach (PaintRole r in Mapped) byLayer[LayerOf[r]] = stacked[r];
            }
            masks = null;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();

            // Keep manual edits: whatever changed on a layer since Auto-Paint last wrote it was painted by hand.
            if (KeepEdits)
                foreach (int idx in byLayer.Keys)
                {
                    string note;
                    float pct = ManualEdits.Merge(State, idx, Names[idx], Path.Combine(Backup, "layer_" + idx + ".png"), byLayer[idx], out note);
                    Log.Info("Manual edits, layer " + idx + ": " + note);
                    if (pct > 0) Kept += string.Format(" layer {0}: {1:F1}%", idx, pct);
                }

            // Layers an earlier Auto-Paint run painted that this run doesn't use: emptied, unless painted by hand since.
            List<int> used = new List<int>(byLayer.Keys);
            Grid any = null; foreach (Grid gq in byLayer.Values) { any = gq; break; }
            if (any != null)
                foreach (int idx in ManualEdits.RecordedLayers(State))
                {
                    if (idx <= 0 || idx >= count || used.Contains(idx) || idx == EdgeLayer) continue;
                    if (Names[idx].StartsWith(EdgeBlend.LayerPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    string why;
                    if (ManualEdits.UnchangedSinceRecorded(State, idx, Names[idx], Path.Combine(Backup, "layer_" + idx + ".png"), out why))
                    {
                        Cleared.Add(idx);
                        Log.Info("Old Auto-Paint layer " + idx + " (" + Names[idx] + ") emptied: not used by this run");
                    }
                    else Log.Info("Old Auto-Paint layer " + idx + " (" + Names[idx] + ") left as it is: " + why);
                }

            // At most ChunkLimit painted layers per 256 m chunk (base layer included).
            if (progress != null) progress("Checking layers per chunk", 90);
            Dictionary<int, Grid> others = new Dictionary<int, Grid>();
            for (int idx = 1; idx < count; idx++)
            {
                if (used.Contains(idx) || Cleared.Contains(idx) || idx == EdgeLayer) continue;
                string f = Path.Combine(Backup, "layer_" + idx + ".png");
                if (File.Exists(f)) try { others[idx] = ImageIO.ReadPngChannel0(f); } catch (Exception ex) { Log.Error("Reading layer " + idx + " for the chunk count failed", ex); }
            }
            Log.Info(PaintEngine.LimitPerChunk(byLayer, others, EdgeLayer >= 0, Cell, ChunkLimit));
            others = null;

            // The maps CAW will import.
            Directory.CreateDirectory(OutDir);
            List<int> keys = new List<int>(byLayer.Keys);
            keys.Sort();
            foreach (int idx in keys)
            {
                if (progress != null) progress("Writing layer " + idx, 92 + 6 * Written.Count / Math.Max(1, keys.Count));
                ImageIO.WriteGrayPng(byLayer[idx], Path.Combine(OutDir, "layer_" + idx + ".png"));
                byLayer[idx] = null; // one map at a time
                Written.Add(idx);
            }
            if (Cleared.Count > 0 && any != null)
            {
                Grid empty = new Grid(any.W, any.H);
                foreach (int idx in Cleared) ImageIO.WriteGrayPng(empty, Path.Combine(OutDir, "layer_" + idx + "_empty.png"));
            }
        }

        public static Dictionary<PaintRole, float> Amounts(PaintSettings s)
        {
            Dictionary<PaintRole, float> a = new Dictionary<PaintRole, float>();
            a[PaintRole.Rock] = s.Rock / 50f; a[PaintRole.Dirt] = s.Dirt / 50f;
            a[PaintRole.Shore] = s.Shore / 50f; a[PaintRole.Meadow] = s.Meadow / 50f;
            return a;
        }

        /// <summary>Types the style has no example of (e.g. no sand in an inland world) use the built-in rules.</summary>
        public static void FillMissingRoles(Dictionary<PaintRole, Grid> masks, StyleProfile style, Grid h, float cell, PaintSettings s)
        {
            bool missing = false;
            foreach (PaintRole r in PaintEngine.Roles) if (!style.Roles.ContainsKey(r)) missing = true;
            if (!missing) return;
            Dictionary<PaintRole, Grid> builtIn = PaintEngine.Compute(h, cell, s, null);
            foreach (PaintRole r in PaintEngine.Roles) if (!style.Roles.ContainsKey(r)) masks[r] = builtIn[r];
        }

        // ---------- job files (CAWPlusHelper) ----------

        public void WriteTo(string dir)
        {
            using (BinaryWriter w = new BinaryWriter(File.Create(Path.Combine(dir, "paint_in.bin"))))
            {
                HelperLink.WriteGrid(w, Heights);
                HelperLink.WriteGrid(w, WaterOffset);
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("cell=").Append(HelperLink.F(Cell)).Append('\n');
            HelperLink.WriteFields(sb, "s.", S);
            sb.Append("gains=").Append(Gains.Replace("\n", "/")).Append('\n');
            List<string> m = new List<string>(); foreach (PaintRole r in Mapped) m.Add(r.ToString());
            sb.Append("mapped=").Append(string.Join(",", m.ToArray())).Append('\n');
            foreach (KeyValuePair<PaintRole, int> kv in LayerOf) sb.Append("layerof.").Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
            sb.Append("variety=").Append(Variety).Append('\n');
            foreach (KeyValuePair<PaintRole, List<KeyValuePair<int, float>>> kv in LayersOfRole)
            {
                List<string> p = new List<string>();
                foreach (KeyValuePair<int, float> e in kv.Value) p.Add(e.Key + ":" + HelperLink.F(e.Value));
                sb.Append("layers.").Append(kv.Key).Append('=').Append(string.Join(",", p.ToArray())).Append('\n');
            }
            sb.Append("base=").Append(BaseIdx).Append('\n');
            sb.Append("patch=").Append(HelperLink.F(PatchPx)).Append('\n');
            sb.Append("keep=").Append(KeepEdits).Append('\n');
            sb.Append("state=").Append(State).Append('\n');
            sb.Append("backup=").Append(Backup).Append('\n');
            sb.Append("out=").Append(OutDir).Append('\n');
            sb.Append("fingerprint=").Append(Fingerprint).Append('\n');
            // layer names come from the world file and may hold any character (a line break would start a new key, a '|'
            // would shift every later name): each is written base64-encoded
            string[] enc = new string[Names.Length];
            for (int i = 0; i < Names.Length; i++) enc[i] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Names[i] ?? ""));
            sb.Append("names=").Append(string.Join("|", enc)).Append('\n');
            sb.Append("edge=").Append(EdgeLayer).Append('\n');
            sb.Append("chunk=").Append(ChunkLimit).Append('\n');
            if (Style != null) sb.Append("stylename=").Append(HelperLink.OneLine(Style.Name)).Append('\n');
            File.WriteAllText(Path.Combine(dir, "job.txt"), sb.ToString());
            if (Style != null) File.WriteAllText(Path.Combine(dir, "style.txt"), Style.ToText());
        }

        public static PaintJob ReadFrom(string dir)
        {
            PaintJob j = new PaintJob();
            using (BinaryReader r = new BinaryReader(File.OpenRead(Path.Combine(dir, "paint_in.bin"))))
            {
                j.Heights = HelperLink.ReadGrid(r);
                j.WaterOffset = HelperLink.ReadGrid(r);
            }
            Dictionary<string, string> kv = HelperLink.ReadKeyValues(Path.Combine(dir, "job.txt"));
            j.Cell = HelperLink.PF(kv["cell"]);
            HelperLink.ReadFields(kv, "s.", j.S);
            j.S.WaterOffset = j.WaterOffset;
            j.Gains = kv["gains"].Replace("/", "\n");
            foreach (string r in kv["mapped"].Split(',')) if (r.Length > 0) j.Mapped.Add((PaintRole)Enum.Parse(typeof(PaintRole), r));
            foreach (KeyValuePair<string, string> e in kv)
            {
                if (e.Key.StartsWith("layerof.")) j.LayerOf[(PaintRole)Enum.Parse(typeof(PaintRole), e.Key.Substring(8))] = int.Parse(e.Value);
                else if (e.Key.StartsWith("layers."))
                {
                    List<KeyValuePair<int, float>> l = new List<KeyValuePair<int, float>>();
                    foreach (string p in e.Value.Split(','))
                    {
                        int c = p.IndexOf(':');
                        if (c > 0) l.Add(new KeyValuePair<int, float>(int.Parse(p.Substring(0, c)), HelperLink.PF(p.Substring(c + 1))));
                    }
                    j.LayersOfRole[(PaintRole)Enum.Parse(typeof(PaintRole), e.Key.Substring(7))] = l;
                }
            }
            j.Variety = bool.Parse(kv["variety"]);
            j.BaseIdx = int.Parse(kv["base"]);
            j.PatchPx = HelperLink.PF(kv["patch"]);
            j.KeepEdits = bool.Parse(kv["keep"]);
            j.State = kv["state"]; j.Backup = kv["backup"]; j.OutDir = kv["out"]; j.Fingerprint = kv["fingerprint"];
            string[] enc = kv["names"].Length > 0 ? kv["names"].Split('|') : new string[0];
            j.Names = new string[enc.Length];
            for (int i = 0; i < enc.Length; i++) j.Names[i] = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(enc[i]));
            j.EdgeLayer = int.Parse(kv["edge"]);
            j.ChunkLimit = int.Parse(kv["chunk"]);
            string sf = Path.Combine(dir, "style.txt");
            if (File.Exists(sf)) j.Style = StyleProfile.Parse(kv.ContainsKey("stylename") ? kv["stylename"] : "style", File.ReadAllLines(sf));
            return j;
        }

        public void WriteResult(string dir)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("written=").Append(Join(Written)).Append('\n');
            sb.Append("cleared=").Append(Join(Cleared)).Append('\n');
            sb.Append("match=").Append(MatchNote).Append('\n');
            sb.Append("kept=").Append(Kept).Append('\n');
            File.WriteAllText(Path.Combine(dir, "paint_out.tmp"), sb.ToString());
            File.Move(Path.Combine(dir, "paint_out.tmp"), Path.Combine(dir, "paint_out.txt"));
        }

        public bool ReadResult(string dir)
        {
            string f = Path.Combine(dir, "paint_out.txt");
            if (!File.Exists(f)) return false;
            Dictionary<string, string> kv = HelperLink.ReadKeyValues(f);
            Written = Split(kv["written"]); Cleared = Split(kv["cleared"]);
            MatchNote = kv["match"]; Kept = kv["kept"];
            return true;
        }

        private static string Join(List<int> l) { string[] s = new string[l.Count]; for (int i = 0; i < s.Length; i++) s[i] = l[i].ToString(); return string.Join(",", s); }
        private static List<int> Split(string s) { List<int> l = new List<int>(); foreach (string p in s.Split(',')) if (p.Length > 0) l.Add(int.Parse(p)); return l; }
    }
}
