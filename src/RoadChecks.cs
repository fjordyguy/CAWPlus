using System;
using System.Collections.Generic;

namespace CawPlus
{
    /// <summary>
    /// What an EA town's road layout looks like, measured from its .world road graph (tools/roadshape.py, tools/roads.py):
    /// the targets the road checks compare a planned layout against. Stored per Like world in worlds.txt (column 32).
    /// </summary>
    internal sealed class RoadTargets
    {
        public float Km = float.NaN, WorldM = 2048;
        public int T = -1, X = -1, Caps = -1, Core = -1, Hoods = -1;
        public float Hard = float.NaN, Sharp = float.NaN, Grid = float.NaN, Parallel = float.NaN, Reach = float.NaN;

        /// <summary>"km,T,X,caps,core,hoods,hard,sharp,grid,parallel,reach,worldM" (worlds.txt column 32); null if empty.</summary>
        public static RoadTargets Parse(string v)
        {
            if (string.IsNullOrEmpty(v)) return null;
            string[] p = v.Split(',');
            if (p.Length < 12) return null;
            RoadTargets t = new RoadTargets();
            t.Km = F(p[0]); t.T = (int)F(p[1]); t.X = (int)F(p[2]); t.Caps = (int)F(p[3]); t.Core = (int)F(p[4]); t.Hoods = (int)F(p[5]);
            t.Hard = F(p[6]); t.Sharp = F(p[7]); t.Grid = F(p[8]); t.Parallel = F(p[9]); t.Reach = F(p[10]); t.WorldM = F(p[11]);
            return t;
        }

        private static float F(string s)
        {
            float v;
            return float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : float.NaN;
        }
    }

    /// <summary>The measured shape of a planned layout, the same way roadshape.py measures EA's.</summary>
    internal sealed class RoadShape
    {
        public float Km, Hard, Sharp, Grid, Parallel, Reach;
        public int T, X, Dead, Core, Hoods, Pieces;
        public bool Connected;
    }

    /// <summary>
    /// The road checklist (the tester, 2026-10-21: "a pass system for the roads to meet criteria"): a planned layout is
    /// measured as EA's towns were and has to tick off ten points against the Like world's own figures, with tolerances
    /// set from how much EA's 23 worlds vary. RoadPlanner.PlanChecked tries several layouts and keeps the best.
    /// </summary>
    internal static class RoadChecks
    {
        public sealed class Result
        {
            public int Passed, Total;
            public double Score;                    // 0..1, how close on the checks that failed too
            public List<string> Failed = new List<string>();
            public RoadShape Shape;
            public string Fallback;                 // set when no layout passed every must (RoadPlanner.PlanChecked)
            public override string ToString()
            {
                return (Fallback != null ? "Fallback: " + Fallback + ". " : "") + "Road checks " + Passed + "/" + Total
                    + (Failed.Count > 0 ? " (missed: " + string.Join(", ", Failed.ToArray()) + ")" : "");
            }
        }

