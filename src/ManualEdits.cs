using System;
using System.Collections.Generic;
using System.IO;

namespace CawPlus
{
    /// <summary>
    /// Keeps hand-painted changes across Auto-Paint runs. After each run the exact map written to every layer is saved
    /// (Documents\CAWPlus\Worlds\&lt;world&gt;\gen_layer_N.png). Next run, current layer minus that saved map = what was
    /// painted by hand since; those areas keep the hand-painted value instead of the newly generated one.
    /// </summary>
    public static class ManualEdits
    {
        private const float Threshold = 6f / 255f; // ignore 8-bit round-trip differences

        private static string GenPath(string state, int idx) { return Path.Combine(state, "gen_layer_" + idx + ".png"); }
        private static string RunPath(string state, int idx) { return Path.Combine(state, "gen_layer_" + idx + ".run"); }
        private static string LastRunPath(string state) { return Path.Combine(state, "last_run.txt"); }
        private static string TerrainPath(string state) { return Path.Combine(state, "terrain.txt"); }

        /// <summary>
        /// The terrain the current run paints (set by Auto-Paint before Merge): records made on other terrain are ignored.
        /// Unsaved worlds all share one record folder, and Apply terrain changes the land under the old paint, so without
        /// this a record from another world was compared with this one's layers and their differences "kept" as hand
        /// edits (the tester's v0.40 log: 19% of layer 1 kept in a fresh world).
        /// </summary>
        public static string CurrentTerrain = "";

        /// <summary>A short fingerprint of a height map (32 x 32 samples, rounded to 1 m).</summary>
        public static string Fingerprint(Grid h)
        {
            uint f = 2166136261;
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    int v = (int)Math.Round(h[(int)((long)x * (h.W - 1) / 31), (int)((long)y * (h.H - 1) / 31)]);
                    for (int b = 0; b < 4; b++) { f ^= (uint)((v >> (8 * b)) & 0xff); f *= 16777619; }
                }
            return h.W + "x" + h.H + "_" + f.ToString("x8");
        }

        private static bool SameTerrain(string state, out string note)
        {
            string t = TerrainPath(state);
            string rec = File.Exists(t) ? File.ReadAllText(t).Trim() : "";
            if (CurrentTerrain.Length > 0 && rec != CurrentTerrain)
            {
                note = rec.Length == 0 ? "record has no terrain fingerprint (older run), ignored" : "the terrain has changed since the record (new terrain or another world), record ignored";
                return false;
            }
            note = ""; return true;
        }

        /// <summary>
        /// Marks which run last painted this world. A layer's record only counts if that same run wrote it, so an
        /// older record (from before other repaints, an older version or a different layer mapping) is never mistaken
        /// for hand edits.
        /// </summary>
        public static void EndRun(string state, string runId)
        {
            try { Directory.CreateDirectory(state); File.WriteAllText(LastRunPath(state), runId); File.WriteAllText(TerrainPath(state), CurrentTerrain); }
            catch (Exception ex) { Log.Error("Saving run id failed", ex); }
        }
        private static string NamePath(string state, int idx) { return Path.Combine(state, "gen_layer_" + idx + ".txt"); }

        /// <summary>Layers Auto-Paint has a record of (it painted them in some run).</summary>
        public static List<int> RecordedLayers(string state)
        {
            List<int> l = new List<int>();
            if (!Directory.Exists(state)) return l;
            foreach (string f in Directory.GetFiles(state, "gen_layer_*.png"))
            {
                int idx;
                string n = Path.GetFileNameWithoutExtension(f).Substring("gen_layer_".Length);
                if (int.TryParse(n, out idx)) l.Add(idx);
            }
            l.Sort();
            return l;
        }

        /// <summary>
        /// True if the layer still holds what Auto-Paint wrote (same texture, under 2% of the map changed by more than
        /// 6/255): safe to empty. 'why' says otherwise.
        /// </summary>
        public static bool UnchangedSinceRecorded(string state, int idx, string layerName, string currentPng, out string why)
        {
            string gen = GenPath(state, idx), nm = NamePath(state, idx);
            if (!File.Exists(gen) || !File.Exists(nm)) { why = "no record"; return false; }
            if (File.ReadAllText(nm) != (layerName ?? "")) { why = "now a different texture"; return false; }
            if (!File.Exists(currentPng)) { why = "could not be exported"; return false; }
            if (!SameTerrain(state, out why)) return false;
            Grid last = ImageIO.ReadPngChannel0(gen), cur = ImageIO.ReadPngChannel0(currentPng);
            if (last.W != cur.W || last.H != cur.H) { why = "size changed"; return false; }
            int diff = 0;
            for (int i = 0; i < cur.D.Length; i++) if (Math.Abs(cur.D[i] - last.D[i]) > 6f / 255f) diff++;
            float pct = 100f * diff / cur.D.Length;
            if (pct >= 2f) { why = string.Format("{0:F1}% of it was painted by hand since", pct); return false; }
            why = ""; return true;
        }

