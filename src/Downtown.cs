using System;
using System.Collections.Generic;
using System.Globalization;

namespace CawPlus
{
    /// <summary>
    /// The downtown block grid of an EA world (tools/downtown.py, worlds.txt column 39): straight streets along two
    /// directions at right angles round the town core, how many each way, how far apart and how long, and how many roads
    /// leave the town. the tester's mark-up of Sunset Valley, Barnacle Bay and Appaloosa Plains (2026-10-24): a centre about
    /// 300 m across (4-6 ha) holding 13-28% of the lots, main roads running straight through it and out (67-79% of main
    /// road radial), small neighbourhoods (1-6 ha) of one street or a cul-de-sac 100-500 m out. RoadPlanner lays its
    /// downtown to this grid; the downtown check compares a plan with it.
    /// </summary>
    internal sealed class DowntownGrid
    {
        public int StreetsA = 3, StreetsB = 3;       // straight streets along each direction
        public float SpacingA = 80, SpacingB = 80;   // metres between neighbouring streets
        public float LengthA = 140, LengthB = 140;   // median street length within the core, metres
        public int RoadsOut = 5;                     // road directions leaving the town (300 m from the core)
        public float GridShare = 0.6f;               // core road length within 10 deg of the two directions

        /// <summary>"A,B,spA,spB,lenA,lenB,out,grid"; null if empty or malformed.</summary>
        public static DowntownGrid Parse(string v)
        {
            if (string.IsNullOrEmpty(v)) return null;
            string[] p = v.Split(',');
            if (p.Length < 8) return null;
            CultureInfo ic = CultureInfo.InvariantCulture;
            DowntownGrid g = new DowntownGrid();
            g.StreetsA = int.Parse(p[0], ic); g.StreetsB = int.Parse(p[1], ic);
            g.SpacingA = float.Parse(p[2], ic); g.SpacingB = float.Parse(p[3], ic);
            g.LengthA = float.Parse(p[4], ic); g.LengthB = float.Parse(p[5], ic);
            g.RoadsOut = int.Parse(p[6], ic); g.GridShare = float.Parse(p[7], ic);
            return g;
        }

        /// <summary>The median of each figure over several towns (worlds whose roads and lots weren't measured).</summary>
        public static DowntownGrid Typical(List<DowntownGrid> all)
        {
            if (all.Count == 0) return null;
            DowntownGrid g = new DowntownGrid();
            List<float> v = new List<float>();
            v.Clear(); foreach (DowntownGrid d in all) v.Add(d.StreetsA); g.StreetsA = (int)Median(v);
            v.Clear(); foreach (DowntownGrid d in all) v.Add(d.StreetsB); g.StreetsB = (int)Median(v);
            v.Clear(); foreach (DowntownGrid d in all) if (d.SpacingA > 0) v.Add(d.SpacingA); g.SpacingA = Median(v);
            v.Clear(); foreach (DowntownGrid d in all) if (d.SpacingB > 0) v.Add(d.SpacingB); g.SpacingB = Median(v);
            v.Clear(); foreach (DowntownGrid d in all) v.Add(d.LengthA); g.LengthA = Median(v);
            v.Clear(); foreach (DowntownGrid d in all) v.Add(d.LengthB); g.LengthB = Median(v);
            v.Clear(); foreach (DowntownGrid d in all) v.Add(d.RoadsOut); g.RoadsOut = (int)Median(v);
            v.Clear(); foreach (DowntownGrid d in all) v.Add(d.GridShare); g.GridShare = Median(v);
            return g;
        }

        // ---------- the grid to lay ----------

        /// <summary>Streets along the main street (2-6) and their spacing (45-160 m; the world's, else the other way's, else 80 m).</summary>
        /// <summary>A block grid at all: at least two streets each way (Sunlit Tides, Midnight Hollow, Isla Paradiso and
        /// Al Simhara have none: their centres are a few roads, not a grid).</summary>
        public bool HasGrid { get { return StreetsA >= 2 && StreetsB >= 2; } }
        public int LayA { get { return Math.Max(2, Math.Min(6, StreetsA)); } }
        public int LayB { get { return Math.Max(2, Math.Min(6, StreetsB)); } }
        public float LaySpacingA { get { return Clamp(SpacingA > 0 ? SpacingA : SpacingB > 0 ? SpacingB : 80, 45, 160); } }
        public float LaySpacingB { get { return Clamp(SpacingB > 0 ? SpacingB : SpacingA > 0 ? SpacingA : 80, 45, 160); } }
        /// <summary>Roads leaving the town, 3-8 (EA: 1-8, median 5).</summary>
        public int LayOut { get { return Math.Max(3, Math.Min(8, RoadsOut)); } }