        public static RoadShape Measure(RoadNet net, float worldM)
        {
            RoadShape r = new RoadShape();
            r.Km = net.TotalKm;
            // junction arms: a road ending at the junction adds 1, a road passing through it adds 2
            List<float[]> js = new List<float[]>();
            foreach (float[] j in net.Junctions)
            {
                int arms = 0;
                foreach (Road rd in net.Roads)
                {
                    List<float[]> p = rd.Points;
                    if (p.Count < 2) continue;
                    if (D(p[0], j) < 12 || D(p[p.Count - 1], j) < 12) { arms++; continue; }
                    for (int k = 1; k < p.Count; k++) if (SegD(p[k - 1], p[k], j) < 12) { arms += 2; break; }
                }
                if (arms == 3) r.T++; else if (arms >= 4) r.X++;
                if (arms >= 3) js.Add(j);
            }
            foreach (Road rd in net.Roads)
            {
                if (rd.Points.Count < 2) continue;
                foreach (float[] e in new[] { rd.Points[0], rd.Points[rd.Points.Count - 1] })
                {
                    bool atJ = false, edge = e[0] < 40 || e[1] < 40 || e[0] > worldM - 40 || e[1] > worldM - 40, onRoad = false;
                    foreach (float[] j in net.Junctions) if (D(e, j) < 15) { atJ = true; break; }
                    if (!atJ)
                        foreach (Road o in net.Roads)
                        {
                            if (o == rd) continue;
                            for (int k = 1; k < o.Points.Count; k++) if (SegD(o.Points[k - 1], o.Points[k], e) < 15) { onRoad = true; break; }
                            if (onRoad) break;
                        }
                    if (!atJ && !edge && !onRoad) r.Dead++;
                }
            }
            // downtown core and neighbourhood clusters (junctions linked within 160 m, 2+ junctions, > 400 m from the core)
            int ci = -1;
            for (int i = 0; i < js.Count; i++)
            {
                int c = 0; foreach (float[] b in js) if (D(js[i], b) <= 250) c++;
                if (c > r.Core) { r.Core = c; ci = i; }
            }
            int[] lab = new int[js.Count]; for (int i = 0; i < lab.Length; i++) lab[i] = -1;
            int nc = 0;
            for (int i = 0; i < js.Count; i++)
            {
                if (lab[i] >= 0) continue;
                Stack<int> st = new Stack<int>(); st.Push(i); lab[i] = nc;
                while (st.Count > 0)
                {
                    int k = st.Pop();
                    for (int j = 0; j < js.Count; j++) if (lab[j] < 0 && D(js[k], js[j]) <= 160) { lab[j] = nc; st.Push(j); }
                }
                nc++;
            }
            for (int c = 0; c < nc && ci >= 0; c++)
            {
                int m = 0; float sx = 0, sy = 0;
                for (int i = 0; i < js.Count; i++) if (lab[i] == c) { m++; sx += js[i][0]; sy += js[i][1]; }
                if (m >= 2 && D(new[] { sx / m, sy / m }, js[ci]) > 400) r.Hoods++;
            }
            // bends, hard turns, grid share; parallel roads; reach
            double total = 0; int hard = 0; double[] hist = new double[90]; double sharp = 0;
            List<float[]> samples = new List<float[]>();
            for (int ri = 0; ri < net.Roads.Count; ri++)
            {
                List<float[]> p = Resample(net.Roads[ri].Points, 2);
                int n = p.Count; if (n < 3) continue;
                double[] L = new double[n];
                for (int i = 1; i < n; i++) L[i] = L[i - 1] + D(p[i - 1], p[i]);
                double lastHard = -1e9;
                for (int i = 0; i < n - 1; i++)
                {
                    double seg = L[i + 1] - L[i]; if (seg <= 0) continue;
                    total += seg;
                    double h = Math.Atan2(p[i + 1][1] - p[i][1], p[i + 1][0] - p[i][0]);
                    hist[((int)(h * 180 / Math.PI) % 90 + 90) % 90] += seg;
                    if (i % 4 == 0) samples.Add(new[] { p[i][0], p[i][1], (float)h, ri });
                    int j0 = i; while (j0 > 0 && L[i] - L[j0] < 8) j0--;
                    int j1 = i + 1; while (j1 < n - 1 && L[j1] - L[i] < 8) j1++;
                    if (j1 - 1 <= j0 || L[j1] - L[j0] < 12) continue;
                    double a0 = Math.Atan2(p[j0 + 1][1] - p[j0][1], p[j0 + 1][0] - p[j0][0]), a1 = Math.Atan2(p[j1][1] - p[j1 - 1][1], p[j1][0] - p[j1 - 1][0]);
                    double d = Math.Abs(Math.IEEERemainder(a1 - a0, 2 * Math.PI)), span = L[j1] - L[j0];
                    if (d > 1e-6 && span / d < 25) sharp += seg;
                    if (d > Math.PI / 3 && L[i] - lastHard > 16) { hard++; lastHard = L[i]; }
                }
            }
            if (total > 0)
            {
                r.Hard = (float)(hard / (total / 1000)); r.Sharp = (float)(sharp / total);
                double best = 0;
                for (int c = 0; c < 90; c++) { double sum = 0; for (int dd = -10; dd <= 10; dd++) sum += hist[((c + dd) % 90 + 90) % 90]; best = Math.Max(best, sum); }
                r.Grid = (float)(best / total);
            }
            Dictionary<long, List<float[]>> grid = new Dictionary<long, List<float[]>>();
            foreach (float[] q in samples) { long key = (long)Math.Floor(q[0] / 40) * 100000 + (long)Math.Floor(q[1] / 40); List<float[]> l; if (!grid.TryGetValue(key, out l)) grid[key] = l = new List<float[]>(); l.Add(q); }
            int close = 0;
            foreach (float[] q in samples)
            {
                long gx = (long)Math.Floor(q[0] / 40), gy = (long)Math.Floor(q[1] / 40); bool hit = false;
                for (long dx = -1; dx <= 1 && !hit; dx++)
                    for (long dy = -1; dy <= 1 && !hit; dy++)
                    {
                        List<float[]> l; if (!grid.TryGetValue((gx + dx) * 100000 + gy + dy, out l)) continue;
                        foreach (float[] o in l)
                        {
                            if (o[3] == q[3]) continue;
                            float d = D(o, q);
                            if (d > 4 && d < 25 && Math.Abs(Math.Sin(o[2] - q[2])) < 0.5) { hit = true; break; }
                        }
                    }
                if (hit) close++;
            }
            r.Parallel = samples.Count > 0 ? (float)close / samples.Count : 0;
            int cells = (int)(worldM / 64), reach = 0;
            bool[] near = new bool[cells * cells];
            foreach (float[] q in samples) { int x = (int)(q[0] / 64), y = (int)(q[1] / 64); if (x >= 0 && y >= 0 && x < cells && y < cells) near[y * cells + x] = true; }
            for (int y = 0; y < cells; y++)
                for (int x = 0; x < cells; x++)
                {
                    bool any = false;
                    for (int dy = -3; dy <= 3 && !any; dy++) for (int dx = -3; dx <= 3 && !any; dx++)
                        { int xx = x + dx, yy = y + dy; if (xx >= 0 && yy >= 0 && xx < cells && yy < cells && near[yy * cells + xx]) any = true; }
                    if (any) reach++;
                }
            r.Reach = cells > 0 ? (float)reach / (cells * cells) : 0;
            r.Connected = Connected(net);
            r.Pieces = net.Roads.Count;
            return r;
        }

