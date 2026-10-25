using System;
using System.Collections.Generic;
using System.Globalization;

namespace CawPlus
{
    /// <summary>
    /// A world's road design profile (#97; tools/roadprofile.py, worlds.txt column 40): the share of its road length in
    /// each class of why a stretch runs where it does. Per 16 m stretch, the first that applies:
    ///   town     within 230 m of the town core (densest 200 m disc of lots)
    ///   coast    within 70 m of the sea and running along the shore (within 30 deg of the shoreline)
    ///   climb    grade over 7 %
    ///   contour  across a hillside: land slope over 8 % (40 m smoothed), the road climbing less than 40 % of it
    ///   valley   on a valley floor: 12 m or more below the mean ground within 150 m, land slope under 8 %
    ///   rural    no lot within 70 m
    ///   street   the rest
    /// EA (22 worlds): Isla Paradiso and Roaring Heights 37% coast, Shang Simla 44% climb, Lunar Lakes 39% contour,
    /// Midnight Hollow 26% valley, Lucky Palms 29% rural and 27% contour round its buttes, Aurora Skies 36% town.
    /// The plan's profile is checked against the Like world's (PlanChecked) and scored in tools/Fidelity.
    /// </summary>
    internal sealed class RoadProfile
    {
        public static readonly string[] Names = { "town", "coast", "climb", "contour", "valley", "rural", "street" };
        public float[] Share = new float[7];

        /// <summary>"town,coast,climb,contour,valley,rural,street" in percent; null if empty or malformed.</summary>
        public static RoadProfile Parse(string v)
        {
            if (string.IsNullOrEmpty(v)) return null;
            string[] p = v.Split(',');
            if (p.Length < 7) return null;
            RoadProfile r = new RoadProfile();
            for (int i = 0; i < 7; i++) r.Share[i] = float.Parse(p[i], CultureInfo.InvariantCulture) / 100f;
            return r;
        }

        public static RoadProfile Typical(List<RoadProfile> all)
        {
            if (all.Count == 0) return null;
            RoadProfile r = new RoadProfile();
            float sum = 0;
            for (int i = 0; i < 7; i++)
            {
                List<float> v = new List<float>();
                foreach (RoadProfile a in all) v.Add(a.Share[i]);
                v.Sort(); r.Share[i] = v[v.Count / 2]; sum += r.Share[i];
            }
            for (int i = 0; i < 7; i++) r.Share[i] /= Math.Max(1e-6f, sum);
            return r;
        }

        /// <summary>A plan's profile on heights 'h' (rows = z) of 'cell' metres.</summary>
        public static RoadProfile Measure(RoadNet net, Grid h, float cell, float sea)
        {
            List<float[]> lots = new List<float[]>();
            foreach (PlannedLot l in net.Lots) lots.Add(l.Centre);
            float[] core = net.Centre ?? TownShape.Core(lots, new[] { h.W * cell / 2, h.H * cell / 2 }); // the plan's town centre, as TownShape.Measure
            float[] seaD = SeaDistance(h, cell, sea);
            Grid hs = h.Blur(20f / cell), mean = h.Blur(87f / cell);
            Dictionary<long, List<float[]>> lotHash = new Dictionary<long, List<float[]>>();
            foreach (float[] p in lots) { long k = Key(p[0], p[1]); List<float[]> l; if (!lotHash.TryGetValue(k, out l)) { l = new List<float[]>(); lotHash[k] = l; } l.Add(p); }
            double[] len = new double[7]; double tot = 0;
            foreach (Road r in net.Roads)
            {
                List<float[]> p = Resample(r.Points, 16);
                for (int i = 1; i < p.Count; i++)
                {
                    float[] a = p[i - 1], b = p[i];
                    float L = Dist(a, b);
                    if (L < 1e-3f) continue;
                    float mx = (a[0] + b[0]) / 2, mz = (a[1] + b[1]) / 2;
                    float tx = (b[0] - a[0]) / L, tz = (b[1] - a[1]) / L;
                    float grade = Math.Abs(At(h, b[0], b[1], cell) - At(h, a[0], a[1], cell)) / L;
                    float sx = (At(hs, mx + cell, mz, cell) - At(hs, mx - cell, mz, cell)) / (2 * cell), sz = (At(hs, mx, mz + cell, cell) - At(hs, mx, mz - cell, cell)) / (2 * cell);
                    float slope = (float)Math.Sqrt(sx * sx + sz * sz);
                    int c;
                    if (Dist(new[] { mx, mz }, core) < 230) c = 0;
                    else
                    {
                        float sd = AtD(seaD, h, mx, mz, cell);
                        float gx = AtD(seaD, h, mx + cell, mz, cell) - AtD(seaD, h, mx - cell, mz, cell), gz = AtD(seaD, h, mx, mz + cell, cell) - AtD(seaD, h, mx, mz - cell, cell);
                        float gl = (float)Math.Sqrt(gx * gx + gz * gz);
                        bool along = gl > 1e-6f && Math.Abs(tx * gx / gl + tz * gz / gl) < 0.5f;
                        if (sd < 70 && along) c = 1;
                        else if (grade > 0.07f) c = 2;
                        else if (slope > 0.08f && grade < 0.4f * slope) c = 3;
                        else if (At(h, mx, mz, cell) < At(mean, mx, mz, cell) - 12 && slope < 0.08f) c = 4;
                        else if (!LotNear(lotHash, mx, mz)) c = 5;
                        else c = 6;
                    }
                    len[c] += L; tot += L;
                }
            }
            RoadProfile res = new RoadProfile();
            for (int i = 0; i < 7; i++) res.Share[i] = tot > 0 ? (float)(len[i] / tot) : 0;
            return res;
        }

