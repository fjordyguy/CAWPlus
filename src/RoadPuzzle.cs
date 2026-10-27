using System;
using System.Collections.Generic;

namespace CawPlus
{
    /// <summary>
    /// Puzzle mode (#137, the tester: "click them together like a puzzle so no roads are being drawn from scratch"): the town is
    /// put together only from pieces of EA's own road graphs: a town-centre district at the centre, then districts
    /// (road_templates.txt) in town and junctions and connectors (road_pieces.txt) between and beyond them. Every piece is
    /// laid as EA drew it, turned and mirrored so one of its ports meets an open port of the network end to end, heading the
    /// opposite way. Only the joints are adjusted: a connector that runs into another road ends on it (a T), and two open
    /// ports that come within 30 m of each other facing each other are joined by a short straight piece (a loop).
    /// </summary>
    internal static partial class RoadPlanner
    {
        private const int PieceOwn = 1000;   // owner ids of puzzle pieces: 1000 + the piece's number
        private const float JointFree = 22f; // metres round a joint where a piece may touch the piece it joins
        private const float LoopGap = 30f;   // open ports this close and facing each other are joined

        private sealed class OpenPort
        {
            public float X, Y, Dx, Dy;
            public int Own;          // the piece it belongs to
            public char Kind;        // that piece's kind (D, J, C)
            public Road Arm;         // a junction's arm leading to this port (dropped if the port stays open)
            public bool Open = true;
        }

        private sealed class PuzzleState
        {
            public List<RoadTemplate> Districts = new List<RoadTemplate>(), Joins = new List<RoadTemplate>(), Links = new List<RoadTemplate>();
            public List<OpenPort> Ports = new List<OpenPort>();
            public int Next = PieceOwn;
            public float Km, TargetKm, TownR;
            public int DistrictsLeft, EdgeLeft;
            public string World = "", Region = "";
            public int Pieces, Tees, Loops, Edges, SideTries, Bridges;
        }

        /// <summary>A piece placed on the map and how it fits: its roads in map metres (each walked from the joint
        /// outwards where that matters) and how many points of each to lay.</summary>
        private sealed class PieceFit
        {
            public RoadTemplate T; public float Ang, Mirror, X, Y, Score;
            public int Port = -1;                    // the piece's port at the joint (-1: none, the seed)
            public List<List<float[]>> Roads = new List<List<float[]>>();
            public List<float[]> TeeEnd;             // per road: the point on another road it ends at (or null)
            public bool Edge;                        // a connector that runs off the map edge
            public float[] Bridge;                   // a connector that crosses water: bank to bank (x1, y1, x2, y2), then
            public float[] Landing;                  // straight on to here on the far bank, where its port opens
        }

