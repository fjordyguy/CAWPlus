using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace CawPlus
{
    /// <summary>
    /// Generate Roads (Roads and Trees toolbar), its own step after the terrain (the tester, 2026-10-24): reads the open
    /// world's land, plans roads and lots for a World type over it (RoadPlanner, in CAWPlusHelper when available) with
    /// a preview, then Build roads (optionally smoothing the ground under them first) and Place lots (fitted to the
    /// roads CAW built, optionally levelling their pads). Each is its own undo step.
    /// </summary>
    public sealed class RoadsForm : Form
    {
        private ComboBox cbType;
        private NumericUpDown nudSeed, nudEdge;
        private TrackBar tbGrade, tbVariety;
        private CheckBox chkSmooth, chkCawGrade, chkPads, chk3D, chkDesign, chkShelves;
        private ComboBox cbLook, cbPaint;
        private NumericUpDown nudBrush;
        private PlanMap plan;            // the painted plan (#171), per world
        private string planPath;
        private float[] drawnRoads; // the World Builder's roads (#173), packed as RoadSettings.DrawnRoads; null = plan the town
        private Bitmap shownMap;         // map2D with the plan drawn over it
        private Button btnRead, btnBuild, btnLots;
        private PictureBox picture;
        private ProgressBar progress;
        private Label status, info;
        private Timer planTimer;
        private bool busy, applying;
        private MapView view;

        private List<WorldPreset> presets;
        private TerrainInfo ti;          // the open world's land, as read
        private Grid coarse;             // its heights on the planner's 8 m grid
        private RoadNet net;             // the layout shown
        private RoadNet builtNet;        // the layout Build roads laid, for Place lots
        private Bitmap map2D;
        private Work.Stop planStop;
        private bool planRunning, planPending;
        private const int RoadTries = 12;
        private delegate void HeightChange(Grid h, float cell);

        public RoadsForm()
        {
            SuspendLayout();
            Text = "Generate Roads  (CAW Plus)";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(900, 630);
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 8.25f);
            int x = 10, y = 10, colW = 360;

            GroupBox gb = new GroupBox(); gb.Text = "Roads"; gb.SetBounds(x, y, colW, 406);
            int ry = 18;
            Label lt = new Label(); lt.Text = "World type"; lt.SetBounds(10, ry + 4, 64, 16); gb.Controls.Add(lt);
            cbType = new ComboBox(); cbType.DropDownStyle = ComboBoxStyle.DropDownList; cbType.SetBounds(76, ry, 274, 20);
            presets = WorldPreset.LoadAll();
            foreach (WorldPreset w in presets) cbType.Items.Add(w);
            cbType.SelectedIndexChanged += delegate { ApplyPreset(); };
            new ToolTip().SetToolTip(cbType, "The town is laid out the way this Sims 3 world's is: its road length, junctions, lots and kind of place.");
            gb.Controls.Add(cbType);
            ry += 30;
            Label ls = new Label(); ls.Text = "Seed"; ls.SetBounds(10, ry + 4, 34, 16); gb.Controls.Add(ls);
            nudSeed = Num(gb, 46, ry, 0, 999999, 1);
            Button bRand = new Button(); bRand.Text = "New layout"; bRand.SetBounds(122, ry - 1, 84, 23);
            bRand.Click += delegate { nudSeed.Value = new Random().Next(1, 999999); };
            gb.Controls.Add(bRand);
            ry += 30;
            Label le = new Label(); le.Text = "Roads out of world"; le.SetBounds(10, ry + 4, 108, 16); gb.Controls.Add(le);
            nudEdge = Num(gb, 120, ry, 0, 3, 2); nudEdge.Width = 50;
            new ToolTip().SetToolTip(nudEdge, "Roads that leave the map (for looks: they run under the distant terrain).");
            ry += 28;
            Label lk = new Label(); lk.Text = "Road look"; lk.SetBounds(10, ry + 4, 64, 16); gb.Controls.Add(lk);
            cbLook = new ComboBox(); cbLook.DropDownStyle = ComboBoxStyle.DropDownList; cbLook.SetBounds(76, ry, 200, 20);
            new ToolTip().SetToolTip(cbLook, "The road texture set Build roads uses (CAW's Road panel list). Roads already built keep theirs.");
            gb.Controls.Add(cbLook);
            Button bLook = new Button(); bLook.Text = "Add..."; bLook.SetBounds(282, ry - 1, 68, 23);
            bLook.Click += delegate { AddRoadLook(); };
            new ToolTip().SetToolTip(bLook, "Adds a road look with CAW's own road texture dialog (street, detail, sidewalk, corner and crosswalk pictures).");
            gb.Controls.Add(bLook);
            ry += 30;
            tbVariety = Slider(gb, "Neighbourhood variety", 60, ref ry);
            tbGrade = Slider(gb, "Max steepness", 40, ref ry);
            new ToolTip().SetToolTip(tbGrade, "The steepest slope a road may climb: 4 % (left) to 20 % (right).\n" +
                "The World type sets it to that EA world's own roads (9 in 10 of its road metres are no steeper).\n" +
                "Lower = roads wind round hills and steep ground stays empty; higher = roads go straight up.");
            chkSmooth = new CheckBox(); chkSmooth.Text = "Smooth ground under roads before building"; chkSmooth.Checked = true; chkSmooth.SetBounds(10, ry, colW - 20, 20);
            new ToolTip().SetToolTip(chkSmooth, "Build roads first levels a narrow strip along each planned road (the world's heights are backed up;\n" +
                "Generate Terrain's 'Undo terrain' puts them back), so CAW can lay the roads without bumps.");
            gb.Controls.Add(chkSmooth);
            ry += 22;
            chkShelves = new CheckBox(); chkShelves.Text = "Neighbourhoods on their own shelves (heights)"; chkShelves.Checked = true; chkShelves.SetBounds(10, ry, colW - 20, 20);
            new ToolTip().SetToolTip(chkShelves, "Before smoothing the ground under the roads, each neighbourhood gets its own flat shelf, those further out\n" +
                "higher (by the World type's measured step: Sunset Valley ~25 m, Appaloosa Plains ~12 m), with banks between,\n" +
                "as EA's towns step up their hillsides. A neighbourhood already higher keeps its ground.");
            gb.Controls.Add(chkShelves);
            ry += 22;
            chkCawGrade = new CheckBox(); chkCawGrade.Text = "Grade roads after building (CAW's grading tool)"; chkCawGrade.Checked = true; chkCawGrade.SetBounds(10, ry, colW - 20, 20);
            gb.Controls.Add(chkCawGrade);
            ry += 22;
            chkPads = new CheckBox(); chkPads.Text = "Level lot pads (terraces)"; chkPads.Checked = true; chkPads.SetBounds(10, ry, colW - 20, 20);
            new ToolTip().SetToolTip(chkPads, "Place lots first levels the ground under each lot to its road, blended back into the land,\n" +
                "so lots on a hillside step up in terraces as in EA's worlds (heights backed up first).");
            gb.Controls.Add(chkPads);
            ry += 22;
            chkDesign = new CheckBox(); chkDesign.Text = "Town designer (EA-style layout, trial)"; chkDesign.Checked = false; chkDesign.SetBounds(10, ry, colW - 20, 20);
            new ToolTip().SetToolTip(chkDesign, "Lays the town as EA's are: the centre by the coast, a small grid of uniform blocks round a park,\n" +
                "a road along the shore, a ring of roads round the centre, roads out that follow the land and short streets\n" +
                "off them, with green between. The status line says what was decided and why. Off = the older planner.");
            gb.Controls.Add(chkDesign);
            Controls.Add(gb);
            y += 412;

            btnRead = new Button(); btnRead.Text = "Read terrain"; btnRead.SetBounds(x, y, 88, 28);
            btnRead.Click += delegate { ReadTerrain(); };
            new ToolTip().SetToolTip(btnRead, "Reads the open world's land again (after changing the terrain) and plans over it.");
            btnBuild = new Button(); btnBuild.Text = "Build roads"; btnBuild.SetBounds(x + 91, y, 88, 28); btnBuild.Font = new Font(Font, FontStyle.Bold);
            btnBuild.Click += delegate { BuildRoads(); };
            btnLots = new Button(); btnLots.Text = "Place lots"; btnLots.SetBounds(x + 182, y, 88, 28);
            btnLots.Click += delegate { PlaceLots(); };
            new ToolTip().SetToolTip(btnLots, "After Build roads: places the planned lots, each lined up with a straight stretch of the road CAW built,\n" +
                "residential in 'Residential Lots' and community in 'Commercial Lots'. Its own undo step.");
            Controls.Add(btnRead); Controls.Add(btnBuild); Controls.Add(btnLots);
            y += 34;
            progress = new ThemedProgress(); progress.SetBounds(x, y, colW, 14); Controls.Add(progress);
            y += 18;
            status = new Label(); status.SetBounds(x, y, colW, 630 - y - 4); Controls.Add(status);

            picture = new PictureBox(); picture.SetBounds(x + colW + 18, 10, 512, 512);
            picture.SizeMode = PictureBoxSizeMode.Zoom; picture.BackColor = Color.FromArgb(40, 40, 40); picture.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(picture);
            chk3D = new CheckBox(); chk3D.Text = "3D view"; chk3D.SetBounds(x + colW + 18, 526, 80, 22);
            chk3D.CheckedChanged += delegate { Redraw(); };
            Button bTurn = new Button(); bTurn.Text = "Turn"; bTurn.SetBounds(x + colW + 102, 525, 60, 23);
            CheckBox chkExag = new CheckBox(); chkExag.Text = "Exaggerate heights (x2)"; chkExag.SetBounds(x + colW + 172, 526, 170, 22);
            view = new MapView(picture, chk3D, chkExag);
            bTurn.Click += delegate { view.Turn(); };
            new ToolTip().SetToolTip(chk3D, "The plan in 3D at true height. Drag to orbit, right-drag to pan, wheel to zoom, double-click to reset.");
            Controls.Add(chk3D); Controls.Add(bTurn); Controls.Add(chkExag);
            // the plan painter (#171): paint where the town, houses, countryside, forest and main roads go
            Label lp = new Label(); lp.Text = "Paint plan"; lp.SetBounds(x + colW + 18, 556, 62, 16); Controls.Add(lp);
            cbPaint = new ComboBox(); cbPaint.DropDownStyle = ComboBoxStyle.DropDownList; cbPaint.SetBounds(x + colW + 82, 552, 130, 20);
            cbPaint.Items.Add("(off)"); foreach (string nm in PlanMap.Names) cbPaint.Items.Add(nm);
            cbPaint.SelectedIndex = 0;
            new ToolTip().SetToolTip(cbPaint, "Paint on the 2D map where things go: the planner builds to it.\n" +
                "Town centre: the downtown. Houses: neighbourhoods and side streets only there. Countryside: farm lanes.\n" +
                "Forest, Park, Keep clear: no streets or lots (Auto-Place plants the forest). Main road: drawn roads are followed.\n" +
                "Unpainted ground is planned as usual. Kept per world.");
            Controls.Add(cbPaint);
            Label lb = new Label(); lb.Text = "Brush"; lb.SetBounds(x + colW + 220, 556, 36, 16); Controls.Add(lb);
            nudBrush = Num(this, x + colW + 258, 552, 8, 200, 40); nudBrush.Width = 52;
            new ToolTip().SetToolTip(nudBrush, "Brush radius in metres.");
            Button bClear = new Button(); bClear.Text = "Clear plan"; bClear.SetBounds(x + colW + 318, 551, 80, 23);
            bClear.Click += delegate { if (plan != null) { plan.Clear(); SavePlan(); Redraw(); Schedule(); } };
            Controls.Add(bClear);
            cbPaint.SelectedIndexChanged += delegate
            {
                bool on = cbPaint.SelectedIndex > 0;
                if (on && chk3D.Checked) chk3D.Checked = false; // painting is on the 2D map
                view.Painter = on ? new MapView.PaintFn(PaintPlan) : null;
            };
            view.PaintDone += delegate { SavePlan(); Schedule(); };
            info = new Label(); info.SetBounds(x + colW + 18, 580, 512, 46); Controls.Add(info);

            planTimer = new Timer(); planTimer.Interval = 300;
            planTimer.Tick += delegate { planTimer.Stop(); Plan(); };
            nudSeed.ValueChanged += delegate { Schedule(); };
            nudEdge.ValueChanged += delegate { Schedule(); };
            tbVariety.ValueChanged += delegate { Schedule(); };
            tbGrade.ValueChanged += delegate { Schedule(); };
            chkDesign.CheckedChanged += delegate { Schedule(); };

            // the World type Generate Terrain used, else the first
            int pick = 0;
            for (int i = 0; i < presets.Count; i++)
                if (WorldPreset.Inspiration != null && WorldPreset.Short(presets[i].Name) == WorldPreset.Inspiration) pick = i;
            applying = true;
            if (presets.Count > 0) cbType.SelectedIndex = pick;
            applying = false;
            if (presets.Count > 0) ApplyPreset();
            Shown += delegate { ReadTerrain(); };
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            Theme.Apply(this);
        }

        private NumericUpDown Num(Control parent, int x, int y, int min, int max, int value)
        {
            NumericUpDown n = new NumericUpDown(); n.Minimum = min; n.Maximum = max; n.Value = value; n.SetBounds(x, y, 70, 20);
            parent.Controls.Add(n);
            return n;
        }

        private TrackBar Slider(GroupBox gb, string label, int value, ref int y)
        {
            Label l = new Label(); l.Text = label; l.SetBounds(10, y + 4, 130, 16); gb.Controls.Add(l);
            // (fixed height: an auto-sized TrackBar is ~45 px tall and the next row covered half of it)
            TrackBar t = new ThemedSlider(); t.AutoSize = false; t.Minimum = 0; t.Maximum = 100; t.TickFrequency = 10; t.Value = value; t.SetBounds(140, y, 210, 28);
            gb.Controls.Add(t);
            y += 32;
            return t;
        }

        private WorldPreset Preset { get { return cbType.SelectedItem as WorldPreset; } }

        // ---------------- road look ----------------

        private void LoadRoadLooks(object world, int select)
        {
            cbLook.Items.Clear();
            if (world == null) return;
            try
            {
                int sel;
                List<string> sets = Caw.RoadSets(world, out sel);
                foreach (string n in sets) cbLook.Items.Add(n);
                if (select < 0) select = sel >= 0 ? sel : 0;
                if (cbLook.Items.Count > 0) cbLook.SelectedIndex = Math.Min(select, cbLook.Items.Count - 1);
                Log.Info("Road looks: " + string.Join(", ", sets.ToArray()) + "; active " + sel);
            }
            catch (Exception ex) { Log.Error("Reading the road looks failed", ex); }
        }

        private void AddRoadLook()
        {
            if (busy || Injector.BusyElsewhere(this)) return;
            object world = Caw.CurrentWorld;
            if (world == null) { Status("Open a world first."); return; }
            try
            {
                string name;
                int idx = Caw.AddRoadSetWithDialog(this, world, out name);
                if (idx < 0) { Status("No road look added."); return; }
                LoadRoadLooks(world, idx);
                Status("Road look '" + name + "' added; Build roads will use it.");
            }
            catch (Exception ex) { Log.Error("Adding a road look failed", ex); Status("Adding a road look failed: " + ex.Message); }
        }

        private void ApplyPreset()
        {
            WorldPreset w = Preset;
            if (w == null || applying) return;
            applying = true;
            try
            {
                nudEdge.Value = Math.Max(nudEdge.Minimum, Math.Min(nudEdge.Maximum, w.EdgeRoads));
                tbVariety.Value = (int)Math.Max(0, Math.Min(100, w.Variety));
                tbGrade.Value = (int)Math.Max(0, Math.Min(100, w.Steepness));
            }
            finally { applying = false; }
            Schedule();
        }

        private void Schedule() { if (applying || coarse == null) return; planTimer.Stop(); planTimer.Start(); }

        /// <summary>Where Generate Terrain put the town (a river fork's point, #139), saved with the world's backups; null if none.</summary>
        private static float[] TownHint()
        {
            try
            {
                object world = Caw.CurrentWorld;
                if (world == null) return null;
                string f = Path.Combine(Caw.WorldDir(Path.Combine(Log.Dir, "Backups"), world), "town_hint.txt");
                if (!File.Exists(f)) return null;
                string[] t = File.ReadAllText(f).Trim().Split(' ');
                return new[] { float.Parse(t[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(t[1], System.Globalization.CultureInfo.InvariantCulture) };
            }
            catch { return null; }
        }

        /// <summary>The planner's settings from the World type and the controls (as Generate Terrain used to set them).</summary>
        /// <summary>The road bridges of the World type (with the spans measured in CAW, bridges.txt, applied).</summary>
        private List<BridgeType> WorldBridges()
        {
            Bridges.ApplyMeasured(SelfTest.MeasuredBridges());
            return Bridges.ForWorld(Preset != null ? WorldPreset.Key(Preset.Name) : "");
        }

        private RoadSettings Cfg()
        {
            WorldPreset w = Preset;
            RoadSettings r = new RoadSettings();
            r.Seed = (int)nudSeed.Value;
            r.EdgeRoads = (int)nudEdge.Value;
            r.Variety = tbVariety.Value;
            r.MaxGrade = 4 + tbGrade.Value * 0.16f;
            // (EA district layouts, EA town centres and the puzzle of EA road pieces are no longer offered: the tester found the
            // towns messy with them; the planner lays the town from the World type's figures)
            r.Templates = 0; r.TemplateCentre = false; r.Puzzle = false;
            r.Plan = plan != null && coarse != null && plan.N == coarse.W ? plan.ToSettings() : null;
            r.SeaLevel = ti != null ? ti.WaterLevel : 10;
            r.Footpaths = false;
            if (w != null)
            {
                r.Targets = w.RoadTargets; r.Town = w.Town; r.Grid = w.Grid; r.Profile = w.Profile; r.Lots = w.LotFigures; r.LotsLand = w.Terrain;
                r.LotsWorldM = w.RoadTargets != null ? w.RoadTargets.WorldM : 0;
                r.Downtown = w.DowntownBlocks > 0; r.DowntownBlocks = Math.Max(1, w.DowntownBlocks);
                r.Neighbourhoods = w.Neighbourhoods; r.CommunityLots = w.Lots;
                r.FootpathKm = Math.Max(0, w.FootpathKm); r.ClimbCost = w.ClimbCost; r.TargetKm = w.RoadKm;
                r.TownDesign = w.TownDesign;
                r.DeadEndShare = w.DeadEndShare; r.BuildSlope = w.BuildSlope; r.Character = w.Character; r.WorldType = w.Name; r.RoadTop = w.RoadTop;
                if (w.Coast == CoastType.Volcano) r.Ring = new[] { 0.5f, 0.5f, 0.30f }; // a road round the atoll
                r.IslandTowns = TerrainGen.IsIslandCoast(w.Coast); // islands no bridge reaches get streets of their own
            }
            r.BridgeSpans = Bridges.Spans(WorldBridges()); // the world type's own bridges set how wide a crossing can be
            r.CentreHint = TownHint();
            r.DrawnRoads = drawnRoads; // a World Builder world: only the roads drawn, nothing added
            r.Design = chkDesign.Checked; // the town designer (TownDesigner.cs)
            return r;
        }

        /// <summary>The roads drawn in the World Builder for this world (packed n, x0, y0 ... per line), or null if it has none.</summary>
        private static float[] DrawnRoads(string sketchPath, float worldSize)
        {
            try
            {
                if (!File.Exists(sketchPath)) return null;
                WorldSketch sk = WorldSketch.FromText(File.ReadAllText(sketchPath));
                if (sk == null) return null;
                List<float> d = new List<float>();
                foreach (List<float[]> line in sk.Lines(new[] { SketchTile.Road }, new[] { SketchTile.Bridge }, worldSize, true))
                {
                    d.Add(line.Count);
                    foreach (float[] p in line) { d.Add(p[0]); d.Add(p[1]); }
                }
                if (d.Count > 0) Log.Info("Generate Roads: using the " + d.Count + "-number road drawing from the World Builder (" + sketchPath + ")");
                return d.Count > 0 ? d.ToArray() : null;
            }
            catch (Exception ex) { Log.Error("Reading the World Builder sketch failed", ex); return null; }
        }

        // ---------------- reading the land ----------------

        private void ReadTerrain()
        {
            if (busy || Injector.BusyElsewhere(this)) return;
            object world = Caw.CurrentWorld;
            if (world == null) { Status("Open a world first."); return; }
            SetBusy(true);
            try
            {
                ti = TerrainSampler.Analyze(world, Caw.LayerCount(Caw.Terrain(world)), OnProgress);
                LoadRoadLooks(world, -1);
                coarse = Coarse(ti);
                planPath = Path.Combine(Caw.WorldDir(Path.Combine(Log.Dir, "Worlds"), world), "plan.png");
                string wdir = Path.GetDirectoryName(planPath);
                // the World Builder's roads and plan only while the world still has the land it made (#173)
                bool built = WorldSketch.AppliedTo(wdir, ti.Heights);
                plan = PlanMap.Load(planPath, coarse.W);
                if (plan != null && File.Exists(Path.Combine(wdir, "sketch.txt")) && !built)
                {
                    Log.Info("Generate Roads: the World Builder's plan is for other land (not applied to this one), not used");
                    plan = null;
                }
                drawnRoads = built ? DrawnRoads(Path.Combine(wdir, "sketch.txt"), ti.WorldSize) : null;
                SetBusy(false);
                Status(string.Format("Read the world: {0:F0} m, sea level {1:F1} m. {2}Planning roads...", ti.WorldSize, ti.WaterLevel, ti.Notes));
                Plan();
            }
            catch (Exception ex) { Log.Error("Generate Roads: reading the terrain failed", ex); SetBusy(false); Status("Reading the terrain failed: " + ex.Message); }
        }

        /// <summary>The planner works on exact 8 m cells: the paint-map heights (any metres per pixel) resampled to that spacing.</summary>
        internal static Grid Coarse(TerrainInfo ti)
        {
            float step = TerrainGen.CoarseCell / ti.CellMeters;
            // smoothed (sigma 10 m): the world's rises and mounds (EA-sized, 24 m across) aren't slopes a road must climb;
            // Build roads grades the ground under each road anyway
            Grid soft = ti.Heights.Blur(Math.Max(step * 0.4f, 10f / ti.CellMeters));
            int cn = (int)(ti.WorldSize / TerrainGen.CoarseCell) + 1;
            Grid c = new Grid(cn, cn);
            for (int j = 0; j < cn; j++)
                for (int i = 0; i < cn; i++) c[i, j] = TerrainGen.Sample(soft, i * step, j * step);
            // ponds above the sea are water to the planner too (it only knows the sea level): under the sea here (#141)
            if (ti.WaterOffset != null)
                for (int j = 0; j < cn; j++)
                    for (int i = 0; i < cn; i++)
                    {
                        float off = TerrainGen.Sample(ti.WaterOffset, i * step, j * step);
                        if (off > 0.5f && c[i, j] < ti.WaterLevel + off) c[i, j] = ti.WaterLevel - 1;
                    }
            return c;
        }

        // ---------------- planning (worker thread) ----------------

        private void Plan()
        {
            if (busy || coarse == null) return;
            if (planRunning) { planPending = true; if (planStop != null) planStop.Requested = true; return; }
            RoadSettings rs = Cfg();
            Grid h = coarse; float sea = ti.WaterLevel;
            Work.Stop stop = new Work.Stop();
            planStop = stop; planRunning = true;
            UseWaitCursor = true;
            Status("Planning roads...");
            Thread t = new Thread(delegate ()
            {
                RoadNet n = null; RoadChecks.Result checks = null; Bitmap bmp = null; Exception error = null;
                try
                {
                    n = Helper.PlanRoads(h, rs, RoadTries, stop, out checks);
                    if (n != null && !stop.Requested) bmp = TerrainGenForm.Render(h, TerrainGen.CoarseCell, sea, n, 512);
                }
                catch (Exception ex) { error = ex; }
                try { BeginInvoke(new MethodInvoker(delegate { PlanDone(stop, n, checks, bmp, error); })); }
                catch (Exception) { if (bmp != null) bmp.Dispose(); }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void PlanDone(Work.Stop stop, RoadNet n, RoadChecks.Result checks, Bitmap bmp, Exception error)
        {
            planRunning = false;
            UseWaitCursor = busy;
            if (planPending || stop.Requested || n == null)
            {
                if (bmp != null) bmp.Dispose();
                if (error != null) Log.Error("Road plan failed", error);
                planPending = false;
                if (n == null && error != null) Status("Planning failed: " + error.Message); else Plan();
                return;
            }
            net = n;
            Bitmap old = map2D; map2D = bmp; Redraw();
            if (old != null && !ReferenceEquals(old, picture.Image)) old.Dispose();
            info.Text = string.Format("Roads: {0:F1} km in {1} pieces, {2} junctions, {3} bridge(s); lots: {4} of {5}.", n.TotalKm, n.Roads.Count,
                n.Junctions.Count, n.Bridges.Count, n.Lots.Count, n.LotsWanted) + (checks != null ? " " + checks : "")
                + (n.TemplatesUsed.Count > 0 ? "\nEA layouts: " + string.Join(", ", n.TemplatesUsed.ToArray()) + "." : "");
            Log.Info("Generate Roads: " + info.Text.Replace('\n', ' ') + " Clean-up: " + n.CleanupSummary() + ".");
            if (n.Design != null && n.Design.Count > 0)
            {
                foreach (string d in n.Design) Log.Info("Town designer: " + d);
                Status("Layout ready (town designer: " + n.Design.Count + " decisions, each in the log). 'New layout' tries another; 'Build roads' lays it in CAW.");
            }
            else Status("Layout ready. 'New layout' tries another; 'Build roads' lays it in CAW.");
        }

        private void Redraw()
        {
            if (map2D == null) return;
            Bitmap old = shownMap;
            shownMap = plan != null && plan.Any ? WithPlan(map2D, plan) : null;
            view.SetMap(shownMap ?? map2D, coarse, TerrainGen.CoarseCell, ti != null ? ti.WaterLevel : 0, null);
            if (old != null) old.Dispose();
        }

        // ---------------- the plan painter (#171) ----------------

        private static readonly Color[] PlanColours = {
            Color.Transparent, Color.FromArgb(150, 230, 60, 60), Color.FromArgb(130, 250, 200, 60), Color.FromArgb(120, 200, 230, 120),
            Color.FromArgb(140, 20, 110, 40), Color.FromArgb(120, 120, 220, 120), Color.FromArgb(140, 120, 120, 130), Color.FromArgb(200, 40, 40, 40) };

        /// <summary>The map with the painted plan laid over it, half see-through.</summary>
        private static Bitmap WithPlan(Bitmap map, PlanMap p)
        {
            Bitmap b = new Bitmap(map);
            using (Bitmap o = new Bitmap(p.N, p.N, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                for (int y = 0; y < p.N; y++)
                    for (int x = 0; x < p.N; x++)
                    {
                        int code = p.Codes[y * p.N + x];
                        if (code > 0 && code < PlanColours.Length) o.SetPixel(x, y, PlanColours[code]);
                    }
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    g.DrawImage(o, new Rectangle(0, 0, b.Width, b.Height));
                }
            }
            return b;
        }

        private void PaintPlan(float u, float v, bool first)
        {
            if (plan == null || coarse == null || u < 0 || v < 0 || u > 1 || v > 1) return;
            PlanArea a = (PlanArea)(cbPaint.SelectedIndex - 1); // "Erase" is PlanArea.None
            plan.Paint(u * (plan.N - 1), v * (plan.N - 1), (float)nudBrush.Value / TerrainGen.CoarseCell, a);
            Redraw();
        }

        private void SavePlan()
        {
            if (plan == null || planPath == null) return;
            try { plan.Save(planPath); } catch (Exception ex) { Log.Error("Saving the plan failed", ex); }
        }

        // ---------------- building ----------------

        private string BackupHeights(object world)
        {
            string dir = Caw.WorldDir(Path.Combine(Log.Dir, "Backups"), world);
            Directory.CreateDirectory(dir);
            string backup = Path.Combine(dir, "height_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            if (!Caw.SaveHeightImage(world, backup)) throw new IOException("CAW could not export the current heights for a backup.");
            ManualEdits.PruneBackups(dir, 10);
            return backup;
        }

        /// <summary>Replaces the world's heights with 'change' applied to them; puts the backup back if the import fails.</summary>
        private void ChangeHeights(object world, string what, HeightChange change)
        {
            string backup = BackupHeights(world);
            int W, H; ImageIO.PngSize(backup, out W, out H);
            Grid h = HeightImport.ReadVertices(world, ti, W, H, OnProgress);
            float cell = ti.WorldSize / (W - 1);
            change(h, cell);
            try
            {
                string clip;
                float err = HeightImport.Apply(world, ti, h, W, H, OnProgress, out clip);
                Log.Info(string.Format("Generate Roads: {0}; heights re-imported, mean error {1:F2} m, backup {2}", what, err, backup));
                try { Caw.RefreshSea(world, ti.WaterLevel); } catch (Exception ex) { Log.Error("Refreshing the sea level failed", ex); }
            }
            catch (Exception ex)
            {
                if (!(ex is WorldChangedException)) // the backup belongs to the world the action began on
                    try { Caw.ImportHeightImage(world, backup); } catch (Exception ex2) { Log.Error("Restoring the heights failed", ex2); }
                throw;
            }
        }

        private void BuildRoads()
        {
            if (busy || Injector.BusyElsewhere(this)) return;
            object world = Caw.CurrentWorld;
            if (world == null || ti == null) { Status("Open a world and read its terrain first."); return; }
            if (net == null || net.Roads.Count == 0) { Status("No roads planned yet (too steep or too much water?)."); return; }
            if (MessageBox.Show(this, string.Format("Build {0:F1} km of roads ({1} pieces, {2} intersections) in CAW?{3}\n\nCAW's Undo removes them.",
                    net.TotalKm, net.Roads.Count, net.Junctions.Count, chkSmooth.Checked ? "\n\nThe ground under them is smoothed first (heights backed up)." : ""),
                    "Build roads", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            if (planStop != null) planStop.Requested = true;
            SetBusy(true);
            try
            {
                RoadNet plan = net;
                float hoodStep = Preset != null && Preset.TownDesign != null && Preset.TownDesign.Length > 4 ? Preset.TownDesign[4] : 0;
                if (chkShelves.Checked && hoodStep >= 1 && plan.Lots.Count > 0)
                {
                    float seaS = ti.WaterLevel; int shelves = 0; int seedS = (int)nudSeed.Value;
                    float stepS = Math.Min(25f, hoodStep);
                    ChangeHeights(world, "neighbourhoods on their own shelves", delegate (Grid h, float cell) { shelves = LotPlanner.HoodShelves(h, cell, plan.Lots, seaS, plan.Centre, stepS, seedS); });
                    Log.Info(string.Format("Generate Roads: {0} neighbourhoods on their own shelves, {1:F1} m step (the World type's)", shelves, stepS));
                }
                if (chkSmooth.Checked)
                {
                    List<List<float[]>> lines = new List<List<float[]>>();
                    foreach (Road r in plan.Roads) lines.Add(r.Points);
                    float sea = ti.WaterLevel;
                    List<float[]> bridges = plan.Bridges; // the land raised to each bridge's deck at both ends (#131)
                    float span = 0;
                    foreach (BridgeType b in WorldBridges()) span = Math.Max(span, b.Length);
                    if (span <= 0) span = RoadBuilder.BridgeLength;
                    ChangeHeights(world, "ground smoothed under " + lines.Count + " roads", delegate (Grid h, float cell) { TerrainGen.GradeRoads(h, cell, lines, sea, bridges, span); });
                }
                if (cbLook.SelectedIndex >= 0)
                {
                    try { Caw.SelectRoadSet(world, cbLook.SelectedIndex); Log.Info("Road look: " + cbLook.SelectedItem + " (set " + cbLook.SelectedIndex + ")"); }
                    catch (Exception ex) { Log.Error("Selecting the road look failed", ex); }
                }
                string note = RoadBuilder.Build(world, ti, plan, OnProgress, chkCawGrade.Checked, WorldBridges());
                builtNet = plan;
                SetBusy(false);
                Injector.Done("roads");
                Status(note + (plan.Lots.Count > 0 ? " Next: 'Place lots' (" + plan.Lots.Count + " planned)." : ""));
            }
            catch (Exception ex)
            {
                Log.Error("Building roads failed", ex);
                SetBusy(false);
                Status("Building roads failed: " + ex.Message);
            }
        }

        private void PlaceLots()
        {
            if (busy || Injector.BusyElsewhere(this)) return;
            object world = Caw.CurrentWorld;
            if (world == null) { Status("Open a world first."); return; }
            if (builtNet == null) { Status("Build roads first: the lots go along the roads this window builds."); return; }
            if (builtNet.Lots.Count == 0) { Status("No lots were planned along these roads."); return; }
            SetBusy(true);
            try
            {
                TerrainInfo now = TerrainSampler.Analyze(world, Caw.LayerCount(Caw.Terrain(world)), OnProgress);
                string fitNote;
                List<RoadBuilder.LotPlacement> lots = RoadBuilder.FitLots(world, now, builtNet, out fitNote);
                if (chkPads.Checked && lots.Count > 0)
                {
                    List<PlannedLot> pads = RoadBuilder.ToMapLots(now, lots);
                    float sea = now.WaterLevel;
                    ti = now;
                    List<List<float[]>> built = RoadBuilder.MapRoadLines(world, now); // their ground stays as it is (#133)
                    ChangeHeights(world, "pads levelled under " + pads.Count + " lots", delegate (Grid h, float cell) { LotPlanner.LevelPads(h, cell, pads, sea, built); });
                }
                string note = RoadBuilder.PlaceFitted(world, lots, OnProgress) + fitNote;
                Injector.Done("lots");
                SetBusy(false);
                Status(note);
            }
            catch (Exception ex)
            {
                Log.Error("Placing lots failed", ex);
                SetBusy(false);
                Status("Placing lots failed: " + ex.Message);
            }
        }

        // ---------------- helpers ----------------

        private void OnProgress(string stage, int pct)
        {
            progress.Value = Math.Max(0, Math.Min(100, pct));
            status.Text = stage + "...";
            status.Refresh(); progress.Refresh();
            Injector.Pump();
        }

        private void Status(string s) { status.Text = s; }

        private void SetBusy(bool b)
        {
            busy = b;
            Injector.SetBusy(this, b);
            btnRead.Enabled = btnBuild.Enabled = btnLots.Enabled = !b;
            UseWaitCursor = b;
            if (!b) progress.Value = 0;
        }
    }
}