        /// <summary>1 - half the summed difference of the shares (1 = the same mix, 0 = nothing in common); 'missed' names
        /// the classes more than 10 points off.</summary>
        public float Score(RoadProfile got, out string missed)
        {
            float d = 0; missed = "";
            for (int i = 0; i < 7; i++)
            {
                float e = Math.Abs(got.Share[i] - Share[i]); d += e;
                if (e > 0.1f) missed += (missed.Length > 0 ? ", " : "") + Names[i] + (got.Share[i] > Share[i] ? " (too much)" : " (too little)");
            }
            return Math.Max(0, 1 - d / 2);
        }

        /// <summary>The road design check for PlanChecked, passed at 0.7.</summary>
        public void AddCheck(RoadChecks.Result res, RoadNet net, Grid h, float cell, float sea)
        {
            string missed;
            double sc = Score(Measure(net, h, cell, sea), out missed);
            res.Score = (res.Score * res.Total + sc) / (res.Total + 1);
            res.Total++;
            if (sc >= 0.7) res.Passed++; else res.Failed.Add("road design");
        }

        public string Describe()
        {
            string s = "";
            for (int i = 0; i < 7; i++) s += (i > 0 ? ", " : "") + Names[i] + " " + (Share[i] * 100).ToString("F0", CultureInfo.InvariantCulture) + "%";
            return s;
        }

        // ---------- helpers ----------

        private static bool LotNear(Dictionary<long, List<float[]>> hash, float x, float z)
        {
            long gx = (long)Math.Floor(x / 70), gz = (long)Math.Floor(z / 70);
            for (long dx = -1; dx <= 1; dx++)
                for (long dz = -1; dz <= 1; dz++)
                {
                    List<float[]> l;
                    if (!hash.TryGetValue((gx + dx) * 1000003L + gz + dz, out l)) continue;
                    foreach (float[] p in l) if ((p[0] - x) * (p[0] - x) + (p[1] - z) * (p[1] - z) < 70 * 70) return true;
                }
            return false;
        }

        private static long Key(float x, float z) { return (long)Math.Floor(x / 70) * 1000003L + (long)Math.Floor(z / 70); }

        private static float At(Grid g, float x, float z, float cell) { return TerrainGen.Sample(g, x / cell, z / cell); }

        private static float AtD(float[] d, Grid h, float x, float z, float cell)
        {
            int i = Math.Max(0, Math.Min(h.W - 1, (int)Math.Round(x / cell))), j = Math.Max(0, Math.Min(h.H - 1, (int)Math.Round(z / cell)));
            return d[j * h.W + i];
        }

        /// <summary>Distance (m) to the sea: water below 'sea' + 0.2 joined to the map edge (chamfer 3-4).</summary>
        private static float[] SeaDistance(Grid h, float cell, float sea)
        {
            int w = h.W, n = h.H, N = w * n;
            float[] d = new float[N];
            for (int i = 0; i < N; i++) d[i] = 1e9f;
            Queue<int> q = new Queue<int>();
            float sl = sea + 0.2f;
            for (int i = 0; i < N; i++)
            {
                int x = i % w, y = i / w;
                if (h.D[i] < sl && (x == 0 || y == 0 || x == w - 1 || y == n - 1)) { d[i] = 0; q.Enqueue(i); }
            }
            while (q.Count > 0)
            {
                int i = q.Dequeue(), x = i % w, y = i / w;
                int[] nb = { x > 0 ? i - 1 : -1, x < w - 1 ? i + 1 : -1, y > 0 ? i - w : -1, y < n - 1 ? i + w : -1 };
                foreach (int j in nb) if (j >= 0 && h.D[j] < sl && d[j] > 0) { d[j] = 0; q.Enqueue(j); }
            }
            float d1 = cell, d2 = cell * 1.4142f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x; float v = d[i];
                    if (x > 0) v = Math.Min(v, d[i - 1] + d1);
                    if (y > 0) { v = Math.Min(v, d[i - w] + d1); if (x > 0) v = Math.Min(v, d[i - w - 1] + d2); if (x < w - 1) v = Math.Min(v, d[i - w + 1] + d2); }
                    d[i] = v;
                }
            for (int y = n - 1; y >= 0; y--)
                for (int x = w - 1; x >= 0; x--)
                {
                    int i = y * w + x; float v = d[i];
                    if (x < w - 1) v = Math.Min(v, d[i + 1] + d1);
                    if (y < n - 1) { v = Math.Min(v, d[i + w] + d1); if (x < w - 1) v = Math.Min(v, d[i + w + 1] + d2); if (x > 0) v = Math.Min(v, d[i + w - 1] + d2); }
                    d[i] = v;
                }
            return d;
        }

        private static float Dist(float[] a, float[] b) { return (float)Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1])); }

        private static List<float[]> Resample(List<float[]> p, float step)
        {
            List<float[]> o = new List<float[]>();
            if (p.Count == 0) return o;
            o.Add(p[0]);
            float carry = 0;
            for (int k = 1; k < p.Count; k++)
            {
                float len = Dist(p[k - 1], p[k]);
                if (len < 1e-6f) continue;
                for (float t = step - carry; t <= len; t += step) o.Add(new[] { p[k - 1][0] + (p[k][0] - p[k - 1][0]) * t / len, p[k - 1][1] + (p[k][1] - p[k - 1][1]) * t / len });
                carry = (carry + len) % step;
            }
            if (Dist(o[o.Count - 1], p[p.Count - 1]) > 1) o.Add(p[p.Count - 1]);
            return o;
        }
    }
}