        /// <summary>
        /// Puts the town together from EA pieces. Returns false (nothing laid) if no town centre piece fits, so the
        /// built-in planner is used instead.
        /// </summary>
        private static bool PuzzleTown(Ctx c, float worldSize, int lotsWanted, int hoods)
        {
            RoadSettings s = c.S;
            PuzzleState z = new PuzzleState();
            z.World = WorldPreset.Key(s.WorldType ?? ""); z.Region = s.Character != null ? s.Character.Region : "";
            List<RoadTemplate> cores = new List<RoadTemplate>();
            foreach (RoadTemplate t in FromWorlds(s, RoadTemplates.All()))
            {
                if (!Allowed(t, z.World)) continue;
                if (t.Core && t.Km >= 0.3f) cores.Add(t);
                else if (!t.Core && t.LotCount >= 4 && t.Km >= 0.12f && t.Ports.Count > 0) z.Districts.Add(t);
            }
            foreach (RoadTemplate t in FromWorlds(s, RoadTemplates.Pieces()))
                if (Allowed(t, z.World) && t.Ports.Count > 0) (t.Kind == 'J' ? z.Joins : z.Links).Add(t);
            if (z.Links.Count == 0) return false;

            float tk = s.TargetKm;
            if (tk <= 0 && s.Targets != null && !float.IsNaN(s.Targets.Km))
                tk = s.Targets.Km * (s.Targets.WorldM > 0 ? worldSize * worldSize / (s.Targets.WorldM * s.Targets.WorldM) : 1);
            if (tk <= 0) tk = 7f * Sq(worldSize / 2048f);
            z.TargetKm = tk;
            z.TownR = c.Shape != null ? Clamp(c.Shape.LotDec[8], 250, worldSize * 0.4f) : Math.Max(300f, worldSize * 0.22f);
            z.DistrictsLeft = hoods;
            z.EdgeLeft = s.EdgeRoads;

            // the seed: one of EA's town centres at the centre, else a junction piece
            float cx = (c.Centre % c.N) * Cell, cy = (c.Centre / c.N) * Cell;
            PieceFit seed = null;
            TemplateSpot at = BestSpot(c, cores, 8f, 4, cx, cy, new[] { 0f, 40f }, Town);
            if (at != null && at.Fit >= 0.7f)
            {
                seed = Fit(c, z, at.T, at.Angle, at.Mirror, at.X, at.Y, -1, null, z.Next);
                if (seed != null && seed.Score < 0.6f) seed = null;
            }
            if (seed == null)
                foreach (RoadTemplate t in Draw(c, z, z.Joins, 6))
                    for (int a = 0; a < 24; a++)
                        for (int m = -1; m <= 1; m += 2)
                        {
                            PieceFit f = Fit(c, z, t, (float)(a * Math.PI / 12), m, cx, cy, -1, null, z.Next);
                            if (f != null && (seed == null || f.Score > seed.Score)) seed = f;
                        }
            if (seed == null) return false;
            if (Lay(c, z, seed, null) <= 0) return false;
            if (seed.T.Kind == 'D') c.Net.TemplatesUsed.Add(seed.T.Name + " (centre)");
            c.Net.TownRadius = seed.T.Kind == 'D' ? Math.Max(60f, Math.Min(300f, seed.T.Reach)) : 60f;

            // grow from the open ports, nearest the centre first, until the town has its road length
            int guard = 0;
            while (z.Km < z.TargetKm && guard++ < 2000)
            {
                OpenPort o = null; float od = float.MaxValue;
                foreach (OpenPort p in z.Ports)
                {
                    if (!p.Open) continue;
                    float d = (float)Math.Sqrt(Sq(p.X - cx) + Sq(p.Y - cy)) + (float)c.Rng.NextDouble() * 80;
                    if (d < od) { od = d; o = p; }
                }
                if (o == null && (o = SidePort(c, z, cx, cy)) == null) break;
                o.Open = false;
                PieceFit f = NextPiece(c, z, o, od < z.TownR);
                if (f != null) { Lay(c, z, f, o); o.Arm = null; }
            }

            // a junction arm whose port nothing joined is a stub: dropped
            foreach (OpenPort p in z.Ports) if (p.Arm != null) DropRoad(c, p.Arm, p.Own);
            c.Net.LotsWanted = lotsWanted; c.Net.HoodsAsked = hoods;
            c.Net.PuzzleBridges = z.Bridges;
            c.Net.PuzzlePieces = z.Pieces; c.Net.PuzzleTees = z.Tees; c.Net.PuzzleLoops = z.Loops;
            return true;
        }

        /// <summary>
        /// When no open port is left and the town wants more road: a place to start a side road off the middle of a laid
        /// piece (a T, as EA's side streets leave its roads), square to it on either side, on a piece within 1.3 x the town
        /// spread. Null after 80 tries.
        /// </summary>
        private static OpenPort SidePort(Ctx c, PuzzleState z, float cx, float cy)
        {
            List<Road> hosts = new List<Road>();
            foreach (Road r in c.Net.Roads)
            {
                if (r.Template == null || r.Points.Count < 2 || RoadLen(r.Points) < 60) continue;
                float[] mid = r.Points[r.Points.Count / 2];
                if (Math.Sqrt(Sq(mid[0] - cx) + Sq(mid[1] - cy)) < z.TownR * 1.3f) hosts.Add(r);
            }
            while (hosts.Count > 0 && z.SideTries++ < 80)
            {
                Road r = hosts[c.Rng.Next(hosts.Count)];
                List<float[]> p = Resample(r.Points, 4f);
                int k = (int)(p.Count * (0.3 + 0.4 * c.Rng.NextDouble()));
                if (k < 1 || k >= p.Count - 1) continue;
                float dx = p[k + 1][0] - p[k - 1][0], dy = p[k + 1][1] - p[k - 1][1], l = (float)Math.Sqrt(dx * dx + dy * dy);
                if (l < 0.1f) continue;
                float side = c.Rng.NextDouble() < 0.5 ? 1 : -1;
                bool nearJunction = false;
                foreach (OpenPort q in z.Ports) if (D2(new[] { q.X, q.Y }, p[k]) < 40 * 40) { nearJunction = true; break; }
                if (nearJunction) continue;
                OpenPort o = new OpenPort();
                o.X = p[k][0]; o.Y = p[k][1]; o.Dx = -dy / l * side; o.Dy = dx / l * side; o.Own = NoOwner; o.Kind = 'T';
                z.Ports.Add(o);
                return o;
            }
            return null;
        }

