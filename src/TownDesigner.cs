using System;
using System.Collections.Generic;
using System.Globalization;

namespace CawPlus
{
    /// <summary>
    /// The town designer (the tester, 2026-10-27: "I see a nice beach front, I will place a road set back and this will be a
    /// promenade to the beach lot; a road back to the town centre where there will be a 64x64 park and the road fits round
    /// it; roads split off the junctions to neighbourhoods with cul-de-sacs, country roads follow the hillside out of town;
    /// blocks uniform and neat. EA worlds have order and uniform layouts that look pleasing to the eye."). Instead of laying
    /// patterns and checking numbers afterwards, it reads the land, decides what goes where, and lays each road for a
    /// purpose, every one straight, parallel to the town's axis, or one smooth curve along the land:
    /// 0. by the coast: with the sea within 1.5 km the centre moves to roomy gentle ground 60-320 m from it (EA's town
    ///    centres stand a median ~145 m from the water);
    /// 1. one axis for the whole town: along the shore for a coastal town, else the flattest way through the centre;
    /// 2. downtown: the World type's number of uniform blocks on that axis, a 64 x 64 park on a block with streets round it;
    /// 3. a road along the shore near town (beach or low bluff), set back a lot's depth plus a strip, joined to the centre;
    /// 4. ways out of town: country roads from the edge of the map in, the routes following the land;
    /// 5. roads out of the centre: downtown streets carried on, straight out of the grid, then along the land;
    /// 6. a ring of roads round the centre (Appaloosa Plains, Champs Les Sims, Twinbrook), following the land;
    /// 7. short straight streets off those roads, square-on, 150 m+ apart with green between, out to where the World
    ///    type keeps 90 % of its lots; single streets further out only if the town is still short of road.
    /// Archipelagos and atolls keep the planner (bridged islands, ring road).
    /// Every decision is written to RoadNet.Design.
    /// </summary>
    internal static partial class RoadPlanner
    {
        private static void DesignTown(Ctx c, float worldSize, int lotsWanted)
        {
            RoadSettings s = c.S; int n = c.N;
            List<string> log = c.Net.Design;
            CultureInfo ci = CultureInfo.InvariantCulture;
            float[] sea = SeaDistance(c);
            // 0. a coast near: the town is built from the coast (the tester: "they would be built from the coast"; Sunset
            //    Valley's centre is ~300 m from its bay): the centre moves onto roomy, gentle ground 60-320 m from the sea (EA: a median ~145 m)
            if (!c.PlanCentre && c.S.CentreHint == null) CoastalCentre(c, sea, log); // a painted centre or a river-fork hint wins
            float cx = (c.Centre % n) * Cell, cy = (c.Centre / n) * Cell;
            float front, depth; LotSize(s, out front, out depth);
            // 1. the town's axis
            string axisWhy;
            float axis = TownAxis(c, sea, cx, cy, out axisWhy);
            log.Add(string.Format(ci, "Town centre at ({0:F0}, {1:F0}) on the flattest open ground; the town is laid on one axis, {2:F0} deg ({3}).", cx, cy, axis * 180 / Math.PI, axisWhy));
            // 2. downtown blocks with the park in the middle
            float bx = s.Grid != null ? s.Grid.LaySpacingB : Clamp(3 * front + 12, 80, 130);
            float by = s.Grid != null ? s.Grid.LaySpacingA : Clamp(2 * depth + 12, 70, 120);
            by = Math.Max(by, 76); // a 64 m park fits between two streets
            int along = s.Grid != null ? Math.Max(1, s.Grid.LayB - 1) : Math.Max(2, Math.Min(5, s.DowntownBlocks));
            int across = s.Grid != null ? Math.Max(1, s.Grid.LayA - 1) : 2;
            // as many streets as the World type's downtown (Sunset Valley 3 x 4, Starlight Shores 3 x 2), not more: a bigger
            // grid packs the town in
            int before = c.Net.Roads.Count;
            float townRadius;
            bool gridless = s.Grid != null && !s.Grid.HasGrid;
            if (gridless)
            {
                // no grid in the World type's centre (Midnight Hollow, Al Simhara): a main street through it, houses along it
                float ca0 = (float)Math.Cos(axis), sa0 = (float)Math.Sin(axis);
                StraightStreet(c, cx, cy, ca0, sa0, 160, 40, Town, -1, RoadKind.Main);
                StraightStreet(c, cx, cy, -ca0, -sa0, 160, 40, Town, -1, RoadKind.Main);
                townRadius = 160;
                log.Add("Downtown: the World type's centre has no block grid, so one main street runs through it on the axis.");
            }
            else townRadius = GridRoads(c, cx, cy, axis, along, across, bx, by, -(across / 2), Town, -1, true, 0.5f, false);
            if (gridless) { } // no blocks, no park block
            else if (c.Net.Roads.Count - before < 4)
            {
                RemoveFrom(c, before, Town);
                townRadius = Downtown(c);
                log.Add("Downtown: the land round the centre is too broken for the aligned blocks; the usual downtown is laid instead.");
            }
            else
            {
                log.Add(string.Format(ci, "Downtown: {0} x {1} uniform blocks of {2:F0} x {3:F0} m on the axis (two lots deep), main street through the middle and on out of town.", along, across, bx, by));
                float ca = (float)Math.Cos(axis), sa = (float)Math.Sin(axis);
                // the block nearest the centre with streets on all four sides
                List<float[]> blocks = new List<float[]>();
                foreach (float fu in new[] { 0f, 0.5f, -0.5f, 1f, -1f })
                    foreach (float fv in new[] { 0f, 0.5f, -0.5f, 1f, -1f })
                        blocks.Add(new[] { fu * bx, fv * by, Sq(fu * bx) + Sq(fv * by) });
                blocks.Sort((p1, p2) => p1[2].CompareTo(p2[2]));
                foreach (float[] q in blocks)
                {
                    float u = q[0], v = q[1];
                    float px = cx + u * ca - v * sa, py = cy + u * sa + v * ca;
                    if (!BlockRingRoaded(c, px, py, ca, sa, bx, by)) continue;
                    float pw = Math.Min(64, bx - 14), pd = Math.Min(64, by - 14);
                    c.Net.Park = new[] { px, py, ca, sa, pw, pd };
                    log.Add(string.Format(ci, "Park: a {0:F0} x {1:F0} m park on the block beside the centre, the downtown streets round all four sides.", pw, pd));
                    break;
                }
            }
            c.Net.TownRadius = townRadius;
            // 3. a beach near town: the promenade
            if (s.Character == null || s.Character.Region != "nile-desert") Promenade(c, sea, cx, cy, depth, log);
            // 4. ways out of town
            List<int> edges = EdgeCells(c, s.EdgeRoads);
            foreach (int e in edges)
            {
                List<int> path = AStar(c, e, null, NoOwner, 0);
                if (AddPath(c, path, RoadKind.Main, Town, -1))
                {
                    c.Net.Roads[c.Net.Roads.Count - 1].Kind = RoadKind.Main;
                    log.Add(string.Format(ci, "Way out of town to the {0} edge: a country road of {1:F0} m, kept to the gentlest line across the land.", Compass((e % n) * Cell - cx, (e / n) * Cell - cy), PathLen(c, path)));
                }
            }
            // 5. streets off the main roads (EA's commonest neighbourhood: short straight streets lined with houses,
            //    leaving the main roads square-on just outside the centre); then, if the town is still short of road,
            //    small neighbourhoods further out
            float targetKm = s.TargetKm > 0 ? s.TargetKm : s.Targets != null && !float.IsNaN(s.Targets.Km) ? s.Targets.Km * (s.Targets.WorldM > 0 ? worldSize * worldSize / (s.Targets.WorldM * s.Targets.WorldM) : 1) : 0;
            // the World type's road length is the budget: a village (Midnight Hollow, 3 km) gets no ring and few roads out
            Spokes(c, cx, cy, townRadius, targetKm, log);
            // a ring of roads round the centre (Appaloosa Plains, Champs Les Sims, Twinbrook, Dragon Valley): the town
            // spreads along it with green between, instead of packing round the grid
            if (s.TownLoops && (targetKm <= 0 || NetKm(c) < targetKm * 0.6f))
            {
                int ringFrom = c.Net.Roads.Count;
                // at the World type's ring distance (EA: 260-690 m from the core, Sunset Valley 307 m), else just outside the grid
                float ringArg = s.TownDesign != null ? Math.Max(townRadius + 30, s.TownDesign[3] - 120) : townRadius + 60;
                TownLoops(c, ringArg);
                if (c.Net.Roads.Count > ringFrom)
                    log.Add(string.Format(ci, "Ring road: {0} arcs round the centre, about {1:F0} m out{2}, following the land; the roads out of town cross it.", c.Net.LoopArcs, Math.Max(170, ringArg + 120) * 1.07f, s.TownDesign != null ? " (the World type's distance)" : ""));
            }
            BranchStreets(c, cx, cy, townRadius, front, depth, lotsWanted, targetKm, log);
            int hoods = HoodCount(s, lotsWanted, worldSize);
            float hoodR = Clamp(worldSize * 0.06f, 70, 130);
            c.Net.HoodsAsked = hoods;
            int made = 0;
            if (targetKm > 0 && NetKm(c) < targetKm * 0.8f)
            {
                List<int> hubs = Hubs(c, Math.Max(hoods, 3) * 4, townRadius + 60, hoodR * 2.0f, hoodR);
                for (int k = 0; k < hubs.Count && NetKm(c) < targetKm * 0.85f; k++)
                {
                    int hub = hubs[k], own = k + 1;
                    if (!FreeOfOthers(c, (hub % n) * Cell, (hub / n) * Cell, own)) continue;
                    float hx = (hub % n) * Cell, hy = (hub / n) * Cell;
                    // one street lined with houses (small grids out in the fields stood empty: no lots reach them)
                    if (!LinearHood(c, hub, hoodR, own)) continue;
                    made++;
                    c.Net.Hoods.Add(HoodStyle.Linear); c.Net.HoodCentres.Add(new[] { hx, hy });
                    log.Add(string.Format(ci, "Neighbourhood {0}, {1:F0} m {2} of the centre: {3}, joined back to the town.", made, (float)Math.Sqrt(Sq(hx - cx) + Sq(hy - cy)), Compass(hx - cx, hy - cy),
                        "one straight street lined with houses"));
                }
            }
            // 6. everything joined
            ConnectAll(c);
            if (s.IslandTowns) IslandTowns(c);
            c.Net.LotsWanted = lotsWanted;
            log.Add(string.Format(ci, "In all {0:F1} km of road{1}; no other side streets.", NetKm(c), targetKm > 0 ? string.Format(ci, " (the World type has {0:F1} km)", targetKm) : ""));
        }

