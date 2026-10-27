using System;
using System.Collections.Generic;

namespace CawPlus
{
    /// <summary>
    /// Turns a planned RoadNet into real CAW roads the way CAW's own road tools do: an intersection wherever three or
    /// more roads meet, each road as a chain of splines (ends k0/k3 = ports 0/1, controls k1/k2 along the tangents),
    /// each piece connected to the next and to the intersection port facing its direction. All one undo step.
    /// </summary>
    internal static class RoadBuilder
    {
        private const float NodeSnap = 14f;  // metres: road ends this close are the same junction
        private const float PieceLength = 50f;

        private sealed class End { public int Road; public bool AtStart; }
        private sealed class Node { public float[] P; public List<End> Ends = new List<End>(); public object Intersection; public bool[] Used; public bool Bridge; public Dictionary<int, int> Assigned; }
        private sealed class Span { public float[] Mid, Dir, EndA, EndB; public float Length = BridgeLength; public BridgeType Type; }

        internal const float BridgeLength = RoadPlanner.DefaultBridgeSpan; // EA's EP7 bridges: 72 m (wooden, flat), plank 65 m

        // known road bridge objects (instance, name), best first
        private static readonly ulong[] BridgeIds = { 0x98d89bUL, 0x98d899UL, 0x989e21UL };
        private static readonly string[] BridgeNames = { "BridgeFlatEP7", "BridgeWoodenEP7", "bridgePlank" };

