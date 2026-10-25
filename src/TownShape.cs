using System;
using System.Collections.Generic;
using System.Globalization;

namespace CawPlus
{
    /// <summary>
    /// How an EA town spreads out from its core (tools/townshape.py, worlds.txt column 38): the distances from the core
    /// that hold 10%, 20% .. 100% of its lots and of its road length, and the share of its road with no lot within 70 m
    /// (country road). EA's towns are compact: half the lots within ~160-350 m of the core, ~40% of the road within
    /// 300 m, and long empty roads beyond. The planner adds side streets where the road spread falls short and picks
    /// lots to the lot spread (RoadPlanner.Infill, LotPlanner.Place); the town shape check compares a plan with it.
    /// </summary>
    internal sealed class TownShape
    {
        public float[] LotDec = new float[10];   // metres from the core holding 10..100 % of the lots
        public float[] RoadDec = new float[10];  // metres from the core holding 10..100 % of the road length
        public float Country = 0.3f;             // share of road length with no lot within 70 m
        public float WorldM = 2048;              // the world's size, metres

        /// <summary>"l1..l10;r1..r10;country;worldM"; null if empty or malformed.</summary>
        public static TownShape Parse(string v)
        {
            if (string.IsNullOrEmpty(v)) return null;
            string[] p = v.Split(';');
            if (p.Length < 3) return null;
            CultureInfo ic = CultureInfo.InvariantCulture;
            string[] a = p[0].Split(','), b = p[1].Split(',');
            if (a.Length != 10 || b.Length != 10) return null;
            TownShape t = new TownShape();
            for (int i = 0; i < 10; i++) { t.LotDec[i] = float.Parse(a[i], ic); t.RoadDec[i] = float.Parse(b[i], ic); }
            t.Country = float.Parse(p[2], ic);
            if (p.Length > 3) t.WorldM = float.Parse(p[3], ic);
            return t;
        }

        /// <summary>The median of each figure over several towns (worlds whose lots weren't measured).</summary>
        public static TownShape Typical(List<TownShape> all)
        {
            if (all.Count == 0) return null;
            TownShape t = new TownShape();
            List<float> v = new List<float>();
            for (int i = 0; i < 10; i++)
            {
                v.Clear(); foreach (TownShape s in all) v.Add(s.LotDec[i]); t.LotDec[i] = Median(v);
                v.Clear(); foreach (TownShape s in all) v.Add(s.RoadDec[i]); t.RoadDec[i] = Median(v);
            }
            v.Clear(); foreach (TownShape s in all) v.Add(s.Country); t.Country = Median(v);
            return t;
        }

        /// <summary>A copy with every distance times 'k' (a town with more or fewer lots than the EA world spreads further or less).</summary>
        public TownShape Scaled(float k)
        {
            TownShape t = new TownShape(); t.Country = Country; t.WorldM = WorldM;
            for (int i = 0; i < 10; i++) { t.LotDec[i] = LotDec[i] * k; t.RoadDec[i] = RoadDec[i] * k; }
            return t;
        }

        /// <summary>The scale for a town of 'lots' lots against this world's 'eaLots': the square root of the ratio, 0.6-1.3.</summary>
        public static float ScaleFor(int lots, int eaLots)
        {
            if (lots <= 0 || eaLots <= 0) return 1;
            return Math.Max(0.6f, Math.Min(1.3f, (float)Math.Sqrt((double)lots / eaLots)));
        }

        /// <summary>Which tenth of the spread distance 'd' falls in (0-9), or 10 beyond the last.</summary>
        public static int Band(float[] dec, float d)
        {
            for (int i = 0; i < 10; i++) if (d <= dec[i]) return i;
            return 10;
        }

        // ---------- measuring a plan ----------

        /// <summary>The densest 200 m disc of the lot centres (as tools/townshape.py); 'fallback' without lots.</summary>
        public static float[] Core(List<float[]> lots, float[] fallback)
        {
            if (lots.Count == 0) return fallback;
            float[] best = lots[0]; int bs = -1;
            foreach (float[] p in lots)
            {
                int s = 0;
                foreach (float[] q in lots) if ((p[0] - q[0]) * (p[0] - q[0]) + (p[1] - q[1]) * (p[1] - q[1]) < 200 * 200) s++;
                if (s > bs) { bs = s; best = p; }
            }
            return best;
        }