        // ---------- measuring a plan ----------

        /// <summary>A plan's downtown, measured as tools/downtown.py measures EA's, round 'core' (metres).</summary>
        public static DowntownGrid Measure(RoadNet net, float[] core)
        {
            const float R = 230;
            List<float[]> segs = new List<float[]>(); // mid x, z, angle (deg, 0-180), length
            double[] hist = new double[90]; double tot = 0;
            List<float> outAngles = new List<float>();
            foreach (Road r in net.Roads)
            {
                List<float[]> p = Resample(r.Points, 4);
                for (int i = 1; i < p.Count; i++)
                {
                    float[] a = p[i - 1], b = p[i];
                    float len = Dist(a, b);
                    if (len < 1e-3f) continue;
                    float mx = (a[0] + b[0]) / 2, mz = (a[1] + b[1]) / 2;
                    float da = Dist(a, core), db = Dist(b, core);
                    if ((da - 300) * (db - 300) < 0) outAngles.Add((float)(((Math.Atan2(a[1] - core[1], a[0] - core[0]) * 180 / Math.PI) + 360) % 360));
                    if (Dist(new[] { mx, mz }, core) > R) continue;
                    float ang = (float)(((Math.Atan2(b[1] - a[1], b[0] - a[0]) * 180 / Math.PI) % 180 + 180) % 180);
                    segs.Add(new[] { mx, mz, ang, len });
                    hist[((int)ang) % 90] += len; tot += len;
                }
            }
            DowntownGrid g = new DowntownGrid();
            if (tot <= 0) { g.StreetsA = g.StreetsB = 0; g.RoadsOut = 0; g.GridShare = 0; return g; }
            int best = 0; double bv = -1;
            for (int cc = 0; cc < 90; cc++) { double s = 0; for (int k = -8; k <= 8; k++) s += hist[((cc + k) % 90 + 90) % 90]; if (s > bv) { bv = s; best = cc; } }
            double gs = 0; for (int k = -10; k <= 10; k++) gs += hist[((best + k) % 90 + 90) % 90];
            g.GridShare = (float)(gs / tot);
            for (int axis = 0; axis < 2; axis++)
            {
                double th = (best + 90 * axis) * Math.PI / 180;
                float nx = -(float)Math.Sin(th), nz = (float)Math.Cos(th);
                List<float[]> offs = new List<float[]>();
                foreach (float[] s in segs)
                {
                    float d = Math.Abs(((s[2] - (best + 90 * axis)) % 180 + 270) % 180 - 90);
                    if (d <= 10) offs.Add(new[] { (s[0] - core[0]) * nx + (s[1] - core[1]) * nz, s[3] });
                }
                offs.Sort(delegate (float[] x, float[] y) { return x[0].CompareTo(y[0]); });
                List<float[]> cl = new List<float[]>(); // offset, length
                foreach (float[] o in offs)
                {
                    if (cl.Count > 0 && o[0] - cl[cl.Count - 1][0] < 15) { cl[cl.Count - 1][0] = o[0]; cl[cl.Count - 1][1] += o[1]; }
                    else cl.Add(new[] { o[0], o[1] });
                }
                List<float> pos = new List<float>(), lens = new List<float>();
                foreach (float[] c in cl) if (c[1] >= 50) { pos.Add(c[0]); lens.Add(c[1]); }
                List<float> sp = new List<float>();
                for (int i = 1; i < pos.Count; i++) if (pos[i] - pos[i - 1] < 200) sp.Add(pos[i] - pos[i - 1]);
                if (axis == 0) { g.StreetsA = pos.Count; g.SpacingA = Median(sp); g.LengthA = Median(lens); }
                else { g.StreetsB = pos.Count; g.SpacingB = Median(sp); g.LengthB = Median(lens); }
            }
            outAngles.Sort();
            int groups = 0; float last = -99;
            foreach (float a in outAngles) { if (a - last > 20) groups++; last = a; }
            if (groups > 1 && outAngles[0] + 360 - outAngles[outAngles.Count - 1] <= 20) groups--;
            g.RoadsOut = groups;
            return g;
        }