        /// <summary>Island villages and the Egyptian and Chinese towns stay on their own world type (as BestSpot).</summary>
        private static bool Allowed(RoadTemplate t, string world)
        {
            return Array.IndexOf(OwnWorldOnly, t.World) < 0 || t.World == world;
        }

        /// <summary>Up to 'picks' pieces drawn from 'pool' without repeats (the world type's own x4, its region's x2).</summary>
        private static List<RoadTemplate> Draw(Ctx c, PuzzleState z, List<RoadTemplate> pool, int picks)
        {
            List<RoadTemplate> left = new List<RoadTemplate>(pool), got = new List<RoadTemplate>();
            for (int pick = 0; pick < picks && left.Count > 0; pick++)
            {
                float total = 0;
                foreach (RoadTemplate t in left) total += Weight(t, z.World, z.Region, 4f);
                double r = c.Rng.NextDouble() * total;
                int k = 0;
                for (; k < left.Count - 1; k++) { r -= Weight(left[k], z.World, z.Region, 4f); if (r <= 0) break; }
                got.Add(left[k]); left.RemoveAt(k);
            }
            return got;
        }

        /// <summary>The best piece for an open port: in town a district first (while the town wants more), after a
        /// connector a junction about half the time, else a connector (only a connector for a side road); null if nothing
        /// fits (the port stays a dead end).</summary>
        private static PieceFit NextPiece(Ctx c, PuzzleState z, OpenPort o, bool inTown)
        {
            List<List<RoadTemplate>> order = new List<List<RoadTemplate>>();
            if (inTown && z.DistrictsLeft > 0 && o.Kind != 'D' && c.Rng.NextDouble() < 0.6) order.Add(z.Districts);
            if (o.Kind == 'T') order.Add(z.Links); // a side road off a laid piece
            else if (o.Kind == 'C' && c.Rng.NextDouble() < 0.5) { order.Add(z.Joins); order.Add(z.Links); }
            else { order.Add(z.Links); order.Add(z.Joins); }
            foreach (List<RoadTemplate> pool in order)
            {
                PieceFit best = null;
                int picks = pool == z.Links ? 10 : 6;
                foreach (RoadTemplate t in Draw(c, z, pool, picks))
                {
                    float[] tweaks = t.Kind == 'C' ? new[] { 0f, 0.2f, -0.2f, 0.4f, -0.4f } : new[] { 0f, 0.14f, -0.14f };
                    for (int p = 0; p < t.Ports.Count; p++)
                        for (int m = -1; m <= 1; m += 2)
                            foreach (float tw in tweaks)
                            {
                                float[] q = t.Ports[p];
                                float ang = (float)(Math.Atan2(-o.Dy, -o.Dx) - Math.Atan2(q[3] * m, q[2])) + tw;
                                float[] at = Place(new[] { q[0], q[1] }, ang, m, 0, 0);
                                PieceFit f = Fit(c, z, t, ang, m, o.X - at[0], o.Y - at[1], p, new[] { o.X, o.Y }, z.Next);
                                if (f == null) continue;
                                f.Score -= Math.Abs(tw) * 0.5f; // turned at the joint: a slight kink
                                if (best == null || f.Score > best.Score) best = f;
                            }
                }
                if (best != null) return best;
            }
            // nothing fits: if the port faces water close by, a bridge across it (the bridge exception)
            float[] back = { o.X - o.Dx * 10, o.Y - o.Dy * 10 }, from = { o.X, o.Y };
            float[] cross = Crossing(c, back, from, NoOwner, 30);
            if (cross == null) return null;
            PieceFit b = new PieceFit();
            b.T = BridgePiece; b.Ang = 0; b.Mirror = 1; b.Port = -1; b.Score = 0.85f;
            b.TeeEnd = new List<float[]> { null };
            b.Roads.Add(new List<float[]> { from });
            b.Bridge = new[] { cross[0], cross[1], cross[2], cross[3] };
            b.Landing = new[] { cross[4], cross[5] };
            return b;
        }

