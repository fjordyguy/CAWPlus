using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace CawPlus
{
    /// <summary>
    /// World Builder (#173, the tester: "a really simple world builder that you fill in squares for ... when you click create it
    /// transforms it into a world, smoothing out the landscape and placing things down"): paint squares of grass, hills,
    /// mountains, forest, lake, sea, river, road, bridge, town centre, houses, park and beach; Create shows the world they
    /// make (WorldSketch.Build: smooth natural land, no squares showing); Apply to world puts the heights, lakes and sea into
    /// CAW and saves the plan, so Generate Roads builds the roads and lots there and Auto-Place grows the forests drawn.
    /// </summary>
    public sealed class WorldBuilderForm : Form
    {
        private static readonly Color[] Colours =
        {
            Color.FromArgb(140, 190, 90), Color.FromArgb(150, 130, 70), Color.FromArgb(110, 100, 95), Color.FromArgb(40, 110, 50),
            Color.FromArgb(90, 160, 220), Color.FromArgb(40, 90, 170), Color.FromArgb(70, 140, 230), Color.FromArgb(55, 55, 58),
            Color.FromArgb(170, 110, 60), Color.FromArgb(220, 90, 70), Color.FromArgb(240, 200, 150), Color.FromArgb(120, 220, 120),
            Color.FromArgb(235, 220, 160)
        };
        private static readonly string[] Tips =
        {
            "Open grass: low, gently rolling land", "Rolling hills", "Mountains: the deeper inside a mountain area, the higher the peaks",
            "Forest: low wooded ground; Auto-Place grows woods here", "Lake: at sea level, or a raised pond if the land round it is high",
            "Sea (paint it along a map edge)", "River: draw a line of squares; it winds and carves a channel", "Road: Generate Roads builds a main road along the line",
            "Bridge: a road square over a river or water", "Town centre: the downtown streets and community lots go here",
            "Houses: neighbourhood streets and homes", "Park: open ground with scattered trees", "Beach: low sand by the sea"
        };

        private WorldSketch sketch;
        private readonly PictureBox canvas;
        private readonly ListBox palette;
        private readonly NumericUpDown nudBrush, nudSeed;
        private readonly ComboBox cbSize;
        private readonly TrackBar tbMount, tbHills;
        private readonly CheckBox chkFill, chkLevel;
        private readonly NumericUpDown nudLevel;
        private readonly Button btnUndo, btnRedo;
        private readonly Label lblHover;
        private readonly Button btnCreate, btnApply, btnRoads;
        private readonly ThemedProgress progress;
        private readonly Label status, lblMount, lblHills;
        private readonly PictureBox picture;
        private readonly MapView view;
        private float worldSize = 2048, seaLevel = 10;
        private bool worldRead, busy;
        private int lastX = -1, lastY = -1;
        private volatile int bgPct = -1;
        private volatile string bgStage = "";
        // undo / redo: the squares (and ground levels) before each stroke or change
        private readonly List<Snap> undo = new List<Snap>(), redo = new List<Snap>();
        private bool sizeChanging;

        private sealed class Snap
        {
            public int N; public byte[] T; public float[] Level;
            public Snap(WorldSketch s) { N = s.N; T = (byte[])s.T.Clone(); Level = s.Level == null ? null : (float[])s.Level.Clone(); }
        }

        public WorldBuilderForm()
        {
            SuspendLayout();
            Text = "World Builder  (CAW Plus)";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1350, 760);
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 8.25f);

            Label help = new Label();
            help.Text = "Left button paints, right button erases. Create shows the world; Apply to world puts it in CAW.";
            help.SetBounds(10, 8, 500, 18); Controls.Add(help);
            lblHover = new Label(); lblHover.SetBounds(510, 8, 160, 18); lblHover.TextAlign = ContentAlignment.TopRight; Controls.Add(lblHover);
            canvas = new PictureBox(); canvas.SetBounds(10, 28, 660, 660); canvas.BackColor = Color.Black;
            canvas.Paint += delegate (object s, PaintEventArgs e) { DrawSketch(e.Graphics); };
            canvas.MouseDown += delegate (object s, MouseEventArgs e) { lastX = lastY = -1; Remember(); PaintAt(e); };
            canvas.MouseMove += delegate (object s, MouseEventArgs e) { if (e.Button != MouseButtons.None) PaintAt(e); Hover(e); };
            canvas.MouseLeave += delegate { lblHover.Text = ""; };
            canvas.MouseUp += delegate { lastX = lastY = -1; };
            Controls.Add(canvas);

            int x = 682, y = 28;
            Label lp = new Label(); lp.Text = "Paint with"; lp.SetBounds(x, y, 120, 16); Controls.Add(lp);
            palette = new ListBox(); palette.SetBounds(x, y + 18, 150, 13 * 22 + 4); palette.DrawMode = DrawMode.OwnerDrawFixed; palette.ItemHeight = 22;
            foreach (string n in WorldSketch.Names) palette.Items.Add(n);
            palette.SelectedIndex = 1;
            palette.DrawItem += delegate (object s, DrawItemEventArgs e)
            {
                if (e.Index < 0) return;
                bool sel = (e.State & DrawItemState.Selected) != 0;
                using (Brush bg = new SolidBrush(sel ? Color.FromArgb(59, 130, 246) : Color.FromArgb(24, 25, 28))) e.Graphics.FillRectangle(bg, e.Bounds);
                using (Brush sw = new SolidBrush(Colours[e.Index])) e.Graphics.FillRectangle(sw, e.Bounds.X + 4, e.Bounds.Y + 3, 16, 16);
                using (Brush tx = new SolidBrush(Color.FromArgb(230, 231, 233))) e.Graphics.DrawString(WorldSketch.Names[e.Index], Font, tx, e.Bounds.X + 26, e.Bounds.Y + 4);
            };
            ToolTip paletteTip = new ToolTip();
            palette.SelectedIndexChanged += delegate { paletteTip.SetToolTip(palette, Tips[Math.Max(0, palette.SelectedIndex)]); };
            Controls.Add(palette);
            y += 18 + 13 * 22 + 12;
            Label lb = new Label(); lb.Text = "Brush (squares)"; lb.SetBounds(x, y + 3, 92, 16); Controls.Add(lb);
            nudBrush = new NumericUpDown(); nudBrush.SetBounds(x + 96, y, 54, 20); nudBrush.Minimum = 1; nudBrush.Maximum = 12; nudBrush.Value = 1; Controls.Add(nudBrush);
            y += 26;
            chkFill = new CheckBox(); chkFill.Text = "Fill an area"; chkFill.SetBounds(x, y, 150, 20); Controls.Add(chkFill);
            new ToolTip().SetToolTip(chkFill, "Click fills every touching square of the same kind (e.g. turn all the empty grass into sea in one click).");
            y += 26;
            Label ls = new Label(); ls.Text = "Square size"; ls.SetBounds(x, y + 3, 70, 16); Controls.Add(ls);
            cbSize = new ComboBox(); cbSize.DropDownStyle = ComboBoxStyle.DropDownList; cbSize.SetBounds(x, y + 20, 150, 20);
            cbSize.Items.Add("32 x 32 squares"); cbSize.Items.Add("64 x 64 squares"); cbSize.Items.Add("128 x 128 squares");
            Controls.Add(cbSize);
            y += 50;
            Button bClear = new Button(); bClear.Text = "Clear"; bClear.SetBounds(x, y, 150, 26);
            bClear.Click += delegate
            {
                if (MessageBox.Show(this, "Clear the whole sketch to grass?", "World Builder", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                Remember(); Array.Clear(sketch.T, 0, sketch.T.Length); sketch.Level = null; canvas.Invalidate(); SaveSketch();
            };
            Controls.Add(bClear);
            y += 30;
            Button bFrom = new Button(); bFrom.Text = "From this world"; bFrom.SetBounds(x, y, 150, 26);
            new ToolTip().SetToolTip(bFrom, "Fills the squares from the open world's land (sea, lakes, beaches, grass, hills, mountains),\n" +
                "e.g. after Generate Terrain or on an EA world, so you can change it by painting. Roads, town and forest are left for you to paint.");
            bFrom.Click += delegate { FromWorld(); };
            Controls.Add(bFrom);
            y += 30;
            btnUndo = new Button(); btnUndo.Text = "Undo"; btnUndo.SetBounds(x, y, 72, 26); btnUndo.Click += delegate { Undo(undo, redo); };
            btnRedo = new Button(); btnRedo.Text = "Redo"; btnRedo.SetBounds(x + 78, y, 72, 26); btnRedo.Click += delegate { Undo(redo, undo); };
            new ToolTip().SetToolTip(btnUndo, "Undo the last stroke or change (Ctrl+Z).");
            new ToolTip().SetToolTip(btnRedo, "Redo (Ctrl+Y).");
            Controls.Add(btnUndo); Controls.Add(btnRedo);
            y += 38;
            chkLevel = new CheckBox(); chkLevel.Text = "Paint ground height"; chkLevel.SetBounds(x, y, 150, 20); Controls.Add(chkLevel);
            y += 24;
            nudLevel = new NumericUpDown(); nudLevel.SetBounds(x, y, 60, 20); nudLevel.Minimum = 0; nudLevel.Maximum = 300; nudLevel.Increment = 5; nudLevel.Value = 20; Controls.Add(nudLevel);
            Label lLv = new Label(); lLv.Text = "m above the sea"; lLv.SetBounds(x + 64, y + 3, 90, 16); Controls.Add(lLv);
            string levelTip = "Instead of a kind of ground, the brush sets how high the flat ground stands (grass, town centre, houses, park, forest):\n" +
                "raised plains, a town on a plateau, terraces. Right button sets it back to 0. Hills, mountains, water and beaches ignore it.\n" +
                "Raised squares show lighter, with their height in metres.";
            new ToolTip().SetToolTip(chkLevel, levelTip); new ToolTip().SetToolTip(nudLevel, levelTip);
            chkLevel.CheckedChanged += delegate { canvas.Invalidate(); };
            nudLevel.ValueChanged += delegate { if (chkLevel.Checked) canvas.Invalidate(); };

            // the right column: heights, create, preview
            int rx = 846, ry = 28, rw = 494;
            lblMount = new Label(); lblMount.SetBounds(rx, ry, 240, 18); Controls.Add(lblMount);
            tbMount = new ThemedSlider(); tbMount.SetBounds(rx, ry + 20, 240, 30); tbMount.Minimum = 40; tbMount.Maximum = 500; tbMount.Value = 160; tbMount.TickStyle = TickStyle.None;
            tbMount.ValueChanged += delegate { Labels(); };
            Controls.Add(tbMount);
            lblHills = new Label(); lblHills.SetBounds(rx + 250, ry, 240, 18); Controls.Add(lblHills);
            tbHills = new ThemedSlider(); tbHills.SetBounds(rx + 250, ry + 20, 240, 30); tbHills.Minimum = 10; tbHills.Maximum = 120; tbHills.Value = 40; tbHills.TickStyle = TickStyle.None;
            tbHills.ValueChanged += delegate { Labels(); };
            Controls.Add(tbHills);
            ry += 62;
            Label lsd = new Label(); lsd.Text = "Variation"; lsd.SetBounds(rx, ry + 5, 60, 18); Controls.Add(lsd);
            nudSeed = new NumericUpDown(); nudSeed.SetBounds(rx + 62, ry + 2, 70, 22); nudSeed.Minimum = 1; nudSeed.Maximum = 99999; nudSeed.Value = 1; Controls.Add(nudSeed);
            new ToolTip().SetToolTip(nudSeed, "The same squares with different natural detail (hill shapes, coastline wiggles, river bends).");
            btnCreate = new Button(); btnCreate.Text = "Create"; btnCreate.SetBounds(rx + 142, ry, 100, 28); btnCreate.Font = new Font(Font, FontStyle.Bold);
            btnCreate.Click += delegate { CreatePreview(); };
            btnApply = new Button(); btnApply.Text = "Apply to world"; btnApply.SetBounds(rx + 248, ry, 116, 28);
            btnApply.Click += delegate { ApplyToWorld(); };
            btnRoads = new Button(); btnRoads.Text = "Next: Roads"; btnRoads.SetBounds(rx + 370, ry, 124, 28); btnRoads.Enabled = false;
            btnRoads.Click += delegate { Injector.Open("roads", Owner); };
            new ToolTip().SetToolTip(btnApply, "Puts this land into the open world (its heights are backed up first; Generate Terrain's 'Undo terrain' puts them back),\n" +
                "makes the lakes and sea, and saves the plan: Generate Roads then builds roads along your road squares and homes in your town\n" +
                "and houses squares, and Auto-Place grows woods in your forest squares.");
            Controls.Add(btnCreate); Controls.Add(btnApply); Controls.Add(btnRoads);
            ry += 36;
            rw = 470;
            picture = new PictureBox(); picture.SetBounds(rx + 12, ry, rw, rw); picture.SizeMode = PictureBoxSizeMode.Zoom; picture.BackColor = Color.FromArgb(40, 40, 40);
            Controls.Add(picture);
            CheckBox chk3D = new CheckBox(); chk3D.Text = "3D view"; chk3D.SetBounds(rx, ry + rw + 4, 80, 22);
            CheckBox chkExag = new CheckBox(); chkExag.Text = "Exaggerate heights (x2)"; chkExag.SetBounds(rx + 90, ry + rw + 4, 170, 22);
            Button bTurn = new Button(); bTurn.Text = "Turn"; bTurn.SetBounds(rx + 270, ry + rw + 3, 60, 23);
            view = new MapView(picture, chk3D, chkExag);
            chk3D.CheckedChanged += delegate { view.Redraw(); };
            bTurn.Click += delegate { view.Turn(); };
            Controls.Add(chk3D); Controls.Add(chkExag); Controls.Add(bTurn);
            progress = new ThemedProgress(); progress.SetBounds(10, 698, 660, 12); Controls.Add(progress);
            status = new Label(); status.SetBounds(10, 714, 1330, 40); Controls.Add(status);

            sketch = LoadSketch() ?? new WorldSketch(WorldSketch.DefaultN);
            cbSize.SelectedIndex = sketch.N >= 128 ? 2 : sketch.N >= 64 ? 1 : 0;
            cbSize.SelectedIndexChanged += delegate { if (!sizeChanging) { Remember(); ChangeSquares(cbSize.SelectedIndex == 2 ? 128 : cbSize.SelectedIndex == 1 ? 64 : 32); } };
            KeyPreview = true;
            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.Z && !e.Shift) { Undo(undo, redo); e.Handled = true; }
                else if (e.Control && (e.KeyCode == Keys.Y || (e.KeyCode == Keys.Z && e.Shift))) { Undo(redo, undo); e.Handled = true; }
            };
            UndoButtons();
            tbMount.Value = Math.Max(tbMount.Minimum, Math.Min(tbMount.Maximum, (int)sketch.MountainHeight));
            tbHills.Value = Math.Max(tbHills.Minimum, Math.Min(tbHills.Maximum, (int)sketch.HillHeight));
            nudSeed.Value = Math.Max(1, Math.Min(99999, sketch.Seed));
            Labels();
            FormClosing += delegate { if (!busy) SaveSketch(); };
            ResumeLayout(false);
            Theme.Apply(this);
            Status("Paint the world in squares: sea along an edge, mountains, a river line, a town centre with houses round it, roads joining them. Then Create.");
        }

        private void Labels()
        {
            lblMount.Text = "Mountain height: up to " + tbMount.Value + " m above the sea";
            lblHills.Text = "Hill height: up to " + tbHills.Value + " m";
            canvas.Invalidate();
        }

        private void Status(string s) { status.Text = s; }

        // ---------- painting ----------

        private void DrawSketch(Graphics g)
        {
            int n = sketch.N; float k = (float)canvas.Width / n;
            Brush[] br = new Brush[Colours.Length];
            for (int i = 0; i < br.Length; i++) br[i] = new SolidBrush(Colours[i]);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    g.FillRectangle(br[sketch.T[y * n + x]], x * k, y * k, k + 0.5f, k + 0.5f);
            foreach (Brush b in br) b.Dispose();
            if (sketch.Level != null)
            {
                // raised flat ground: lighter the higher it stands; the height written in when the squares are big enough
                bool numbers = k >= 18 && chkLevel.Checked;
                using (Font f = new Font("Segoe UI", 6.75f))
                using (Brush num = new SolidBrush(Color.FromArgb(230, 20, 20, 20)))
                    for (int y = 0; y < n; y++)
                        for (int x = 0; x < n; x++)
                        {
                            float lv = sketch.Level[y * n + x];
                            if (lv < 0.5f || !WorldSketch.TakesLevel(sketch[x, y])) continue;
                            using (Brush w = new SolidBrush(Color.FromArgb((int)Math.Min(170, 40 + lv * 1.2f), 255, 255, 255))) g.FillRectangle(w, x * k, y * k, k + 0.5f, k + 0.5f);
                            if (numbers) g.DrawString(((int)Math.Round(lv)).ToString(), f, num, x * k + 1, y * k + k / 2 - 6);
                        }
            }
            // fainter lines when the squares are small, so 128 squares still show without hiding the map
            using (Pen p = new Pen(Color.FromArgb(n > 64 ? 26 : 40, 0, 0, 0)))
                    for (int i = 0; i <= n; i++) { g.DrawLine(p, i * k, 0, i * k, canvas.Height); g.DrawLine(p, 0, i * k, canvas.Width, i * k); }
            using (Brush tx = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
                g.DrawString(string.Format("{0} x {0} squares, each {1:0} m", n, worldSize / n), Font, tx, 4, 4);
        }

        private void PaintAt(MouseEventArgs e)
        {
            int n = sketch.N;
            int cx = Grid.Clampi((int)(e.X * n / (float)canvas.Width), 0, n - 1), cy = Grid.Clampi((int)(e.Y * n / (float)canvas.Height), 0, n - 1);
            SketchTile t = e.Button == MouseButtons.Right ? SketchTile.Grass : (SketchTile)Math.Max(0, palette.SelectedIndex);
            float lv = e.Button == MouseButtons.Right ? 0 : (float)nudLevel.Value;
            if (chkLevel.Checked && sketch.Level == null) sketch.Level = new float[n * n];
            if (chkFill.Checked && e.Button != MouseButtons.None) { if (lastX < 0) Flood(cx, cy, t, lv); lastX = cx; lastY = cy; canvas.Invalidate(); return; }
            // along the line from the last square, so a fast drag leaves no gaps
            int x0 = lastX < 0 ? cx : lastX, y0 = lastY < 0 ? cy : lastY;
            int steps = Math.Max(Math.Abs(cx - x0), Math.Abs(cy - y0));
            for (int s = 0; s <= steps; s++)
            {
                float f = steps == 0 ? 1 : (float)s / steps;
                Stamp((int)Math.Round(x0 + (cx - x0) * f), (int)Math.Round(y0 + (cy - y0) * f), t, lv);
            }
            lastX = cx; lastY = cy;
            canvas.Invalidate();
        }

        private void Stamp(int cx, int cy, SketchTile t, float lv)
        {
            int r = (int)nudBrush.Value - 1, n = sketch.N;
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                    if (x >= 0 && y >= 0 && x < n && y < n && (r < 2 || (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r + r))
                    {
                        if (chkLevel.Checked) sketch.Level[y * n + x] = lv;
                        else sketch[x, y] = t;
                    }
        }

        /// <summary>Every touching square of the same kind as the one clicked: turned into 't', or in ground height mode set to 'lv' m.</summary>
        private void Flood(int sx, int sy, SketchTile t, float lv)
        {
            int n = sketch.N; SketchTile from = sketch[sx, sy];
            bool level = chkLevel.Checked;
            if (!level && from == t) return;
            bool[] seen = new bool[n * n];
            Stack<int> st = new Stack<int>(); st.Push(sy * n + sx);
            while (st.Count > 0)
            {
                int i = st.Pop(), x = i % n, y = i / n;
                if (seen[i] || (SketchTile)sketch.T[i] != from) continue;
                seen[i] = true;
                if (level) sketch.Level[i] = lv; else sketch.T[i] = (byte)t;
                if (x > 0) st.Push(i - 1); if (x < n - 1) st.Push(i + 1);
                if (y > 0) st.Push(i - n); if (y < n - 1) st.Push(i + n);
            }
        }

        private void Hover(MouseEventArgs e)
        {
            int n = sketch.N;
            int cx = Grid.Clampi((int)(e.X * n / (float)canvas.Width), 0, n - 1), cy = Grid.Clampi((int)(e.Y * n / (float)canvas.Height), 0, n - 1);
            SketchTile t = sketch[cx, cy];
            float lv = sketch.Level != null && WorldSketch.TakesLevel(t) ? sketch.Level[cy * n + cx] : 0;
            lblHover.Text = WorldSketch.Names[(int)t] + (lv >= 0.5f ? string.Format(", {0:0} m up", lv) : "") + string.Format("  ({0}, {1})", cx + 1, cy + 1);
        }

        /// <summary>Keeps the squares as they are now, for Undo (called before each stroke or change).</summary>
        private void Remember()
        {
            undo.Add(new Snap(sketch));
            if (undo.Count > 60) undo.RemoveAt(0);
            redo.Clear();
            UndoButtons();
        }

        /// <summary>Undo (from = undo, to = redo) or Redo (the other way round).</summary>
        private void Undo(List<Snap> from, List<Snap> to)
        {
            if (busy || from.Count == 0) return;
            to.Add(new Snap(sketch));
            Snap s = from[from.Count - 1]; from.RemoveAt(from.Count - 1);
            WorldSketch w = new WorldSketch(s.N);
            Array.Copy(s.T, w.T, s.T.Length); w.Level = s.Level;
            w.Seed = sketch.Seed; w.MountainHeight = sketch.MountainHeight; w.HillHeight = sketch.HillHeight;
            sketch = w;
            sizeChanging = true;
            cbSize.SelectedIndex = s.N >= 128 ? 2 : s.N >= 64 ? 1 : 0;
            sizeChanging = false;
            UndoButtons();
            canvas.Invalidate();
        }

        private void UndoButtons() { btnUndo.Enabled = undo.Count > 0; btnRedo.Enabled = redo.Count > 0; }

        /// <summary>The sketch at another number of squares (each square takes the kind under its middle).</summary>
        private void ChangeSquares(int n)
        {
            if (n == sketch.N) return;
            WorldSketch s = new WorldSketch(n);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++) s.T[y * n + x] = sketch.T[(y * sketch.N / n) * sketch.N + x * sketch.N / n];
            s.Seed = sketch.Seed; s.MountainHeight = sketch.MountainHeight; s.HillHeight = sketch.HillHeight;
            if (sketch.Level != null)
            {
                s.Level = new float[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++) s.Level[y * n + x] = sketch.Level[(y * sketch.N / n) * sketch.N + x * sketch.N / n];
            }
            sketch = s;
            canvas.Invalidate();
        }

        private WorldSketch Current()
        {
            sketch.Seed = (int)nudSeed.Value; sketch.MountainHeight = tbMount.Value; sketch.HillHeight = tbHills.Value;
            return sketch;
        }

        // ---------- saving (per world) ----------

        private static string SketchPath()
        {
            object world = Caw.CurrentWorld;
            return world == null ? null : Path.Combine(Caw.WorldDir(Path.Combine(Log.Dir, "Worlds"), world), "sketch.txt");
        }

        private static WorldSketch LoadSketch()
        {
            try { string p = SketchPath(); return p != null && File.Exists(p) ? WorldSketch.FromText(File.ReadAllText(p)) : null; }
            catch (Exception ex) { Log.Error("Reading the World Builder sketch failed", ex); return null; }
        }

        private void SaveSketch()
        {
            try
            {
                string p = SketchPath();
                if (p == null || !Current().Any) return;
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllText(p, sketch.ToText());
            }
            catch (Exception ex) { Log.Error("Saving the World Builder sketch failed", ex); }
        }

        private void FromWorld()
        {
            if (busy || Injector.BusyElsewhere(this)) return;
            object world = Caw.CurrentWorld;
            if (world == null) { Status("Open a world first."); return; }
            if (sketch.Any && MessageBox.Show(this, "Replace your squares with the open world's land?", "World Builder", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            Remember();
            SetBusy(true);
            try
            {
                TerrainInfo ti = ReadWorld(world);
                WorldSketch s = WorldSketch.FromHeights(ti.Heights, ti.CellMeters, ti.WaterLevel, sketch.N);
                s.Seed = (int)nudSeed.Value;
                sketch = s;
                tbMount.Value = Math.Max(tbMount.Minimum, Math.Min(tbMount.Maximum, (int)s.MountainHeight));
                tbHills.Value = Math.Max(tbHills.Minimum, Math.Min(tbHills.Maximum, (int)s.HillHeight));
                sizeChanging = true; cbSize.SelectedIndex = s.N >= 128 ? 2 : s.N >= 64 ? 1 : 0; sizeChanging = false;
                canvas.Invalidate();
                Status("Squares filled from this world's land. Paint roads, town, houses and forest on it, change what you like, then Create.");
            }
            catch (Exception ex) { Log.Error("World Builder: reading the world failed", ex); Status("Reading the world failed: " + ex.Message); }
            finally { SetBusy(false); }
        }

        // ---------- create and apply ----------

        private TerrainInfo ReadWorld(object world)
        {
            TerrainInfo ti = TerrainSampler.Analyze(world, Caw.LayerCount(Caw.Terrain(world)), OnProgress);
            worldSize = ti.WorldSize; seaLevel = ti.WaterLevel; worldRead = true;
            return ti;
        }

        private void OnProgress(string stage, int pct)
        {
            progress.Value = Math.Max(0, Math.Min(100, pct)); status.Text = stage + "...";
            Injector.Pump();
        }

        private delegate object Task();

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

        private void SetBusy(bool b)
        {
            busy = b;
            Injector.SetBusy(this, b);
            btnCreate.Enabled = btnApply.Enabled = !b;
            UseWaitCursor = b;
            if (!b) progress.Value = 0;
        }

        private void CreatePreview()
        {
            if (busy || Injector.BusyElsewhere(this)) return;
            object world = Caw.CurrentWorld;
            WorldSketch s = Current();
            if (!s.Any) { Status("Paint some squares first."); return; }
            SetBusy(true);
            try
            {
                if (!worldRead && world != null) ReadWorld(world);
                float ws = worldSize, sea = seaLevel;
                bgStage = "Shaping the land"; bgPct = 30;
                RoadNet net = null;
                TerrainGen.Result r = (TerrainGen.Result)RunInBackground(delegate ()
                {
                    TerrainGen.Result b = Helper.BuildSketch(s, 513, ws, sea, true, null);
                    bgStage = "Laying out the drawn roads and lots"; bgPct = 80;
                    net = PlanDrawn(s, b, ws, sea);
                    return b;
                });
                ShowResult(r, s, net);
                SaveSketch();
                float lo, hi; r.Heights.MinMax(out lo, out hi);
                Status(string.Format("The world from your {0} x {0} squares: land up to {1:0} m above the sea, {2} river(s), {3} raised lake(s){4}. " +
                    "Change squares and Create again, or 'Apply to world'.", s.N, hi - sea, r.Rivers.Count, r.Ponds.Count,
                    net == null ? "" : string.Format("; {0:0.0} km of road, {1} bridge(s), about {2} lots", net.TotalKm, net.Bridges.Count, net.Lots.Count)));
            }
            catch (Exception ex) { Log.Error("World Builder: Create failed", ex); Status("Create failed: " + ex.Message); }
            finally { SetBusy(false); }
        }

        private static RoadNet PlanDrawn(WorldSketch s, TerrainGen.Result r, float worldSize, float sea)
        {
            try { return s.PlanRoads(r, worldSize, sea, Preset()); }
            catch (Exception ex) { Log.Error("World Builder: planning the drawn roads failed", ex); return null; }
        }

        /// <summary>The World type for the lots and bridges: the one Generate Terrain used, else Sunset Valley.</summary>
        private static WorldPreset Preset()
        {
            WorldPreset w = null;
            foreach (WorldPreset p in WorldPreset.LoadAll())
                if (WorldPreset.Short(p.Name) == WorldPreset.Inspiration || (w == null && p.Name.StartsWith("Sunset Valley"))) w = p;
            return w;
        }

        /// <summary>The land, with the drawn roads (and their lots), town and forest over it.</summary>
        private void ShowResult(TerrainGen.Result r, WorldSketch s, RoadNet net)
        {
            Bitmap bmp = TerrainGenForm.Render(r.Heights, r.Cell, seaLevel, null, 512, r.Ponds);
            float k = 512f / worldSize;
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                PlanMap plan = s.Plan(129, worldSize);
                float pk = 512f / (plan.N - 1);
                using (Brush town = new SolidBrush(Color.FromArgb(90, 220, 90, 70)), houses = new SolidBrush(Color.FromArgb(70, 240, 200, 150)),
                             forest = new SolidBrush(Color.FromArgb(110, 30, 90, 40)), park = new SolidBrush(Color.FromArgb(70, 120, 220, 120)))
                    for (int i = 0; i < plan.Codes.Length; i++)
                    {
                        Brush b = null;
                        switch ((PlanArea)plan.Codes[i])
                        {
                            case PlanArea.Centre: b = town; break;
                            case PlanArea.Houses: b = houses; break;
                            case PlanArea.Forest: b = forest; break;
                            case PlanArea.Park: b = park; break;
                        }
                        if (b != null) g.FillRectangle(b, (i % plan.N) * pk - pk / 2, (i / plan.N) * pk - pk / 2, pk, pk);
                    }
                if (net != null) TerrainGenForm.DrawNet(g, net, k);
                else
                using (Pen road = new Pen(Color.FromArgb(230, 40, 40, 44), 3f))
                    foreach (List<float[]> line in s.Lines(new[] { SketchTile.Road }, new[] { SketchTile.Bridge }, worldSize, true))
                        for (int i = 1; i < line.Count; i++) g.DrawLine(road, line[i - 1][0] * k, line[i - 1][1] * k, line[i][0] * k, line[i][1] * k);
            }
            view.SetMap(bmp, r.Heights, r.Cell, seaLevel, null);
        }

        private string BackupDir(object world)
        {
            string d = Caw.WorldDir(Path.Combine(Log.Dir, "Backups"), world);
            Directory.CreateDirectory(d);
            return d;
        }

        private void ApplyToWorld()
        {
            if (busy || Injector.BusyElsewhere(this)) return;
            object world = Caw.CurrentWorld;
            if (world == null) { Status("Open a world first (File > New for a blank one)."); return; }
            WorldSketch s = Current();
            if (!s.Any) { Status("Paint some squares first."); return; }
            if (MessageBox.Show(this, "Replace the open world's land with this one?\n\nThe current heights are backed up first; Generate Terrain's " +
                    "'Undo terrain' puts them back. Existing ponds are removed. Lots and objects are not moved, so this is best on a new or empty world.",
                    "World Builder", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            SetBusy(true);
            string backup = null, clipNote = "";
            bool imported = false;
            try
            {
                TerrainInfo ti = ReadWorld(world);
                string current = Path.Combine(TerrainSampler.TempDir, "height_current.png");
                if (File.Exists(current)) File.Delete(current);
                if (!Caw.SaveHeightImage(world, current)) throw new IOException("CAW could not export the current heights.");
                int W, H; ImageIO.PngSize(current, out W, out H);
                backup = Path.Combine(BackupDir(world), "height_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
                File.Copy(current, backup, true);
                ManualEdits.PruneBackups(BackupDir(world), 10);
                float ws = worldSize, sea = seaLevel;
                Log.Info(string.Format("World Builder: {0} x {0} squares on a {1} m world (height image {2}x{3}), sea {4:F1} m, backup {5}", s.N, ws, W, H, sea, backup));
                bgStage = "Shaping the land (full size)"; bgPct = 20;
                DateTime t0 = DateTime.Now;
                RoadNet net = null;
                TerrainGen.Result full = (TerrainGen.Result)RunInBackground(delegate ()
                {
                    TerrainGen.Result b = Helper.BuildSketch(s, W, ws, sea, true, null);
                    net = PlanDrawn(s, b, ws, sea);
                    return b;
                });
                Log.Info(string.Format("World Builder: land worked out in {0:F1} s; {1} rivers, {2} raised lakes", (DateTime.Now - t0).TotalSeconds, full.Rivers.Count, full.Ponds.Count));

                OnProgress("Calibrating CAW's height import", 80);
                imported = true;
                // ponds made for the old land would flood the new land up to their level
                try { int gone = Caw.RemoveAllPonds(world); if (gone > 0) Log.Info("Removed " + gone + " pond(s) left from the old land"); }
                catch (Exception ex) { Log.Error("Removing old ponds failed", ex); }
                float err = HeightImport.Apply(world, ti, full.Heights, W, H, OnProgress, out clipNote);
                int ponds = 0;
                foreach (float[] q in full.Ponds)
                {
                    try
                    {
                        float wx, wz;
                        TerrainSampler.Map(ti.Orientation, q[0] / ti.WorldSize, q[1] / ti.WorldSize, out wx, out wz);
                        float span, maxSpan = Math.Max(100f, 8 * q[2]);
                        object p = Caw.CreatePondChecked(world, wx * ti.WorldSize, wz * ti.WorldSize, q[3], ti.WorldSize / (W - 1), maxSpan, out span);
                        if (p != null) ponds++;
                        Log.Info(string.Format("World Builder: lake at ({0:F0}, {1:F0}) m, level {2:F1} m: {3}, water {4:F0} m across", wx * ti.WorldSize, wz * ti.WorldSize, q[3],
                            p != null ? "made" : span > maxSpan ? "removed: it flooded more than " + maxSpan.ToString("F0") + " m" : "CAW refused", span));
                    }
                    catch (Exception ex) { Log.Error("Making a lake failed", ex); }
                }
                try { Log.Info("Sea level refreshed: " + Caw.RefreshSea(world, ti.WaterLevel)); }
                catch (Exception ex) { Log.Error("Refreshing the sea level failed", ex); }

                // the plan (Generate Roads: town, houses, drawn roads; Auto-Place: forest, park) and where the town belongs
                string wdir = Caw.WorldDir(Path.Combine(Log.Dir, "Worlds"), world);
                int cn = (int)(ti.WorldSize / TerrainGen.CoarseCell) + 1;
                s.Plan(cn, ti.WorldSize).Save(Path.Combine(wdir, "plan.png"));
                WorldSketch.MarkApplied(wdir, full.Heights);
                SaveSketch();
                string hint = Path.Combine(BackupDir(world), "town_hint.txt");
                System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
                if (full.TownHint != null) File.WriteAllText(hint, full.TownHint[0].ToString("F1", ci) + " " + full.TownHint[1].ToString("F1", ci));
                else if (File.Exists(hint)) File.Delete(hint);

                ShowResult(full, s, net);
                // Generate Roads, Auto-Paint and Auto-Place then start with the World type the preview's lots came from
                if (WorldPreset.Inspiration == null) { WorldPreset w = Preset(); if (w != null) WorldPreset.Inspiration = WorldPreset.Short(w.Name); }
                Injector.Done("terrain");
                btnRoads.Enabled = true;
                Status(string.Format("Applied (mean height error {0:F2} m, {1} of {2} raised lakes made). Next: Generate Roads builds your roads, town and houses; " +
                    "then Auto-Paint and Auto-Place (woods in your forest squares).{3}", err, ponds, full.Ponds.Count, clipNote));
            }
            catch (Exception ex)
            {
                Log.Error("World Builder: applying failed", ex);
                bool back = false;
                if (imported && backup != null && File.Exists(backup) && !(ex is WorldChangedException))
                    try { back = Caw.ImportHeightImage(world, backup); } catch (Exception ex2) { Log.Error("Restoring the heights failed", ex2); }
                Status("Applying failed: " + ex.Message + (!imported ? "" : back ? " The previous heights were put back." : " Use Generate Terrain's 'Undo terrain' to put the previous heights back."));
            }
            finally { SetBusy(false); }
        }
    }
}