        /// <summary>The ten checks against the Like world's targets (scaled to this map's size).</summary>
        public static Result Check(RoadShape g, RoadTargets e, float worldM)
        {
            Result res = new Result(); res.Shape = g;
            double score = 0;
            float scale = e.WorldM > 0 ? (worldM * worldM) / (e.WorldM * e.WorldM) : 1; // road amount grows with the land
            Add(res, ref score, "connected", g.Connected ? 1 : 0);
            if (!float.IsNaN(e.Km)) { float want = e.Km * scale; Add(res, ref score, "road length", Band(g.Km, want * 0.8f, want * 1.2f, want * 0.4f)); }
            if (e.Core >= 0) Add(res, ref score, "downtown", Band(g.Core, Math.Max(3, 0.6f * e.Core), 1e9f, Math.Max(2, 0.3f * e.Core)));
            if (e.Hoods >= 0) Add(res, ref score, "neighbourhoods", Band(g.Hoods, Math.Max(0, e.Hoods - 1), e.Hoods + 3, 1.5f));
            if (e.T >= 0) Add(res, ref score, "junctions", Band(g.T, e.T * scale * 0.65f, e.T * scale * 1.35f + 2, Math.Max(3, e.T * 0.3f)) * Band(g.X, 0, e.X * scale * 1.5f + 2, 3));
            if (e.Caps >= 0) Add(res, ref score, "dead ends", Band(g.Dead, 0, e.Caps * scale * 1.5f + 2, 4));
            if (!float.IsNaN(e.Hard)) Add(res, ref score, "flow", Band(g.Hard, 0, Math.Max(1f, e.Hard * 1.5f), 1f) * Band(g.Sharp, 0, e.Sharp + 0.03f, 0.04f));
            if (!float.IsNaN(e.Grid)) Add(res, ref score, "grid", Band(g.Grid, e.Grid - 0.15f, e.Grid + 0.15f, 0.12f));
            if (!float.IsNaN(e.Parallel)) Add(res, ref score, "spacing", Band(g.Parallel, 0, e.Parallel + 0.05f, 0.06f));
            if (!float.IsNaN(e.Reach)) Add(res, ref score, "spread", Band(g.Reach, e.Reach - 0.15f, 1, 0.15f));
            res.Score = res.Total > 0 ? score / res.Total : 0;
            return res;
        }

