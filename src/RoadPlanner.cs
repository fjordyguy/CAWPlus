using System;
using System.Collections.Generic;

namespace CawPlus
{
    public enum RoadKind { Main = 0, Street = 1, Lane = 2 }

    /// <summary>Neighbourhood road patterns, after EA worlds: Riverview's loops, small suburban grids, cul-de-sacs, farm lanes.</summary>
    public enum HoodStyle { Loop, Grid, CulDeSac, Rural, Linear }

    internal sealed class RoadSettings
    {
        /// <summary>The planning grid with raised ponds (x, z, radius, level) under the sea, so roads keep out of them and
        /// bridge them as water (the planner only knows the sea level; #141).</summary>
        public static Grid WithPonds(Grid h, float cell, float sea, List<float[]> ponds)
        {
            if (ponds == null || ponds.Count == 0) return h;
            Grid c = new Grid(h.W, h.H);
            Array.Copy(h.D, c.D, h.D.Length);
            foreach (float[] q in ponds)
            {
                int r = (int)(q[2] * 1.7f / cell) + 1, cx = (int)(q[0] / cell), cz = (int)(q[1] / cell);
                for (int j = Math.Max(0, cz - r); j <= Math.Min(h.H - 1, cz + r); j++)
                    for (int i = Math.Max(0, cx - r); i <= Math.Min(h.W - 1, cx + r); i++)
                        if (c[i, j] < q[3]) c[i, j] = sea - 1;
            }
            return c;
        }


        public int Seed = 1;
        public bool Downtown = true;      // main street with a few blocks
        public int DowntownBlocks = 4;    // blocks along the main street (2..8)
        public int Neighbourhoods = 5;    // residential areas (0..8)
        public int CommunityLots = 4;     // sites for parks, school, shops... (0..8)
        public int EdgeRoads = 2;         // roads in from the world edge (0..3)
        public float Variety = 60;        // 0 = all neighbourhoods alike, 100 = every style mixed
        public float MaxGrade = 8;        // percent; EA worlds measured (tools/roadgrade.py): ~20 % of road length is steeper than 10 %
        public float SeaLevel = 10;
        public bool CentralTown = true;   // town centre in the middle of the map, like EA's worlds
        public bool Bridges = true;       // roads may cross water up to MaxBridgeWater wide on a bridge
        public bool Footpaths = true;     // promenades, neighbourhood trails and a park loop (PathPlanner)
        public bool TownLoops = true;     // loop roads round the downtown core (Bridgewood, EA towns)
        public float FootpathKm;          // with a Like world: add trails up to its footpath length (0 = only the above)
        public float PathRoadMin = 30, PathRoadMax = 200; // added trails start 30-200 m from roads (the Like world's p10-p90)
        public List<float[]> PathBlockers;   // circles (x, z, r; map metres) footpaths keep out of: lots, existing objects (#136)
        public float TargetKm;            // with a Like world: side streets are added until the town has about this much road (0 = off)
        public float DeadEndShare = 0.4f; // share of those side streets left as dead ends (EA: capped dead ends / junctions)
        public float BuildSlope = 10;     // percent: steepest land counted as buildable (EA: 3-33 %, tools/roadgrade.py land_slope_at_roads p75)
        public float SideCost = 1f;       // how strongly routes avoid running across steep hillsides (was 2.5; 1 matches EA best: roads 71.9 -> 72.8%, seeds 4-6)
        public float ClimbCost = 12;      // how strongly routes avoid climbing (cost x (1 + ClimbCost (grade/max)^2)); EA's roads climb more on hilly worlds
        public RoadTargets Targets;       // the Like world's measured town (RoadChecks); null = no checks
        public DowntownGrid Grid;         // the Like world's downtown grid and roads out of town; null = the built-in downtown
        public RoadProfile Profile;       // the Like world's road design profile (checked, not laid); null = none
        public TownShape Town;            // the Like world's town shape (side streets and lots spread like it); null = none
        public LotTargets Lots;           // the Like world's lots (LotPlanner); null = no lots planned
        public TerrainProfile LotsLand;   // the Like world's terrain, to scale its lot count to this map's land
        public float LotsWorldM;          // the Like world's size in metres
        public float LotsAmount = 1;      // x the scaled count
        public RoadCharacter Character;   // the world type's region: geography checks and hints (src/RoadCharacter.cs); null = none
        public string WorldType = "";     // the preset Targets/Lots/LotsLand/Character came from (CAWPlusHelper looks them up by it)
        public float[] CentreHint;        // x, z metres: the town centre belongs near here (a river fork's point, #139); null = none
        public float[] Ring;              // x, z, radius as shares of the map size: a road all the way round this circle (an atoll's islands, Sunlit Tides); null = none

        public RoadSettings Clone() { return (RoadSettings)MemberwiseClone(); }
    }

    internal sealed class Road
    {
        public RoadKind Kind;
        public int Area = -1;                               // -1 town/downtown, else neighbourhood index
        public List<float[]> Points = new List<float[]>(); // metres
        internal List<int> Cells = new List<int>();         // planning-grid cells it runs over
        internal List<float[]> Spans = new List<float[]>();  // bridges on this road (x1, y1, x2, y2)
        public bool Straight;                               // a downtown grid street or a straight side street: kept straight
    }

    internal sealed class LotSite
    {
        public string Label;
        public float[] P;   // metres
    }

    internal sealed class RoadNet
    {
        public List<Road> Roads = new List<Road>();
        public List<float[]> Junctions = new List<float[]>(); // metres, where 3+ road ends meet or a road ends on another
        public List<LotSite> Sites = new List<LotSite>();     // community lot sites
        public List<HoodStyle> Hoods = new List<HoodStyle>();
        public float MotorwayKm;                              // the through road's length (desert towns), 0 = none
        public List<float[]> HoodCentres = new List<float[]>(); // metres, one per entry in Hoods
        public int LoopArcs, LoopVerts; // TownLoops: arcs built, ring points found
        public int RingArcs;            // RingRoad: arcs built of 24
        public int ShoreArcs;           // ShoreRoad: arcs built
        public float TownRadius;                               // downtown radius, metres
        public List<FootPath> Paths = new List<FootPath>();    // footpaths (PathPlanner)
        public float PathKm;
        public int PathEndsJoined;                             // path ends led on to a road verge or another path
        public List<float[]> Bridges = new List<float[]>();    // metres: (x1, y1, x2, y2) bank to bank, straight
        public int LinksStraightened;                          // short links made straight and square between parallel roads (#134)
        public int DeadEndsJoined, DeadEndsTrimmed, DeadEndsDropped; // dead ends that stopped near another road (DeadEndClearance)
        public int IslandsJoined, IslandsDropped, IslandsLeft;     // cut-off road groups (ReconnectIslands): linked, dropped, still apart
        public float IslandsDroppedKm;
        public List<PlannedLot> Lots = new List<PlannedLot>(); // lots fronting the roads (LotPlanner)
        public int LotsWanted;
        public int HoodsAsked;                                 // neighbourhoods asked for (Hoods holds those that fit the land)
        public float[] Centre;
        public float TotalKm;
    }

    /// <summary>
    /// Plans a Sims 3-style town on the 8 m grid, as a checklist: a downtown main street with a few blocks, roads in
    /// from the world edge, several neighbourhoods each in its own pattern (loop, small grid, cul-de-sacs, farm lane),
    /// and sites for community lots. Routes follow the terrain: steep ground costs a lot, beyond the grade limit is off
    /// limits, water is impassable. Roads end on other roads (T-junctions) or at grid crossings, never cross mid-way.
    /// Reference: Riverview's road graph (decoded from its .world): small central grid, 400-700 m curving loops.
    /// </summary>
    internal static class RoadPlanner
    {
        private const float Cell = TerrainGen.CoarseCell;
        public const float MaxBridgeWater = 56f;  // metres of water a bridge may cross (EA bridge pieces span 65-72 m)
        private const float BridgePenalty = 260f; // metres of extra route a bridge is worth, so it's used only to save a detour
        private const int Town = 0;          // owner id of downtown and edge roads; neighbourhood i = i + 1; sites = 100 + i
        private const int NoOwner = -99;
        private const int Ring = 400;        // owner id of the loop roads round the core
        private const int RingOwn = 450;     // owner id of an atoll's ring road
        private const int ArteryOwn = 460;   // owner ids of the town's main roads out (460 + k)
        private const int ShoreOwn = 470;
        private const int MotorOwn = 490;    // owner id of a desert town's through road    // owner id of a coastal town's shore road

        private sealed class Ctx
        {
            public Grid H; public int N; public bool[] Land; public float[] Flat;
            public int[] OnRoad;   // 0 = none, else kind + 1
            public int[] Owner;
            public float MaxGrade; public Random Rng; public RoadNet Net; public RoadSettings S;
            public int Centre;
            public TownShape Shape;  // the Like world's town shape scaled to this town's lots (null = none)
            public float[] Slope;      // ground slope (rise/run), lightly blurred
            public float[] Build;      // share of buildable ground (land, slope < 10%) within ~40 m
            public float[] BuildWide;  // the same within about a neighbourhood's radius
            public float[] BuildTown;  // the same within ~120 m (downtown)
            public float[] Ridge;      // metres above the 150 m-blurred terrain: > 0 on hills and ridges
            public float[] Low;        // 0 at sea level .. 1 at the top: roads prefer low ground
            public float[] Shore;      // metres to the nearest water
            // A* buffers (direction-aware states), reused between calls
            public float[] G; public int[] From; public int[] Stamp; public int CurStamp;
            public List<float[]> BridgeMids = new List<float[]>(); // metres, bridges planned so far
            public float[] Crowd; public int[] CrowdOwner;            // nearness to existing roads (0..1) and whose
        }

        /// <summary>
        /// Plans up to 'tries' layouts (the road seed, then variations of it) and keeps the first that passes every road
        /// check against the Like world's town (RoadChecks), else the one that passes most. Without targets, one plan.
        /// </summary>
        public static RoadNet PlanChecked(Grid h, RoadSettings s, int tries, out RoadChecks.Result result)
        {
            return PlanChecked(h, s, tries, null, out result);
        }

        /// <summary>As above; the layouts are planned in parallel, and 'stop' abandons them (result null).</summary>
        public static RoadNet PlanChecked(Grid h, RoadSettings s, int tries, Work.Stop stop, out RoadChecks.Result result)
        {
            if (s.Grid != null && s.Grid.StreetsA + s.Grid.StreetsB <= 2) s.Downtown = false; // no centre streets at all (Sunlit Tides: houses along the main road)
            if (s.Grid != null && !s.Grid.HasGrid) s.Grid = null; // the world type's centre isn't a grid
            result = null;
            if ((s.Targets == null && s.Character == null && s.Town == null && s.Grid == null && s.Profile == null) || tries <= 1) return Plan(h, s);
            float worldM = (h.W - 1) * Cell;
            RoadNet[] nets = new RoadNet[tries];
            RoadChecks.Result[] found = new RoadChecks.Result[tries];
            Work.For(tries, delegate (int t)
            {
                RoadSettings q = s.Clone(); q.Seed = s.Seed + t * 1009;
                nets[t] = Plan(h, q);
                found[t] = s.Targets != null ? RoadChecks.Check(RoadChecks.Measure(nets[t], worldM), s.Targets, worldM) : new RoadChecks.Result();
                if (s.Character != null) s.Character.AddChecks(found[t], RoadCharacter.Measure(nets[t], h, Cell, s.SeaLevel));
                if (s.Town != null) s.Town.AddCheck(found[t], nets[t], s.Lots != null ? s.Lots.Count : 0);
                if (s.Grid != null) s.Grid.AddCheck(found[t], nets[t]);
                if (s.Profile != null) s.Profile.AddCheck(found[t], nets[t], h, Cell, s.SeaLevel);
                if (nets[t].LotsWanted > 0) // the lots the world type has: a layout that can't take them isn't its town
                {
                    RoadChecks.Result f = found[t];
                    double got = Math.Min(1.0, nets[t].Lots.Count / (0.9 * nets[t].LotsWanted));
                    f.Score = (f.Score * f.Total + got) / (f.Total + 1); f.Total++;
                    if (got >= 0.999) f.Passed++; else f.Failed.Add("lots");
                }
            }, stop);
            if (stop != null && stop.Requested) return null;
            // a must, not a vote: no stretch of road running close beside another (or doubling back beside itself).
            // Build roads would join their ends into one junction and lay the pieces on top of each other.
            bool[] clean = new bool[tries];
            for (int t = 0; t < tries; t++)
                if (nets[t] != null && found[t] != null)
                {
                    clean[t] = Alongside(nets[t]) == 0;
                    RoadChecks.Result f = found[t];
                    f.Score = (f.Score * f.Total + (clean[t] ? 1 : 0)) / (f.Total + 1); f.Total++;
                    if (clean[t]) f.Passed++; else f.Failed.Add("roads alongside");
                }
            // musts before similarity (#120, fjordyguy's world-design notes: never trade a must for a closer score): roads all
            // connected, at least 90% of the world type's lots placed, no road running beside another. Only layouts passing
            // all three are compared (the most checks passed, then the closest; ties: the lowest try). If none does, the
            // one passing the most musts is kept as a fallback, and the result says so.
            const int AllMusts = 3;
            int[] musts = new int[tries];
            bool anyAll = false;
            for (int t = 0; t < tries; t++)
            {
                if (nets[t] == null || found[t] == null) continue;
                musts[t] = (clean[t] ? 1 : 0) + (found[t].Failed.Contains("connected") ? 0 : 1)
                         + (nets[t].LotsWanted <= 0 || nets[t].Lots.Count >= 0.9f * nets[t].LotsWanted ? 1 : 0);
                anyAll |= musts[t] == AllMusts;
            }
            RoadNet best = null; int bestMusts = -1, bestT = -1;
            for (int t = 0; t < tries; t++)
            {
                RoadChecks.Result r = found[t];
                if (r == null || (anyAll && musts[t] != AllMusts)) continue;
                if (best == null || musts[t] > bestMusts || (musts[t] == bestMusts && (r.Passed > result.Passed || (r.Passed == result.Passed && r.Score > result.Score))))
                { best = nets[t]; result = r; bestMusts = musts[t]; bestT = t; }
            }
            if (best != null && !anyAll)
            {
                List<string> miss = new List<string>();
                if (found[bestT].Failed.Contains("connected")) miss.Add("roads connected");
                if (!(best.LotsWanted <= 0 || best.Lots.Count >= 0.9f * best.LotsWanted)) miss.Add("90% of the lots");
                if (!clean[bestT]) miss.Add("no road beside another");
                result.Fallback = string.Format("no layout of {0} passed every must; kept the best, missing: {1}", tries, string.Join(", ", miss.ToArray()));
            }
            return best;
        }