        /// <summary>
        /// Downtown streets carry on out of town (EA's towns have 4-6 roads out of the centre: Sunset Valley 6): every
        /// downtown street end left open at the grid's edge is continued outwards, straight where the land allows, else
        /// then along the land (the gentlest route) to a point 250-450 m out, ending on another road or open (side streets then branch off it).
        /// </summary>
        private static void Spokes(Ctx c, float cx, float cy, float townRadius, float targetKm, List<string> log)
        {
            int count = c.Net.Roads.Count, made = 0;
            float total = 0;
            List<float> angles = new List<float>();
            foreach (Road er in c.Net.Roads) // the ways out of town already laid count as roads out
                if (er.Area == -1 && !er.Straight && er.Points.Count > 1)
                    foreach (float[] q in er.Points)
                    { float dq = (float)Math.Sqrt(Sq(q[0] - cx) + Sq(q[1] - cy)); if (dq > townRadius && dq < townRadius + 30) angles.Add((float)Math.Atan2(q[1] - cy, q[0] - cx)); }
            for (int r = 0; r < count; r++)
            {
                Road road = c.Net.Roads[r];
                if (road.Area != -1 || !road.Straight || road.Points.Count < 2) continue;
                for (int end = 0; end < 2; end++)
                {
                    List<float[]> p = road.Points;
                    float[] a = end == 0 ? p[0] : p[p.Count - 1], b = end == 0 ? p[1] : p[p.Count - 2];
                    float d = (float)Math.Sqrt(Sq(a[0] - cx) + Sq(a[1] - cy));
                    if (d < townRadius * 0.6f) continue;
                    float dx = a[0] - b[0], dy = a[1] - b[1], dl = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (dl < 1e-3f) continue;
                    dx /= dl; dy /= dl;
                    if ((a[0] - cx) * dx + (a[1] - cy) * dy < 0) continue; // heading back into town
                    // a road already carries on straight ahead (the grid's next block): not a way out
                    float ax = a[0] + dx * 20, ay = a[1] + dy * 20;
                    if (!Inside(c, ax, ay, 2) || c.OnRoad[Idx(c, ax, ay)] != 0) continue;
                    // spread round the town: no two roads out within 35 deg of each other
                    float th = (float)Math.Atan2(a[1] - cy, a[0] - cx);
                    bool close = false;
                    foreach (float o in angles) { float dd = Math.Abs(th - o); if (dd > Math.PI) dd = 2 * (float)Math.PI - dd; if (dd < 0.61f) close = true; }
                    if (close) continue;
                    // straight out of the grid for a block's worth, then on along the land to a point 250-450 m out
                    float len = 250 + 200 * (float)c.Rng.NextDouble();
                    int own = 580 + made, start = c.Net.Roads.Count;
                    if (!StraightStreet(c, a[0], a[1], dx, dy, 60, 40, own, -1, RoadKind.Main)) continue;
                    List<float[]> sp = c.Net.Roads[start].Points;
                    float[] e = sp[sp.Count - 1];
                    int goal = -1;
                    foreach (float sc in new[] { 1f, 0.85f, 0.7f, 0.55f })
                        foreach (float turn in new[] { 0f, 0.3f, -0.3f, 0.6f, -0.6f })
                        {
                            if (goal >= 0) break;
                            float tx = dx * (float)Math.Cos(turn) - dy * (float)Math.Sin(turn), ty = dx * (float)Math.Sin(turn) + dy * (float)Math.Cos(turn);
                            float gx = a[0] + tx * len * sc, gy = a[1] + ty * len * sc;
                            if (!Inside(c, gx, gy, (int)(80 / Cell))) continue;
                            int gi = Idx(c, gx, gy);
                            if (c.Land[gi] && c.OnRoad[gi] == 0 && !(c.High != null && c.High[gi]) && c.Build[gi] >= 0.3f) goal = gi;
                        }
                    float lineLen = RoadLen(sp);
                    if (goal >= 0)
                    {
                        List<int> path = AStar(c, Idx(c, e[0], e[1]), new[] { goal }, own, 0);
                        float straight = (float)Math.Sqrt(Sq((goal % c.N) * Cell - e[0]) + Sq((goal / c.N) * Cell - e[1]));
                        if (path != null && PathLen(c, path) < straight * 1.5f + 40 && AddPath(c, path, RoadKind.Main, own, -1)) lineLen += PathLen(c, path);
                    }
                    total += lineLen; made++; angles.Add(th);
                }
            }
            log.Add(string.Format(CultureInfo.InvariantCulture, "Roads out of the centre: {0} downtown streets carried on out of town ({1:F1} km).", made, total / 1000));
        }

