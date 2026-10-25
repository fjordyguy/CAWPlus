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
    /// Distant Terrain (Terrain toolbar), one of the last steps: makes a custom distant terrain object for the open
    /// world (DistantMaker: a ring of land that carries on from the world's own edge, coloured from its paint and
    /// trees), writes it as a .package into CAW's own Packages folder (for the game it's copied into Mods\Packages by hand), and
    /// after CAW has been restarted places it in the world in place of the old one.
    /// </summary>
    public sealed class DistantTerrainForm : Form
    {
        private TrackBar tbRing, tbHills, tbRise, tbFar, tbVariety, tbCarry;
        private Label lblRing, lblHills, lblRise, lblFar, lblVariety, lblCarry;
        private NumericUpDown nudSeed;
        private CheckBox chkRemove, chk3D;
        private Button btnRead, btnMake, btnPlace, btnFolder;
        private PictureBox picture;
        private ProgressBar progress;
        private Label status, info;
        private Timer previewTimer;
        private bool busy;
        private MapView view;

        private TerrainInfo ti;
        private DistantInput input;
        private DistantResult preview;
        private Bitmap map2D;
        private string albedoNote = "", treeNote = "";
        private float[] gain = { 1, 1, 1 };   // colour match for this world (saved in Documents\CAWPlus\Worlds\<world>)
        private volatile int previewRun;

        public DistantTerrainForm()
        {
            SuspendLayout();
            Text = "Distant Terrain  (CAW Plus)";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(900, 600);
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 8.25f);
            int x = 10, y = 10, colW = 360;

            GroupBox gb = new GroupBox(); gb.Text = "The land beyond the map"; gb.SetBounds(x, y, colW, 320);
            int ry = 22;
            tbRing = Slider(gb, "Distance to the horizon", 500, 3000, 1500, ref ry, out lblRing);
            tbHills = Slider(gb, "Hills", 0, 200, 100, ref ry, out lblHills);
            tbRise = Slider(gb, "Rises towards the horizon", 0, 200, 100, ref ry, out lblRise);
            tbFar = Slider(gb, "Beyond the sea", 0, 100, 40, ref ry, out lblFar);
            new ToolTip().SetToolTip(tbFar, "Where the world's edge is sea: 0 = open sea to the horizon, higher = a far coast and islands.");
            tbVariety = Slider(gb, "Variety", 0, 200, 100, ref ry, out lblVariety);
            new ToolTip().SetToolTip(tbVariety, "How much the land changes round the ring: lowland with broad valleys in some directions,\n" +
                "hills in others, mountain ranges in others. 0 = the same all round.");
            tbCarry = Slider(gb, "Rivers carry on", 0, 100, 60, ref ry, out lblCarry);
            new ToolTip().SetToolTip(tbCarry, "Rivers and inlets that reach the map edge: this share carries on to the horizon (some through a lake);\n" +
                "the others open into a lake or end in a valley. Which one is which changes with the seed.");
            Label ls = new Label(); ls.Text = "Seed"; ls.SetBounds(10, ry + 4, 34, 16); gb.Controls.Add(ls);
            nudSeed = new NumericUpDown(); nudSeed.Minimum = 1; nudSeed.Maximum = 999999; nudSeed.Value = 1; nudSeed.SetBounds(46, ry, 70, 20); gb.Controls.Add(nudSeed);
            Button bNew = new Button(); bNew.Text = "New"; bNew.SetBounds(122, ry - 1, 60, 23);
            bNew.Click += delegate { nudSeed.Value = new Random().Next(1, 999999); };
            gb.Controls.Add(bNew);
            ry += 30;
            chkRemove = new CheckBox(); chkRemove.Text = "Place: replace the world's old distant terrain"; chkRemove.Checked = true; chkRemove.SetBounds(10, ry, colW - 20, 20);
            gb.Controls.Add(chkRemove);
            Controls.Add(gb);
            y += 326;

            btnRead = new Button(); btnRead.Text = "Read world"; btnRead.SetBounds(x, y, 86, 28);
            btnRead.Click += delegate { ReadWorld(); };
            new ToolTip().SetToolTip(btnRead, "Reads the world's land, sea, paint colours and trees again (after changing them).");
            btnMake = new Button(); btnMake.Text = "Make distant terrain"; btnMake.SetBounds(x + 89, y, 130, 28); btnMake.Font = new Font(Font, FontStyle.Bold);
            btnMake.Click += delegate { Make(); };
            new ToolTip().SetToolTip(btnMake, "Builds the distant terrain at full detail and installs it as custom content\n" +
                "(CAW's own Packages folder). Restart CAW, then click 'Place in world'.\n" +
                "For the game, copy the .package into Documents\\Electronic Arts\\The Sims 3\\Mods\\Packages yourself.");
            btnPlace = new Button(); btnPlace.Text = "Place in world"; btnPlace.SetBounds(x + 222, y, 100, 28);
            btnPlace.Click += delegate { Place(); };
            Controls.Add(btnRead); Controls.Add(btnMake); Controls.Add(btnPlace);
            y += 34;
            btnFolder = new Button(); btnFolder.Text = "Open folder"; btnFolder.SetBounds(x, y, 86, 24);
            btnFolder.Click += delegate { OpenFolder(); };
            Controls.Add(btnFolder);
            Button bMatch = new Button(); bMatch.Text = "Match colour..."; bMatch.SetBounds(x + 89, y, 110, 24);
            bMatch.Click += delegate { MatchColour(); };
            new ToolTip().SetToolTip(bMatch, "After placing it: look across the map edge in CAW's 3D view, then click your own ground\n" +
                "and the distant terrain right next to it. The colours are corrected to match;\nmake the distant terrain again and restart CAW to see it.");
            Button bReset = new Button(); bReset.Text = "Reset colour"; bReset.SetBounds(x + 202, y, 90, 24);
            bReset.Click += delegate { SetGain(new float[] { 1, 1, 1 }, true); };
            Controls.Add(bMatch); Controls.Add(bReset);
            y += 30;
            progress = new ThemedProgress(); progress.SetBounds(x, y, colW, 14); Controls.Add(progress);
            y += 18;
            status = new Label(); status.SetBounds(x, y, colW, 600 - y - 4); Controls.Add(status);

            picture = new PictureBox(); picture.SetBounds(x + colW + 18, 10, 512, 512);
            picture.SizeMode = PictureBoxSizeMode.Zoom; picture.BackColor = Color.FromArgb(40, 40, 40); picture.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(picture);
            chk3D = new CheckBox(); chk3D.Text = "3D view"; chk3D.Checked = true; chk3D.SetBounds(x + colW + 18, 526, 80, 22);
            chk3D.CheckedChanged += delegate { Redraw(); };
            Button bTurn = new Button(); bTurn.Text = "Turn"; bTurn.SetBounds(x + colW + 102, 525, 60, 23);
            CheckBox chkExag = new CheckBox(); chkExag.Text = "Exaggerate heights (x2)"; chkExag.SetBounds(x + colW + 172, 526, 170, 22);
            view = new MapView(picture, chk3D, chkExag);
            bTurn.Click += delegate { view.Turn(); };
            new ToolTip().SetToolTip(chk3D, "The world and its distant terrain in 3D at true height. Drag to orbit, right-drag to pan, wheel to zoom, double-click to reset.");
            Controls.Add(chk3D); Controls.Add(bTurn); Controls.Add(chkExag);
            info = new Label(); info.SetBounds(x + colW + 18, 552, 512, 46); Controls.Add(info);

            previewTimer = new Timer(); previewTimer.Interval = 400;
            previewTimer.Tick += delegate { previewTimer.Stop(); Preview(); };
            EventHandler changed = delegate { UpdateLabels(); if (input != null) { previewTimer.Stop(); previewTimer.Start(); } };
            tbRing.ValueChanged += changed; tbHills.ValueChanged += changed; tbRise.ValueChanged += changed; tbFar.ValueChanged += changed;
            tbVariety.ValueChanged += changed; tbCarry.ValueChanged += changed;
            nudSeed.ValueChanged += changed;
            UpdateLabels();
            Shown += delegate { ReadWorld(); };
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            Theme.Apply(this);
        }

        private TrackBar Slider(Control parent, string label, int min, int max, int value, ref int y, out Label val)
        {
            Label l = new Label(); l.Text = label; l.SetBounds(10, y + 6, 140, 16); parent.Controls.Add(l);
            TrackBar t = new ThemedSlider(); t.AutoSize = false; t.Minimum = min; t.Maximum = max; t.Value = value; t.TickFrequency = (max - min) / 10;
            t.SetBounds(150, y, 120, 28); parent.Controls.Add(t);
            val = new Label(); val.SetBounds(274, y + 6, 80, 16); parent.Controls.Add(val);
            y += 34;
            return t;
        }

        private void UpdateLabels()
        {
            lblRing.Text = tbRing.Value + " m";
            lblHills.Text = tbHills.Value + "%";
            lblRise.Text = tbRise.Value + "%";
            lblFar.Text = tbFar.Value == 0 ? "open sea" : tbFar.Value + "%";
            lblVariety.Text = tbVariety.Value + "%";
            lblCarry.Text = tbCarry.Value + "%";
        }

        private DistantSettings Settings(bool full)
        {
            DistantSettings s = new DistantSettings();
            s.Ring = tbRing.Value; s.Hills = tbHills.Value / 100f; s.Rise = tbRise.Value / 100f; s.FarLand = tbFar.Value / 100f;
            s.Variety = tbVariety.Value / 100f; s.CarryOn = tbCarry.Value / 100f; s.Gain = (float[])gain.Clone();
            s.Seed = (int)nudSeed.Value;
            if (!full) { s.Field = 384; s.TexSize = 512; s.NormalSize = 64; }
            return s;
        }

        // ---------------- reading the world ----------------

        private void ReadWorld()
        {
            if (busy || Injector.BusyElsewhere(this)) return;
            object world = Caw.CurrentWorld;
            if (world == null) { Status("Open a world first."); return; }
            SetBusy(true);
            try
            {
                ti = TerrainSampler.Analyze(world, Caw.LayerCount(Caw.Terrain(world)), OnProgress);
                DistantInput inp = new DistantInput();
                inp.Heights = ti.Heights; inp.Orientation = ti.Orientation; inp.Origin = ti.Origin; inp.WorldSize = ti.WorldSize; inp.Sea = ti.WaterLevel;
                OnProgress("Reading the paint colours", 50);
                try { inp.Albedo = Albedo(world, 512, out albedoNote); inp.AlbW = inp.AlbH = 512; }
                catch (Exception ex) { Log.Error("Distant terrain: reading the paint failed", ex); inp.Albedo = null; albedoNote = "paint not read"; }
                OnProgress("Reading the trees", 85);
                try { inp.Trees = Trees(ti, 256, out treeNote); }
                catch (Exception ex) { Log.Error("Distant terrain: reading the trees failed", ex); treeNote = "trees not read"; }
                input = inp;
                gain = LoadGain(world);
                SetBusy(false);
                Status(string.Format("Read the world: {0:F0} m, sea level {1:F1} m; {2}; {3}.", ti.WorldSize, ti.WaterLevel, albedoNote, treeNote));
                Log.Info("Distant terrain: " + albedoNote + "; " + treeNote);
                Preview();
            }
            catch (Exception ex) { Log.Error("Distant terrain: reading the world failed", ex); SetBusy(false); Status("Reading the world failed: " + ex.Message); }
        }

        /// <summary>
        /// The world's ground colour per map pixel: each paint layer's texture colour (the matching file in
        /// SourceTextures, else a colour for its kind) laid over the ones below it by the layer's opacity.
        /// </summary>
        private int[] Albedo(object world, int size, out string note)
        {
            object terr = Caw.Terrain(world);
            int n = Caw.LayerCount(terr);
            List<string> files = TextureLibrary.Scan(Caw.TerrainTexturesPath());
            float[] r = new float[size * size], g = new float[size * size], b = new float[size * size];
            string tmp = Path.Combine(TerrainSampler.TempDir, "distant_layer.png");
            int found = 0, used = 0;
            for (int i = 0; i < n; i++)
            {
                string name = Caw.LayerName(terr, i);
                if (name.StartsWith(EdgeBlend.LayerPrefix, StringComparison.OrdinalIgnoreCase)) continue;   // our own world-edge layer
                string texPath = Caw.LayerTexturePath(terr, i);
                float cr, cg, cb; bool fromFile = LayerColour(name, texPath, files, out cr, out cg, out cb);
                Log.Info(string.Format("Distant terrain colour, layer {0} '{1}': {2:F0},{3:F0},{4:F0} ({5})", i, name, cr, cg, cb,
                    fromFile ? "texture " + (colourFile[(name ?? "").ToLowerInvariant()] ?? "") : "not found, a colour for its kind; CAW's texture path: " + (texPath ?? "none")));
                if (fromFile) found++;
                Grid a = null;
                if (i > 0)
                {
                    if (File.Exists(tmp)) File.Delete(tmp);
                    if (!Caw.ExportLayer(world, i, tmp) || !File.Exists(tmp)) continue;
                    Grid full = ImageIO.ReadPngChannel0(tmp);
                    a = Resample(full, size);
                }
                used++;
                for (int k = 0; k < r.Length; k++)
                {
                    float w = a == null ? 1f : Math.Max(0f, Math.Min(1f, a.D[k] / 255f));
                    r[k] += (cr - r[k]) * w; g[k] += (cg - g[k]) * w; b[k] += (cb - b[k]) * w;
                }
                OnProgress("Reading the paint colours", 50 + 30 * (i + 1) / Math.Max(1, n));
            }
            int[] px = new int[size * size];
            for (int k = 0; k < px.Length; k++) px[k] = (Clamp255(r[k]) << 16) | (Clamp255(g[k]) << 8) | Clamp255(b[k]);
            note = string.Format("paint: {0} layers, {1} textures found in SourceTextures", used, found);
            return px;
        }

        private static Grid Resample(Grid full, int size)
        {
            Grid o = new Grid(size, size);
            float sx = (float)full.W / size, sy = (float)full.H / size;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) o.D[y * size + x] = full.At((x + 0.5f) * sx - 0.5f, (y + 0.5f) * sy - 0.5f);
            return o;
        }

        private static readonly Dictionary<string, float[]> colourCache = new Dictionary<string, float[]>();
        private static readonly Dictionary<string, string> colourFile = new Dictionary<string, string>();

        /// <summary>
        /// A layer's texture colour: the file CAW says the layer uses (GetPaintingLayerTextureFilePath; relative paths
        /// tried against SourceTextures and CAW's folder), else a file in SourceTextures with the layer's name, else a
        /// colour for its kind (return false).
        /// </summary>
        private static bool LayerColour(string layerName, string texPath, List<string> files, out float r, out float g, out float b)
        {
            string key = (layerName ?? "").ToLowerInvariant();
            float[] c;
            if (!colourCache.TryGetValue(key, out c))
            {
                List<string> tries = new List<string>();
                if (!string.IsNullOrEmpty(texPath))
                {
                    tries.Add(texPath);
                    string folder = Caw.TerrainTexturesPath();
                    if (!string.IsNullOrEmpty(folder)) tries.Add(Path.Combine(folder, Path.GetFileName(texPath)));
                    tries.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, texPath.TrimStart('\\', '/')));
                    string tb = Path.GetFileNameWithoutExtension(texPath);
                    foreach (string f in files) if (string.Equals(Path.GetFileNameWithoutExtension(f), tb, StringComparison.OrdinalIgnoreCase)) tries.Add(f);
                }
                string bare = Path.GetFileNameWithoutExtension(layerName ?? "");
                foreach (string f in files) if (string.Equals(Path.GetFileNameWithoutExtension(f), bare, StringComparison.OrdinalIgnoreCase)) tries.Add(f);
                foreach (string f in tries)
                {
                    try
                    {
                        if (!File.Exists(f)) continue;
                        TextureFeatures tf = TextureAnalyzer.Analyze(f);
                        if (tf != null) { c = new float[] { tf.R * 255, tf.G * 255, tf.B * 255, 1 }; colourFile[key] = f; break; }
                    }
                    catch (Exception ex) { Log.Error("Distant terrain: texture " + f, ex); }
                }
                if (c == null)
                {
                    // not found: a colour for its kind
                    if (TextureLibrary.Matches(PaintRole.Rock, key)) c = new float[] { 124, 118, 108, 0 };
                    else if (TextureLibrary.Matches(PaintRole.Shore, key)) c = new float[] { 196, 184, 148, 0 };
                    else if (TextureLibrary.Matches(PaintRole.Dirt, key)) c = new float[] { 128, 108, 82, 0 };
                    else c = new float[] { 98, 122, 66, 0 };
                }
                colourCache[key] = c;
            }
            r = c[0]; g = c[1]; b = c[2];
            return c[3] > 0;
        }

        /// <summary>Tree cover per map cell (0..1) from the trees in the world's layers.</summary>
        private static Grid Trees(TerrainInfo ti, int size, out string note)
        {
            Grid t = new Grid(size, size);
            int count = 0;
            foreach (Caw.WorldObject o in Caw.WorldObjects())
            {
                if (!o.Tree && (o.Name ?? "").IndexOf("tree", StringComparison.OrdinalIgnoreCase) < 0) continue;
                float mx, my; TerrainSampler.WorldToMap(ti, o.X, o.Z, out mx, out my);
                int x = (int)(mx / ti.MapW * size), y = (int)(my / ti.MapH * size);
                if (x < 0 || y < 0 || x >= size || y >= size) continue;
                t.D[y * size + x] += 1; count++;
            }
            note = count + " trees";
            if (count == 0) return null;
            float cellArea = (ti.WorldSize / size) * (ti.WorldSize / size);
            for (int k = 0; k < t.D.Length; k++) t.D[k] = Math.Min(1f, t.D[k] * 60f / cellArea);   // a tree crown covers ~60 m2
            return t.Blur(1.5f);
        }

        private static int Clamp255(float v) { return v < 0 ? 0 : (v > 255 ? 255 : (int)v); }

        // ---------------- colour match ----------------

        private static string GainFile(object world) { return Path.Combine(Caw.WorldDir(Path.Combine(Log.Dir, "Worlds"), world), "distant_colour.txt"); }

        private static float[] LoadGain(object world)
        {
            try
            {
                string f = GainFile(world);
                if (File.Exists(f))
                {
                    string[] p = File.ReadAllText(f).Trim().Split(' ');
                    if (p.Length == 3)
                        return new float[] { float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
                            float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture) };
                }
            }
            catch (Exception ex) { Log.Error("Distant terrain: reading the colour match failed", ex); }
            return new float[] { 1, 1, 1 };
        }

        private void SetGain(float[] g, bool save)
        {
            gain = g;
            object world = Caw.CurrentWorld;
            if (save && world != null)
            {
                try
                {
                    File.WriteAllText(GainFile(world), string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F4} {1:F4} {2:F4}", g[0], g[1], g[2]));
                }
                catch (Exception ex) { Log.Error("Distant terrain: saving the colour match failed", ex); }
            }
            Log.Info(string.Format("Distant terrain colour match: x{0:F2} / {1:F2} / {2:F2} (red / green / blue)", g[0], g[1], g[2]));
            Status(g[0] == 1 && g[1] == 1 && g[2] == 1 ? "Colour match reset."
                : string.Format("Colour matched (x{0:F2} red, x{1:F2} green, x{2:F2} blue). Click 'Make distant terrain', then restart CAW to see it.", g[0], g[1], g[2]));
            if (input != null) Preview();
        }

        /// <summary>
        /// Two clicks on a picture of CAW's 3D view: your own ground near the map edge, then the distant terrain right
        /// beside it (same distance, so the fog and light are the same). The colour correction becomes
        /// old x ground / distant per channel (clamped 0.4-2.5), so repeating it refines it.
        /// </summary>
        private void MatchColour()
        {
            if (busy || Caw.CurrentWorld == null) return;
            Bitmap shot;
            Hide();
            try { Application.DoEvents(); Thread.Sleep(150); shot = Caw.CaptureRenderPanel(); }
            catch (Exception ex) { Log.Error("Screen capture failed", ex); shot = null; }
            finally { Show(); }
            if (shot == null) { Status("Couldn't capture CAW's 3D view."); return; }
            using (Form f = new Form())
            {
                f.Text = "1 of 2: click your own ground near the map edge";
                f.StartPosition = FormStartPosition.CenterParent; f.ShowInTaskbar = false;
                float k = Math.Min(1f, Math.Min(1000f / shot.Width, 680f / shot.Height));
                f.ClientSize = new Size((int)(shot.Width * k), (int)(shot.Height * k));
                PictureBox pb = new PictureBox(); pb.Dock = DockStyle.Fill; pb.SizeMode = PictureBoxSizeMode.StretchImage; pb.Image = shot;
                pb.Cursor = Cursors.Cross;
                Color ground = Color.Empty, distant = Color.Empty;
                pb.MouseClick += delegate (object o, MouseEventArgs e)
                {
                    Color c = EdgeBlend.AverageColour(shot, (int)(e.X / k), (int)(e.Y / k), 6);
                    if (ground.IsEmpty) { ground = c; f.Text = "2 of 2: click the distant terrain right next to it"; }
                    else { distant = c; f.DialogResult = DialogResult.OK; }
                };
                f.Controls.Add(pb);
                if (Theme.Dialog(f, this) == DialogResult.OK && !ground.IsEmpty && !distant.IsEmpty)
                {
                    float[] g = (float[])gain.Clone();
                    int[] gc = { ground.R, ground.G, ground.B }, dc = { distant.R, distant.G, distant.B };
                    for (int i = 0; i < 3; i++) g[i] = Math.Max(0.4f, Math.Min(2.5f, g[i] * Math.Max(1, gc[i]) / Math.Max(1, dc[i])));
                    Log.Info(string.Format("Distant terrain colour match: ground {0},{1},{2}, distant {3},{4},{5}", ground.R, ground.G, ground.B, distant.R, distant.G, distant.B));
                    SetGain(g, true);
                }
            }
            shot.Dispose();
        }

        // ---------------- preview (worker thread) ----------------

        private void Preview()
        {
            if (input == null) return;
            DistantInput inp = input; DistantSettings s = Settings(false);
            int run = ++previewRun;
            UseWaitCursor = true;
            Thread t = new Thread(delegate ()
            {
                DistantResult r = null; Bitmap bmp = null; Exception error = null;
                try
                {
                    r = DistantMaker.Make(inp, s, null);
                    if (run == previewRun) bmp = ToBitmap(DistantMaker.PreviewColours(r, inp.Sea), r.TexSize, r.Half / r.R);
                }
                catch (Exception ex) { error = ex; }
                try { BeginInvoke(new MethodInvoker(delegate { PreviewDone(run, r, bmp, error); })); }
                catch (Exception) { if (bmp != null) bmp.Dispose(); }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void PreviewDone(int run, DistantResult r, Bitmap bmp, Exception error)
        {
            if (run != previewRun) { if (bmp != null) bmp.Dispose(); return; }
            UseWaitCursor = busy;
            if (error != null) { Log.Error("Distant terrain preview failed", error); Status("Preview failed: " + error.Message); return; }
            preview = r;
            Bitmap old = map2D; map2D = bmp; Redraw();
            if (old != null && !ReferenceEquals(old, picture.Image)) old.Dispose();
            info.Text = r.Summary + " " + PlaceText();
            Status("Preview ready (the square in the middle is your world). 'Make distant terrain' builds it at full detail.");
        }

        /// <summary>Where the object goes (Place in world does it): the middle of the map, Y 0, no rotation.</summary>
        private string PlaceText()
        {
            if (ti == null) return "";
            float c = ti.Origin + ti.WorldSize / 2f;
            return string.Format("Goes at X {0:F0}, Y 0, Z {0:F0}, rotation 0.", c);
        }

        private static Bitmap ToBitmap(int[] px, int n, float holeShare)
        {
            Bitmap b = new Bitmap(n, n);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++) b.SetPixel(x, y, Color.FromArgb(255, Color.FromArgb(px[y * n + x])));
            using (Graphics g = Graphics.FromImage(b))
            using (Pen p = new Pen(Color.FromArgb(200, 255, 255, 255), 1))
            {
                float h = holeShare * n / 2f;
                g.DrawRectangle(p, n / 2f - h, n / 2f - h, 2 * h, 2 * h);
            }
            return b;
        }

        private void Redraw()
        {
            if (map2D == null || preview == null) return;
            float km = (preview.Field.W - 1) * preview.FieldCell / 1000f;
            view.SetMap(map2D, preview.Field, preview.FieldCell, ti != null ? ti.WaterLevel : 0,
                string.Format(System.Globalization.CultureInfo.InvariantCulture, "Area {0:0.0} x {0:0.0} km (world + distant terrain)", km));
        }

        // ---------------- making the package ----------------

        private static string OwnFolder { get { string d = Path.Combine(Log.Dir, "DistantTerrain"); Directory.CreateDirectory(d); return d; } }

        /// <summary>CAW's own custom content folder: Packages in the CAW install folder (where CAWPlus.exe is).</summary>
        private static string CawPackagesFolder() { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Packages"); }

        private static string FileName(object world) { return "CAWPlus_DistantTerrain_" + Caw.WorldKey(world) + ".package"; }

        private void Make()
        {
            if (busy || Injector.BusyElsewhere(this)) return;
            object world = Caw.CurrentWorld;
            if (world == null || input == null) { Status("Open a world and read it first."); return; }
            SetBusy(true);
            try
            {
                OnProgress("Finding a distant terrain object to copy", 1);
                string tnote;
                DistantMaker.Template tpl = DistantTerrain.FindTemplate(Caw.Catalog(), out tnote);
                if (tpl == null) { SetBusy(false); Status("Can't make it: " + tnote + "."); return; }
                string worldKey = Caw.WorldKey(world);
                DistantInput inp = input; DistantSettings s = Settings(true);
                byte[] pkg = null; DistantResult r = null;
                RunInBackground(delegate ()
                {
                    r = DistantMaker.Make(inp, s, BgProgress);
                    BgProgress("Writing the package", 95);
                    pkg = DistantMaker.Package(r, worldKey, Caw.WorldName(world), tpl);
                    return null;
                });
                try { Log.Info(EdgeCheck(world, inp, r)); } catch (Exception ex) { Log.Error("Distant terrain edge check failed", ex); }
                string file = FileName(world);
                string own = Path.Combine(OwnFolder, file);
                File.WriteAllBytes(own, pkg);
                // CAW reads custom content from its own Packages folder (the tester, 2026-10-24); the game's Mods\Packages is left to the user
                string cawDir = CawPackagesFolder(), inCaw = null, cawError = null;
                try { Directory.CreateDirectory(cawDir); inCaw = Path.Combine(cawDir, file); File.WriteAllBytes(inCaw, pkg); }
                catch (Exception ex) { inCaw = null; cawError = ex.Message; Log.Error("Distant terrain: writing into CAW's Packages folder failed", ex); }
                float c = ti.Origin + ti.WorldSize / 2f;
                Log.Info(string.Format("Distant terrain made: {0} ({1:F1} MB), {2}; template {3}; CAW {4}; place at ({5:F0}, 0, {5:F0}), rotation 0",
                    own, pkg.Length / 1048576.0, r.Summary, tnote, inCaw ?? "not written (" + cawError + ")", c));
                SetBusy(false);
                info.Text = r.Summary + " " + PlaceText();
                string where = string.Format("Position X {0:F0}, Y 0, Z {0:F0}, rotation 0 (the middle of the map; its heights are already the real heights, so Y is 0).", c);
                string msg = (inCaw != null
                        ? "Installed for CAW:\n" + inCaw + "\n\nClose CAW and start it again (CAW reads its Packages folder when it starts), open this world, then click 'Place in world'."
                        : "Couldn't write into CAW's Packages folder (" + cawError + "; Program Files usually needs admin rights).\nCopy\n" + own + "\ninto\n" + cawDir + "\nthen restart CAW and click 'Place in world'.")
                    + "\n\nFor the game (and anyone you share the world with), copy the .package into Documents\\Electronic Arts\\The Sims 3\\Mods\\Packages."
                    + "\n\nTo place it by hand instead: " + where;
                Injector.Done("distant-make");
                Status((inCaw != null ? "Made and installed. Restart CAW, then 'Place in world'. " : "Made; copy it into CAW's Packages folder (see message). ") + where);
                MessageBox.Show(this, msg, "Distant terrain", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log.Error("Making the distant terrain failed", ex);
                SetBusy(false);
                Status("Making it failed: " + ex.Message);
            }
        }

        /// <summary>
        /// For the log: on each side of the map (in world axes), the ground CAW reports 2 m inside the edge, the height
        /// the ring was built from there, and the ring 60 m and 300 m outside, at 5 points along the side. If the ring
        /// doesn't follow a side of the world, this shows whether the world was read wrongly or the ring is turned.
        /// </summary>
        private string EdgeCheck(object world, DistantInput inp, DistantResult r)
        {
            HeightFn live = Caw.MakeHeightFn(world, "GetTerrainHeight");
            float c = ti.Origin + ti.WorldSize / 2f, a = r.Half, R = r.R, f = r.FieldCell;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendFormat("Distant terrain edge check (orientation {0}, origin {1}, centre {2:F0}; CAW ground / read / ring 60 m out / 300 m out):", ti.Orientation, ti.Origin, c);
            string[] names = { "-x (west?)", "+x", "-z", "+z" };
            for (int side = 0; side < 4; side++)
            {
                sb.Append("\n  side " + names[side] + ":");
                for (int k = 0; k < 5; k++)
                {
                    float along = -a * 0.8f + 0.4f * a * k;
                    float nx = side == 0 ? -1 : side == 1 ? 1 : 0, nz = side == 2 ? -1 : side == 3 ? 1 : 0;
                    float lx = nx != 0 ? nx * (a - 2) : along, lz = nz != 0 ? nz * (a - 2) : along;
                    float g = live(c + lx, c + lz), rd = DistantMaker.WorldH(inp, lx, lz);
                    float o60 = r.Field.At((lx + nx * 62 + R) / f, (lz + nz * 62 + R) / f), o300 = r.Field.At((lx + nx * 302 + R) / f, (lz + nz * 302 + R) / f);
                    sb.AppendFormat(" [{0:F0} / {1:F0} / {2:F0} / {3:F0}]", g, rd, o60, o300);
                }
            }
            return sb.ToString();
        }

        // ---------------- placing ----------------

        private void Place()
        {
            if (busy || Injector.BusyElsewhere(this)) return;
            object world = Caw.CurrentWorld;
            if (world == null || ti == null) { Status("Open a world and read it first."); return; }
            ulong inst = DistantMaker.Instance(Caw.WorldKey(world));
            if (DistantTerrain.Resource(S3Pkg.OBJK, 0, inst) == null)
            {
                Status("CAW hasn't loaded this world's distant terrain: make it first, then restart CAW.");
                return;
            }
            List<object> old = new List<object>(), mine = new List<object>();
            foreach (object o in DistantTerrain.Objects(null))
            {
                string n = "";
                try { n = Convert.ToString(Caw.GetProperty(o, "InstanceName")); } catch { }
                if (n.IndexOf("CAWPlus_DistantTerrain", StringComparison.OrdinalIgnoreCase) >= 0) mine.Add(o); else old.Add(o);
            }
            string q = (mine.Count > 0 ? "The world already has CAW Plus's distant terrain; it's replaced.\n" : "") +
                       (chkRemove.Checked && old.Count > 0 ? "The world's old distant terrain (" + old.Count + " object(s)) is removed.\n" : "") +
                       "\nPlace the distant terrain? CAW's Undo removes it.";
            if (MessageBox.Show(this, q, "Place distant terrain", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            SetBusy(true);
            try
            {
                bool group = false;
                List<object> remove = new List<object>(mine);
                if (chkRemove.Checked) remove.AddRange(old);
                foreach (object o in remove) { Caw.Delete(o, group); group = true; }
                if (!Caw.UseLayer(world, "DistantTerrain")) throw new InvalidOperationException("couldn't make or select the 'DistantTerrain' World Layer");
                Caw.CatalogItem item = new Caw.CatalogItem();
                item.Name = "CAWPlus_DistantTerrain_" + Caw.WorldKey(world);
                item.Key = Caw.MakeKey(S3Pkg.OBJK, 0, inst);
                float c = ti.Origin + ti.WorldSize / 2f;
                object placed = Caw.Place(item, c, c, 0f, 0, group, 0f);
                SetBusy(false);
                if (placed == null) { Status("CAW refused to place the object (see the log)."); return; }
                Log.Info(string.Format("Distant terrain placed at ({0:F0}, 0, {0:F0}); removed {1} old object(s)", c, remove.Count));
                Injector.Done("distant-place");
                Status("Placed. Look out from the map edge: the land should carry on from the world's own.");
            }
            catch (Exception ex)
            {
                Log.Error("Placing the distant terrain failed", ex);
                SetBusy(false);
                Status("Placing it failed: " + ex.Message);
            }
        }

        private void OpenFolder()
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + (Directory.Exists(CawPackagesFolder()) ? CawPackagesFolder() : OwnFolder) + "\""); }
            catch (Exception ex) { Status("Couldn't open the folder: " + ex.Message); }
        }

        // ---------------- helpers ----------------

        private delegate object Task();
        private volatile string bgStage;
        private volatile int bgPct = -1;

        private void BgProgress(string stage, int pct) { bgStage = stage; bgPct = pct; }

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
                Application.DoEvents();
            }
            bgPct = -1;
            if (error != null) throw new InvalidOperationException(error.Message, error);
            return result;
        }

        private void OnProgress(string stage, int pct)
        {
            progress.Value = Math.Max(0, Math.Min(100, pct));
            status.Text = stage + "...";
            status.Refresh(); progress.Refresh();
            Application.DoEvents();
        }

        private void Status(string s) { status.Text = s; }

        private void SetBusy(bool b)
        {
            busy = b;
            Injector.SetBusy(this, b);
            btnRead.Enabled = btnMake.Enabled = btnPlace.Enabled = !b;
            UseWaitCursor = b;
            if (!b) progress.Value = 0;
        }
    }
}