        /// <param name="bridges">the world type's road bridges (Bridges.ForWorld); null = EA's EP7 bridges</param>
        public static string Build(object world, TerrainInfo ti, RoadNet net, ProgressFn progress, bool grade = false, List<BridgeType> bridges = null)
        {
            // 1. Planned metres -> world (x, z).
            List<List<float[]>> roads = new List<List<float[]>>();
            foreach (Road r in net.Roads)
            {
                List<float[]> w = new List<float[]>();
                foreach (float[] p in r.Points)
                {
                    float wx, wz;
                    TerrainSampler.Map(ti.Orientation, p[0] / ti.WorldSize, p[1] / ti.WorldSize, out wx, out wz);
                    w.Add(new[] { ti.Origin + wx * ti.WorldSize, ti.Origin + wz * ti.WorldSize });
                }
                if (w.Count >= 2) roads.Add(w);
            }
            List<float[]> junctions = new List<float[]>();
            foreach (float[] j in net.Junctions)
            {
                float wx, wz;
                TerrainSampler.Map(ti.Orientation, j[0] / ti.WorldSize, j[1] / ti.WorldSize, out wx, out wz);
                junctions.Add(new[] { ti.Origin + wx * ti.WorldSize, ti.Origin + wz * ti.WorldSize });
            }

            Log.Info(string.Format("Build roads: {0} planned roads, {1} junctions, world {2} m, origin {3}, orientation {4}",
                roads.Count, junctions.Count, ti.WorldSize, ti.Origin, ti.Orientation));
            // CAW's road tool calls World.TryEdit first: it checks the world out for editing, marks it modified and
            // flags the routing data for a rebuild.
            try { Log.Info("World.TryEdit: " + Caw.Call(world, "TryEdit")); }
            catch (Exception ex) { Log.Error("World.TryEdit failed", ex); }
            // CAW picks the road texture set (SetRoadTextureIdx) only when its Road panel opens; a new world whose Road
            // panel was never opened may have none selected, and roads built then were invisible (the tester, v0.10).
            try { EnsureRoadTextureSet(world); }
            catch (Exception ex) { Log.Error("Choosing the road texture set failed", ex); }

            // 1b. Bridges: cut each road where it crosses water; its two cut ends become the bridge's ends.
            List<Span> spans = new List<Span>();
            foreach (float[] b in net.Bridges)
            {
                float ax, az, bx, bz;
                TerrainSampler.Map(ti.Orientation, b[0] / ti.WorldSize, b[1] / ti.WorldSize, out ax, out az);
                TerrainSampler.Map(ti.Orientation, b[2] / ti.WorldSize, b[3] / ti.WorldSize, out bx, out bz);
                float[] A = { ti.Origin + ax * ti.WorldSize, ti.Origin + az * ti.WorldSize }, B = { ti.Origin + bx * ti.WorldSize, ti.Origin + bz * ti.WorldSize };
                Span sp = new Span(); sp.Mid = new[] { (A[0] + B[0]) / 2, (A[1] + B[1]) / 2 }; sp.Dir = Dir(A, B);
                // the world type's own bridge that fits this crossing (the shortest reaching 8 m onto both banks)
                if (bridges != null && bridges.Count > 0)
                {
                    sp.Type = Bridges.Pick(bridges, (float)Math.Sqrt(D2(A, B)));
                    if (sp.Type != null && sp.Type.Length > 0) sp.Length = sp.Type.Length;
                }
                sp.EndA = new[] { sp.Mid[0] - sp.Dir[0] * sp.Length / 2, sp.Mid[1] - sp.Dir[1] * sp.Length / 2 };
                sp.EndB = new[] { sp.Mid[0] + sp.Dir[0] * sp.Length / 2, sp.Mid[1] + sp.Dir[1] * sp.Length / 2 };
                if (CutForBridge(roads, sp)) spans.Add(sp);
                else Log.Info(string.Format("  bridge at ({0:F1}, {1:F1}): no road found to cut", sp.Mid[0], sp.Mid[1]));
            }

            // 2. Split roads that run through a junction (a T), so every junction is a road end. The junction can fall
            //    between two stored points, so split on the nearest segment.
            foreach (float[] j in junctions)
                for (int ri = 0; ri < roads.Count; ri++)
                {
                    List<float[]> r = roads[ri];
                    if (D2(r[0], j) < NodeSnap * NodeSnap || D2(r[r.Count - 1], j) < NodeSnap * NodeSnap) continue;
                    int seg = -1; float bd = NodeSnap * NodeSnap; float[] at = null;
                    for (int k = 0; k + 1 < r.Count; k++)
                    {
                        float[] q = Project(r[k], r[k + 1], j);
                        float d = D2(q, j);
                        if (d < bd) { bd = d; seg = k; at = q; }
                    }
                    if (seg < 0) continue;
                    List<float[]> pts = new List<float[]>(r);
                    int split;
                    if (D2(pts[seg], at) < 9) split = seg;
                    else if (D2(pts[seg + 1], at) < 9) split = seg + 1;
                    else { pts.Insert(seg + 1, at); split = seg + 1; }
                    if (split == 0 || split == pts.Count - 1) continue;
                    roads[ri] = pts.GetRange(0, split + 1); roads.Add(pts.GetRange(split, pts.Count - split));
                }

            // 2b. Dead-end spurs under 30 m (left by smoothing next to a junction) would be a stub out of the junction and
            // straight back (a piece on top of a piece); dropped. So are spurs under 50 m leaving their junction within 40
            // deg of another road there: the two would be built side by side (pt13: a lane running 32 m past the T where
            // a straight street leaves it, Twinbrook seed 1).
            int spurs = 0;
            List<Node> ends0 = BuildNodes(roads);
            for (int ri = roads.Count - 1; ri >= 0; ri--)
            {
                List<float[]> r = roads[ri];
                float len = Length(r);
                if (len >= 50f) continue;
                Node a = NodeAt(ends0, r[0]), b = NodeAt(ends0, r[r.Count - 1]);
                bool looseA = a != null && a.Ends.Count == 1 && !AtBridgeEnd(spans, r[0]);
                bool looseB = b != null && b.Ends.Count == 1 && !AtBridgeEnd(spans, r[r.Count - 1]);
                if (looseA == looseB || AtBridgeEnd(spans, r[0]) || AtBridgeEnd(spans, r[r.Count - 1])) continue;
                bool drop = len < 30f;
                if (!drop)
                {
                    Node j = looseA ? b : a; bool fromStart = !looseA; // the spur leaves junction j from this end
                    if (j != null)
                    {
                        float[] d = Leaving(r, fromStart);
                        foreach (End e in j.Ends)
                        {
                            if (e.Road == ri) continue;
                            float[] o = Leaving(roads[e.Road], e.AtStart);
                            if (d[0] * o[0] + d[1] * o[1] > 0.766f) { drop = true; break; } // within 40 deg
                        }
                    }
                }
                if (drop) { roads.RemoveAt(ri); spurs++; ends0 = BuildNodes(roads); }
            }
            if (spurs > 0) Log.Info("Build roads: " + spurs + " dead-end spur(s) dropped (under 30 m, or under 50 m beside another road out of the same junction)");
            // 2c. Short links (under 30 m) lying along other roads all the way (a little loop off a road and back onto it,
            // left by smoothing) would be laid on top of that road; dropped (pt13: Sunset Valley seed 2).
            int along = 0;
            for (int ri = roads.Count - 1; ri >= 0; ri--)
            {
                List<float[]> r = roads[ri];
                if (Length(r) >= 30f || AtBridgeEnd(spans, r[0]) || AtBridgeEnd(spans, r[r.Count - 1])) continue;
                bool all = true;
                foreach (float[] q in r)
                {
                    float best = float.MaxValue;
                    for (int k = 0; k < roads.Count && best > 64f; k++)
                    {
                        if (k == ri) continue;
                        List<float[]> o = roads[k];
                        for (int j = 0; j + 1 < o.Count; j++) best = Math.Min(best, SegD2(q, o[j], o[j + 1]));
                    }
                    if (best > 64f) { all = false; break; } // more than 8 m from every other road
                }
                // only if its two ends stay joined another way (a loop off a road and back); a link between two
                // junctions dropped this way split a junction and made a U-turn (pt13, Champs Les Sims seed 1)
                if (all && Redundant(roads, ri)) { roads.RemoveAt(ri); along++; }
            }
            if (along > 0) Log.Info("Build roads: " + along + " short link(s) along other roads dropped");

            // 3. Junction nodes from road ends; merge pass-through (degree 2) nodes into one road.
            List<Node> nodes = BuildNodes(roads);
            // a short road with both ends in one junction is a scrap inside it: dropped
            int scraps = 0;
            foreach (Node n in nodes)
                for (int a = 0; a < n.Ends.Count; a++)
                    for (int b = a + 1; b < n.Ends.Count; b++)
                    {
                        List<float[]> r = roads[n.Ends[a].Road];
                        // (a short road at a bridge end is the bridge's link to its junction: kept)
                        if (r != null && n.Ends[a].Road == n.Ends[b].Road && Length(r) < 3 * NodeSnap
                            && !AtBridgeEnd(spans, r[0]) && !AtBridgeEnd(spans, r[r.Count - 1])) roads[n.Ends[a].Road] = null;
                    }
            for (int ri = roads.Count - 1; ri >= 0; ri--) if (roads[ri] == null) { roads.RemoveAt(ri); scraps++; }
            if (scraps > 0) { nodes = BuildNodes(roads); Log.Info("Build roads: " + scraps + " scrap road(s) inside junctions dropped"); }
            bool merged = true;
            while (merged)
            {
                merged = false;
                foreach (Node n in nodes)
                {
                    if (n.Ends.Count != 2 || n.Ends[0].Road == n.Ends[1].Road) continue;
                    if (AtBridgeEnd(spans, n.P)) continue; // a bridge end stays a node, so the bridge joins there
                    List<float[]> a = Oriented(roads[n.Ends[0].Road], !n.Ends[0].AtStart); // ends at the node
                    List<float[]> b = Oriented(roads[n.Ends[1].Road], n.Ends[1].AtStart);  // starts at the node
                    List<float[]> joined = new List<float[]>(a); joined.AddRange(b.GetRange(1, b.Count - 1));
                    int keep = Math.Min(n.Ends[0].Road, n.Ends[1].Road), drop = Math.Max(n.Ends[0].Road, n.Ends[1].Road);
                    roads[keep] = joined; roads.RemoveAt(drop);
                    merged = true;
                    break;
                }
                if (merged) nodes = BuildNodes(roads);
            }

            // 3b. Two planned roads overlapping between the same two junctions would be built twice, one on top of the
            // other (the tester's v0.40 test): keep the first.
            int dupes = 0;
            for (int i = roads.Count - 1; i >= 0; i--)
                for (int k = 0; k < i; k++)
                    if (SameRoad(roads[i], roads[k])) { roads.RemoveAt(i); dupes++; break; }
            if (dupes > 0) { nodes = BuildNodes(roads); Log.Info("Build roads: " + dupes + " duplicate road(s) dropped"); }

            // 3b2. A planned road that goes out and straight back (a hairpin, from smoothing near a junction) would be
            // built as two pieces on top of each other: its turning point is dropped.
            int spikes = 0;
            foreach (List<float[]> r in roads)
                for (int k = r.Count - 2; k >= 1; k--)
                    if (k + 1 < r.Count && D2(r[k - 1], r[k]) > 1e-4f && D2(r[k], r[k + 1]) > 1e-4f
                        && Angle(Dir(r[k - 1], r[k]), Dir(r[k], r[k + 1])) > 135f) { r.RemoveAt(k); spikes++; }
            if (spikes > 0) { nodes = BuildNodes(roads); Log.Info("Build roads: " + spikes + " hairpin point(s) removed"); }

            // 3c. An intersection piece has 4 ports: a 5th road at a junction was left with a stray stub. It now meets the
            // road nearest its direction 30 m out instead, as a T-junction of its own.
            int moved = 0;
            for (int guard = 0; guard < 50; guard++)
            {
                Node over = null;
                float limit = 0;
                foreach (Node n in nodes)
                {
                    if (n.Ends.Count < 3 || AtBridgeEnd(spans, n.P)) continue;
                    // more roads than ports, or two arriving so close together that the second gets a port facing away
                    // and wraps round the junction (a piece over a piece: Barnacle Bay seed 2)
                    // (55°: with ports every 90°, one of two roads closer than that meets its port 45°+ off and curls)
                    float lim = n.Ends.Count > 4 ? (float)Math.PI : CrowdDeg * (float)Math.PI / 180f;
                    if (n.Ends.Count > 4 || ClosestPair(roads, n) < lim) { over = n; limit = lim; break; }
                }
                if (over == null || !MoveFifthRoad(roads, over, limit)) break;
                nodes = BuildNodes(roads); moved++;
            }
            if (moved > 0) Log.Info("Build roads: " + moved + " road(s) moved off crowded junctions");

            List<object> made = new List<object>(); // every road piece built, for the tidy-up pass
            List<float[]> builtEnds = new List<float[]>();   // ends of every road piece built (x0, z0, x1, z1), to never build one twice
            List<object> bridgeSplines = new List<object>(); // bridge pieces: never moved or capped, only joined to

            // 4. Intersections where 3+ roads meet: moved onto their roads' lines (FitJunctionPoint), turned to suit all of them (FitRotation).
            bool group = false;
            int inters = 0, splines = 0, failed = 0;
            PortMisfit.Clear();
            int shifted = 0; float shiftSum = 0;
            foreach (Node n in nodes)
            {
                if (n.Ends.Count < 3 || AtBridgeEnd(spans, n.P)) continue;
                float sh = FitJunctionPoint(roads, n);
                if (sh > 0.5f) { shifted++; shiftSum += sh; }
            }
            if (shifted > 0) Log.Info(string.Format("Build roads: {0} junction(s) moved onto their roads' lines (mean {1:F1} m)", shifted, shiftSum / shifted));
            foreach (Node n in nodes)
            {
                if (n.Ends.Count < 3) continue;
                float rot = FitRotation(roads, n);
                object it = null;
                try { it = Caw.CreateIntersection(world, n.P[0], n.P[1], rot, group); }
                catch (Exception ex) { Log.Error("Creating intersection failed", ex); }
                if (it == null) { failed++; if (failed < 5) Log.Info(string.Format("  intersection at ({0:F1}, {1:F1}) not created", n.P[0], n.P[1])); continue; }
                group = true; inters++;
                n.Intersection = it; n.Used = new bool[Caw.PortCount(it)];
                n.Assigned = AssignPorts(roads, n);
                if (inters <= 3)
                {
                    string ports = "";
                    for (uint p = 0; p < n.Used.Length; p++) { float[] pp = Caw.PortPosition(it, p), pn = Caw.PortNormal(it, p); ports += string.Format(" [{0}: ({1:F1}, {2:F1}) n ({3:F2}, {4:F2})]", p, pp[0], pp[1], pn[0], pn[1]); }
                    Log.Info(string.Format("  intersection {0} at ({1:F1}, {2:F1}), rot {3:F2} rad, {4} roads, ports{5}", inters, n.P[0], n.P[1], rot, n.Ends.Count, ports));
                }
            }

            Log.Info("Build roads: " + PortMisfitSummary());

            // 4b. Bridges: the bridge object at the middle of each crossing, turned along the road (CAW's yaw = −angle − 90°,
            //     measured on Vermont's three bridges), its deck level with the banks; the road ends there use its ports.
            int bridgesBuilt = 0;
            Caw.CatalogItem bridgeItem = spans.Count > 0 ? FindBridge() : null;
            Dictionary<ulong, Caw.CatalogItem> bridgeItems = new Dictionary<ulong, Caw.CatalogItem>();
            if (bridgeItem != null)
            {
                try { if (!Caw.UseLayer(world, "Bridges & Tunnels")) Caw.EnsureLayer(world, "Bridges & Tunnels"); } // objects need a World Layer
                catch (Exception ex) { Log.Error("Choosing a World Layer for bridges failed", ex); }
            }
            foreach (Span sp in spans)
            {
                if (bridgeItem == null) break;
                Caw.CatalogItem item = bridgeItem;
                if (sp.Type != null)
                {
                    if (!bridgeItems.ContainsKey(sp.Type.Instance)) bridgeItems[sp.Type.Instance] = FindBridge(sp.Type);
                    if (bridgeItems[sp.Type.Instance] != null) item = bridgeItems[sp.Type.Instance];
                }
                float deck = Math.Max(ti.WaterLevel + 1.5f, (Caw.TerrainHeight(world, sp.EndA[0], sp.EndA[1]) + Caw.TerrainHeight(world, sp.EndB[0], sp.EndB[1])) / 2);
                float yaw = -(float)Math.Atan2(sp.Dir[1], sp.Dir[0]) - (float)Math.PI / 2;
                object spline = null;
                try { spline = Caw.PlaceBridge(item, sp.Mid[0], sp.Mid[1], deck, yaw, group); }
                catch (Exception ex) { Log.Error("Placing a bridge failed", ex); }
                if (spline == null && item != bridgeItem)
                {
                    // the world type's own bridge isn't installed (its pack is missing): EA's EP7 bridge instead
                    Log.Info("  " + item.Name + " couldn't be placed; using " + bridgeItem.Name);
                    bridgeItems[sp.Type.Instance] = null;
                    try { spline = Caw.PlaceBridge(bridgeItem, sp.Mid[0], sp.Mid[1], deck, yaw, true); }
                    catch (Exception ex) { Log.Error("Placing a bridge failed", ex); }
                }
                if (spline == null) { failed++; group = true; continue; } // commands may have been submitted: keep one undo step
                group = true; bridgesBuilt++;
                made.Add(spline); bridgeSplines.Add(spline);
                float[] p0 = Caw.PortPosition(spline, 0), p1 = Caw.PortPosition(spline, 1);
                float[] axis = Dir(p0, p1);
                double off = Math.Acos(Math.Min(1, Math.Abs(axis[0] * sp.Dir[0] + axis[1] * sp.Dir[1]))) * 180 / Math.PI;
                float g0 = Caw.TerrainHeight(world, p0[0], p0[1]), g1 = Caw.TerrainHeight(world, p1[0], p1[1]);
                Log.Info(string.Format("  bridge {0} at ({1:F1}, {2:F1}), deck {3:F1} m, yaw {4:F1}°, ends ({5:F1}, {6:F1}) ({7:F1}, {8:F1}), span {9:F1} m, {10:F0}° off the road{11}; ground at the ports {12:F1} / {13:F1} m{14}",
                    bridgesBuilt, sp.Mid[0], sp.Mid[1], deck, yaw * 180 / Math.PI, p0[0], p0[1], p1[0], p1[1], Math.Sqrt(D2(p0, p1)), off,
                    off > 10 ? " (CHECK: yaw convention differs)" : "", g0, g1,
                    Math.Max(Math.Abs(g0 - deck), Math.Abs(g1 - deck)) > 0.75f ? " (CHECK: land and deck don't meet; tick Smooth ground under roads)" : ""));
                foreach (float[] end in new[] { sp.EndA, sp.EndB })
                {
                    Node nd = NodeAt(nodes, end);
                    if (nd == null)
                    {
                        // the road beyond is short and ends at a junction near the bank (cut away with the bridge's
                        // length): join the bridge to the nearest junction or road end within 45 m instead
                        nd = NearestNode(nodes, end, 45f);
                        if (nd != null && nd.Intersection == null && nd.Ends.Count == 1)
                        {
                            // a lone road end: move it onto the bridge end, so the road runs to the bridge
                            End e1 = nd.Ends[0];
                            List<float[]> rr = roads[e1.Road];
                            if (e1.AtStart) rr[0] = end; else rr[rr.Count - 1] = end;
                            nd.P = end;
                        }
                        if (nd == null) { Log.Info(string.Format("    no road ends at bridge end ({0:F1}, {1:F1})", end[0], end[1])); continue; }
                        Log.Info(string.Format("    bridge end ({0:F1}, {1:F1}) joined to the road {2:F0} m away", end[0], end[1], Math.Sqrt(D2(nd.P, end))));
                    }
                    if (nd.Intersection == null && nd.Ends.Count >= 2)
                    {
                        // two roads at the bridge end (a junction on the bank): a bridge port takes only one, so the
                        // junction gets an intersection piece just past the bridge end, linked to it
                        float[] outw = Dir(sp.Mid, end);
                        float[] at = { end[0] + outw[0] * StubLength, end[1] + outw[1] * StubLength };
                        object it = null;
                        try { it = Caw.CreateIntersection(world, at[0], at[1], (float)Math.Atan2(outw[1], outw[0]), group); }
                        catch (Exception ex) { Log.Error("Creating an intersection at a bridge end failed", ex); }
                        if (it != null)
                        {
                            inters++;
                            foreach (End e in nd.Ends)
                            {
                                List<float[]> rr = roads[e.Road];
                                if (e.AtStart) rr[0] = at; else rr[rr.Count - 1] = at;
                            }
                            nd.P = at; nd.Intersection = it; nd.Used = new bool[Caw.PortCount(it)];
                            Log.Info(string.Format("    bridge end ({0:F1}, {1:F1}): {2} roads, intersection placed {3:F0} m past it", end[0], end[1], nd.Ends.Count, StubLength));
                        }
                    }
                    if (nd.Intersection != null)
                    {
                        // a road joins right at the bridge end: link the intersection to the bridge with a short piece
                        uint ip, bp = D2(Caw.PortPosition(spline, 0), end) < D2(Caw.PortPosition(spline, 1), end) ? 0u : 1u;
                        float[] bpos = Caw.PortPosition(spline, bp), bout = Outward(spline, bp);
                        if (!PickPort(nd, Neg(bout), out ip)) continue;
                        float[] ipos = Caw.PortPosition(nd.Intersection, ip), iout = Outward(nd.Intersection, ip);
                        float L = (float)Math.Sqrt(D2(ipos, bpos)) / 3f;
                        object link = null;
                        try { link = Caw.CreateSpline(world, ipos, new[] { ipos[0] + iout[0] * L, ipos[1] + iout[1] * L }, new[] { bpos[0] + bout[0] * L, bpos[1] + bout[1] * L }, bpos, group); }
                        catch (Exception ex) { Log.Error("Linking a bridge to an intersection failed", ex); }
                        if (link == null) { failed++; continue; }
                        splines++;
                        Caw.ConnectPorts(nd.Intersection, ip, link, 0); Caw.ConnectPorts(link, 1, spline, bp);
                        Log.Info(string.Format("    bridge end ({0:F1}, {1:F1}) linked to the intersection there", end[0], end[1]));
                        continue;
                    }
                    nd.Intersection = spline; nd.Used = new bool[2]; nd.Bridge = true;
                }
            }

            // 5. Each road as a chain of splines, attached to intersection ports at its ends.
            for (int ri = 0; ri < roads.Count; ri++)
            {
                if (progress != null && ri % 5 == 0) progress("Building roads", 100 * ri / roads.Count);
                List<float[]> r = roads[ri];
                // a road end moved onto a bridge end (4b) can make a second copy of a road already built
                bool copy = false;
                for (int k = 0; k < ri && !copy; k++) copy = SameRoad(r, roads[k]);
                if (copy) { Log.Info(string.Format("  road ({0:F1}, {1:F1}) -> ({2:F1}, {3:F1}) is a copy of another, skipped", r[0][0], r[0][1], r[r.Count - 1][0], r[r.Count - 1][1])); continue; }
                float[] startPos = r[0], endPos = r[r.Count - 1];
                // the road's direction measured 20 m along it (the first couple of planned points are only metres
                // apart, so their direction is noisy and could pick a port facing well off the road)
                float[] startTan = DirAlong(r, true, 20f), endTan = DirAlong(r, false, 20f);
                object startObj = null, endObj = null; uint startPort = 0, endPort = 0;
                Node ns = NodeAt(nodes, r[0]), ne = NodeAt(nodes, r[r.Count - 1]);
                if (ns != null && ns.Intersection != null && PickPortFor(ns, ri, true, startTan, out startPort))
                {
                    startObj = ns.Intersection; startPos = Caw.PortPosition(startObj, startPort); startTan = Outward(startObj, startPort);
                }
                if (ne != null && ne.Intersection != null && PickPortFor(ne, ri, false, Neg(endTan), out endPort))
                {
                    endObj = ne.Intersection; endPos = Caw.PortPosition(endObj, endPort); endTan = Neg(Outward(endObj, endPort));
                }
                List<float[]> pts = new List<float[]>(r);
                pts[0] = startPos; pts[pts.Count - 1] = endPos;
                if (Length(pts) < 12)
                {
                    // A short piece between two ports (typically a bridge end and a junction just past the bank) must
                    // still be built, or the bridge is left with a loose end: one piece, port to port, leaving each
                    // port along its own direction.
                    if (startObj == null || endObj == null || D2(startPos, endPos) < 1) continue;
                    // two planned roads between the same pair of ports (nodes a few metres apart) would build the same
                    // short piece twice, one on top of the other (the tester's v0.40 test: a doubled road at a crossroads)
                    if (Built(builtEnds, startPos, endPos)) { Log.Info(string.Format("  short link ({0:F1}, {1:F1}) -> ({2:F1}, {3:F1}) already built, skipped", startPos[0], startPos[1], endPos[0], endPos[1])); continue; }
                    object link = null;
                    float Ls = (float)Math.Sqrt(D2(startPos, endPos)) / 3f;
                    float[] c1 = { startPos[0] + startTan[0] * Ls, startPos[1] + startTan[1] * Ls }, c2 = { endPos[0] - endTan[0] * Ls, endPos[1] - endTan[1] * Ls };
                    try { link = Caw.CreateSpline(world, startPos, c1, c2, endPos, group); }
                    catch (Exception ex) { Log.Error("Creating a short link piece failed", ex); }
                    if (link == null) { failed++; Log.Info(string.Format("  short link ({0:F1}, {1:F1}) -> ({2:F1}, {3:F1}) not created", startPos[0], startPos[1], endPos[0], endPos[1])); continue; }
                    group = true; splines++; made.Add(link); builtEnds.Add(new[] { startPos[0], startPos[1], endPos[0], endPos[1] });
                    Caw.ConnectPorts(startObj, startPort, link, 0); Caw.ConnectPorts(link, 1, endObj, endPort);
                    continue;
                }
                // Ease out of each intersection or bridge port: the road's own points are dropped for a stretch that
                // grows with the angle between the port and the road (14-40 m), and one piece curves from the port's
                // direction into the road's. (A straight 18 m stub followed by the turn made the curls and S-bends at
                // junctions in the tester's France test.)
                int stubA = -1, stubB = -1;
                float total = Length(pts);
                float[] tQA = null, tQB = null;
                if (startObj != null) stubA = EaseIn(pts, true, startTan, total, out tQA);
                if (endObj != null) stubB = EaseIn(pts, false, endTan, total, out tQB);
                if (stubA >= 0 && stubB >= 0 && stubB <= stubA) stubB = -1;
                // round off corners left by re-routing and splitting (planned roads have none this sharp)
                {
                    int lo = stubA >= 0 ? 2 : 1, tail = stubB >= 0 ? 2 : 1;
                    pts = CutCorners(pts, lo, tail);
                    if (stubB >= 0) stubB = pts.Count - 2;
                }

                // break into pieces of about PieceLength, and wherever the road has turned more than 45° since the
                // last cut (one cubic piece can't take a sharp bend)
                List<int> cuts = new List<int>(); cuts.Add(0);
                float acc = 0, turn = 0;
                for (int k = 1; k < pts.Count; k++)
                {
                    acc += (float)Math.Sqrt(D2(pts[k], pts[k - 1]));
                    if (k + 1 < pts.Count) turn += Angle(Dir(pts[k - 1], pts[k]), Dir(pts[k], pts[k + 1]));
                    bool forced = k == stubA || k == stubB;
                    if (k < pts.Count - 1 && (forced || acc >= PieceLength || (turn > 45f && acc > 8f))) { cuts.Add(k); acc = 0; turn = 0; }
                }
                cuts.Add(pts.Count - 1);
                // no tiny pieces (CAW refuses most of them, and the straight fallback kinks the road): a piece under 12 m
                // is merged into its neighbour, unless both of its cuts are forced (the straight stubs at a port)
                for (int c = cuts.Count - 2; c >= 1; c--)
                {
                    float before = 0, after = 0;
                    for (int k = cuts[c - 1] + 1; k <= cuts[c]; k++) before += (float)Math.Sqrt(D2(pts[k], pts[k - 1]));
                    for (int k = cuts[c] + 1; k <= cuts[c + 1]; k++) after += (float)Math.Sqrt(D2(pts[k], pts[k - 1]));
                    bool forced = cuts[c] == stubA || cuts[c] == stubB;
                    if ((before < 12f || after < 12f) && !forced) cuts.RemoveAt(c);
                }
                object prev = null;
                for (int c = 0; c + 1 < cuts.Count; c++)
                {
                    int ia = cuts[c], ib = cuts[c + 1];
                    float[] A = pts[ia], B = pts[ib];
                    float[] tA = ia == 0 ? startTan : ia == stubA ? tQA : ia == stubB ? tQB : Dir(pts[Math.Max(0, ia - 1)], pts[Math.Min(pts.Count - 1, ia + 1)]);
                    float[] tB = ib == pts.Count - 1 ? endTan : ib == stubB ? tQB : ib == stubA ? tQA : Dir(pts[Math.Max(0, ib - 1)], pts[Math.Min(pts.Count - 1, ib + 1)]);
                    float L = (float)Math.Sqrt(D2(A, B)) / 3f;
                    float[] k1 = { A[0] + tA[0] * L, A[1] + tA[1] * L }, k2 = { B[0] - tB[0] * L, B[1] - tB[1] * L };
                    object sp = null;
                    try { sp = Caw.CreateSpline(world, A, k1, k2, B, group); }
                    catch (Exception ex) { if (failed < 5) Log.Error("Creating road spline failed", ex); }
                    if (sp == null)
                    {
                        // CAW refused the curve (the tester's logs: mostly short pieces at junctions, where the asked-for
                        // tangents disagree with the piece's direction): first a gentler curve, tangents halfway to the
                        // chord and shorter handles, so the road still bends smoothly instead of kinking at both ends
                        float cl = (float)Math.Sqrt(D2(A, B)); float[] ch = cl > 1e-3f ? new[] { (B[0] - A[0]) / cl, (B[1] - A[1]) / cl } : new[] { 1f, 0f };
                        float[] gA = Norm(new[] { tA[0] + ch[0], tA[1] + ch[1] }, ch), gB = Norm(new[] { tB[0] + ch[0], tB[1] + ch[1] }, ch);
                        float gl = cl / 4f;
                        try { sp = Caw.CreateSpline(world, A, new[] { A[0] + gA[0] * gl, A[1] + gA[1] * gl }, new[] { B[0] - gB[0] * gl, B[1] - gB[1] * gl }, B, group); } catch { }
                        if (sp != null) Log.Info(string.Format("  spline ({0:F1}, {1:F1}) -> ({2:F1}, {3:F1}) built with a gentler curve", A[0], A[1], B[0], B[1]));
                    }
                    if (sp == null)
                    {
                        // then straight, then as two halves (joined)
                        float[] s1 = { A[0] + (B[0] - A[0]) / 3, A[1] + (B[1] - A[1]) / 3 }, s2 = { A[0] + (B[0] - A[0]) * 2 / 3, A[1] + (B[1] - A[1]) * 2 / 3 };
                        try { sp = Caw.CreateSpline(world, A, s1, s2, B, group); } catch { }
                        if (sp == null)
                        {
                            float[] M = { (A[0] + B[0]) / 2, (A[1] + B[1]) / 2 };
                            object h1 = null, h2 = null;
                            try { h1 = Caw.CreateSpline(world, A, Lerp(A, M, 1f / 3), Lerp(A, M, 2f / 3), M, group); } catch { }
                            if (h1 != null) group = true;
                            try { if (h1 != null) h2 = Caw.CreateSpline(world, M, Lerp(M, B, 1f / 3), Lerp(M, B, 2f / 3), B, true); } catch { }
                            if (h1 != null && h2 == null) { try { Caw.DeleteRoads(new[] { h1 }, true); } catch (Exception ex) { Log.Error("Removing a half road piece failed", ex); } h1 = null; }
                            if (h1 != null && h2 != null)
                            {
                                Caw.ConnectPorts(h1, 1, h2, 0); made.Add(h1); made.Add(h2); splines += 2; group = true;
                                if (prev != null) Caw.ConnectPorts(prev, 1, h1, 0);
                                else if (ia == 0 && startObj != null) Caw.ConnectPorts(startObj, startPort, h1, 0);
                                if (ib == pts.Count - 1 && endObj != null) Caw.ConnectPorts(h2, 1, endObj, endPort);
                                prev = h2;
                                continue;
                            }
                        }
                        else Log.Info(string.Format("  spline ({0:F1}, {1:F1}) -> ({2:F1}, {3:F1}) built straight after the curve was refused", A[0], A[1], B[0], B[1]));
                    }
                    if (sp == null)
                    {
                        if (failed < 8) Log.Info(string.Format("  spline ({0:F1}, {1:F1}) -> ({2:F1}, {3:F1}), {4:F1} m, not created (CAW returned null, also straight and halved); ground {5:F1}/{6:F1} m",
                            A[0], A[1], B[0], B[1], Math.Sqrt(D2(A, B)), Caw.TerrainHeight(world, A[0], A[1]), Caw.TerrainHeight(world, B[0], B[1])));
                        failed++; prev = null; continue;
                    }
                    group = true; splines++;
                    if (splines <= 3) Log.Info(string.Format("  spline {0}: ({1:F1}, {2:F1}) ({3:F1}, {4:F1}) ({5:F1}, {6:F1}) ({7:F1}, {8:F1}), ground {9:F1} m",
                        splines, A[0], A[1], k1[0], k1[1], k2[0], k2[1], B[0], B[1], Caw.TerrainHeight(world, A[0], A[1])));
                    try
                    {
                        if (prev != null) Caw.ConnectPorts(prev, 1, sp, 0);
                        else if (ia == 0 && startObj != null) Caw.ConnectPorts(startObj, startPort, sp, 0);
                        if (ib == pts.Count - 1 && endObj != null) Caw.ConnectPorts(sp, 1, endObj, endPort);
                    }
                    catch (Exception ex) { if (failed < 5) Log.Error("Connecting road ports failed", ex); failed++; }
                    prev = sp;
                    made.Add(sp); builtEnds.Add(new[] { A[0], A[1], B[0], B[1] });
                }
            }
            // 6. Tidy up: join loose ends to a free port nearby, then cap the remaining dead ends (not roads leaving the map).
            int snapped = 0, caps = 0;
            foreach (Node nd in nodes) if (nd.Intersection != null && !nd.Bridge) made.Add(nd.Intersection);
            try { Tidy(world, ti, made, bridgeSplines, ref group, out snapped, out caps); }
            catch (Exception ex) { Log.Error("Tidying road ends failed", ex); }

            try { CheckBuilt(world, made); }
            catch (Exception ex) { Log.Error("Checking built roads failed", ex); }

            // 7. CAW's own grading over the roads (the tester: "run this over the roads which will make them smooth"):
            // Flatten slope, edge 3 m, smooth edges, 3 passes; bridge pieces left alone
            int graded = 0;
            if (grade && made.Count > 0)
            {
                if (progress != null) progress("Grading roads", 97);
                try { graded = Caw.GradeRoads(world, made, bridgeSplines, 3f, 3); }
                catch (Exception ex) { Log.Error("Grading roads failed", ex); }
            }

            string note = string.Format("Built {0} intersections, {1} road pieces and {2} of {3} bridges; joined {4} loose ends, capped {5} dead ends{6}. CAW's Undo removes them all.",
                inters, splines, bridgesBuilt, spans.Count, snapped, caps,
                failed > 0 ? " (" + failed + " failed, see log)" : "");
            if (graded > 0) note += " Graded " + graded + " road objects with CAW's grading tool (a separate undo step).";
            if (spans.Count > 0 && bridgeItem == null) note += " No road bridge object was found in the catalog (needs Supernatural's bridges), so roads stop at the water.";
            Log.Info("Roads: " + note);
            return note;
        }