        /// <summary>The piece a bridge from a port counts as (it has no EA geometry of its own).</summary>
        private static readonly RoadTemplate BridgePiece = new RoadTemplate { Kind = 'B', World = "bridge", Pattern = "bridge" };

        /// <summary>
        /// The bridge exception: from 'b', heading on from 'a', up to 'lead' m of land to the bank, then water no wider than
        /// MaxWater (the world type's longest bridge less 16 m), then at least 24 m of good land beyond (the landing),
        /// banks within 4 m of each other's height, no road in the way, no other bridge within the bridge length + 38 m.
        /// Returns bank A, bank B and the landing (6 numbers), or null.
        /// </summary>
        private static float[] Crossing(Ctx c, float[] a, float[] b, int own, float lead)
        {
            float hx = b[0] - a[0], hy = b[1] - a[1], hl = (float)Math.Sqrt(hx * hx + hy * hy);
            if (hl < 0.5f) return null;
            hx /= hl; hy /= hl;
            const float step = 2f;
            float t = 0;
            float[] bankA = b;
            // land up to the bank
            for (; ; t += step)
            {
                float[] p = { b[0] + hx * (t + step), b[1] + hy * (t + step) };
                if (!Inside(c, p[0], p[1], (int)(80 / Cell))) return null;
                if (!c.Land[Idx(c, p[0], p[1])]) break;
                if (t + step > lead) return null;
                bankA = p;
            }
            // the water
            float water = 0;
            float[] bankB = null;
            for (float u = t + step; ; u += step)
            {
                float[] p = { b[0] + hx * u, b[1] + hy * u };
                if (!Inside(c, p[0], p[1], (int)(80 / Cell))) return null;
                int i = Idx(c, p[0], p[1]);
                if (c.OnRoad[i] != 0 && c.Owner[i] != own) return null;
                if (c.Land[i]) { bankB = p; break; }
                water = u - t;
                if (water > c.MaxWater) return null;
            }
            if (water < 4) return null;
            if (Math.Abs(HeightAt(c, bankA[0], bankA[1]) - HeightAt(c, bankB[0], bankB[1])) > 4) return null;
            // the landing beyond
            float[] prev = bankB, land = null;
            for (float u = step; u <= 24; u += step)
            {
                float[] p = { bankB[0] + hx * u, bankB[1] + hy * u };
                if (PuzzlePoint(c, p, prev, own, null, false) != 0) return null;
                prev = p; land = p;
            }
            float mx = (bankA[0] + bankB[0]) / 2, my = (bankA[1] + bankB[1]) / 2, apart = c.BridgeSpan + 38;
            foreach (float[] q in c.BridgeMids) if (Sq(q[0] - mx) + Sq(q[1] - my) < apart * apart) return null;
            return new[] { bankA[0], bankA[1], bankB[0], bankB[1], land[0], land[1] };
        }