        public static RoadNet Plan(Grid h, RoadSettings s)
        {
            int n = h.W;
            Ctx c = new Ctx();
            c.H = h; c.N = n; c.S = s; c.Net = new RoadNet();
            c.Rng = new Random(s.Seed * 104729 + 7);
            c.MaxGrade = s.MaxGrade / 100f;
            c.Land = new bool[n * n];
            for (int i = 0; i < n * n; i++) c.Land[i] = h.D[i] > s.SeaLevel + 0.5f;
            c.Flat = Flatness(h, c.Land);
            c.OnRoad = new int[n * n];
            c.Owner = new int[n * n];
            c.Crowd = new float[n * n]; c.CrowdOwner = new int[n * n];
            float worldSize = (n - 1) * Cell;
            float R = Clamp(worldSize * 0.11f, 90, 230); // neighbourhood radius, metres
            Measure(c, R);
            int lotsWanted = s.Lots != null ? (int)Math.Round(LotPlanner.Wanted(s.Lots, s.LotsLand, s.LotsWorldM, h, Cell, s.SeaLevel) * s.LotsAmount) : 0;
            if (s.Town != null) c.Shape = s.Town.Scaled(TownShape.ScaleFor(lotsWanted, s.Lots != null ? s.Lots.Count : 0));

            // 1. Town centre: the biggest flat area, low, not on a hill; with CentralTown, in the middle of the map
            //    (EA's worlds have the main town central, roads spreading out to the edges); else anywhere inland.
            float lo, hi; h.MinMax(out lo, out hi);
            int centre = -1; float best = float.MinValue;
            int[] lims = s.CentreHint != null ? new[] { n / 10 } : s.CentralTown ? new[] { n * 3 / 10, n / 6 } : new[] { n / 6 };
            foreach (int lim in lims)
            {
                for (int y = lim; y < n - lim; y++)
                    for (int x = lim; x < n - lim; x++)
                    {
                        int i = y * n + x;
                        if (!c.Land[i]) continue;
                        float mid = 1 - (float)Math.Sqrt(Sq(x - n / 2f) + Sq(y - n / 2f)) / (n * 0.5f);
                        float rel = (h.D[i] - lo) / Math.Max(1, hi - lo);
                        float v = c.BuildTown[i] * 2.2f + c.Flat[i] * 0.6f + mid * (s.CentralTown ? 2.5f : 0.8f) - rel * 0.8f
                                  - Math.Max(0, c.Ridge[i]) / 15f + (float)c.Rng.NextDouble() * 0.2f;
                        if (c.BuildTown[i] < 0.35f) v -= 2; // not enough room for a town here
                        // coastal towns (Icelandic fjord towns, harbours, island and waterfront cities) sit by the water
                        if (s.Character != null && s.Character.CoastPull) v += 1.5f * (1 - Math.Min(1, Math.Abs(c.Shore[i] - 90) / 150f));
                        // (a river fork: the town on its point, Pittsburgh-style; the hint outweighs being central)
                        if (s.CentreHint != null) v += 8f * (1 - Math.Min(1, (float)Math.Sqrt(Sq(x * Cell - s.CentreHint[0]) + Sq(y * Cell - s.CentreHint[1])) / 300f));
                        if (v > best) { best = v; centre = i; }
                    }
                if (centre >= 0 && c.BuildTown[centre] >= 0.35f) break;
            }
            if (centre < 0) return c.Net;
            c.Centre = centre;
            c.Net.Centre = new[] { (centre % n) * Cell, (centre / n) * Cell };

            // 1b. A through road across the map beside the town (the tester, #91: as Lucky Palms has), laid first so the town's
            // roads are built against it and join it (a later route can't cross the town's roads)
            if (s.Character != null && s.Character.Motorway) Motorway(c, worldSize);

            // 2. Downtown: main street with a few blocks either side.
            float townRadius = 60;
            if (s.Downtown || s.Grid != null) townRadius = Downtown(c);
            c.Net.TownRadius = townRadius;
            // 2a. With the Like world's grid: main roads straight out of the town in its number of directions (the tester's
            // mark-up: 67-79% of EA's main road runs out from the centre, not round it), instead of the loops below
            if (s.Grid != null) Arterials(c, townRadius, worldSize);
            // 2b. Loop roads round the core, following the land (Bridgewood and most EA towns: a small grid wrapped in
            // curving loops, not long straight roads out to far neighbourhoods; the tester's v0.40 test)
            // (not in towns strung along one road: Icelandic fjord towns, French village-rue)
            if (s.TownLoops && s.Grid == null && (s.Character == null || !s.Character.Linear)) TownLoops(c, townRadius);
            // 2c. Ring road round an atoll (the tester: Sunlit Tides' roads should go round in a circle), bridging the channels
            if (s.Ring != null) RingRoad(c, new[] { s.Ring[0] * worldSize, s.Ring[1] * worldSize, s.Ring[2] * worldSize });
            // 2d. Shore road through a coastal town (Icelandic fjord towns, tropical islands: EA's Aurora Skies has 72% of its
            // road within 80 m of the sea, Isla Paradiso 68%; roads only, seeds 4-6: Aurora 65.7 -> 68.6, Isla 59.4 -> 65.1;
            // waterfront cities scored worse with it: Bridgeport -1.9, Roaring Heights -1.3)
            else if (s.Character != null && s.Character.ShoreRoad) ShoreRoad(c, Math.Max(350f, worldSize * 0.25f));

            // 3. Roads in from the world edge (the first one to the centre if there is no downtown).
            List<int> edges = EdgeCells(c, s.EdgeRoads);
            for (int k = 0; k < edges.Count; k++)
            {
                List<int> path = c.OnRoad[centre] == 0 && k == 0 ? AStar(c, edges[k], new[] { centre }, NoOwner, 0)
                                                                : AStar(c, edges[k], null, NoOwner, 0);
                AddPath(c, path, RoadKind.Main, Town, -1);
            }
            if (c.OnRoad[centre] == 0) { MarkCell(c, centre, RoadKind.Main, Town); }

            // 4. Neighbourhoods, each in its own pattern, joined to the town.
            // candidate spots, best first; a neighbourhood that doesn't fit the land is skipped for the next spot
            // with the Like world's grid: EA's neighbourhoods are small (1-6 ha, 150-350 m across) and many (6-10), 100-500 m
            // from the centre, mostly one street lined with houses or a short cul-de-sac (the tester's mark-up)
            int hoods = s.Neighbourhoods;
            float hoodR = R;
            if (s.Grid != null)
            {
                int wantLots = s.Lots != null ? (int)Math.Round(LotPlanner.Wanted(s.Lots, s.LotsLand, s.LotsWorldM, h, Cell, s.SeaLevel) * s.LotsAmount) : 80;
                hoods = Math.Max(3, Math.Min(10, (int)Math.Round(wantLots * 0.55f / 7f)));
                hoodR = Clamp(worldSize * 0.06f, 70, 130);
            }
            c.Net.HoodsAsked = hoods;
            List<int> hubs = s.Grid != null ? Hubs(c, hoods * 3, townRadius + 40, hoodR * 2.0f, hoodR)
                                            : Hubs(c, hoods * 3, townRadius + R * 1.1f, R * 2.1f, R);
            List<HoodStyle> styles = s.Grid != null ? SmallStyles(c, hoods) : Styles(c, hoods);
            if (s.Grid != null) R = hoodR;
            for (int k = 0, made = 0; k < hubs.Count && made < hoods; k++)
            {
                int own = k + 1;
                HoodStyle st = styles[made];
                // let the land decide what fits: grids only on wide flat ground, loops on fairly open ground,
                // otherwise a street that follows the valley (cul-de-sacs)
                float open = c.BuildWide[hubs[k]];
                if (st == HoodStyle.Grid && open < 0.75f) st = HoodStyle.Loop;
                if (st == HoodStyle.Loop && open < 0.6f) st = HoodStyle.CulDeSac;
                if (FreeOfOthers(c, (hubs[k] % n) * Cell, (hubs[k] / n) * Cell, own) == false) continue;
                bool ok;
                switch (st)
                {
                    case HoodStyle.Loop: ok = LoopHood(c, hubs[k], R, own); break;
                    case HoodStyle.Linear: ok = LinearHood(c, hubs[k], R, own); break;
                    case HoodStyle.Grid: ok = GridHood(c, hubs[k], own); break;
                    case HoodStyle.CulDeSac: ok = CulDeSacHood(c, hubs[k], R, own); break;
                    default: ok = RuralHood(c, hubs[k], R, own); break;
                }
                if (ok) { c.Net.Hoods.Add(st); c.Net.HoodCentres.Add(new[] { (hubs[k] % n) * Cell, (hubs[k] / n) * Cell }); made++; }
            }

            // 5. Community lot sites (park, school, beach...), with a short spur road if not already on a road.
            Sites(c, s.CommunityLots);

            // 6. Every separate piece joined to the town, or dropped if it can't be.
            ConnectAll(c);

            // 6b. Side streets: EA towns have many short streets off the main roads (T-junctions, dead ends). Add them into
            // buildable land no road serves yet until the Like world's road length is reached, and, with lots, until the
            // roads can front as many lots as the Like world has (the tester: lots first, the roads built round them).
            float targetKm = s.TargetKm;
            // with the Like world's grid the neighbourhoods are small, so the side streets make up the world's road
            // length even where the preset had them off (they were off where big neighbourhoods already gave it)
            if (s.Grid != null && targetKm <= 0 && s.Targets != null && !float.IsNaN(s.Targets.Km))
                targetKm = s.Targets.Km * (s.Targets.WorldM > 0 ? worldSize * worldSize / (s.Targets.WorldM * s.Targets.WorldM) : 1);
            c.Net.LotsWanted = lotsWanted;
            if (lotsWanted > 0)
            {
                int onRoadWanted = (int)Math.Round(lotsWanted * Math.Max(0.1f, Math.Min(1f, s.Lots.OnRoad)));
                int fit = LotPlanner.Place(h, Cell, c.Net, s.SeaLevel, s.Lots, lotsWanted, s.Seed, true, c.Shape).Count;
                if (fit < onRoadWanted)
                {
                    float km = 0;
                    foreach (Road r in c.Net.Roads) for (int i = 1; i < r.Points.Count; i++) km += (float)Math.Sqrt(Sq(r.Points[i][0] - r.Points[i - 1][0]) + Sq(r.Points[i][1] - r.Points[i - 1][1])) / 1000f;
                    // about 40% of a road's two sides takes lots (bends, junctions, steep or wet ground, gaps)
                    float more = (onRoadWanted - fit) * s.Lots.FrontagePerLot() / (2 * 0.4f) / 1000f;
                    float cap = s.TargetKm > 0 ? s.TargetKm * 1.6f : km + more;
                    targetKm = Math.Max(targetKm, Math.Min(cap, km + more));
                }
            }
            if (targetKm > 0) Infill(c, targetKm);

            // 7. Smooth, find junctions, measure.
            // which road ends meet another road (a T or a crossing node), before smoothing moves things
            List<bool[]> meets = EndsMeeting(c);
            // grid staircases and small switchbacks out first (9 m), then round the corners
            foreach (Road r in c.Net.Roads)
            {
                if (r.Straight) { r.Points = Simplify(r.Points, 0.5f, 40f); continue; } // grid and straight streets as laid
                if (TautRoads && r.Spans.Count == 0)
                {
                    // straight runs where the land allows, joined by round bends (#162, the tester: roads wobbly, hooks at bends)
                    r.Points = Simplify(Fillet(Taut(c, r, Resample(r.Points, Cell)), BendRadius(r.Kind)), 0.5f, 30f);
                    continue;
                }
                List<float[]> p = Simplify(r.Points, 9f, 64f);
                p = Relax(c, Resample(p, Cell), 14, 7f); // bends eased into curves (straight grid streets stay straight)
                p = TerrainGen.Chaikin(TerrainGen.Chaikin(p));
                // no bends tighter than ~30 m radius (EA's Riverview: 0.3% of its road under 20 m; CAW draws tight
                // bends badly and refuses some)
                p = Ease(c, Resample(p, 6f), 32f, 80, 20f);
                // fair the line: small S-wiggles out (the tester: roads looked hand-drawn; ours had 3-7 wiggles per km at
                // 10 m steps, EA's 0.3-2.5), keeping the bends that follow the land
                p = Relax(c, Resample(p, 10f), 30, 4f);
                r.Points = Simplify(p, 1f, 30f);
            }
            SnapEnds(c.Net, meets);
            c.Net.Roads.RemoveAll(delegate (Road r) { return r.Points.Count < 2; });
            MergeEnds(c.Net);
            StraightEnds(c);
            StraightLinks(c);
            DeadEndClearance(c);
            ReconnectIslands(c);
            // the bend where a straightened end rejoins its road eased again (ends and straight runs stay)
            foreach (Road r in c.Net.Roads) if (r.Spans.Count == 0 && !r.Straight) r.Points = Simplify(Ease(c, Resample(r.Points, 6f), 32f, 40, 8f), 1f, 30f);
            foreach (Road r in c.Net.Roads) c.Net.Bridges.AddRange(r.Spans);
            c.Net.Junctions = FindJunctions(c.Net);
            foreach (Road r in c.Net.Roads)
                for (int i = 1; i < r.Points.Count; i++) c.Net.TotalKm += (float)Math.Sqrt(Sq(r.Points[i][0] - r.Points[i - 1][0]) + Sq(r.Points[i][1] - r.Points[i - 1][1])) / 1000f;

            // 7b. Lots along the finished roads.
            if (lotsWanted > 0) c.Net.Lots = LotPlanner.Place(h, Cell, c.Net, s.SeaLevel, s.Lots, lotsWanted, s.Seed, false, c.Shape);

            // 8. Footpaths: a network of their own between and beside the roads.
            if (s.Footpaths) PathPlanner.Plan(h, c.Net, s);
            return c.Net;
        }

        // ---------- the town's main roads out ----------

        /// <summary>
        /// Main roads leaving the town in the Like world's number of directions (DowntownGrid.LayOut, 3-8), spread round
        /// it: each from a spot 380-650 m out on buildable land, routed (A*, the usual costs) in to the town, where it ends
        /// in a T. The main street already leaves both ways, so those directions are skipped. Neighbourhoods and side
        /// streets then hang off these roads, as on the tester's mark-up of EA's towns.
        /// </summary>
        private static void Arterials(Ctx c, float townRadius, float worldSize)
        {
            int n = c.N, want = c.S.Grid.LayOut;
            float cx = (c.Centre % n) * Cell, cy = (c.Centre / n) * Cell;
            // directions already leaving the town (300 m circle)
            List<double> taken = new List<double>();
            foreach (Road r in c.Net.Roads)
                for (int i = 1; i < r.Points.Count; i++)
                {
                    float da = (float)Math.Sqrt(Sq(r.Points[i - 1][0] - cx) + Sq(r.Points[i - 1][1] - cy)), db = (float)Math.Sqrt(Sq(r.Points[i][0] - cx) + Sq(r.Points[i][1] - cy));
                    float rr = Math.Max(townRadius + 20, 120);
                    if ((da - rr) * (db - rr) < 0) taken.Add(Math.Atan2(r.Points[i][1] - cy, r.Points[i][0] - cx));
                }
            double start = c.Rng.NextDouble() * 2 * Math.PI;
            float far = Clamp(c.Shape != null ? c.Shape.LotDec[8] : 500, 380, 650);
            int made = 0;
            for (int k = 0; k < want * 3 && taken.Count < want && made < want; k++)
            {
                double th = start + 2 * Math.PI * (k % want) / want + (k / want) * 0.35;
                bool near = false;
                foreach (double t in taken) if (Math.Abs(Math.IEEERemainder(th - t, 2 * Math.PI)) < Math.PI / (want + 1)) near = true;
                if (near) continue;
                // the land spot furthest out along that way (up to 'far'), buildable and dry
                int target = -1;
                for (float d = far; d >= townRadius + 120; d -= Cell * 2)
                {
                    float x = cx + (float)Math.Cos(th) * d, y = cy + (float)Math.Sin(th) * d;
                    if (!Inside(c, x, y, (int)(90 / Cell))) continue;
                    int i = Idx(c, x, y);
                    if (c.Land[i] && c.OnRoad[i] == 0 && c.Build[i] >= 0.3f) { target = i; break; }
                }
                if (target < 0) continue;
                List<int> path = AStar(c, target, null, ArteryOwn + k, 0);
                if (path == null || path.Count < 6) continue;
                float straight = Dist(target, path[path.Count - 1], n) * Cell, len = 0;
                for (int q = 1; q < path.Count; q++) len += Dist(path[q], path[q - 1], n) * Cell;
                if (len > 1.8f * straight + 80) continue; // a long way round a hill: not a road out
                if (AddPath(c, path, RoadKind.Main, ArteryOwn + k, -1)) { taken.Add(th); made++; }
            }
        }

        /// <summary>Neighbourhood patterns with the Like world's grid: mostly one street lined with houses or a short
        /// cul-de-sac, sometimes a loop, a farm lane furthest out (the tester's mark-up of Sunset Valley, Barnacle Bay,
        /// Appaloosa Plains: 13 linear, 9 cul-de-sac, 1 loop, 1 grid, 1 rural).</summary>
        private static List<HoodStyle> SmallStyles(Ctx c, int count)
        {
            HoodStyle[] mix = { HoodStyle.Linear, HoodStyle.CulDeSac, HoodStyle.Linear, HoodStyle.Linear, HoodStyle.CulDeSac, HoodStyle.Loop, HoodStyle.Linear, HoodStyle.CulDeSac, HoodStyle.Rural, HoodStyle.Linear };
            List<HoodStyle> pool = new List<HoodStyle>();
            for (int k = 0; k < count; k++) pool.Add(mix[k % mix.Length]);
            for (int i = pool.Count - 1; i > 0; i--) { int j = c.Rng.Next(i + 1); HoodStyle t = pool[i]; pool[i] = pool[j]; pool[j] = t; }
            pool.Sort(delegate (HoodStyle a, HoodStyle b) { return (a == HoodStyle.Rural ? 1 : 0).CompareTo(b == HoodStyle.Rural ? 1 : 0); });
            return pool;
        }

        /// <summary>
        /// One straight street lined with houses (EA's commonest neighbourhood): from the road nearest the spot, straight
        /// out through it for 1.4-2.2 R, turning by up to 30 deg where the land won't take it straight; it ends where it
        /// meets another road or as a dead end.
        /// </summary>
        private static bool LinearHood(Ctx c, int hub, float R, int own)
        {
            int n = c.N, area = own - 1;
            float hx = (hub % n) * Cell, hy = (hub / n) * Cell;
            int near = NearestRoad(c, hub % n, hub / n, (int)(260 / Cell));
            if (near < 0) return false;
            float sx = (near % n) * Cell, sy = (near / n) * Cell;
            float dx = hx - sx, dy = hy - sy, dl = (float)Math.Sqrt(dx * dx + dy * dy);
            if (dl < 20) { float[] cd = ContourDir(c, hx, hy); dx = cd[0]; dy = cd[1]; dl = 1; }
            dx /= dl; dy /= dl;
            float len = R * (1.4f + 0.8f * (float)c.Rng.NextDouble()) + dl;
            foreach (float turn in new[] { 0f, 0.26f, -0.26f, 0.52f, -0.52f })
            {
                float tx = dx * (float)Math.Cos(turn) - dy * (float)Math.Sin(turn), ty = dx * (float)Math.Sin(turn) + dy * (float)Math.Cos(turn);
                if (StraightStreet(c, sx, sy, tx, ty, len, Math.Min(len, 90), own, area, RoadKind.Street)) return true;
            }
            return false;
        }

        /// <summary>
        /// A straight street from a road at (x, y) along (dx, dy): cell by cell on land, within the grade limit, clear of
        /// the map rim; it stops where it meets another road (a T) or after 'maxLen'. Added if at least 'minLen' long.
        /// </summary>
        private static bool StraightStreet(Ctx c, float x, float y, float dx, float dy, float maxLen, float minLen, int own, int area, RoadKind kind)
        {
            bool joined;
            return StraightStreet(c, x, y, dx, dy, maxLen, minLen, own, area, kind, out joined);
        }

        private static bool StraightStreet(Ctx c, float x, float y, float dx, float dy, float maxLen, float minLen, int own, int area, RoadKind kind, out bool joined)
        {
            joined = false;
            List<float[]> pts = new List<float[]> { new[] { x, y } };
            int rim = (int)(80 / Cell);
            float px = x, py = y;
            for (float d = Cell; d <= maxLen; d += Cell)
            {
                float qx = x + dx * d, qy = y + dy * d;
                if (!Inside(c, qx, qy, rim)) break;
                int i = Idx(c, qx, qy);
                if (!c.Land[i] || Math.Abs(HeightAt(c, qx, qy) - HeightAt(c, px, py)) / Cell > c.MaxGrade) break;
                if (d > 24 && c.OnRoad[i] != 0) { pts.Add(new[] { qx, qy }); joined = true; break; } // meets another road: a T
                if (d > 24 && c.Crowd[i] > 0.6f && c.CrowdOwner[i] != own) break;     // running alongside another road
                pts.Add(new[] { qx, qy }); px = qx; py = qy;
            }
            if ((pts.Count - 1) * Cell < minLen) { joined = false; return false; }
            AddRoad(c, pts, kind, own, area);
            c.Net.Roads[c.Net.Roads.Count - 1].Straight = true;
            return true;
        }

        // ---------- loops round the core ----------

        /// <summary>
        /// A ring of loop roads round the downtown, 120-250 m out: ~10 points round the core on buildable, gentle land away
        /// from ridges and water, each pair joined by the cheapest route (so the ring curves round hills and stops where the
        /// land does, e.g. at the sea, leaving arcs); then 3-4 links from the ring in to the core. Bridgewood's town is this
        /// shape: a 2 x 3 block grid with loops round it.
        /// </summary>
        private static void TownLoops(Ctx c, float townRadius)
        {
            int n = c.N, before = c.Net.Roads.Count;
            float cx = (c.Centre % n) * Cell, cy = (c.Centre / n) * Cell;
            float ringR = Math.Max(170, townRadius + 120) * (0.9f + 0.35f * (float)c.Rng.NextDouble());
            int count = 8 + c.Rng.Next(3);
            double phase = c.Rng.NextDouble() * Math.PI * 2;
            int[] verts = new int[count];
            float[] scales = { 1f, 0.88f, 1.12f, 0.76f, 1.25f };
            for (int k = 0; k < count; k++)
            {
                verts[k] = -1;
                double th = phase + 2 * Math.PI * k / count + (c.Rng.NextDouble() - 0.5) * 0.35;
                float rk = ringR * (0.78f + 0.44f * (float)c.Rng.NextDouble()); // uneven: an organic loop, not a polygon
                foreach (float sc in scales)
                {
                    float x = cx + (float)Math.Cos(th) * rk * sc, y = cy + (float)Math.Sin(th) * rk * sc;
                    if (!Inside(c, x, y, 12)) continue;
                    int i = Idx(c, x, y);
                    if (!c.Land[i] || c.OnRoad[i] != 0 || c.Shore[i] < ShoreKeep * 1.5f || c.Build[i] < 0.4f || c.Ridge[i] > 8) continue;
                    verts[k] = i; break;
                }
            }
            int arcs = 0;
            foreach (int v in verts) if (v >= 0) c.Net.LoopVerts++;
            for (int k = 0; k < count; k++)
            {
                int a = verts[k], b = verts[(k + 1) % count];
                if (a < 0 || b < 0) continue;
                List<int> path = AStar(c, a, new[] { b }, Ring, 0);
                if (path == null) continue;
                float len = 0; for (int q = 1; q < path.Count; q++) len += Dist(path[q], path[q - 1], n) * Cell;
                if (len > 2.2f * Dist(a, b, n) * Cell + 40) continue; // a long detour round a hill or bay: leave a gap
                if (AddPath(c, path, RoadKind.Street, Ring, -1)) arcs++;
            }
            c.Net.LoopArcs = arcs;
            if (arcs < 3) { RemoveFrom(c, before, Ring); return; }
            // links in to the core from points spread round the ring
            int links = 0, want = 3 + c.Rng.Next(2);
            for (int k = 0; k < count && links < want; k += Math.Max(1, count / want))
            {
                if (verts[k] < 0) continue;
                List<int> link = AStar(c, verts[k], new[] { c.Centre }, Ring, 0);
                if (link != null && link.Count * Cell < ringR * 2) { AddPath(c, link, RoadKind.Street, Ring, -1); links++; }
            }
        }

        /// <summary>
        /// A road round a circle (centre x, z, radius in m): 24 points round it, each the land cell nearest the circle on
        /// its radius (within 25%), joined in turn by the cheapest route (bridges over the channels). Arcs that would be a
        /// long detour are left out. Then joined to the town by the usual ConnectAll.
        /// </summary>
        private static void RingRoad(Ctx c, float[] ring)
        {
            int n = c.N, count = 24;
            int[] verts = new int[count];
            // the atoll's ring isn't a circle (TerrainGen.AtollRadius, warped): along each direction take the runs of land
            // between 0.5 and 1.6 x the radius, and in the run nearest the previous direction's (the ring, not an islet
            // or the volcano's far side) the cell furthest from the shore
            float prevF = 1;
            for (int k = 0; k < count; k++)
            {
                verts[k] = -1;
                double th = 2 * Math.PI * k / count;
                float bestRun = float.MaxValue, runF0 = -1, runBestShore = -1; int runBest = -1;
                for (float f = 0.5f; f <= 1.62f; f += 0.01f)
                {
                    float x = ring[0] + (float)Math.Cos(th) * ring[2] * f, y = ring[1] + (float)Math.Sin(th) * ring[2] * f;
                    bool land = Inside(c, x, y, 6) && f < 1.6f;
                    int i = land ? Idx(c, x, y) : -1;
                    land = land && c.Land[i] && c.Shore[i] >= 10;
                    if (land)
                    {
                        if (runF0 < 0) { runF0 = f; runBestShore = -1; runBest = -1; }
                        if (c.Shore[i] > runBestShore) { runBestShore = c.Shore[i]; runBest = i; }
                    }
                    else if (runF0 >= 0)
                    {
                        float mid = (runF0 + f) / 2, d = Math.Abs(mid - prevF);
                        if (d < bestRun && runBest >= 0) { bestRun = d; verts[k] = runBest; }
                        runF0 = -1;
                    }
                }
                if (verts[k] >= 0)
                {
                    float vx = (verts[k] % n) * Cell - ring[0], vy = (verts[k] / n) * Cell - ring[1];
                    prevF = (float)Math.Sqrt(vx * vx + vy * vy) / ring[2];
                }
            }
            int arcs = 0;
            for (int k = 0; k < count; k++)
            {
                int a = verts[k], b = verts[(k + 1) % count];
                if (a < 0 || b < 0 || a == b) continue;
                List<int> path = AStar(c, a, new[] { b }, RingOwn, 0);
                if (path == null) continue;
                float len = 0; for (int q = 1; q < path.Count; q++) len += Dist(path[q], path[q - 1], n) * Cell;
                if (len > 2.5f * Dist(a, b, n) * Cell + 60) continue;
                if (AddPath(c, path, RoadKind.Main, RingOwn, -1)) arcs++;
            }
            c.Net.RingArcs = arcs;
        }

