using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace CawPlus
{
    /// <summary>
    /// Self-test (CAW Plus tab; Utilities toolbar as a fallback): runs every CAW call CAW Plus relies on, in the open world, and writes a pass/fail
    /// report to the log and Documents\CAWPlus\selftest.txt. Changes go into one undo step, which it undoes at the end.
    /// It also measures two things the code can't know in advance: which way CAW turns an intersection for a given
    /// rotation, and how a bridge's yaw lines up with its span.
    /// </summary>
    internal static class SelfTest
    {
        private sealed class Report
        {
            public readonly List<string> Lines = new List<string>();
            public int Pass, Fail;
            public void Ok(string what, string detail) { Pass++; Add("PASS  " + what + (detail.Length > 0 ? ": " + detail : "")); }
            public void Bad(string what, string detail) { Fail++; Add("FAIL  " + what + (detail.Length > 0 ? ": " + detail : "")); }
            public void Note(string text) { Add("      " + text); }
            private void Add(string s) { Lines.Add(s); Log.Info("Self-test " + s); }
        }

        /// <summary>Documents\CAWPlus\bridges.txt: the bridge spans the self-test measured.</summary>
        public static string BridgesFile { get { return Path.Combine(Log.Dir, "bridges.txt"); } }

        /// <summary>The lines of bridges.txt, or null if the self-test hasn't measured the bridges yet.</summary>
        public static string[] MeasuredBridges()
        {
            try { return File.Exists(BridgesFile) ? File.ReadAllLines(BridgesFile) : null; }
            catch (Exception ex) { Log.Error("Reading bridges.txt failed", ex); return null; }
        }

        public static void Run(IWin32Window owner)
        {
            object world = Caw.CurrentWorld;
            if (world == null) { MessageBox.Show(owner, "Open a world first (a copy, or a new blank world).", "CAW Plus self-test"); return; }
            if (MessageBox.Show(owner, "Run the CAW Plus self-test in this world?\n\nIt places a tree, an intersection, a road piece and (if you have one) " +
                    "a road bridge, checks them, then undoes all of it. A World Layer called 'CAW Plus self-test' is left behind (empty).\n\n" +
                    "Use a copy of a world.", "CAW Plus self-test", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            Report r = new Report();
            Log.Info("Self-test: start, world '" + Caw.WorldName(world) + "'");
            Cursor.Current = Cursors.WaitCursor;
            bool group = false;
            List<object> roads = new List<object>();

            // 1. world and terrain
            float size = 0, sea = 0;
            Try(r, "World open", delegate { return Caw.WorldName(world); });
            Try(r, "Sea level", delegate { sea = Caw.SeaLevel(world); return sea.ToString("F1") + " m"; });
            Try(r, "Height image (SaveHeightImage)", delegate
            {
                string p = Path.Combine(TerrainSampler.TempDir, "selftest_height.png");
                if (File.Exists(p)) File.Delete(p);
                if (!Caw.SaveHeightImage(world, p)) throw new Exception("CAW returned false");
                int w, h; ImageIO.PngSize(p, out w, out h);
                size = w % 2 == 1 ? w - 1 : w;
                return w + " x " + h + " px, world " + size + " m";
            });
            if (size <= 0) size = 1024;
            float tx = size * 0.25f, tz = size * 0.25f; // inside the map whether its origin is 0 or -size/2
            Try(r, "Terrain height (GetTerrainHeight)", delegate { return Caw.TerrainHeight(world, tx, tz).ToString("F1") + " m at the test spot"; });
            Try(r, "Paint layers", delegate
            {
                object terr = Caw.Terrain(world);
                int n = Caw.LayerCount(terr);
                string p = Path.Combine(TerrainSampler.TempDir, "selftest_layer0.png");
                if (File.Exists(p)) File.Delete(p);
                bool ok = Caw.ExportLayer(world, 0, p) && File.Exists(p);
                if (!ok) throw new Exception(n + " layers, but exporting layer 0 failed");
                return n + " layers, base '" + Caw.LayerName(terr, 0) + "', export OK";
            });

            // 2. catalog and World Layer
            Caw.CatalogItem tree = null;
            Try(r, "Object catalog", delegate
            {
                List<Caw.CatalogItem> cat = Caw.Catalog();
                int trees = 0;
                foreach (Caw.CatalogItem c in cat) if (c.IsTree) { trees++; if (tree == null) tree = c; }
                if (trees == 0) throw new Exception(cat.Count + " items but no trees");
                return cat.Count + " items, " + trees + " trees";
            });
            Try(r, "World Layer (create/select)", delegate
            {
                if (!Caw.UseLayer(world, "CAW Plus self-test")) throw new Exception("could not create or select it");
                return "'CAW Plus self-test' is active";
            });

            // 3. objects
            if (tree != null)
                Try(r, "Place a tree", delegate
                {
                    float px = tx + 30, pz = tz + 30;
                    object o = Caw.Place(tree, px, pz, 0.5f, 0, group);
                    if (o == null) throw new Exception("CAW refused '" + tree.Name + "'");
                    group = true;
                    object pos = Caw.GetProperty(o, "Position");
                    float ox = Convert.ToSingle(Caw.GetField(pos, "x")), oz = Convert.ToSingle(Caw.GetField(pos, "z"));
                    float d = (float)Math.Sqrt((ox - px) * (ox - px) + (oz - pz) * (oz - pz));
                    if (d > 1) throw new Exception(string.Format("asked ({0:F1}, {1:F1}), got ({2:F1}, {3:F1})", px, pz, ox, oz));
                    return "'" + tree.Name + "' at the right spot";
                });

            // 4. roads: intersection, which way CAW turns it, a road piece, connect, move an end
            object it = null;
            Try(r, "Road texture set", delegate
            {
                object terr = Caw.Terrain(world);
                uint sets = Convert.ToUInt32(Caw.Call(terr, "GetRoadTextureArraySize"));
                ushort idx = Convert.ToUInt16(Caw.Call(terr, "GetRoadTextureIdx"));
                if (sets == 0) throw new Exception("the world has no road texture sets");
                return sets + " set(s), selected " + idx + (idx >= sets ? " (none valid: Build roads selects set 0)" : "");
            });
            const float rot = 0.3f; // about 17°
            Try(r, "Intersection (CmdCreateRoadIntersection)", delegate
            {
                it = Caw.CreateIntersection(world, tx, tz, rot, group);
                if (it == null) throw new Exception("CAW returned null");
                group = true; roads.Add(it);
                return Caw.PortCount(it) + " ports";
            });
            if (it != null)
            {
                Try(r, "Intersection rotation direction", delegate
                {
                    // the port normal angle closest to +rot or -rot (mod 90°) tells which way CAW turns
                    double plus = 9, minus = 9, reach = 0, outwardDot = 0;
                    for (uint p = 0; p < Caw.PortCount(it); p++)
                    {
                        float[] n = Caw.PortNormal(it, p), pp = Caw.PortPosition(it, p);
                        double a = Math.Atan2(n[1], n[0]);
                        plus = Math.Min(plus, AngMod90(a - rot)); minus = Math.Min(minus, AngMod90(a + rot));
                        float ox = pp[0] - tx, oz = pp[1] - tz;
                        reach += Math.Sqrt(ox * ox + oz * oz); outwardDot += n[0] * ox + n[1] * oz;
                    }
                    reach /= Math.Max(1, Caw.PortCount(it));
                    r.Note(string.Format("ports {0:F1} m from the middle; normals point {1}", reach, outwardDot >= 0 ? "outwards" : "inwards"));
                    if (plus <= minus) return "matches CAW Plus's convention (junction fit and caps turn the right way)";
                    throw new Exception("OPPOSITE to CAW Plus's convention: junction fit and caps turn the wrong way (flip the sign in Caw.CreateIntersection)");
                });
                Try(r, "Road piece + connect (CmdCreateRoadSpline, CmdConnectRoadPorts)", delegate
                {
                    float[] a = Caw.PortPosition(it, 0), n = Caw.PortNormal(it, 0);
                    float[] b = { a[0] + n[0] * 40, a[1] + n[1] * 40 };
                    object sp = Caw.CreateSpline(world, a, new[] { a[0] + n[0] * 13, a[1] + n[1] * 13 }, new[] { b[0] - n[0] * 13, b[1] - n[1] * 13 }, b, group);
                    if (sp == null) throw new Exception("CAW refused a straight 40 m piece");
                    group = true; roads.Add(sp);
                    Caw.ConnectPorts(it, 0, sp, 0);
                    if (Caw.ConnectedTo(sp, 0) == null) throw new Exception("created, but GetConnectedObject says port 0 isn't joined");
                    if (Caw.ConnectedTo(sp, 1) != null) throw new Exception("its free end reports a connection");
                    // move the free end 3 m sideways
                    float[] nb = { b[0] - n[1] * 3, b[1] + n[0] * 3 };
                    if (!Caw.MoveSplineEnd(sp, 1, nb, new[] { -n[0], -n[1] }, true)) throw new Exception("moving its end failed");
                    float[] e = Caw.PortPosition(sp, 1);
                    float d = (float)Math.Sqrt((e[0] - nb[0]) * (e[0] - nb[0]) + (e[1] - nb[1]) * (e[1] - nb[1]));
                    if (d > 0.5f) throw new Exception(string.Format("end moved, but {0:F1} m off", d));
                    object found; uint fp;
                    bool snap = Caw.CanSnap(world, sp, 1, out found, out fp);
                    return "joined, free end detected, end moved; CAW's snap test ran (" + (snap ? "found a port" : "nothing in reach") + ")";
                });
            }

            // 5. bridge: placed along a known direction, then compare its span with that direction
            Try(r, "Road bridge (object + CmdCreateRoadBridge)", delegate
            {
                Caw.CatalogItem br = RoadBuilder.FindBridge();
                if (br == null) throw new Exception("no road bridge object found");
                const double dirAngle = 0.5; // the road direction we want
                float yaw = -(float)dirAngle - (float)Math.PI / 2;
                float bx = tx + 120, bz = tz + 40;
                object spline = Caw.PlaceBridge(br, bx, bz, Caw.TerrainHeight(world, bx, bz), yaw, group);
                group = true;
                if (spline == null) throw new Exception("'" + br.Name + "' placed no bridge piece (expansion missing, or not a road bridge)");
                roads.Add(spline);
                float[] p0 = Caw.PortPosition(spline, 0), p1 = Caw.PortPosition(spline, 1);
                double axis = Math.Atan2(p1[1] - p0[1], p1[0] - p0[0]);
                double off = Math.Abs(((axis - dirAngle) % Math.PI + Math.PI * 1.5) % Math.PI - Math.PI / 2) * 180 / Math.PI;
                double span = Math.Sqrt((p1[0] - p0[0]) * (p1[0] - p0[0]) + (p1[1] - p0[1]) * (p1[1] - p0[1]));
                if (off > 10) throw new Exception(string.Format("'{0}' spans {1:F1} m but is {2:F0}° off the road direction (bridge yaw needs fixing)", br.Name, span, off));
                return string.Format("'{0}' spans {1:F1} m, {2:F0}° off the road direction", br.Name, span, off);
            });

            // 5b. every EA bridge type: its span (port to port) in CAW, kept in bridges.txt so the planner and Build roads
            //     use each World type's own bridges at their real lengths; a type whose pack isn't installed, or that is
            //     not a road bridge, is written with length 0
            Try(r, "Measure bridges (every EA bridge type)", delegate
            {
                List<string> lines = new List<string>();
                lines.Add("# EA bridge spans measured in CAW by the self-test: instance|name|metres port to port (0 = not placed or not a road bridge)");
                int measured = 0;
                string summary = "";
                foreach (BridgeType t in Bridges.All)
                {
                    float len = 0;
                    try
                    {
                        Caw.CatalogItem item = RoadBuilder.FindBridge(t);
                        object spline = item != null ? Caw.PlaceBridge(item, tx + 120, tz + 40, Caw.TerrainHeight(world, tx + 120, tz + 40), 0, group) : null;
                        group = true;
                        if (spline != null)
                        {
                            roads.Add(spline);
                            float[] p0 = Caw.PortPosition(spline, 0), p1 = Caw.PortPosition(spline, 1);
                            len = (float)Math.Sqrt((p1[0] - p0[0]) * (p1[0] - p0[0]) + (p1[1] - p0[1]) * (p1[1] - p0[1]));
                            measured++;
                        }
                    }
                    catch (Exception ex) { Log.Error("Measuring bridge " + t.Name + " failed", ex); }
                    lines.Add(t.Instance.ToString("x16") + "|" + t.Name + "|" + len.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                    summary += (summary.Length > 0 ? ", " : "") + t.Name + " " + (len > 0 ? len.ToString("F1") + " m" : "-");
                }
                File.WriteAllLines(BridgesFile, lines.ToArray());
                Bridges.ApplyMeasured(lines.ToArray());
                return measured + " of " + Bridges.All.Length + " placed: " + summary;
            });

            // 6. undo everything in one step
            Try(r, "Undo (one step removes the test objects)", delegate
            {
                if (!group) return "nothing to undo";
                Caw.Call(Caw.MainForm, "CallUndo");
                int still = 0;
                foreach (object o in roads)
                {
                    object v = null;
                    try { v = Caw.GetProperty(o, "Visible"); } catch { }
                    if (v != null && Convert.ToBoolean(v)) still++;
                }
                if (still > 0) throw new Exception(still + " road object(s) still visible after one undo");
                return "all gone";
            });

            Cursor.Current = Cursors.Default;
            string file = Path.Combine(Log.Dir, "selftest.txt");
            try
            {
                List<string> all = new List<string>();
                all.Add("CAW Plus self-test " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ", world '" + Caw.WorldName(world) + "'");
                all.AddRange(r.Lines);
                all.Add(r.Pass + " passed, " + r.Fail + " failed");
                File.WriteAllLines(file, all.ToArray());
            }
            catch (Exception ex) { Log.Error("Writing selftest.txt failed", ex); }
            Log.Info("Self-test: " + r.Pass + " passed, " + r.Fail + " failed");
            MessageBox.Show(owner, string.Join("\n", r.Lines.ToArray()) + "\n\n" + r.Pass + " passed, " + r.Fail + " failed.\nSaved to " + file,
                "CAW Plus self-test", MessageBoxButtons.OK, r.Fail > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        private delegate string Step();

        private static void Try(Report r, string what, Step step)
        {
            try { r.Ok(what, step() ?? ""); }
            catch (Exception ex)
            {
                Exception e = ex is System.Reflection.TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                r.Bad(what, e.Message);
                Log.Error("Self-test step '" + what + "' failed", e);
            }
        }

        /// <summary>Distance of an angle from the nearest multiple of 90°, in radians.</summary>
        private static double AngMod90(double a)
        {
            double q = Math.PI / 2, m = a % q;
            if (m < 0) m += q;
            return Math.Min(m, q - m);
        }
    }
}