        /// <summary>
        /// How a piece fits here: null if it can't be laid. Connectors are walked from the joint: one that runs into
        /// another road ends on it (a T) if at least 40 m is laid and it meets it at 35 deg or more; one that runs off the
        /// map edge is an edge road while the town wants them. Junctions need the joined arm and two more. Districts need
        /// 80% of their road length on good ground.
        /// </summary>
        private static PieceFit Fit(Ctx c, PuzzleState z, RoadTemplate t, float ang, float m, float x, float y, int port, float[] joint, int own)
        {
            PieceFit f = new PieceFit();
            f.T = t; f.Ang = ang; f.Mirror = m; f.X = x; f.Y = y; f.Port = port;
            f.TeeEnd = new List<float[]>();
            if (t.Kind == 'C')
            {
                List<float[]> w = new List<float[]>();
                foreach (float[] q in t.Roads[0]) w.Add(Place(q, ang, m, x, y));
                // walk from the end at the joint
                if (port >= 0 && D2(Place(new[] { t.Ports[port][0], t.Ports[port][1] }, ang, m, x, y), w[w.Count - 1]) < D2(Place(new[] { t.Ports[port][0], t.Ports[port][1] }, ang, m, x, y), w[0]))
                    w.Reverse();
                float L = RoadLen(w), laid = 0;
                List<float[]> keep = new List<float[]>();
                float[] tee = null;
                bool edge = false, full = true;
                for (int k = 0; k < w.Count; k++)
                {
                    int code = PuzzlePoint(c, w[k], k > 0 ? w[k - 1] : null, own, joint, z.EdgeLeft > 0);
                    if (code == 0) { if (k > 0) laid += (float)Math.Sqrt(D2(w[k], w[k - 1])); keep.Add(w[k]); continue; }
                    full = false;
                    if (code == 3 && laid >= 40) { edge = true; break; }
                    if (code == 2 && laid >= 40 && keep.Count >= 2) tee = TeePoint(c, keep, own, 24f);
                    if (code == 4 && laid >= 30 && keep.Count >= 2)
                    {
                        // the bridge exception: EA's pieces stop at water, so a bridge carries the road straight on
                        float[] cross = Crossing(c, keep[keep.Count - 2], keep[keep.Count - 1], own, 0);
                        if (cross != null)
                        {
                            f.Bridge = new[] { cross[0], cross[1], cross[2], cross[3] };
                            f.Landing = new[] { cross[4], cross[5] };
                        }
                    }
                    break;
                }
                if (full) f.Score = PortAt(t, w[w.Count - 1], ang, m, x, y) ? 1f : 0.5f; // no port at its far end: one of EA's dead ends
                else if (tee != null) f.Score = 0.9f;
                else if (f.Bridge != null) f.Score = 0.85f;
                else if (edge) f.Score = 0.8f;
                else if (laid >= 80 && laid >= 0.5f * L) f.Score = 0.3f * laid / L;
                else return null;
                f.Roads.Add(keep); f.TeeEnd.Add(tee); f.Edge = edge;
                return f;
            }
            if (t.Kind == 'J')
            {
                int arms = 0;
                for (int r = 0; r < t.Roads.Count; r++)
                {
                    List<float[]> w = new List<float[]>();
                    foreach (float[] q in t.Roads[r]) w.Add(Place(q, ang, m, x, y));
                    bool ok = RoadLen(w) >= 3;
                    for (int k = 0; k < w.Count && ok; k++) ok = PuzzlePoint(c, w[k], k > 0 ? w[k - 1] : null, own, joint, false) == 0;
                    if (!ok && r == port) return null; // the arm at the joint must fit
                    f.Roads.Add(ok ? w : null); f.TeeEnd.Add(null);
                    if (ok) arms++;
                }
                if (arms < 3) return null;
                f.Score = 0.6f + 0.1f * arms;
                return f;
            }
            // a district: as TemplateFit, with the joint free
            float good = 0, all = 0, bad = 0, limit = t.Km * 1000 * 0.2f;
            foreach (List<float[]> road in t.Roads)
            {
                List<float[]> w = new List<float[]>();
                float[] prev = null, prevLocal = null;
                foreach (float[] q in road)
                {
                    float[] p = Place(q, ang, m, x, y);
                    float seg = prevLocal != null ? (float)Math.Sqrt(D2(q, prevLocal)) : 0;
                    all += seg;
                    if (PuzzlePoint(c, p, prev, own, joint, false) == 0) { good += seg; w.Add(p); }
                    else { bad += seg; if (bad > limit) return null; w.Add(null); }
                    prev = p; prevLocal = q;
                }
                f.Roads.Add(w); f.TeeEnd.Add(null);
            }
            if (all <= 0 || good / all < 0.8f) return null;
            f.Score = 0.5f + 0.5f * good / all;
            return f;
        }

        /// <summary>Whether the piece has a port at (about) this map point.</summary>
        private static bool PortAt(RoadTemplate t, float[] w, float ang, float m, float x, float y)
        {
            foreach (float[] q in t.Ports) if (D2(Place(new[] { q[0], q[1] }, ang, m, x, y), w) < 9) return true;
            return false;
        }