        /// <summary>
        /// A through road: from one map edge to the opposite one, passing 250-400 m to the side of the town centre (the
        /// town's roads join it; it doesn't run down the main street), smoothed into long, wide curves (no bend under ~250 m
        /// radius where the land allows). EA's Lucky Palms has one across its desert with a road from it into town.
        /// </summary>
        private static void Motorway(Ctx c, float worldSize)
        {
            int n = c.N;
            float cx = (c.Centre % n) * Cell, cy = (c.Centre / n) * Cell;
            double a0 = c.Rng.NextDouble() * Math.PI;
            // a few directions, until one crosses the whole map (a wide river can stop it on one side)
            for (int attempt = 0; attempt < 8; attempt++)
            {
                double a = a0 + attempt * Math.PI / 8;
                float dx = (float)Math.Cos(a), dy = (float)Math.Sin(a), side = attempt % 2 == 0 ? 1 : -1;
                float off = (250 + 150 * (float)c.Rng.NextDouble()) * side;
                float wx = cx - dy * off, wy = cy + dx * off;
                int way = NearestLand(c, wx, wy, 120);
                int entry = EdgeLand(c, wx - dx * worldSize, wy - dy * worldSize), exit = EdgeLand(c, wx + dx * worldSize, wy + dy * worldSize);
                if (way < 0 || entry < 0 || exit < 0 || entry == exit) continue;
                List<int> p1 = AStar(c, entry, new[] { way }, MotorOwn, 0);
                if (p1 == null) continue;
                List<int> p2 = AStar(c, way, new[] { exit }, MotorOwn, 0);
                if (p2 == null) continue;
                int before = c.Net.Roads.Count;
                AddPath(c, p1, RoadKind.Main, MotorOwn, -1);
                AddPath(c, p2, RoadKind.Main, MotorOwn, -1);
                for (int k = before; k < c.Net.Roads.Count; k++)
                {
                    Road r = c.Net.Roads[k];
                    if (r.Spans.Count > 0 || r.Points.Count < 3) continue;
                    List<float[]> p = Simplify(r.Points, 30f, 200f);
                    p = Relax(c, Resample(p, Cell), 60, 40f);
                    p = TerrainGen.Chaikin(TerrainGen.Chaikin(TerrainGen.Chaikin(p)));
                    r.Points = Ease(c, Resample(p, 8f), 250f, 120, 40f);
                }
                for (int k = before; k < c.Net.Roads.Count; k++)
                    for (int q = 1; q < c.Net.Roads[k].Points.Count; q++) c.Net.MotorwayKm += (float)Math.Sqrt(D2(c.Net.Roads[k].Points[q - 1], c.Net.Roads[k].Points[q])) / 1000f;
                return;
            }
        }

        private static int NearestLand(Ctx c, float x, float y, float reach)
        {
            int best = -1; float bd = float.MaxValue;
            for (float dy = -reach; dy <= reach; dy += Cell)
                for (float dx = -reach; dx <= reach; dx += Cell)
                {
                    if (!Inside(c, x + dx, y + dy, 4)) continue;
                    int i = Idx(c, x + dx, y + dy);
                    if (!c.Land[i]) continue;
                    float d = dx * dx + dy * dy;
                    if (d < bd) { bd = d; best = i; }
                }
            return best;
        }

        /// <summary>The land cell on the map edge nearest where the line towards (x, y) leaves the map.</summary>
        private static int EdgeLand(Ctx c, float x, float y)
        {
            int n = c.N; float max = (n - 1) * Cell;
            float ex = Math.Max(Cell, Math.Min(max - Cell, x)), ey = Math.Max(Cell, Math.Min(max - Cell, y));
            int best = -1; float bd = float.MaxValue;
            for (int k = 1; k < n - 1; k++)
                foreach (int i in new[] { n + k, (n - 2) * n + k, k * n + 1, k * n + n - 2 })
                {
                    if (!c.Land[i]) continue;
                    float d = Sq((i % n) * Cell - ex) + Sq((i / n) * Cell - ey);
                    if (d < bd) { bd = d; best = i; }
                }
            return best;
        }

        /// <summary>
        /// A road along the coast through the town: round the town centre (within 'reach' m), in each 8 degree direction
        /// the land cell 30-60 m from the water nearest the centre; neighbouring points (within 260 m) are joined by the
        /// cheapest route (coastal regions already pull routes to the shore; bridges allowed). Gaps where the coast turns
        /// away leave separate arcs, joined to the network later (ConnectAll).
        /// </summary>
        private static void ShoreRoad(Ctx c, float reach)
        {
            int n = c.N, bins = 45;
            float cx = (c.Centre % n) * Cell, cy = (c.Centre / n) * Cell;
            float[] sea = SeaDistance(c);
            int[] best = new int[bins]; float[] bd = new float[bins];
            for (int k = 0; k < bins; k++) { best[k] = -1; bd[k] = float.MaxValue; }
            int r = (int)(reach / Cell);
            int x0 = Math.Max(10, c.Centre % n - r), x1 = Math.Min(n - 11, c.Centre % n + r), y0 = Math.Max(10, c.Centre / n - r), y1 = Math.Min(n - 11, c.Centre / n + r);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * n + x;
                    if (!c.Land[i] || c.Shore[i] < ShoreKeep + 6 || sea[i] > ShoreKeep + 36 || c.Slope[i] > c.MaxGrade * 1.5f) continue;
                    float dx = x * Cell - cx, dy = y * Cell - cy, d = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (d > reach || d < 30) continue;
                    double a = Math.Atan2(dy, dx); if (a < 0) a += 2 * Math.PI;
                    int k = Math.Min(bins - 1, (int)(a / (2 * Math.PI) * bins));
                    if (d < bd[k]) { bd[k] = d; best[k] = i; }
                }
            int arcs = 0;
            for (int k = 0; k < bins; k++)
            {
                int a = best[k], b = best[(k + 1) % bins];
                if (a < 0 || b < 0 || a == b || Dist(a, b, n) * Cell > 260) continue;
                List<int> path = AStar(c, a, new[] { b }, ShoreOwn, 0);
                if (path == null) continue;
                float len = 0; for (int q = 1; q < path.Count; q++) len += Dist(path[q], path[q - 1], n) * Cell;
                if (len > 2.2f * Dist(a, b, n) * Cell + 40) continue; // round a headland or bay: leave a gap
                if (AddPath(c, path, RoadKind.Main, ShoreOwn, -1)) arcs++;
            }
            c.Net.ShoreArcs = arcs;
            if (Environment.GetEnvironmentVariable("CAWDBG") != null) { int pts = 0; foreach (int b in best) if (b >= 0) pts++; Console.Error.WriteLine("DBG shore road: " + pts + " points, " + arcs + " arcs, centre " + cx + "," + cy); for (int k = 0; k < bins; k += 5) if (best[k] >= 0) Console.Error.WriteLine("DBG   pt " + (best[k] % n) * Cell + "," + (best[k] / n) * Cell + " d " + bd[k] + " sea " + sea[best[k]]); }
        }