        /// <summary>Drops the record of a layer Auto-Paint no longer uses.</summary>
        public static void Forget(string state, int idx)
        {
            foreach (string f in new[] { GenPath(state, idx), NamePath(state, idx), RunPath(state, idx) })
                try { if (File.Exists(f)) File.Delete(f); } catch (Exception ex) { Log.Error("Forgetting " + f + " failed", ex); }
        }

        public static void Record(string state, int idx, string layerName, string writtenPng, string runId)
        {
            try
            {
                Directory.CreateDirectory(state);
                File.WriteAllText(RunPath(state, idx), runId);
                File.Copy(writtenPng, GenPath(state, idx), true);
                File.WriteAllText(NamePath(state, idx), layerName ?? "");
                Log.Info("Recorded generated map for layer " + idx + " (" + layerName + ")");
            }
            catch (Exception ex) { Log.Error("Recording generated layer " + idx + " failed", ex); }
        }

        /// <summary>
        /// Adds manual edits into 'generated' in place. Returns the % of the map that was hand-edited (0 if none or no
        /// record). 'note' says what happened, for the log.
        /// </summary>
        public static float Merge(string state, int idx, string layerName, string currentPng, Grid generated, out string note)
        {
            string gen = GenPath(state, idx), nm = NamePath(state, idx);
            if (!File.Exists(gen) || !File.Exists(nm)) { note = "no earlier Auto-Paint record, nothing to keep"; return 0; }
            if (File.ReadAllText(nm) != (layerName ?? "")) { note = "layer " + idx + " is now a different texture, record ignored"; return 0; }
            string rp = RunPath(state, idx), lp = LastRunPath(state);
            if (!File.Exists(rp) || !File.Exists(lp) || File.ReadAllText(rp) != File.ReadAllText(lp))
            {
                note = "record is not from the last Auto-Paint run, ignored";
                return 0;
            }
            if (!File.Exists(currentPng)) { note = "current layer could not be exported"; return 0; }
            if (!SameTerrain(state, out note)) return 0;

            Grid last = ImageIO.ReadPngChannel0(gen), cur = ImageIO.ReadPngChannel0(currentPng);
            if (last.W != generated.W || last.H != generated.H || cur.W != generated.W || cur.H != generated.H)
            {
                note = string.Format("size mismatch (record {0}x{1}, current {2}x{3}, new {4}x{5})", last.W, last.H, cur.W, cur.H, generated.W, generated.H);
                return 0;
            }

            int n = generated.D.Length;
            Grid changed = new Grid(generated.W, generated.H);
            for (int i = 0; i < n; i++)
            {
                float d = cur.D[i] - last.D[i];
                if (d > Threshold || d < -Threshold) changed.D[i] = 1;
            }
            int flagged = 0;
            for (int i = 0; i < n; i++) if (changed.D[i] > 0) flagged++;
            if (flagged > n * 0.4f)
            {
                // Hand edits never cover most of the world; this means CAW changed the map on import (resampling etc.).
                note = string.Format("{0:F0}% of the layer differs from the record; treated as a CAW round-trip change, not edits", 100f * flagged / n);
                return 0;
            }
            // Brush strokes are areas; isolated pixels are just compression/resampling noise. Where a stroke is found,
            // use exactly what was painted (feathered at the stroke edge) instead of the new generated value.
            Grid area = changed.Blur(1f);
            int kept = 0;
            for (int i = 0; i < n; i++)
            {
                float w = Grid.Ramp(area.D[i], 0.3f, 0.6f);
                if (w <= 0) continue;
                generated.D[i] = generated.D[i] * (1 - w) + cur.D[i] * w;
                if (changed.D[i] > 0) kept++;
            }
            float pct = 100f * kept / n;
            note = string.Format("{0} hand-edited pixels kept ({1:F2}%)", kept, pct);
            return pct;
        }

        public static void CopyDir(string from, string to)
        {
            try
            {
                if (!Directory.Exists(from)) return;
                Directory.CreateDirectory(to);
                foreach (string f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            }
            catch (Exception ex) { Log.Error("Copying " + from + " failed", ex); }
        }

        /// <summary>Keeps the newest 'keep' backups (sub-folders or height_*.png files, named by time) in a world's backup folder.</summary>
        public static void PruneBackups(string worldBackups, int keep)
        {
            try
            {
                string root = Path.GetFullPath(Path.Combine(Log.Dir, "Backups")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(worldBackups);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(full)) return;
                string[] dirs = Directory.GetDirectories(full), files = Directory.GetFiles(full, "height_*");
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase); Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < dirs.Length - keep; i++) Directory.Delete(dirs[i], true);
                for (int i = 0; i < files.Length - keep; i++) File.Delete(files[i]);
            }
            catch (Exception ex) { Log.Error("Pruning backups in " + worldBackups + " failed", ex); }
        }

        public static void DeleteDir(string dir)
        {
            // only ever a world's own record folder: strictly inside Documents\CAWPlus\Worlds
            string root = Path.GetFullPath(Path.Combine(Log.Dir, "Worlds")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || full.Length <= root.Length)
            {
                Log.Info("Not deleting " + dir + ": outside " + root);
                return;
            }
            try { if (Directory.Exists(full)) Directory.Delete(full, true); }
            catch (Exception ex) { Log.Error("Deleting " + dir + " failed", ex); }
        }
    }
}
