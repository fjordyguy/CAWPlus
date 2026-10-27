using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace CawPlus
{
    /// <summary>
    /// The Footpaths window (Roads and Trees toolbar, after Build roads; the tester's v0.40 test): reads the roads already in
    /// the world, plans footpaths round them the way the World type has them (length, distance from the roads, trails
    /// into the hills, promenades by the water; PathPlanner.PlanOnRoads), shows them, then builds them as CAW footpaths.
    /// </summary>
    public sealed class FootpathForm : Form
    {
        private ComboBox cbLike;
        private TrackBar tbAmount;
        private Label lblAmount;
        private NumericUpDown nudSeed;
        private Button btnPlan, btnBuild;
        private PictureBox picture;
        private ProgressBar progress;
        private Label status, info;
        private readonly List<WorldPreset> presets = WorldPreset.LoadAll();
        private TerrainInfo ti;
        private List<FootPath> planned;
        private bool busy;
        private const int Tries = 6;

        public FootpathForm()
        {
            SuspendLayout();
            Text = "Footpaths  (CAW Plus)";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(900, 560);
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 8.25f);
            int x = 10, y = 10, colW = 350;

            Label intro = new Label(); intro.SetBounds(x, y, colW, 44);
            intro.Text = "Footpaths round the roads already in your world (build the roads first). They follow the world type: " +
                         "how much path it has and how far from the roads, promenades by the water, trails into the hills.";
            Controls.Add(intro);
            y += 50;
            Label ll = new Label(); ll.Text = "World type"; ll.SetBounds(x, y + 3, 70, 16); Controls.Add(ll);
            cbLike = new ComboBox(); cbLike.DropDownStyle = ComboBoxStyle.DropDownList; cbLike.SetBounds(x + 72, y, colW - 72, 20);
            int pick = 0;
            for (int i = 0; i < presets.Count; i++)
            {
                cbLike.Items.Add(presets[i].Name);
                if (WorldPreset.Inspiration != null && WorldPreset.Short(presets[i].Name) == WorldPreset.Inspiration) pick = i;
            }
            if (cbLike.Items.Count > 0) cbLike.SelectedIndex = pick;
            cbLike.SelectedIndexChanged += delegate { ShowTarget(); };
            Controls.Add(cbLike);
            y += 28;
            lblAmount = new Label(); lblAmount.SetBounds(x, y + 3, 120, 16); Controls.Add(lblAmount);
            tbAmount = new ThemedSlider(); tbAmount.AutoSize = false; tbAmount.Minimum = 25; tbAmount.Maximum = 200; tbAmount.Value = 100; tbAmount.TickFrequency = 25;
            tbAmount.SetBounds(x + 120, y, colW - 120, 30);
            tbAmount.ValueChanged += delegate { ShowTarget(); };
            Controls.Add(tbAmount);
            y += 36;
            Label ls = new Label(); ls.Text = "Seed"; ls.SetBounds(x, y + 3, 40, 16); Controls.Add(ls);
            nudSeed = new NumericUpDown(); nudSeed.Minimum = 1; nudSeed.Maximum = 99999; nudSeed.Value = 1; nudSeed.SetBounds(x + 42, y, 70, 20);
            Controls.Add(nudSeed);
            y += 30;
            btnPlan = new Button(); btnPlan.Text = "Plan footpaths"; btnPlan.SetBounds(x, y, 160, 28);
            btnPlan.Click += delegate { PlanPaths(); };
            Controls.Add(btnPlan);
            btnBuild = new Button(); btnBuild.Text = "Build footpaths"; btnBuild.SetBounds(x + 170, y, 160, 28); btnBuild.Enabled = false;
            btnBuild.Font = new Font(Font, FontStyle.Bold);
            btnBuild.Click += delegate { BuildPaths(); };
            Controls.Add(btnBuild);
            y += 36;
            progress = new ThemedProgress(); progress.SetBounds(x, y, colW, 14); Controls.Add(progress);
            y += 18;
            status = new Label(); status.SetBounds(x, y, colW, 80); Controls.Add(status);
            y += 84;
            info = new Label(); info.SetBounds(x, y, colW, 120); Controls.Add(info);

            picture = new PictureBox(); picture.SetBounds(x + colW + 18, 10, 512, 512);
            picture.SizeMode = PictureBoxSizeMode.Zoom; picture.BackColor = Color.FromArgb(40, 40, 40); picture.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(picture);
            ShowTarget();
            Status("Click 'Plan footpaths' to read the roads and terrain and plan the paths.");
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            Theme.Apply(this);
        }

        private WorldPreset Like() { return cbLike.SelectedIndex >= 0 ? presets[cbLike.SelectedIndex] : null; }

        private void ShowTarget()
        {
            lblAmount.Text = "Amount: " + tbAmount.Value + "%";
            WorldPreset w = Like();
            float[] t = w != null ? w.PathTargets : null;
            info.Text = t != null && t.Length > 3 && t[0] > 0
                ? string.Format("{0} has {1:F1} km of footpath on a 2 km map, {2:F0} m from the roads on average ({3:F0}-{4:F0} m), in pieces of ~{5:F0} m.",
                    WorldPreset.Short(w.Name), t[0], t[2], t[1], t[3], t.Length > 4 ? t[4] : 20)
                : (w != null ? WorldPreset.Short(w.Name) + " has (almost) no footpaths; a light network is planned at this amount." : "");
        }

        private void PlanPaths()
        {
            if (busy || Injector.BusyElsewhere(this)) return;
            object world = Caw.CurrentWorld;
            if (world == null) { Status("Open a world first."); return; }
            SetBusy(true);
            try
            {
                ti = TerrainSampler.Analyze(world, Caw.LayerCount(Caw.Terrain(world)), OnProgress);
                // the roads already built, in metres on the terrain map
                List<List<float[]>> roads = new List<List<float[]>>();
                foreach (float[] k in Caw.RoadSplines(world))
                {
                    if (k.Length < 8) continue;
                    List<float[]> line = new List<float[]>();
                    for (int s = 0; s <= 8; s++)
                    {
                        float u = s / 8f, a = (1 - u) * (1 - u) * (1 - u), b = 3 * u * (1 - u) * (1 - u), c = 3 * u * u * (1 - u), d = u * u * u;
                        float wx = a * k[0] + b * k[2] + c * k[4] + d * k[6], wz = a * k[1] + b * k[3] + c * k[5] + d * k[7];
                        float mx, my; TerrainSampler.WorldToMap(ti, wx, wz, out mx, out my);
                        line.Add(new[] { mx * ti.CellMeters, my * ti.CellMeters });
                    }
                    roads.Add(line);
                }
                if (roads.Count == 0) { SetBusy(false); Status("No roads found: build the roads first (Generate Roads > Build roads, or CAW's road tool)."); return; }
                float step = TerrainGen.CoarseCell / ti.CellMeters;
                Grid soft = ti.Heights.Blur(step * 0.4f);
                int cn = (int)(ti.WorldSize / TerrainGen.CoarseCell) + 1;
                Grid coarse = new Grid(cn, cn);
                for (int j = 0; j < cn; j++)
                    for (int i = 0; i < cn; i++) coarse[i, j] = TerrainGen.Sample(soft, i * step, j * step);
                // lots and the objects already placed: paths keep out of them (#136)
                List<float[]> blockers = new List<float[]>();
                int nLots = 0, nObj = 0;
                try
                {
                    foreach (float[] lc in Caw.LotCircles())
                    {
                        float mx, my; TerrainSampler.WorldToMap(ti, lc[0], lc[1], out mx, out my);
                        blockers.Add(new[] { mx * ti.CellMeters, my * ti.CellMeters, lc[2] + 2f, 1f }); nLots++;
                    }
                    foreach (Caw.WorldObject o in Caw.WorldObjects())
                    {
                        float mx, my; TerrainSampler.WorldToMap(ti, o.X, o.Z, out mx, out my);
                        float r = o.Tree ? 1.5f : Math.Max(0.8f, Math.Min(12f, o.Radius * 0.8f));
                        blockers.Add(new[] { mx * ti.CellMeters, my * ti.CellMeters, r, 0f }); nObj++;
                    }
                }
                catch (Exception ex) { Log.Error("Reading lots and objects for the footpaths failed", ex); }
                Log.Info(string.Format("Footpaths keep out of {0} lots and {1} objects", nLots, nObj));
                WorldPreset w = Like();
                float[] targets = w != null ? w.PathTargets : null;
                float amount = tbAmount.Value / 100f, sea = ti.WaterLevel;
                int seed = (int)nudSeed.Value;
                string checks = "";
                List<FootPath> paths = (List<FootPath>)RunInBackground(delegate ()
                {
                    return PathPlanner.PlanOnRoads(coarse, roads, sea, seed, targets, amount, Tries, blockers, out checks);
                });
                planned = paths;
                RoadNet show = new RoadNet();
                foreach (List<float[]> r in roads) { Road rd = new Road(); rd.Kind = RoadKind.Street; rd.Points = r; show.Roads.Add(rd); }
                show.Paths = paths;
                Image old = picture.Image;
                picture.Image = TerrainGenForm.Render(coarse, TerrainGen.CoarseCell, sea, show, 512);
                if (old != null) old.Dispose();
                Log.Info("Footpaths planned (" + (w != null ? w.Name : "-") + ", " + tbAmount.Value + "%): " + checks);
                SetBusy(false);
                btnBuild.Enabled = paths.Count > 0;
                Status(checks + ". Brown = footpaths. Click 'Build footpaths' to lay them in CAW.");
            }
            catch (Exception ex)
            {
                Log.Error("Planning footpaths failed", ex);
                SetBusy(false);
                Status("Planning footpaths failed: " + ex.Message);
            }
        }

        private void BuildPaths()
        {
            if (busy || Injector.BusyElsewhere(this) || planned == null || ti == null) return;
            object world = Caw.CurrentWorld;
            if (world == null) { Status("Open a world first."); return; }
            if (MessageBox.Show(this, string.Format("Build {0} footpaths in CAW?\n\nCAW's Undo removes them.", planned.Count), "Footpaths",
                    MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            SetBusy(true);
            try
            {
                string note = RoadBuilder.BuildPaths(world, ti, planned, OnProgress);
                SetBusy(false);
                btnBuild.Enabled = false;
                Injector.Done("paths");
                Status(note);
            }
            catch (Exception ex)
            {
                Log.Error("Building footpaths failed", ex);
                SetBusy(false);
                Status("Building footpaths failed: " + ex.Message);
            }
        }

        // ---------------- helpers ----------------

        private delegate object Task();

        /// <summary>Runs pure computation on a worker thread, keeping CAW responsive.</summary>
        private object RunInBackground(Task task)
        {
            object result = null; Exception error = null;
            Thread t = new Thread(delegate () { try { result = task(); } catch (Exception ex) { error = ex; } });
            t.IsBackground = true;
            t.Start();
            int tick = 0;
            while (!t.Join(20))
            {
                progress.Value = (tick++ * 2) % 100; status.Text = "Planning footpaths...";
                Injector.Pump();
            }
            if (error != null) throw new InvalidOperationException(error.Message, error);
            return result;
        }

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
            btnPlan.Enabled = !b; if (b) btnBuild.Enabled = false;
            UseWaitCursor = b;
            if (!b) progress.Value = 0;
        }
    }
}