        /// <summary>
        /// Short straight streets leaving the main roads and downtown square-on (EA towns: Sunset Valley, Appaloosa Plains,
        /// Champs Les Sims, Twinbrook), nearest the centre first, out to where the World type keeps 90 % of its lots. Each
        /// is 80-180 m, lined with houses on both sides, streets start at least 150 m apart (two lot depths and green between); on a
        /// hillside the street turns up to 20 deg towards the contour. Half the time the street carries on across the
        /// main road (a crossroads). Stops when the streets can take the town's lots or it has the World type's road.
        /// </summary>
        private static void BranchStreets(Ctx c, float cx, float cy, float townRadius, float front, float depth, int lotsWanted, float targetKm, List<string> log)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            // the World type's own town (library/town_design.txt): side streets leave a road every 'run' m (both sides, so
            // each side every 2 run), dead-end streets 'dead' m long, that share of them carried on across as crossroads
            float[] td = c.S.TownDesign;
            float spacing = td != null ? Clamp(2 * td[0], 2 * depth + 24, 260) : Math.Max(2 * depth + 24, 150); // green between them
            float deadLen = td != null ? Clamp(td[1], 50, 300) : 130;
            float crossShare = td != null ? Clamp(td[2], 0.05f, 0.6f) : 0.5f;
            float maxOut = c.Shape != null ? Math.Max(c.Shape.LotDec[8], townRadius + 150) : townRadius + 350;
            List<float[]> starts = new List<float[]>(); // x, y, normal x, normal y of every street laid
            float capacity = 0, km = 0;
            int made = 0, cross = 0, own = 600, nNear = 0, nHigh = 0, nFail = 0, nSpots = 0, from = 0;
            for (int round = 0; round < 3; round++) // streets off the main roads, then off those streets
            {
                List<float[]> spots = new List<float[]>(); // distance + noise, x, y, normal x, normal y
                int parents = c.Net.Roads.Count;
                for (int r = from; r < parents; r++)
                {
                    List<float[]> p = c.Net.Roads[r].Points;
                    float run = 0, total = RoadLen(p);
                    for (int k = 1; k < p.Count; k++)
                    {
                        float sx = p[k][0] - p[k - 1][0], sy = p[k][1] - p[k - 1][1], sl = (float)Math.Sqrt(sx * sx + sy * sy);
                        if (sl < 1e-3f) continue;
                        for (float t = 0; t < sl; t += 8)
                        {
                            run += 8;
                            if (run < depth + 10 || run > total - depth - 10) continue; // a lot's depth clear of the road's ends
                            float x = p[k - 1][0] + sx * t / sl, y = p[k - 1][1] + sy * t / sl;
                            float d = (float)Math.Sqrt(Sq(x - cx) + Sq(y - cy));
                            if (d < townRadius * 0.85f || d > maxOut) continue;
                            float nx = -sy / sl, ny = sx / sl;
                            spots.Add(new[] { d + (float)c.Rng.NextDouble() * 60, x, y, nx, ny });
                            spots.Add(new[] { d + (float)c.Rng.NextDouble() * 60, x, y, -nx, -ny });
                        }
                    }
                }
                from = parents; nSpots += spots.Count;
                spots.Sort((u, v) => u[0].CompareTo(v[0]));
                foreach (float[] q in spots)
                {
                    if (capacity >= lotsWanted * 0.75f || (targetKm > 0 && NetKm(c) >= targetKm)) break;
                    bool near = false;
                    foreach (float[] st in starts)
                    {
                        float d2 = Sq(st[0] - q[1]) + Sq(st[1] - q[2]);
                        bool same = st[2] * q[3] + st[3] * q[4] > 0;
                        // same side: two lot depths and a road apart; the other side: straight across (a crossroads) or well clear
                        if (same ? d2 < spacing * spacing : d2 > 16 && d2 < 40 * 40) { near = true; break; }
                    }
                    if (near) { nNear++; continue; }
                    int i = Idx(c, q[1], q[2]);
                    if (c.High != null && c.High[i]) { nHigh++; continue; }
                    float dx = q[3], dy = q[4];
                    if (c.Slope[i] > 0.06f)
                    {
                        float[] cd = ContourDir(c, q[1], q[2]);
                        float best = -2, bx2 = dx, by2 = dy;
                        foreach (float turn in new[] { 0f, 0.35f, -0.35f })
                        {
                            float tx = dx * (float)Math.Cos(turn) - dy * (float)Math.Sin(turn), ty = dx * (float)Math.Sin(turn) + dy * (float)Math.Cos(turn);
                            float al = Math.Abs(tx * cd[0] + ty * cd[1]);
                            if (al > best + 0.05f) { best = al; bx2 = tx; by2 = ty; }
                        }
                        dx = bx2; dy = by2;
                    }
                    float len = Clamp(deadLen * (float)Math.Exp(0.35 * Gauss(c.Rng)), 50, 300);
                    int before = c.Net.Roads.Count;
                    if (!StraightStreet(c, q[1], q[2], dx, dy, len, 50, own++, -1, RoadKind.Street)) { nFail++; continue; }
                    float l = RoadLen(c.Net.Roads[before].Points);
                    capacity += 2 * l / (front + 8) * 0.8f; km += l; made++;
                    starts.Add(new[] { q[1], q[2], q[3], q[4] });
                    if (c.Rng.NextDouble() < crossShare && StraightStreet(c, q[1], q[2], -dx, -dy, len, 50, own++, -1, RoadKind.Street))
                    {
                        float l2 = RoadLen(c.Net.Roads[c.Net.Roads.Count - 1].Points);
                        capacity += 2 * l2 / (front + 8) * 0.8f; km += l2; cross++;
                        starts.Add(new[] { q[1], q[2], -q[3], -q[4] });
                    }
                }
            }
            log.Add(string.Format(ci, "Streets: {0} short straight streets off the main roads and downtown ({1} carried on across as crossroads), {2:F1} km, between {3:F0} and {4:F0} m from the centre, each lined with houses (spots {5}, too close {6}, high {7}, no room {8}).",
                made + cross, cross, km / 1000, townRadius * 0.85f, maxOut, nSpots, nNear, nHigh, nFail));
        }

