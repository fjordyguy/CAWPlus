using System;
using System.Collections.Generic;
using System.Globalization;

namespace CawPlus
{
    /// <summary>
    /// Where EA's roads go on the land, learned from the 22 EA worlds (tools/roadlearn/roadcost.py -> library/road_cost.txt):
    /// per terrain feature, the log of how much likelier a road is in each band than on land overall. Held out a world at a
    /// time it tells road from land with AUC 0.81 (0.65 Sunlit Tides .. 0.96 Midnight Hollow). EA's roads keep to gentle,
    /// lowish ground, on shelves a few metres above the land round them (not in hollows, not on ridges), 30-200 m from water.
    /// The map-edge feature is learned too but not used: routes already keep off the rim, and edge roads must cross it.
    /// </summary>
    internal static class RoadCost
    {
        private static readonly string[] Names = { "slope", "rank", "ridge", "water" };
        private static float[][] edges, logs;

        /// <summary>Reads road_cost.txt lines ("feature|edges|log ratios").</summary>
        public static void Load(string[] lines)
        {
            float[][] e = new float[Names.Length][], l = new float[Names.Length][];
            foreach (string raw in lines)
            {
                string t = raw.Trim();
                if (t.Length == 0 || t[0] == '#') continue;
                string[] p = t.Split('|');
                if (p.Length < 3) continue;
                int k = Array.IndexOf(Names, p[0]);
                if (k < 0) continue;
                e[k] = Floats(p[1]); l[k] = Floats(p[2]);
            }
            for (int k = 0; k < Names.Length; k++) if (e[k] == null || l[k] == null || l[k].Length != e[k].Length - 1) return;
            edges = e; logs = l;
        }

        private static float[] Floats(string s)
        {
            string[] a = s.Split(',');
            float[] f = new float[a.Length];
            for (int i = 0; i < a.Length; i++) f[i] = float.Parse(a[i], CultureInfo.InvariantCulture);
            return f;
        }

        private static void LoadEmbedded()
        {
            if (edges != null) return;
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, string[]> f in Embedded.Files("road_cost.txt")) lines.AddRange(f.Value);
            if (lines.Count > 0) Load(lines.ToArray());
        }

        /// <summary>
        /// A cost multiplier per planning cell: exp(-strength x summed log ratio), 0.4-3; null when the table isn't there.
        /// 'land' = cells above the sea, 'shore' = metres to water, 'ridge' = metres above the 150 m smoothed ground.
        /// </summary>
        public static float[] Multiplier(Grid h, float cell, bool[] land, float[] shore, float[] ridge, float strength)
        {
            LoadEmbedded();
            if (edges == null || strength <= 0) return null;
            int n = h.W, N = n * n;
            Grid s40 = h.Blur(40f / cell);
            // height rank among the land cells
            List<float> lh = new List<float>();
            for (int i = 0; i < N; i++) if (land[i]) lh.Add(h.D[i]);
            lh.Sort();
            float[] m = new float[N];
            for (int i = 0; i < N; i++)
            {
                if (!land[i]) { m[i] = 1; continue; }
                int x = i % n, y = i / n;
                float gx = (s40.D[y * n + Math.Min(n - 1, x + 1)] - s40.D[y * n + Math.Max(0, x - 1)]) / (2 * cell);
                float gy = (s40.D[Math.Min(n - 1, y + 1) * n + x] - s40.D[Math.Max(0, y - 1) * n + x]) / (2 * cell);
                float slope = (float)Math.Sqrt(gx * gx + gy * gy);
                int lo = 0, hi = lh.Count;
                while (lo < hi) { int mid = (lo + hi) / 2; if (lh[mid] < h.D[i]) lo = mid + 1; else hi = mid; }
                float rank = lh.Count > 1 ? (float)lo / (lh.Count - 1) : 0;
                float s = Log(0, slope) + Log(1, rank) + Log(2, ridge[i]) + Log(3, shore[i]);
                m[i] = Math.Max(0.4f, Math.Min(3f, (float)Math.Exp(-strength * s)));
            }
            return m;
        }

        private static float Log(int k, float v)
        {
            float[] e = edges[k];
            int b = 0;
            while (b < e.Length - 2 && v >= e[b + 1]) b++;
            return logs[k][b];
        }
    }
}
