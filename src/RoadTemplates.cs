using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CawPlus
{
    /// <summary>
    /// One district of an EA world as a road layout template (#137, tools/roadtemplates.py): its streets and lots in local
    /// metres (origin at the middle of its lots, x along their main axis), where its roads leave it, and tags.
    /// </summary>
    internal sealed class RoadTemplate
    {
        public string World = "", Pattern = "", Region = "";
        public char Kind = 'D';   // D district (road_templates.txt), J junction, C connector (road_pieces.txt: the roads between districts)
        public int Number, LotCount;
        public bool Core;
        public float Km;   // road km (the file also holds length, width, slope, relief, height above the sea, distance from the core, grid share)
        public readonly List<List<float[]>> Roads = new List<List<float[]>>();
        public readonly List<float[]> Lots = new List<float[]>();   // x, z, ux, uz, frontage, depth, community (1/0)
        public readonly List<float[]> Ports = new List<float[]>();  // x, z, dx, dz
        /// <summary>The furthest road point from the middle, metres.</summary>
        public float Reach;
        public string Name { get { return Kind == 'D' ? World + " " + Number + " (" + Pattern + ")" : World + " " + Kind + Number; } }
    }

    internal static class RoadTemplates
    {
        /// <summary>A folder holding road_templates.txt to read as well (tools/Fidelity sets it to the repo's library).</summary>
        public static string Dir = null;

        private static List<RoadTemplate> all, pieces;
        private static readonly object gate = new object();

        /// <summary>Every template: built into the exe, then a Library folder next to it, then Dir (a later file replaces).</summary>
        public static List<RoadTemplate> All()
        {
            lock (gate) { if (all == null) all = Load("road_templates.txt"); return all; }
        }

        /// <summary>Every junction and connector piece (road_pieces.txt, tools/roadpieces.py), loaded as All().</summary>
        public static List<RoadTemplate> Pieces()
        {
            lock (gate) { if (pieces == null) pieces = Load("road_pieces.txt"); return pieces; }
        }

        private static List<RoadTemplate> Load(string file)
        {
            string[] lines = null;
            foreach (KeyValuePair<string, string[]> e in Embedded.Files(file)) lines = e.Value;
            foreach (string f in new[] { Path.Combine(Path.Combine(WorldPreset.BaseDir, "Library"), file),
                                         Dir != null ? Path.Combine(Dir, file) : null })
                if (f != null && File.Exists(f)) lines = File.ReadAllLines(f);
            return lines != null ? Parse(lines) : new List<RoadTemplate>();
        }

        public static List<RoadTemplate> Parse(string[] lines)
        {
            List<RoadTemplate> list = new List<RoadTemplate>();
            RoadTemplate t = null;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                try
                {
                    if (k == "template")
                    {
                        string[] p = v.Split('|');
                        if (p.Length < 14) { t = null; continue; }
                        t = new RoadTemplate();
                        t.World = p[0]; t.Number = int.Parse(p[1], CultureInfo.InvariantCulture); t.Core = p[2] == "1"; t.Pattern = p[3]; t.Region = p[4];
                        t.LotCount = int.Parse(p[5], CultureInfo.InvariantCulture); t.Km = F(p[6]);
                        list.Add(t);
                    }
                    else if (k == "piece")
                    {
                        // kind|world|n|region|road km|slope|turn|lots near
                        string[] p = v.Split('|');
                        if (p.Length < 8) { t = null; continue; }
                        t = new RoadTemplate();
                        t.Kind = p[0].Length > 0 ? p[0][0] : 'C'; t.World = p[1]; t.Number = int.Parse(p[2], CultureInfo.InvariantCulture);
                        t.Region = p[3]; t.Km = F(p[4]); t.Pattern = t.Kind == 'J' ? "junction" : "connector";
                        list.Add(t);
                    }
                    else if (t == null) continue;
                    else if (k == "road")
                    {
                        List<float[]> r = new List<float[]>();
                        foreach (string q in v.Split(';')) { string[] xy = q.Split(','); r.Add(new[] { F(xy[0]), F(xy[1]) }); }
                        if (r.Count < 2) continue;
                        t.Roads.Add(r);
                        foreach (float[] q in r) t.Reach = Math.Max(t.Reach, (float)Math.Sqrt(q[0] * q[0] + q[1] * q[1]));
                    }
                    else if (k == "lot" || k == "port")
                    {
                        string[] p = v.Split(',');
                        float[] a = new float[p.Length];
                        for (int i = 0; i < p.Length; i++) a[i] = F(p[i]);
                        (k == "lot" ? t.Lots : t.Ports).Add(a);
                    }
                }
                catch (FormatException) { } // a bad line is skipped, not the file
            }
            list.RemoveAll(delegate (RoadTemplate q) { return q.Roads.Count == 0; });
            return list;
        }

        private static float F(string s) { return float.Parse(s, CultureInfo.InvariantCulture); }
    }
}