        /// <summary>Metres to the sea (ground below the sea level joined to the map edge; ponds, inland lakes and the
        /// barely-dry ground the planner counts as wet don't count), chamfer distance.</summary>
        private static float[] SeaDistance(Ctx c)
        {
            int n = c.N, N = n * n;
            float[] d = new float[N];
            for (int i = 0; i < N; i++) d[i] = float.MaxValue;
            Queue<int> q = new Queue<int>();
            float sl = c.S.SeaLevel;
            for (int i = 0; i < N; i++)
            {
                int x = i % n, y = i / n;
                if (c.H.D[i] < sl && (x == 0 || y == 0 || x == n - 1 || y == n - 1)) { d[i] = 0; q.Enqueue(i); }
            }
            while (q.Count > 0) // the sea itself: water reachable from the edge
            {
                int i = q.Dequeue(), x = i % n, y = i / n;
                foreach (int j in new[] { x > 0 ? i - 1 : -1, x < n - 1 ? i + 1 : -1, y > 0 ? i - n : -1, y < n - 1 ? i + n : -1 })
                    if (j >= 0 && c.H.D[j] < sl && d[j] > 0) { d[j] = 0; q.Enqueue(j); }
            }
            float d1 = Cell, d2 = Cell * 1.4142f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x; float v = d[i];
                    if (x > 0) v = Math.Min(v, d[i - 1] + d1);
                    if (y > 0) { v = Math.Min(v, d[i - n] + d1); if (x > 0) v = Math.Min(v, d[i - n - 1] + d2); if (x < n - 1) v = Math.Min(v, d[i - n + 1] + d2); }
                    d[i] = v;
                }
            for (int y = n - 1; y >= 0; y--)
                for (int x = n - 1; x >= 0; x--)
                {
                    int i = y * n + x; float v = d[i];
                    if (x < n - 1) v = Math.Min(v, d[i + 1] + d1);
                    if (y < n - 1) { v = Math.Min(v, d[i + n] + d1); if (x < n - 1) v = Math.Min(v, d[i + n + 1] + d2); if (x > 0) v = Math.Min(v, d[i + n - 1] + d2); }
                    d[i] = v;
                }
            return d;
        }

        // ---------- downtown ----------

        /// <summary>Main street through the centre along its flattest direction, cross streets every 76 m, back streets 90 m
        /// either side. Every crossing is a node where separate road pieces end. Returns the downtown's radius in metres.</summary>
        private static float Downtown(Ctx c)
        {
            int blocks = Math.Max(2, Math.Min(8, c.S.DowntownBlocks));
            float bx = 76, by = 90, half = blocks * bx / 2;
            float cx = (c.Centre % c.N) * Cell, cy = (c.Centre / c.N) * Cell;
            // flattest direction for the main street
            float angle = 0, bestCost = float.MaxValue;
            for (int k = 0; k < 12; k++)
            {
                float a = (float)(k * Math.PI / 12), cost = 0;
                for (float t = -half - bx; t <= half + bx; t += Cell)
                {
                    float x = cx + t * (float)Math.Cos(a), y = cy + t * (float)Math.Sin(a);
                    float x2 = x + Cell * (float)Math.Cos(a), y2 = y + Cell * (float)Math.Sin(a);
                    if (!Inside(c, x, y) || !Inside(c, x2, y2)) { cost += 1; continue; }
                    cost += Math.Abs(HeightAt(c, x2, y2) - HeightAt(c, x, y)) / Cell + (c.Land[Idx(c, x, y)] ? 0 : 1);
                }
                cost += (float)c.Rng.NextDouble() * 0.05f;
                if (cost < bestCost) { bestCost = cost; angle = a; }
            }
            int jMin = blocks >= 3 ? -1 : 0;
            if (c.S.Grid != null)
            {
                // the Like world's grid: its number of straight streets each way and their spacing (tools/downtown.py);
                // the main street (j = 0) runs along the flattest way, through the middle and on out of town
                DowntownGrid g = c.S.Grid;
                int across = g.LayA - 1, along = g.LayB - 1;
                float sb = g.LaySpacingB, sa = g.LaySpacingA;
                // the grid where it fits best: every 15 deg, shifted up to half a block each way, the most grid nodes on
                // buildable land, then the flattest main street (Barnacle Bay's and Champs Les Sims' town centres sit
                // against a hill or a river, so the grid on the centre itself kept only a crossroads)
                float bestA = angle, bestX = cx, bestY = cy; int bestNodes = -1; float bestTie = float.MaxValue;
                for (int k = 0; k < 12; k++)
                {
                    float a = (float)(k * Math.PI / 12);
                    for (int sx = -1; sx <= 1; sx++)
                        for (int sy = -1; sy <= 1; sy++)
                        {
                            float ox = cx + (sx * sb * (float)Math.Cos(a) - sy * sa * (float)Math.Sin(a)) / 2, oy = cy + (sx * sb * (float)Math.Sin(a) + sy * sa * (float)Math.Cos(a)) / 2;
                            int nodes = GridNodes(c, ox, oy, a, along, across, sb, sa, -(across / 2), 0.5f);
                            float tie = (float)Math.Abs(Math.IEEERemainder(a - angle, Math.PI)) + Math.Abs(sx) * 0.3f + Math.Abs(sy) * 0.3f;
                            if (nodes > bestNodes || nodes == bestNodes && tie < bestTie) { bestNodes = nodes; bestTie = tie; bestA = a; bestX = ox; bestY = oy; }
                        }
                }
                return GridRoads(c, bestX, bestY, bestA, along, across, sb, sa, -(across / 2), Town, -1, true, c.MaxGrade, 0.5f, false);
            }
            return GridRoads(c, cx, cy, angle, blocks, 1 - jMin, bx, by, jMin, Town, -1, true, c.MaxGrade, 0.55f);
        }

        /// <summary>
        /// A block grid: nodes i = 0..along, j = jMin..jMin+across, centred on (cx, cy) along 'angle'. Edges between valid
        /// neighbouring nodes become separate roads. With 'mainStreet', the j = 0 line is a main road extended one block
        /// further each way. Returns the grid's radius in metres.
        /// </summary>
        private static float GridRoads(Ctx c, float cx, float cy, float angle, int along, int across, float bx, float by, int jMin,
                                       int own, int area, bool mainStreet, float maxGrade, float minBuild = 0.6f, bool organicRim = true)
        {
            float ca = (float)Math.Cos(angle), sa = (float)Math.Sin(angle);
            int iMin = mainStreet ? -1 : 0, iMax = mainStreet ? along + 1 : along;
            int ni = iMax - iMin + 1, nj = across + 1;
            float[][] pos = new float[ni * nj][];
            bool[] valid = new bool[ni * nj];
            float ox = -along * bx / 2, oy = -(jMin + across / 2f) * by;
            for (int i = 0; i < ni; i++)
                for (int j = 0; j < nj; j++)
                {
                    float u = ox + (i + iMin) * bx, v = oy + (j + jMin) * by;
                    float x = cx + u * ca - v * sa, y = cy + u * sa + v * ca;
                    pos[i * nj + j] = new[] { x, y };
                    bool extension = (i + iMin < 0 || i + iMin > along) && j + jMin != 0;
                    valid[i * nj + j] = !extension && Inside(c, x, y, 5) && c.Land[Idx(c, x, y)] && c.Build[Idx(c, x, y)] >= minBuild
                                        && c.Ridge[Idx(c, x, y)] < 5 && FreeOfOthers(c, x, y, own);
                }
            float radius = 0;
            for (int i = 0; i < ni; i++)
                for (int j = 0; j < nj; j++)
                {
                    int a = i * nj + j;
                    if (!valid[a]) continue;
                    if (i + 1 < ni && valid[a + nj]) radius = Math.Max(radius, Edge(c, pos[a], pos[a + nj], j + jMin == 0 && mainStreet ? RoadKind.Main : RoadKind.Street, own, area, maxGrade, cx, cy, !organicRim));
                    if (j + 1 < nj && valid[a + 1] && i + iMin >= 0 && i + iMin <= along) radius = Math.Max(radius, Edge(c, pos[a], pos[a + 1], RoadKind.Street, own, area, maxGrade, cx, cy, !organicRim));
                }
            if (mainStreet && organicRim)
            {
                // blocky in the middle, organic at the edges: some outer streets carry on as curving roads that follow the land
                for (int i = 0; i < ni; i++)
                    for (int j = 0; j < nj; j++)
                    {
                        int a = i * nj + j;
                        bool rim = i + iMin == 0 || i + iMin == along || j == 0 || j == nj - 1;
                        if (!valid[a] || !rim || j + jMin == 0 || c.Rng.NextDouble() > 0.5) continue;
                        if (Idx(c, pos[a][0], pos[a][1]) < 0 || c.OnRoad[Idx(c, pos[a][0], pos[a][1])] == 0) continue; // node not used
                        float dx = pos[a][0] - cx, dy = pos[a][1] - cy, dl = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (dl < 1) continue;
                        List<float[]> w = Walk(c, pos[a][0], pos[a][1], dx / dl, dy / dl, 70 + (float)c.Rng.NextDouble() * 110, own);
                        if (w.Count * Cell < 40) continue;
                        w.Insert(0, pos[a]);
                        AddRoad(c, w, RoadKind.Street, own, area);
                        radius = Math.Max(radius, dl + w.Count * Cell);
                    }
            }
            return radius;
        }

        /// <summary>How many of a grid's nodes (as GridRoads lays them, main street extensions left out) fall on dry,
        /// buildable land off ridges.</summary>
        private static int GridNodes(Ctx c, float cx, float cy, float angle, int along, int across, float bx, float by, int jMin, float minBuild)
        {
            float ca = (float)Math.Cos(angle), sa = (float)Math.Sin(angle);
            float ox = -along * bx / 2, oy = -(jMin + across / 2f) * by;
            int count = 0;
            for (int i = 0; i <= along; i++)
                for (int j = 0; j <= across; j++)
                {
                    float u = ox + i * bx, v = oy + (j + jMin) * by;
                    float x = cx + u * ca - v * sa, y = cy + u * sa + v * ca;
                    if (!Inside(c, x, y, 5)) continue;
                    int q = Idx(c, x, y);
                    if (c.Land[q] && c.Build[q] >= minBuild && c.Ridge[q] < 5) count++;
                }
            return count;
        }

        private static float Edge(Ctx c, float[] a, float[] b, RoadKind kind, int own, int area, float maxGrade, float cx, float cy, bool straight = false)
        {
            float len = (float)Math.Sqrt(D2(a, b));
            int steps = Math.Max(1, (int)(len / Cell));
            List<float[]> pts = new List<float[]>();
            for (int k = 0; k <= steps; k++)
            {
                float t = (float)k / steps;
                float x = a[0] + (b[0] - a[0]) * t, y = a[1] + (b[1] - a[1]) * t;
                if (!c.Land[Idx(c, x, y)]) return 0;
                if (pts.Count > 0)
                {
                    float[] p = pts[pts.Count - 1];
                    if (Math.Abs(HeightAt(c, x, y) - HeightAt(c, p[0], p[1])) / (len / steps) > maxGrade) return 0;
                }
                pts.Add(new[] { x, y });
            }
            AddRoad(c, pts, kind, own, area);
            if (straight) c.Net.Roads[c.Net.Roads.Count - 1].Straight = true;
            return (float)Math.Sqrt(Math.Max(D2(a, new[] { cx, cy }), D2(b, new[] { cx, cy })));
        }

        // ---------- edge roads ----------

        /// <summary>Up to 'count' edge cells on different sides of the map: flat land, not too far from the centre.</summary>
        private static List<int> EdgeCells(Ctx c, int count)
        {
            int n = c.N;
            int[] bestCell = { -1, -1, -1, -1 }; float[] bestV = { float.MinValue, float.MinValue, float.MinValue, float.MinValue };
            for (int k = 2; k < n - 2; k++)
                for (int side = 0; side < 4; side++)
                {
                    int x, y;
                    switch (side) { case 0: x = k; y = 1; break; case 1: x = k; y = n - 2; break; case 2: x = 1; y = k; break; default: x = n - 2; y = k; break; }
                    int i = y * n + x;
                    if (!c.Land[i]) continue;
                    float v = c.Flat[i] - Dist(i, c.Centre, n) / n * 0.8f + (float)c.Rng.NextDouble() * 0.4f;
                    // an exit far above or below the town needs switchbacks to reach (#162: Appaloosa's edge road zig-zagged
                    // 60 m down to a high corner in tight hairpins): marked down when the straight climb is over ~60% of
                    // the grade limit
                    float climb = Math.Abs(c.H.D[i] - c.H.D[c.Centre]) / Math.Max(Cell, Dist(i, c.Centre, n) * Cell);
                    v -= Math.Max(0, climb / Math.Max(0.01f, c.MaxGrade) - 0.6f) * 3f;
                    if (v > bestV[side]) { bestV[side] = v; bestCell[side] = i; }
                }
            List<int> order = new List<int>();
            for (int side = 0; side < 4; side++) if (bestCell[side] >= 0) order.Add(side);
            order.Sort(delegate (int a, int b) { return bestV[b].CompareTo(bestV[a]); });
            List<int> res = new List<int>();
            for (int k = 0; k < order.Count && res.Count < count; k++) res.Add(bestCell[order[k]]);
            return res;
        }

        // ---------- neighbourhoods ----------

        private static List<int> Hubs(Ctx c, int count, float minFromCentre, float minBetween, float R)
        {
            int n = c.N;
            List<int> hubs = new List<int>();
            int margin = (int)(R * 0.6f / Cell);
            // not further out than a third of the map, or a big downtown leaves no room on small or hilly maps
            minFromCentre = Math.Min(minFromCentre, (n - 1) * Cell * 0.33f);
            // with the Like world's town shape: neighbourhoods inside its lot spread (EA's towns are compact: 90% of the lots
            // within ~400-700 m of the core), further out only if nothing nearer fits
            float spread = c.Shape != null ? c.Shape.LotDec[8] : float.MaxValue;
            if (c.Shape != null) minFromCentre = Math.Min(minFromCentre, spread * 0.8f);
            // strict first; hilly worlds then take the gentlest ground there is (villages on terraces)
            float[] needBuild = { 0.5f, 0.3f }, needFlat = { 0.4f, 0.25f };
            for (int pass = 0; pass < 2 && hubs.Count < count; pass++)
                for (int k = hubs.Count; k < count; k++)
                {
                    int best = -1; float bv = float.MinValue;
                    for (int y = margin; y < n - margin; y++)
                        for (int x = margin; x < n - margin; x++)
                        {
                            int i = y * n + x;
                            if (!c.Land[i] || c.Flat[i] < needFlat[pass] || c.OnRoad[i] != 0 || c.BuildWide[i] < needBuild[pass]) continue;
                            float dc = Dist(i, c.Centre, n) * Cell;
                            if (dc < minFromCentre) continue;
                            float dmin = float.MaxValue;
                            foreach (int q in hubs) dmin = Math.Min(dmin, Dist(i, q, n) * Cell);
                            if (dmin < minBetween) continue;
                            // flat, not far out of town, spread from the others
                            float v = c.BuildWide[i] * 1.5f + c.Flat[i] * 0.5f - dc / ((n - 1) * Cell) * 0.9f
                                      + Math.Min(dmin, minBetween * 2) / (minBetween * 2) * 0.4f - Math.Max(0, c.Ridge[i]) / 20f
                                      + (float)c.Rng.NextDouble() * 0.25f;
                            if (dc > spread) v -= 1.2f * (dc - spread) / R;
                            if (v > bv) { bv = v; best = i; }
                        }
                    if (best < 0) break;
                    hubs.Add(best);
                }
            hubs.Sort(delegate (int a, int b) { return Dist(a, c.Centre, n).CompareTo(Dist(b, c.Centre, n)); });
            return hubs;
        }

        /// <summary>One style per hub (hubs sorted nearest first): low variety = mostly loops and cul-de-sacs; farm lanes go furthest out.</summary>
        private static List<HoodStyle> Styles(Ctx c, int count)
        {
            List<HoodStyle> pool = new List<HoodStyle>();
            float v = c.S.Variety / 100f;
            HoodStyle[] mix = v < 0.3f ? new[] { HoodStyle.Loop, HoodStyle.CulDeSac }
                            : new[] { HoodStyle.Loop, HoodStyle.CulDeSac, HoodStyle.Grid, HoodStyle.Loop, HoodStyle.Rural, HoodStyle.CulDeSac, HoodStyle.Grid, HoodStyle.Rural };
            int take = v < 0.3f ? 2 : v < 0.7f ? 6 : 8;
            for (int k = 0; k < count; k++) pool.Add(mix[k % Math.Min(take, mix.Length)]);
            for (int i = pool.Count - 1; i > 0; i--) { int j = c.Rng.Next(i + 1); HoodStyle t = pool[i]; pool[i] = pool[j]; pool[j] = t; }
            // farm lanes on the outskirts
            pool.Sort(delegate (HoodStyle a, HoodStyle b) { return (a == HoodStyle.Rural ? 1 : 0).CompareTo(b == HoodStyle.Rural ? 1 : 0); });
            return pool;
        }

        /// <summary>Riverview-style loop: a ring road round the hub following the ground, sometimes a street across it,
        /// joined to the town once or twice.</summary>
        private static bool LoopHood(Ctx c, int hub, float R, int own)
        {
            int n = c.N, area = own - 1, before = c.Net.Roads.Count;
            float hx = (hub % n) * Cell, hy = (hub / n) * Cell;
            int count = 9 + c.Rng.Next(3);
            double phase = c.Rng.NextDouble() * Math.PI * 2;
            List<int> verts = new List<int>();
            float[] scales = { 1f, 0.85f, 1.15f, 0.7f, 1.3f };
            for (int k = 0; k < count; k++)
            {
                double th = phase + 2 * Math.PI * k / count + (c.Rng.NextDouble() - 0.5) * 0.3;
                float r = R * (0.8f + 0.35f * (float)c.Rng.NextDouble());
                foreach (float sc in scales)
                {
                    float x = hx + (float)Math.Cos(th) * r * sc, y = hy + (float)Math.Sin(th) * r * sc;
                    if (!Inside(c, x, y, 6)) continue;
                    int i = Idx(c, x, y);
                    if (!c.Land[i] || c.Build[i] < 0.5f || c.Ridge[i] > 6 || !FreeOfOthers(c, x, y, own)) continue;
                    verts.Add(i); break;
                }
            }
            if (verts.Count < 5) return CulDeSacHood(c, hub, R, own);
            int built = 0;
            for (int k = 0; k < verts.Count; k++)
            {
                int a = verts[k], b = verts[(k + 1) % verts.Count];
                if (AddPath(c, AStar(c, a, new[] { b }, own, 0), RoadKind.Street, own, area)) built++;
            }
            if (built < verts.Count / 2) { RemoveFrom(c, before, own); return false; }
            // a street across the middle, half the time
            if (c.Rng.NextDouble() < 0.5)
            {
                int a = verts[0], b = verts[verts.Count / 2];
                List<int> across = AStar(c, a, new[] { b }, own, 0);
                if (across != null && across.Count * Cell < R * 2.6f) AddPath(c, across, RoadKind.Street, own, area);
            }
            if (!Connect(c, verts, own, area, true, R)) { RemoveFrom(c, before, own); return false; }
            return true;
        }

        /// <summary>A small suburban grid (3 x 2 blocks of 110 x 75 m) facing the town.</summary>
        private static bool GridHood(Ctx c, int hub, int own)
        {
            int n = c.N;
            float hx = (hub % n) * Cell, hy = (hub / n) * Cell;
            float cx = (c.Centre % n) * Cell, cy = (c.Centre / n) * Cell;
            float angle = (float)Math.Atan2(cy - hy, cx - hx) + (c.Rng.Next(2) == 0 ? 0 : (float)Math.PI / 2);
            int before = c.Net.Roads.Count;
            GridRoads(c, hx, hy, angle, 3, 2, 110, 75, 0, own, own - 1, false, c.MaxGrade, 0.7f);
            if (c.Net.Roads.Count - before < 4) { RemoveFrom(c, before, own); return false; }
            List<int> cells = new List<int>();
            for (int k = before; k < c.Net.Roads.Count; k++) foreach (float[] p in c.Net.Roads[k].Points) cells.Add(Idx(c, p[0], p[1]));
            if (!Connect(c, cells, own, own - 1, false, 200)) { RemoveFrom(c, before, own); return false; }
            return true;
        }

        /// <summary>A curving street along the contour with short dead-end lanes off it.</summary>
        private static bool CulDeSacHood(Ctx c, int hub, float R, int own)
        {
            int n = c.N, area = own - 1, before = c.Net.Roads.Count;
            float hx = (hub % n) * Cell, hy = (hub / n) * Cell;
            float[] d = ContourDir(c, hx, hy);
            List<float[]> a = Walk(c, hx, hy, d[0], d[1], R * 0.9f, own), b = Walk(c, hx, hy, -d[0], -d[1], R * 0.9f, own);
            List<float[]> spine = new List<float[]>();
            for (int k = a.Count - 1; k >= 0; k--) spine.Add(a[k]);
            spine.Add(new[] { hx, hy });
            spine.AddRange(b);
            if (spine.Count * Cell < 80) return false;
            AddRoad(c, spine, RoadKind.Street, own, area);
            int lanes = 3 + c.Rng.Next(2);
            for (int k = 0; k < lanes; k++)
            {
                int at = 2 + (int)((spine.Count - 4) * (k + 0.5f) / lanes);
                if (at < 1 || at >= spine.Count - 1) continue;
                float[] p = spine[at], q = spine[at - 1], r = spine[at + 1];
                float tx = r[0] - q[0], ty = r[1] - q[1], tl = (float)Math.Sqrt(tx * tx + ty * ty);
                if (tl < 1e-3f) continue;
                float side = k % 2 == 0 ? 1 : -1;
                List<float[]> lane = Walk(c, p[0], p[1], -ty / tl * side, tx / tl * side, 60 + (float)c.Rng.NextDouble() * 70, own);
                if (lane.Count * Cell < 40) continue;
                lane.Insert(0, p);
                AddRoad(c, lane, RoadKind.Lane, own, area);
            }
            List<int> ends = new List<int>();
            ends.Add(Idx(c, spine[0][0], spine[0][1])); ends.Add(Idx(c, spine[spine.Count - 1][0], spine[spine.Count - 1][1]));
            if (!Connect(c, ends, own, area, false, R)) { RemoveFrom(c, before, own); return false; }
            return true;
        }

        /// <summary>A farm road: joined to the town, then winding on away from it, with a couple of driveways.</summary>
        private static bool RuralHood(Ctx c, int hub, float R, int own)
        {
            int n = c.N, area = own - 1;
            float hx = (hub % n) * Cell, hy = (hub / n) * Cell;
            List<int> link = AStar(c, hub, null, own, 0);
            if (!Reasonable(c, link, R) || !AddPath(c, link, RoadKind.Street, own, area)) return false;
            float cx = (c.Centre % n) * Cell, cy = (c.Centre / n) * Cell;
            float dx = hx - cx, dy = hy - cy, dl = (float)Math.Sqrt(dx * dx + dy * dy);
            if (dl < 1) { dx = 1; dy = 0; dl = 1; }
            List<float[]> lane = Walk(c, hx, hy, dx / dl, dy / dl, R * (1.5f + (float)c.Rng.NextDouble()), own, 0.5f);
            if (lane.Count * Cell < 60) return true;
            lane.Insert(0, new[] { hx, hy });
            AddRoad(c, lane, RoadKind.Lane, own, area);
            for (int k = 0; k < 2; k++)
            {
                int at = lane.Count * (k + 1) / 3;
                if (at < 1 || at >= lane.Count - 1) continue;
                float[] p = lane[at], q = lane[at - 1], r = lane[at + 1];
                float tx = r[0] - q[0], ty = r[1] - q[1], tl = (float)Math.Sqrt(tx * tx + ty * ty);
                if (tl < 1e-3f) continue;
                float side = k == 0 ? 1 : -1;
                List<float[]> drive = Walk(c, p[0], p[1], -ty / tl * side, tx / tl * side, 40 + (float)c.Rng.NextDouble() * 40, own);
                if (drive.Count * Cell < 32) continue;
                drive.Insert(0, p);
                AddRoad(c, drive, RoadKind.Lane, own, area);
            }
            return true;
        }

        /// <summary>Joins a neighbourhood to the rest from its cell nearest the centre; with 'second', also from the far side.</summary>
        private static bool Connect(Ctx c, List<int> cells, int own, int area, bool second, float R)
        {
            int n = c.N;
            int first = -1; float bd = float.MaxValue;
            foreach (int i in cells) { if (i < 0) continue; float d = Dist(i, c.Centre, n); if (d < bd) { bd = d; first = i; } }
            if (first < 0) return false;
            List<int> link = AStar(c, first, null, own, 0);
            if (!Reasonable(c, link, R)) return false;
            AddPath(c, link, RoadKind.Street, own, area);
            if (!second || c.Rng.NextDouble() > 0.6) return true;
            int far = -1; float fd = 0;
            foreach (int i in cells) { if (i < 0) continue; float d = Dist(i, first, n); if (d > fd) { fd = d; far = i; } }
            if (far < 0) return true;
            List<int> path = AStar(c, far, null, own, 0);
            if (path != null && path.Count * Cell < R * 2.5f) AddPath(c, path, RoadKind.Street, own, area);
            return true;
        }

        /// <summary>A connection that doesn't fit the land: none at all, or a long detour (over 3 radii and more than
        /// twice the straight distance). The neighbourhood is then left out rather than squeezed in.</summary>
        private static bool Reasonable(Ctx c, List<int> path, float R)
        {
            if (path == null || path.Count < 2) return false;
            float len = 0;
            for (int k = 1; k < path.Count; k++) len += Dist(path[k], path[k - 1], c.N) * Cell;
            float straight = Dist(path[0], path[path.Count - 1], c.N) * Cell;
            return len <= R * 3 || (len <= 2 * Math.Max(straight, 1) && len <= R * 6);
        }

        // ---------- community lots ----------

        private static readonly string[] SiteLabels = { "Park", "School", "Shops", "Beach", "Sports field", "Library", "Graveyard", "Pool" };

        private static void Sites(Ctx c, int count)
        {
            int n = c.N;
            float worldSize = (n - 1) * Cell;
            for (int k = 0, label = 0; k < count && label < SiteLabels.Length * 2; label++)
            {
                string name = SiteLabels[label % SiteLabels.Length];
                bool water = name == "Beach";
                if (label >= SiteLabels.Length && water) continue;
                float near = name == "Park" || name == "Shops" || name == "Library" ? 0.12f : name == "School" || name == "Pool" ? 0.25f : 0.4f;
                int best = -1; float bv = float.MinValue;
                for (int t = 0; t < 1500; t++)
                {
                    int x = 6 + c.Rng.Next(n - 12), y = 6 + c.Rng.Next(n - 12), i = y * n + x;
                    if (!c.Land[i] || c.OnRoad[i] != 0 || c.Flat[i] < 0.55f) continue;
                    bool tooClose = false;
                    foreach (LotSite s in c.Net.Sites) if (Sq(s.P[0] - x * Cell) + Sq(s.P[1] - y * Cell) < 70 * 70) { tooClose = true; break; }
                    if (tooClose) continue;
                    if (water && !NearWater(c, x, y, 5)) continue;
                    float roadDist = RoadDistance(c, x, y, 6);
                    if (roadDist < 2) continue;           // keep the lot off the road itself
                    float dc = Dist(i, c.Centre, n) * Cell / worldSize;
                    float v = c.Flat[i] - Math.Abs(dc - near) * 2 - (roadDist > 5 ? 0.6f : 0) + (float)c.Rng.NextDouble() * 0.2f;
                    if (v > bv) { bv = v; best = i; }
                }
                if (best < 0) continue;
                int bx = best % n, by = best / n;
                if (RoadDistance(c, bx, by, 6) > 5)
                {
                    List<int> spur = AStar(c, best, null, 100 + k, 0);
                    if (spur == null || spur.Count * Cell > 300) continue;
                    AddPath(c, spur, RoadKind.Lane, 100 + k, -1);
                }
                LotSite site = new LotSite(); site.Label = name; site.P = new[] { bx * Cell, by * Cell };
                c.Net.Sites.Add(site);
                k++;
            }
        }

        private static bool NearWater(Ctx c, int x, int y, int r)
        {
            for (int yy = y - r; yy <= y + r; yy++)
                for (int xx = x - r; xx <= x + r; xx++)
                    if (xx >= 0 && yy >= 0 && xx < c.N && yy < c.N && !c.Land[yy * c.N + xx]) return true;
            return false;
        }

        /// <summary>Distance in cells to the nearest road (Chebyshev), or max + 1.</summary>
        private static float RoadDistance(Ctx c, int x, int y, int max)
        {
            for (int r = 0; r <= max; r++)
                for (int yy = y - r; yy <= y + r; yy++)
                    for (int xx = x - r; xx <= x + r; xx++)
                    {
                        if (Math.Max(Math.Abs(xx - x), Math.Abs(yy - y)) != r) continue;
                        if (xx >= 0 && yy >= 0 && xx < c.N && yy < c.N && c.OnRoad[yy * c.N + xx] != 0) return r;
                    }
            return max + 1;
        }

        // ---------- terrain measures ----------

        /// <summary>0..1, 1 = dead flat over ~40 m around the cell.</summary>
        private static float[] Flatness(Grid h, bool[] land)
        {
            int n = h.W;
            Grid g = new Grid(n, n);
            for (int y = 1; y < n - 1; y++)
                for (int x = 1; x < n - 1; x++)
                {
                    float gx = (h[x + 1, y] - h[x - 1, y]) / (2 * Cell), gy = (h[x, y + 1] - h[x, y - 1]) / (2 * Cell);
                    g[x, y] = (float)Math.Sqrt(gx * gx + gy * gy);
                }
            Grid b = g.Blur(2.5f);
            float[] f = new float[n * n];
            for (int i = 0; i < f.Length; i++) f[i] = land[i] ? Math.Max(0, 1 - b.D[i] / 0.12f) : 0;
            return f;
        }

        /// <summary>Slope, buildable share at three scales, ridges and low ground for the planner.</summary>
        private static void Measure(Ctx c, float R)
        {
            int n = c.N;
            Grid h = c.H, slope = new Grid(n, n), build = new Grid(n, n);
            for (int y = 1; y < n - 1; y++)
                for (int x = 1; x < n - 1; x++)
                {
                    float gx = (h[x + 1, y] - h[x - 1, y]) / (2 * Cell), gy = (h[x, y + 1] - h[x, y - 1]) / (2 * Cell);
                    slope[x, y] = (float)Math.Sqrt(gx * gx + gy * gy);
                }
            slope = slope.Blur(1f);
            // buildable: under 10% slope, or up to the road grade limit on worlds set steeper (lots get flattened anyway)
            // buildable land: slope under the Like world's own (75th percentile of the land its roads cross), else 10 %
            float buildSlope = Math.Max(c.S.BuildSlope / 100f, c.MaxGrade);
            for (int i = 0; i < n * n; i++) build.D[i] = c.Land[i] && slope.D[i] < buildSlope ? 1 : 0;
            c.Slope = slope.D;
            c.Build = build.Blur(40f / Cell).D;
            c.BuildWide = build.Blur(R * 0.6f / Cell).D;
            c.BuildTown = build.Blur(120f / Cell).D;
            Grid broad = h.Blur(150f / Cell);
            float lo, hi; h.MinMax(out lo, out hi);
            float sea = c.S.SeaLevel, span = Math.Max(1, hi - sea);
            c.Ridge = new float[n * n]; c.Low = new float[n * n];
            for (int i = 0; i < n * n; i++) { c.Ridge[i] = h.D[i] - broad.D[i]; c.Low[i] = Math.Max(0, (h.D[i] - sea) / span); }
            c.Shore = ShoreDistance(c.Land, n);
        }

        /// <summary>
        /// Roads keep back from the water: EA's Riverview keeps 95% of its road length at least 24 m from water (Bridgewood
        /// 44 m). Within ShoreKeep metres a road costs up to 9x per step; smoothing never moves a road nearer the water.
        /// (the tester's v0.40 test: a road piece hung off the land into the sea.)
        /// </summary>
        public const float ShoreKeep = 24f;

        private static float[] ShoreDistance(bool[] land, int n)
        {
            float[] d = new float[n * n];
            const float Inf = 1e9f, A = Cell, B = Cell * 1.41421f;
            for (int i = 0; i < n * n; i++) d[i] = land[i] ? Inf : 0;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x; if (d[i] == 0) continue;
                    if (x > 0) d[i] = Math.Min(d[i], d[i - 1] + A);
                    if (y > 0) { d[i] = Math.Min(d[i], d[i - n] + A); if (x > 0) d[i] = Math.Min(d[i], d[i - n - 1] + B); if (x < n - 1) d[i] = Math.Min(d[i], d[i - n + 1] + B); }
                }
            for (int y = n - 1; y >= 0; y--)
                for (int x = n - 1; x >= 0; x--)
                {
                    int i = y * n + x; if (d[i] == 0) continue;
                    if (x < n - 1) d[i] = Math.Min(d[i], d[i + 1] + A);
                    if (y < n - 1) { d[i] = Math.Min(d[i], d[i + n] + A); if (x < n - 1) d[i] = Math.Min(d[i], d[i + n + 1] + B); if (x > 0) d[i] = Math.Min(d[i], d[i + n - 1] + B); }
                }
            return d;
        }

        /// <summary>A smoothing move from 'from' to 'to' that takes the road nearer the water than ShoreKeep: refused.</summary>
        private static bool CloserToShore(Ctx c, int from, int to)
        {
            return c.Shore != null && from >= 0 && c.Shore[to] < ShoreKeep && c.Shore[to] < c.Shore[from];
        }

        /// <summary>Unit direction along the contour (across the slope) at (x, y) metres.</summary>
        private static float[] ContourDir(Ctx c, float x, float y)
        {
            float gx = HeightAt(c, x + Cell, y) - HeightAt(c, x - Cell, y), gy = HeightAt(c, x, y + Cell) - HeightAt(c, x, y - Cell);
            float l = (float)Math.Sqrt(gx * gx + gy * gy);
            if (l < 1e-3f) { double a = c.Rng.NextDouble() * Math.PI; return new[] { (float)Math.Cos(a), (float)Math.Sin(a) }; }
            return new[] { -gy / l, gx / l };
        }

        // ---------- routing ----------

        private static readonly int[] DX = { 1, 1, 0, -1, -1, -1, 0, 1 }, DY = { 0, 1, 1, 1, 0, -1, -1, -1 }; // 45° steps, in order
        private static readonly float[] TurnCost = { 0, 0.5f, 4f, 14f, -1 };                       // per 45° steps of turn, x cell length

        /// <summary>
        /// Path of least resistance from 'start' to the goals (or, if null, to any road cell not of 'own' — or, with
        /// 'label', not of the start's piece — further than 'avoidNear' cells away). Per step the cost is length x
        /// (1 + 12·(grade/max)² + steep hillside + hill/ridge + height above sea), plus a cost for turning (states carry
        /// the heading), so routes keep to low, gentle ground round the foot of hills and curve smoothly. Steeper than
        /// 1.6x max or water is impassable; other roads are cheap so routes join them; the world edge strip is avoided.
        /// A diagonal step may not slip between two cells of another road, so routes meet roads instead of crossing them.
        /// </summary>
        private static List<int> AStar(Ctx c, int start, int[] goals, int own, float avoidNear, int[] label = null)
        {
            if (start < 0) return null;
            int n = c.N, states = n * n * 9; // 8 headings + "no heading yet" (8)
            if (c.G == null || c.G.Length != states) { c.G = new float[states]; c.From = new int[states]; c.Stamp = new int[states]; c.CurStamp = 0; }
            int stamp = ++c.CurStamp;
            Dictionary<int, bool> goalSet = null;
            if (goals != null) { goalSet = new Dictionary<int, bool>(); foreach (int q in goals) if (q >= 0) goalSet[q] = true; }
            int sx0 = start % n, sy0 = start / n;
            float tx = 0, ty = 0;
            if (goals != null) { tx = goals[0] % n; ty = goals[0] / n; }
            BinaryHeap open = new BinaryHeap();
            int s0 = start * 9 + 8;
            c.G[s0] = 0; c.From[s0] = -1; c.Stamp[s0] = stamp; open.Push(s0, 0);
            int found = -1, pops = 0;
            float maxG = c.MaxGrade;
            while (open.Count > 0 && pops++ < states * 2)
            {
                int st = open.Pop();
                int cur = st / 9, hd = st % 9;
                int cx = cur % n, cy = cur / n;
                bool other = c.OnRoad[cur] != 0 && (label != null ? label[cur] != label[start] : c.Owner[cur] != own);
                bool atGoal = goalSet != null ? goalSet.ContainsKey(cur)
                    : other && cur != start && Sq(cx - sx0) + Sq(cy - sy0) >= avoidNear * avoidNear;
                if (atGoal) { found = st; break; }
                if (other && cur != start && goalSet != null) continue; // fixed-goal routes stop at other roads (AddPath ends there)
                float gcur = c.G[st];
                for (int k = 0; k < 8; k++)
                {
                    float turn = 0;
                    if (hd < 8)
                    {
                        int steps = Math.Abs(k - hd); if (steps > 4) steps = 8 - steps;
                        if (TurnCost[steps] < 0) continue;
                        turn = TurnCost[steps] * Cell;
                    }
                    int x = cx + DX[k], y = cy + DY[k];
                    if (x < 1 || y < 1 || x > n - 2 || y > n - 2) continue;
                    int i = y * n + x;
                    if (!c.Land[i])
                    {
                        // a bridge: straight on across the water to the far bank
                        if (!c.S.Bridges || hd != k || !c.Land[cur]) continue;
                        int far = BridgeLanding(c, cx, cy, k);
                        if (far < 0 || BridgeTooClose(c, st, cur, far)) continue;
                        float span = Dist(far, cur, n) * Cell;
                        int ns2 = far * 9 + k;
                        float ng2 = gcur + span * 1.5f + BridgePenalty;
                        if (c.Stamp[ns2] != stamp || ng2 < c.G[ns2])
                        {
                            c.Stamp[ns2] = stamp; c.G[ns2] = ng2; c.From[ns2] = st;
                            int fx = far % n, fy = far / n;
                            float heur2 = goals != null ? (float)Math.Sqrt(Sq(fx - tx) + Sq(fy - ty)) * Cell * 0.35f : 0;
                            open.Push(ns2, ng2 + heur2);
                        }
                        continue;
                    }
                    bool diag = (k & 1) == 1;
                    if (diag && OtherRoad(c, cy * n + x, own) && OtherRoad(c, y * n + cx, own)) continue;
                    float len = diag ? Cell * 1.41421f : Cell;
                    float grade = Math.Abs(c.H.D[i] - c.H.D[cur]) / len;
                    if (grade > maxG * 1.6f) continue;
                    float cost = len * (1 + c.S.ClimbCost * Sq(grade / maxG)      // climbing
                                          + c.S.SideCost * Sq(c.Slope[i] / (2 * maxG))     // cutting across a steep hillside
                                          + Math.Max(0, c.Ridge[i]) * 0.08f        // over hills and ridges
                                          + c.Low[i] * 0.5f) + turn;               // low ground preferred
                    if (c.Shore[i] < ShoreKeep) cost *= 1 + 8 * Sq(1 - c.Shore[i] / ShoreKeep); // keep back from the water
                    else if (c.S.Character != null && c.S.Character.CoastPull && c.Shore[i] < 90) cost *= 0.65f; // coastal towns: roads along the shore
                    // near another road but not on it: running alongside costs, so routes keep apart and meet
                    // the roads they join square-on (the shortest way through the crowded band)
                    if (c.OnRoad[i] == 0 && c.CrowdOwner[i] != own) cost += len * 2.5f * c.Crowd[i];
                    if (c.OnRoad[i] != 0 && c.Owner[i] != own) cost *= 0.35f;
                    if (x < 10 || y < 10 || x > n - 11 || y > n - 11) cost *= 10; // keep off the world edge (~80 m; edge roads still cross it)
                    int ns = i * 9 + k;
                    float ng = gcur + cost;
                    if (c.Stamp[ns] != stamp || ng < c.G[ns])
                    {
                        c.Stamp[ns] = stamp; c.G[ns] = ng; c.From[ns] = st;
                        float heur = goals != null ? (float)Math.Sqrt(Sq(x - tx) + Sq(y - ty)) * Cell * 0.35f : 0;
                        open.Push(ns, ng + heur);
                    }
                }
            }
            if (found < 0) return null;
            List<int> path = new List<int>();
            for (int st = found; st >= 0; st = c.From[st]) path.Add(st / 9);
            path.Reverse();
            return path;
        }

        /// <summary>
        /// From land cell (cx, cy) heading k: the first land cell on the far side of the water, if the water is at most
        /// MaxBridgeWater wide, the far bank isn't much higher or lower, and no road is crossed. -1 otherwise.
        /// </summary>
        private static int BridgeLanding(Ctx c, int cx, int cy, int k)
        {
            int n = c.N;
            float len = (k & 1) == 1 ? Cell * 1.41421f : Cell;
            int maxSteps = (int)(MaxBridgeWater / len) + 1;
            for (int t = 1; t <= maxSteps + 1; t++)
            {
                int x = cx + DX[k] * t, y = cy + DY[k] * t;
                if (x < 2 || y < 2 || x > n - 3 || y > n - 3) return -1;
                int i = y * n + x;
                if (c.OnRoad[i] != 0) return -1;
                if (!c.Land[i])
                {
                    if (t > maxSteps) return -1;
                    continue;
                }
                if (t == 1) return -1;
                for (int e = 1; e <= 3; e++)
                {
                    int bx = cx - DX[k] * e, by = cy - DY[k] * e;
                    if (bx < 1 || by < 1 || bx > n - 2 || by > n - 2 || !c.Land[by * n + bx]) return -1;
                }
                // land again: the bank must be about level with ours and lead on (not a sliver of land)
                for (int e = 1; e <= 3; e++) // at least ~24 m of land beyond: no bridges back to back over a sliver
                {
                    int ex = cx + DX[k] * (t + e), ey = cy + DY[k] * (t + e);
                    if (ex < 1 || ey < 1 || ex > n - 2 || ey > n - 2 || !c.Land[ey * n + ex]) return -1;
                }
                if (Math.Abs(c.H.D[i] - c.H.D[cy * n + cx]) > 4) return -1;
                return i;
            }
            return -1;
        }

        /// <summary>Bridge pieces are 65-72 m long, so two bridges need ~110 m between them: none on this route within
        /// 14 steps back, and none planned already near this one's middle.</summary>
        private static bool BridgeTooClose(Ctx c, int st, int cur, int far)
        {
            int n = c.N;
            float mx = (cur % n + far % n) * Cell / 2, my = (cur / n + far / n) * Cell / 2;
            foreach (float[] b in c.BridgeMids) if (Sq(b[0] - mx) + Sq(b[1] - my) < 110 * 110) return true;
            int s = st;
            for (int k = 0; k < 14 && s >= 0; k++)
            {
                int prev = c.From[s];
                if (prev < 0) break;
                int a = s / 9, b = prev / 9;
                if (Math.Max(Math.Abs(a % n - b % n), Math.Abs(a / n - b / n)) > 1) return true; // a jump = a bridge
                s = prev;
            }
            return false;
        }

        private static bool OtherRoad(Ctx c, int i, int own) { return c.OnRoad[i] != 0 && c.Owner[i] != own; }

        /// <summary>
        /// Adds the new part of a route: it starts at the last cell of the owner's own roads it leaves from and stops where
        /// it reaches another road (a T-junction) or its goal.
        /// </summary>
        private static bool AddPath(Ctx c, List<int> path, RoadKind kind, int own, int area, int[] label = null)
        {
            if (path == null || path.Count < 2) return false;
            List<float[]> pts = new List<float[]>(), spans = new List<float[]>();
            int n = c.N;
            int skipTo = -1;
            for (int k = 0; k < path.Count; k++)
            {
                int cell = path[k];
                float[] p = { (cell % n) * Cell, (cell / n) * Cell };
                if (k > 0 && Math.Max(Math.Abs(cell % n - path[k - 1] % n), Math.Abs(cell / n - path[k - 1] / n)) > 1)
                {
                    float[] bank = { (path[k - 1] % n) * Cell, (path[k - 1] / n) * Cell };
                    spans.Add(new[] { bank[0], bank[1], p[0], p[1] });
                    // straight on to the bridge and off it: the road's last and next ~24 m follow the bridge's axis
                    // (BridgeLanding checked that land), so it doesn't turn sharply at the bank
                    float dl = (float)Math.Sqrt(Sq(p[0] - bank[0]) + Sq(p[1] - bank[1]));
                    float ux = (p[0] - bank[0]) / dl, uy = (p[1] - bank[1]) / dl;
                    float[] before = { bank[0] - ux * 3 * Cell, bank[1] - uy * 3 * Cell };
                    while (pts.Count > 1 && D2(pts[pts.Count - 2], bank) < Sq(3 * Cell + 2)) pts.RemoveAt(pts.Count - 2);
                    if (pts.Count >= 1 && D2(pts[pts.Count - 1], bank) < 1) pts.Insert(pts.Count - 1, before);
                    if (k + 4 < path.Count)
                    {
                        pts.Add(p);
                        pts.Add(new[] { p[0] + ux * 3 * Cell, p[1] + uy * 3 * Cell });
                        skipTo = k + 3;
                        continue;
                    }
                }
                if (k <= skipTo && k < path.Count - 1 && c.OnRoad[cell] == 0) continue; // on the straight beyond the bridge
                bool road = c.OnRoad[cell] != 0, mine = road && (label != null ? label[cell] == label[path[0]] : c.Owner[cell] == own);
                if (mine && k < path.Count - 1) { pts.Clear(); spans.Clear(); pts.Add(p); continue; } // still on our own roads
                pts.Add(p);
                if (road && k > 0) break;
            }
            if (pts.Count < 2) return false;
            AddRoad(c, pts, kind, own, area);
            c.Net.Roads[c.Net.Roads.Count - 1].Spans = spans;
            foreach (float[] b in spans) c.BridgeMids.Add(new[] { (b[0] + b[2]) / 2, (b[1] + b[3]) / 2 });
            return true;
        }

        private static void AddRoad(Ctx c, List<float[]> pts, RoadKind kind, int own, int area)
        {
            Road r = new Road(); r.Kind = kind; r.Area = area; r.Points = pts;
            c.Net.Roads.Add(r);
            for (int k = 0; k < pts.Count; k++)
            {
                if (k == 0) { int i0 = Idx(c, pts[0][0], pts[0][1]); MarkCell(c, i0, kind, own); if (i0 >= 0) r.Cells.Add(i0); }
                else MarkLine(c, pts[k - 1], pts[k], kind, own, r.Cells);
            }
        }

        private static void RemoveFrom(Ctx c, int first, int own)
        {
            // only used right after adding, before anything else is routed: unmark and drop
            for (int k = c.Net.Roads.Count - 1; k >= first; k--)
            {
                foreach (int i in c.Net.Roads[k].Cells) if (c.Owner[i] == own) c.OnRoad[i] = 0;
                // its bridges no longer keep others away
                foreach (float[] b in c.Net.Roads[k].Spans)
                    c.BridgeMids.RemoveAll(delegate (float[] q) { return Math.Abs(q[0] - (b[0] + b[2]) / 2) < 0.1f && Math.Abs(q[1] - (b[1] + b[3]) / 2) < 0.1f; });
                c.Net.Roads.RemoveAt(k);
            }
        }

        /// <summary>Marks the cells between two points 4-connected, so an 8-connected route can't slip through.</summary>
        private static void MarkLine(Ctx c, float[] a, float[] b, RoadKind kind, int own, List<int> cells)
        {
            int x0 = (int)(a[0] / Cell + 0.5f), y0 = (int)(a[1] / Cell + 0.5f), x1 = (int)(b[0] / Cell + 0.5f), y1 = (int)(b[1] / Cell + 0.5f);
            int sx = Math.Sign(x1 - x0), sy = Math.Sign(y1 - y0), ax = Math.Abs(x1 - x0), ay = Math.Abs(y1 - y0);
            int x = x0, y = y0, ex = 0, ey = 0;
            MarkCell(c, Cl(c, x, y), kind, own);
            while (x != x1 || y != y1)
            {
                // step along the axis that lags behind the line most
                if (ay == 0 || (ax != 0 && (ex + 1) * ay <= (ey + 1) * ax)) { x += sx; ex++; } else { y += sy; ey++; }
                int ci = Cl(c, x, y);
                MarkCell(c, ci, kind, own);
                if (ci >= 0) cells.Add(ci);
            }
        }

        private static int Cl(Ctx c, int x, int y) { return x < 0 || y < 0 || x >= c.N || y >= c.N ? -1 : y * c.N + x; }

        private static void MarkCell(Ctx c, int i, RoadKind kind, int own)
        {
            if (i < 0 || i >= c.OnRoad.Length || c.OnRoad[i] != 0) return;
            c.OnRoad[i] = (int)kind + 1; c.Owner[i] = own;
            // roads keep ~30 m apart unless they join: cells within 4 cells get a 'crowded' value, 1 next to the road
            int n = c.N, x0 = i % n, y0 = i / n;
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = x0 + dx, y = y0 + dy;
                    if (x < 0 || y < 0 || x >= n || y >= n) continue;
                    float v = 1 - (float)Math.Sqrt(dx * dx + dy * dy) / 4.5f;
                    int j = y * n + x;
                    if (v > c.Crowd[j]) { c.Crowd[j] = v; c.CrowdOwner[j] = own; }
                }
        }

        /// <summary>
        /// Walks from (x, y) in direction (dx, dy), turning up to 30° a step towards the flattest way (with 'bias' > 0 it
        /// also keeps more to its heading), for 'target' metres; stops at the grade limit or near another road.
        /// </summary>
        private static List<float[]> Walk(Ctx c, float x, float y, float dx, float dy, float target, int own, float bias = 0.004f)
        {
            List<float[]> pts = new List<float[]>();
            for (float walked = 0; walked < target; walked += Cell)
            {
                float bestCost = float.MaxValue, bdx = 0, bdy = 0;
                for (int k = -2; k <= 2; k++)
                {
                    float ang = k * 0.12f; // up to ±14° per 8 m: smooth curves
                    float ndx = dx * (float)Math.Cos(ang) - dy * (float)Math.Sin(ang), ndy = dx * (float)Math.Sin(ang) + dy * (float)Math.Cos(ang);
                    float nx = x + ndx * Cell, ny = y + ndy * Cell;
                    if (!Inside(c, nx, ny, 4)) continue;
                    int i = Idx(c, nx, ny);
                    if (!c.Land[i]) continue;
                    if (walked > 16 && c.OnRoad[i] != 0) continue;
                    float grade = Math.Abs(HeightAt(c, nx, ny) - HeightAt(c, x, y)) / Cell;
                    float cost = grade + 0.3f * c.Slope[i] + Math.Max(0, c.Ridge[i]) * 0.004f + Math.Abs(k) * bias * 0.1f + Math.Abs(k) * 0.004f;
                    if (cost < bestCost) { bestCost = cost; bdx = ndx; bdy = ndy; }
                }
                if (bestCost > c.MaxGrade) break;
                dx = bdx; dy = bdy; x += dx * Cell; y += dy * Cell;
                if (walked > 24 && NearRoad(c, x, y, 3)) break;
                pts.Add(new[] { x, y });
            }
            return pts;
        }

        private static bool NearRoad(Ctx c, float x, float y, int r)
        {
            int cx = (int)(x / Cell + 0.5f), cy = (int)(y / Cell + 0.5f);
            for (int yy = cy - r; yy <= cy + r; yy++)
                for (int xx = cx - r; xx <= cx + r; xx++)
                    if (xx >= 0 && yy >= 0 && xx < c.N && yy < c.N && c.OnRoad[yy * c.N + xx] != 0) return true;
            return false;
        }

        /// <summary>No other owner's road within ~32 m (keeps neighbourhoods and grids from overlapping).</summary>
        private static bool FreeOfOthers(Ctx c, float x, float y, int own)
        {
            int cx = (int)(x / Cell + 0.5f), cy = (int)(y / Cell + 0.5f);
            for (int yy = cy - 4; yy <= cy + 4; yy++)
                for (int xx = cx - 4; xx <= cx + 4; xx++)
                    if (xx >= 0 && yy >= 0 && xx < c.N && yy < c.N && c.OnRoad[yy * c.N + xx] != 0 && c.Owner[yy * c.N + xx] != own) return false;
            return true;
        }

        /// <summary>For each road, whether its start / end cell touches another road's cells.</summary>
        private static List<bool[]> EndsMeeting(Ctx c)
        {
            int n = c.N;
            Dictionary<int, List<int>> byCell = new Dictionary<int, List<int>>();
            for (int r = 0; r < c.Net.Roads.Count; r++)
                foreach (int i in c.Net.Roads[r].Cells)
                {
                    List<int> l;
                    if (!byCell.TryGetValue(i, out l)) { l = new List<int>(); byCell[i] = l; }
                    if (l.Count == 0 || l[l.Count - 1] != r) l.Add(r);
                }
            List<bool[]> res = new List<bool[]>();
            for (int r = 0; r < c.Net.Roads.Count; r++)
            {
                List<int> cells = c.Net.Roads[r].Cells;
                bool[] m = new bool[2];
                for (int e = 0; e < 2 && cells.Count > 0; e++)
                {
                    int at = e == 0 ? cells[0] : cells[cells.Count - 1], ax = at % n, ay = at / n;
                    for (int dy = -1; dy <= 1 && !m[e]; dy++)
                        for (int dx = -1; dx <= 1 && !m[e]; dx++)
                        {
                            int x = ax + dx, y = ay + dy;
                            if (x < 0 || y < 0 || x >= n || y >= n) continue;
                            List<int> l;
                            if (byCell.TryGetValue(y * n + x, out l)) foreach (int q in l) if (q != r) { m[e] = true; break; }
                        }
                }
                res.Add(m);
            }
            return res;
        }

        /// <summary>
        /// Smoothing rounds corners, so a road that ended on another road's bend can be left metres short of it. Ends
        /// that met another road are moved onto the nearest point of the other roads' final lines (within 30 m).
        /// </summary>
        private static void SnapEnds(RoadNet net, List<bool[]> meets)
        {
            for (int r = 0; r < net.Roads.Count && r < meets.Count; r++)
                for (int e = 0; e < 2; e++)
                {
                    if (!meets[r][e]) continue;
                    List<float[]> pts = net.Roads[r].Points;
                    if (pts.Count < 2) continue;
                    float[] p = e == 0 ? pts[0] : pts[pts.Count - 1];
                    float bd = 30 * 30; float[] best = null;
                    for (int q = 0; q < net.Roads.Count; q++)
                    {
                        if (q == r) continue;
                        List<float[]> o = net.Roads[q].Points;
                        for (int k = 0; k + 1 < o.Count; k++)
                        {
                            float[] a = o[k], b = o[k + 1];
                            float dx = b[0] - a[0], dy = b[1] - a[1], l = dx * dx + dy * dy;
                            float t = l < 1e-6f ? 0 : Math.Max(0, Math.Min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / l));
                            float[] pr = { a[0] + dx * t, a[1] + dy * t };
                            float d = D2(pr, p);
                            if (d < bd) { bd = d; best = pr; }
                        }
                    }
                    if (best == null || bd < 0.25f) continue;
                    if (e == 0) pts.Insert(0, best); else pts.Add(best);
                }
        }

        /// <summary>
        /// Road ends that stop a few metres short of each other become one junction (pt13: an end 6 m from a junction
        /// was built as a second junction on top of the first, the tester's "T laid on top of the road"), and junctions
        /// closer than 25 m merge into the one more roads meet at. Moved ends drop the points they pass over.
        /// </summary>
        private static void MergeEnds(RoadNet net)
        {
            foreach (Road r in net.Roads) Unhook(r.Points);
            TrimAlongside(net);
            List<float[]> ends = new List<float[]>(); List<int[]> who = new List<int[]>();
            for (int r = 0; r < net.Roads.Count; r++)
            {
                List<float[]> p = net.Roads[r].Points;
                ends.Add(p[0]); who.Add(new[] { r, 0 });
                ends.Add(p[p.Count - 1]); who.Add(new[] { r, 1 });
            }
            // 1. clusters of ends within 12 m: all moved to the point most of them already share
            bool[] done = new bool[ends.Count];
            for (int a = 0; a < ends.Count; a++)
            {
                if (done[a]) continue;
                List<int> cl = new List<int>();
                for (int b = 0; b < ends.Count; b++)
                    if (!done[b] && D2(ends[a], ends[b]) < 12 * 12 && who[b][0] != who[a][0] || b == a) cl.Add(b);
                if (cl.Count < 2) continue;
                float[] anchor = ends[a]; int best = -1;
                foreach (int i in cl)
                {
                    int same = 0; foreach (int j in cl) if (D2(ends[i], ends[j]) < 0.25f) same++;
                    if (same > best) { best = same; anchor = ends[i]; }
                }
                foreach (int i in cl)
                {
                    done[i] = true;
                    if (D2(ends[i], anchor) >= 0.25f) MoveEnd(net.Roads[who[i][0]], who[i][1], anchor);
                }
            }
            // 2. junctions closer than 25 m (two Ts on the same road, a T next to a crossing): ends at the weaker one move
            // to the stronger
            for (int pass = 0; pass < 3; pass++)
            {
                List<float[]> js = FindJunctions(net, 1f);
                bool moved = false;
                for (int a = 0; a < js.Count && !moved; a++)
                    for (int b = a + 1; b < js.Count && !moved; b++)
                    {
                        if (D2(js[a], js[b]) >= 25 * 25) continue;
                        float[] keep = Degree(net, js[a]) >= Degree(net, js[b]) ? js[a] : js[b], drop = keep == js[a] ? js[b] : js[a];
                        foreach (Road r in net.Roads)
                            for (int e = 0; e < 2; e++)
                            {
                                float[] q = e == 0 ? r.Points[0] : r.Points[r.Points.Count - 1];
                                if (D2(q, drop) < 1f && D2(q, keep) > 1f) { MoveEnd(r, e, keep); moved = true; }
                            }
                    }
                if (!moved) break;
            }
            net.Roads.RemoveAll(delegate (Road r) { return r.Points.Count < 2 || (r.Points.Count == 2 && D2(r.Points[0], r.Points[1]) < 1f); });
        }

        /// <summary>Drops points where the road turns back on itself (more than 120 deg), e.g. an end that juts past a
        /// junction and hooks back.</summary>
        private static void Unhook(List<float[]> p)
        {
            for (bool again = true; again && p.Count > 2;)
            {
                again = false;
                for (int k = 1; k + 1 < p.Count; k++)
                {
                    float ax = p[k][0] - p[k - 1][0], ay = p[k][1] - p[k - 1][1], bx = p[k + 1][0] - p[k][0], by = p[k + 1][1] - p[k][1];
                    float la = (float)Math.Sqrt(ax * ax + ay * ay), lb = (float)Math.Sqrt(bx * bx + by * by);
                    if (la < 1e-3f || lb < 1e-3f) { p.RemoveAt(k); again = true; break; }
                    if ((ax * bx + ay * by) / (la * lb) < -0.5f) { p.RemoveAt(k); again = true; break; }
                }
            }
        }

        /// <summary>
        /// A road that ends on another after running alongside it (within 12 m) is cut back to where it first comes
        /// that close and joined there (pt13, Oasis Landing: a street ran 40 m beside the main road into its junction).
        /// </summary>
        private static void TrimAlongside(RoadNet net)
        {
            for (int r = 0; r < net.Roads.Count; r++)
                for (int e = 0; e < 2; e++)
                {
                    List<float[]> p = net.Roads[r].Points;
                    if (p.Count < 3) continue;
                    if (e == 1) p.Reverse();
                    int best = -1; int bestK = 0; float[] proj = null;
                    for (int q = 0; q < net.Roads.Count; q++)
                    {
                        if (q == r) continue;
                        List<float[]> o = net.Roads[q].Points;
                        int k = 0; float[] pr = null;
                        while (k < p.Count - 1)
                        {
                            float[] np = Nearest(o, p[k]);
                            if (D2(np, p[k]) >= 16 * 16) break; // as RoadPlanner.Alongside (was 12 m: Monte Vista s1 ran in 10-15 m beside)
                            pr = np; k++;
                        }
                        if (k >= 2 && k > bestK) { bestK = k; best = q; proj = pr; }
                    }
                    if (best >= 0)
                    {
                        // the stretch beside the other road: points 0..bestK-1; keep from bestK-1 on, joined at its projection
                        float along = 0; for (int k = 1; k < bestK; k++) along += (float)Math.Sqrt(D2(p[k - 1], p[k]));
                        if (along > 10f && p.Count - (bestK - 1) >= 2)
                        {
                            float[] join = Nearest(net.Roads[best].Points, p[bestK - 1]);
                            p.RemoveRange(0, bestK - 1);
                            p[0] = join;
                        }
                    }
                    if (e == 1) p.Reverse();
                }
        }

        private static float[] Nearest(List<float[]> o, float[] x)
        {
            float bd = float.MaxValue; float[] best = o[0];
            for (int k = 0; k + 1 < o.Count; k++)
            {
                float[] a = o[k], b = o[k + 1];
                float dx = b[0] - a[0], dy = b[1] - a[1], l = dx * dx + dy * dy;
                float t = l < 1e-6f ? 0 : Math.Max(0, Math.Min(1, ((x[0] - a[0]) * dx + (x[1] - a[1]) * dy) / l));
                float[] pr = { a[0] + dx * t, a[1] + dy * t };
                float d = D2(pr, x);
                if (d < bd) { bd = d; best = pr; }
            }
            return best;
        }

        /// <summary>
        /// Roads leave a junction straight, as EA's do (median heading change 0 deg over the first 40 m in 21 worlds;
        /// tools: objst/wiggle study): the first 30 m of every road end at a junction becomes a straight line to the point
        /// 30 m along, where the road keeps to land and the grade limit that way.
        /// </summary>
        private static void StraightEnds(Ctx c)
        {
            const float run = 30f;
            List<float[]> js = FindJunctions(c.Net, 1f);
            foreach (Road r in c.Net.Roads)
                for (int e = 0; e < 2; e++)
                {
                    List<float[]> p = r.Points;
                    float[] end = e == 0 ? p[0] : p[p.Count - 1];
                    bool atJ = false; foreach (float[] j in js) if (D2(j, end) < 1f) { atJ = true; break; }
                    if (!atJ) continue;
                    if (e == 1) p.Reverse();
                    // the point 'run' metres along (not past the road's middle)
                    float total = 0; for (int k = 1; k < p.Count; k++) total += (float)Math.Sqrt(D2(p[k - 1], p[k]));
                    float want = Math.Min(run, total * 0.45f), acc = 0; int kk = -1; float[] at = null;
                    for (int k = 1; k < p.Count && at == null; k++)
                    {
                        float l = (float)Math.Sqrt(D2(p[k - 1], p[k]));
                        if (acc + l >= want && l > 1e-3f)
                        {
                            float t = (want - acc) / l;
                            at = new[] { p[k - 1][0] + (p[k][0] - p[k - 1][0]) * t, p[k - 1][1] + (p[k][1] - p[k - 1][1]) * t };
                            kk = k;
                        }
                        acc += l;
                    }
                    if (at != null && want >= 12f)
                    {
                        // square to the junction's through road when within 30 deg of it (#134: a road meeting its port
                        // off-square bends into it; two such bends between near junctions made an S)
                        float[] sq = SquareTo(c.Net, r, p[0], at, want);
                        if (sq != null && StraightOk(c, p[0], sq)) at = sq;
                    }
                    if (at != null && want >= 12f && StraightOk(c, p[0], at))
                    {
                        p.RemoveRange(1, kk - 1);
                        p.Insert(1, at);
                    }
                    if (e == 1) p.Reverse();
                }
        }

        /// <summary>
        /// A short road (under 200 m) whose two ends are T-junctions on roughly parallel roads becomes one straight street
        /// square to both, its ends slid up to 12 m along their roads (the tester, 2026-10-25, #134: a link between two near
        /// junctions S-bent, the turns out of both ends overlapping; ChatGPT: fit the whole link, not each end).
        /// </summary>
        private static void StraightLinks(Ctx c)
        {
            int made = 0;
            foreach (Road r in c.Net.Roads)
            {
                if (r.Spans.Count > 0 || r.Straight) continue;
                List<float[]> p = r.Points;
                float len = 0; for (int k = 1; k < p.Count; k++) len += (float)Math.Sqrt(D2(p[k - 1], p[k]));
                if (len > 200 || len < 30) continue;
                float[] a = p[0], b = p[p.Count - 1];
                if (len > 1.3f * Math.Sqrt(D2(a, b))) continue; // a winding road stays as it is
                Road ha = HostOf(c.Net, r, a), hb = HostOf(c.Net, r, b);
                if (ha == null || hb == null || ha == hb) continue;
                float[] ta = SegDir(ha.Points, a), tb = SegDir(hb.Points, b);
                if (Math.Abs(ta[0] * tb[0] + ta[1] * tb[1]) < 0.966f) continue; // within 15 deg of parallel
                if (ta[0] * tb[0] + ta[1] * tb[1] < 0) { tb = new[] { -tb[0], -tb[1] }; }
                float axx = ta[0] + tb[0], axz = ta[1] + tb[1], al = (float)Math.Sqrt(axx * axx + axz * axz);
                axx /= al; axz /= al;
                // how far b sits along the streets from a: half of it taken up at each end
                float off = (b[0] - a[0]) * axx + (b[1] - a[1]) * axz;
                if (Math.Abs(off) < 0.5f || Math.Abs(off) > 24f) continue;
                float[] a2 = { a[0] + ta[0] * Sign(ta, axx, axz) * off / 2, a[1] + ta[1] * Sign(ta, axx, axz) * off / 2 };
                float[] b2 = { b[0] - tb[0] * off / 2, b[1] - tb[1] * off / 2 };
                if (MinSegD2(ha.Points, a2) > 4f || MinSegD2(hb.Points, b2) > 4f) continue; // still on their roads
                if (!StraightOk(c, a2, b2)) continue;
                r.Points = Resample(new List<float[]> { a2, b2 }, 20f);
                r.Straight = true; made++;
            }
            c.Net.LinksStraightened = made;
        }

        /// <summary>
        /// A dead end keeps at least DeadEndClear m from any road it isn't joined to near by (the tester, 2026-10-25: no road
        /// ends near other roads unless they connect). EA's 22 worlds: of ~330 dead-end tips (not at the map edge), the
        /// nearest unrelated road is a median 70-110 m away, under 30 m for 19 tips (Bridgeport and Twinbrook downtowns),
        /// under 50 m for ~10% ('unrelated' = more than max(80 m, 3x the gap) away along the roads; scratch spacing.py).
        /// A tip that stops short is joined straight to the road it nears if that link is land within the grade limit and
        /// carries on the road's heading (a T, or two dead ends facing each other made one road); else the road is cut back
        /// until its tip is clear; a short spur that can't be is dropped, and so is a short piece joined to nothing.
        /// </summary>
        private static void DeadEndClearance(Ctx c)
        {
            // inside the downtown a shorter clearance: EA's dense downtowns do leave short stubs near other streets
            // (Bridgeport 16 of its 49 near tips; the tester agreed, 2026-10-25)
            const float clearOut = 50f, clearTown = 25f, downtownR = 230f, joinReach = 50f;
            float[] ctr = c.Net.Centre;
            // pieces already joined to nothing at either end before this pass: only these may be dropped as loose
            // (dropping one piece must not leave its neighbour loose and drop that too: Bridgeport lost ~8 pieces and
            // 18 T-junctions per layout that way, #165)
            Dictionary<Road, bool> looseAtStart = new Dictionary<Road, bool>();
            foreach (Road r0 in c.Net.Roads)
            {
                List<float[]> q0 = r0.Points;
                if (q0.Count >= 2 && !Touches(c.Net, r0, q0[0]) && !Touches(c.Net, r0, q0[q0.Count - 1])) looseAtStart[r0] = true;
            }
            for (int pass = 0; pass < 3; pass++)
            {
                bool changed = false;
                for (int ri = 0; ri < c.Net.Roads.Count; ri++)
                {
                    Road r = c.Net.Roads[ri];
                    if (r.Spans.Count > 0) continue;
                    for (int e = 0; e < 2 && ri < c.Net.Roads.Count && c.Net.Roads[ri] == r; e++)
                    {
                        List<float[]> p = r.Points;
                        if (p.Count < 2) break;
                        float[] tip = e == 0 ? p[0] : p[p.Count - 1];
                        if (Touches(c.Net, r, tip) || EdgeDist(c, tip[0], tip[1]) < 60f) continue;
                        float clear = ctr != null && D2(tip, ctr) < downtownR * downtownR ? clearTown : clearOut;
                        Road near; float[] at;
                        float d = UnrelatedGap(c.Net, r, e, tip, clear, out near, out at);
                        if (d >= clear) continue;
                        if (e == 1) p.Reverse();
                        // 0. a piece joined to nothing at either end (left over when a link was dropped) goes
                        float[] far = p[p.Count - 1];
                        bool inTown = ctr != null && D2(tip, ctr) < downtownR * downtownR;
                        if (looseAtStart.ContainsKey(r) && !inTown && !Touches(c.Net, r, far) && EdgeDist(c, far[0], far[1]) >= 60f)
                        {
                            float plen = 0; for (int k = 1; k < p.Count; k++) plen += (float)Math.Sqrt(D2(p[k - 1], p[k]));
                            if (plen < 300f) { c.Net.Roads.RemoveAt(ri); ri--; c.Net.DeadEndsDropped++; changed = true; break; }
                        }
                        // 1. join: straight on from the tip to the road it nears
                        bool joined = false;
                        if (near != null && near != r && d <= joinReach && p.Count >= 2)
                        {
                            float hx = p[0][0] - p[1][0], hz = p[0][1] - p[1][1], hl = (float)Math.Sqrt(hx * hx + hz * hz);
                            float lx = at[0] - p[0][0], lz = at[1] - p[0][1], ll = (float)Math.Sqrt(lx * lx + lz * lz);
                            bool ahead = hl > 1e-3f && ll > 1e-3f && (hx * lx + hz * lz) / (hl * ll) > 0.5f; // within 60 deg
                            if (ahead && StraightOk(c, p[0], at) && !CrossesOther(c.Net, r, near, p[0], at))
                            {
                                p.Insert(0, at); joined = true; c.Net.DeadEndsJoined++; changed = true;
                            }
                        }
                        // 2. else cut back until the tip is clear (at least 40 m of road left)
                        // cut back / drop only a tip that stops really close (under 30 m): cutting every tip within 50 m took
                        // 0.5 km of road and 5 T-junctions per map with it (Fidelity roads 68.4 -> 66.3 on 8 worlds, #165)
                        if (!joined && d < 30f)
                        {
                            float len = 0; for (int k = 1; k < p.Count; k++) len += (float)Math.Sqrt(D2(p[k - 1], p[k]));
                            List<float[]> q = Resample(p, 4f);
                            int cut = -1; float cutLen = 0;
                            for (int k = 1; k < q.Count; k++)
                            {
                                cutLen += (float)Math.Sqrt(D2(q[k - 1], q[k]));
                                if (len - cutLen < 40f) break;
                                List<float[]> rest = q.GetRange(k, q.Count - k);
                                Road probe = new Road(); probe.Points = rest; probe.Kind = r.Kind;
                                Road n2; float[] a2;
                                if (UnrelatedGap(c.Net, probe, 0, rest[0], clear, out n2, out a2, r) >= clear) { cut = k; break; }
                            }
                            if (cut > 0)
                            {
                                r.Points = Simplify(q.GetRange(cut, q.Count - cut), 0.5f, 30f); p = r.Points;
                                c.Net.DeadEndsTrimmed++; changed = true;
                            }
                            else if (len < 150f && !inTown && OtherEndJoined(c.Net, r, p))
                            {
                                c.Net.Roads.RemoveAt(ri); ri--; c.Net.DeadEndsDropped++; changed = true;
                                break;
                            }
                        }
                        if (e == 1 && c.Net.Roads.Contains(r)) r.Points.Reverse();
                    }
                }
                if (!changed) break;
            }
        }

        /// <summary>
        /// Groups of roads left cut off from the main network by smoothing and trimming (an end within 15 m of another road
        /// counts as joined, as the "connected" check counts it). The main network is the group holding the road nearest the
        /// town centre (else the longest). Each round, every other group (largest first) looks for a straight link from one of
        /// its free ends (an end already on another road is never extended: it could cross that road with no junction) to the
        /// main network: up to 80 m, land and grade checked, crossing nothing, carrying on within 60 deg of the end's heading;
        /// it meets the middle of a road at 45 deg or more (no hook), or carries straight on (within 30 deg) where it meets a
        /// road's end. The first group that can is linked and the round starts again, since the main network has grown. When
        /// none can, groups under 600 m holding no bridge or main road are dropped and the rest are counted (IslandsLeft),
        /// so the "connected" must rejects the layout. Bridgeport: 11 of 12 layouts failed "connected" this way (#165).
        /// </summary>
        private static void ReconnectIslands(Ctx c)
        {
            const float JoinD = 15f, Reach = 80f, DropKm = 0.6f, EndZone = 12f;
            c.Net.IslandsLeft = 0;
            for (int guard = 0; guard < 1000; guard++)
            {
                List<Road> roads = c.Net.Roads; int n = roads.Count;
                if (n <= 1) return;
                int[] comp = new int[n]; for (int i = 0; i < n; i++) comp[i] = -1;
                List<float> compLen = new List<float>(); int nc = 0;
                for (int s0 = 0; s0 < n; s0++)
                {
                    if (comp[s0] >= 0) continue;
                    Stack<int> st = new Stack<int>(); st.Push(s0); comp[s0] = nc; float L = 0;
                    while (st.Count > 0)
                    {
                        int a = st.Pop(); L += RoadLen(roads[a].Points);
                        for (int b = 0; b < n; b++)
                            if (comp[b] < 0 && Meets(roads[a], roads[b], JoinD)) { comp[b] = nc; st.Push(b); }
                    }
                    compLen.Add(L); nc++;
                }
                if (nc <= 1) return;
                int main = 0; for (int k = 1; k < nc; k++) if (compLen[k] > compLen[main]) main = k;
                float[] ctr = c.Net.Centre;
                if (ctr != null)
                {
                    float bd = float.MaxValue;
                    for (int i = 0; i < n; i++) { float d = MinSegD2(roads[i].Points, ctr); if (d < bd) { bd = d; main = comp[i]; } }
                }
                List<int> order = new List<int>();
                for (int k = 0; k < nc; k++) if (k != main) order.Add(k);
                order.Sort(delegate (int x, int y) { return compLen[y].CompareTo(compLen[x]); });
                bool linked = false;
                foreach (int g in order)
                {
                    float bestD = Reach; Road from = null; int fromEnd = 0; float[] at = null;
                    for (int i = 0; i < n; i++)
                    {
                        if (comp[i] != g || roads[i].Spans.Count > 0) continue;
                        List<float[]> p = roads[i].Points;
                        if (p.Count < 2) continue;
                        for (int e = 0; e < 2; e++)
                        {
                            float[] tip = e == 0 ? p[0] : p[p.Count - 1], prev = e == 0 ? p[1] : p[p.Count - 2];
                            bool joinedHere = false;
                            for (int o = 0; o < n && !joinedHere; o++) joinedHere = o != i && MinSegD2(roads[o].Points, tip) < JoinD * JoinD;
                            if (joinedHere) continue; // a T or a joint already: extending it could cross that road
                            float hx = tip[0] - prev[0], hz = tip[1] - prev[1], hl = (float)Math.Sqrt(hx * hx + hz * hz);
                            if (hl < 1e-3f) continue;
                            for (int j = 0; j < n; j++)
                            {
                                if (comp[j] != main) continue;
                                List<float[]> q = roads[j].Points;
                                float[] x = Nearest(q, tip);
                                float lx = x[0] - tip[0], lz = x[1] - tip[1], d = (float)Math.Sqrt(lx * lx + lz * lz);
                                if (d >= bestD || d < 1e-3f) continue;
                                if ((hx * lx + hz * lz) / (hl * d) < 0.5f) continue; // carries on from the end
                                int qe = D2(x, q[0]) < EndZone * EndZone ? 0 : D2(x, q[q.Count - 1]) < EndZone * EndZone ? 1 : -1;
                                if (qe >= 0)
                                {
                                    // at the target's end: join end to end, carrying straight on into it
                                    float[] qa = qe == 0 ? q[0] : q[q.Count - 1], qb = qe == 0 ? q[1] : q[q.Count - 2];
                                    float ox = qa[0] - qb[0], oz = qa[1] - qb[1], ol = (float)Math.Sqrt(ox * ox + oz * oz);
                                    if (ol < 1e-3f || (-(ox * lx + oz * lz)) / (ol * d) < 0.866f) continue;
                                    x = qa; lx = x[0] - tip[0]; lz = x[1] - tip[1]; d = (float)Math.Sqrt(lx * lx + lz * lz);
                                    if (d >= bestD || d < 1e-3f || (hx * lx + hz * lz) / (hl * d) < 0.5f) continue;
                                }
                                else
                                {
                                    float[] dir = DirAt(q, x);
                                    if (dir != null && Math.Abs((dir[0] * lx + dir[1] * lz) / d) > 0.7071f) continue; // meets at 45 deg or more
                                }
                                if (!StraightOk(c, tip, x) || CrossesOther(c.Net, roads[i], roads[j], tip, x)) continue;
                                bestD = d; from = roads[i]; fromEnd = e; at = x;
                            }
                        }
                    }
                    if (from == null) continue;
                    if (fromEnd == 0) from.Points.Insert(0, at); else from.Points.Add(at);
                    c.Net.IslandsJoined++;
                    linked = true;
                    break;
                }
                if (linked) continue;
                // nothing can be linked any more: drop the small groups, count the rest
                Dictionary<int, bool> drop = new Dictionary<int, bool>();
                foreach (int g in order)
                {
                    bool precious = false;
                    for (int i = 0; i < n && !precious; i++) precious = comp[i] == g && (roads[i].Spans.Count > 0 || roads[i].Kind == RoadKind.Main);
                    if (!precious && compLen[g] < DropKm * 1000f) { drop[g] = true; c.Net.IslandsDropped++; c.Net.IslandsDroppedKm += compLen[g] / 1000f; }
                }
                for (int i = n - 1; i >= 0; i--) if (drop.ContainsKey(comp[i])) roads.RemoveAt(i);
                c.Net.IslandsLeft = order.Count - drop.Count;
                return;
            }
        }

        /// <summary>Unit direction of the road segment nearest x; null if the road has no length there.</summary>
        private static float[] DirAt(List<float[]> q, float[] x)
        {
            float best = float.MaxValue; float[] d = null;
            for (int k = 1; k < q.Count; k++)
            {
                List<float[]> seg = new List<float[]> { q[k - 1], q[k] };
                float e = MinSegD2(seg, x);
                if (e >= best) continue;
                float dx = q[k][0] - q[k - 1][0], dz = q[k][1] - q[k - 1][1], l = (float)Math.Sqrt(dx * dx + dz * dz);
                if (l < 1e-3f) continue;
                best = e; d = new[] { dx / l, dz / l };
            }
            return d;
        }

        private static float RoadLen(List<float[]> p) { float L = 0; for (int k = 1; k < p.Count; k++) L += (float)Math.Sqrt(D2(p[k - 1], p[k])); return L; }

        private static bool Meets(Road a, Road b, float d)
        {
            float d2 = d * d;
            if (MinSegD2(b.Points, a.Points[0]) < d2 || MinSegD2(b.Points, a.Points[a.Points.Count - 1]) < d2) return true;
            return MinSegD2(a.Points, b.Points[0]) < d2 || MinSegD2(a.Points, b.Points[b.Points.Count - 1]) < d2;
        }

        /// <summary>Whether the point lies on another road (an end or the middle, within 2 m).</summary>
        private static bool Touches(RoadNet net, Road self, float[] x)
        {
            foreach (Road o in net.Roads) if (o != self && MinSegD2(o.Points, x) <= 4f) return true;
            return false;
        }

        private static bool OtherEndJoined(RoadNet net, Road r, List<float[]> pTipFirst)
        {
            return Touches(net, r, pTipFirst[pTipFirst.Count - 1]);
        }

        /// <summary>
        /// Distance from the tip (end e of r) to the nearest road it isn't joined to near by, up to 'reach': another road,
        /// unless r's other end lies on it and the way round (r's length + along it) is under max(80, 3x the gap); or r
        /// itself further on than that. 'skip' is ignored (the road a probe was cut from).
        /// </summary>
        private static float UnrelatedGap(RoadNet net, Road r, int e, float[] tip, float reach, out Road near, out float[] at, Road skip = null)
        {
            near = null; at = null;
            List<float[]> p = r.Points;
            float[] other = e == 0 ? p[p.Count - 1] : p[0];
            float len = 0; for (int k = 1; k < p.Count; k++) len += (float)Math.Sqrt(D2(p[k - 1], p[k]));
            float best = reach;
            foreach (Road o in net.Roads)
            {
                if (o == r || o == skip) continue;
                float[] x = Nearest(o.Points, tip);
                float d = (float)Math.Sqrt(D2(x, tip));
                if (d >= best) continue;
                if (MinSegD2(o.Points, other) <= 4f)
                {
                    float[] touch = Nearest(o.Points, other);
                    if (len + (float)Math.Sqrt(D2(touch, x)) <= Math.Max(80f, 3 * d)) continue; // joined near by: a short spur
                }
                best = d; near = o; at = x;
            }
            // r curling back on itself
            List<float[]> self = skip != null ? skip.Points : p;
            float arc = 0; int start = e == 0 ? 0 : p.Count - 1, dir = e == 0 ? 1 : -1;
            if (skip == null)
                for (int k = start + dir; k >= 0 && k < p.Count; k += dir)
                {
                    arc += (float)Math.Sqrt(D2(p[k - dir], p[k]));
                    float d = (float)Math.Sqrt(D2(p[k], tip));
                    if (d < best && arc > Math.Max(80f, 3 * d)) { best = d; near = r; at = p[k]; }
                }
            return best;
        }

        /// <summary>Whether the segment a-b crosses any road other than r and 'target' (the road it joins).</summary>
        private static bool CrossesOther(RoadNet net, Road r, Road target, float[] a, float[] b)
        {
            foreach (Road o in net.Roads)
            {
                if (o == r || o == target) continue;
                for (int k = 0; k + 1 < o.Points.Count; k++)
                    if (SegmentsCross(a, b, o.Points[k], o.Points[k + 1])) return true;
            }
            return false;
        }

        private static bool SegmentsCross(float[] a, float[] b, float[] c, float[] d)
        {
            float d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
            return ((d1 > 0) != (d2 > 0)) && ((d3 > 0) != (d4 > 0));
        }

        private static float Cross(float[] o, float[] a, float[] b) { return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]); }

        private static float Sign(float[] t, float ax, float az) { return t[0] * ax + t[1] * az >= 0 ? 1 : -1; }

        /// <summary>The road whose middle (not its ends) the point lies on (within 2 m): a T-junction's through road.</summary>
        private static Road HostOf(RoadNet net, Road self, float[] e)
        {
            foreach (Road o in net.Roads)
            {
                if (o == self) continue;
                List<float[]> q = o.Points;
                if (D2(q[0], e) < 144 || D2(q[q.Count - 1], e) < 144) continue;
                if (MinSegD2(q, e) <= 4f)
                {
                    // and no other road ends here (a crossroads moves with more than one road)
                    int ends = 0;
                    foreach (Road x in net.Roads) { if (D2(x.Points[0], e) < 4) ends++; if (D2(x.Points[x.Points.Count - 1], e) < 4) ends++; }
                    return ends == 1 ? o : null;
                }
            }
            return null;
        }

        private static float MinSegD2(List<float[]> q, float[] e)
        {
            float best = float.MaxValue;
            for (int k = 0; k + 1 < q.Count; k++) best = Math.Min(best, SegD2(q[k], q[k + 1], e));
            return best;
        }

        /// <summary>Unit direction of the polyline's segment nearest the point.</summary>
        private static float[] SegDir(List<float[]> q, float[] e)
        {
            int bk = 0; float best = float.MaxValue;
            for (int k = 0; k + 1 < q.Count; k++) { float d = SegD2(q[k], q[k + 1], e); if (d < best) { best = d; bk = k; } }
            float dx = q[bk + 1][0] - q[bk][0], dz = q[bk + 1][1] - q[bk][1], l = (float)Math.Sqrt(dx * dx + dz * dz);
            return l < 1e-4f ? new[] { 1f, 0f } : new[] { dx / l, dz / l };
        }

        /// <summary>
        /// The point 'run' m from junction j along the direction j->at turned to the nearest right angle of the junction's
        /// through road (its two most nearly opposite other roads, within 30 deg of straight), if that turn is under 30 deg;
        /// else null.
        /// </summary>
        private static float[] SquareTo(RoadNet net, Road self, float[] j, float[] at, float run)
        {
            List<float[]> dirs = new List<float[]>();
            foreach (Road o in net.Roads)
            {
                if (o == self) continue;
                List<float[]> q = o.Points;
                if (D2(q[0], j) < 1f) dirs.Add(Toward(q, 0, 1, j));
                else if (D2(q[q.Count - 1], j) < 1f) dirs.Add(Toward(q, q.Count - 1, -1, j));
                else
                    for (int k = 1; k + 1 < q.Count; k++)
                        if (D2(q[k], j) < 1f) { dirs.Add(Toward(q, k, 1, j)); dirs.Add(Toward(q, k, -1, j)); break; }
            }
            float bestDot = -0.866f; float[] axis = null; // the most nearly opposite pair, within 30 deg of straight
            for (int a = 0; a < dirs.Count; a++)
                for (int b = a + 1; b < dirs.Count; b++)
                {
                    float d = dirs[a][0] * dirs[b][0] + dirs[a][1] * dirs[b][1];
                    if (d < bestDot) { bestDot = d; float ax = dirs[a][0] - dirs[b][0], az = dirs[a][1] - dirs[b][1], l = (float)Math.Sqrt(ax * ax + az * az); axis = new[] { ax / l, az / l }; }
                }
            if (axis == null) return null;
            float dx = at[0] - j[0], dz = at[1] - j[1], dl = (float)Math.Sqrt(dx * dx + dz * dz);
            if (dl < 1e-3f) return null;
            dx /= dl; dz /= dl;
            float[] best = null; float bestCos = 0.866f; // within 30 deg
            foreach (float[] cand in new[] { axis, new[] { -axis[0], -axis[1] }, new[] { -axis[1], axis[0] }, new[] { axis[1], -axis[0] } })
            {
                float cs = cand[0] * dx + cand[1] * dz;
                if (cs > bestCos) { bestCos = cs; best = cand; }
            }
            if (best == null || bestCos > 0.9998f) return null; // already square (within ~1 deg)
            return new[] { j[0] + best[0] * run, j[1] + best[1] * run };
        }

        /// <summary>Unit direction from j along road points q starting at index k, stepping 'step', about 20 m out.</summary>
        private static float[] Toward(List<float[]> q, int k, int step, float[] j)
        {
            float[] far = q[k];
            for (int i = k + step; i >= 0 && i < q.Count; i += step) { far = q[i]; if (D2(far, j) >= 400f) break; }
            float dx = far[0] - j[0], dz = far[1] - j[1], l = (float)Math.Sqrt(dx * dx + dz * dz);
            return l < 1e-3f ? new[] { 1f, 0f } : new[] { dx / l, dz / l };
        }

        private static bool StraightOk(Ctx c, float[] a, float[] b)
        {
            float len = (float)Math.Sqrt(D2(a, b));
            int steps = Math.Max(1, (int)(len / 4f));
            float prev = HeightAt(c, a[0], a[1]);
            for (int k = 1; k <= steps; k++)
            {
                float x = a[0] + (b[0] - a[0]) * k / steps, y = a[1] + (b[1] - a[1]) * k / steps;
                int i = Idx(c, x, y);
                if (i < 0 || !c.Land[i]) return false;
                float hh = HeightAt(c, x, y);
                if (Math.Abs(hh - prev) / (len / steps) > Math.Max(c.MaxGrade, 0.12f) * 1.2f) return false;
                prev = hh;
            }
            return true;
        }

        private static int Degree(RoadNet net, float[] j)
        {
            int d = 0;
            foreach (Road r in net.Roads)
            {
                if (D2(r.Points[0], j) < 1f) d++;
                if (D2(r.Points[r.Points.Count - 1], j) < 1f) d++;
                for (int k = 1; k + 1 < r.Points.Count; k++) if (D2(r.Points[k], j) < 1f) { d += 2; break; }
            }
            return d;
        }

        /// <summary>Moves one end of a road to 'to', dropping the points it would double back over.</summary>
        private static void MoveEnd(Road r, int e, float[] to)
        {
            List<float[]> p = r.Points;
            if (e == 1) p.Reverse();
            float reach = (float)Math.Sqrt(D2(p[0], to)) + 4f;
            while (p.Count > 2 && D2(p[1], to) < reach * reach && D2(p[1], to) < D2(p[0], to) + 16f) p.RemoveAt(1);
            p[0] = new[] { to[0], to[1] };
            if (e == 1) p.Reverse();
        }

        // ---------- connectivity ----------

        /// <summary>
        /// Roads grouped into connected pieces: two roads join where one's end lies on (or next to) the other's cells, so
        /// roads merely running side by side stay apart. Returns the piece per cell (-1 = no road) and each piece's size.
        /// </summary>
        private static int[] Label(Ctx c, out List<int> sizes)
        {
            int n = c.N, nr = c.Net.Roads.Count;
            Dictionary<int, List<int>> byCell = new Dictionary<int, List<int>>();
            for (int r = 0; r < nr; r++)
                foreach (int i in c.Net.Roads[r].Cells)
                {
                    List<int> l;
                    if (!byCell.TryGetValue(i, out l)) { l = new List<int>(); byCell[i] = l; }
                    if (l.Count == 0 || l[l.Count - 1] != r) l.Add(r);
                }
            int[] par = new int[nr];
            for (int r = 0; r < nr; r++) par[r] = r;
            int[] off = { 0, 1, -1, n, -n };
            for (int r = 0; r < nr; r++)
            {
                List<int> cells = c.Net.Roads[r].Cells;
                if (cells.Count == 0) continue;
                foreach (int e in new[] { cells[0], cells[cells.Count - 1] })
                    foreach (int o in off)
                    {
                        List<int> l;
                        if (e + o < 0 || e + o >= n * n || !byCell.TryGetValue(e + o, out l)) continue;
                        foreach (int q in l) { int a = Find(par, r), b = Find(par, q); if (a != b) par[a] = b; }
                    }
            }
            int[] lab = new int[n * n];
            for (int i = 0; i < lab.Length; i++) lab[i] = -1;
            Dictionary<int, int> ids = new Dictionary<int, int>();
            sizes = new List<int>();
            foreach (KeyValuePair<int, List<int>> kv in byCell)
            {
                int root = Find(par, kv.Value[0]), id;
                if (!ids.TryGetValue(root, out id)) { id = sizes.Count; ids[root] = id; sizes.Add(0); }
                lab[kv.Key] = id; sizes[id]++;
            }
            return lab;
        }

        private static int Find(int[] par, int a) { while (par[a] != a) { par[a] = par[par[a]]; a = par[a]; } return a; }

        /// <summary>Joins each piece not connected to the centre by the cheapest route to any other piece; drops pieces
        /// that can't be reached (water, cliffs).</summary>
        /// <summary>
        /// Side streets until the network is about 'targetKm' long: a buildable spot 40-250 m from the roads, routed (A*,
        /// the usual costs) to the nearest road, where it ends in a T; the spot itself is the street's dead end.
        /// </summary>
        private static void Infill(Ctx c, float targetKm)
        {
            int n = c.N;
            float km = 0;
            foreach (Road r in c.Net.Roads) for (int i = 1; i < r.Points.Count; i++) km += (float)Math.Sqrt(D2(r.Points[i], r.Points[i - 1])) / 1000f;
            // planning-grid roads come out ~3% longer than after smoothing
            targetKm *= 1.03f;
            int own = 500;
            // with the Like world's town shape: road length per tenth of its spread from the core, and the tenth that is
            // furthest short gets the next street (EA's towns are compact: ~40% of the road within 300 m of the core)
            TownShape sh = c.Shape;
            float[] have = sh != null ? RoadBands(c, sh) : null;
            int[] fails = new int[10];
            for (int tries = 0; tries < (c.S.Grid != null ? 1500 : 600) && km < targetKm; tries++)
            {
                int x, y, band = -1;
                if (sh != null)
                {
                    float most = 0;
                    for (int b = 0; b < 10; b++)
                    {
                        float shortBy = targetKm * 0.1f - have[b];
                        if (fails[b] < 40 && shortBy > most) { most = shortBy; band = b; }
                    }
                }
                if (band >= 0)
                {
                    float r0 = band > 0 ? sh.RoadDec[band - 1] : 0, r1 = sh.RoadDec[band];
                    double ang = c.Rng.NextDouble() * 2 * Math.PI, rr = Math.Sqrt(r0 * r0 + (r1 * r1 - r0 * r0) * c.Rng.NextDouble()) / Cell;
                    x = c.Centre % n + (int)Math.Round(Math.Cos(ang) * rr); y = c.Centre / n + (int)Math.Round(Math.Sin(ang) * rr);
                    fails[band]++; // cleared when a street is added
                    if (x < 4 || y < 4 || x >= n - 4 || y >= n - 4) continue;
                }
                else { x = 4 + c.Rng.Next(n - 8); y = 4 + c.Rng.Next(n - 8); }
                int i = y * n + x;
                int rim = (int)(90 / Cell); // not under the distant terrain
                if (x < rim || y < rim || x >= n - rim || y >= n - rim) continue;
                if (!c.Land[i] || c.OnRoad[i] != 0 || c.Build[i] < 0.45f || c.Crowd[i] > 0.05f) continue;
                // near the network, not out in the wilds
                int near = NearestRoad(c, x, y, (int)(250 / Cell));
                if (near < 0) continue;
                int before = c.Net.Roads.Count;
                // with the Like world's grid: a straight street off the nearest road, out through the spot (EA's side
                // streets are straight: houses along one road), else the usual route
                bool straight = false, joined = false;
                // only in town (within the distance holding 70% of the Like world's lots): country side streets wind
                float town = sh != null ? sh.LotDec[6] : 350;
                float tdx = x - c.Centre % n, tdy = y - c.Centre / n;
                if (c.S.Grid != null && (tdx * tdx + tdy * tdy) * Cell * Cell < town * town)
                {
                    float sx = (near % n) * Cell, sy = (near / n) * Cell, ex = x * Cell - sx, ey = y * Cell - sy, el = (float)Math.Sqrt(ex * ex + ey * ey);
                    if (el > 1 && !NearJunctionEnd(c, near))
                    {
                        straight = StraightStreet(c, sx, sy, ex / el, ey / el, el + 40 + (float)c.Rng.NextDouble() * 80, 48, own, -1, RoadKind.Lane, out joined);
                        if (straight) { List<float[]> sp = c.Net.Roads[c.Net.Roads.Count - 1].Points; i = Idx(c, sp[sp.Count - 1][0], sp[sp.Count - 1][1]); }
                    }
                }
                if (!straight)
                {
                    List<int> path = AStar(c, i, null, own, 0);
                    if (path == null || path.Count < 6 || path.Count * Cell > 320) continue;
                    if (NearJunctionEnd(c, path[path.Count - 1])) continue; // a T, not a crossing squeezed onto another junction
                    if (!AddPath(c, path, RoadKind.Lane, own, -1)) continue;
                }
                // most of EA's side streets join a road at both ends: link the far end to another road at least 80 m
                // from where it joined, if that's a short street (else it stays a dead end, capped by Build roads)
                if (!joined && c.Rng.NextDouble() >= c.S.DeadEndShare)
                {
                    List<int> back = AStar(c, i, null, own, 80f / Cell);
                    if (back != null && back.Count >= 3 && back.Count * Cell < 220 && !NearJunctionEnd(c, back[back.Count - 1]))
                        AddPath(c, back, RoadKind.Lane, own, -1);
                }
                for (int k = before; k < c.Net.Roads.Count; k++)
                {
                    List<float[]> p = c.Net.Roads[k].Points;
                    for (int j = 1; j < p.Count; j++) km += (float)Math.Sqrt(D2(p[j], p[j - 1])) / 1000f;
                }
                if (sh != null) { have = RoadBands(c, sh); if (band >= 0) fails[band] = 0; }
                own++;
            }
        }

        /// <summary>Stretches (8 m samples) of road lying within 16 m of another road, or within 26 m of the same road more than
        /// 80 m further along it (a road doubling back), and running the same way (within 30 deg either direction). The 30 m at each end of a road
        /// are left out: that's where it meets its junctions.</summary>
        public static int Alongside(RoadNet net)
        {
            // SelfNear: a road doubling back (Starlight Shores s1: 24 m apart). EndSkip: the ends meet other roads at junctions,
            // but a square approach isn't parallel in its last 20 m; at 30 m two lanes converging at ~25 deg into a T passed
            // (pt13: Monte Vista s1, Lucky Palms s3, built side by side)
            const float Step = 8, Near = 16, SelfNear = 26, EndSkip = 20, SelfGap = 80, Bin = 32;
            List<float[]>[] pts = new List<float[]>[net.Roads.Count];
            Dictionary<long, List<int[]>> hash = new Dictionary<long, List<int[]>>(); // bin -> (road, sample)
            for (int r = 0; r < net.Roads.Count; r++)
            {
                pts[r] = Resample(net.Roads[r].Points, Step);
                for (int i = 1; i < pts[r].Count; i++)
                {
                    long k = (long)Math.Floor(pts[r][i][0] / Bin) * 100003L + (long)Math.Floor(pts[r][i][1] / Bin);
                    List<int[]> l; if (!hash.TryGetValue(k, out l)) { l = new List<int[]>(); hash[k] = l; }
                    l.Add(new[] { r, i });
                }
            }
            int count = 0;
            for (int r = 0; r < pts.Length; r++)
            {
                List<float[]> p = pts[r]; int n = p.Count;
                for (int i = 1; i < n; i++)
                {
                    if (i * Step < EndSkip || (n - 1 - i) * Step < EndSkip) continue;
                    float tx = p[i][0] - p[i - 1][0], tz = p[i][1] - p[i - 1][1], tl = (float)Math.Sqrt(tx * tx + tz * tz);
                    if (tl < 1e-3f) continue;
                    bool hit = false;
                    long bx = (long)Math.Floor(p[i][0] / Bin), bz = (long)Math.Floor(p[i][1] / Bin);
                    for (long dx = -1; dx <= 1 && !hit; dx++)
                        for (long dz = -1; dz <= 1 && !hit; dz++)
                        {
                            List<int[]> l;
                            if (!hash.TryGetValue((bx + dx) * 100003L + bz + dz, out l)) continue;
                            foreach (int[] q in l)
                            {
                                if (q[0] == r && Math.Abs(q[1] - i) * Step < SelfGap) continue;
                                List<float[]> o = pts[q[0]]; float[] a = o[q[1] - 1], b = o[q[1]];
                                if (q[0] != r && (q[1] * Step < EndSkip || (o.Count - 1 - q[1]) * Step < EndSkip)) continue;
                                float ux = b[0] - a[0], uz = b[1] - a[1], ul = (float)Math.Sqrt(ux * ux + uz * uz);
                                if (ul < 1e-3f || Math.Abs((tx * ux + tz * uz) / (tl * ul)) < 0.866f) continue;
                                float t = Math.Max(0, Math.Min(1, ((p[i][0] - a[0]) * ux + (p[i][1] - a[1]) * uz) / (ul * ul)));
                                float ex = a[0] + ux * t - p[i][0], ez = a[1] + uz * t - p[i][1];
                                float lim = q[0] == r ? SelfNear : Near;
                                if (ex * ex + ez * ez < lim * lim) { hit = true; break; }
                            }
                        }
                    if (hit) count++;
                }
            }
            return count;
        }

        /// <summary>Road length (km) in each tenth of the town shape's road spread from the town centre; beyond the last tenth isn't counted.</summary>
        private static float[] RoadBands(Ctx c, TownShape sh)
        {
            float[] o = new float[10];
            float[] ctr = { (c.Centre % c.N) * Cell, (c.Centre / c.N) * Cell };
            foreach (Road r in c.Net.Roads)
                for (int i = 1; i < r.Points.Count; i++)
                {
                    float[] a = r.Points[i - 1], b = r.Points[i];
                    float[] m = { (a[0] + b[0]) / 2, (a[1] + b[1]) / 2 };
                    int band = TownShape.Band(sh.RoadDec, (float)Math.Sqrt(D2(m, ctr)));
                    if (band < 10) o[band] += (float)Math.Sqrt(D2(a, b)) / 1000f;
                }
            return o;
        }

        /// <summary>True if this road cell is within ~24 m of where roads already meet or end.</summary>
        private static bool NearJunctionEnd(Ctx c, int cell)
        {
            float[] q = { (cell % c.N) * Cell, (cell / c.N) * Cell };
            foreach (Road r in c.Net.Roads)
            {
                if (r.Points.Count < 2) continue;
                if (D2(r.Points[0], q) < 24 * 24 || D2(r.Points[r.Points.Count - 1], q) < 24 * 24) return true;
            }
            return false;
        }

        /// <summary>A road cell within 'r' cells of (x, y), or -1.</summary>
        private static int NearestRoad(Ctx c, int x, int y, int r)
        {
            int n = c.N;
            for (int d = 1; d <= r; d += 2)
                for (int k = -d; k <= d; k += 2)
                    foreach (int[] q in new[] { new[] { x + k, y - d }, new[] { x + k, y + d }, new[] { x - d, y + k }, new[] { x + d, y + k } })
                        if (q[0] >= 0 && q[1] >= 0 && q[0] < n && q[1] < n && c.OnRoad[q[1] * n + q[0]] != 0) return q[1] * n + q[0];
            return -1;
        }

        private static void ConnectAll(Ctx c)
        {
            int n = c.N;
            for (int round = 0; round < 40; round++)
            {
                List<int> sizes;
                int[] lab = Label(c, out sizes);
                if (sizes.Count <= 1) return;
                int main = 0;
                for (int k = 1; k < sizes.Count; k++) if (sizes[k] > sizes[main]) main = k;
                // the piece nearest the centre first
                int pick = -1, start = -1; float bd = float.MaxValue;
                for (int i = 0; i < lab.Length; i++)
                {
                    if (lab[i] < 0 || lab[i] == main) continue;
                    float d = Dist(i, c.Centre, n) - sizes[lab[i]] * 0.01f;
                    if (d < bd) { bd = d; start = i; pick = lab[i]; }
                }
                if (start < 0) return;
                List<int> path = AStar(c, start, null, c.Owner[start], 0, lab);
                if (path != null && AddPath(c, path, RoadKind.Street, c.Owner[start], -1, lab)) continue;
                // unreachable: drop the piece
                for (int k = c.Net.Roads.Count - 1; k >= 0; k--)
                {
                    List<float[]> pts = c.Net.Roads[k].Points;
                    int i0 = Idx(c, pts[0][0], pts[0][1]);
                    if (i0 >= 0 && lab[i0] == pick) c.Net.Roads.RemoveAt(k);
                }
                for (int i = 0; i < lab.Length; i++) if (lab[i] == pick) { c.OnRoad[i] = 0; c.Owner[i] = 0; }
                c.Net.Sites.RemoveAll(delegate (LotSite st) { int i = Idx(c, st.P[0], st.P[1]); return RoadDistance(c, i % n, i / n, 6) > 5; });
            }
        }

        // ---------- graph ----------

        /// <summary>
        /// Junctions: road ends where three or more ends meet, or where an end touches another road's middle (a T).
        /// Two ends meeting is just one road continuing.
        /// </summary>
        private static List<float[]> FindJunctions(RoadNet net) { return FindJunctions(net, 30f); }

        private static List<float[]> FindJunctions(RoadNet net, float dedup)
        {
            const float near = 12 * 12;
            List<float[]> j = new List<float[]>();
            for (int ri = 0; ri < net.Roads.Count; ri++)
                for (int e = 0; e < 2; e++)
                {
                    List<float[]> pr = net.Roads[ri].Points;
                    float[] p = e == 0 ? pr[0] : pr[pr.Count - 1];
                    int degree = 0;
                    foreach (Road r in net.Roads)
                    {
                        List<float[]> q = r.Points;
                        if (D2(q[0], p) < near) degree++;
                        if (D2(q[q.Count - 1], p) < near) degree++;
                        // an end on another road's middle (anywhere along a segment, not just at a stored point) is a T
                        if (D2(q[0], p) >= near && D2(q[q.Count - 1], p) >= near)
                            for (int k = 0; k + 1 < q.Count; k++)
                                if (SegD2(q[k], q[k + 1], p) < near) { degree += 2; break; }
                    }
                    if (degree < 3) continue;
                    bool dup = false;
                    foreach (float[] q in j) if (D2(q, p) < Math.Max(1f, dedup * dedup)) { dup = true; break; }
                    if (!dup) j.Add(p);
                }
            return j;
        }

        private static float SegD2(float[] a, float[] b, float[] p)
        {
            float dx = b[0] - a[0], dy = b[1] - a[1], l = dx * dx + dy * dy;
            float t = l < 1e-6f ? 0 : Math.Max(0, Math.Min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / l));
            return Sq(a[0] + dx * t - p[0]) + Sq(a[1] + dy * t - p[1]);
        }

        /// <summary>Points every 'step' metres along the polyline (ends kept).</summary>
        private static List<float[]> Resample(List<float[]> p, float step)
        {
            List<float[]> o = new List<float[]>();
            o.Add(p[0]);
            float carry = 0;
            for (int k = 1; k < p.Count; k++)
            {
                float[] a = p[k - 1], b = p[k];
                float len = (float)Math.Sqrt(D2(a, b));
                for (float d = step - carry; d < len; d += step) o.Add(new[] { a[0] + (b[0] - a[0]) * d / len, a[1] + (b[1] - a[1]) * d / len });
                carry = (carry + len) % step;
            }
            if (D2(o[o.Count - 1], p[p.Count - 1]) > 1) o.Add(p[p.Count - 1]); else o[o.Count - 1] = p[p.Count - 1];
            return o;
        }

        /// <summary>Straight runs + round bends instead of the relaxed grid route (#162); false = the old smoothing.</summary>
        public static bool TautRoads = true;
        /// <summary>How far (m) a straightened stretch may lie from the route the search found.</summary>
        public static float TautReach = 40f;
        /// <summary>Roads keep this far (m) from the map edge unless their route already ran nearer (edge roads).</summary>
        private const float EdgeKeep = 80f;

        /// <summary>Bend radius (m) by road kind: main roads sweep, lanes may turn tighter (EA: few bends under 25 m).</summary>
        public static float BendRadius(RoadKind k) { return k == RoadKind.Main ? 60f : k == RoadKind.Street ? 40f : 28f; }

        /// <summary>
        /// Pulls a routed road tight (#162): from each point, the farthest later point a straight line can reach is joined
        /// directly, as long as that line stays on land, no nearer the shore, the map edge or another road than the route
        /// was, no steeper than the grade limit (or the route's own steepest stretch there) and within TautReach of the
        /// route. Grid staircases and small detours become straight runs; bends the land needs (switchbacks, curves round
        /// hills) stay as corners for Fillet to round.
        /// </summary>
        private static List<float[]> Taut(Ctx c, Road r, List<float[]> p)
        {
            if (p.Count < 3) return p;
            Dictionary<int, bool> mine = new Dictionary<int, bool>();
            foreach (int ci in r.Cells) mine[ci] = true;
            int own = r.Cells.Count > 0 ? c.Owner[r.Cells[r.Cells.Count / 2]] : -1;
            List<float[]> o = new List<float[]>();
            o.Add(p[0]);
            int i = 0;
            while (i < p.Count - 1)
            {
                int best = i + 1, fails = 0;
                for (int j = i + 2; j < p.Count; j++)
                {
                    if (Straightable(c, p, i, j, mine, own)) { best = j; fails = 0; }
                    else if (++fails > 8) break;
                }
                o.Add(p[best]);
                i = best;
            }
            return o;
        }

        private static float EdgeDist(Ctx c, float x, float y)
        {
            float w = (c.N - 1) * Cell;
            return Math.Min(Math.Min(x, y), Math.Min(w - x, w - y));
        }

        /// <summary>Can points i..j of the route be replaced by the straight line p[i]-p[j]? (see Taut)</summary>
        private static bool Straightable(Ctx c, List<float[]> p, int i, int j, Dictionary<int, bool> mine, int own)
        {
            float[] a = p[i], b = p[j];
            float dx = b[0] - a[0], dy = b[1] - a[1], len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) return true;
            float ux = dx / len, uy = dy / len;
            // the route's own figures between i and j
            float dev = 0, shoreMin = float.MaxValue, edgeMin = float.MaxValue, crowdMax = 0;
            for (int k = i; k <= j; k++)
            {
                float ex = p[k][0] - a[0], ey = p[k][1] - a[1];
                dev = Math.Max(dev, Math.Abs(ex * uy - ey * ux));
                int ci = Idx(c, p[k][0], p[k][1]);
                if (ci >= 0)
                {
                    if (c.Shore != null) shoreMin = Math.Min(shoreMin, c.Shore[ci]);
                    if (c.Crowd != null && c.CrowdOwner[ci] != own) crowdMax = Math.Max(crowdMax, c.Crowd[ci]);
                }
                edgeMin = Math.Min(edgeMin, EdgeDist(c, p[k][0], p[k][1]));
            }
            if (dev > TautReach) return false;
            // the grade limit is a hard cap (ChatGPT, #162): a route already steeper than it isn't straightened into an
            // equally steep line; it keeps its corridor
            float gradeLim = c.MaxGrade + 0.005f;
            const float step = 4f;
            int n = (int)Math.Ceiling(len / step);
            float h2 = 0, h1 = 0;
            for (int q = 0; q <= n; q++)
            {
                float t = Math.Min(len, q * step), x = a[0] + ux * t, y = a[1] + uy * t;
                int ci = Idx(c, x, y);
                if (ci < 0 || !c.Land[ci]) return false;
                if (c.Shore != null && c.Shore[ci] < ShoreKeep && c.Shore[ci] < shoreMin - 1) return false;
                if (EdgeDist(c, x, y) < Math.Min(EdgeKeep, edgeMin) - 1) return false;
                bool nearEnd = t < 14 || len - t < 14;
                if (!nearEnd && c.OnRoad[ci] != 0 && !mine.ContainsKey(ci)) return false;
                if (!nearEnd && c.Crowd != null && c.CrowdOwner[ci] != own && c.Crowd[ci] > Math.Max(0.6f, crowdMax) + 0.05f) return false;
                float h = HeightAt(c, x, y);
                if (q >= 2 && Math.Abs(h - h2) / (2 * step) > gradeLim) return false; // grade over 8 m
                h2 = h1; h1 = h;
            }
            return true;
        }

        /// <summary>
        /// Rounds each corner with a circular arc of the given radius (smaller where the straight runs either side are too
        /// short to fit it), sampled every ~4 m, so a road bends smoothly instead of kinking (#162).
        /// </summary>
        private static List<float[]> Fillet(List<float[]> p, float radius)
        {
            if (p.Count < 3) return p;
            List<float[]> o = new List<float[]>();
            o.Add(p[0]);
            for (int k = 1; k < p.Count - 1; k++)
            {
                float[] a = p[k - 1], b = p[k], q = p[k + 1];
                float l1 = (float)Math.Sqrt(D2(a, b)), l2 = (float)Math.Sqrt(D2(b, q));
                if (l1 < 0.5f || l2 < 0.5f) { o.Add(b); continue; }
                float u1x = (b[0] - a[0]) / l1, u1y = (b[1] - a[1]) / l1, u2x = (q[0] - b[0]) / l2, u2y = (q[1] - b[1]) / l2;
                float theta = (float)Math.Acos(Math.Max(-1f, Math.Min(1f, u1x * u2x + u1y * u2y)));
                if (theta < 0.02f) { o.Add(b); continue; }
                float tanH = (float)Math.Tan(theta / 2);
                // each straight run is shared by the corners at both its ends (the first and last belong to one corner)
                float t = Math.Min(radius * tanH, Math.Min(k == 1 ? l1 * 0.9f : l1 * 0.5f, k == p.Count - 2 ? l2 * 0.9f : l2 * 0.5f));
                float rr = t / tanH;
                float side = u1x * u2y - u1y * u2x > 0 ? 1 : -1;
                float nx = -u1y * side, ny = u1x * side;
                float sx = b[0] - u1x * t, sy = b[1] - u1y * t, cx = sx + nx * rr, cy = sy + ny * rr;
                double a0 = Math.Atan2(sy - cy, sx - cx);
                int steps = Math.Max(2, (int)Math.Ceiling(theta * rr / 4f));
                for (int st = 0; st <= steps; st++)
                {
                    double ang = a0 + side * theta * st / steps;
                    o.Add(new[] { cx + rr * (float)Math.Cos(ang), cy + rr * (float)Math.Sin(ang) });
                }
            }
            o.Add(p[p.Count - 1]);
            return o;
        }

        /// <summary>
        /// Eases bends: each inner point moves halfway to its neighbours' midpoint, repeatedly, so a route flows in curves.
        /// Ends stay put; no point moves more than 'maxShift' metres from where it was routed, or onto water or a steeper grade.
        /// </summary>
        private static List<float[]> Relax(Ctx c, List<float[]> p, int iterations, float maxShift)
        {
            if (p.Count < 3) return p;
            float[][] orig = p.ToArray();
            List<float[]> cur = new List<float[]>();
            foreach (float[] q in p) cur.Add(new[] { q[0], q[1] });
            for (int it = 0; it < iterations; it++)
                for (int k = 1; k < cur.Count - 1; k++)
                {
                    float mx = (cur[k - 1][0] + cur[k + 1][0]) / 2, my = (cur[k - 1][1] + cur[k + 1][1]) / 2;
                    float nx = cur[k][0] + (mx - cur[k][0]) * 0.5f, ny = cur[k][1] + (my - cur[k][1]) * 0.5f;
                    float dx = nx - orig[k][0], dy = ny - orig[k][1], d = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (d > maxShift) { nx = orig[k][0] + dx / d * maxShift; ny = orig[k][1] + dy / d * maxShift; }
                    int i = Idx(c, nx, ny);
                    if (i < 0 || !c.Land[i] || CloserToShore(c, Idx(c, cur[k][0], cur[k][1]), i)) continue;
                    float before = Math.Max(Grade(c, cur[k - 1], cur[k]), Grade(c, cur[k], cur[k + 1]));
                    float after = Math.Max(Grade(c, cur[k - 1], new[] { nx, ny }), Grade(c, new[] { nx, ny }, cur[k + 1]));
                    if (after > Math.Max(before, c.MaxGrade)) continue;
                    cur[k][0] = nx; cur[k][1] = ny;
                }
            return cur;
        }

        /// <summary>
        /// Widens bends tighter than minR: points where the circle through the points two either side is smaller are
        /// moved towards their neighbours' midpoint (at most maxShift from where they started; ends fixed; never onto
        /// water or steeper than the grade limit).
        /// </summary>
        private static List<float[]> Ease(Ctx c, List<float[]> p, float minR, int iterations, float maxShift)
        {
            if (p.Count < 5) return p;
            float[][] orig = p.ToArray();
            List<float[]> cur = new List<float[]>();
            foreach (float[] q in p) cur.Add(new[] { q[0], q[1] });
            for (int it = 0; it < iterations; it++)
            {
                bool moved = false;
                for (int k = 2; k < cur.Count - 2; k++)
                {
                    if (Radius(cur[k - 2], cur[k], cur[k + 2]) >= minR) continue;
                    for (int j = k - 1; j <= k + 1; j++)
                    {
                        float mx = (cur[j - 1][0] + cur[j + 1][0]) / 2, my = (cur[j - 1][1] + cur[j + 1][1]) / 2;
                        float nx = cur[j][0] + (mx - cur[j][0]) * 0.6f, ny = cur[j][1] + (my - cur[j][1]) * 0.6f;
                        float dx = nx - orig[j][0], dy = ny - orig[j][1], d = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (d > maxShift) { nx = orig[j][0] + dx / d * maxShift; ny = orig[j][1] + dy / d * maxShift; }
                        int i = Idx(c, nx, ny);
                        if (i < 0 || !c.Land[i] || CloserToShore(c, Idx(c, cur[j][0], cur[j][1]), i)) continue;
                        float before = Math.Max(Grade(c, cur[j - 1], cur[j]), Grade(c, cur[j], cur[j + 1]));
                        float after = Math.Max(Grade(c, cur[j - 1], new[] { nx, ny }), Grade(c, new[] { nx, ny }, cur[j + 1]));
                        if (after > Math.Max(before, c.MaxGrade)) continue;
                        cur[j][0] = nx; cur[j][1] = ny; moved = true;
                    }
                }
                if (!moved) break;
            }
            return cur;
        }

        private static float Radius(float[] a, float[] b, float[] q)
        {
            float ab = (float)Math.Sqrt(D2(a, b)), bq = (float)Math.Sqrt(D2(b, q)), qa = (float)Math.Sqrt(D2(q, a));
            float area = Math.Abs((b[0] - a[0]) * (q[1] - a[1]) - (q[0] - a[0]) * (b[1] - a[1])) / 2;
            return area < 1e-4f ? float.MaxValue : ab * bq * qa / (4 * area);
        }

        private static float Grade(Ctx c, float[] a, float[] b)
        {
            float l = (float)Math.Sqrt(D2(a, b));
            return l < 0.5f ? 0 : Math.Abs(HeightAt(c, b[0], b[1]) - HeightAt(c, a[0], a[1])) / l;
        }

        private static List<float[]> Simplify(List<float[]> p, float tol, float maxLen)
        {
            if (p.Count < 3) return p;
            bool[] keep = new bool[p.Count];
            keep[0] = keep[p.Count - 1] = true;
            Dp(p, 0, p.Count - 1, tol, maxLen, keep);
            List<float[]> o = new List<float[]>();
            for (int i = 0; i < p.Count; i++) if (keep[i]) o.Add(p[i]);
            return o;
        }

        private static void Dp(List<float[]> p, int a, int b, float tol, float maxLen, bool[] keep)
        {
            if (b <= a + 1) return;
            float ax = p[a][0], ay = p[a][1], bx = p[b][0], by = p[b][1];
            float len = (float)Math.Sqrt(Sq(bx - ax) + Sq(by - ay));
            int idx = -1; float dmax = 0;
            for (int i = a + 1; i < b; i++)
            {
                float d = len < 1e-4f ? (float)Math.Sqrt(Sq(p[i][0] - ax) + Sq(p[i][1] - ay))
                                      : Math.Abs((bx - ax) * (ay - p[i][1]) - (ax - p[i][0]) * (by - ay)) / len;
                if (d > dmax) { dmax = d; idx = i; }
            }
            // keep a point at least every maxLen metres so CAW's splines follow gentle curves faithfully
            if (dmax > tol || len > maxLen) { if (idx < 0) idx = (a + b) / 2; keep[idx] = true; Dp(p, a, idx, tol, maxLen, keep); Dp(p, idx, b, tol, maxLen, keep); }
        }

        private static bool Inside(Ctx c, float x, float y, int margin = 1)
        {
            int ix = (int)(x / Cell + 0.5f), iy = (int)(y / Cell + 0.5f);
            return ix >= margin && iy >= margin && ix < c.N - margin && iy < c.N - margin;
        }
        private static int Idx(Ctx c, float x, float y)
        {
            int ix = (int)(x / Cell + 0.5f), iy = (int)(y / Cell + 0.5f);
            return ix < 0 || iy < 0 || ix >= c.N || iy >= c.N ? -1 : iy * c.N + ix;
        }
        private static float HeightAt(Ctx c, float x, float y) { return TerrainGen.Sample(c.H, x / Cell, y / Cell); }
        private static float Dist(int a, int b, int n) { return (float)Math.Sqrt(Sq(a % n - b % n) + Sq(a / n - b / n)); }
        private static float D2(float[] a, float[] b) { return Sq(a[0] - b[0]) + Sq(a[1] - b[1]); }
        private static float Sq(float v) { return v * v; }
        private static float Clamp(float v, float lo, float hi) { return v < lo ? lo : v > hi ? hi : v; }

        private sealed class BinaryHeap
        {
            private readonly List<int> items = new List<int>();
            private readonly List<float> keys = new List<float>();
            public int Count { get { return items.Count; } }
            public void Push(int item, float key)
            {
                items.Add(item); keys.Add(key);
                int i = items.Count - 1;
                while (i > 0) { int p = (i - 1) / 2; if (keys[p] <= keys[i]) break; Swap(i, p); i = p; }
            }
            public int Pop()
            {
                int top = items[0];
                int last = items.Count - 1;
                items[0] = items[last]; keys[0] = keys[last];
                items.RemoveAt(last); keys.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < items.Count && keys[l] < keys[m]) m = l;
                    if (r < items.Count && keys[r] < keys[m]) m = r;
                    if (m == i) break;
                    Swap(i, m); i = m;
                }
                return top;
            }
            private void Swap(int a, int b)
            {
                int t = items[a]; items[a] = items[b]; items[b] = t;
                float k = keys[a]; keys[a] = keys[b]; keys[b] = k;
            }
        }
    }
}
