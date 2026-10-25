using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CawPlus
{
    /// <summary>
    /// The Spawners window (next to Auto-Place): fishing spots, seeds, gems, metals, meteorites, butterflies, bugs and
    /// pet animals, placed the way EA's worlds place them (SpawnerEngine), each group in its own World Layer.
    /// </summary>
    public sealed class SpawnerForm : Form
    {
        private ComboBox cbRef;
        private CheckBox[] chkGroup;
        private NumericUpDown[] nudCount;
        private Label[] lblTypes;
        private CheckedListBox list;
        private TextBox txtFilter;
        private CheckBox chkOwnLayers;
        private NumericUpDown nudSeed;
        private Button btnRead, btnPlace, btnRemove;
        private PictureBox picture;
        private ProgressBar progress;
        private Label status, counts;
        private Timer previewTimer;

        private readonly List<PlaceItem> all = new List<PlaceItem>();
        private readonly List<PlaceItem> shown = new List<PlaceItem>();
        private TerrainInfo terrain;
        private Grid work; private int factor = 1; private float workCell;
        private float landHa;
        private readonly List<float[]> occupied = new List<float[]>();
        private readonly List<float[]> roadPts = new List<float[]>();
        private readonly List<List<float[]>> roadLines = new List<List<float[]>>(); // each spline's knots, map coords
        private List<float[]> lots = new List<float[]>();
        private List<string> layerNames = new List<string>();
        private PlacementEngine.Maps maps;
        private List<SpawnPlacement> plan;
        private readonly List<object> lastPlaced = new List<object>();
        private bool busy, filling, applying;

        private static readonly Color[] GroupColors = { Color.FromArgb(0, 200, 255), Color.FromArgb(0, 90, 220), Color.FromArgb(240, 220, 60),
            Color.FromArgb(220, 60, 220), Color.FromArgb(200, 200, 200), Color.FromArgb(255, 120, 0), Color.FromArgb(255, 150, 200),
            Color.FromArgb(120, 70, 30), Color.FromArgb(130, 255, 130), Color.White };

        public SpawnerForm()
        {
            SuspendLayout();
            Text = "Spawners  (CAW Plus)";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(900, 640);
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 8.25f);
            int x = 10, y = 10, colW = 360;
            int n = SpawnerEngine.GroupNames.Length;

            GroupBox gb = new GroupBox(); gb.Text = "What to place"; gb.SetBounds(x, y, colW, 330);
            Label lr = new Label(); lr.Text = "World type"; lr.SetBounds(10, 22, 60, 16); gb.Controls.Add(lr);
            cbRef = new ComboBox(); cbRef.DropDownStyle = ComboBoxStyle.DropDownList; cbRef.SetBounds(72, 18, 278, 20);
            cbRef.SelectedIndexChanged += delegate { ApplyReference(); };
            new ToolTip().SetToolTip(cbRef, "How many of each kind per hectare, and which spawners, as in this Sims 3 world.");
            gb.Controls.Add(cbRef);
            Label h1 = new Label(); h1.Text = "Kind"; h1.SetBounds(10, 46, 150, 16); h1.ForeColor = Color.DimGray; gb.Controls.Add(h1);
            Label h2 = new Label(); h2.Text = "How many"; h2.SetBounds(196, 46, 60, 16); h2.ForeColor = Color.DimGray; gb.Controls.Add(h2);
            chkGroup = new CheckBox[n]; nudCount = new NumericUpDown[n]; lblTypes = new Label[n];
            for (int g = 0; g < n; g++)
            {
                int gy = 64 + g * 26;
                Panel swatch = new Panel(); swatch.BackColor = GroupColors[g]; swatch.SetBounds(10, gy + 5, 8, 8); gb.Controls.Add(swatch);
                chkGroup[g] = new CheckBox(); chkGroup[g].Text = SpawnerEngine.GroupNames[g]; chkGroup[g].SetBounds(22, gy, 172, 20);
                chkGroup[g].Checked = g != (int)SpawnGroup.PetAnimals && g != (int)SpawnGroup.Other;
                chkGroup[g].CheckedChanged += delegate { FillList(); SchedulePreview(); };
                gb.Controls.Add(chkGroup[g]);
                nudCount[g] = new NumericUpDown(); nudCount[g].Minimum = 0; nudCount[g].Maximum = 500; nudCount[g].SetBounds(196, gy, 56, 20);
                nudCount[g].ValueChanged += delegate { if (!applying) SchedulePreview(); };
                gb.Controls.Add(nudCount[g]);
                lblTypes[g] = new Label(); lblTypes[g].SetBounds(258, gy + 3, 96, 16); lblTypes[g].ForeColor = Color.DimGray; gb.Controls.Add(lblTypes[g]);
            }
            Controls.Add(gb);
            y += 336;

            GroupBox gl = new GroupBox(); gl.Text = "Spawner types"; gl.SetBounds(x, y, colW, 186);
            Label ls = new Label(); ls.Text = "Search"; ls.SetBounds(10, 22, 40, 16); gl.Controls.Add(ls);
            txtFilter = new TextBox(); txtFilter.SetBounds(54, 18, 296, 20); txtFilter.TextChanged += delegate { FillList(); }; gl.Controls.Add(txtFilter);
            list = new CheckedListBox(); list.SetBounds(10, 44, 340, 132); list.CheckOnClick = true; list.IntegralHeight = false;
            list.ItemCheck += delegate (object o, ItemCheckEventArgs e)
            {
                if (filling) return;
                shown[e.Index].Selected = e.NewValue == CheckState.Checked;
                UpdateTypeCounts(); SchedulePreview();
            };
            gl.Controls.Add(list);
            Controls.Add(gl);
            y += 192;

            chkOwnLayers = new CheckBox(); chkOwnLayers.Text = "Sort into World Layers"; chkOwnLayers.Checked = true; chkOwnLayers.SetBounds(x, y, 150, 20);
            new ToolTip().SetToolTip(chkOwnLayers, "Uses your Spawners layer if the world has one; otherwise EA's layers (FishSpawners, SeedSpawners...).");
            Controls.Add(chkOwnLayers);
            Label lsd = new Label(); lsd.Text = "Seed"; lsd.SetBounds(x + 160, y + 3, 30, 16); Controls.Add(lsd);
            nudSeed = new NumericUpDown(); nudSeed.Minimum = 0; nudSeed.Maximum = 999999; nudSeed.Value = 11; nudSeed.SetBounds(x + 192, y, 70, 20);
            nudSeed.ValueChanged += delegate { SchedulePreview(); };
            Controls.Add(nudSeed);
            y += 26;
            btnRead = new Button(); btnRead.Text = "Read world"; btnRead.SetBounds(x, y, 110, 28); btnRead.Click += delegate { ReadWorld(); };
            btnPlace = new Button(); btnPlace.Text = "Place"; btnPlace.SetBounds(x + 115, y, 110, 28); btnPlace.Font = new Font(Font, FontStyle.Bold);
            btnPlace.Click += delegate { PlaceAll(); };
            btnRemove = new Button(); btnRemove.Text = "Remove last"; btnRemove.SetBounds(x + 230, y, 110, 28); btnRemove.Click += delegate { RemoveLast(); };
            Controls.Add(btnRead); Controls.Add(btnPlace); Controls.Add(btnRemove);
            y += 32;
            progress = new ThemedProgress(); progress.SetBounds(x, y, colW, 12); Controls.Add(progress);
            y += 14;
            status = new Label(); status.SetBounds(x, y, colW, 30); status.Text = "Click 'Read world' to start."; Controls.Add(status);

            picture = new PictureBox(); picture.SetBounds(x + colW + 18, 10, 512, 512);
            picture.SizeMode = PictureBoxSizeMode.Zoom; picture.BackColor = Color.FromArgb(40, 40, 40); picture.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(picture);
            counts = new Label(); counts.SetBounds(x + colW + 18, 528, 512, 100);
            counts.Text = "Dots in each kind's colour (see the swatches). Fishing spots sit on the water; the rest on gentle ground away from roads and lots.";
            Controls.Add(counts);

            cbRef.Items.Add("Generic (about Sunset Valley)");
            int sel = 0;
            foreach (ObjectLibrary l in ObjectLibrary.LoadAll())
            {
                bool has = false;
                foreach (RefObject r in l.Items) if (r.Kind == PlaceKind.Spawner) { has = true; break; }
                if (!has) continue;
                cbRef.Items.Add(l);
                if (string.Equals(l.Name, WorldPreset.Inspiration ?? "Sunset Valley", StringComparison.OrdinalIgnoreCase)) sel = cbRef.Items.Count - 1;
            }
            cbRef.SelectedIndex = sel;
            previewTimer = new Timer(); previewTimer.Interval = 400;
            previewTimer.Tick += delegate { previewTimer.Stop(); UpdatePreview(); };
            // scale with the screen's DPI (text was cut off on some displays)
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            Theme.Apply(this);
        }

        // ---------------- reading ----------------

        private void ReadWorld()
        {
            if (busy || Injector.BusyElsewhere(this)) return;
            object world = Caw.CurrentWorld;
            if (world == null) { Status("Open a world first."); return; }
            SetBusy(true);
            try
            {
                terrain = TerrainSampler.Analyze(world, Caw.LayerCount(Caw.Terrain(world)), OnProgress);
                factor = Math.Max(1, terrain.MapW / 768);
                work = TerrainSampler.Downsample(terrain.Heights, factor);
                workCell = terrain.CellMeters * factor;
                int landCells = 0;
                for (int i = 0; i < work.D.Length; i++) if (work.D[i] >= terrain.WaterLevel) landCells++;
                landHa = landCells * workCell * workCell / 10000f;

                OnProgress("Reading roads, lots and objects", 96);
                roadPts.Clear(); roadLines.Clear();
                foreach (float[] k in Caw.RoadSplines(world))
                {
                    List<float[]> line = new List<float[]>();
                    for (int j = 0; j + 1 < k.Length; j += 2)
                    {
                        float mx, my; TerrainSampler.WorldToMap(terrain, k[j], k[j + 1], out mx, out my);
                        roadPts.Add(new[] { mx, my }); line.Add(new[] { mx, my });
                    }
                    roadLines.Add(line);
                }
                lots = new List<float[]>();
                foreach (float[] c in Caw.LotCircles())
                {
                    float mx, my; TerrainSampler.WorldToMap(terrain, c[0], c[1], out mx, out my);
                    lots.Add(new[] { mx, my, c[2] / terrain.CellMeters });
                }
                occupied.Clear();
                int spawnersThere = 0;
                foreach (Caw.WorldObject o in Caw.WorldObjects())
                {
                    float mx, my; TerrainSampler.WorldToMap(terrain, o.X, o.Z, out mx, out my);
                    float r = o.Tree ? 2f : Math.Max(1f, Math.Min(15f, o.Radius * 0.8f));
                    occupied.Add(new[] { mx, my, r / terrain.CellMeters });
                    if ((o.Name ?? "").IndexOf("spawn", StringComparison.OrdinalIgnoreCase) >= 0) spawnersThere++;
                }
                layerNames = Caw.LayerNames();

                OnProgress("Reading the object catalog", 98);
                PlacementEngine.Known.Clear();
                foreach (ObjectLibrary l in ObjectLibrary.LoadAll())
                    foreach (RefObject r in l.Items)
                        if (r.Instance != null && r.Instance.Length == 16 && !PlacementEngine.Known.ContainsKey(r.Instance)) PlacementEngine.Known[r.Instance] = r.Kind;
                all.Clear();
                foreach (Caw.CatalogItem c in Caw.Catalog())
                {
                    PlaceItem p = PlacementEngine.Classify(c);
                    if (p.Kind == PlaceKind.Spawner) all.Add(p);
                }
                Log.Info(string.Format("Spawners: {0} spawner types in the catalog, {1} spawners already in the world, {2:F0} ha of land", all.Count, spawnersThere, landHa));
                SetBusy(false);
                ApplyReference();
                Status(string.Format("{0} spawner types in the catalog; {1} spawners already in the world.", all.Count, spawnersThere));
            }
            catch (Exception ex)
            {
                Log.Error("Spawners: reading the world failed", ex);
                SetBusy(false);
                Status("Reading the world failed: " + ex.Message);
            }
        }

        /// <summary>Counts per group from the Like world's density and this world's land; ticks the types it uses.</summary>
        private void ApplyReference()
        {
            try { ApplyReferenceCore(); }
            catch (Exception ex) { Log.Error("World type failed", ex); Status("World type failed: " + ex.Message); }
        }

        private void ApplyReferenceCore()
        {
            ObjectLibrary lib = cbRef.SelectedItem as ObjectLibrary;
            float[] perHa = SpawnerEngine.PerHa(lib);
            applying = true;
            for (int g = 0; g < nudCount.Length; g++)
            {
                int c = landHa > 0 ? (int)Math.Round(perHa[g] * landHa) : 0;
                nudCount[g].Value = Math.Max(nudCount[g].Minimum, Math.Min(nudCount[g].Maximum, c));
            }
            applying = false;
            int used = 0;
            foreach (PlaceItem p in all)
            {
                p.Ref = lib != null ? lib.Find(p.Catalog.Name, p.Catalog.InstanceHex) : null;
                p.Weight = p.Ref != null ? p.Ref.Count : 1;
                p.Selected = lib == null || p.Ref != null;
                if (p.Ref != null) used++;
            }
            // a group the Like world uses none of (in this catalog): all its types
            for (int g = 0; g < nudCount.Length; g++)
            {
                bool any = false;
                foreach (PlaceItem p in all) if (p.Selected && SpawnerEngine.GroupOf(p.Name) == (SpawnGroup)g) { any = true; break; }
                if (!any) foreach (PlaceItem p in all) if (SpawnerEngine.GroupOf(p.Name) == (SpawnGroup)g) p.Selected = true;
            }
            FillList();
            UpdateTypeCounts();
            SchedulePreview();
            if (lib != null && all.Count > 0) Status("Like " + lib.Name + ": " + used + " of its spawner types found in this catalog.");
        }

        private void FillList()
        {
            try { FillListCore(); }
            catch (Exception ex) { Log.Error("Item list failed", ex); Status("Item list failed: " + ex.Message); }
        }

        private void FillListCore()
        {
            if (list == null) return;
            filling = true;
            list.BeginUpdate();
            list.Items.Clear(); shown.Clear();
            foreach (PlaceItem p in all)
            {
                int g = (int)SpawnerEngine.GroupOf(p.Name);
                if (!chkGroup[g].Checked) continue;
                if (txtFilter.Text.Length > 0 && p.Name.IndexOf(txtFilter.Text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                shown.Add(p);
                list.Items.Add(p.Name + "   [" + SpawnerEngine.GroupNames[g] + "]", p.Selected);
            }
            list.EndUpdate();
            filling = false;
        }

        private void UpdateTypeCounts()
        {
            int[] n = new int[lblTypes.Length], sel = new int[lblTypes.Length];
            foreach (PlaceItem p in all) { int g = (int)SpawnerEngine.GroupOf(p.Name); n[g]++; if (p.Selected) sel[g]++; }
            for (int g = 0; g < lblTypes.Length; g++) lblTypes[g].Text = sel[g] + " of " + n[g] + " types";
        }

        // ---------------- preview ----------------

        private void SchedulePreview() { if (work != null && previewTimer != null) { previewTimer.Stop(); previewTimer.Start(); } }

        private int[] Counts()
        {
            int[] c = new int[nudCount.Length];
            for (int g = 0; g < c.Length; g++) c[g] = chkGroup[g].Checked ? (int)nudCount[g].Value : 0;
            return c;
        }

        private void UpdatePreview()
        {
            if (work == null || busy) return;
            try
            {
                PlaceSettings s = new PlaceSettings();
                s.Seed = (int)nudSeed.Value; s.WaterLevel = terrain.WaterLevel;
                s.WaterOffset = terrain.WaterOffset != null ? TerrainSampler.Downsample(terrain.WaterOffset, factor) : null;
                Grid blocked = new Grid(work.W, work.H);
                foreach (float[] p in roadPts) Disc(blocked, p[0] / factor, p[1] / factor, 10f / workCell);
                foreach (float[] l in lots) Disc(blocked, l[0] / factor, l[1] / factor, l[2] / factor + 4f / workCell);
                int rim = (int)Math.Ceiling(60f / workCell); // the map's rim is under the distant terrain
                for (int yy = 0; yy < blocked.H; yy++)
                    for (int xx = 0; xx < blocked.W; xx++)
                        if (xx < rim || yy < rim || xx >= blocked.W - rim || yy >= blocked.H - rim) blocked.D[yy * blocked.W + xx] = 1;
                maps = PlacementEngine.BuildMaps(work, workCell, s, blocked);
                if (roadLines.Count > 0) maps.RoadDist = PlacementEngine.RoadDistance(maps, roadLines, workCell, factor);
                int[] want = Counts();
                plan = SpawnerEngine.Place(maps, workCell, factor, all, want, s.Seed, occupied, cbRef.SelectedItem as ObjectLibrary);
                int[] got = new int[want.Length];
                foreach (SpawnPlacement p in plan) got[(int)SpawnerEngine.GroupOf(all[p.Item].Name)]++;
                picture.Image = Render();
                List<string> parts = new List<string>();
                for (int g = 0; g < want.Length; g++)
                    if (want[g] > 0) parts.Add(SpawnerEngine.GroupNames[g] + " " + got[g] + (got[g] < want[g] ? " of " + want[g] : ""));
                counts.Text = plan.Count + " spawners: " + string.Join(", ", parts.ToArray()) + ".\n" +
                    "Fewer than asked means there isn't enough suitable ground (or water) left.";
            }
            catch (Exception ex) { Log.Error("Spawner preview failed", ex); Status("Preview failed: " + ex.Message); }
        }

        private static void Disc(Grid g, float cx, float cy, float r)
        {
            int x0 = Math.Max(0, (int)(cx - r)), x1 = Math.Min(g.W - 1, (int)(cx + r)), y0 = Math.Max(0, (int)(cy - r)), y1 = Math.Min(g.H - 1, (int)(cy + r));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if ((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy) <= r * r) g.D[y * g.W + x] = 1;
        }

        private Bitmap Render()
        {
            int size = 512;
            Bitmap bmp = new Bitmap(size, size);
            float k = (float)size / work.W;
            using (Graphics g = Graphics.FromImage(bmp))
            {
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        int wx = Math.Min(work.W - 1, (int)(x / k)), wy = Math.Min(work.H - 1, (int)(y / k)), i = wy * work.W + wx;
                        Color c = maps.Water.D[i] > 0 ? (maps.Fresh.D[i] > 0 ? Color.FromArgb(70, 110, 140) : Color.FromArgb(45, 70, 100))
                                : maps.Blocked.D[i] > 0 ? Color.FromArgb(130, 95, 85) : Color.FromArgb(105, 120, 75);
                        float shade = 0.75f + 0.25f * (1 - Math.Min(1, maps.Slope.D[i] / 40f));
                        bmp.SetPixel(x, y, Color.FromArgb((int)(c.R * shade), (int)(c.G * shade), (int)(c.B * shade)));
                    }
                foreach (SpawnPlacement p in plan)
                {
                    int grp = (int)SpawnerEngine.GroupOf(all[p.Item].Name);
                    float px = p.MapX / factor * k, py = p.MapY / factor * k, r = 3f;
                    using (SolidBrush b = new SolidBrush(GroupColors[grp])) g.FillEllipse(b, px - r, py - r, 2 * r, 2 * r);
                    using (Pen o = new Pen(Color.Black, 1f)) g.DrawEllipse(o, px - r, py - r, 2 * r, 2 * r);
                }
            }
            return bmp;
        }

        // ---------------- placing ----------------

        /// <summary>The user's own spawner layer if the world has one (any layer with 'spawn' in its name), else EA's per kind.</summary>
        private string LayerFor(SpawnGroup g)
        {
            string ea = SpawnerEngine.LayerNames[(int)g];
            foreach (string l in layerNames) if (string.Equals(l, ea, StringComparison.OrdinalIgnoreCase)) return l;
            foreach (string l in layerNames) if (l.IndexOf("spawn", StringComparison.OrdinalIgnoreCase) >= 0) return l;
            return ea;
        }

        private void PlaceAll()
        {
            if (busy || Injector.BusyElsewhere(this)) return;
            object world = Caw.CurrentWorld;
            if (world == null || terrain == null) { Status("Click 'Read world' first."); return; }
            if (plan == null || plan.Count == 0) { Status("Nothing to place."); return; }
            if (!chkOwnLayers.Checked && !Caw.HasActiveLayer(world))
            {
                MessageBox.Show(this, "Select the World Layer to place into first (in CAW's World Layers panel).", "Spawners");
                return;
            }
            if (MessageBox.Show(this, "Place " + plan.Count + " spawners" + (chkOwnLayers.Checked ? " (sorted into World Layers)" : "") + "?\n\n" +
                    "'Remove last' (or CAW's Undo) takes them out again.", "Spawners", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            SetBusy(true);
            lastPlaced.Clear();
            int failed = 0;
            DateTime t0 = DateTime.Now;
            Caw.ResetPlaceTiming();
            try
            {
                List<SpawnPlacement> ordered = new List<SpawnPlacement>(plan);
                ordered.Sort(delegate (SpawnPlacement a, SpawnPlacement b)
                {
                    return string.CompareOrdinal(LayerFor(SpawnerEngine.GroupOf(all[a.Item].Name)), LayerFor(SpawnerEngine.GroupOf(all[b.Item].Name)));
                });
                string current = null;
                for (int k = 0; k < ordered.Count; k++)
                {
                    SpawnPlacement p = ordered[k];
                    if (chkOwnLayers.Checked)
                    {
                        string layer = LayerFor(SpawnerEngine.GroupOf(all[p.Item].Name));
                        if (layer != current)
                        {
                            current = layer;
                            try { if (!Caw.UseLayer(world, layer)) Log.Info("Could not switch to World Layer '" + layer + "'"); }
                            catch (Exception ex) { Log.Error("Switching to World Layer '" + layer + "' failed", ex); }
                        }
                    }
                    float x, z; TerrainSampler.MapToWorld(terrain, p.MapX, p.MapY, out x, out z);
                    // fishing spots sit on the water surface (EA: median 4.3 m above the bed in Riverview)
                    float y = float.NaN;
                    if (p.OnWater)
                    {
                        y = terrain.WaterLevel;
                        if (terrain.WaterOffset != null)
                        {
                            int ix = Math.Min(terrain.WaterOffset.W - 1, Math.Max(0, (int)p.MapX)), iy = Math.Min(terrain.WaterOffset.H - 1, Math.Max(0, (int)p.MapY));
                            y += terrain.WaterOffset[ix, iy];
                        }
                    }
                    object o = null;
                    try { o = Caw.Place(all[p.Item].Catalog, x, z, p.Yaw, 0, lastPlaced.Count > 0, y); }
                    catch (Exception ex) { if (failed < 5) Log.Error("Placing " + all[p.Item].Name + " failed", ex); }
                    if (o != null) lastPlaced.Add(o); else failed++;
                    if (k == 9 && lastPlaced.Count == 0) { Status("The first 10 failed; stopped. See the log."); break; }
                    if (k % 10 == 0) { OnProgress("Placing " + (k + 1) + " of " + plan.Count, 100 * k / plan.Count); Application.DoEvents(); }
                }
                Log.Info(string.Format("Spawners: placed {0}, failed {1}, {2:F0} s", lastPlaced.Count, failed, (DateTime.Now - t0).TotalSeconds));
                Log.Info("Spawners timing: " + Caw.PlaceTimingSummary());
                if (lastPlaced.Count > 0) Injector.Done("spawners");
                Status("Placed " + lastPlaced.Count + " spawners" + (failed > 0 ? " (" + failed + " failed, see log)" : "") + ".");
                foreach (SpawnPlacement p in plan) occupied.Add(new[] { p.MapX, p.MapY, 3f / terrain.CellMeters });
            }
            catch (Exception ex) { Log.Error("Spawners failed", ex); Status("Placing failed: " + ex.Message); }
            SetBusy(false);
        }

        private void RemoveLast()
        {
            if (busy || lastPlaced.Count == 0) { Status("Nothing placed in this session."); return; }
            if (MessageBox.Show(this, "Remove the " + lastPlaced.Count + " spawners from the last run?", "Spawners",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            SetBusy(true);
            int removed = 0;
            for (int i = lastPlaced.Count - 1; i >= 0; i--)
            {
                try { Caw.Delete(lastPlaced[i], removed > 0); removed++; }
                catch (Exception ex) { if (removed < 3) Log.Error("Removing spawner failed", ex); }
            }
            lastPlaced.Clear();
            SetBusy(false);
            Status("Removed " + removed + " spawners.");
        }

        private void OnProgress(string stage, int pct)
        {
            progress.Value = Math.Max(0, Math.Min(100, pct));
            status.Text = stage + "...";
            status.Refresh(); progress.Refresh();
        }

        private void Status(string s) { status.Text = s; }

        private void SetBusy(bool b)
        {
            busy = b;
            Injector.SetBusy(this, b);
            btnRead.Enabled = btnPlace.Enabled = btnRemove.Enabled = !b;
            UseWaitCursor = b;
            if (!b) progress.Value = 0;
        }
    }
}
