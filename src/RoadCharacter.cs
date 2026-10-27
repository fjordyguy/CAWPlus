using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CawPlus
{
    /// <summary>
    /// The geography check for road layouts (the tester, 2026-10-22: "the road layout also makes sense" for where the world is
    /// set). Each world type has a region (worlds.txt column 37): the real-world kind of place it is modelled on and how
    /// roads are laid out there. A planned layout is measured five ways and must fall in the region's bands:
    ///   order       how grid-like the street directions are (Boeing 2019: 1 = perfect grid, 0 = no order; US and Canadian
    ///               towns are far more grid-like than European ones)
    ///   radial      roads pointing at the town centre (0.64 = no preference); measured and logged, not checked: EA's
    ///               worlds range only 0.56-0.73 and no region stands out on it
    ///   elongation  the town's road spread, short axis / long axis (low = a town strung along one road: Icelandic fjord
    ///               towns, the French "village-rue")
    ///   coast       share of road within 80 m of the sea (coastal roads: islands, harbours, fjords)
    ///   ring        share of directions round the map centre with road 15-45% of the map out (a ring road: Rarotonga's
    ///               Ara Tapu round its volcanic island)
    /// The bands are centred on the EA world's own figures where its road graph was measured (tools/roadgeo.py,
    /// library/&lt;key&gt;_roadgeo.json), with the region's width; otherwise the region's band. Only the measures that
    /// define a region are checked. RoadPlanner.PlanChecked counts them with the other road checks.
    /// </summary>
    internal sealed class RoadCharacter
    {
        public string Region = "", Description = "";
        public float Order = float.NaN, Radial = float.NaN, Elongation = float.NaN, Coast = float.NaN, Ring = float.NaN;
        // bands (lo, hi) for the measures this region checks; NaN = not checked
        public float[] OrderBand, ElongBand, CoastBand, RingBand;
        public bool Motorway;                          // a road right across the map beside the town, the town joined to it (Lucky Palms)
        public bool CoastPull, Linear, ShoreRoad; // planner hints: roads drawn towards the shore; no round loops; a road along the coast through town

        private sealed class Def
        {
            public string Key, Text; public float[] Order, Elong, Coast, Ring; public bool CoastPull, Linear, ShoreRoad, Motorway;
            public Def(string key, string text) { Key = key; Text = text; }
        }

        private static readonly List<Def> Regions = MakeRegions();

        private static List<Def> MakeRegions()
        {
            List<Def> l = new List<Def>();
            Def d;
            d = new Def("us-suburb", "American small town: a Main Street grid at the core, collector loops and cul-de-sacs round it (the post-1960s street hierarchy)");
            d.Order = new[] { 0.05f, 0.35f }; d.Elong = new[] { 0.55f, 1f }; d.Coast = new[] { 0f, 0.35f }; l.Add(d);
            d = new Def("us-city", "American city by the water: a firm downtown grid, avenues along the waterfront");
            d.Order = new[] { 0.2f, 0.55f }; d.Coast = new[] { 0.3f, 0.9f }; d.CoastPull = true; l.Add(d);
            d = new Def("us-desert-grid", "American desert town: long straight roads on a grid over flat ground");
            d.Order = new[] { 0.3f, 0.65f }; d.Elong = new[] { 0.6f, 1f }; d.Motorway = true; l.Add(d);
            d = new Def("planned", "Planned town (campus, new town): a regular grid, roads all round");
            d.Order = new[] { 0.4f, 0.8f }; d.Ring = new[] { 0.7f, 1f }; l.Add(d);
            d = new Def("iceland-fjord", "Icelandic coastal town: strung along the shore and one main road, a small core, no grid");
            d.Coast = new[] { 0.5f, 0.95f }; d.Elong = new[] { 0.25f, 0.7f }; d.CoastPull = true; d.Linear = true; d.ShoreRoad = true; l.Add(d);
            d = new Def("pacific-ring", "Volcanic Pacific island: one road round the island by the shore (Rarotonga's Ara Tapu), none over the mountain");
            d.Coast = new[] { 0.55f, 0.95f }; d.Ring = new[] { 0.85f, 1f }; d.CoastPull = true; l.Add(d);
            d = new Def("island-coastal", "Tropical islands: roads follow each island's shore, irregular, no grid");
            d.Coast = new[] { 0.45f, 0.9f }; d.Order = new[] { 0f, 0.2f }; d.CoastPull = true; d.ShoreRoad = true; l.Add(d);
            d = new Def("harbour-village", "Old harbour town: narrow winding streets, a waterfront street to the harbour");
            d.Coast = new[] { 0.1f, 0.5f }; d.Order = new[] { 0f, 0.3f }; l.Add(d);
            d = new Def("french-village", "French village: houses along one main road (village-rue) or clustered round the church, little grid");
            d.Elong = new[] { 0.35f, 0.75f }; d.Order = new[] { 0f, 0.3f }; d.Linear = true; l.Add(d);
            d = new Def("italian-hilltown", "Italian hill town: streets bend with the land between the gates and the piazza, no overall grid");
            d.Order = new[] { 0f, 0.2f }; d.Elong = new[] { 0.6f, 1f }; l.Add(d);
            d = new Def("chinese-mountain", "Chinese mountain village: few roads along the valley and lake, the rest stairways and paths up the slopes");
            d.Order = new[] { 0f, 0.3f }; d.Elong = new[] { 0.4f, 0.8f }; l.Add(d);
            d = new Def("nile-desert", "Egyptian town by the Nile: an organic old town, roads along the river");
            d.Order = new[] { 0f, 0.2f }; l.Add(d);
            d = new Def("fantasy-valley", "Fantasy valley village: lanes winding round the castle and down to the shore");
            d.Order = new[] { 0f, 0.3f }; d.Coast = new[] { 0.2f, 0.6f }; l.Add(d);
            d = new Def("alien-planned", "Alien colony: a compact round settlement with roads all round");
            d.Ring = new[] { 0.7f, 1f }; d.Elong = new[] { 0.75f, 1f }; l.Add(d);
            return l;
        }

        /// <summary>
        /// The target for a world: region (worlds.txt column 37) and the EA world's measured figures (roadgeo json text,
        /// may be null). Null if the region is unknown.
        /// </summary>
        public static RoadCharacter For(string region, string measuredJson)
        {
            if (string.IsNullOrEmpty(region)) return null;
            Def d = null;
            foreach (Def q in Regions) if (string.Equals(q.Key, region.Trim(), StringComparison.OrdinalIgnoreCase)) d = q;
            if (d == null) return null;
            RoadCharacter c = new RoadCharacter();
            c.Region = d.Key; c.Description = d.Text; c.CoastPull = d.CoastPull; c.Linear = d.Linear; c.ShoreRoad = d.ShoreRoad; c.Motorway = d.Motorway;
            float mo = Num(measuredJson, "order"), me = Num(measuredJson, "elongation"), mc = Num(measuredJson, "coast"), mg = Num(measuredJson, "ring");
            c.OrderBand = Centre(d.Order, mo); c.ElongBand = Centre(d.Elong, me);
            c.CoastBand = Centre(d.Coast, mc); c.RingBand = Centre(d.Ring, mg);
            return c;
        }

        // the region's band, moved to sit round the EA world's own figure when there is one (same width, within 0..1)
        private static float[] Centre(float[] band, float measured)
        {
            if (band == null) return null;
            if (float.IsNaN(measured)) return band;
            float half = (band[1] - band[0]) / 2;
            float lo = Math.Max(0, measured - half), hi = Math.Min(1, measured + half);
            if (band[1] >= 1) hi = 1; // open-ended bands (ring, elongation) stay open at the top
            return new[] { lo, hi };
        }

        private static float Num(string t, string k)
        {
            if (t == null) return float.NaN;
            Match m = Regex.Match(t, "\"" + k + "\"\\s*:\\s*(-?[0-9.eE+-]+)");
            float v;
            return m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : float.NaN;
        }

        /// <summary>
        /// Measures a planned layout the same way tools/roadgeo.py measures EA's (roads as polylines in metres on the
        /// heights grid 'h' with 'cell' metres per cell). Fills Order, Radial, Elongation, Coast, Ring.
        /// </summary>
        public static RoadCharacter Measure(RoadNet net, Grid h, float cell, float sea)
        {
            RoadCharacter c = new RoadCharacter();
            float world = (h.W - 1) * cell;
            List<float[]> segs = new List<float[]>(); // x0, z0, x1, z1, length
            foreach (Road r in net.Roads)
                for (int i = 1; i < r.Points.Count; i++)
                {
                    float[] a = r.Points[i - 1], b = r.Points[i];
                    float l = (float)Math.Sqrt((b[0] - a[0]) * (b[0] - a[0]) + (b[1] - a[1]) * (b[1] - a[1]));
                    int n = Math.Max(1, (int)(l / 8));
                    for (int k = 0; k < n; k++)
                    {
                        float t0 = (float)k / n, t1 = (float)(k + 1) / n;
                        segs.Add(new[] { a[0] + (b[0] - a[0]) * t0, a[1] + (b[1] - a[1]) * t0, a[0] + (b[0] - a[0]) * t1, a[1] + (b[1] - a[1]) * t1, l / n });
                    }
                }
            if (segs.Count < 4) return c;
            double tot = 0;
            foreach (float[] s in segs) tot += s[4];
            // orientation order
            double[] hist = new double[36];
            foreach (float[] s in segs)
            {
                double b = Math.Atan2(s[3] - s[1], s[2] - s[0]) * 180 / Math.PI;
                b = ((b % 180) + 180) % 180;
                hist[(int)(b / 5) % 36] += s[4];
            }
            double H = 0;
            foreach (double v in hist) if (v > 0) { double p = v / tot; H -= p * Math.Log(p); }
            double hmax = Math.Log(36), hg = Math.Log(4);
            c.Order = (float)(1 - Math.Pow((H - hg) / (hmax - hg), 2));
            // town centre: the most road within 250 m (32 m search grid)
            float cx = world / 2, cz = world / 2; double best = -1;
            for (float gx = 0; gx < world; gx += 32)
                for (float gz = 0; gz < world; gz += 32)
                {
                    double v = 0;
                    foreach (float[] s in segs)
                    {
                        float mx = (s[0] + s[2]) / 2 - gx, mz = (s[1] + s[3]) / 2 - gz;
                        if (mx * mx + mz * mz < 250 * 250) v += s[4];
                    }
                    if (v > best) { best = v; cx = gx; cz = gz; }
                }
            // radiality 60-600 m out
            double num = 0, den = 0;
            foreach (float[] s in segs)
            {
                float mx = (s[0] + s[2]) / 2 - cx, mz = (s[1] + s[3]) / 2 - cz;
                float r = (float)Math.Sqrt(mx * mx + mz * mz);
                if (r < 60 || r > 600) continue;
                num += Math.Abs((mx * (s[2] - s[0]) + mz * (s[3] - s[1])) / (r * s[4])) * s[4]; den += s[4];
            }
            c.Radial = den > 0 ? (float)(num / den) : 0.64f;
            // elongation within 700 m: weighted covariance of the road pieces' midpoints
            double sw = 0, sx = 0, sz = 0;
            foreach (float[] s in segs)
            {
                float mx = (s[0] + s[2]) / 2, mz = (s[1] + s[3]) / 2;
                if ((mx - cx) * (mx - cx) + (mz - cz) * (mz - cz) >= 700 * 700) continue;
                sw += s[4]; sx += mx * s[4]; sz += mz * s[4];
            }
            if (sw > 0)
            {
                double ax = sx / sw, az = sz / sw, cxx = 0, czz = 0, cxz = 0;
                foreach (float[] s in segs)
                {
                    float mx = (s[0] + s[2]) / 2, mz = (s[1] + s[3]) / 2;
                    if ((mx - cx) * (mx - cx) + (mz - cz) * (mz - cz) >= 700 * 700) continue;
                    cxx += (mx - ax) * (mx - ax) * s[4]; czz += (mz - az) * (mz - az) * s[4]; cxz += (mx - ax) * (mz - az) * s[4];
                }
                cxx /= sw; czz /= sw; cxz /= sw;
                double tr = cxx + czz, det = cxx * czz - cxz * cxz, disc = Math.Sqrt(Math.Max(0, tr * tr / 4 - det));
                double l1 = tr / 2 + disc, l2 = tr / 2 - disc;
                c.Elongation = (float)Math.Sqrt(Math.Max(0, l2) / Math.Max(1e-6, l1));
            }
            // by the sea: within 80 m of water (chamfer distance on the heights grid)
            int n2 = h.W;
            float[] dist = LandChecklist.Distance(delegate (int i) { return h.D[i] < sea; }, h.W, h.H, cell);
            double coast = 0;
            bool[] ring = new bool[36];
            foreach (float[] s in segs)
            {
                float mx = (s[0] + s[2]) / 2, mz = (s[1] + s[3]) / 2;
                int gx = Math.Max(0, Math.Min(n2 - 1, (int)Math.Round(mx / cell))), gz = Math.Max(0, Math.Min(n2 - 1, (int)Math.Round(mz / cell)));
                if (dist[gz * n2 + gx] < 80) coast += s[4];
                float rx = mx - world / 2, rz = mz - world / 2, rr = (float)Math.Sqrt(rx * rx + rz * rz) / world;
                if (rr >= 0.15f && rr <= 0.45f)
                {
                    double a = Math.Atan2(rz, rx) * 180 / Math.PI; a = (a + 360) % 360;
                    ring[(int)(a / 10) % 36] = true;
                }
            }
            c.Coast = (float)(coast / tot);
            int rc = 0; foreach (bool b in ring) if (b) rc++;
            c.Ring = rc / 36f;
            return c;
        }

        /// <summary>Adds this region's checks (only the measures that define it) to a road-check result.</summary>
        public void AddChecks(RoadChecks.Result res, RoadCharacter got)
        {
            Add(res, "grid order (" + Region + ")", got.Order, OrderBand, 0.1f);
            Add(res, "town shape (" + Region + ")", got.Elongation, ElongBand, 0.15f);
            Add(res, "roads by the shore (" + Region + ")", got.Coast, CoastBand, 0.15f);
            Add(res, "ring road (" + Region + ")", got.Ring, RingBand, 0.2f);
        }

        private static void Add(RoadChecks.Result res, string name, float v, float[] band, float soft)
        {
            if (band == null || float.IsNaN(v)) return;
            double s = v < band[0] ? Math.Max(0, 1 - (band[0] - v) / soft) : v > band[1] ? Math.Max(0, 1 - (v - band[1]) / soft) : 1;
            res.Score = (res.Score * res.Total + s) / (res.Total + 1);
            res.Total++;
            if (s >= 0.999) res.Passed++; else res.Failed.Add(name);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "order {0:0.00}, radial {1:0.00}, elongation {2:0.00}, coast {3:0.00}, ring {4:0.00}", Order, Radial, Elongation, Coast, Ring);
        }
    }
}