        /// <summary>The centre moved to roomy, gentle, low ground 60-320 m from the sea (nearest ~150 m) when the sea is within 1.5 km.</summary>
        private static void CoastalCentre(Ctx c, float[] sea, List<string> log)
        {
            int n = c.N;
            float ox = (c.Centre % n) * Cell, oy = (c.Centre / n) * Cell;
            float best = float.MinValue; int pick = -1;
            for (int i = 0; i < n * n; i++)
            {
                if (!c.Land[i] || (c.High != null && c.High[i]) || sea[i] < 60 || sea[i] > 320) continue;
                float d = (float)Math.Sqrt(Sq((i % n) * Cell - ox) + Sq((i / n) * Cell - oy));
                if (d > 1500 || c.BuildTown[i] < 0.5f || c.Slope[i] > 0.08f) continue;
                int x = i % n, y = i / n;
                int rim = (int)(Math.Max(350f, n * Cell * 0.18f) / Cell); // room for the town on every side, not against the map edge
                if (x < rim || y < rim || x >= n - rim || y >= n - rim) continue;
                float v = 2 * c.BuildTown[i] + c.BuildWide[i] - c.Slope[i] * 5 - d / 3000f - c.Low[i] - Math.Abs(sea[i] - 150) / 400f;
                if (v > best) { best = v; pick = i; }
            }
            if (pick < 0 || pick == c.Centre) return;
            float nd = (float)Math.Sqrt(Sq((pick % n) * Cell - ox) + Sq((pick / n) * Cell - oy));
            c.Centre = pick;
            c.Net.Centre = new[] { (pick % n) * Cell, (pick / n) * Cell };
            log.Add(string.Format(CultureInfo.InvariantCulture, "The sea is near, so the town is built from the coast: centre moved {0:F0} m to gentle ground {1:F0} m from the water.", nd, sea[pick]));
        }