        /// <summary>The same figures measured on a plan (its lots and roads, metres), from the plan's own town centre: the
        /// planner lays the downtown and fills the road bands round it, so lots gathering anywhere else count against it.
        /// EA's worlds have no planned centre; their core is the densest 200 m disc of lots (Core), which is their downtown.</summary>
        public static TownShape Measure(RoadNet net)
        {
            List<float[]> lots = new List<float[]>();
            foreach (PlannedLot l in net.Lots) lots.Add(l.Centre);
            float[] core = net.Centre ?? Core(lots, new float[] { 0, 0 });
            List<float> ld = new List<float>(); List<float> lw = new List<float>();
            foreach (float[] p in lots) { ld.Add(Dist(core, p)); lw.Add(1); }
            List<float> rd = new List<float>(), rw = new List<float>();
            float country = 0, total = 0;
            foreach (Road r in net.Roads)
                for (int i = 1; i < r.Points.Count; i++)
                {
                    float[] a = r.Points[i - 1], b = r.Points[i];
                    float len = Dist(a, b);
                    int n = Math.Max(1, (int)(len / 10));
                    for (int k = 0; k < n; k++)
                    {
                        float t = (k + 0.5f) / n;
                        float[] m = { a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t };
                        rd.Add(Dist(core, m)); rw.Add(len / n); total += len / n;
                        bool near = false;
                        foreach (float[] p in lots) if ((p[0] - m[0]) * (p[0] - m[0]) + (p[1] - m[1]) * (p[1] - m[1]) < 70 * 70) { near = true; break; }
                        if (!near) country += len / n;
                    }
                }
            TownShape s = new TownShape();
            s.LotDec = Deciles(ld, lw); s.RoadDec = Deciles(rd, rw);
            s.Country = total > 0 ? country / total : 0;
            return s;
        }

        /// <summary>
        /// How close a measured plan is to this shape, 0..1: the lot spread at 50 % and 90 %, the road spread at 50 % and
        /// 90 % (each 1 when equal, 0 at twice or half), and the country road share (0 at 0.4 apart). 'missed' names the
        /// figures under 0.6.
        /// </summary>
        public float Score(TownShape got, out string missed)
        {
            float[] c = { Close(got.LotDec[4], LotDec[4]), Close(got.LotDec[8], LotDec[8]), Close(got.RoadDec[4], RoadDec[4]),
                          Close(got.RoadDec[8], RoadDec[8]), Math.Max(0, 1 - Math.Abs(got.Country - Country) / 0.4f) };
            string[] names = { "lots spread (half)", "lots spread (90 %)", "roads spread (half)", "roads spread (90 %)", "country roads" };
            missed = "";
            float sum = 0;
            for (int i = 0; i < c.Length; i++) { sum += c[i]; if (c[i] < 0.6f) missed += (missed.Length > 0 ? ", " : "") + names[i]; }
            return sum / c.Length;
        }

        /// <summary>The town spread check for PlanChecked: this shape scaled to the plan's lots against the Like world's
        /// 'eaLots', passed at a score of 0.7 (each figure within about 1.3x, country roads within 0.12).</summary>
        public void AddCheck(RoadChecks.Result res, RoadNet net, int eaLots)
        {
            if (net.Lots.Count == 0) return;
            string missed;
            double sc = Scaled(ScaleFor(net.LotsWanted, eaLots)).Score(Measure(net), out missed);
            res.Score = (res.Score * res.Total + sc) / (res.Total + 1);
            res.Total++;
            if (sc >= 0.7) res.Passed++; else res.Failed.Add("town spread");
        }

        public string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "lots half within {0:F0} m, 90% within {1:F0} m; roads half within {2:F0} m, 90% within {3:F0} m; country roads {4:F0}%",
                LotDec[4], LotDec[8], RoadDec[4], RoadDec[8], Country * 100);
        }

        private static float Close(float a, float b)
        {
            if (a <= 0 || b <= 0) return 0;
            return Math.Max(0, 1 - Math.Abs((float)Math.Log(a / b)) / (float)Math.Log(2));
        }

        private static float[] Deciles(List<float> v, List<float> w)
        {
            float[] o = new float[10];
            if (v.Count == 0) return o;
            int[] idx = new int[v.Count];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            float[] keys = v.ToArray();
            Array.Sort(keys, idx);
            float tot = 0; foreach (float x in w) tot += x;
            float acc = 0; int k = 0;
            for (int i = 0; i < idx.Length && k < 10; i++)
            {
                acc += w[idx[i]];
                while (k < 10 && acc >= tot * (k + 1) / 10f - 1e-6f) { o[k] = keys[i]; k++; }
            }
            for (; k < 10; k++) o[k] = keys[keys.Length - 1];
            return o;
        }

        private static float Median(List<float> v) { v.Sort(); return v.Count == 0 ? 0 : v[v.Count / 2]; }
        private static float Dist(float[] a, float[] b) { return (float)Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1])); }
    }
}