        /// <summary>
        /// A piece's road point: 0 = can be laid; 1 = can't (water, map rim, steeper than 1.25 x the grade limit from the
        /// last point, ground the plan keeps free); 2 = too near another piece's road; 3 = off the map (an edge road may go
        /// there); 4 = water (a bridge may cross it). Within JointFree m of the joint other roads don't count.
        /// </summary>
        private static int PuzzlePoint(Ctx c, float[] w, float[] prev, int own, float[] joint, bool edgeOk)
        {
            if (!Inside(c, w[0], w[1], 1)) return 3;
            if (!Inside(c, w[0], w[1], (int)(80 / Cell)) && !edgeOk) return 1;
            int i = Idx(c, w[0], w[1]);
            if (!c.Land[i]) return 4;
            if (!Buildable(c, i)) return 1;
            if (prev != null)
            {
                float d = (float)Math.Sqrt(D2(w, prev));
                if (d >= 1 && Math.Abs(HeightAt(c, w[0], w[1]) - HeightAt(c, prev[0], prev[1])) / d > c.MaxGrade * 1.25f) return 1;
            }
            if (joint != null && D2(w, joint) < JointFree * JointFree) return 0;
            if ((c.OnRoad[i] != 0 && c.Owner[i] != own) || (c.Crowd[i] > 0.6f && c.CrowdOwner[i] != own)) return 2;
            return 0;
        }

        /// <summary>Where a connector heading on from its last point meets another piece's road: the nearest point of that
        /// road within 'reach' m ahead, met at 35 deg or more; null if none.</summary>
        private static float[] TeePoint(Ctx c, List<float[]> keep, int own, float reach)
        {
            float[] a = keep[keep.Count - 2], b = keep[keep.Count - 1];
            float hx = b[0] - a[0], hy = b[1] - a[1], hl = (float)Math.Sqrt(hx * hx + hy * hy);
            if (hl < 0.1f) return null;
            hx /= hl; hy /= hl;
            float[] best = null; float bd = reach * reach;
            foreach (Road r in c.Net.Roads)
            {
                for (int k = 1; k < r.Points.Count; k++)
                {
                    float[] p = r.Points[k - 1], q = r.Points[k];
                    float sx = q[0] - p[0], sy = q[1] - p[1], sl = sx * sx + sy * sy;
                    float u = sl > 0 ? Clamp(((b[0] - p[0]) * sx + (b[1] - p[1]) * sy) / sl, 0, 1) : 0;
                    float[] e = { p[0] + sx * u, p[1] + sy * u };
                    float d = D2(e, b);
                    if (d >= bd) continue;
                    float ex = e[0] - b[0], ey = e[1] - b[1], el = (float)Math.Sqrt(d);
                    if (el > 1 && (ex * hx + ey * hy) / el < 0.5f) continue;                         // not ahead
                    float sn = (float)Math.Sqrt(sl);
                    if (sn > 0 && Math.Abs((hx * sy - hy * sx) / sn) < 0.57f) continue;              // nearly alongside (< 35 deg)
                    bd = d; best = e;
                }
            }
            return best;
        }