        /// <summary>The World type's typical lot: frontage and depth, by count (else 30 x 30).</summary>
        private static void LotSize(RoadSettings s, out float front, out float depth)
        {
            front = 30; depth = 30;
            if (s.Lots == null || s.Lots.Sizes.Count == 0) return;
            List<float> f = new List<float>(), d = new List<float>();
            foreach (int[] z in s.Lots.Sizes) for (int k = 0; k < z[2]; k++) { f.Add(z[0]); d.Add(z[1]); }
            f.Sort(); d.Sort();
            front = f[f.Count / 2]; depth = d[d.Count / 2];
        }

        /// <summary>Along the shore for a coastal town (the sea within 400 m), else the flattest way through the centre.</summary>
        private static float TownAxis(Ctx c, float[] sea, float cx, float cy, out string why)
        {
            int ci = Idx(c, cx, cy);
            if (sea[ci] < 400)
            {
                // the shore's direction: across the sea distance's gradient, averaged round the centre
                float gx = 0, gy = 0;
                for (int dy = -8; dy <= 8; dy++)
                    for (int dx = -8; dx <= 8; dx++)
                    {
                        float x = cx + dx * Cell * 2, y = cy + dy * Cell * 2;
                        if (!Inside(c, x + Cell, y + Cell, 2) || !Inside(c, x - Cell, y - Cell, 2)) continue;
                        float ex = sea[Idx(c, x + Cell, y)] - sea[Idx(c, x - Cell, y)], ey = sea[Idx(c, x, y + Cell)] - sea[Idx(c, x, y - Cell)];
                        if (Math.Abs(ex) > 30 || Math.Abs(ey) > 30) continue;
                        gx += ex; gy += ey;
                    }
                if (gx * gx + gy * gy > 1)
                {
                    why = "along the shore";
                    return (float)Math.Atan2(gx, -gy); // perpendicular to the way to the sea
                }
            }
            float best = 0, bestCost = float.MaxValue;
            for (int k = 0; k < 24; k++)
            {
                float a = (float)(k * Math.PI / 24), cost = 0;
                for (float t = -200; t <= 200; t += Cell)
                {
                    float x = cx + t * (float)Math.Cos(a), y = cy + t * (float)Math.Sin(a);
                    float x2 = x + Cell * (float)Math.Cos(a), y2 = y + Cell * (float)Math.Sin(a);
                    if (!Inside(c, x, y, 2) || !Inside(c, x2, y2, 2)) { cost += 1; continue; }
                    cost += Math.Abs(HeightAt(c, x2, y2) - HeightAt(c, x, y)) / Cell + (c.Land[Idx(c, x, y)] ? 0 : 1);
                }
                if (cost < bestCost) { bestCost = cost; best = a; }
            }
            why = "the flattest way through the centre";
            return best;
        }