        // 1 inside [lo, hi], falling to 0 over 'soft' outside it
        private static double Band(double v, double lo, double hi, double soft)
        {
            if (v < lo) return Math.Max(0, 1 - (lo - v) / soft);
            if (v > hi) return Math.Max(0, 1 - (v - hi) / soft);
            return 1;
        }

        private static void Add(Result res, ref double score, string name, double v)
        {
            res.Total++; score += v;
            if (v >= 0.999) res.Passed++; else res.Failed.Add(name);
        }

        private static bool Connected(RoadNet net)
        {
            int n = net.Roads.Count; if (n <= 1) return true;
            int[] lab = new int[n]; for (int i = 0; i < n; i++) lab[i] = -1;
            Stack<int> st = new Stack<int>(); st.Push(0); lab[0] = 0; int seen = 1;
            while (st.Count > 0)
            {
                int a = st.Pop();
                for (int b = 0; b < n; b++)
                    if (lab[b] < 0 && Touch(net.Roads[a], net.Roads[b])) { lab[b] = 0; seen++; st.Push(b); }
            }
            return seen == n;
        }

        private static bool Touch(Road a, Road b)
        {
            foreach (float[] e in new[] { a.Points[0], a.Points[a.Points.Count - 1] })
                for (int k = 1; k < b.Points.Count; k++) if (SegD(b.Points[k - 1], b.Points[k], e) < 15) return true;
            foreach (float[] e in new[] { b.Points[0], b.Points[b.Points.Count - 1] })
                for (int k = 1; k < a.Points.Count; k++) if (SegD(a.Points[k - 1], a.Points[k], e) < 15) return true;
            return false;
        }

        private static float D(float[] a, float[] b) { float dx = a[0] - b[0], dy = a[1] - b[1]; return (float)Math.Sqrt(dx * dx + dy * dy); }

        private static float SegD(float[] a, float[] b, float[] p)
        {
            float dx = b[0] - a[0], dy = b[1] - a[1], l2 = dx * dx + dy * dy;
            float t = l2 > 0 ? Math.Max(0, Math.Min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / l2)) : 0;
            float x = a[0] + t * dx - p[0], y = a[1] + t * dy - p[1];
            return (float)Math.Sqrt(x * x + y * y);
        }

        private static List<float[]> Resample(List<float[]> p, float step)
        {
            List<float[]> o = new List<float[]>();
            if (p.Count == 0) return o;
            o.Add(p[0]); float acc = 0;
            for (int i = 1; i < p.Count; i++)
            {
                float d = D(p[i - 1], p[i]);
                if (d <= 0) continue;
                float t = step - acc;
                while (t <= d) { o.Add(new[] { p[i - 1][0] + (p[i][0] - p[i - 1][0]) * t / d, p[i - 1][1] + (p[i][1] - p[i - 1][1]) * t / d }); t += step; }
                acc = (acc + d) % step;
            }
            if (D(o[o.Count - 1], p[p.Count - 1]) > 0.5f) o.Add(p[p.Count - 1]);
            return o;
        }
    }
}