        /// <summary>Lays a fitted piece and opens its other ports; returns the metres laid.</summary>
        private static float Lay(Ctx c, PuzzleState z, PieceFit f, OpenPort joined)
        {
            int own = z.Next++, first = c.Net.Roads.Count;
            float laid = 0;
            RoadKind kind = f.T.Kind == 'D' ? RoadKind.Street : RoadKind.Main;
            List<Road> arm = new List<Road>();
            for (int r = 0; r < f.Roads.Count; r++)
            {
                List<float[]> w = f.Roads[r];
                arm.Add(null);
                if (w == null) continue;
                List<float[]> run = new List<float[]>();
                foreach (float[] p in w)
                {
                    if (p != null) { run.Add(p); continue; }
                    laid += LayPieceRun(c, run, kind, own); run = new List<float[]>();
                }
                if (f.TeeEnd[r] != null) run.Add(f.TeeEnd[r]);
                if (r == 0 && f.Bridge != null)
                {
                    // straight across: the bank, the far bank, the landing (the builder puts the bridge between the banks)
                    float[] a = { f.Bridge[0], f.Bridge[1] };
                    if (run.Count == 0 || D2(run[run.Count - 1], a) > 1) run.Add(a);
                    run.Add(new[] { f.Bridge[2], f.Bridge[3] }); run.Add(f.Landing);
                }
                int before = c.Net.Roads.Count;
                laid += LayPieceRun(c, run, kind, own);
                if (c.Net.Roads.Count > before) arm[r] = c.Net.Roads[c.Net.Roads.Count - 1];
                if (r == 0 && f.Bridge != null && c.Net.Roads.Count > before)
                {
                    c.Net.Roads[c.Net.Roads.Count - 1].Spans.Add(f.Bridge);
                    c.BridgeMids.Add(new[] { (f.Bridge[0] + f.Bridge[2]) / 2, (f.Bridge[1] + f.Bridge[3]) / 2 });
                    z.Bridges++;
                }
            }
            for (int k = first; k < c.Net.Roads.Count; k++) c.Net.Roads[k].Template = f.T.Name;
            if (laid <= 0) return 0;
            z.Km += laid / 1000f; z.Pieces++;
            if (f.TeeEnd.Count > 0 && f.TeeEnd[0] != null) z.Tees++;
            if (f.Edge) { z.EdgeLeft--; z.Edges++; }
            if (f.Bridge != null)
            {
                // the far bank's port: straight on from the landing
                float dx = f.Landing[0] - f.Bridge[2], dy = f.Landing[1] - f.Bridge[3], l = (float)Math.Sqrt(dx * dx + dy * dy);
                OpenPort o = new OpenPort();
                o.X = f.Landing[0]; o.Y = f.Landing[1]; o.Dx = dx / l; o.Dy = dy / l; o.Own = own; o.Kind = 'C';
                z.Ports.Add(o);
            }
            if (f.T.Kind == 'D' && joined != null) { z.DistrictsLeft--; c.Net.TemplatesUsed.Add(f.T.Name); }

            // its other ports, where the road reaching them was laid
            for (int p = 0; p < f.T.Ports.Count; p++)
            {
                if (p == f.Port) continue;
                float[] q = f.T.Ports[p];
                float[] pos = Place(new[] { q[0], q[1] }, f.Ang, f.Mirror, f.X, f.Y);
                float[] dir = Place(new[] { q[2], q[3] }, f.Ang, f.Mirror, 0, 0);
                bool reached = false;
                for (int k = first; k < c.Net.Roads.Count && !reached; k++)
                    foreach (float[] w in c.Net.Roads[k].Points) if (D2(w, pos) < 36) { reached = true; break; }
                if (!reached || f.Edge) continue;
                OpenPort o = new OpenPort();
                o.X = pos[0]; o.Y = pos[1]; o.Dx = dir[0]; o.Dy = dir[1]; o.Own = own; o.Kind = f.T.Kind;
                if (f.T.Kind == 'J' && p < arm.Count) o.Arm = arm[p];
                z.Ports.Add(o);
                CloseLoop(c, z, o);
            }
            return laid;
        }

        /// <summary>A new open port facing another within LoopGap m: the two are joined by a short straight piece.</summary>
        private static void CloseLoop(Ctx c, PuzzleState z, OpenPort o)
        {
            foreach (OpenPort p in z.Ports)
            {
                if (p == o || !p.Open || p.Own == o.Own) continue;
                float dx = p.X - o.X, dy = p.Y - o.Y, d = (float)Math.Sqrt(dx * dx + dy * dy);
                if (d > LoopGap || o.Dx * p.Dx + o.Dy * p.Dy > -0.5f) continue;
                if (d > 1 && (dx * o.Dx + dy * o.Dy) / d < 0.5f) continue;
                if (d >= 3) LayPieceRun(c, new List<float[]> { new[] { o.X, o.Y }, new[] { p.X, p.Y } }, RoadKind.Street, o.Own);
                o.Open = p.Open = false; o.Arm = p.Arm = null; z.Loops++;
                return;
            }
        }

        private static float LayPieceRun(Ctx c, List<float[]> run, RoadKind kind, int own)
        {
            if (run.Count < 2) return 0;
            float L = RoadLen(run);
            if (L < 3) return 0;
            AddRoad(c, run, kind, own, -1);
            c.Net.Roads[c.Net.Roads.Count - 1].Straight = true; // EA's geometry, kept as drawn
            return L;
        }

        /// <summary>Drops a junction's arm (its first cells, at the junction's middle, stay marked).</summary>
        private static void DropRoad(Ctx c, Road r, int own)
        {
            if (!c.Net.Roads.Remove(r)) return;
            for (int k = 2; k < r.Cells.Count; k++) if (c.Owner[r.Cells[k]] == own) c.OnRoad[r.Cells[k]] = 0;
        }
    }
}
