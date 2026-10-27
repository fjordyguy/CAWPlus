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
    /// Generate Terrain: random heightmap (seeded, with sliders) previewed as a map, then applied to the open world
    /// through CAW's own height import.
    /// </summary>
    public sealed class TerrainGenForm : Form
    {
        private NumericUpDown nudSeed, nudRivers;
        private ComboBox cbCoast, cbSize, cbLike;
        private bool applyingPreset;
        private int userCoast = -1; // coast picked by hand (Any EA world keeps it), -1 = from the EA worlds
        private TrackBar tbMountains, tbHills, tbRough, tbErosion, tbFlat, tbWater, tbHeight;
        private CheckBox chk3D;
        private Bitmap map2D;              // the 2D map shown (or draped over the heights in 3D)
        private Grid mapHeights; private float mapCell, mapSea;
        private MapView view;            // the preview: 2D map or the 3D view with its camera
        private string terrainKey;         // the settings the shown terrain was made from: unchanged settings reuse it
        private float[] presetCurve;                  // the Like world's measured height distribution (null = built-in)
        private bool presetLakes = true;              // the Like world has inland lakes
        private float presetCorner = -1;              // the Like world's sea direction (edge .. corner)
        private float presetRiverWidth = 1, presetPondRadius = 50;
        private string presetStyle = "";
        private int presetPonds;                      // raised ponds the Like world has
        private float presetSea = -1, presetLake = -1; // the Like world's sea and lake shares (%)
        private int presetHoods = 5;                  // the World type's neighbourhoods: terrain pockets (GenSettings.Pockets)
        private TerrainProfile presetTerrain;         // the Like world's terrain and water, for the terrain checks
        private const int TerrainTries = 8;           // variations of the seed tried (TerrainGen.GenerateChecked)
        private string checkedKey;                    // seed, Like world, coast and size the variation below was chosen for
        private int checkedSeed;
        private TerrainChecks.Result checkedResult;
        private Button btnApply, btnUndo;
        private PictureBox picture;
        private ProgressBar progress;
        private Label status, info;
        private Timer previewTimer;
        private bool busy;

        private float worldSize = 1536, seaLevel = 10;
        private float openWorldSize;         // size of the world open in CAW, 0 if unknown
        private static readonly int[] Sizes = { 512, 1024, 2048 };
        private TerrainGen.Result preview;   // preview-resolution terrain + rivers
        // the preview is worked out on a background thread (the tester's v0.40 test: CAW froze ~10 s per change)
        private Work.Stop previewStop;
        private bool previewRunning, previewPending;

        public TerrainGenForm()
        {
            SuspendLayout();
            Text = "Generate Terrain  (CAW Plus)";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(900, 770);
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 8.25f);
            int x = 10, y = 10, colW = 360;

            GroupBox gbT = new GroupBox(); gbT.Text = "Terrain"; gbT.SetBounds(x, y, colW, 390);
            int ty = 18;
            Label lk = new Label(); lk.Text = "World type"; lk.SetBounds(10, ty + 4, 64, 16); gbT.Controls.Add(lk);
            cbLike = new ComboBox(); cbLike.DropDownStyle = ComboBoxStyle.DropDownList; cbLike.SetBounds(76, ty, 274, 20);
            cbLike.Items.Add(AnyEA);
            cbLike.Items.Add("(your own settings)");
            presets = WorldPreset.LoadAll();
            foreach (WorldPreset wp in presets) cbLike.Items.Add(wp);
            cbLike.SelectedIndexChanged += delegate
            {
                if (cbLike.SelectedIndex == 0) ApplyInspired();
                else { userCoast = -1; ApplyPreset(cbLike.SelectedItem as WorldPreset); } // one world: its own coast
            };
            new ToolTip().SetToolTip(cbLike, "Sets the terrain and town sliders like a Sims 3 world. Measured worlds were fitted to their own heightmap;\n" +
                "(approx.) ones are set from what the world is known for. Change anything afterwards; the seed still picks the exact map.");
            gbT.Controls.Add(cbLike);
            ty += 28;
            Label ls = new Label(); ls.Text = "Seed"; ls.SetBounds(10, ty + 4, 34, 16); gbT.Controls.Add(ls);
            nudSeed = Num(gbT, 46, ty, 0, 999999, new Random().Next(1, 99999));
            nudSeed.ValueChanged += delegate { if (cbLike.SelectedIndex == 0) ApplyInspired(); }; // a new map = a new EA world
            Button bNew = new Button(); bNew.Text = "New map"; bNew.SetBounds(122, ty - 1, 70, 23);
            bNew.Click += delegate { nudSeed.Value = new Random().Next(1, 999999); };
            gbT.Controls.Add(bNew);
            Label lc = new Label(); lc.Text = "Coast"; lc.SetBounds(200, ty + 4, 36, 16); gbT.Controls.Add(lc);
            cbCoast = new ComboBox(); cbCoast.DropDownStyle = ComboBoxStyle.DropDownList; cbCoast.SetBounds(238, ty, 112, 20);
            cbCoast.Items.AddRange(new object[] { "None (inland)", "Sea on one side", "Island", "Central lake", "Islands (archipelago)", "Iceland (fjords + lowland)", "Volcano (atoll)", "Orleans (channels)" });
            cbCoast.SelectedIndex = 1;
            cbCoast.SelectedIndexChanged += delegate
            {
                if (!applyingPreset) userCoast = cbCoast.SelectedIndex; // the user's pick stays when the seed changes
                Schedule();
            };
            gbT.Controls.Add(cbCoast);
            ty += 30;
            Label lw = new Label(); lw.Text = "World size"; lw.SetBounds(10, ty + 4, 64, 16); gbT.Controls.Add(lw);
            cbSize = new ComboBox(); cbSize.DropDownStyle = ComboBoxStyle.DropDownList; cbSize.SetBounds(76, ty, 150, 20);
            cbSize.SelectedIndexChanged += delegate { OnSizeChanged(); };
            gbT.Controls.Add(cbSize);
            Button bSave = new Button(); bSave.Text = "Heightmap for New World..."; bSave.SetBounds(232, ty - 1, 118, 23);
            bSave.Click += delegate { SaveForNewWorld(); };
            new ToolTip().SetToolTip(bSave, "CAW can't resize a world. To make a world of this size: save the heightmap,\n" +
                "use it in CAW's File > New World, then open Generate Terrain there and click Apply terrain.");
            gbT.Controls.Add(bSave);
            ty += 30;
            tbMountains = Slider(gbT, "Mountains", 45, ref ty);
            tbHills = Slider(gbT, "Hills", 50, ref ty);
            tbRough = Slider(gbT, "Roughness", 40, ref ty);
            tbErosion = Slider(gbT, "Erosion (valleys)", 50, ref ty);
            tbFlat = Slider(gbT, "Flat land (towns, farms)", 40, ref ty);
            tbWater = Slider(gbT, "Water", 15, ref ty);
            tbWater.ValueChanged += delegate { if (!applyingPreset) presetSea = presetLake = -1; }; // by hand: the slider decides
            tbHeight = Slider(gbT, "Max height", 16, ref ty);
            Label lr = new Label(); lr.Text = "Rivers"; lr.SetBounds(10, ty + 4, 60, 16); gbT.Controls.Add(lr);
            nudRivers = Num(gbT, 140, ty, 0, 5, 2);
            Controls.Add(gbT);
            y += 396;

            y += 6;

            // one step at a time (the tester, 2026-10-24): terrain, then roads, then lots, each its own button and undo step
            btnApply = new Button(); btnApply.Text = "Apply terrain"; btnApply.SetBounds(x, y, 88, 28); btnApply.Font = new Font(Font, FontStyle.Bold);
            btnApply.Click += delegate { ApplyTerrain(); };
            btnUndo = new Button(); btnUndo.Text = "Undo terrain"; btnUndo.SetBounds(x + 91, y, 88, 28);
            btnUndo.Click += delegate { UndoTerrain(); };
            Controls.Add(btnApply); Controls.Add(btnUndo);
            y += 34;
            progress = new ThemedProgress(); progress.SetBounds(x, y, colW, 14); Controls.Add(progress);
            y += 18;
            status = new Label(); status.SetBounds(x, y, colW, 90); Controls.Add(status);

            picture = new PictureBox(); picture.SetBounds(x + colW + 18, 10, 512, 512);
            picture.SizeMode = PictureBoxSizeMode.Zoom; picture.BackColor = Color.FromArgb(40, 40, 40); picture.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(picture);
            chk3D = new CheckBox(); chk3D.Text = "3D view"; chk3D.SetBounds(x + colW + 18, 526, 80, 22);
            chk3D.CheckedChanged += delegate { RedrawMap(); };
            Button bTurn = new Button(); bTurn.Text = "Turn"; bTurn.SetBounds(x + colW + 102, 525, 60, 23);
            CheckBox chkExag = new CheckBox(); chkExag.Text = "Exaggerate heights (x2)"; chkExag.SetBounds(x + colW + 172, 526, 170, 22);
            view = new MapView(picture, chk3D, chkExag);
            bTurn.Click += delegate { view.Turn(); };
            new ToolTip().SetToolTip(chk3D, "The map in 3D at true height. Drag to orbit, right-drag to pan, wheel to zoom, double-click to reset; 'Turn' goes a quarter round.");
            new ToolTip().SetToolTip(chkExag, "Draw heights twice as tall so gentle hills read clearly (peaks then look twice as sharp as in CAW).");
            Controls.Add(chk3D); Controls.Add(bTurn); Controls.Add(chkExag);
            // colour scheme of the preview (#145): green, desert, or the world type's own EA paint
            ComboBox cbColours = new ComboBox(); cbColours.DropDownStyle = ComboBoxStyle.DropDownList;
            cbColours.SetBounds(x + colW + 350, 525, 180, 22);
            cbColours.Items.AddRange(new object[] { "Colours: green (default)", "Colours: desert", "Colours: world type's paint" });
            cbColours.SelectedIndex = Math.Max(0, Math.Min(2, ColourScheme));
            cbColours.SelectedIndexChanged += delegate
            {
                ColourScheme = cbColours.SelectedIndex;
                Recolour();
            };
            new ToolTip().SetToolTip(cbColours, "How the preview is coloured. Desert: sand, rust and pale rock with green along the water.\n" +
                "World type's paint: the ground types (grass, dirt, rock, sand) the world type's EA paint style puts on each slope and height.\n" +
                "Only the preview changes; the terrain is the same.");
            Controls.Add(cbColours);
            info = new Label(); info.SetBounds(x + colW + 18, 552, 512, 170); Controls.Add(info);

            previewTimer = new Timer(); previewTimer.Interval = 300;
            previewTimer.Tick += delegate { previewTimer.Stop(); UpdatePreview(); };

            try
            {
                object world = Caw.CurrentWorld;
                if (world != null) { seaLevel = Caw.SeaLevel(world); openWorldSize = OpenWorldSize(world); }
            }
            catch (Exception ex) { Log.Error("Reading the open world failed", ex); }
            FillSizes();
            Status("Change the sliders or seed; the map updates. 'Apply terrain' replaces the open world's heights (it is backed up first).");
            cbLike.SelectedIndex = 0; // the standard: every map is one of the EA worlds
            Schedule();
            // scale with the screen's DPI (text was cut off on some displays)
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            Theme.Apply(this);
        }

        private NumericUpDown Num(Control parent, int x, int y, int min, int max, int value)
        {
            NumericUpDown n = new NumericUpDown(); n.Minimum = min; n.Maximum = max; n.Value = value; n.SetBounds(x, y, 72, 20);
            n.ValueChanged += delegate { Schedule(); };
            parent.Controls.Add(n);
            return n;
        }

        private TrackBar Slider(GroupBox gb, string label, int value, ref int ty)
        {
            Label l = new Label(); l.Text = label; l.SetBounds(10, ty + 6, 128, 16); gb.Controls.Add(l);
            TrackBar tb = new ThemedSlider(); tb.AutoSize = false; tb.Minimum = 0; tb.Maximum = 100; tb.Value = value; tb.TickFrequency = 10;
            tb.SetBounds(140, ty, 212, 28);
            tb.ValueChanged += delegate { Schedule(); };
            gb.Controls.Add(tb);
            ty += 30;
            return tb;
        }

        private void Schedule() { if (applyingPreset) return; if (previewTimer != null) { previewTimer.Stop(); previewTimer.Start(); } }

        private const string AnyEA = "Any EA world (a random one each map)";
        private List<WorldPreset> presets;

        /// <summary>'Any EA world': the seed picks one EA world (WorldPreset.Inspired).</summary>
        private void ApplyInspired()
        {
            string label;
            WorldPreset w = WorldPreset.Inspired(presets, (int)nudSeed.Value, userCoast, out label);
            if (w == null) return;
            ApplyPreset(w);
            Status(label + " Each new seed picks one EA world and uses only that world's settings; choose a world under World type to keep to it.");
        }

        /// <summary>Sets the terrain, river and town controls from a world preset, then updates the preview once.</summary>
        private void ApplyPreset(WorldPreset w)
        {
            if (w == null) return;
            applyingPreset = true;
            try
            {
                cbCoast.SelectedIndex = Math.Max(0, Math.Min(cbCoast.Items.Count - 1, (int)w.Coast));
                Set(tbMountains, w.Mountains); Set(tbHills, w.Hills); Set(tbRough, w.Roughness); Set(tbErosion, w.Erosion);
                Set(tbFlat, w.FlatLand); Set(tbWater, w.Water); Set(tbHeight, (w.MaxHeight - 30) / 6.7f);
                nudRivers.Value = Math.Max(nudRivers.Minimum, Math.Min(nudRivers.Maximum, w.Rivers));
                presetHoods = Math.Max(0, Math.Min(8, w.Neighbourhoods));
                presetCurve = w.HeightCurve; presetLakes = w.InlandLakes; presetCorner = w.CoastCorner; presetRiverWidth = w.RiverWidth; presetPonds = w.Ponds; presetPondRadius = w.PondRadius; presetSea = w.SeaShare; presetLake = w.LakeShare; presetTerrain = w.Terrain; presetStyle = w.Style;
            }
            finally { applyingPreset = false; }
            Log.Info("Generate Terrain: preset '" + w.Name + "' (" + w.Source + ")" + (w.Note != null && w.Note.StartsWith("Inspired") ? ": " + w.Note : ""));
            WorldPreset.Inspiration = WorldPreset.Short(w.Name);
            Status(w.Name + ": " + w.Note + (w.Character != null ? " Roads: " + w.Character.Description + "." : "") + (w.Source == "measured" ? " (Fitted to the world's own heightmap.)" :
                   " (Approximate: set from what the world is known for; send its .world file to have it measured.)"));
            Schedule();
        }

        /// <summary>
        /// Generates with the terrain checks. The variation of the seed is searched only when the seed, Like world, coast
        /// or size changes (about 0.5 s per variation); slider moves reuse the one chosen, so the preview stays quick.
        /// </summary>
        private TerrainGen.Result GenChecked(GenSettings g, int size, float ws, bool full, ProgressFn progress, CheckJob job, out TerrainChecks.Result tc)
        {
            if (job.Key == checkedKey) { g.Seed = checkedSeed; tc = checkedResult; TerrainChecks.Result none; return Helper.Generate(g, size, ws, full, progress, null, 0, 0, null, out none); }
            TerrainGen.Result r = Helper.Generate(g, size, ws, full, progress, job.Target, job.Water, TerrainTries, null, out tc);
            checkedKey = job.Key; checkedSeed = g.Seed; checkedResult = tc;
            return r;
        }

        /// <summary>What the terrain checks need, read from the window's controls on CAW's UI thread.</summary>
        private sealed class CheckJob { public string Key; public TerrainProfile Target; public float Water; }

        private CheckJob Job(GenSettings g, float ws)
        {
            CheckJob j = new CheckJob();
            j.Key = g.Seed + "|" + cbLike.SelectedIndex + "|" + cbCoast.SelectedIndex + "|" + ws;
            j.Target = presetTerrain; j.Water = WaterTarget();
            return j;
        }

        // ---------------- background work ----------------

        private delegate object Task();
        private volatile string bgStage;
        private volatile int bgPct = -1;

        /// <summary>Progress from a worker thread: shown by RunInBackground's loop on the UI thread.</summary>
        private void BgProgress(string stage, int pct) { bgStage = stage; bgPct = pct; }

        /// <summary>
        /// Runs pure computation (no CAW calls, no windows) on a worker thread and keeps CAW responsive meanwhile, showing
        /// its progress; returns its result or rethrows its error.
        /// </summary>
        private object RunInBackground(Task task)
        {
            object result = null; Exception error = null;
            Thread t = new Thread(delegate () { try { result = task(); } catch (Exception ex) { error = ex; } });
            t.IsBackground = true;
            t.Start();
            while (!t.Join(20))
            {
                int pct = bgPct;
                if (pct >= 0) { progress.Value = Math.Max(0, Math.Min(100, pct)); status.Text = bgStage + "..."; }
                Injector.Pump();
            }
            bgPct = -1;
            if (error != null) throw new InvalidOperationException(error.Message, error);
            return result;
        }

        /// <summary>The water share the terrain checks aim for: the Like world's, or the Water slider once moved by hand.</summary>
        private float WaterTarget() { return presetSea >= 0 && presetTerrain != null ? presetTerrain.Water : tbWater.Value / 100f; }

        private static void Set(TrackBar t, float v) { t.Value = Math.Max(t.Minimum, Math.Min(t.Maximum, (int)Math.Round(v))); }

        private GenSettings Gen()
        {
            GenSettings s = new GenSettings();
            s.Seed = (int)nudSeed.Value;
            s.Mountains = tbMountains.Value; s.Hills = tbHills.Value; s.Roughness = tbRough.Value; s.Erosion = tbErosion.Value; s.Pockets = presetHoods; // town site where the land suits it
            s.FlatLand = tbFlat.Value; s.Water = tbWater.Value; s.Rivers = (int)nudRivers.Value;
            s.MaxHeight = 30 + tbHeight.Value * 6.7f; // 30..700 m (EA's Lunar Lakes reaches 684 m; Sunset Valley 386 m)
            s.SeaLevel = seaLevel;
            s.Coast = (CoastType)cbCoast.SelectedIndex;
            s.HeightCurve = presetCurve; s.InlandLakes = presetLakes; s.CoastCorner = presetCorner; s.RiverWidth = presetRiverWidth; s.Ponds = presetPonds; s.PondRadius = presetPondRadius; s.SeaShare = presetSea; s.LakeShare = presetLake; s.SetStyle(presetStyle);
            return s;
        }

        // ---------------- preview ----------------

        private void UpdatePreview()
        {
            if (busy || Injector.BusyElsewhere(this)) return;
            if (previewRunning)
            {
                // a newer request: drop the one being worked out and start again when it has stopped
                previewPending = true;
                if (previewStop != null) previewStop.Requested = true;
                return;
            }
            GenSettings g; CheckJob job; string ckey; int cseed; TerrainChecks.Result cres; string tkey; TerrainGen.Result reuse;
            try
            {
                g = Gen(); job = Job(g, worldSize);
                // nothing the terrain is made from changed (TerrainKey): keep the terrain shown
                tkey = TerrainKey(g, worldSize, job);
                reuse = tkey == terrainKey ? preview : null;
                ckey = checkedKey; cseed = checkedSeed; cres = checkedResult;
            }
            catch (Exception ex) { Log.Error("Terrain preview failed", ex); return; }
            float ws = worldSize;
            Work.Stop stop = new Work.Stop();
            previewStop = stop; previewRunning = true;
            UseWaitCursor = true;
            Status("Working out the map...");
            Thread t = new Thread(delegate ()
            {
                TerrainGen.Result res = null; Bitmap bmp = null;
                TerrainChecks.Result tchecks = null; Exception error = null;
                try
                {
                    if (reuse != null) { res = reuse; tchecks = cres; }
                    else if (job.Key == ckey) { g.Seed = cseed; tchecks = cres; TerrainChecks.Result none; res = Helper.Generate(g, 384, ws, false, null, null, 0, 0, stop, out none); }
                    else res = Helper.Generate(g, 384, ws, false, null, job.Target, job.Water, TerrainTries, stop, out tchecks);
                    if (res != null && !stop.Requested) bmp = Render(res.Heights, res.Cell, g.SeaLevel, null, 512, res.Ponds);
                }
                catch (Exception ex) { error = ex; }
                try
                {
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        PreviewDone(stop, job, g, res, bmp, tchecks, error, ws, tkey);
                    }));
                }
                catch (Exception) { if (bmp != null) bmp.Dispose(); } // the window was closed
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>Shows a map (2D, or in 3D over its heights); the form keeps the 2D bitmap for the 3D view.</summary>
        private void ShowMap(Bitmap bmp, Grid h, float cell, float sea)
        {
            Bitmap oldMap = map2D;
            map2D = bmp; mapHeights = h; mapCell = cell; mapSea = sea;
            RedrawMap();
            if (oldMap != null && !ReferenceEquals(oldMap, picture.Image)) oldMap.Dispose();
        }

        /// <summary>Re-renders the current preview in the chosen colours (no new terrain).</summary>
        private void Recolour()
        {
            try
            {
                if (preview == null || preview.Heights == null) return;
                ShowMap(Render(preview.Heights, preview.Cell, mapSea, null, 512, preview.Ponds), preview.Heights, preview.Cell, mapSea);
            }
            catch (Exception ex) { Log.Error("Recolouring the preview failed", ex); }
        }

        private void RedrawMap()
        {
            if (map2D == null) return;
            view.SetMap(map2D, mapHeights, mapCell, mapSea, null);
        }

        /// <summary>Everything the terrain is made from (GenSettings' fields, world size, the checks' target), as a key.</summary>
        private static string TerrainKey(GenSettings g, float ws, CheckJob job)
        {
            System.Text.StringBuilder b = new System.Text.StringBuilder();
            foreach (System.Reflection.FieldInfo f in typeof(GenSettings).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            {
                object v = f.GetValue(g);
                Array a = v as Array;
                if (a != null) { b.Append(a.Length); foreach (object o in a) b.Append(',').Append(o); }
                else b.Append(v);
                b.Append('|');
            }
            return b.Append(ws).Append('|').Append(job.Key).Append('|').Append(job.Water).ToString();
        }

        /// <summary>Shows a finished preview (on the UI thread), or starts the newer one that was asked for meanwhile.</summary>
        private void PreviewDone(Work.Stop stop, CheckJob job, GenSettings g, TerrainGen.Result res, Bitmap bmp,
                                 TerrainChecks.Result tchecks, Exception error, float ws, string tkey)
        {
            previewRunning = false;
            UseWaitCursor = busy;
            if (previewPending || stop.Requested || res == null)
            {
                if (bmp != null) bmp.Dispose();
                if (error != null) Log.Error("Terrain preview failed", error);
                previewPending = false;
                UpdatePreview();
                return;
            }
            if (error != null) { Log.Error("Terrain preview failed", error); Status("Preview failed: " + error.Message); return; }
            try
            {
                if (tchecks != null) { checkedKey = job.Key; checkedSeed = g.Seed; checkedResult = tchecks; }
                preview = res; terrainKey = tkey;
                ShowMap(bmp, res.Heights, res.Cell, g.SeaLevel);
                float lo, hi; res.Heights.MinMax(out lo, out hi);
                int water = 0; foreach (float v in res.Heights.D) if (v < g.SeaLevel) water++;
                info.Text = string.Format("Heights {0:F0}-{1:F0} m, sea level {2:F0} m, {3:F0}% water, {4} river(s).", lo, hi, g.SeaLevel,
                                100f * water / res.Heights.D.Length, res.Rivers.Count)
                          + string.Format(" World {0:F0} m.", ws)
                          + (tchecks != null ? "\n" + tchecks.ToString() + " against the world type's land and water." : "");
                Status("Map ready. 'Apply terrain' replaces the open world's heights (backed up first).");
            }
            catch (Exception ex) { Log.Error("Terrain preview failed", ex); Status("Preview failed: " + ex.Message); }
        }

        // ---------- preview colours (#145) ----------

        /// <summary>The preview's colour scheme for this session: 0 green, 1 desert, 2 the world type's EA paint style.</summary>
        internal static int ColourScheme;
        private static readonly object styleLock = new object();
        private static string schemeStyleKey;
        private static StyleProfile schemeStyle;

        /// <summary>The paint style of the world type the map was made like (WorldPreset.Inspiration), cached; null if none.</summary>
        private static StyleProfile SchemeStyle()
        {
            string world = WorldPreset.Inspiration;
            if (string.IsNullOrEmpty(world)) return null;
            string key = WorldPreset.Key(world);
            lock (styleLock)
            {
                if (key == schemeStyleKey) return schemeStyle;
                StyleProfile found = null;
                try
                {
                    foreach (StyleProfile p in StyleProfile.LoadAll())
                        if (WorldPreset.Key(p.Name) == key) { found = p; if (p.FromEA) break; }
                }
                catch (Exception ex) { Log.Error("Loading the world type's paint style for the preview failed", ex); }
                schemeStyleKey = key; schemeStyle = found;
                return found;
            }
        }

        private static int BinOf(float v, float[] edges)
        {
            int b = 0;
            while (b < edges.Length && v >= edges[b]) b++;
            return b;
        }

        private static void RoleColour(PaintRole r, out float cr, out float cg, out float cb)
        {
            switch (r)
            {
                case PaintRole.Meadow: cr = 98; cg = 132; cb = 62; break;
                case PaintRole.Shore: cr = 206; cg = 186; cb = 140; break;
                case PaintRole.Dirt: cr = 140; cg = 106; cb = 74; break;
                default: cr = 130; cg = 126; cb = 120; break; // rock
            }
        }

        /// <summary>Metres from each cell to the nearest water cell (two-pass chamfer); only for the desert scheme.</summary>
        private static float[] WaterDistance(Grid h, float cell, float sea)
        {
            int w = h.W, ht = h.H;
            float[] d = new float[w * ht];
            const float Far = 1e9f;
            for (int i = 0; i < d.Length; i++) d[i] = h.D[i] < sea ? 0 : Far;
            float a = cell, b = cell * 1.4142f;
            for (int y = 0; y < ht; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x; float v = d[i];
                    if (x > 0) v = Math.Min(v, d[i - 1] + a);
                    if (y > 0)
                    {
                        v = Math.Min(v, d[i - w] + a);
                        if (x > 0) v = Math.Min(v, d[i - w - 1] + b);
                        if (x < w - 1) v = Math.Min(v, d[i - w + 1] + b);
                    }
                    d[i] = v;
                }
            for (int y = ht - 1; y >= 0; y--)
                for (int x = w - 1; x >= 0; x--)
                {
                    int i = y * w + x; float v = d[i];
                    if (x < w - 1) v = Math.Min(v, d[i + 1] + a);
                    if (y < ht - 1)
                    {
                        v = Math.Min(v, d[i + w] + a);
                        if (x < w - 1) v = Math.Min(v, d[i + w + 1] + b);
                        if (x > 0) v = Math.Min(v, d[i + w - 1] + b);
                    }
                    d[i] = v;
                }
            return d;
        }

        private static float SampleArr(float[] a, int w, int ht, float x, float y)
        {
            x = Math.Max(0, Math.Min(w - 1, x)); y = Math.Max(0, Math.Min(ht - 1, y));
            int x0 = Math.Min(w - 2, (int)x), y0 = Math.Min(ht - 2, (int)y);
            if (x0 < 0 || y0 < 0) return a[0];
            float fx = x - x0, fy = y - y0;
            int i = y0 * w + x0;
            return (a[i] * (1 - fx) + a[i + 1] * fx) * (1 - fy) + (a[i + w] * (1 - fx) + a[i + w + 1] * fx) * fy;
        }

        /// <summary>The built-in green relief colour for land at relative height t (0 = sea level, 1 = the top).</summary>
        private static void GreenLand(float t, out float r, out float g, out float b)
        {
            if (t < 0.5f) { float u = t * 2; r = 96 + 32 * u; g = 128 - 8 * u; b = 64 + 18 * u; }
            else { float u = (t - 0.5f) * 2; r = 128 + 22 * u; g = 120 + 26 * u; b = 82 + 58 * u; }
        }

        internal static Bitmap Render(Grid h, float cell, float sea, RoadNet net, int size, List<float[]> ponds = null)
        {
            Bitmap bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            float lo, hi; h.MinMax(out lo, out hi);
            float k = (float)(h.W - 1) / size;
            int scheme = ColourScheme;
            StyleProfile style = scheme == 2 ? SchemeStyle() : null;
            if (scheme == 2 && style == null) scheme = 0; // no style for this world type: the green scheme
            float[] wdist = scheme == 1 ? WaterDistance(h, cell, sea) : null;
            float baseR = 0, baseG = 0, baseB = 0; bool baseKnown = false;
            if (style != null && !string.IsNullOrEmpty(style.BaseTexture))
            {
                PaintRole? br = StyleProfile.RoleOf(null, style.BaseTexture);
                if (br.HasValue) { RoleColour(br.Value, out baseR, out baseG, out baseB); baseKnown = true; }
            }
            System.Drawing.Imaging.BitmapData bd = bmp.LockBits(new Rectangle(0, 0, size, size), System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
            byte[] row = new byte[bd.Stride];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fx = x * k, fy = y * k;
                    float v = TerrainGen.Sample(h, fx, fy);
                    float gx = (TerrainGen.Sample(h, fx + 1, fy) - TerrainGen.Sample(h, fx - 1, fy)) / (2 * cell);
                    float gy = (TerrainGen.Sample(h, fx, fy + 1) - TerrainGen.Sample(h, fx, fy - 1)) / (2 * cell);
                    float shade = Math.Max(0.45f, Math.Min(1.25f, (-gx - gy) / 1.41f * 0.9f + 0.85f));
                    float t = Math.Max(0, Math.Min(1, (v - sea) / Math.Max(1, hi - sea)));
                    float r, g, b;
                    // raised ponds and lakes (x, z, radius, level): their water shows above the sea too (#141)
                    bool pond = false;
                    if (ponds != null)
                        foreach (float[] q in ponds)
                        {
                            float dx = fx * cell - q[0], dz = fy * cell - q[1];
                            if (v < q[3] && dx * dx + dz * dz < q[2] * q[2] * 2.6f) { pond = true; break; }
                        }
                    if (v < sea || pond)
                    {
                        if (scheme == 1) { r = 40; g = 112; b = 132; } else { r = 55; g = 85; b = 120; }
                        shade = 1;
                    }
                    else if (scheme == 1)
                    {
                        // desert: sand low, tan and rust on slopes, pale rock on the tops; green along the water
                        float slope = (float)(Math.Atan(Math.Sqrt(gx * gx + gy * gy)) * 180 / Math.PI);
                        if (t < 0.45f) { float u = t / 0.45f; r = 214 - 14 * u; g = 184 - 14 * u; b = 132 - 12 * u; }
                        else { float u = (t - 0.45f) / 0.55f; r = 200 + 22 * u; g = 170 + 36 * u; b = 120 + 62 * u; }
                        float steep = Math.Max(0, Math.Min(1, (slope - 12) / 20f));
                        r += (172 - r) * steep; g += (110 - g) * steep; b += (72 - b) * steep;
                        float wd = SampleArr(wdist, h.W, h.H, fx, fy);
                        float oasis = Math.Max(0, 1 - wd / 40f) * Math.Max(0, Math.Min(1, (8 - (v - sea)) / 6f));
                        r += (88 - r) * oasis; g += (124 - g) * oasis; b += (58 - b) * oasis;
                    }
                    else if (scheme == 2)
                    {
                        // the world type's paint: each ground type's share for this slope and height above the water
                        float slope = (float)(Math.Atan(Math.Sqrt(gx * gx + gy * gy)) * 180 / Math.PI);
                        int sb = BinOf(slope, TerrainFeatures.SlopeEdges), hb = BinOf(v - sea, TerrainFeatures.HeightEdges);
                        float sum = 0, ar = 0, ag = 0, ab = 0;
                        foreach (KeyValuePair<PaintRole, RoleStyle> kv in style.Roles)
                        {
                            RoleStyle rs = kv.Value;
                            if (rs == null || rs.Cover <= 0) continue;
                            float ps = rs.Slope != null && sb < rs.Slope.Length && !float.IsNaN(rs.Slope[sb]) ? rs.Slope[sb] : rs.Cover;
                            float ph = rs.Height != null && hb < rs.Height.Length && !float.IsNaN(rs.Height[hb]) ? rs.Height[hb] : rs.Cover;
                            float share = Math.Max(0, Math.Min(1, ps * ph / rs.Cover));
                            float cr, cg, cb; RoleColour(kv.Key, out cr, out cg, out cb);
                            ar += cr * share; ag += cg * share; ab += cb * share; sum += share;
                        }
                        if (sum > 1) { ar /= sum; ag /= sum; ab /= sum; sum = 1; }
                        float br0 = baseR, bg0 = baseG, bb0 = baseB;
                        if (!baseKnown) GreenLand(t, out br0, out bg0, out bb0);
                        r = ar + br0 * (1 - sum); g = ag + bg0 * (1 - sum); b = ab + bb0 * (1 - sum);
                    }
                    else GreenLand(t, out r, out g, out b);
                    row[x * 3] = (byte)Math.Min(255, b * shade); row[x * 3 + 1] = (byte)Math.Min(255, g * shade); row[x * 3 + 2] = (byte)Math.Min(255, r * shade);
                }
                System.Runtime.InteropServices.Marshal.Copy(row, 0, new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), bd.Stride);
            }
            bmp.UnlockBits(bd);
            if (net != null)
                using (Graphics gr = Graphics.FromImage(bmp))
                {
                    gr.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    DrawNet(gr, net, size / ((h.W - 1) * cell));
                }
            return bmp;
        }

        /// <summary>A road plan drawn over a map picture: lots, roads, footpaths and bridges ('s' pixels per metre).</summary>
        internal static void DrawNet(Graphics gr, RoadNet net, float s)
        {
            // lots first, under the roads: residential light, community darker
            using (Brush res = new SolidBrush(Color.FromArgb(200, 236, 214, 160)), com = new SolidBrush(Color.FromArgb(210, 196, 120, 70)))
            using (Pen edge = new Pen(Color.FromArgb(150, 90, 70, 40), 1f))
                foreach (PlannedLot l in net.Lots)
                {
                    float[][] c = LotPlanner.Corners(l.X, l.Z, l.Ux, l.Uz, l.Width, l.Depth);
                    PointF[] q = new PointF[4];
                    for (int i = 0; i < 4; i++) q[i] = new PointF(c[i][0] * s, c[i][1] * s);
                    gr.FillPolygon(l.Community ? com : res, q);
                    gr.DrawPolygon(edge, q);
                }
            Pen[] pens = { new Pen(Color.FromArgb(30, 30, 30), 3.5f), new Pen(Color.FromArgb(240, 240, 230), 2.2f), new Pen(Color.FromArgb(200, 196, 170), 1.5f) };
            foreach (Road r in net.Roads)
            {
                if (r.Points.Count < 2) continue;
                PointF[] pts = new PointF[r.Points.Count];
                for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(r.Points[i][0] * s, r.Points[i][1] * s);
                gr.DrawLines(pens[(int)r.Kind], pts);
            }
            foreach (Pen p in pens) p.Dispose();
            using (Pen path = new Pen(Color.FromArgb(214, 140, 60), 1.4f))
                foreach (FootPath fp in net.Paths)
                {
                    if (fp.Points.Count < 2) continue;
                    PointF[] pts = new PointF[fp.Points.Count + (fp.Closed ? 1 : 0)];
                    for (int i = 0; i < pts.Length; i++) { float[] q = fp.Points[i % fp.Points.Count]; pts[i] = new PointF(q[0] * s, q[1] * s); }
                    gr.DrawLines(path, pts);
                }
            using (Pen deck = new Pen(Color.FromArgb(150, 90, 30), 4.5f), top = new Pen(Color.FromArgb(235, 180, 100), 2f))
                foreach (float[] b in net.Bridges)
                {
                    gr.DrawLine(deck, b[0] * s, b[1] * s, b[2] * s, b[3] * s);
                    gr.DrawLine(top, b[0] * s, b[1] * s, b[2] * s, b[3] * s);
                }
        }

        // ---------------- world size ----------------

        /// <summary>The open world's size in metres, from the size of its height image (1537 px -> 1536 m).</summary>
        private static float OpenWorldSize(object world)
        {
            string path = Path.Combine(TerrainSampler.TempDir, "height_size.png");
            if (File.Exists(path)) File.Delete(path);
            if (!Caw.SaveHeightImage(world, path)) return 0;
            int w, h; ImageIO.PngSize(path, out w, out h);
            return w % 2 == 1 ? w - 1 : w;
        }

        private void FillSizes()
        {
            cbSize.Items.Clear();
            if (openWorldSize > 0) cbSize.Items.Add("This world (" + openWorldSize + " m)");
            cbSize.Items.Add("Small (512 m)"); cbSize.Items.Add("Medium (1024 m)"); cbSize.Items.Add("Large (2048 m)");
            cbSize.SelectedIndex = openWorldSize > 0 ? 0 : 1;
        }

        private float ChosenSize()
        {
            int i = cbSize.SelectedIndex;
            if (openWorldSize > 0) { if (i <= 0) return openWorldSize; i--; }
            return Sizes[Math.Max(0, Math.Min(Sizes.Length - 1, i))];
        }

        private void OnSizeChanged()
        {
            worldSize = ChosenSize();
            if (openWorldSize > 0 && Math.Abs(worldSize - openWorldSize) > 1)
                Status(string.Format("The open world is {0} m. CAW can't change a world's size: use 'Heightmap for New World...' to start " +
                    "a {1} m world, or switch back to 'This world' to apply here.", openWorldSize, worldSize));
            Schedule();
        }

        /// <summary>Saves the map as a 16-bit heightmap for CAW's File > New World, at the chosen size.</summary>
        private void SaveForNewWorld()
        {
            if (busy || Injector.BusyElsewhere(this)) return;
            int size = (int)ChosenSize();
            int img = 512;
            foreach (int v in Sizes) if (Math.Abs(v - size) < Math.Abs(img - size)) img = v;
            GenSettings g = Gen();
            // CAW's New World offers 100/200/300 m, and its Height scale box takes a typed value for taller worlds
            int scale = Math.Max(100, (int)Math.Ceiling(g.MaxHeight / 100f) * 100);
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.RestoreDirectory = true; // the process-wide current folder stays where it was
                d.Title = "Save heightmap for New World";
                d.Filter = "PNG heightmap|*.png";
                d.FileName = string.Format("CAWPlus_{0}_seed{1}.png", img, g.Seed);
                string hm = Path.Combine(Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Electronic Arts"),
                    "The Sims 3 Create A World Tool"), Path.Combine("UserToolData", "HeightMaps"));
                if (Directory.Exists(hm)) d.InitialDirectory = hm;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                SetBusy(true);
                try
                {
                    CheckJob job = Job(g, img);
                    TerrainGen.Result full = (TerrainGen.Result)RunInBackground(delegate ()
                    {
                        TerrainChecks.Result tc; TerrainGen.Result f = GenChecked(g, img, img, true, BgProgress, job, out tc);
                        return f;
                    });
                    Grid grey = new Grid(img, img);
                    for (int i = 0; i < grey.D.Length; i++) grey.D[i] = Math.Max(0, Math.Min(1, full.Heights.D[i] / scale));
                    ImageIO.WriteGray16Png(grey, d.FileName);
                    Log.Info("Saved heightmap for New World: " + d.FileName + " (" + img + " px, height scale " + scale + " m)");
                    SetBusy(false);
                    Status(string.Format("Saved {0}. In CAW: File > New World, choose this image and Height scale {1} (type it in if it isn't listed). Then open " +
                        "Generate Terrain in the new world (same seeds) and click Apply terrain to set exact heights and the sea level.",
                        Path.GetFileName(d.FileName), scale));
                }
                catch (Exception ex) { Log.Error("Saving heightmap failed", ex); SetBusy(false); Status("Saving failed: " + ex.Message); }
            }
        }

        // ---------------- apply terrain ----------------

        private string BackupDir(object world)
        {
            string d = Caw.WorldDir(Path.Combine(Log.Dir, "Backups"), world);
            Directory.CreateDirectory(d);
            return d;
        }

        private void ApplyTerrain()
        {
            if (busy || Injector.BusyElsewhere(this)) return;
            object world = Caw.CurrentWorld;
            if (world == null) { Status("Open a world first (a blank one is fine)."); return; }
            if (openWorldSize > 0 && Math.Abs(ChosenSize() - openWorldSize) > 1)
            {
                if (MessageBox.Show(this, string.Format("You chose a {0} m world, but the open world is {1} m.\n\nCAW can't change a world's size. " +
                        "Pick the right size before you start: use 'Heightmap for New World...' and File > New World for a {0} m world.\n\n" +
                        "Apply to this {1} m world instead?", ChosenSize(), openWorldSize), "World size",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                cbSize.SelectedIndex = 0;
            }
            if (MessageBox.Show(this, "Replace the open world's terrain with this map?\n\nThe current heights are backed up first; " +
                    "'Undo terrain' puts them back. Existing ponds are removed. Lots and objects are not moved, so this is best on a new or empty world.",
                    "Generate Terrain", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            if (previewStop != null) previewStop.Requested = true; // CPU for the full map, not a preview
            SetBusy(true);
            string backup = null, clipNote = "";
            bool imported = false;
            try
            {
                TerrainInfo ti = TerrainSampler.Analyze(world, Caw.LayerCount(Caw.Terrain(world)), OnProgress);
                string tmp = TerrainSampler.TempDir;
                string current = Path.Combine(tmp, "height_current.png");
                if (File.Exists(current)) File.Delete(current);
                if (!Caw.SaveHeightImage(world, current)) throw new IOException("CAW could not export the current heights.");
                int W, H; ImageIO.PngSize(current, out W, out H);
                backup = Path.Combine(BackupDir(world), "height_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
                File.Copy(current, backup, true);
                ManualEdits.PruneBackups(BackupDir(world), 10);
                worldSize = ti.WorldSize; seaLevel = ti.WaterLevel;
                Log.Info(string.Format("Generate Terrain: height image {0}x{1}, world {2} m, sea {3:F1} m, backup {4}", W, H, worldSize, seaLevel, backup));

                GenSettings g = Gen();
                CheckJob job = Job(g, worldSize);
                float ws = worldSize;
                // the computation runs on a worker thread so CAW stays responsive; CAW calls stay on this thread
                TerrainChecks.Result tc = null;
                DateTime t0 = DateTime.Now;
                TerrainGen.Result full = (TerrainGen.Result)RunInBackground(delegate ()
                {
                    TerrainGen.Result f = GenChecked(g, W, ws, true, BgProgress, job, out tc);
                    return f;
                });
                Log.Info(string.Format("Generate Terrain: map worked out in {0:F1} s{1}", (DateTime.Now - t0).TotalSeconds,
                    tc != null ? "; " + tc + " (seed variation " + g.Seed + ")" : ""));

                // CAW's 16-bit import maps grey to height linearly; measure that mapping instead of assuming it.
                OnProgress("Calibrating CAW's height import", 88);
                imported = true; // from here on the world's heights are being replaced
                // ponds made for the old land would flood the new land up to their level
                try { int gone = Caw.RemoveAllPonds(world); if (gone > 0) Log.Info("Removed " + gone + " pond(s) left from the old land"); madePonds.Clear(); }
                catch (Exception ex) { Log.Error("Removing old ponds failed", ex); }
                float err = HeightImport.Apply(world, ti, full.Heights, W, H, OnProgress, out clipNote);
                // ponds above the sea (as the Like world has): CAW's own ponds, flooding each dug hollow to its level
                MakePonds(world, ti, full, W);
                // where the town belongs (a river fork's point): Generate Roads reads it (#139)
                try
                {
                    string hint = Path.Combine(BackupDir(world), "town_hint.txt");
                    if (full.TownHint != null) File.WriteAllText(hint, full.TownHint[0].ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " " + full.TownHint[1].ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                    else if (File.Exists(hint)) File.Delete(hint);
                }
                catch (Exception ex) { Log.Error("Saving the town hint failed", ex); }
                // the water only shows after CAW's own sea-level command runs (the tester's v0.40 test: set by hand)
                try { Log.Info("Sea level refreshed at " + ti.WaterLevel.ToString("F2") + " m: " + Caw.RefreshSea(world, ti.WaterLevel)); }
                catch (Exception ex) { Log.Error("Refreshing the sea level failed", ex); }
                preview = TerrainGen.Generate(g, 384, worldSize, false, null);
                ShowMap(Render(full.Heights, full.Cell, g.SeaLevel, null, 512, full.Ponds), full.Heights, full.Cell, g.SeaLevel);
                SetBusy(false);
                Injector.Done("terrain");
                Status(string.Format("Terrain applied (mean height error {0:F2} m). {1}Backup: {2}{3}", err,
                    "Next: Generate Roads (Roads and Trees toolbar) plans roads and lots over this land. ", Path.GetFileName(backup), clipNote));
            }
            catch (Exception ex)
            {
                Log.Error("Applying terrain failed", ex);
                // don't leave the calibration ramp or a half-done import in the world: put the backup back
                bool back = false;
                // (not if another world was opened meanwhile: the backup belongs to the first one)
                if (imported && backup != null && File.Exists(backup) && !(ex is WorldChangedException))
                    try { back = Caw.ImportHeightImage(world, backup); } catch (Exception ex2) { Log.Error("Restoring the heights failed", ex2); }
                SetBusy(false);
                Status("Applying terrain failed: " + ex.Message + (!imported ? "" : back ? " The previous heights were put back." :
                    " Click 'Undo terrain' to put the previous heights back."));
            }
        }

        private readonly List<object> madePonds = new List<object>();

        private void MakePonds(object world, TerrainInfo ti, TerrainGen.Result full, int W)
        {
            int made = 0;
            foreach (float[] q in full.Ponds)
            {
                try
                {
                    float wx, wz;
                    TerrainSampler.Map(ti.Orientation, q[0] / ti.WorldSize, q[1] / ti.WorldSize, out wx, out wz);
                    float span, maxSpan = Math.Max(100f, 8 * q[2]);
                    object p = Caw.CreatePondChecked(world, wx * ti.WorldSize, wz * ti.WorldSize, q[3], ti.WorldSize / (W - 1), maxSpan, out span);
                    if (p != null) { madePonds.Add(p); made++; }
                    Log.Info(string.Format("Pond at ({0:F0}, {1:F0}) m, radius {2:F0} m, level {3:F1} m: {4}, water {5:F0} m across", wx * ti.WorldSize, wz * ti.WorldSize, q[2], q[3],
                        p != null ? "made" : span > maxSpan ? "removed: it flooded more than " + maxSpan.ToString("F0") + " m" : "CAW refused", span));
                }
                catch (Exception ex) { Log.Error("Making a pond failed", ex); }
            }
            if (full.Ponds.Count > 0) Log.Info("Ponds: " + made + " of " + full.Ponds.Count + " made");
        }

        private void UndoTerrain()
        {
            if (busy || Injector.BusyElsewhere(this)) return;
            object world = Caw.CurrentWorld;
            if (world == null) return;
            string dir = BackupDir(world);
            string[] files = Directory.GetFiles(dir, "height_*.png");
            if (files.Length == 0) { Status("No terrain backups for this world."); return; }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            string latest = files[files.Length - 1];
            if (MessageBox.Show(this, "Restore the heights from " + Path.GetFileName(latest) + "?", "Undo terrain", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            bool ok;
            try
            {
                // a backup of another size isn't this world's (a world can't be resized)
                int bw, bh, cw, ch;
                string now = Path.Combine(dir, "check.png");
                if (ImageIO.PngSize(latest, out bw, out bh) && Caw.SaveHeightImage(world, now) && ImageIO.PngSize(now, out cw, out ch) && (bw != cw || bh != ch))
                {
                    Status("That backup is " + bw + "x" + bh + ", this world is " + cw + "x" + ch + ": not restored.");
                    return;
                }
                ok = Caw.ImportHeightImage(world, latest);
            }
            catch (Exception ex) { Log.Error("Undo terrain failed", ex); Status("Undo terrain failed: " + ex.Message); return; }
            if (ok) { try { File.Move(latest, latest + ".restored"); } catch { } }
            if (ok)
            {
                foreach (object p in madePonds) try { Caw.RemovePond(world, p); } catch (Exception ex) { Log.Error("Removing a pond failed", ex); }
                madePonds.Clear();
            }
            Status(ok ? "Heights restored from " + Path.GetFileName(latest) + "." : "CAW refused the backup image.");
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
            btnApply.Enabled = btnUndo.Enabled = !b;
            UseWaitCursor = b;
            if (!b) progress.Value = 0;
        }
    }
}