        /// <summary>Credit for the share of road running round the town: full at 0.15-0.45 (EA median 0.30), rising from 0
        /// at none (a town with no road round it isn't EA's shape either; ChatGPT's review on #123), falling to 0 at 0.75.</summary>
        public static float RoundCredit(float round)
        {
            return round < 0.15f ? Math.Max(0, round / 0.15f) : Math.Max(0, 1 - Math.Max(0, round - 0.45f) / 0.3f);
        }

        /// <summary>Share of the road 120-500 m from 'core' that runs round it rather than out from it (EA median 0.30).</summary>
        public static float RoundShare(RoadNet net, float[] core)
        {
            double tan = 0, tot = 0;
            foreach (Road r in net.Roads)
                for (int i = 1; i < r.Points.Count; i++)
                {
                    float[] a = r.Points[i - 1], b = r.Points[i];
                    float len = Dist(a, b);
                    float[] m = { (a[0] + b[0]) / 2, (a[1] + b[1]) / 2 };
                    float d = Dist(m, core);
                    if (d < 120 || d > 500 || len < 1e-3f) continue;
                    float c = Math.Abs(((m[0] - core[0]) * (b[0] - a[0]) + (m[1] - core[1]) * (b[1] - a[1])) / (d * len));
                    tot += len; if (c < 0.45f) tan += len;
                }
            return tot > 0 ? (float)(tan / tot) : 0;
        }

        /// <summary>
        /// How close a measured plan's downtown is to this one, 0..1: streets each way (1 apart = 0.5), their spacing (0 at
        /// twice or half), grid share (0 at 0.4 apart), roads out (0 at 4 apart) and how much road runs round the town
        /// (RoundCredit: full at 0.15-0.45, 0 at none or 0.75). 'missed' names the parts under 0.6.
        /// </summary>
        public float Score(DowntownGrid got, float round, out string missed)
        {
            int ea = StreetsA + StreetsB, gt = got.StreetsA + got.StreetsB;
            // a world type without a grid (Sunlit Tides 1 x 1) is matched by having none: its street count as measured,
            // and no block size to compare (half credit if the plan laid a grid anyway)
            bool gridless = !HasGrid;
            float[] c = {
                Math.Max(0, 1 - Math.Abs(gt - (gridless ? ea : Math.Max(4, ea))) / 4f),
                gridless ? (got.HasGrid ? 0.5f : 1f) : Ratio(Avg(got.SpacingA, got.SpacingB), Avg(LaySpacingA, LaySpacingB)),
                Math.Max(0, 1 - Math.Abs(got.GridShare - GridShare) / 0.4f),
                Math.Max(0, 1 - Math.Abs(got.RoadsOut - RoadsOut) / 4f),
                RoundCredit(round) };
            string[] names = { "downtown streets", "block size", "grid", "roads out of town", "roads round the town" };
            missed = "";
            float sum = 0;
            for (int i = 0; i < c.Length; i++) { sum += c[i]; if (c[i] < 0.6f) missed += (missed.Length > 0 ? ", " : "") + names[i]; }
            return sum / c.Length;
        }

        /// <summary>The downtown check for PlanChecked (pass at 0.7), measured round the plan's town centre.</summary>
        public void AddCheck(RoadChecks.Result res, RoadNet net)
        {
            if (net.Centre == null) return;
            string missed;
            double sc = Score(Measure(net, net.Centre), RoundShare(net, net.Centre), out missed);
            res.Score = (res.Score * res.Total + sc) / (res.Total + 1);
            res.Total++;
            if (sc >= 0.7) res.Passed++; else res.Failed.Add("downtown grid");
        }

        public string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} x {1} streets, {2:F0} x {3:F0} m apart, grid {4:P0}, {5} roads out",
                StreetsA, StreetsB, SpacingA, SpacingB, GridShare, RoadsOut);
        }

        private static float Avg(float a, float b) { return a > 0 && b > 0 ? (a + b) / 2 : Math.Max(a, b); }
        private static float Ratio(float a, float b)
        {
            if (a <= 0 || b <= 0) return 0;
            return Math.Max(0, 1 - Math.Abs((float)Math.Log(a / b)) / (float)Math.Log(2));
        }
        private static float Clamp(float v, float lo, float hi) { return v < lo ? lo : v > hi ? hi : v; }
        private static float Median(List<float> v) { if (v.Count == 0) return 0; v.Sort(); return v[v.Count / 2]; }
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
            return o;
        }
    }
}