        /// <summary>Logs the world's road texture sets and selects the first (CAW's base game road) if none is valid.</summary>
        private static void EnsureRoadTextureSet(object world)
        {
            object terr = Caw.Terrain(world);
            uint size = Convert.ToUInt32(Caw.Call(terr, "GetRoadTextureArraySize"));
            ushort idx = Convert.ToUInt16(Caw.Call(terr, "GetRoadTextureIdx"));
            Log.Info(string.Format("  road texture sets: {0}, selected {1}", size, idx));
            if (size > 0 && idx >= size)
            {
                Caw.Call(terr, "SetRoadTextureIdx", (ushort)0);
                Log.Info("  road texture set 0 selected (none was)");
            }
        }

        /// <summary>After building: how many pieces CAW lists per road texture set and how many are visible.</summary>
        private static void CheckBuilt(object world, List<object> made)
        {
            int hidden = 0;
            foreach (object o in made)
            {
                object v = Caw.GetProperty(o, "Visible");
                if (v != null && !Convert.ToBoolean(v)) hidden++;
            }
            object terr = Caw.Terrain(world);
            uint size = Convert.ToUInt32(Caw.Call(terr, "GetRoadTextureArraySize"));
            string sets = "";
            for (uint i = 0; i < size; i++)
            {
                System.Collections.ICollection c = Caw.Call(terr, "GetRoadTextureSetRoadSplines", (ushort)i) as System.Collections.ICollection;
                sets += string.Format(" [{0}: {1} splines]", i, c == null ? -1 : c.Count);
            }
            Log.Info(string.Format("  check: {0} road objects built, {1} not visible; texture sets{2}; selected {3}",
                made.Count, hidden, sets, Caw.Call(terr, "GetRoadTextureIdx")));
        }