        /// <summary>True if roads run round the block centred at (px, py) (its four sides' middles near a road).</summary>
        private static bool BlockRingRoaded(Ctx c, float px, float py, float ca, float sa, float bx, float by)
        {
            int got = 0;
            foreach (float[] q in new[] { new[] { bx / 2, 0f }, new[] { -bx / 2, 0f }, new[] { 0f, by / 2 }, new[] { 0f, -by / 2 } })
            {
                float x = px + q[0] * ca - q[1] * sa, y = py + q[0] * sa + q[1] * ca;
                if (Inside(c, x, y, 2) && NearRoad(c, x, y, 1)) got++;
            }
            return got == 4;
        }

        /// <summary>
        /// A promenade along the nearest gentle beach within reach of the town: the line at a lot's depth plus 6 m of
        /// verge and ~10 m of sand from the sea, followed both ways from the point nearest the centre up to ~450 m each way
        /// while the ground stays gentle and low (a beach, not a cliff), smoothed into long curves, and joined to the town.
        /// </summary>
        private static void Promenade(Ctx c, float[] sea, float cx, float cy, float depth, List<string> log)
        {
            int n = c.N;
            for (int attempt = 0; attempt < 2; attempt++)
                if (PromenadeAt(c, sea, cx, cy, depth + 16 + attempt * 30, log)) return;
        }

