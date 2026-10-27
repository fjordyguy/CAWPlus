using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace CawPlus
{
    /// <summary>
    /// Reads the colour of the world's distant terrain from the game files CAW has loaded.
    /// The distant terrain is an ordinary object in the world (e.g. "terrainDistantEP5" in its own World Layer);
    /// its model and textures are EA assets, not in the .world. Path: object -> ModelComponent.Model.Key (MODL)
    /// -> ResourceMgr.FindRecord -> RCOL resource lists (MODL, then MLODs) -> DDS keys (0x00B2D882) -> mean colour.
    /// The textures found are saved to Documents\CAWPlus\DistantTerrain for checking.
    /// </summary>
    public static class DistantTerrain
    {
        private const uint MODL = 0x01661233, MLOD = 0x01D10F34, DDS = 0x00B2D882;

        public sealed class Result
        {
            public string ObjectName;
            public Color Colour = Color.Empty;
            public string Texture;          // saved file used for the colour
            public string Note;
        }

        public static Result Read()
        {
            Result r = new Result();
            Type rm = Caw.FindType("Sims3.CSHost.ResourceMgr"), keyType = Caw.FindType("Sims3.CSHost.ResourceKey");
            if (rm == null || keyType == null) { r.Note = "ResourceMgr not found"; return r; }
            MethodInfo find = rm.GetMethod("FindRecord", BindingFlags.Static | BindingFlags.Public, null, new[] { keyType }, null);

            List<object> models = new List<object>();
            System.Collections.IEnumerable objs = Caw.Call(Caw.MainForm, "GetAllWorldObjects") as System.Collections.IEnumerable;
            if (objs != null)
                foreach (object o in objs)
                {
                    string name = "", script = "";
                    try { name = Convert.ToString(Caw.GetProperty(o, "InstanceName")); } catch { }
                    try { script = Convert.ToString(Caw.GetProperty(o, "ScriptClassName")); } catch { }
                    if (name.IndexOf("distant", StringComparison.OrdinalIgnoreCase) < 0 &&
                        script.IndexOf("distant", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    try
                    {
                        object key = Caw.GetProperty(Caw.GetProperty(Caw.GetProperty(o, "ModelComponent"), "Model"), "Key");
                        Log.Info("Distant terrain object '" + name + "' (" + script + "), model " + key);
                        models.Add(key);
                        if (r.ObjectName == null) r.ObjectName = name;
                    }
                    catch (Exception ex) { Log.Error("Distant terrain object '" + name + "': no model", ex); }
                }
            if (models.Count == 0) { r.Note = "No distant terrain object found in the world"; return r; }

            // Breadth-first over MODL -> MLOD (two levels), collecting texture keys.
            List<ulong[]> textures = new List<ulong[]>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            List<ulong[]> queue = new List<ulong[]>();
            foreach (object k in models)
                queue.Add(new ulong[] { Convert.ToUInt32(Caw.GetField(k, "mType")), Convert.ToUInt32(Caw.GetField(k, "mGroup")), Convert.ToUInt64(Caw.GetField(k, "mInstance")) });
            for (int qi = 0; qi < queue.Count && qi < 40; qi++)
            {
                ulong[] k = queue[qi];
                byte[] data = Find(find, keyType, k);
                Log.Info("  " + KeyText(k) + ": " + (data == null ? "not found" : data.Length + " bytes"));
                if (data == null) continue;
                foreach (ulong[] refk in References(data))
                {
                    string id = KeyText(refk);
                    if (seen.ContainsKey(id)) continue;
                    seen[id] = true;
                    if (refk[0] == DDS) textures.Add(refk);
                    else if (refk[0] == MLOD || refk[0] == MODL) queue.Add(refk);
                }
            }
            if (textures.Count == 0) { r.Note = "Model found, but no textures in its resource lists (see log)"; return r; }

            string dir = Path.Combine(Log.Dir, "DistantTerrain");
            Directory.CreateDirectory(dir);
            long bestScore = -1;
            foreach (ulong[] t in textures)
            {
                byte[] dds = Find(find, keyType, t);
                if (dds == null || dds.Length < 128 || dds[0] != 'D' || dds[1] != 'D' || dds[2] != 'S') { Log.Info("  texture " + KeyText(t) + ": not a DDS"); continue; }
                string file = Path.Combine(dir, string.Format("{0:X8}_{1:X16}.dds", t[1], t[2]));
                File.WriteAllBytes(file, dds);
                int w, h;
                int[] px = Dds.Decode(file, 256, out w, out h);
                if (px == null) { Log.Info("  texture " + file + ": format not decoded"); continue; }
                double sr = 0, sg = 0, sb = 0;
                foreach (int p in px) { sr += (p >> 16) & 255; sg += (p >> 8) & 255; sb += p & 255; }
                Color mean = Color.FromArgb((int)(sr / px.Length), (int)(sg / px.Length), (int)(sb / px.Length));
                // Normal maps average to a light blue (128,128,255); skip them. Prefer the biggest colour map.
                bool normal = mean.B > 180 && Math.Abs(mean.R - 128) < 30 && Math.Abs(mean.G - 128) < 30;
                int fullW = BitConverter.ToInt32(dds, 16), fullH = BitConverter.ToInt32(dds, 12);
                Log.Info("  texture " + Path.GetFileName(file) + ": " + fullW + "x" + fullH + ", mean " + mean.R + "," + mean.G + "," + mean.B + (normal ? " (normal map, skipped)" : ""));
                long score = normal ? -1 : (long)fullW * fullH;
                if (score > bestScore) { bestScore = score; r.Colour = mean; r.Texture = file; }
            }
            r.Note = r.Colour.IsEmpty ? "Textures found but none could be decoded (see log)" : null;
            return r;
        }

        /// <summary>A resource from the game files CAW has loaded (null if not found).</summary>
        public static byte[] Resource(uint type, uint group, ulong instance)
        {
            Type rm = Caw.FindType("Sims3.CSHost.ResourceMgr"), keyType = Caw.FindType("Sims3.CSHost.ResourceKey");
            if (rm == null || keyType == null) return null;
            MethodInfo find = rm.GetMethod("FindRecord", BindingFlags.Static | BindingFlags.Public, null, new[] { keyType }, null);
            return find == null ? null : Find(find, keyType, new ulong[] { type, group, instance });
        }

        /// <summary>The world's distant terrain objects (by instance or script name), for removing them.</summary>
        public static List<object> Objects()
        {
            List<object> list = new List<object>();
            System.Collections.IEnumerable objs = Caw.Call(Caw.MainForm, "GetAllWorldObjects") as System.Collections.IEnumerable;
            if (objs == null) return list;
            foreach (object o in objs)
            {
                string name = "", script = "";
                try { name = Convert.ToString(Caw.GetProperty(o, "InstanceName")); } catch { }
                try { script = Convert.ToString(Caw.GetProperty(o, "ScriptClassName")); } catch { }
                if (name.IndexOf("distant", StringComparison.OrdinalIgnoreCase) >= 0 || script.IndexOf("distant", StringComparison.OrdinalIgnoreCase) >= 0) list.Add(o);
            }
            return list;
        }

        /// <summary>
        /// An installed distant terrain object to copy for a new one: its catalog definition, object key, visual proxy,
        /// footprint and lights, read from the game files. EA's own distant terrains come first (base game before packs).
        /// </summary>
        internal static DistantMaker.Template FindTemplate(List<Caw.CatalogItem> catalog, out string note)
        {
            note = "";
            List<Caw.CatalogItem> cands = new List<Caw.CatalogItem>();
            foreach (Caw.CatalogItem c in catalog)
            {
                if (c.IsTree || c.Key == null) continue;
                string n = (c.Name ?? "") + " " + (c.Path ?? "");
                if (n.IndexOf("distant", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("cawplus", StringComparison.OrdinalIgnoreCase) < 0) cands.Add(c);
            }
            // base game first: names without a pack tag (EP, DOT, store) sort first
            cands.Sort(delegate (Caw.CatalogItem x, Caw.CatalogItem y) { return Rank(x.Name).CompareTo(Rank(y.Name)); });
            List<ulong[]> keys = new List<ulong[]>(); List<string> names = new List<string>();
            foreach (Caw.CatalogItem c in cands)
            {
                keys.Add(new ulong[] { Convert.ToUInt32(Caw.GetField(c.Key, "mGroup")), Convert.ToUInt64(Caw.GetField(c.Key, "mInstance")) });
                names.Add(c.Name);
            }
            // EA's distant terrain objects (from the EA worlds' layer files), in case the catalog doesn't list them:
            // base game (Sunset Valley) first, then the packs and store worlds
            foreach (ulong inst in KnownDistant) { keys.Add(new ulong[] { 0, inst }); names.Add("EA distant terrain " + inst.ToString("X")); }
            for (int ci = 0; ci < keys.Count; ci++)
            {
                uint g = (uint)keys[ci][0]; ulong i = keys[ci][1];
                DistantMaker.Template t = new DistantMaker.Template();
                t.Objd = Resource(S3Pkg.OBJD, g, i); t.Objk = Resource(S3Pkg.OBJK, g, i);
                if (t.Objd == null || t.Objk == null) { Log.Info("  distant terrain template '" + names[ci] + "': definition not readable"); continue; }
                try
                {
                    ulong[] vp = FirstTgi(t.Objk, 8 + BitConverter.ToInt32(t.Objk, 4), S3Pkg.VPXY);
                    if (vp == null) continue;
                    t.Vpxy = Resource((uint)vp[0], (uint)vp[1], vp[2]);
                    if (t.Vpxy == null) { Log.Info("  distant terrain template '" + names[ci] + "': proxy not readable"); continue; }
                    int ext = (int)BitConverter.ToUInt32(t.Vpxy, 12);
                    int chunk = (int)BitConverter.ToUInt32(t.Vpxy, 20 + 16 * (1 + ext));
                    int tgi = chunk + 12 + BitConverter.ToInt32(t.Vpxy, chunk + 8);
                    ulong[] fp = FirstTgi(t.Vpxy, tgi, S3Pkg.FTPT), li = FirstTgi(t.Vpxy, tgi, S3Pkg.LITE);
                    if (fp != null) t.Ftpt = Resource((uint)fp[0], (uint)fp[1], fp[2]);
                    if (li != null) t.Lite = Resource((uint)li[0], (uint)li[1], li[2]);
                    t.Source = names[ci] + " (" + string.Format("{0:X8}:{1:X16}", g, i) + ")";
                    note = "copied from " + t.Source;
                    Log.Info("Distant terrain template: " + t.Source + ", definition v" + BitConverter.ToUInt32(t.Objd, 0).ToString("X") +
                        ", footprint " + (t.Ftpt != null) + ", lights " + (t.Lite != null));
                    return t;
                }
                catch (Exception ex) { Log.Error("Distant terrain template '" + names[ci] + "' unusable", ex); }
            }
            note = "no installed distant terrain object could be read to copy (" + cands.Count + " in CAW's catalog, " + KnownDistant.Length + " EA ones tried)";
            return null;
        }

        private static readonly ulong[] KnownDistant = { 0x4C9, 0x98A90C, 0x98A350, 0x9898AB, 0x9898AC, 0x989DFB, 0x98D1C6, 0x98B438,
            0x98DAF6, 0x98DEBB, 0xF56C3, 0xF5944, 0xF5B43, 0xF5E6F };

        private static int Rank(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (n.Contains("ep") || n.Contains("dot") || n.Contains("store")) return 2;
            return n == "terraindistant" || n == "distant terrain" ? 0 : 1;
        }

        private static ulong[] FirstTgi(byte[] d, int start, uint type)
        {
            int n = BitConverter.ToInt32(d, start);
            for (int k = 0; k < n && start + 4 + 16 * (k + 1) <= d.Length; k++)
            {
                int o = start + 4 + 16 * k;
                if (BitConverter.ToUInt32(d, o) == type) return new ulong[] { type, BitConverter.ToUInt32(d, o + 4), BitConverter.ToUInt64(d, o + 8) };
            }
            return null;
        }

        private static byte[] Find(MethodInfo find, Type keyType, ulong[] k)
        {
            // Keys in object files sometimes carry flags in the group's top byte; try as-is, then without them.
            uint[] groups = { (uint)k[1], (uint)k[1] & 0x00FFFFFFu };
            foreach (uint g in groups)
            {
                try
                {
                    object key = Activator.CreateInstance(keyType, new object[] { (uint)k[0], g, k[2] });
                    byte[] d = find.Invoke(null, new[] { key }) as byte[];
                    if (d != null && d.Length > 0) return d;
                }
                catch { }
                if (groups[0] == groups[1]) break;
            }
            return null;
        }

        /// <summary>Resource keys referenced by an RCOL resource: its internal and external lists (instance u64, type, group).</summary>
        private static List<ulong[]> References(byte[] d)
        {
            List<ulong[]> list = new List<ulong[]>();
            if (d.Length < 20) return list;
            uint internalCount = BitConverter.ToUInt32(d, 16), externalCount = BitConverter.ToUInt32(d, 12);
            long n = (long)internalCount + externalCount;
            if (n <= 0 || n > 4096 || 20 + n * 16 > d.Length) return list;
            for (int i = 0; i < n; i++)
            {
                int o = 20 + i * 16;
                list.Add(new ulong[] { BitConverter.ToUInt32(d, o + 8), BitConverter.ToUInt32(d, o + 12), BitConverter.ToUInt64(d, o) });
            }
            return list;
        }

        private static string KeyText(ulong[] k) { return string.Format("{0:X8}:{1:X8}:{2:X16}", k[0], k[1], k[2]); }
    }
}