        private const float CrowdDeg = 55f;   // two roads closer than this at a junction: one is moved into its own T
        private const float StubLength = 18f; // metres from a bridge end to the intersection placed past it

        /// <summary>
        /// Rounds corners sharper than 25° between index 'lo' and Count - 1 - 'tail' (the ends and the ease cuts stay):
        /// each such corner point becomes two, a quarter of the way (at most 10 m; over 90°: 40%, at most 20 m) along its
        /// two segments; three passes.
        /// </summary>
        private static List<float[]> CutCorners(List<float[]> pts, int lo, int tail)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                int hi = pts.Count - 1 - tail;
                if (hi < lo) break;
                List<float[]> o = new List<float[]>(pts.Count + 8);
                bool changed = false;
                for (int i = 0; i < pts.Count; i++)
                {
                    float turn = i < lo || i > hi || i == 0 || i == pts.Count - 1 ? 0 : Angle(Dir(pts[i - 1], pts[i]), Dir(pts[i], pts[i + 1]));
                    if (turn <= 25f) { o.Add(pts[i]); continue; }
                    float[] p = pts[i], a = pts[i - 1], b = pts[i + 1];
                    float la = (float)Math.Sqrt(D2(a, p)), lb = (float)Math.Sqrt(D2(b, p));
                    float frac = turn > 90f ? 0.4f : 0.25f, cap = turn > 90f ? 20f : 10f; // a U-turn needs a wider bend
                    float ta = la > 1e-3f ? Math.Min(frac, cap / la) : 0, tb = lb > 1e-3f ? Math.Min(frac, cap / lb) : 0;
                    o.Add(new[] { p[0] + (a[0] - p[0]) * ta, p[1] + (a[1] - p[1]) * ta });
                    o.Add(new[] { p[0] + (b[0] - p[0]) * tb, p[1] + (b[1] - p[1]) * tb });
                    changed = true;
                }
                pts = o;
                if (!changed) break;
            }
            return pts;
        }

        /// <summary>The direction a road leaves its start (or end), measured 20 m along it.</summary>
        private static float[] Leaving(List<float[]> r, bool atStart)
        {
            float[] d = DirAlong(r, atStart, 20f);
            return atStart ? d : new[] { -d[0], -d[1] };
        }

        /// <summary>
        /// Moves a junction to where its roads' lines best meet (least squares over each road's line through its point
        /// 20 m out, along its direction there; held near the old place, at most JunctionShift m). Junction nodes were the
        /// middle of the road ends, often a few metres off the roads' own lines, so a road reached its port parallel but
        /// offset and S-bent into it (the tester, 2026-10-25, #134; ChatGPT's review: fit the position as well as the turn).
        /// Returns how far it moved (m).
        /// </summary>
        private static float FitJunctionPoint(List<List<float[]>> roads, Node n)
        {
            double a11 = JunctionHold, a12 = 0, a22 = JunctionHold, b1 = JunctionHold * n.P[0], b2 = JunctionHold * n.P[1];
            foreach (End e in n.Ends)
            {
                List<float[]> r = roads[e.Road];
                if (r == null || r.Count < 2 || Length(r) < 25) continue;
                float[] d = DirAlong(r, e.AtStart, 20f), end = e.AtStart ? r[0] : r[r.Count - 1];
                float sgn = e.AtStart ? 1 : -1; // DirAlong points away from the junction at a start, towards it at an end
                double qx = end[0] + sgn * d[0] * 20, qz = end[1] + sgn * d[1] * 20;
                double m11 = 1 - d[0] * d[0], m12 = -d[0] * d[1], m22 = 1 - d[1] * d[1]; // projector across the line
                a11 += m11; a12 += m12; a22 += m22;
                b1 += m11 * qx + m12 * qz; b2 += m12 * qx + m22 * qz;
            }
            double det = a11 * a22 - a12 * a12;
            if (Math.Abs(det) < 1e-9) return 0;
            double px = (a22 * b1 - a12 * b2) / det, pz = (a11 * b2 - a12 * b1) / det;
            double dx = px - n.P[0], dz = pz - n.P[1], dist = Math.Sqrt(dx * dx + dz * dz);
            if (dist > JunctionShift) { dx *= JunctionShift / dist; dz *= JunctionShift / dist; dist = JunctionShift; }
            n.P = new[] { n.P[0] + (float)dx, n.P[1] + (float)dz };
            return (float)dist;
        }

        // how strongly a junction stays put (per m², against 1 per m² off each road's line); its furthest move (m)
        private const float JunctionHold = 0.05f, JunctionShift = 10f;

        /// <summary>
        /// Unit direction of travel at one end of a polyline, from the end to the point 'dist' metres along it
        /// (at the start), or from that point to the end (at the end).
        /// </summary>
        private static float[] DirAlong(List<float[]> r, bool atStart, float dist)
        {
            int n = r.Count;
            if (n < 2) return new[] { 1f, 0f };
            float acc = 0;
            for (int k = 1; k < n; k++)
            {
                float[] a = atStart ? r[k - 1] : r[n - k], b = atStart ? r[k] : r[n - 1 - k];
                float d = (float)Math.Sqrt(D2(a, b));
                if (acc + d >= dist || k == n - 1)
                {
                    float t = d > 1e-4f ? Math.Min(1f, (dist - acc) / d) : 1f;
                    float[] q = { a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t };
                    return atStart ? Dir(r[0], q) : Dir(q, r[n - 1]);
                }
                acc += d;
            }
            return Dir(r[0], r[n - 1]); // not reached: the loop returns at its last step
        }

        /// <summary>
        /// Lets the road ease out of a port (direction 'dir', travel direction at that end): drops the road's points
        /// within D of the end, D = 14-40 m growing with the angle between the port and the road (at most a third of
        /// the road), so one piece curves from the port into the road. Returns the index of the first kept point
        /// (the cut) and the road's direction there in 'tq'.
        /// </summary>
        private static int EaseIn(List<float[]> pts, bool atStart, float[] dir, float total, out float[] tq)
        {
            tq = dir;
            // points between the port and the junction's middle (the road was planned to the middle) are dropped first
            float sg = atStart ? 1 : -1;
            float[] e0 = atStart ? pts[0] : pts[pts.Count - 1];
            while (pts.Count > 2)
            {
                float[] q = atStart ? pts[1] : pts[pts.Count - 2];
                if (((q[0] - e0[0]) * dir[0] + (q[1] - e0[1]) * dir[1]) * sg > 2f) break;
                pts.RemoveAt(atStart ? 1 : pts.Count - 2);
            }
            if (pts.Count < 3 || total < 20f) return -1;
            float[] rd = DirAlong(pts, atStart, 30f);
            float ang = Angle(dir, rd);
            float D = Math.Min(total / 3f, Math.Max(14f, Math.Min(40f, 12f + 0.45f * ang)));
            float[] end = atStart ? pts[0] : pts[pts.Count - 1];
            float acc = 0;
            if (atStart)
            {
                while (pts.Count > 2)
                {
                    acc = (float)Math.Sqrt(D2(pts[1], end));
                    if (acc >= D || pts.Count == 3) break;
                    pts.RemoveAt(1);
                }
                tq = pts.Count > 2 ? Dir(pts[1], pts[2]) : Dir(pts[0], pts[1]);
                return pts.Count > 2 ? 1 : -1;
            }
            while (pts.Count > 2)
            {
                acc = (float)Math.Sqrt(D2(pts[pts.Count - 2], end));
                if (acc >= D || pts.Count == 3) break;
                pts.RemoveAt(pts.Count - 2);
            }
            tq = pts.Count > 2 ? Dir(pts[pts.Count - 3], pts[pts.Count - 2]) : Dir(pts[pts.Count - 2], pts[pts.Count - 1]);
            return pts.Count > 2 ? pts.Count - 2 : -1;
        }

        /// <summary>Angle between two unit directions, degrees.</summary>
        private static float Angle(float[] a, float[] b)
        {
            double d = Math.Max(-1, Math.Min(1, a[0] * b[0] + a[1] * b[1]));
            return (float)(Math.Acos(d) * 180 / Math.PI);
        }

        public const string ResidentialLayer = "Residential Lots", CommercialLayer = "Commercial Lots";

        /// <summary>A lot ready for CAW: its four corners in world x/z (front edge 0 -> 1), type and community subtype.</summary>
        internal sealed class LotPlacement { public float[][] World; public bool Community; public uint Sub; }

        /// <summary>
        /// The planned lots in world coordinates, community subtypes chosen (by the lot's kind, KindSubtypes; a lot with
        /// no kind: a beach by the water, else parks, library, gym, shops, restaurant, pool, graveyard in turn) and each
        /// fitted to the road CAW actually built (LotPlanner.SnapToRoads): front edge parallel to a straight stretch of it, 6 m from its centre line.
        /// </summary>
        public static List<LotPlacement> FitLots(object world, TerrainInfo ti, RoadNet net, out string note)
        {
            uint[] community = { 5, 9, 10, 16, 15, 6, 8, 7 }; // small park, library, gym, small shop, restaurant, big park, pool, graveyard
            int ci = 0;
            Dictionary<PlannedLot, uint> kindSub = KindSubtypes(net.Lots, net.Centre);
            List<LotPlacement> list = new List<LotPlacement>();
            foreach (PlannedLot l in net.Lots)
            {
                float[][] c = LotPlanner.Corners(l.X, l.Z, l.Ux, l.Uz, l.Width, l.Depth);
                LotPlacement p = new LotPlacement();
                p.World = new float[4][];
                for (int i = 0; i < 4; i++)
                {
                    float wx, wz;
                    TerrainSampler.Map(ti.Orientation, c[i][0] / ti.WorldSize, c[i][1] / ti.WorldSize, out wx, out wz);
                    p.World[i] = new[] { ti.Origin + wx * ti.WorldSize, ti.Origin + wz * ti.WorldSize };
                }
                p.Community = l.Community;
                uint sub;
                if (l.Community && kindSub.TryGetValue(l, out sub)) p.Sub = sub;
                else if (l.Community)
                {
                    // by the water: a beach lot
                    bool shore = false;
                    float[] ctr = l.Centre;
                    for (int a = 0; a < 8 && !shore; a++)
                    {
                        double th = a * Math.PI / 4;
                        float x = ctr[0] + (float)Math.Cos(th) * (Math.Max(l.Width, l.Depth) / 2f + 25), z = ctr[1] + (float)Math.Sin(th) * (Math.Max(l.Width, l.Depth) / 2f + 25);
                        float mx = x / ti.WorldSize * (ti.Heights.W - 1), mz = z / ti.WorldSize * (ti.Heights.H - 1);
                        if (mx >= 0 && mz >= 0 && mx < ti.Heights.W - 1 && mz < ti.Heights.H - 1 && TerrainGen.Sample(ti.Heights, mx, mz) < ti.WaterLevel) shore = true;
                    }
                    p.Sub = shore ? 13u : community[ci++ % community.Length];
                }
                list.Add(p);
            }
            note = "";
            if (list.Count == 0) return list;
            List<float[]> built = Caw.RoadSplines(world);
            List<float[][]> rects = new List<float[][]>();
            foreach (LotPlacement p in list) rects.Add(p.World);
            int moved, dropped;
            float[][][] fitted = LotPlanner.SnapToRoads(rects, built, 6f, out moved, out dropped);
            List<LotPlacement> kept = new List<LotPlacement>();
            for (int i = 0; i < list.Count; i++) if (fitted[i] != null) { list[i].World = fitted[i]; kept.Add(list[i]); }
            Log.Info(string.Format("Lots fitted to the built roads ({0} road pieces): {1} kept ({2} moved or turned to line up, {3} away from any road as planned), {4} dropped (beside a road that isn't straight along their front)",
                built.Count, kept.Count, moved, LotPlanner.KeptOffRoad, dropped));
            if (dropped > 0) note = string.Format(" {0} planned lot(s) left out: the built road beside them isn't straight along their front.", dropped);
            return kept;
        }

        /// <summary>
        /// CAW's community lot type (Sims3.SimIFace.CommercialLotSubType, base game values only) for each lot with a kind:
        /// beach 13, fishing spot 12, park 5 (6 from 2000 m2), pool 8, graveyard 7; civic buildings: city hall 21 for the
        /// one nearest the centre, stadium 20 for the largest, then hospital 18, theatre 19, big shop 17, restaurant 15,
        /// day spa 14, small shop 16 in turn from the centre out; venues and others: library 9, gym 10, art gallery 11,
        /// restaurant 15, small shop 16 in turn.
        /// </summary>
        internal static Dictionary<PlannedLot, uint> KindSubtypes(List<PlannedLot> lots, float[] centre)
        {
            Dictionary<PlannedLot, uint> sub = new Dictionary<PlannedLot, uint>();
            List<PlannedLot> civic = new List<PlannedLot>(), venue = new List<PlannedLot>();
            foreach (PlannedLot l in lots)
            {
                if (!l.Community || l.Kind == null) continue;
                switch (l.Kind)
                {
                    case "beach": sub[l] = 13; break;
                    case "fishing": sub[l] = 12; break;
                    case "park": sub[l] = l.Width * l.Depth >= 2000 ? 6u : 5u; break;
                    case "pool": sub[l] = 8; break;
                    case "cemetery": sub[l] = 7; break;
                    case "civic": civic.Add(l); break;
                    default: venue.Add(l); break;
                }
            }
            float[] c = centre ?? new float[] { 0, 0 };
            Comparison<PlannedLot> near = delegate (PlannedLot a, PlannedLot b)
            {
                float[] ca = a.Centre, cb = b.Centre;
                return ((ca[0] - c[0]) * (ca[0] - c[0]) + (ca[1] - c[1]) * (ca[1] - c[1])).CompareTo((cb[0] - c[0]) * (cb[0] - c[0]) + (cb[1] - c[1]) * (cb[1] - c[1]));
            };
            civic.Sort(near); venue.Sort(near);
            if (civic.Count > 0) sub[civic[0]] = 21;
            if (civic.Count > 1)
            {
                PlannedLot big = null;
                for (int i = 1; i < civic.Count; i++) if (big == null || civic[i].Width * civic[i].Depth > big.Width * big.Depth) big = civic[i];
                sub[big] = 20;
            }
            // base-game subtypes only (no pack needed): hospital, police station, theatre, science lab, big shop,
            // military base, restaurant, day spa, small shop (Sims3.SimIFace.CommercialLotSubType, decompiled)
            uint[] rh = { 18, 47, 19, 45, 17, 48, 15, 14, 16 };
            int k = 0;
            foreach (PlannedLot l in civic) if (!sub.ContainsKey(l)) sub[l] = rh[k++ % rh.Length];
            uint[] ven = { 9, 10, 11, 15, 16 };
            k = 0;
            foreach (PlannedLot l in venue) sub[l] = ven[k++ % ven.Length];
            return sub;
        }

        /// <summary>
        /// The lots (from FitLots) as CAW lots (Caw.CreateLot = CAW's Place Lot tool), in one undo step.
        /// Run after Build roads: CAW refuses a lot on a road, so they go in once the roads are there.
        /// CAW only adds a lot to the active World Layer, never the Global Layer (WorldLayersControl.AddLot, decompiled:
        /// "Please add a new Layer before adding a lot"), so residential lots go into a "Residential Lots" layer and
        /// community lots into "Commercial Lots", made if missing (EA's worlds keep their lots in their own layers too).
        /// </summary>
        public static string PlaceFitted(object world, List<LotPlacement> lots, ProgressFn progress)
        {
            int made = 0, refused = 0, k = 0, madeRes = 0, madeCom = 0;
            bool group = false;
            string firstWhy = "";
            List<LotPlacement> ordered = new List<LotPlacement>();
            foreach (LotPlacement l in lots) if (!l.Community) ordered.Add(l);
            foreach (LotPlacement l in lots) if (l.Community) ordered.Add(l);
            int layerFor = -1; // 0 residential, 1 commercial: the layer now active
            Caw.ResetLotTiming();
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            List<string> pace = new List<string>(); // seconds per 10 lots, to see whether CAW slows as lots add up
            long lastMs = 0;
            foreach (LotPlacement l in ordered)
            {
                if (k > 0 && k % 10 == 0) { pace.Add(((sw.ElapsedMilliseconds - lastMs) / 1000.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)); lastMs = sw.ElapsedMilliseconds; }
                int want = l.Community ? 1 : 0;
                if (want != layerFor)
                {
                    string name = want == 1 ? CommercialLayer : ResidentialLayer;
                    bool used = false;
                    try { used = Caw.UseLayer(world, name); } catch (Exception ex) { Log.Error("Selecting World Layer '" + name + "' failed", ex); }
                    if (!used)
                    {
                        string msg = "Could not create or select the World Layer '" + name + "'; add a World Layer in CAW, make it active and try again.";
                        Log.Info("Lots: " + msg);
                        return string.Format("Placed {0} lots. {1}", made, msg);
                    }
                    layerFor = want;
                }
                if (progress != null && k % 5 == 0) progress("Placing lots", 100 * k / Math.Max(1, lots.Count));
                k++;
                string why;
                bool ok = false;
                try { ok = Caw.CreateLot(world, l.World, l.Community, l.Sub, group, out why); }
                catch (Exception ex) { why = ex.Message; if (refused == 0) Log.Error("Placing a lot failed", ex); }
                if (ok) { made++; group = true; if (l.Community) madeCom++; else madeRes++; }
                else { refused++; if (firstWhy.Length == 0) firstWhy = why; }
            }
            Log.Info(string.Format("Lots: {0} placed ({1} in '{2}', {3} in '{4}'), {5} refused{6}", made, madeRes, ResidentialLayer, madeCom, CommercialLayer,
                refused, refused > 0 ? " (first: " + firstWhy + ")" : ""));
            Log.Info("Lots timing: " + Caw.LotTimingSummary() + "; seconds per 10 lots: " + string.Join(" ", pace.ToArray()) + "; total " + (sw.ElapsedMilliseconds / 1000.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " s");
            return string.Format("Placed {0} lots: {1} residential in World Layer '{2}', {3} community in '{4}'{5}. CAW's Undo removes them.",
                made, madeRes, ResidentialLayer, madeCom, CommercialLayer, refused > 0 ? string.Format(" ({0} refused: {1})", refused, firstWhy) : "");
        }

        /// <summary>CAW's built road splines as polylines on the terrain map (metres, image orientation), 16 points a piece.</summary>
        public static List<List<float[]>> MapRoadLines(object world, TerrainInfo ti)
        {
            List<List<float[]>> outp = new List<List<float[]>>();
            foreach (float[] k in Caw.RoadSplines(world))
            {
                if (k.Length < 8) continue;
                List<float[]> line = new List<float[]>();
                for (int i = 0; i <= 16; i++)
                {
                    float t = i / 16f, w = 1 - t;
                    float x = w * w * w * k[0] + 3 * w * w * t * k[2] + 3 * w * t * t * k[4] + t * t * t * k[6];
                    float z = w * w * w * k[1] + 3 * w * w * t * k[3] + 3 * w * t * t * k[5] + t * t * t * k[7];
                    float u, v;
                    TerrainSampler.Unmap(ti.Orientation, (x - ti.Origin) / ti.WorldSize, (z - ti.Origin) / ti.WorldSize, out u, out v);
                    line.Add(new[] { u * ti.WorldSize, v * ti.WorldSize });
                }
                outp.Add(line);
            }
            return outp;
        }

        /// <summary>Fitted lots back on the terrain map (metres, image orientation), for levelling their pads.</summary>
        public static List<PlannedLot> ToMapLots(TerrainInfo ti, List<LotPlacement> lots)
        {
            List<PlannedLot> outp = new List<PlannedLot>();
            foreach (LotPlacement p in lots)
            {
                float[][] m = new float[4][];
                for (int i = 0; i < 4; i++)
                {
                    float u, v;
                    TerrainSampler.Unmap(ti.Orientation, (p.World[i][0] - ti.Origin) / ti.WorldSize, (p.World[i][1] - ti.Origin) / ti.WorldSize, out u, out v);
                    m[i] = new[] { u * ti.WorldSize, v * ti.WorldSize };
                }
                outp.Add(LotPlanner.FromCorners(m, p.Community));
            }
            return outp;
        }

        /// <summary>
        /// The Footpaths tool: builds planned paths (metres on the terrain map) as CAW footpaths, each a chain of sidewalk
        /// pieces, in one undo step. Returns a note for the status line.
        /// </summary>
        public static string BuildPaths(object world, TerrainInfo ti, List<FootPath> paths, ProgressFn progress)
        {
            try { Caw.Call(world, "TryEdit"); } catch (Exception ex) { Log.Error("World.TryEdit failed", ex); }
            bool group = false;
            int built = 0, pieces = 0, failed = 0, k = 0;
            foreach (FootPath fp in paths)
            {
                if (progress != null) progress("Building footpaths", 100 * k++ / Math.Max(1, paths.Count));
                List<float[]> w = new List<float[]>();
                foreach (float[] p in fp.Points)
                {
                    float wx, wz;
                    TerrainSampler.Map(ti.Orientation, p[0] / ti.WorldSize, p[1] / ti.WorldSize, out wx, out wz);
                    w.Add(new[] { ti.Origin + wx * ti.WorldSize, ti.Origin + wz * ti.WorldSize });
                }
                try { if (BuildPath(world, w, fp.Closed, ref group, ref pieces, ref failed)) built++; }
                catch (Exception ex) { Log.Error("Building a footpath failed", ex); failed++; }
            }
            string note = string.Format("Built {0} of {1} footpaths ({2} pieces){3}. CAW's Undo removes them.", built, paths.Count, pieces,
                failed > 0 ? ", " + failed + " pieces refused (see log)" : "");
            Log.Info("Footpaths: " + note);
            return note;
        }

        private static float Sq(float v) { return v * v; }

        /// <summary>A piece with these ends (either way round, within 1.5 m) was already built.</summary>
        private static bool Built(List<float[]> ends, float[] a, float[] b)
        {
            foreach (float[] e in ends)
            {
                float d1 = Sq(e[0] - a[0]) + Sq(e[1] - a[1]), d2 = Sq(e[2] - b[0]) + Sq(e[3] - b[1]);
                float d3 = Sq(e[0] - b[0]) + Sq(e[1] - b[1]), d4 = Sq(e[2] - a[0]) + Sq(e[3] - a[1]);
                if ((d1 < 2.25f && d2 < 2.25f) || (d3 < 2.25f && d4 < 2.25f)) return true;
            }
            return false;
        }

        private const float PathPieceLength = 36f; // Riverview's footpath pieces: median 36 m

        /// <summary>One footpath as a chain of CAW sidewalk splines, each joined to the next (and the last to the first
        /// for a loop). A refused piece is tried straight, else skipped (the chain carries on from the next piece).</summary>
        private static bool BuildPath(object world, List<float[]> pts, bool closed, ref bool group, ref int pieces, ref int failed)
        {
            if (closed) pts.Add(pts[0]);
            if (pts.Count < 2 || Length(pts) < 10) return false;
            List<int> cuts = new List<int>(); cuts.Add(0);
            float acc = 0;
            for (int k = 1; k < pts.Count; k++)
            {
                acc += (float)Math.Sqrt(D2(pts[k], pts[k - 1]));
                if (acc >= PathPieceLength && k < pts.Count - 1) { cuts.Add(k); acc = 0; }
            }
            cuts.Add(pts.Count - 1);
            object prev = null, first = null;
            int built = 0;
            for (int c = 0; c + 1 < cuts.Count; c++)
            {
                int ia = cuts[c], ib = cuts[c + 1];
                float[] A = pts[ia], B = pts[ib];
                // tangents across the joins (a loop wraps round)
                float[] tA = ia == 0 && !closed ? Dir(A, pts[1]) : Dir(Wrap(pts, ia - 1, closed), Wrap(pts, ia + 1, closed));
                float[] tB = ib == pts.Count - 1 && !closed ? Dir(pts[ib - 1], B) : Dir(Wrap(pts, ib - 1, closed), Wrap(pts, ib + 1, closed));
                float L = (float)Math.Sqrt(D2(A, B)) / 3f;
                object sp = null;
                try { sp = Caw.CreateSpline(world, A, new[] { A[0] + tA[0] * L, A[1] + tA[1] * L }, new[] { B[0] - tB[0] * L, B[1] - tB[1] * L }, B, group, true); }
                catch (Exception ex) { if (failed < 5) Log.Error("Creating a footpath piece failed", ex); }
                if (sp == null)
                    try { sp = Caw.CreateSpline(world, A, Lerp(A, B, 1f / 3), Lerp(A, B, 2f / 3), B, group, true); } catch { }
                if (sp == null) { failed++; prev = null; continue; }
                group = true; pieces++; built++;
                if (prev != null) Caw.ConnectPorts(prev, 1, sp, 0);
                if (first == null) first = sp;
                prev = sp;
            }
            if (closed && prev != null && first != null && prev != first && built == cuts.Count - 1) Caw.ConnectPorts(prev, 1, first, 0);
            return built > 0;
        }

        private static float[] Wrap(List<float[]> pts, int i, bool closed)
        {
            // a closed list repeats its first point at the end
            int n = pts.Count;
            if (closed) { if (i < 0) i += n - 1; else if (i >= n) i -= n - 1; }
            return pts[Math.Max(0, Math.Min(n - 1, i))];
        }

        /// <summary>The smallest angle (radians) between two roads leaving a junction, measured 20 m out.</summary>
        private static float ClosestPair(List<List<float[]>> roads, Node n)
        {
            float best = (float)Math.PI;
            for (int i = 0; i < n.Ends.Count; i++)
                for (int j = i + 1; j < n.Ends.Count; j++)
                {
                    if (n.Ends[i].Road == n.Ends[j].Road) continue;
                    float[] a = At(Oriented(roads[n.Ends[i].Road], n.Ends[i].AtStart), 20f), b = At(Oriented(roads[n.Ends[j].Road], n.Ends[j].AtStart), 20f);
                    float d = Math.Abs((float)Math.Atan2(a[1] - n.P[1], a[0] - n.P[0]) - (float)Math.Atan2(b[1] - n.P[1], b[0] - n.P[0]));
                    if (d > Math.PI) d = (float)(2 * Math.PI - d);
                    best = Math.Min(best, d);
                }
            return best;
        }

        /// <summary>Of the roads at a junction with more than 4, the shorter of the two closest in direction is cut back
        /// and joined to the other one 30 m out (split there). False if no pair is long enough.</summary>
        private static bool MoveFifthRoad(List<List<float[]>> roads, Node n, float limit)
        {
            int c = n.Ends.Count;
            float[] ang = new float[c];
            List<float[]>[] o = new List<float[]>[c];
            for (int k = 0; k < c; k++)
            {
                o[k] = Oriented(roads[n.Ends[k].Road], n.Ends[k].AtStart); // starts at the node
                float[] q = At(o[k], 20f);
                ang[k] = (float)Math.Atan2(q[1] - n.P[1], q[0] - n.P[0]);
            }
            float best = float.MaxValue; int bi = -1, bj = -1;
            for (int i = 0; i < c; i++)
                for (int j = 0; j < c; j++)
                {
                    if (i == j || n.Ends[i].Road == n.Ends[j].Road) continue;
                    if (Length(o[j]) < 60f || Length(o[i]) < 50f) continue; // j is split at 30 m, i cut back to 40 m
                    float d = Math.Abs(ang[i] - ang[j]); if (d > Math.PI) d = (float)(2 * Math.PI - d);
                    if (d < best || d == best && Length(o[i]) < Length(o[bi])) { best = d; bi = i; bj = j; }
                }
            if (bi < 0 || best > limit) return false;
            List<float[]> partner = o[bj], extra = o[bi];
            int split = 1; float run = 0;
            for (; split < partner.Count - 1; split++) { run += (float)Math.Sqrt(D2(partner[split - 1], partner[split])); if (run >= 30f) break; }
            if (split >= partner.Count - 1) return false;
            // the moved road starts at the split point and carries on from where it is clearly beyond it (further from
            // the junction, without doubling back: a hairpin there was built as two overlapping pieces)
            float[] at = partner[split];
            float beyond = (float)Math.Sqrt(D2(at, n.P)) + 15f;
            int first = -1;
            run = 0;
            for (int k = 1; k < extra.Count && first < 0; k++)
            {
                run += (float)Math.Sqrt(D2(extra[k - 1], extra[k]));
                if (run > 40f && Math.Sqrt(D2(extra[k], n.P)) > beyond) first = k;
            }
            if (first < 0) return false;
            while (first + 1 < extra.Count && Angle(Dir(at, extra[first]), Dir(extra[first], extra[first + 1])) > 75f) first++;
            if (first + 1 >= extra.Count && D2(at, extra[extra.Count - 1]) < 400f) return false;
            List<float[]> cut = new List<float[]>();
            cut.Add(at);
            for (int k = first; k < extra.Count; k++) cut.Add(extra[k]);
            if (cut.Count < 2) return false;
            int ri = n.Ends[bi].Road, rj = n.Ends[bj].Road;
            Log.Info(string.Format("  road moved off the junction at ({0:F0}, {1:F0}) to a T at ({2:F0}, {3:F0}), {4:F0} deg from its neighbour; next points ({5:F0}, {6:F0})",
                n.P[0], n.P[1], at[0], at[1], best * 180 / Math.PI, cut.Count > 1 ? cut[1][0] : 0, cut.Count > 1 ? cut[1][1] : 0));
            roads[ri] = cut;
            roads[rj] = partner.GetRange(0, split + 1);
            roads.Add(partner.GetRange(split, partner.Count - split));
            return true;
        }

        /// <summary>The point d metres along r (its end if shorter).</summary>
        private static float[] At(List<float[]> r, float d)
        {
            for (int k = 1; k < r.Count; k++)
            {
                float l = (float)Math.Sqrt(D2(r[k - 1], r[k]));
                if (l >= d && l > 0) { float t = d / l; return new[] { r[k - 1][0] + t * (r[k][0] - r[k - 1][0]), r[k - 1][1] + t * (r[k][1] - r[k - 1][1]) }; }
                d -= l;
            }
            return r[r.Count - 1];
        }

        /// <summary>Same two end points (either way round) and every point of a within 4 m of b.</summary>
        private static bool SameRoad(List<float[]> a, List<float[]> b)
        {
            float snap = NodeSnap * NodeSnap;
            bool same = D2(a[0], b[0]) < snap && D2(a[a.Count - 1], b[b.Count - 1]) < snap
                     || D2(a[0], b[b.Count - 1]) < snap && D2(a[a.Count - 1], b[0]) < snap;
            if (!same) return false;
            foreach (float[] p in a)
            {
                float best = float.MaxValue;
                for (int j = 0; j + 1 < b.Count; j++) best = Math.Min(best, SegD2(p, b[j], b[j + 1]));
                if (b.Count == 1) best = D2(p, b[0]);
                if (best > 16f) return false;
            }
            return true;
        }

        private static float SegD2(float[] p, float[] a, float[] b)
        {
            float dx = b[0] - a[0], dz = b[1] - a[1], l = dx * dx + dz * dz;
            float t = l > 0 ? Math.Max(0, Math.Min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dz) / l)) : 0;
            float ex = a[0] + t * dx - p[0], ez = a[1] + t * dz - p[1];
            return ex * ex + ez * ez;
        }

        /// <summary>
        /// Junction nodes: road ends within NodeSnap of each other (chained) are one node, placed at their middle.
        /// (Taking the first end as the node and adding ends near it split one junction in two when its ends were
        /// spread: the two-road half was then merged into one road with a U-turn. pt13, Champs Les Sims seed 1.)
        /// </summary>
        private static List<Node> BuildNodes(List<List<float[]>> roads)
        {
            List<float[]> pos = new List<float[]>(); List<End> ends = new List<End>();
            for (int ri = 0; ri < roads.Count; ri++)
                for (int e = 0; e < 2; e++)
                {
                    pos.Add(e == 0 ? roads[ri][0] : roads[ri][roads[ri].Count - 1]);
                    End end = new End(); end.Road = ri; end.AtStart = e == 0; ends.Add(end);
                }
            int m = pos.Count;
            int[] parent = new int[m];
            for (int i = 0; i < m; i++) parent[i] = i;
            float snap = NodeSnap * NodeSnap;
            for (int i = 0; i < m; i++)
                for (int j = i + 1; j < m; j++)
                    if (D2(pos[i], pos[j]) < snap)
                    {
                        int a = Root(parent, i), b = Root(parent, j);
                        if (a != b) parent[b] = a;
                    }
            Dictionary<int, Node> byRoot = new Dictionary<int, Node>();
            List<Node> nodes = new List<Node>();
            for (int i = 0; i < m; i++)
            {
                int r = Root(parent, i);
                Node n;
                if (!byRoot.TryGetValue(r, out n)) { n = new Node(); n.P = new float[2]; byRoot[r] = n; nodes.Add(n); }
                n.Ends.Add(ends[i]);
                n.P[0] += pos[i][0]; n.P[1] += pos[i][1];
            }
            foreach (Node n in nodes) { n.P[0] /= n.Ends.Count; n.P[1] /= n.Ends.Count; }
            return nodes;
        }

        /// <summary>Whether road ri's two ends are still joined without it, by roads of total length at most 3x its own (+40 m).</summary>
        private static bool Redundant(List<List<float[]>> roads, int ri)
        {
            List<float[]> r = roads[ri];
            float limit = 3 * Length(r) + 40f;
            List<Node> nodes = BuildNodes(roads);
            Node a = NodeAt(nodes, r[0]), b = NodeAt(nodes, r[r.Count - 1]);
            if (a == null || b == null) return false;
            if (a == b) return true;
            // Dijkstra over nodes, skipping road ri
            Dictionary<Node, float> dist = new Dictionary<Node, float>();
            List<Node> open = new List<Node>();
            dist[a] = 0; open.Add(a);
            while (open.Count > 0)
            {
                int bi = 0;
                for (int i = 1; i < open.Count; i++) if (dist[open[i]] < dist[open[bi]]) bi = i;
                Node n = open[bi]; open.RemoveAt(bi);
                float dn = dist[n];
                if (n == b) return true;
                if (dn > limit) break;
                foreach (End e in n.Ends)
                {
                    if (e.Road == ri) continue;
                    List<float[]> rr = roads[e.Road];
                    Node o = NodeAt(nodes, e.AtStart ? rr[rr.Count - 1] : rr[0]);
                    if (o == null) continue;
                    float nd = dn + Length(rr), old;
                    if (nd <= limit && (!dist.TryGetValue(o, out old) || nd < old)) { dist[o] = nd; if (!open.Contains(o)) open.Add(o); }
                }
            }
            return false;
        }

        private static int Root(int[] parent, int i)
        {
            while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
            return i;
        }

        private static bool AtBridgeEnd(List<Span> spans, float[] p)
        {
            foreach (Span sp in spans) if (D2(sp.EndA, p) < NodeSnap * NodeSnap || D2(sp.EndB, p) < NodeSnap * NodeSnap) return true;
            return false;
        }

        private static Node NearestNode(List<Node> nodes, float[] p, float within)
        {
            Node best = null; float bd = within * within;
            foreach (Node n in nodes) { if (n.Bridge) continue; float d = D2(n.P, p); if (d < bd) { bd = d; best = n; } }
            return best;
        }

        /// <summary>The nearest node within 2 x NodeSnap (a node sits at the middle of its ends, which can be spread).</summary>
        private static Node NodeAt(List<Node> nodes, float[] p)
        {
            Node best = null; float bd = 4 * NodeSnap * NodeSnap;
            foreach (Node n in nodes) { float d = D2(n.P, p); if (d < bd) { bd = d; best = n; } }
            return best;
        }

        // angle (deg) between each road end and the junction port it is joined to, for the log (#134: a road meeting its
        // port off-square bends into it, and two such bends between near junctions make an S)
        private static readonly List<float> PortMisfit = new List<float>();

        private static string PortMisfitSummary()
        {
            if (PortMisfit.Count == 0) return "no junction ends";
            List<float> v = new List<float>(PortMisfit); v.Sort();
            float sum = 0; int over = 0; foreach (float a in v) { sum += a; if (a > 15) over++; }
            return string.Format("{0} road ends at junctions, off their port: mean {1:F1} deg, p90 {2:F1} deg, over 15 deg {3}", v.Count, sum / v.Count, v[(int)(v.Count * 0.9)], over);
        }

        /// <summary>
        /// The port of a node's intersection for each of its roads, chosen together so the sum of squared angles between
        /// roads and their ports is least (one at a time, a late road could be left a port 90° off). Key: road * 2 + start.
        /// </summary>
        private static Dictionary<int, int> AssignPorts(List<List<float[]>> roads, Node n)
        {
            int pc = n.Used.Length, c = n.Ends.Count;
            if (c == 0 || c > pc || pc > 6) return null;
            float[][] nm = new float[pc][];
            for (int i = 0; i < pc; i++) nm[i] = Caw.PortNormal(n.Intersection, (uint)i);
            float[][] dir = new float[c][];
            for (int k = 0; k < c; k++) dir[k] = Away(roads, n.Ends[k]);
            int[] cur = new int[c], best = null; bool[] used = new bool[pc];
            double bestCost = double.MaxValue;
            Assign(0, c, pc, nm, dir, cur, used, 0, ref bestCost, ref best);
            if (best == null) return null;
            Dictionary<int, int> map = new Dictionary<int, int>();
            for (int k = 0; k < c; k++) { map[n.Ends[k].Road * 2 + (n.Ends[k].AtStart ? 1 : 0)] = best[k]; PortMisfit.Add(Angle(nm[best[k]], dir[k])); }
            return map;
        }

        private static void Assign(int k, int c, int pc, float[][] nm, float[][] dir, int[] cur, bool[] used, double cost, ref double bestCost, ref int[] best)
        {
            if (cost >= bestCost) return;
            if (k == c) { bestCost = cost; best = (int[])cur.Clone(); return; }
            for (int i = 0; i < pc; i++)
            {
                if (used[i]) continue;
                double a = Angle(nm[i], dir[k]) * Math.PI / 180;
                used[i] = true; cur[k] = i;
                Assign(k + 1, c, pc, nm, dir, cur, used, cost + a * a, ref bestCost, ref best);
                used[i] = false;
            }
        }

        /// <summary>The port assigned to this road end (AssignPorts) if still free, else the free port facing 'dir' most closely.</summary>
        private static bool PickPortFor(Node n, int road, bool atStart, float[] dir, out uint port)
        {
            int p;
            if (!n.Bridge && n.Assigned != null && n.Assigned.TryGetValue(road * 2 + (atStart ? 1 : 0), out p) && p < n.Used.Length && !n.Used[p])
            {
                n.Used[p] = true; port = (uint)p;
                return true;
            }
            return PickPort(n, dir, out port);
        }

        /// <summary>The intersection port facing 'dir' most closely that is still free.</summary>
        private static bool PickPort(Node n, float[] dir, out uint port)
        {
            port = 0; float best = -2; int bi = -1;
            if (n.Bridge)
            {
                float bd = float.MaxValue;
                for (int i = 0; i < n.Used.Length; i++)
                {
                    if (n.Used[i]) continue;
                    float d = D2(Caw.PortPosition(n.Intersection, (uint)i), n.P);
                    if (d < bd && d < 400f) { bd = d; bi = i; } // only this bank's port (the other is a span away)
                }
                if (bi < 0) return false;
                n.Used[bi] = true; port = (uint)bi;
                return true;
            }
            for (int i = 0; i < n.Used.Length; i++)
            {
                if (n.Used[i]) continue;
                float[] nm = Caw.PortNormal(n.Intersection, (uint)i);
                float d = nm[0] * dir[0] + nm[1] * dir[1];
                if (d > best) { best = d; bi = i; }
            }
            if (bi < 0) return false;
            n.Used[bi] = true; port = (uint)bi;
            return true;
        }

        /// <summary>Direction a road leaves its node, measured 20 m along it (the first points are only metres apart).</summary>
        private static float[] Away(List<List<float[]>> roads, End e)
        {
            List<float[]> r = roads[e.Road];
            return e.AtStart ? DirAlong(r, true, 20f) : Neg(DirAlong(r, false, 20f));
        }

        private static List<float[]> Oriented(List<float[]> r, bool keep)
        {
            List<float[]> o = new List<float[]>(r);
            if (!keep) o.Reverse();
            return o;
        }

        private static float Length(List<float[]> r) { float l = 0; for (int i = 1; i < r.Count; i++) l += (float)Math.Sqrt(D2(r[i], r[i - 1])); return l; }
        /// <summary>
        /// Intersection rotation that suits all its roads: ports sit every 90°, so pick the turn (0-90°) that minimises
        /// the squared angle between each road and its nearest port. Avoids S-bends where roads meet a junction.
        /// </summary>
        private static float FitRotation(List<List<float[]>> roads, Node n)
        {
            List<double> angs = new List<double>();
            foreach (End e in n.Ends) { float[] d = Away(roads, e); angs.Add(Math.Atan2(d[1], d[0])); }
            double best = 0, bestCost = double.MaxValue;
            for (int deg = 0; deg < 90; deg++)
            {
                double r = deg * Math.PI / 180, cost = 0;
                foreach (double a in angs)
                {
                    double diff = (a - r) % (Math.PI / 2);
                    if (diff < 0) diff += Math.PI / 2;
                    diff = Math.Min(diff, Math.PI / 2 - diff);
                    cost += diff * diff;
                }
                if (cost < bestCost) { bestCost = cost; best = r; }
            }
            return (float)best;
        }

        /// <summary>
        /// Every loose end of the pieces built: join it to the nearest free port within 20 m (moving the piece's end onto
        /// it), else, unless it's a road leaving the map, cap it with an intersection piece the way EA's worlds end roads.
        /// </summary>
        private static void Tidy(object world, TerrainInfo ti, List<object> made, List<object> fixedPieces, ref bool group, out int snapped, out int caps)
        {
            snapped = 0; caps = 0;
            List<object> loose = new List<object>(); List<uint> loosePort = new List<uint>();
            foreach (object o in made)
            {
                int pc = Caw.PortCount(o);
                for (uint p = 0; p < pc; p++) if (Caw.ConnectedTo(o, p) == null && IsSpline(o) && !fixedPieces.Contains(o)) { loose.Add(o); loosePort.Add(p); }
            }
            Log.Info("Tidy: " + loose.Count + " loose road ends");
            float capReach = 0; // distance from an intersection's middle to its ports, measured on the first cap
            for (int i = 0; i < loose.Count; i++)
            {
                object sp = loose[i]; uint port = loosePort[i];
                if (Caw.ConnectedTo(sp, port) != null) continue; // joined meanwhile
                float[] pos = Caw.PortPosition(sp, port);
                // 1. a free port of another piece within 20 m (ours, else what CAW's own snapping finds)
                object target = null; uint tport = 0; float bd = 20 * 20;
                foreach (object o in made)
                {
                    if (o == sp) continue;
                    int pc = Caw.PortCount(o);
                    for (uint p = 0; p < pc; p++)
                    {
                        if (Caw.ConnectedTo(o, p) != null) continue;
                        float d = D2(Caw.PortPosition(o, p), pos);
                        if (d < bd) { bd = d; target = o; tport = p; }
                    }
                }
                if (target == null) Caw.CanSnap(world, sp, port, out target, out tport);
                if (target != null && target != sp)
                {
                    if (Caw.MoveSplineEnd(sp, port, Caw.PortPosition(target, tport), Outward(target, tport), group)) { Caw.ConnectPorts(sp, port, target, tport); snapped++; continue; }
                }
                // 2. a dead end: cap it, unless the road leaves the map here
                float lo = ti.Origin, hi = ti.Origin + ti.WorldSize;
                float[] dir = Outward(sp, port);
                // (an end near the edge heading off the map; one running along the edge is still capped)
                if (pos[0] < lo + 40 && dir[0] < -0.3f || pos[1] < lo + 40 && dir[1] < -0.3f
                    || pos[0] > hi - 40 && dir[0] > 0.3f || pos[1] > hi - 40 && dir[1] > 0.3f) continue;
                float rot = (float)Math.Atan2(dir[1], dir[0]);
                object cap = null;
                try { cap = Caw.CreateIntersection(world, pos[0] + dir[0] * capReach, pos[1] + dir[1] * capReach, rot, group); }
                catch (Exception ex) { Log.Error("Capping a dead end failed", ex); }
                if (cap == null) continue;
                group = true;
                // the cap's port facing back along the road
                uint cp = 0; float best = 2;
                for (uint p = 0; p < Caw.PortCount(cap); p++)
                {
                    float[] nrm = Outward(cap, p);
                    float dot = nrm[0] * dir[0] + nrm[1] * dir[1]; // want −1
                    if (dot < best) { best = dot; cp = p; }
                }
                if (capReach == 0)
                {
                    float[] c0 = Caw.PortPosition(cap, 0), c2 = Caw.PortPosition(cap, Math.Min(2u, (uint)Caw.PortCount(cap) - 1));
                    capReach = (float)Math.Sqrt(D2(c0, c2)) / 2;
                    Log.Info(string.Format("  intersection pieces reach {0:F1} m from their middle to a port", capReach));
                }
                if (Caw.MoveSplineEnd(sp, port, Caw.PortPosition(cap, cp), Outward(cap, cp), group)) { Caw.ConnectPorts(sp, port, cap, cp); caps++; }
            }
            Log.Info("Tidy: joined " + snapped + ", capped " + caps);
        }

        private static bool IsSpline(object o) { return Caw.PortCount(o) == 2; }

        /// <summary>
        /// A port's normal pointing away from its road object (from the middle of all its ports outwards), whatever
        /// convention CAW uses, so roads always leave a port heading out.
        /// </summary>
        private static float[] Outward(object obj, uint port)
        {
            float[] nrm = Caw.PortNormal(obj, port), pos = Caw.PortPosition(obj, port);
            int count = Caw.PortCount(obj);
            float cx = 0, cz = 0;
            for (uint i = 0; i < count; i++) { float[] q = Caw.PortPosition(obj, i); cx += q[0]; cz += q[1]; }
            cx /= Math.Max(1, count); cz /= Math.Max(1, count);
            float ox = pos[0] - cx, oz = pos[1] - cz;
            if (ox * ox + oz * oz > 0.01f && nrm[0] * ox + nrm[1] * oz < 0) return Neg(nrm);
            return nrm;
        }

        /// <summary>Cuts the road crossing a bridge: drops its points within half a bridge length of the middle (along the
        /// crossing, within 12 m sideways) and ends the two pieces exactly at the bridge's ends.</summary>
        private static bool CutForBridge(List<List<float[]>> roads, Span sp)
        {
            // the road that runs through the bridge's middle (not one along the bank nearby)
            int pick = -1; float bd = 8 * 8;
            for (int ri = 0; ri < roads.Count; ri++)
                for (int k = 0; k + 1 < roads[ri].Count; k++)
                {
                    float d = D2(Project(roads[ri][k], roads[ri][k + 1], sp.Mid), sp.Mid);
                    if (d < bd) { bd = d; pick = ri; }
                }
            if (pick < 0) return false;
            List<float[]> r = roads[pick];
            int first = -1, last = -1;
            for (int k = 0; k < r.Count; k++)
            {
                float dx = r[k][0] - sp.Mid[0], dz = r[k][1] - sp.Mid[1];
                float along = dx * sp.Dir[0] + dz * sp.Dir[1], side = Math.Abs(-dx * sp.Dir[1] + dz * sp.Dir[0]);
                if (Math.Abs(along) < sp.Length / 2 && side < 12) { if (first < 0) first = k; last = k; }
            }
            if (first < 0) return false;
            List<float[]> before = r.GetRange(0, first), after = r.GetRange(last + 1, r.Count - last - 1);
            // which bridge end each piece meets (judged from whichever piece exists)
            float[] endBefore, endAfter;
            if (before.Count > 0) endBefore = D2(before[before.Count - 1], sp.EndA) < D2(before[before.Count - 1], sp.EndB) ? sp.EndA : sp.EndB;
            else endBefore = after.Count > 0 && D2(after[0], sp.EndA) < D2(after[0], sp.EndB) ? sp.EndB : sp.EndA;
            endAfter = endBefore == sp.EndA ? sp.EndB : sp.EndA;
            before.Add(endBefore); after.Insert(0, endAfter);
            roads.RemoveAt(pick);
            if (before.Count >= 2) roads.Add(before);
            if (after.Count >= 2) roads.Add(after);
            return true;
        }

        /// <summary>A given bridge type's object: from the catalog by instance, else by its key (the pack may be missing:
        /// then null, and the default bridge is used).</summary>
        internal static Caw.CatalogItem FindBridge(BridgeType t)
        {
            try
            {
                string hex = t.Instance.ToString("x16");
                foreach (Caw.CatalogItem c in Caw.Catalog())
                    if (c.InstanceHex == hex) { Log.Info("Bridge object: " + c.Name + " (catalog, the world type's own)"); return c; }
                Log.Info("Bridge object: " + t.Name + " by instance (not listed in the catalog panel)");
                return Caw.ObjectByInstance(t.Name, t.Instance, t.Group);
            }
            catch (Exception ex) { Log.Error("Finding bridge " + t.Name + " failed", ex); return null; }
        }

        /// <summary>A road bridge object: from the catalog if it lists one, else EA's EP7 bridges by instance.</summary>
        internal static Caw.CatalogItem FindBridge()
        {
            try
            {
                List<Caw.CatalogItem> cat = Caw.Catalog();
                for (int k = 0; k < BridgeIds.Length; k++)
                    foreach (Caw.CatalogItem c in cat)
                        if (c.InstanceHex == BridgeIds[k].ToString("x16")) { Log.Info("Bridge object: " + c.Name + " (catalog)"); return c; }
                foreach (Caw.CatalogItem c in cat)
                {
                    string nm = c.Name.ToLowerInvariant();
                    if (!c.IsTree && nm.Contains("bridge") && !nm.Contains("tunnel")) { Log.Info("Bridge object: " + c.Name + " (catalog, by name)"); return c; }
                }
            }
            catch (Exception ex) { Log.Error("Reading the catalog for bridges failed", ex); }
            Log.Info("Bridge object: " + BridgeNames[0] + " by instance (not listed in the catalog panel)");
            return Caw.ObjectByInstance(BridgeNames[0], BridgeIds[0]);
        }

        private static float[] Lerp(float[] a, float[] b, float t) { return new[] { a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t }; }

        private static float[] Project(float[] a, float[] b, float[] p)
        {
            float dx = b[0] - a[0], dy = b[1] - a[1], l = dx * dx + dy * dy;
            float t = l < 1e-6f ? 0 : Math.Max(0, Math.Min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / l));
            return new[] { a[0] + dx * t, a[1] + dy * t };
        }

        /// <summary>v made unit length, or 'fallback' if it is (nearly) zero.</summary>
        private static float[] Norm(float[] v, float[] fallback)
        {
            float l = (float)Math.Sqrt(v[0] * v[0] + v[1] * v[1]);
            return l < 1e-3f ? fallback : new[] { v[0] / l, v[1] / l };
        }

        private static float D2(float[] a, float[] b) { return (a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]); }
        private static float[] Neg(float[] v) { return new[] { -v[0], -v[1] }; }
        private static float[] Dir(float[] a, float[] b)
        {
            float dx = b[0] - a[0], dy = b[1] - a[1], l = (float)Math.Sqrt(dx * dx + dy * dy);
            return l < 1e-4f ? new[] { 1f, 0f } : new[] { dx / l, dy / l };
        }
    }
}