        private static bool PromenadeAt(Ctx c, float[] sea, float cx, float cy, float D, List<string> log)
        {
            int n = c.N;
            float reach = c.Shape != null ? Math.Max(500, c.Shape.LotDec[8]) : 600;
            float sl = c.S.SeaLevel;
            Func1 good = delegate (int i)
            {
                if (i < 0 || !c.Land[i] || c.Slope[i] > 0.14f || c.High[i]) return false;
                if (Math.Abs(sea[i] - D) > Cell * 0.9f) return false;
                return c.H.D[i] - sl < 14 + D * 0.12f; // low behind the shore: a beach or low bluff, not a cliff top
            };
            int start = -1; float bd = float.MaxValue;
            for (int i = 0; i < n * n; i++)
            {
                if (!good(i)) continue;
                float d = (float)Math.Sqrt(Sq((i % n) * Cell - cx) + Sq((i / n) * Cell - cy));
                if (d < reach && d < bd) { bd = d; start = i; }
            }
            if (start < 0) return false;
            List<float[]> a = TraceIso(c, start, good, 1, 450), b = TraceIso(c, start, good, -1, 450);
            List<float[]> line = new List<float[]>();
            for (int k = b.Count - 1; k >= 1; k--) line.Add(b[k]);
            line.AddRange(a);
            float len = 0; for (int k = 1; k < line.Count; k++) len += (float)Math.Sqrt(D2(line[k], line[k - 1]));
            if (len < 200) return false;
            line = Simplify(TerrainGen.Chaikin(TerrainGen.Chaikin(TerrainGen.Chaikin(line))), 1f, 30f);
            AddRoad(c, line, RoadKind.Main, ShoreOwn, -1);
            c.Net.Roads[c.Net.Roads.Count - 1].Designed = true;
            // joined to the town from its point nearest the centre
            int mid = Idx(c, (start % n) * Cell, (start / n) * Cell);
            List<int> link = AStar(c, mid, null, ShoreOwn, 0);
            string joined = AddPath(c, link, RoadKind.Street, ShoreOwn, -1) ? string.Format(CultureInfo.InvariantCulture, "joined to the town by a {0:F0} m street", PathLen(c, link)) : "not joined";
            log.Add(string.Format(CultureInfo.InvariantCulture, "Beach on the {0} shore, {1:F0} m from the centre: a promenade of {2:F0} m set back {3:F0} m from the water (a row of lots, then the sand), {4}; the lots between it and the beach take the beach lot.",
                Compass((start % n) * Cell - cx, (start / n) * Cell - cy), bd, len, D, joined));
            return true;
        }

        private delegate bool Func1(int i);

        /// <summary>Follows the cells 'good' accepts from 'start' (one way: dir 1 or -1 along the first heading found), 8 m a step.</summary>
        private static List<float[]> TraceIso(Ctx c, int start, Func1 good, int dir, float maxLen)
        {
            int n = c.N;
            List<float[]> pts = new List<float[]> { new[] { (start % n) * Cell, (start / n) * Cell } };
            HashSet0 seen = new HashSet0(); seen.Add(start);
            int cur = start; float hx = 0, hy = 0, len = 0;
            // first heading: along the line (the two farthest good cells within 3 cells)
            if (dir != 0)
            {
                int bestA = -1; float far = 0;
                for (int dy = -3; dy <= 3; dy++)
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        int x = cur % n + dx, y = cur / n + dy;
                        if (x < 0 || y < 0 || x >= n || y >= n || !good(y * n + x)) continue;
                        float d = dx * dx + dy * dy;
                        if (d > far) { far = d; bestA = y * n + x; }
                    }
                if (bestA < 0) return pts;
                hx = (bestA % n - cur % n) * dir; hy = (bestA / n - cur / n) * dir;
                float hl = (float)Math.Sqrt(hx * hx + hy * hy); hx /= hl; hy /= hl;
            }
            while (len < maxLen)
            {
                int next = -1; float bestScore = float.MinValue;
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int x = cur % n + dx, y = cur / n + dy, j = y * n + x;
                        if (x < 0 || y < 0 || x >= n || y >= n || seen.Has(j) || !good(j)) continue;
                        float dl = (float)Math.Sqrt(dx * dx + dy * dy), dot = (dx * hx + dy * hy) / dl;
                        if (dot < 0.5f) continue; // no sharp turns
                        float score = dot * 2 + dl * 0.3f;
                        if (score > bestScore) { bestScore = score; next = j; }
                    }
                if (next < 0) break;
                float ndx = next % n - cur % n, ndy = next / n - cur / n, nl = (float)Math.Sqrt(ndx * ndx + ndy * ndy);
                hx = 0.6f * hx + 0.4f * ndx / nl; hy = 0.6f * hy + 0.4f * ndy / nl;
                float hl2 = (float)Math.Sqrt(hx * hx + hy * hy); hx /= hl2; hy /= hl2;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) { int j = cur + dy * n + dx; if (j >= 0 && j < n * n) seen.Add(j); }
                cur = next; seen.Add(cur);
                pts.Add(new[] { (cur % n) * Cell, (cur / n) * Cell });
                len += nl * Cell;
            }
            return pts;
        }

        /// <summary>A minimal int set (net35: no HashSet without System.Core).</summary>
        private sealed class HashSet0
        {
            private readonly Dictionary<int, bool> d = new Dictionary<int, bool>();
            public void Add(int i) { d[i] = true; }
            public bool Has(int i) { return d.ContainsKey(i); }
        }

        /// <summary>Lots planned on the park's block are left out and the park put there (a community lot, kind park).</summary>
        private static void AddPark(Ctx c)
        {
            float[] p = c.Net.Park;
            float ux = p[2], uz = p[3], vx = -uz, vz = ux, w = p[4], d = p[5];
            c.Net.Lots.RemoveAll(delegate (PlannedLot l)
            {
                float[] m = l.Centre;
                float du = Math.Abs((m[0] - p[0]) * ux + (m[1] - p[1]) * uz), dv = Math.Abs((m[0] - p[0]) * vx + (m[1] - p[1]) * vz);
                float r = (float)Math.Sqrt(l.Width * l.Width + l.Depth * l.Depth) / 2;
                return du < w / 2 + r && dv < d / 2 + r;
            });
            PlannedLot park = new PlannedLot();
            park.Ux = ux; park.Uz = uz; park.Width = (int)w; park.Depth = (int)d;
            park.X = p[0] - ux * w / 2 - park.Vx * d / 2; park.Z = p[1] - uz * w / 2 - park.Vz * d / 2;
            park.Community = true; park.Kind = "park";
            c.Net.Lots.Add(park);
        }

        private static float NetKm(Ctx c)
        {
            float km = 0;
            foreach (Road r in c.Net.Roads) for (int i = 1; i < r.Points.Count; i++) km += (float)Math.Sqrt(D2(r.Points[i], r.Points[i - 1])) / 1000f;
            return km;
        }

        private static float PathLen(Ctx c, List<int> path)
        {
            if (path == null) return 0;
            float l = 0; for (int k = 1; k < path.Count; k++) l += Dist(path[k], path[k - 1], c.N) * Cell;
            return l;
        }

        /// <summary>Compass word for a map direction (x east, z south as the map is drawn).</summary>
        private static string Compass(float dx, float dz)
        {
            string[] w = { "east", "south-east", "south", "south-west", "west", "north-west", "north", "north-east" };
            double a = Math.Atan2(dz, dx); if (a < 0) a += 2 * Math.PI;
            return w[(int)Math.Round(a / (Math.PI / 4)) % 8];
        }
    }
}
