using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace CawPlus
{
    /// <summary>The Auto-Paint panel: map roles to the world's paint layers, tune the look, preview, apply.</summary>
    public sealed class AutoPaintForm : Form
    {
        private sealed class LayerItem
        {
            public const int None = -1, NewLayer = -2;
            public readonly int Index; public readonly string TexturePath; private readonly string text;
            public LayerItem(int index, string text) { Index = index; this.text = text; }
            public LayerItem(string texturePath, string text) { Index = NewLayer; TexturePath = texturePath; this.text = text; }
            public bool IsNew { get { return Index == NewLayer; } }
            public override string ToString() { return text; }
        }

        private readonly Dictionary<PaintRole, ComboBox> roleCombos = new Dictionary<PaintRole, ComboBox>();
        // the style's other textures for each type (EA look: several grasses, rocks...) and its base texture: an existing
        // layer index or a texture file, with its share of the world
        private sealed class TexPick { public int Index = -1; public string Path; public float Share; public string Name; }
        private readonly Dictionary<PaintRole, List<TexPick>> extras = new Dictionary<PaintRole, List<TexPick>>();
        private readonly Dictionary<PaintRole, float> mainShare = new Dictionary<PaintRole, float>();
        private TexPick basePick;
        private CheckBox chkVariety, chkBaseTex;
        private Label lblVariety;
        private TrackBar tbRock, tbShore, tbDirt, tbMeadow, tbSoft, tbDetail;
        private readonly Dictionary<PaintRole, PictureBox> roleThumbs = new Dictionary<PaintRole, PictureBox>();
        private readonly Dictionary<string, Bitmap> thumbCache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private List<string> textureFiles = new List<string>();
        private NumericUpDown nudSeed, nudWater;
        private Button btnAnalyse, btnApply, btnRestore, btnNewSeed;
        private CheckBox chkKeepEdits;
        private ComboBox cbStyle;
        private Button btnLearn, btnSmartPick, btnWorldTex;
        private StyledGenerator previewGen; private string previewGenKey;
        private CheckBox chkEdge;
        private TrackBar tbEdge;
        private Label lblEdge, lblEdgeTex;
        private Panel swDistant, swGround;
        private bool hasDistant, hasGround;
        private string edgeSource;
        private string textureFolder;
        private PictureBox picture;
        private CheckBox chk3D;
        private MapView view;
        private Bitmap map2D;  // the preview map (2D), owned here; MapView draws it or its 3D view
        private readonly Dictionary<string, Color> texColour = new Dictionary<string, Color>();
        private ProgressBar progress;
        private Label status;
        private Timer previewTimer;

        private TerrainInfo terrain;
        private NumericUpDown nudSteep;
        private Grid previewHeights, previewWaterOffset; private float previewCell;
        private bool busy;
        private NumericUpDown nudChunk;

        public AutoPaintForm()
        {
            SuspendLayout();
            Text = "Auto-Paint  (CAW Plus)";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(860, 792);
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 8.25f);

            int x = 10, y = 10, colW = 310;

            // ---- layer mapping ----
            GroupBox gbLayers = new GroupBox(); gbLayers.Text = "Which paint layer is each type?"; gbLayers.SetBounds(x, y, colW, 262);
            int ly = 20;
            foreach (PaintRole r in new[] { PaintRole.Rock, PaintRole.Dirt, PaintRole.Shore, PaintRole.Meadow })
            {
                Label l = new Label(); l.Text = PaintEngine.RoleLabel(r); l.SetBounds(10, ly, 250, 14);
                ComboBox cb = new ComboBox(); cb.DropDownStyle = ComboBoxStyle.DropDownList; cb.SetBounds(10, ly + 14, 250, 20);
                PaintRole role = r;
                cb.SelectedIndexChanged += delegate { ShowThumb(role); SchedulePreview(); };
                // a picture of the chosen texture (click for a bigger view)
                PictureBox th = new PictureBox(); th.SetBounds(266, ly + 1, 34, 32); th.SizeMode = PictureBoxSizeMode.StretchImage;
                th.BorderStyle = BorderStyle.FixedSingle; th.BackColor = Color.FromArgb(60, 60, 60); th.Cursor = Cursors.Hand;
                th.Click += delegate { ShowBigThumb(role); };
                new ToolTip().SetToolTip(th, "The texture this type will paint with. Click to see it bigger.");
                gbLayers.Controls.Add(l); gbLayers.Controls.Add(cb); gbLayers.Controls.Add(th);
                roleCombos[r] = cb; roleThumbs[r] = th;
                ly += 32;
            }
            btnSmartPick = new Button(); btnSmartPick.Text = "Smart pick textures"; btnSmartPick.SetBounds(10, ly + 2, 140, 24);
            btnSmartPick.Click += delegate { SmartPick(); };
            new ToolTip().SetToolTip(btnSmartPick, "Choose a texture for each type from your layers and SourceTextures folder,\n" +
                "judged by name, colour, the textures EA's own worlds use for each type and\n(if a style is selected) what that world used.");
            gbLayers.Controls.Add(btnSmartPick);
            btnWorldTex = new Button(); btnWorldTex.Text = "Get world textures..."; btnWorldTex.SetBounds(156, ly + 2, 146, 24);
            btnWorldTex.Click += delegate { GetWorldTextures(); };
            new ToolTip().SetToolTip(btnWorldTex, "Copies the paint textures EA's worlds use into your SourceTextures folder\n" +
                "(SourceTextures\\" + WorldTextures.FolderName + "), so each World type's style can paint with its own textures.\n" +
                "Choose a folder with world files (.world, e.g. the EA worlds' CAW files): every texture they hold is copied.\n" +
                "Textures still missing are then looked up in the game files CAW has loaded.");
            gbLayers.Controls.Add(btnWorldTex);
            chkVariety = new CheckBox(); chkVariety.Text = "Also paint the style's other textures (EA look)"; chkVariety.Checked = true;
            chkVariety.SetBounds(10, ly + 30, 290, 18);
            new ToolTip().SetToolTip(chkVariety, "EA's worlds paint each kind of ground with several textures (Sunset Valley: four grasses,\n" +
                "four rocks, two sands). With a style, its own textures are picked by name from your layers and SourceTextures,\n" +
                "and each type is split between them in patches, in the world's own proportions.");
            chkVariety.CheckedChanged += delegate { UpdateVarietyLabel(); };
            gbLayers.Controls.Add(chkVariety);
            chkBaseTex = new CheckBox(); chkBaseTex.Text = "Lay the style's base texture under everything"; chkBaseTex.Checked = true;
            chkBaseTex.SetBounds(10, ly + 48, 290, 18);
            new ToolTip().SetToolTip(chkBaseTex, "EA's worlds have their own base texture (Sunset Valley: rock shale) showing wherever nothing\n" +
                "else is painted. This paints it as a layer under the others (your world's layer 0 stays as it is).");
            chkBaseTex.CheckedChanged += delegate { UpdateVarietyLabel(); };
            gbLayers.Controls.Add(chkBaseTex);
            lblVariety = new Label(); lblVariety.SetBounds(10, ly + 68, 292, 30); lblVariety.ForeColor = Color.FromArgb(70, 70, 70);
            gbLayers.Controls.Add(lblVariety);
            Controls.Add(gbLayers);
            y += 270;

            // ---- look ----
            GroupBox gbLook = new GroupBox(); gbLook.Text = "Look"; gbLook.SetBounds(x, y, colW, 312);
            int ty = 18;
            Label lst = new Label(); lst.Text = "Style"; lst.SetBounds(10, ty + 4, 40, 16); gbLook.Controls.Add(lst);
            cbStyle = new ComboBox(); cbStyle.DropDownStyle = ComboBoxStyle.DropDownList; cbStyle.SetBounds(52, ty, 248, 20);
            cbStyle.SelectedIndexChanged += delegate { PickFromStyle(); SchedulePreview(); };
            new ToolTip().SetToolTip(cbStyle, "Built-in rules, or a style learned from another world (e.g. Sunset Valley).\n" +
                "With a style, sliders at 50 = as in that world.");
            gbLook.Controls.Add(cbStyle);
            ty += 28;
            tbRock = AddSlider(gbLook, "Rock on slopes", ref ty);
            tbDirt = AddSlider(gbLook, "Dirt in hollows", ref ty);
            tbShore = AddSlider(gbLook, "Shore sand width", ref ty);
            tbMeadow = AddSlider(gbLook, "Meadow patches", ref ty);
            tbSoft = AddSlider(gbLook, "Edge softness", ref ty);
            tbDetail = AddSlider(gbLook, "Detail (finer)", ref ty);
            new ToolTip().SetToolTip(tbDetail, "Size of the patterns: higher = smaller, more intricate patches and edges.");

            Label ls = new Label(); ls.Text = "Seed"; ls.SetBounds(10, ty + 4, 34, 16); gbLook.Controls.Add(ls);
            nudSeed = new NumericUpDown(); nudSeed.Minimum = 0; nudSeed.Maximum = 999999; nudSeed.Value = 7; nudSeed.SetBounds(44, ty, 66, 20);
            nudSeed.ValueChanged += delegate { SchedulePreview(); };
            gbLook.Controls.Add(nudSeed);
            btnNewSeed = new Button(); btnNewSeed.Text = "New"; btnNewSeed.SetBounds(114, ty - 1, 44, 23);
            btnNewSeed.Click += delegate { nudSeed.Value = new Random().Next(0, 999999); };
            gbLook.Controls.Add(btnNewSeed);

            Label lw = new Label(); lw.Text = "Water (m)"; lw.SetBounds(166, ty + 4, 58, 16); gbLook.Controls.Add(lw);
            nudWater = new NumericUpDown(); nudWater.DecimalPlaces = 1; nudWater.Increment = 0.5m;
            nudWater.Minimum = -100; nudWater.Maximum = 2000; nudWater.SetBounds(226, ty, 74, 20);
            nudWater.ValueChanged += delegate { SchedulePreview(); };
            gbLook.Controls.Add(nudWater);
            Controls.Add(gbLook);
            y += 320;

            // ---- actions ----
            btnAnalyse = new Button(); btnAnalyse.Text = "Read terrain"; btnAnalyse.SetBounds(x, y, 100, 28);
            btnAnalyse.Click += delegate { Analyse(); };
            btnApply = new Button(); btnApply.Text = "Paint world"; btnApply.SetBounds(x + 105, y, 100, 28);
            btnApply.Font = new Font(Font, FontStyle.Bold);
            btnApply.Click += delegate { Apply(); };
            btnRestore = new Button(); btnRestore.Text = "Undo paint"; btnRestore.SetBounds(x + 210, y, 100, 28);
            btnRestore.Click += delegate { RestoreLastBackup(); };
            Controls.Add(btnAnalyse); Controls.Add(btnApply); Controls.Add(btnRestore);
            y += 34;
            btnLearn = new Button(); btnLearn.Text = "Copy this world's painting as a style..."; btnLearn.SetBounds(x, y, colW, 28);
            btnLearn.Click += delegate { LearnStyle(); };
            new ToolTip().SetToolTip(btnLearn, "Only needed for worlds that aren't built in (the EA worlds already are).\n" +
                "Open a painted world you like and click this: it records where that world puts rock, dirt,\n" +
                "sand and grass and which textures it uses, and saves it as a style you can pick above\n" +
                "to paint other worlds the same way. Nothing in the open world is changed.");
            Controls.Add(btnLearn);
            y += 34;

            chkKeepEdits = new CheckBox(); chkKeepEdits.Text = "Keep my manual paint edits when repainting";
            chkKeepEdits.Checked = true; chkKeepEdits.SetBounds(x, y, colW, 20);
            new ToolTip().SetToolTip(chkKeepEdits,
                "Anything you paint by hand on these layers after Auto-Paint is detected and kept on the next 'Paint world'.\n" +
                "Untick to regenerate from scratch.");
            Controls.Add(chkKeepEdits);
            y += 24;
            Label lc = new Label(); lc.Text = "Max paint layers per 256 m chunk"; lc.SetBounds(x, y + 3, 200, 16); Controls.Add(lc);
            nudChunk = new NumericUpDown(); nudChunk.Minimum = 2; nudChunk.Maximum = 12; nudChunk.Value = 8; nudChunk.SetBounds(x + 204, y, 50, 20);
            new ToolTip().SetToolTip(nudChunk, "CAW draws terrain in 256 m chunks; each texture painted in a chunk costs performance\n" +
                "(EA's guide: at most 8 per chunk, the default; 5 is kinder to slower computers). Where a chunk would have more,\n" +
                "the weakest Auto-Paint texture there is left out, faded at the chunk's edge. The base layer counts as one.");
            Controls.Add(nudChunk);
            y += 26;

            progress = new ThemedProgress(); progress.SetBounds(x, y, colW, 14); Controls.Add(progress);
            y += 18;
            status = new Label(); status.SetBounds(x, y, colW, 70); status.Text = "Click 'Read terrain' to start."; Controls.Add(status);

            // ---- preview ----
            picture = new PictureBox(); picture.SetBounds(x + colW + 18, 10, 512, 512);
            picture.SizeMode = PictureBoxSizeMode.Zoom; picture.BackColor = Color.FromArgb(40, 40, 40);
            picture.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(picture);
            // 2D map or 3D view with zoom, as in Generate Terrain (#163)
            chk3D = new CheckBox(); chk3D.Text = "3D view"; chk3D.SetBounds(x + colW + 18, 526, 80, 22);
            chk3D.CheckedChanged += delegate { if (view != null) view.Redraw(); };
            Button bTurn = new Button(); bTurn.Text = "Turn"; bTurn.SetBounds(x + colW + 102, 525, 60, 23);
            CheckBox chkExag = new CheckBox(); chkExag.Text = "Exaggerate heights (x2)"; chkExag.SetBounds(x + colW + 172, 526, 170, 22);
            view = new MapView(picture, chk3D, chkExag);
            bTurn.Click += delegate { view.Turn(); };
            new ToolTip().SetToolTip(chk3D, "The painted terrain in 3D at true height. Drag to orbit, right-drag to pan, wheel to zoom, double-click to reset.");
            Controls.Add(chk3D); Controls.Add(bTurn); Controls.Add(chkExag);
            Label cap = new Label(); cap.SetBounds(x + colW + 346, 524, 184, 32);
            cap.Text = "Colours: each chosen texture's own colour (top-down, as CAW's maps).";
            Controls.Add(cap);

            // ---- world edges ----
            GroupBox gbEdge = new GroupBox(); gbEdge.Text = "World edges"; gbEdge.SetBounds(x + colW + 18, 558, 512, 110);
            chkEdge = new CheckBox(); chkEdge.Text = "Blend world edges into the distant terrain"; chkEdge.SetBounds(10, 18, 260, 20);
            chkEdge.CheckedChanged += delegate { SchedulePreview(); };
            gbEdge.Controls.Add(chkEdge);
            Label lb = new Label(); lb.Text = "Band width"; lb.SetBounds(276, 21, 64, 16); gbEdge.Controls.Add(lb);
            tbEdge = new ThemedSlider(); tbEdge.AutoSize = false; tbEdge.Minimum = 20; tbEdge.Maximum = 400; tbEdge.Value = 120;
            tbEdge.TickFrequency = 20; tbEdge.SetBounds(340, 16, 120, 26);
            tbEdge.ValueChanged += delegate { lblEdge.Text = tbEdge.Value + " m"; SchedulePreview(); };
            gbEdge.Controls.Add(tbEdge);
            lblEdge = new Label(); lblEdge.Text = "120 m"; lblEdge.SetBounds(462, 21, 44, 16); gbEdge.Controls.Add(lblEdge);

            swDistant = Swatch(gbEdge, 10, 46);
            Button bPick = new Button(); bPick.Text = "Pick in 3D view..."; bPick.SetBounds(38, 44, 110, 24);
            bPick.Click += delegate { PickColour(false); };
            Button bGame = new Button(); bGame.Text = "From game"; bGame.SetBounds(152, 44, 80, 24);
            bGame.Click += delegate { ColourFromGame(); };
            new ToolTip().SetToolTip(bGame, "Reads the texture of the world's distant terrain object (e.g. terrainDistantEP5)\n" +
                "from the game files CAW has loaded. Copies are saved in Documents\\CAWPlus\\DistantTerrain.");
            gbEdge.Controls.Add(bGame);
            Button bImg = new Button(); bImg.Text = "From image..."; bImg.SetBounds(236, 44, 86, 24);
            bImg.Click += delegate { ColourFromImage(); };
            swGround = Swatch(gbEdge, 330, 46);
            Button bGround = new Button(); bGround.Text = "Pick your ground..."; bGround.SetBounds(358, 44, 146, 24);
            bGround.Click += delegate { PickColour(true); };
            new ToolTip().SetToolTip(bGround, "Optional: click your own grass near the edge in the same view.\n" +
                "The colour change is then distant / ground, which cancels CAW's lighting and fog.");
            gbEdge.Controls.Add(bPick); gbEdge.Controls.Add(bImg); gbEdge.Controls.Add(bGround);
            lblEdgeTex = new Label(); lblEdgeTex.SetBounds(10, 76, 400, 28); lblEdgeTex.Text = "Recolours: (base layer texture)";
            gbEdge.Controls.Add(lblEdgeTex);
            Button bTex = new Button(); bTex.Text = "Texture..."; bTex.SetBounds(424, 74, 80, 24);
            bTex.Click += delegate { ChooseEdgeSource(); };
            gbEdge.Controls.Add(bTex);
            Controls.Add(gbEdge);

            // ---- where Sims can walk ----
            GroupBox gbRoute = new GroupBox(); gbRoute.Text = "Where Sims can walk"; gbRoute.SetBounds(x + colW + 18, 674, 512, 58);
            Label lr = new Label(); lr.Text = "Block ground steeper than"; lr.SetBounds(10, 24, 150, 16); gbRoute.Controls.Add(lr);
            nudSteep = new NumericUpDown(); nudSteep.Minimum = 15; nudSteep.Maximum = 70; nudSteep.Value = 40; nudSteep.SetBounds(160, 21, 50, 20);
            gbRoute.Controls.Add(nudSteep);
            Label ld = new Label(); ld.Text = "degrees"; ld.SetBounds(214, 24, 50, 16); gbRoute.Controls.Add(ld);
            Button bRoute = new Button(); bRoute.Text = "Block steep ground"; bRoute.SetBounds(274, 19, 130, 24);
            bRoute.Click += delegate { BlockSteep(); };
            new ToolTip().SetToolTip(bRoute, "Paints CAW's Routable Surface ('Sims' = Sims can't walk there) over slopes steeper than this,\n" +
                "as EA does on its mountains. Roads, lots, water and small patches are left walkable. One undo step (Ctrl+Z).\n" +
                "To see it: CAW's Paint tool, Routable Surface.");
            gbRoute.Controls.Add(bRoute);
            Controls.Add(gbRoute);

            previewTimer = new Timer(); previewTimer.Interval = 350;
            previewTimer.Tick += delegate { previewTimer.Stop(); UpdatePreview(); };

            RefreshStyles(WorldPreset.Inspiration); // the EA world Generate Terrain modelled this map on, if any
            RefreshLayers();
            PickFromStyle(); // that world's own textures, picked by name
            // scale with the screen's DPI (text was cut off on some displays)
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            Theme.Apply(this);
        }

        private TrackBar AddSlider(GroupBox gb, string label, ref int ty)
        {
            Label l = new Label(); l.Text = label; l.SetBounds(10, ty + 6, 100, 16); gb.Controls.Add(l);
            TrackBar tb = new ThemedSlider(); tb.AutoSize = false; tb.Minimum = 0; tb.Maximum = 100; tb.Value = 50; tb.TickFrequency = 10;
            tb.SmallChange = 5; tb.LargeChange = 10; tb.SetBounds(110, ty, 195, 30);
            tb.ValueChanged += delegate { SchedulePreview(); };
            gb.Controls.Add(tb);
            ty += 34;
            return tb;
        }

        // ---------------- texture pictures ----------------

        /// <summary>The texture file behind a choice: a new layer's file, or an existing layer's texture found by name.</summary>
        private string TextureOf(LayerItem it)
        {
            if (it == null || it.Index == LayerItem.None) return null;
            if (it.IsNew) return it.TexturePath;
            try
            {
                string name = Path.GetFileNameWithoutExtension(Caw.LayerName(Caw.Terrain(Caw.CurrentWorld), it.Index));
                foreach (string t in textureFiles)
                    if (string.Equals(Path.GetFileNameWithoutExtension(t), name, StringComparison.OrdinalIgnoreCase)) return t;
            }
            catch { }
            return null;
        }

        private Bitmap Thumb(string path, int size)
        {
            if (path == null || !File.Exists(path)) return null;
            string key = path + "|" + size;
            Bitmap b;
            if (thumbCache.TryGetValue(key, out b)) return b;
            try
            {
                Bitmap full = null;
                if (Path.GetExtension(path).ToLowerInvariant() == ".dds")
                {
                    int w, h; int[] px = Dds.Decode(path, size * 2, out w, out h);
                    if (px != null)
                    {
                        full = new Bitmap(w, h);
                        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) full.SetPixel(x, y, Color.FromArgb(255, Color.FromArgb(px[y * w + x])));
                    }
                }
                else if (Path.GetExtension(path).ToLowerInvariant() != ".tga") full = new Bitmap(path);
                if (full == null) return null;
                b = new Bitmap(size, size);
                using (Graphics g = Graphics.FromImage(b)) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(full, 0, 0, size, size); }
                full.Dispose();
            }
            catch (Exception ex) { Log.Error("Texture picture for " + path + " failed", ex); b = null; }
            thumbCache[key] = b;
            return b;
        }

        private void ShowThumb(PaintRole role)
        {
            PictureBox pb; if (!roleThumbs.TryGetValue(role, out pb)) return;
            pb.Image = Thumb(TextureOf(roleCombos[role].SelectedItem as LayerItem), 64);
        }

        private void ShowBigThumb(PaintRole role)
        {
            LayerItem it = roleCombos[role].SelectedItem as LayerItem;
            Bitmap b = Thumb(TextureOf(it), 256);
            if (b == null) { Status("No picture for this choice (texture file not found, or .tga)."); return; }
            using (Form f = new Form())
            {
                f.Text = it.ToString(); f.FormBorderStyle = FormBorderStyle.FixedToolWindow; f.StartPosition = FormStartPosition.CenterParent;
                f.ClientSize = new Size(256, 256); f.ShowInTaskbar = false;
                PictureBox p = new PictureBox(); p.Dock = DockStyle.Fill; p.Image = b; f.Controls.Add(p);
                Theme.Dialog(f, this);
            }
        }

        // ---------------- layers ----------------

        public void RefreshLayers()
        {
            object world = Caw.CurrentWorld;
            if (world == null) return;
            object terr = Caw.Terrain(world);
            int count = Caw.LayerCount(terr);
            List<LayerItem> existing = new List<LayerItem>();
            string[] names = new string[count];
            for (int i = 0; i < count; i++)
            {
                string tt = Caw.LayerTerrainType(terr, i);
                names[i] = (Caw.LayerName(terr, i) + " " + tt).ToLowerInvariant();
                existing.Add(new LayerItem(i, i + ": " + Caw.LayerName(terr, i) + (tt.Length > 0 ? "  [" + tt + "]" : "") + (i == 0 ? "  (base)" : "")));
            }

            textureFolder = Caw.TerrainTexturesPath();
            List<string> textures = TextureLibrary.Scan(textureFolder);
            textureFiles = textures;
            Log.Info("Texture folder: " + (textureFolder ?? "(not found)") + ", " + textures.Count + " texture(s)");

            Dictionary<PaintRole, int> guess = GuessMapping(names);
            foreach (KeyValuePair<PaintRole, ComboBox> kv in roleCombos)
            {
                PaintRole role = kv.Key;
                LayerItem keep = kv.Value.SelectedItem as LayerItem;
                kv.Value.Items.Clear();
                kv.Value.Items.Add(new LayerItem(LayerItem.None, "(don't paint this)"));
                foreach (LayerItem it in existing) kv.Value.Items.Add(it);

                // New-layer choices from the texture folder: textures that suit this role first, then the rest.
                List<string> suited = new List<string>(), other = new List<string>();
                foreach (string t in textures) (TextureLibrary.Matches(role, Path.GetFileName(t)) ? suited : other).Add(t);
                LayerItem firstSuited = null;
                foreach (string t in suited)
                {
                    LayerItem it = new LayerItem(t, "+ new layer: " + Path.GetFileName(t));
                    if (firstSuited == null) firstSuited = it;
                    kv.Value.Items.Add(it);
                }
                foreach (string t in other) kv.Value.Items.Add(new LayerItem(t, "+ new layer: " + Path.GetFileName(t) + "  (other)"));

                // Keep the previous choice if it still exists; otherwise existing layer by name, then a suited texture.
                int sel = -1;
                if (keep != null)
                    for (int i = 0; i < kv.Value.Items.Count && sel < 0; i++)
                    {
                        LayerItem it = (LayerItem)kv.Value.Items[i];
                        if (keep.IsNew ? (it.IsNew && it.TexturePath == keep.TexturePath) : (!it.IsNew && it.Index == keep.Index && it.Index < count)) sel = i;
                    }
                if (sel < 0 && guess.ContainsKey(role)) sel = guess[role] + 1;
                if (sel < 0 && firstSuited != null) sel = kv.Value.Items.IndexOf(firstSuited);
                kv.Value.SelectedIndex = sel < 0 ? 0 : sel;
            }
            terrain = null; // world may have changed
        }

        /// <summary>Best guess from layer names/types. Index 0 is the base layer and is never auto-assigned.</summary>
        private static Dictionary<PaintRole, int> GuessMapping(string[] names)
        {
            Dictionary<PaintRole, int> g = new Dictionary<PaintRole, int>();
            for (int i = 1; i < names.Length; i++)
                if (!names[i].StartsWith(EdgeBlend.LayerPrefix.ToLowerInvariant()))
                foreach (PaintRole r in new[] { PaintRole.Rock, PaintRole.Dirt, PaintRole.Shore, PaintRole.Meadow })
                    if (!g.ContainsKey(r) && TextureLibrary.Matches(r, names[i])) { g[r] = i; break; }
            return g;
        }

        /// <summary>Mapped roles sorted by CAW layer index (bottom -> top). Returns null + message if invalid.</summary>
        private List<PaintRole> MappedRoles(Dictionary<PaintRole, int> layerOf, out string error)
        {
            error = null;
            List<PaintRole> roles = new List<PaintRole>();
            Dictionary<int, PaintRole> used = new Dictionary<int, PaintRole>();
            Dictionary<string, PaintRole> usedTex = new Dictionary<string, PaintRole>(StringComparer.OrdinalIgnoreCase);
            int nextNew = 1000; // new layers are added on top of the existing ones, in this order
            foreach (KeyValuePair<PaintRole, ComboBox> kv in roleCombos)
            {
                LayerItem it = kv.Value.SelectedItem as LayerItem;
                if (it == null || it.Index == LayerItem.None) continue;
                if (it.IsNew)
                {
                    if (usedTex.ContainsKey(it.TexturePath)) { error = "Two types are set to the same new texture (" + Path.GetFileName(it.TexturePath) + ")."; return null; }
                    usedTex[it.TexturePath] = kv.Key;
                    layerOf[kv.Key] = nextNew++;
                }
                else
                {
                    if (used.ContainsKey(it.Index)) { error = "Two types are set to the same layer (" + it + ")."; return null; }
                    used[it.Index] = kv.Key;
                    layerOf[kv.Key] = it.Index;
                }
                roles.Add(kv.Key);
            }
            roles.Sort(delegate (PaintRole a, PaintRole b) { return layerOf[a].CompareTo(layerOf[b]); });
            return roles;
        }

        private PaintSettings ReadSettings()
        {
            PaintSettings s = new PaintSettings();
            s.Rock = tbRock.Value; s.Dirt = tbDirt.Value; s.Shore = tbShore.Value; s.Meadow = tbMeadow.Value; s.Softness = tbSoft.Value; s.Detail = tbDetail.Value;
            s.Seed = (int)nudSeed.Value; s.WaterLevel = (float)nudWater.Value;
            return s;
        }

        // ---------------- analyse ----------------

        private bool Analyse()
        {
            if (busy) return false;
            object world = Caw.CurrentWorld;
            if (world == null) { Status("Open a world first."); return false; }
            SetBusy(true);
            try
            {
                RefreshLayers();
                int count = Caw.LayerCount(Caw.Terrain(world));
                terrain = TerrainSampler.Analyze(world, count, OnProgress);
                nudWater.Value = Math.Max(nudWater.Minimum, Math.Min(nudWater.Maximum, (decimal)Math.Round(terrain.WaterLevel, 1)));
                int f = Math.Max(1, terrain.MapW / 384);
                previewHeights = TerrainSampler.Downsample(terrain.Heights, f);
                previewWaterOffset = terrain.WaterOffset != null ? TerrainSampler.Downsample(terrain.WaterOffset, f) : null;
                previewCell = terrain.CellMeters * f;
                float lo, hi; terrain.Heights.MinMax(out lo, out hi);
                Status(string.Format("Terrain read: {0}x{1} map, {2:F0} m wide, heights {3:F0}-{4:F0} m, water {5:F1} m. {6}",
                    terrain.MapW, terrain.MapH, terrain.WorldSize, lo, hi, terrain.WaterLevel, terrain.Notes));
                SetBusy(false);
                UpdatePreview();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Analyse failed", ex);
                Status("Reading terrain failed: " + ex.Message);
                SetBusy(false);
                return false;
            }
        }

        // ---------------- preview ----------------

        private void SchedulePreview() { if (previewHeights != null) { previewTimer.Stop(); previewTimer.Start(); } }

        private void UpdatePreview()
        {
            if (previewHeights == null || busy) return;
            try
            {
                PaintSettings s = ReadSettings();
                s.WaterOffset = previewWaterOffset;
                Dictionary<PaintRole, int> layerOf = new Dictionary<PaintRole, int>();
                string err;
                List<PaintRole> mapped = MappedRoles(layerOf, out err);
                StyleProfile style = cbStyle.SelectedItem as StyleProfile;
                // What EA's own worlds paint each kind of ground with (built-in styles from their .world files).
                Dictionary<PaintRole, List<string>> ea = new Dictionary<PaintRole, List<string>>();
                foreach (PaintRole r in PaintEngine.Roles) ea[r] = new List<string>();
                foreach (StyleProfile p in StyleProfile.LoadAll())
                    if (p.FromEA)
                        foreach (KeyValuePair<PaintRole, RoleStyle> kv in p.Roles) ea[kv.Key].AddRange(kv.Value.Textures);
                Dictionary<PaintRole, Grid> masks;
                string matchNote = null;
                if (style != null)
                {
                    List<PaintRole> roles = mapped != null && mapped.Count > 0 ? mapped : new List<PaintRole>(PaintEngine.Roles);
                    string key = style.Name + "|" + s.Seed + "|" + s.WaterLevel + "|" + string.Join(",", Array.ConvertAll(roles.ToArray(), delegate (PaintRole r) { return r.ToString(); }));
                    if (previewGen == null || previewGenKey != key)
                    {
                        previewGen = new StyledGenerator(style, previewHeights, previewCell, s.WaterLevel, previewWaterOffset, s.Seed, roles);
                        previewGenKey = key;
                    }
                    masks = previewGen.Fit(PaintJob.Amounts(s), s.Softness / 50f, StyledGenerator.MaxPasses, null);
                    PaintJob.FillMissingRoles(masks, style, previewHeights, previewCell, s);
                    matchNote = string.Format("Match to {0}: {1:F0}% after {2} pass(es).", style.Name, previewGen.Match, previewGen.Passes);
                }
                else masks = PaintEngine.Compute(previewHeights, previewCell, s, null);
                Bitmap bmp;
                Dictionary<PaintRole, Color> cols = RoleColours();
                Color baseCol = BaseLayerColour();
                if (mapped != null && mapped.Count > 0 && ((chkVariety.Checked && extras.Count > 0) || (chkBaseTex.Checked && basePick != null)))
                    bmp = VarietyPreview(masks, mapped, layerOf, cols, baseCol, s);
                else if (mapped != null && mapped.Count > 0)
                    bmp = PaintEngine.RenderPreview(previewHeights, PaintEngine.ToCawStack(masks, mapped), mapped, s.WaterLevel, previewCell, 512, previewWaterOffset, cols, baseCol);
                else
                    bmp = PaintEngine.RenderPreview(previewHeights, masks, PaintEngine.Roles, s.WaterLevel, previewCell, 512, previewWaterOffset, cols, baseCol);
                if (chkEdge.Checked && hasDistant)
                    OverlayEdge(bmp, EdgeBlend.Mask(previewHeights, previewCell, tbEdge.Value, s.Seed), swDistant.BackColor);
                Bitmap old = map2D; map2D = bmp;
                view.SetMap(map2D, previewHeights, previewCell, s.WaterLevel, null);
                if (old != null && !ReferenceEquals(old, picture.Image)) old.Dispose();
                if (err != null) Status(err);
                else if (matchNote != null) Status(matchNote);
            }
            catch (Exception ex) { Log.Error("Preview failed", ex); Status("Preview failed: " + ex.Message); }
        }

        /// <summary>
        /// The preview with the style's other textures and its base texture, split as Paint world splits them
        /// (PaintEngine.ToCawLayers). Layers not added yet get numbers above the existing ones, in the order Paint world
        /// adds them, so the stacking matches.
        /// </summary>
        private Bitmap VarietyPreview(Dictionary<PaintRole, Grid> masks, List<PaintRole> mapped, Dictionary<PaintRole, int> layerOf,
                                      Dictionary<PaintRole, Color> cols, Color baseCol, PaintSettings s)
        {
            Dictionary<PaintRole, List<KeyValuePair<int, float>>> layersOfRole = new Dictionary<PaintRole, List<KeyValuePair<int, float>>>();
            Dictionary<int, Color> colourOf = new Dictionary<int, Color>();
            int next = 2000;
            List<int> taken = new List<int>();
            foreach (PaintRole r in mapped) taken.Add(layerOf[r]);
            foreach (PaintRole r in mapped)
            {
                float ms; if (!mainShare.TryGetValue(r, out ms)) ms = 1;
                List<KeyValuePair<int, float>> l = new List<KeyValuePair<int, float>>();
                l.Add(new KeyValuePair<int, float>(layerOf[r], ms));
                Color rc; colourOf[layerOf[r]] = cols.TryGetValue(r, out rc) ? rc : PaintEngine.RoleColor(r);
                List<TexPick> ex;
                if (chkVariety.Checked && extras.TryGetValue(r, out ex))
                    foreach (TexPick tp in ex)
                    {
                        int idx = tp.Index >= 0 ? tp.Index : next++;
                        if (idx <= 0 || taken.Contains(idx)) continue;
                        taken.Add(idx); l.Add(new KeyValuePair<int, float>(idx, tp.Share));
                        Color c; colourOf[idx] = TexColour(tp.Path ?? TextureOfLayer(idx), out c) ? c : colourOf[layerOf[r]];
                    }
                layersOfRole[r] = l;
            }
            int baseIdx = -1;
            if (chkBaseTex.Checked && basePick != null)
            {
                int idx = basePick.Index >= 0 ? basePick.Index : next++;
                if (idx > 0 && !taken.Contains(idx))
                {
                    baseIdx = idx;
                    Color c; colourOf[idx] = TexColour(basePick.Path ?? TextureOfLayer(idx), out c) ? c : baseCol;
                }
            }
            Dictionary<int, Grid> byLayer = PaintEngine.ToCawLayers(masks, layersOfRole, baseIdx, 60f / Math.Max(0.1f, previewCell), s.Seed);
            List<int> order = new List<int>(byLayer.Keys); order.Sort();
            List<Grid> grids = new List<Grid>(); List<Color> colours = new List<Color>();
            foreach (int i in order) { grids.Add(byLayer[i]); Color c; colours.Add(colourOf.TryGetValue(i, out c) ? c : baseCol); }
            return PaintEngine.RenderStack(previewHeights, grids, colours, baseCol, s.WaterLevel, previewCell, 512, previewWaterOffset);
        }

        /// <summary>The texture file of an existing CAW layer, found by the layer's name among the SourceTextures.</summary>
        private string TextureOfLayer(int idx)
        {
            try
            {
                object t = Caw.Terrain(Caw.CurrentWorld);
                if (t == null || idx < 0 || idx >= Caw.LayerCount(t)) return null;
                string p = Caw.LayerTexturePath(t, idx);
                if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
                string name = Path.GetFileNameWithoutExtension(Caw.LayerName(t, idx));
                foreach (string f in textureFiles)
                    if (string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase)) return f;
            }
            catch { }
            return null;
        }

        /// <summary>The mean colour of each role's chosen texture (roles without a readable texture keep the role colour).</summary>
        private Dictionary<PaintRole, Color> RoleColours()
        {
            Dictionary<PaintRole, Color> cols = new Dictionary<PaintRole, Color>();
            foreach (KeyValuePair<PaintRole, ComboBox> kv in roleCombos)
            {
                Color c;
                if (TexColour(TextureOf(kv.Value.SelectedItem as LayerItem), out c)) cols[kv.Key] = c;
            }
            return cols;
        }

        /// <summary>The base layer's (layer 0) texture colour: its texture file from CAW, else a SourceTextures file of its name.</summary>
        private Color BaseLayerColour()
        {
            Color c;
            try
            {
                object t = Caw.Terrain(Caw.CurrentWorld);
                if (t != null)
                {
                    string p = Caw.LayerTexturePath(t, 0);
                    if (TexColour(p, out c)) return c;
                    string name = Path.GetFileNameWithoutExtension(Caw.LayerName(t, 0));
                    foreach (string f in textureFiles)
                        if (string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase) && TexColour(f, out c)) return c;
                }
            }
            catch (Exception ex) { Log.Error("Base layer colour failed", ex); }
            return PaintEngine.BaseColor;
        }

        private bool TexColour(string path, out Color c)
        {
            c = Color.Empty;
            if (string.IsNullOrEmpty(path)) return false;
            if (texColour.TryGetValue(path, out c)) return c != Color.Empty;
            try
            {
                if (File.Exists(path))
                {
                    TextureFeatures f = TextureAnalyzer.Analyze(path);
                    if (f != null) c = Color.FromArgb((int)(f.R * 255), (int)(f.G * 255), (int)(f.B * 255));
                }
            }
            catch (Exception ex) { Log.Error("Texture colour for " + path + " failed", ex); }
            texColour[path] = c;
            return c != Color.Empty;
        }

        // ---------------- apply ----------------

        private string WorldStateDir(object world)
        {
            return Caw.WorldDir(Path.Combine(Log.Dir, "Worlds"), world);
        }

        private void Apply()
        {
            if (busy || Injector.BusyElsewhere(this)) return;
            if (terrain == null && !Analyse()) return;
            object world = Caw.CurrentWorld;
            if (world == null) { Status("Open a world first."); return; }

            Dictionary<PaintRole, int> layerOf = new Dictionary<PaintRole, int>();
            string err;
            List<PaintRole> mapped = MappedRoles(layerOf, out err);
            if (mapped == null) { MessageBox.Show(this, err, "Auto-Paint", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (mapped.Count == 0) { MessageBox.Show(this, "Choose at least one paint layer to fill.", "Auto-Paint"); return; }

            string list = "";
            foreach (PaintRole r in mapped) list += "\n  - " + roleCombos[r].SelectedItem + "  <- " + PaintEngine.RoleLabel(r);
            if (layerOf.ContainsValue(0)) list += "\n\nWarning: layer 0 is usually the base layer.";
            if (chkVariety.Checked)
                foreach (KeyValuePair<PaintRole, List<TexPick>> kv in extras)
                    if (layerOf.ContainsKey(kv.Key)) foreach (TexPick tp in kv.Value) list += "\n  - " + tp.Name + (tp.Index > 0 ? "" : " (new layer)") + "  <- " + PaintEngine.RoleLabel(kv.Key) + " (variety)";
            if (chkBaseTex.Checked && basePick != null) list += "\n  - " + basePick.Name + (basePick.Index > 0 ? "" : " (new layer)") + "  <- base, under everything";
            if (MessageBox.Show(this, "Repaint these layers across the whole world?" + list +
                    "\n\nTheir current paint is backed up first; use 'Undo paint' to restore it.",
                    "Auto-Paint", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            SetBusy(true);
            try
            {
                // 1. Add any new layers chosen from the texture folder (CAW's own undoable Add Layer command).
                List<int> added = new List<int>();
                foreach (PaintRole r in new List<PaintRole>(mapped))
                {
                    LayerItem it = (LayerItem)roleCombos[r].SelectedItem;
                    if (!it.IsNew) continue;
                    OnProgress("Adding layer " + Path.GetFileName(it.TexturePath), 0);
                    int idx = -1;
                    try { idx = Caw.AddLayer(world, it.TexturePath, TextureLibrary.TerrainType(r), Path.GetFileName(it.TexturePath)); }
                    catch (Exception ex) { Log.Error("Adding layer from " + it.TexturePath + " failed", ex); }
                    if (idx < 0)
                    {
                        SetBusy(false);
                        Status("CAW could not add a layer from " + Path.GetFileName(it.TexturePath) + ". See Documents\\CAWPlus\\CAWPlus.log.");
                        RefreshLayers();
                        return;
                    }
                    layerOf[r] = idx;
                    added.Add(idx);
                    Application.DoEvents();
                }
                if (added.Count > 0)
                {
                    mapped.Sort(delegate (PaintRole a, PaintRole b) { return layerOf[a].CompareTo(layerOf[b]); });
                    TerrainInfo keepTerrain = terrain; // same world; adding layers doesn't change the heights
                    RefreshLayers();
                    terrain = keepTerrain;
                    foreach (PaintRole r in mapped) roleCombos[r].SelectedIndex = layerOf[r] + 1; // now an existing layer
                }

                // 1b. The style's other textures (variety) and its base texture: existing layers, else added like the above.
                Dictionary<PaintRole, List<KeyValuePair<int, float>>> layersOfRole = new Dictionary<PaintRole, List<KeyValuePair<int, float>>>();
                int baseIdx = -1;
                {
                    List<int> taken = new List<int>();
                    foreach (PaintRole r in mapped) taken.Add(layerOf[r]);
                    bool addedMore = false;
                    foreach (PaintRole r in mapped)
                    {
                        float ms; if (!mainShare.TryGetValue(r, out ms)) ms = 1;
                        List<KeyValuePair<int, float>> l = new List<KeyValuePair<int, float>>();
                        l.Add(new KeyValuePair<int, float>(layerOf[r], ms));
                        List<TexPick> ex;
                        if (chkVariety.Checked && extras.TryGetValue(r, out ex))
                            foreach (TexPick tp in ex)
                            {
                                int idx = tp.Index;
                                if (idx < 0)
                                {
                                    OnProgress("Adding layer " + Path.GetFileName(tp.Path), 0);
                                    try { idx = Caw.AddLayer(world, tp.Path, TextureLibrary.TerrainType(r), Path.GetFileName(tp.Path)); }
                                    catch (Exception ex2) { Log.Error("Adding variety layer from " + tp.Path + " failed", ex2); idx = -1; }
                                    if (idx >= 0) { tp.Index = idx; tp.Path = null; addedMore = true; }
                                    Application.DoEvents();
                                }
                                if (idx > 0 && !taken.Contains(idx)) { taken.Add(idx); l.Add(new KeyValuePair<int, float>(idx, tp.Share)); }
                            }
                        layersOfRole[r] = l;
                    }
                    if (chkBaseTex.Checked && basePick != null)
                    {
                        int idx = basePick.Index;
                        if (idx < 0)
                        {
                            PaintRole br = PaintRole.Meadow;
                            foreach (PaintRole r in PaintEngine.Roles) if (TextureLibrary.Matches(r, basePick.Name)) { br = r; break; }
                            try { idx = Caw.AddLayer(world, basePick.Path, TextureLibrary.TerrainType(br), Path.GetFileName(basePick.Path)); }
                            catch (Exception ex2) { Log.Error("Adding base texture layer failed", ex2); idx = -1; }
                            if (idx >= 0) { basePick.Index = idx; basePick.Path = null; addedMore = true; }
                        }
                        if (idx > 0 && !taken.Contains(idx)) baseIdx = idx;
                    }
                    if (addedMore)
                    {
                        TerrainInfo keepTerrain = terrain;
                        RefreshLayers();
                        terrain = keepTerrain;
                        foreach (PaintRole r in mapped) roleCombos[r].SelectedIndex = layerOf[r] + 1;
                    }
                }
                bool variety = baseIdx >= 0;
                foreach (List<KeyValuePair<int, float>> l in layersOfRole.Values) if (l.Count > 1) variety = true;

                int edgeIdx = -1;
                if (chkEdge.Checked)
                {
                    if (!hasDistant) { SetBusy(false); Status("Pick the distant terrain colour first (World edges)."); return; }
                    OnProgress("Making edge texture", 0);
                    string eerr;
                    edgeIdx = PrepareEdgeLayer(world, out eerr);
                    if (edgeIdx < 0) { SetBusy(false); Status(eerr); RefreshLayers(); return; }
                    TerrainInfo keepT = terrain; RefreshLayers(); terrain = keepT;
                    foreach (PaintRole r in mapped) roleCombos[r].SelectedIndex = layerOf[r] + 1;
                }

                object terr = Caw.Terrain(world);
                int count = Caw.LayerCount(terr);

                // 2. Backup every layer, plus the record of what Auto-Paint last generated (for manual-edit tracking).
                string backup = Path.Combine(Caw.WorldDir(Path.Combine(Log.Dir, "Backups"), world),
                                             DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(backup);
                ManualEdits.PruneBackups(Path.GetDirectoryName(backup), 10);
                int backedUp = 0;
                for (int i = 0; i < count; i++)
                {
                    OnProgress("Backing up layer " + i, 100 * i / Math.Max(1, count));
                    try { if (Caw.ExportLayer(world, i, Path.Combine(backup, "layer_" + i + ".png"))) backedUp++; }
                    catch (Exception ex) { Log.Error("Backup of layer " + i + " failed", ex); }
                }
                string state = WorldStateDir(world);
                ManualEdits.CopyDir(state, Path.Combine(backup, "state"));
                Log.Info("Backed up " + backedUp + "/" + count + " layers to " + backup);
                if (backedUp == 0 &&
                    MessageBox.Show(this, "No layers could be backed up, so this can't be undone. Continue anyway?", "Auto-Paint",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                { SetBusy(false); Status("Cancelled."); return; }

                // 3-4. Masks, manual edits, old layers and the per-chunk limit: worked out on a worker thread so CAW stays
                // responsive (the tester's v0.40 test: Auto-Paint froze CAW). CAW is only asked for names here, up front.
                PaintSettings s = ReadSettings();
                s.WaterOffset = terrain.WaterOffset;
                PaintJob job = new PaintJob();
                job.Heights = terrain.Heights; job.WaterOffset = terrain.WaterOffset; job.Cell = terrain.CellMeters; job.S = s;
                job.Style = cbStyle.SelectedItem as StyleProfile;
                job.Gains = previewGen != null && job.Style != null ? previewGen.GainsText() : "";
                job.Mapped = new List<PaintRole>(mapped);
                foreach (PaintRole r in mapped) job.LayerOf[r] = layerOf[r];
                job.Variety = variety; job.LayersOfRole = layersOfRole; job.BaseIdx = baseIdx;
                job.PatchPx = 60f / Math.Max(0.1f, terrain.CellMeters);
                job.KeepEdits = chkKeepEdits.Checked;
                job.State = state; job.Backup = backup;
                job.OutDir = Path.Combine(Log.Dir, "LastPaint");
                job.Fingerprint = ManualEdits.Fingerprint(terrain.Heights);
                ManualEdits.CurrentTerrain = job.Fingerprint;
                job.Names = new string[count];
                for (int i = 0; i < count; i++) job.Names[i] = Caw.LayerName(terr, i);
                job.EdgeLayer = edgeIdx; job.ChunkLimit = (int)nudChunk.Value;
                // 3-4. Masks, manual edits, old layers, the per-chunk limit and the layer PNGs: in CAWPlusHelper (64-bit,
                // no memory squeeze) or, without it, on a worker thread here. CAW only imports what it lists.
                foreach (string old in Directory.Exists(job.OutDir) ? Directory.GetFiles(job.OutDir, "layer_*.png") : new string[0])
                    try { File.Delete(old); } catch { }
                RunInBackground(delegate () { Helper.Paint(job, BgProgress); return null; });
                previewGen = null; // its corrections were used; the preview makes a new one
                GC.Collect();

                // 5. Import into CAW and remember exactly what was written.
                string runId = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                List<string> failed = new List<string>();
                int k = 0;
                foreach (int idx in job.Written)
                {
                    OnProgress("Painting layer " + idx, 100 * k++ / Math.Max(1, job.Written.Count));
                    string p = Path.Combine(job.OutDir, "layer_" + idx + ".png");
                    bool ok = false;
                    try { ok = Caw.ImportLayer(world, idx, p); }
                    catch (Exception ex) { Log.Error("Import of layer " + idx + " failed", ex); }
                    if (ok) ManualEdits.Record(state, idx, Caw.LayerName(terr, idx), p, runId);
                    else failed.Add(idx.ToString());
                    Application.DoEvents();
                }
                foreach (int idx in job.Cleared)
                {
                    string p = Path.Combine(job.OutDir, "layer_" + idx + "_empty.png");
                    try { if (Caw.ImportLayer(world, idx, p)) ManualEdits.Forget(state, idx); else failed.Add(idx + " (emptying)"); }
                    catch (Exception ex) { Log.Error("Emptying old layer " + idx + " failed", ex); }
                }
                string matchNote = job.MatchNote, kept = job.Kept;
                int paintedCount = job.Written.Count;
                // 6. World edges: own layer, on top of the others it was added after.
                if (edgeIdx >= 0)
                {
                    OnProgress("Painting world edges", 95);
                    Grid edge = EdgeBlend.Mask(terrain.Heights, terrain.CellMeters, tbEdge.Value, s.Seed);
                    if (chkKeepEdits.Checked)
                    {
                        string note;
                        ManualEdits.Merge(state, edgeIdx, Caw.LayerName(terr, edgeIdx), Path.Combine(backup, "layer_" + edgeIdx + ".png"), edge, out note);
                        Log.Info("Manual edits, edge layer " + edgeIdx + ": " + note);
                    }
                    string p = Path.Combine(job.OutDir, "layer_" + edgeIdx + "_edge.png");
                    ImageIO.WriteGrayPng(edge, p);
                    bool ok = false;
                    try { ok = Caw.ImportLayer(world, edgeIdx, p); }
                    catch (Exception ex) { Log.Error("Import of edge layer failed", ex); }
                    if (ok) ManualEdits.Record(state, edgeIdx, Caw.LayerName(terr, edgeIdx), p, runId);
                    else failed.Add(edgeIdx + " (edges)");
                    // Older edge layers with another colour are emptied so only the current one shows.
                    for (int i = 1; i < count; i++)
                        if (i != edgeIdx && Caw.LayerName(terr, i).StartsWith(EdgeBlend.LayerPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            string z = Path.Combine(job.OutDir, "layer_" + i + "_empty.png");
                            ImageIO.WriteGrayPng(new Grid(edge.W, edge.H), z);
                            try { Caw.ImportLayer(world, i, z); } catch (Exception ex) { Log.Error("Clearing old edge layer " + i + " failed", ex); }
                        }
                }

                ManualEdits.EndRun(state, runId);
                OnProgress("Done", 100);
                if (failed.Count == 0) Injector.Done("paint");
                Status(failed.Count == 0
                    ? "Painted " + paintedCount + " layer(s)" + (added.Count > 0 ? " (" + added.Count + " new)" : "") +
                      (variety ? " with the style's own textures" + (baseIdx >= 0 ? " and base" : "") : "") + "." +
                      (kept.Length > 0 ? " Kept manual edits on" + kept + "." : "") + matchNote + " Backup: " + backup
                    : "CAW refused layer(s) " + string.Join(", ", failed.ToArray()) + ". See Documents\\CAWPlus\\CAWPlus.log.");
            }
            catch (Exception ex)
            {
                Log.Error("Apply failed", ex);
                Status(ex is OutOfMemoryException
                    ? "Painting failed: CAW ran out of memory (it is a 32-bit program). Save, restart CAW Plus and paint again before other tools."
                    : "Painting failed: " + ex.Message);
            }
            SetBusy(false);
        }

        private void RestoreLastBackup()
        {
            if (busy || Injector.BusyElsewhere(this)) return;
            object world = Caw.CurrentWorld;
            if (world == null) return;
            string root = Caw.WorldDir(Path.Combine(Log.Dir, "Backups"), world);
            if (!Directory.Exists(root)) { Status("No backups for this world yet."); return; }
            List<string> dirs = new List<string>();
            foreach (string d in Directory.GetDirectories(root))
                if (!d.EndsWith("_restored")) dirs.Add(d);
            if (dirs.Count == 0) { Status("No unused backups left for this world."); return; }
            dirs.Sort(StringComparer.OrdinalIgnoreCase);
            string latest = dirs[dirs.Count - 1];
            if (MessageBox.Show(this, "Restore paint from " + Path.GetFileName(latest) + "?", "Undo paint",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            SetBusy(true);
            int restored = 0;
            try
            {
                int count = Caw.LayerCount(Caw.Terrain(world));
                for (int idx = 0; idx < count; idx++)
                {
                    string f = Path.Combine(latest, "layer_" + idx + ".png");
                    if (!File.Exists(f)) continue;
                    try { if (Caw.ImportLayer(world, idx, f)) restored++; }
                    catch (Exception ex) { Log.Error("Restore of layer " + idx + " failed", ex); }
                }
                // Layers Auto-Paint added in that run were empty when backed up, so they are now empty again.
                // Put back the matching record of what Auto-Paint had generated, so manual-edit tracking stays correct.
                string state = WorldStateDir(world);
                ManualEdits.DeleteDir(state);
                ManualEdits.CopyDir(Path.Combine(latest, "state"), state);
            }
            catch (Exception ex) { Log.Error("Undo paint failed", ex); Status("Undo paint failed: " + ex.Message); SetBusy(false); return; }
            SetBusy(false);
            // Move the used backup aside so the next 'Undo paint' goes one step further back.
            try { Directory.Move(latest, latest + "_restored"); } catch { }
            Log.Info("Undo paint: restored " + restored + " layer(s) and the edit record from " + latest);
            Status("Restored " + restored + " layer(s) from " + Path.GetFileName(latest) + ".");
        }

        // ---------------- world edges ----------------

        private static Panel Swatch(Control parent, int x, int y)
        {
            Panel p = new Panel(); p.SetBounds(x, y, 22, 20); p.BorderStyle = BorderStyle.FixedSingle; p.BackColor = Theme.Input;
            parent.Controls.Add(p);
            return p;
        }

        private void SetDistant(Color c)
        {
            swDistant.BackColor = c; hasDistant = true;
            if (!chkEdge.Checked) chkEdge.Checked = true; else SchedulePreview();
            Log.Info("Distant terrain colour: " + c.R + "," + c.G + "," + c.B);
        }

        /// <summary>Paints CAW's Routable Surface (no Sims) over steep ground, keeping roads, lots and water walkable (#90).</summary>
        private void BlockSteep()
        {
            if (busy || Injector.BusyElsewhere(this)) return;
            object world = Caw.CurrentWorld;
            if (world == null || terrain == null) { Status("Open a world and click 'Read terrain' first."); return; }
            TerrainInfo ti = terrain;
            int step = Math.Max(1, (int)Math.Round(4f / Math.Max(0.1f, ti.CellMeters)));
            float m = ti.CellMeters * step;
            int w = (ti.Heights.W + step - 1) / step, h = (ti.Heights.H + step - 1) / step;
            bool[] keep = new bool[w * h];
            int roads = 0, lots = 0;
            try
            {
                List<float[]> paths, bridges;
                List<float[]> splines = Caw.RoadSplines(world, out paths, out bridges);
                splines.AddRange(paths);
                foreach (float[] k in splines)
                {
                    List<float[]> pts = new List<float[]>();
                    for (int i = 0; i <= 16; i++)
                    {
                        float t = i / 16f, u = 1 - t, x, z;
                        if (k.Length >= 8)
                        {
                            x = u * u * u * k[0] + 3 * u * u * t * k[2] + 3 * u * t * t * k[4] + t * t * t * k[6];
                            z = u * u * u * k[1] + 3 * u * u * t * k[3] + 3 * u * t * t * k[5] + t * t * t * k[7];
                        }
                        else { x = k[0] + (k[k.Length - 2] - k[0]) * t; z = k[1] + (k[k.Length - 1] - k[1]) * t; }
                        float mx, my; TerrainSampler.WorldToMap(ti, x, z, out mx, out my);
                        pts.Add(new float[] { mx / step, my / step });
                    }
                    RouteMask.MarkLine(keep, w, h, pts, 10f, m);
                    roads++;
                }
                foreach (float[] c in Caw.LotCircles())
                {
                    float mx, my; TerrainSampler.WorldToMap(ti, c[0], c[1], out mx, out my);
                    RouteMask.MarkDisc(keep, w, h, mx / step, my / step, c[2] + 4f, m);
                    lots++;
                }
            }
            catch (Exception ex) { Log.Error("Block steep ground: reading roads and lots failed", ex); }
            float deg = (float)nudSteep.Value;
            int bw, bh;
            bool[] block = RouteMask.Steep(ti.Heights, ti.CellMeters, step, ti.WaterLevel, deg, 400f, keep, out bw, out bh);
            List<float[]> points = new List<float[]>();
            for (int y = 0; y < bh; y++)
                for (int x = 0; x < bw; x++)
                    if (block[y * bw + x])
                    {
                        float wx, wz; TerrainSampler.MapToWorld(ti, (x + 0.5f) * step, (y + 0.5f) * step, out wx, out wz);
                        points.Add(new float[] { wx, wz });
                    }
            float ha = points.Count * m * m / 10000f, landHa = ti.WorldSize * ti.WorldSize / 10000f;
            Log.Info(string.Format("Block steep ground: > {0} deg, {1} cells of {2:F1} m ({3:F1} ha, {4:F1}% of the map); kept walkable: {5} road pieces, {6} lots",
                deg, points.Count, m, ha, 100 * ha / landHa, roads, lots));
            if (points.Count == 0) { Status("No ground steeper than " + deg + " degrees (outside roads, lots and water)."); return; }
            if (MessageBox.Show(this, string.Format("Block {0:F1} ha ({1:F1}% of the map) steeper than {2} degrees for Sims?\n\n" +
                    "Roads, lots and water stay walkable. CAW's Undo (Ctrl+Z) removes it.", ha, 100 * ha / landHa, deg),
                    "Block steep ground", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            SetBusy(true);
            try
            {
                int n = Caw.PaintRoutable(world, points, (uint)Math.Max(1, Math.Ceiling(m)), 1u, OnProgress);
                SetBusy(false);
                Status(string.Format("Blocked {0:F1} ha of steep ground ({1} brush stamps). See it with CAW's Paint tool, Routable Surface.", ha, n));
                Log.Info("Block steep ground: " + n + " stamps painted");
                Injector.Done("steep");
            }
            catch (Exception ex) { Log.Error("Block steep ground failed", ex); SetBusy(false); Status("Blocking steep ground failed: " + ex.Message); }
        }

        private void PickColour(bool ground)
        {
            Bitmap shot;
            Hide();
            try { Application.DoEvents(); System.Threading.Thread.Sleep(150); shot = Caw.CaptureRenderPanel(); }
            catch (Exception ex) { Log.Error("Screen capture failed", ex); shot = null; } // e.g. locked or secure desktop
            finally { Show(); }
            if (shot == null) { Status("Couldn't capture CAW's 3D view."); return; }
            using (Form f = new Form())
            {
                f.Text = ground ? "Click your own grass near the world edge" : "Click the distant terrain";
                f.StartPosition = FormStartPosition.CenterParent; f.ShowInTaskbar = false;
                float k = Math.Min(1f, Math.Min(1000f / shot.Width, 680f / shot.Height));
                f.ClientSize = new Size((int)(shot.Width * k), (int)(shot.Height * k));
                PictureBox pb = new PictureBox(); pb.Dock = DockStyle.Fill; pb.SizeMode = PictureBoxSizeMode.StretchImage; pb.Image = shot;
                pb.Cursor = Cursors.Cross;
                Color picked = Color.Empty;
                pb.MouseClick += delegate (object o, MouseEventArgs e)
                {
                    picked = EdgeBlend.AverageColour(shot, (int)(e.X / k), (int)(e.Y / k), 4);
                    f.DialogResult = DialogResult.OK;
                };
                f.Controls.Add(pb);
                if (Theme.Dialog(f, this) == DialogResult.OK && !picked.IsEmpty)
                {
                    if (ground) { swGround.BackColor = picked; hasGround = true; SchedulePreview(); Log.Info("Ground colour: " + picked.R + "," + picked.G + "," + picked.B); }
                    else SetDistant(picked);
                }
            }
            shot.Dispose();
        }

        private void ColourFromGame()
        {
            try
            {
                DistantTerrain.Result r = DistantTerrain.Read();
                if (r.Colour.IsEmpty) { Status("Distant terrain: " + r.Note); return; }
                SetDistant(r.Colour); hasGround = false; swGround.BackColor = Theme.Input;
                Status("Distant terrain colour from " + r.ObjectName + " (" + Path.GetFileName(r.Texture) + ")");
            }
            catch (Exception ex) { Log.Error("Reading distant terrain failed", ex); Status(ex.Message); }
        }

        private void ColourFromImage()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Image of the distant terrain (texture or screenshot)";
                d.Filter = "Images|*.dds;*.png;*.jpg;*.jpeg;*.bmp|All files|*.*";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { SetDistant(EdgeBlend.AverageColour(d.FileName)); hasGround = false; swGround.BackColor = Theme.Input; }
                catch (Exception ex) { Status(ex.Message); }
            }
        }

        private void ChooseEdgeSource()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Grass texture to recolour for the edges";
                d.Filter = "Textures|*.dds;*.png;*.jpg;*.bmp|All files|*.*";
                if (textureFolder != null) d.InitialDirectory = textureFolder;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                edgeSource = d.FileName;
                lblEdgeTex.Text = "Recolours: " + Path.GetFileName(edgeSource);
            }
        }

        /// <summary>The texture to recolour: the chosen one, else the base layer's file, else the meadow layer's.</summary>
        private string ResolveEdgeSource(object terr)
        {
            if (edgeSource != null && File.Exists(edgeSource)) return edgeSource;
            Dictionary<string, string> byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string t in TextureLibrary.Scan(textureFolder)) byName[Path.GetFileNameWithoutExtension(t)] = t;
            string f;
            if (byName.TryGetValue(Path.GetFileNameWithoutExtension(Caw.LayerName(terr, 0)), out f)) return f;
            LayerItem m = roleCombos[PaintRole.Meadow].SelectedItem as LayerItem;
            if (m != null && m.IsNew) return m.TexturePath;
            if (m != null && m.Index >= 0 && byName.TryGetValue(Path.GetFileNameWithoutExtension(Caw.LayerName(terr, m.Index)), out f)) return f;
            return null;
        }

        private static void OverlayEdge(Bitmap bmp, Grid mask, Color c)
        {
            System.Drawing.Imaging.BitmapData bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            byte[] row = new byte[bd.Stride];
            for (int y = 0; y < bmp.Height; y++)
            {
                IntPtr p = new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride);
                System.Runtime.InteropServices.Marshal.Copy(p, row, 0, bd.Stride);
                int my = Math.Min(mask.H - 1, y * mask.H / bmp.Height);
                for (int x = 0; x < bmp.Width; x++)
                {
                    float a = mask[Math.Min(mask.W - 1, x * mask.W / bmp.Width), my];
                    if (a <= 0) continue;
                    row[x * 3] = (byte)(row[x * 3] * (1 - a) + c.B * a);
                    row[x * 3 + 1] = (byte)(row[x * 3 + 1] * (1 - a) + c.G * a);
                    row[x * 3 + 2] = (byte)(row[x * 3 + 2] * (1 - a) + c.R * a);
                }
                System.Runtime.InteropServices.Marshal.Copy(row, 0, p, bd.Stride);
            }
            bmp.UnlockBits(bd);
        }

        /// <summary>Makes the recoloured texture and finds or adds its layer. Returns the layer index, or -1.</summary>
        private int PrepareEdgeLayer(object world, out string error)
        {
            error = null;
            object terr = Caw.Terrain(world);
            string src = ResolveEdgeSource(terr);
            if (src == null) { error = "No grass texture to recolour; choose one with 'Texture...'."; return -1; }
            string outDir = textureFolder ?? Path.Combine(Log.Dir, "Textures");
            string tex = EdgeBlend.MakeTexture(src, swDistant.BackColor, swGround.BackColor, hasGround, outDir);
            string name = Path.GetFileName(tex);
            int count = Caw.LayerCount(terr);
            for (int i = 1; i < count; i++) if (string.Equals(Caw.LayerName(terr, i), name, StringComparison.OrdinalIgnoreCase)) return i;
            int idx = Caw.AddLayer(world, tex, "Grass01", name);
            if (idx < 0) error = "CAW could not add the edge layer from " + name + ".";
            return idx;
        }

        // ---------------- styles ----------------

        /// <summary>
        /// The selected style's own textures (an EA world's paint layers): for each type the biggest one found in your
        /// layers or SourceTextures folder is selected, the others become variety layers; the style's base texture too.
        /// Textures you don't have are listed; those types keep their current choice (or use Smart pick).
        /// </summary>
        private void PickFromStyle()
        {
            extras.Clear(); mainShare.Clear(); basePick = null;
            StyleProfile p = cbStyle.SelectedItem as StyleProfile;
            object world = Caw.CurrentWorld;
            if (p == null || world == null) { UpdateVarietyLabel(); return; }
            try
            {
                object terr = Caw.Terrain(world);
                int count = Caw.LayerCount(terr);
                Dictionary<string, int> layerByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 1; i < count; i++) layerByName[Path.GetFileNameWithoutExtension(Caw.LayerName(terr, i))] = i;
                Dictionary<string, string> fileByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string t in textureFiles)
                {
                    string bn = Path.GetFileNameWithoutExtension(t);
                    fileByName[bn] = t;
                    // (some worlds name the same texture with "_IMG" on the end: 05_DXT1_TP_grassPasture_IMG)
                    if (bn.EndsWith("_IMG", StringComparison.OrdinalIgnoreCase) && !fileByName.ContainsKey(bn.Substring(0, bn.Length - 4))) fileByName[bn.Substring(0, bn.Length - 4)] = t;
                }
                int found = 0, total = 0;
                List<string> missing = new List<string>();
                foreach (PaintRole r in roleCombos.Keys)
                {
                    RoleStyle rs;
                    if (!p.Roles.TryGetValue(r, out rs) || rs.Textures.Count == 0) continue;
                    List<TexPick> list = new List<TexPick>();
                    for (int k = 0; k < rs.Textures.Count; k++)
                    {
                        total++;
                        TexPick tp = Find(rs.Textures[k], layerByName, fileByName);
                        if (tp == null) { missing.Add(rs.Textures[k]); continue; }
                        tp.Share = rs.Shares.Count == rs.Textures.Count ? rs.Shares[k] : 1f / rs.Textures.Count;
                        list.Add(tp); found++;
                    }
                    if (list.Count == 0) continue;
                    list.Sort(delegate (TexPick a, TexPick b) { return b.Share.CompareTo(a.Share); });
                    // the biggest one is the type's main layer
                    ComboBox cb = roleCombos[r];
                    for (int i = 0; i < cb.Items.Count; i++)
                    {
                        LayerItem it = (LayerItem)cb.Items[i];
                        if (list[0].Index > 0 ? (!it.IsNew && it.Index == list[0].Index) : (it.IsNew && it.TexturePath == list[0].Path)) { cb.SelectedIndex = i; break; }
                    }
                    mainShare[r] = list[0].Share;
                    list.RemoveAt(0);
                    extras[r] = list;
                }
                if (p.BaseTexture.Length > 0 && !string.Equals(Path.GetFileNameWithoutExtension(Caw.LayerName(terr, 0)), p.BaseTexture, StringComparison.OrdinalIgnoreCase))
                {
                    basePick = Find(p.BaseTexture, layerByName, fileByName);
                    total++;
                    if (basePick != null) found++; else missing.Add(p.BaseTexture);
                }
                Log.Info("Style textures (" + p.Name + "): found " + found + " of " + total + (missing.Count > 0 ? "; missing " + string.Join(", ", missing.ToArray()) : ""));
                if (total > 0)
                    Status(p.Name + "'s own textures: " + found + " of " + total + " found in your layers and SourceTextures" +
                           (missing.Count > 0 ? " (not found: " + string.Join(", ", missing.ToArray()) + "; those types keep the current choice, or use Smart pick)" : "") + ".");
            }
            catch (Exception ex) { Log.Error("Picking the style's textures failed", ex); }
            UpdateVarietyLabel();
        }

        /// <summary>
        /// "Get world textures...": every paint texture in the world files under a chosen folder, then the built-in styles'
        /// textures still missing from the game files CAW has loaded, into SourceTextures\CAWPlus EA textures.
        /// </summary>
        private void GetWorldTextures()
        {
            if (busy || Injector.BusyElsewhere(this)) return;
            string folder = textureFolder ?? Caw.TerrainTexturesPath();
            if (folder == null || !Directory.Exists(folder)) { Status("Your SourceTextures folder wasn't found (see the log), so there's nowhere to put the textures."); return; }
            string outRoot = Path.Combine(folder, WorldTextures.FolderName);
            HashSet<string> have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string t in TextureLibrary.Scan(folder))
            {
                string bn = Path.GetFileNameWithoutExtension(t);
                have.Add(bn);
                if (bn.EndsWith("_IMG", StringComparison.OrdinalIgnoreCase)) have.Add(bn.Substring(0, bn.Length - 4));
            }
            // the textures the built-in (and your) styles paint with
            List<string> wanted = new List<string>();
            foreach (object o in cbStyle.Items)
            {
                StyleProfile p = o as StyleProfile;
                if (p == null) continue;
                foreach (RoleStyle rs in p.Roles.Values) wanted.AddRange(rs.Textures);
                if (p.BaseTexture.Length > 0) wanted.Add(p.BaseTexture);
            }

            string worlds = null;
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "Choose a folder with world files (.world): the EA worlds' CAW files, or CAW's own Worlds folder. " +
                                "Subfolders are searched too. Cancel to look in the game files only.";
                string def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), @"Electronic Arts\The Sims 3 Create A World Tool\UserToolData\Worlds");
                if (Directory.Exists(def)) d.SelectedPath = def;
                if (d.ShowDialog(this) == DialogResult.OK) worlds = d.SelectedPath;
            }

            SetBusy(true);
            try
            {
                WorldTextures.Result wr = null;
                if (worlds != null)
                {
                    Status("Reading world files...");
                    wr = (WorldTextures.Result)RunInBackground(delegate () { return WorldTextures.FromWorldFiles(worlds, outRoot, have, BgProgress); });
                    Log.Info("World textures from " + worlds + ": " + wr.Worlds + " world file(s), " + wr.Written + " texture(s) copied, " + wr.Present +
                             " already there" + (wr.Missing.Count > 0 ? "; not held in the files: " + string.Join(", ", wr.Missing.ToArray()) : ""));
                    foreach (string f in wr.Failed) Log.Info("  could not read " + f);
                }
                Status("Looking in the game files...");
                Application.DoEvents();
                List<string> game = WorldTextures.FromGame(wanted, outRoot, have, DistantTerrain.Resource);
                Log.Info("World textures from the game files: " + game.Count + (game.Count > 0 ? " (" + string.Join(", ", game.ToArray()) + ")" : ""));
                List<string> still = new List<string>();
                foreach (string w in wanted) { string n = WorldTextures.CleanName(w); if (n.Length > 0 && !have.Contains(n) && !still.Contains(n)) still.Add(n); }
                still.Sort(StringComparer.OrdinalIgnoreCase);
                if (still.Count > 0) Log.Info("Style textures still missing: " + string.Join(", ", still.ToArray()));

                RefreshLayers();
                PickFromStyle();
                string msg = (wr != null ? "Copied " + wr.Written + " texture(s) from " + wr.Worlds + " world file(s)" : "No world files read") +
                             ", " + game.Count + " from the game files, into SourceTextures\\" + WorldTextures.FolderName + ". " +
                             (still.Count == 0 ? "Every built-in style's textures are there now." :
                              still.Count + " style texture(s) still missing (listed in the log).");
                Status(msg);
            }
            catch (Exception ex) { Log.Error("Getting world textures failed", ex); Status("Getting world textures failed: " + ex.Message); }
            finally { SetBusy(false); }
        }

        private static TexPick Find(string tex, Dictionary<string, int> layerByName, Dictionary<string, string> fileByName)
        {
            string key = Path.GetFileNameWithoutExtension(tex);
            int idx; string path;
            if (layerByName.TryGetValue(key, out idx)) return new TexPick { Index = idx, Name = key };
            if (fileByName.TryGetValue(key, out path)) return new TexPick { Path = path, Name = key };
            return null;
        }

        private void UpdateVarietyLabel()
        {
            if (lblVariety == null) return;
            int n = 0; List<string> names = new List<string>();
            foreach (List<TexPick> l in extras.Values) foreach (TexPick t in l) { n++; names.Add(t.Name); }
            string txt = "";
            if (n > 0) txt = (chkVariety.Checked ? "Variety: +" : "Not used: ") + n + " (" + string.Join(", ", names.ToArray()) + ")";
            else txt = "Variety: none (choose an EA style)";
            if (basePick != null) txt += (chkBaseTex.Checked ? "; base: " : "; base not used: ") + basePick.Name;
            lblVariety.Text = txt;
        }

        private void RefreshStyles(string select)
        {
            string keep = select ?? (cbStyle.SelectedItem is StyleProfile ? ((StyleProfile)cbStyle.SelectedItem).Name : null);
            cbStyle.Items.Clear();
            cbStyle.Items.Add("Built-in rules (CAW Plus)");
            int sel = 0;
            foreach (StyleProfile p in StyleProfile.LoadAll())
            {
                cbStyle.Items.Add(p);
                if (keep != null && string.Equals(p.Name, keep, StringComparison.OrdinalIgnoreCase)) sel = cbStyle.Items.Count - 1;
            }
            cbStyle.SelectedIndex = sel;
            previewGen = null;
        }

        private void LearnStyle()
        {
            if (busy || Injector.BusyElsewhere(this)) return;
            if (terrain == null && !Analyse()) return;
            object world = Caw.CurrentWorld;
            if (world == null) { Status("Open a world first."); return; }
            string name = Prompt("Learn style", "Name for this style (e.g. Sunset Valley):", Caw.WorldName(world));
            if (string.IsNullOrEmpty(name)) return;
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');

            SetBusy(true);
            try
            {
                object terr = Caw.Terrain(world);
                int count = Caw.LayerCount(terr);
                int f = Math.Max(1, terrain.MapW / 512);
                Grid h = TerrainSampler.Downsample(terrain.Heights, f);
                List<Grid> layers = new List<Grid>(); List<string> names = new List<string>(), types = new List<string>();
                string tmp = Path.Combine(TerrainSampler.TempDir, "learn_layer.png");
                for (int i = 0; i < count; i++)
                {
                    OnProgress("Reading layer " + i, 100 * i / Math.Max(1, count));
                    names.Add(Caw.LayerName(terr, i)); types.Add(Caw.LayerTerrainType(terr, i));
                    Grid g = null;
                    if (i > 0)
                    {
                        try
                        {
                            if (File.Exists(tmp)) File.Delete(tmp);
                            if (Caw.ExportLayer(world, i, tmp) && File.Exists(tmp))
                            {
                                Grid full = ImageIO.ReadPngChannel0(tmp);
                                if (full.W == terrain.MapW && full.H == terrain.MapH) g = TerrainSampler.Downsample(full, f);
                                else Log.Info("Layer " + i + " map is " + full.W + "x" + full.H + ", expected " + terrain.MapW + "x" + terrain.MapH + "; skipped");
                            }
                        }
                        catch (Exception ex) { Log.Error("Reading layer " + i + " for style failed", ex); }
                    }
                    layers.Add(g);
                    Application.DoEvents();
                }
                OnProgress("Learning", 95);
                StyleProfile p = StyleProfile.Learn(name, Caw.WorldName(world), h, terrain.CellMeters * f, terrain.WaterLevel,
                    terrain.WaterOffset != null ? TerrainSampler.Downsample(terrain.WaterOffset, f) : null, layers, names, types);
                float maxCover = 0;
                foreach (RoleStyle rs in p.Roles.Values) maxCover = Math.Max(maxCover, rs.Cover);
                if (maxCover < 0.005f)
                {
                    SetBusy(false);
                    MessageBox.Show(this, "This world's paint layers are (almost) empty, so there is no style to learn from.\n\n" +
                        "Learn from a finished world instead, e.g. a copy of an EA world with its original paint.",
                        "Learn style", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Status("Not saved: nothing painted to learn from.");
                    return;
                }
                string path = Path.Combine(StyleProfile.Dir, name + ".style");
                p.Save(path);
                string summary = "";
                foreach (KeyValuePair<PaintRole, RoleStyle> kv in p.Roles)
                {
                    summary += string.Format(" {0} {1:F0}%,", kv.Key, kv.Value.Cover * 100);
                    Log.Info(string.Format("Style {0}: {1} cover {2:P1}, patch {3:F0} m, soft {4:F2}, textures {5}", name, kv.Key,
                        kv.Value.Cover, kv.Value.PatchMeters, kv.Value.Softness, string.Join(", ", kv.Value.Textures.ToArray())));
                }
                SetBusy(false);
                RefreshStyles(name);
                Status(p.Roles.Count == 0
                    ? "Saved style '" + name + "', but no layer looked like rock, dirt, sand or grass. Check the layer names/types."
                    : "Learned style '" + name + "':" + summary.TrimEnd(',') + ". Saved to " + path);
            }
            catch (Exception ex)
            {
                Log.Error("Learning style failed", ex);
                SetBusy(false);
                Status("Learning style failed: " + ex.Message);
            }
        }

        private void SmartPick()
        {
            if (busy || Injector.BusyElsewhere(this)) return;
            object world = Caw.CurrentWorld;
            if (world == null) { Status("Open a world first."); return; }
            SetBusy(true);
            try
            {
                object terr = Caw.Terrain(world);
                int count = Caw.LayerCount(terr);
                List<string> textures = TextureLibrary.Scan(textureFolder);
                Dictionary<string, string> byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string t in textures) byName[Path.GetFileNameWithoutExtension(t)] = t;

                // Candidates: existing layers (1..n) and every texture in the folder.
                List<string> candName = new List<string>(), candPath = new List<string>(); List<int> candLayer = new List<int>();
                for (int i = 1; i < count; i++)
                {
                    string ln = Caw.LayerName(terr, i), file;
                    if (ln.StartsWith(EdgeBlend.LayerPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    byName.TryGetValue(Path.GetFileNameWithoutExtension(ln), out file);
                    candName.Add(ln + " " + Caw.LayerTerrainType(terr, i)); candPath.Add(file); candLayer.Add(i);
                }
                foreach (string t in textures) { candName.Add(Path.GetFileName(t)); candPath.Add(t); candLayer.Add(-1); }

                string baseFile;
                byName.TryGetValue(Path.GetFileNameWithoutExtension(Caw.LayerName(terr, 0)), out baseFile);
                TextureFeatures baseF = baseFile != null ? TextureAnalyzer.Analyze(baseFile) : null;
                StyleProfile style = cbStyle.SelectedItem as StyleProfile;
                // What EA's own worlds paint each kind of ground with (built-in styles from their .world files).
                Dictionary<PaintRole, List<string>> ea = new Dictionary<PaintRole, List<string>>();
                foreach (PaintRole r in PaintEngine.Roles) ea[r] = new List<string>();
                foreach (StyleProfile p in StyleProfile.LoadAll())
                    if (p.FromEA)
                        foreach (KeyValuePair<PaintRole, RoleStyle> kv in p.Roles) ea[kv.Key].AddRange(kv.Value.Textures);

                // Score every candidate for every role, then assign greedily, best match first, one texture per role.
                List<float[]> scores = new List<float[]>();
                for (int c = 0; c < candName.Count; c++)
                {
                    if (c % 10 == 0) OnProgress("Looking at textures", 100 * c / candName.Count);
                    TextureFeatures tf = candPath[c] != null ? TextureAnalyzer.Analyze(candPath[c]) : null;
                    float[] sc = new float[4];
                    foreach (PaintRole r in PaintEngine.Roles)
                    {
                        RoleStyle rs = style != null && style.Roles.ContainsKey(r) ? style.Roles[r] : null;
                        float v = TextureAnalyzer.Score(r, candName[c], tf, rs != null ? rs.Textures : null, baseF, ea[r]);
                        if (candLayer[c] > 0 && v > 0) v += 0.25f; // reuse an existing layer rather than add one
                        sc[(int)r] = v;
                    }
                    scores.Add(sc);
                }
                Dictionary<PaintRole, int> pick = new Dictionary<PaintRole, int>();
                List<int> used = new List<int>();
                while (pick.Count < 4)
                {
                    float best = 0.6f; int bc = -1; PaintRole br = PaintRole.Meadow;
                    for (int c = 0; c < scores.Count; c++)
                    {
                        if (used.Contains(c)) continue;
                        foreach (PaintRole r in PaintEngine.Roles)
                            if (!pick.ContainsKey(r) && scores[c][(int)r] > best) { best = scores[c][(int)r]; bc = c; br = r; }
                    }
                    if (bc < 0) break;
                    pick[br] = bc; used.Add(bc);
                    Log.Info(string.Format("Smart pick {0}: {1} (score {2:F2}; {3})", br, candName[bc], best,
                        TextureAnalyzer.Describe(candPath[bc] != null ? TextureAnalyzer.Analyze(candPath[bc]) : null)));
                }

                string summary = "";
                foreach (KeyValuePair<PaintRole, ComboBox> kv in roleCombos)
                {
                    ComboBox cb = kv.Value;
                    int sel = 0;
                    int c;
                    if (pick.TryGetValue(kv.Key, out c))
                        for (int i = 0; i < cb.Items.Count; i++)
                        {
                            LayerItem it = (LayerItem)cb.Items[i];
                            if (candLayer[c] > 0 ? (!it.IsNew && it.Index == candLayer[c]) : (it.IsNew && it.TexturePath == candPath[c])) { sel = i; break; }
                        }
                    cb.SelectedIndex = sel;
                    summary += " " + kv.Key + ": " + (sel == 0 ? "none suitable" : Path.GetFileName(candName[c].Split(' ')[0])) + ";";
                }
                SetBusy(false);
                Status("Smart pick:" + summary.TrimEnd(';') + ". Check them, then Read terrain / Paint world.");
            }
            catch (Exception ex)
            {
                Log.Error("Smart pick failed", ex);
                SetBusy(false);
                Status("Smart pick failed: " + ex.Message);
            }
        }

        private string Prompt(string title, string text, string value)
        {
            using (Form f = new Form())
            {
                f.Text = title; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = f.MaximizeBox = false; f.ShowInTaskbar = false; f.ClientSize = new Size(320, 96); f.Font = Font;
                Label l = new Label(); l.Text = text; l.SetBounds(10, 10, 300, 18);
                TextBox tb = new TextBox(); tb.Text = value; tb.SetBounds(10, 32, 300, 20);
                Button ok = new Button(); ok.Text = "OK"; ok.DialogResult = DialogResult.OK; ok.SetBounds(154, 62, 75, 24);
                Button cancel = new Button(); cancel.Text = "Cancel"; cancel.DialogResult = DialogResult.Cancel; cancel.SetBounds(235, 62, 75, 24);
                f.Controls.Add(l); f.Controls.Add(tb); f.Controls.Add(ok); f.Controls.Add(cancel);
                f.AcceptButton = ok; f.CancelButton = cancel;
                return Theme.Dialog(f, this) == DialogResult.OK ? tb.Text.Trim() : null;
            }
        }

        // ---------------- helpers ----------------

        private void OnProgress(string stage, int pct)
        {
            progress.Value = Math.Max(0, Math.Min(100, pct));
            status.Text = stage + "...";
            status.Refresh(); progress.Refresh();
        }

        private void Status(string s) { status.Text = s; }

        // ---------------- background work ----------------

        private delegate object Task();
        private volatile string bgStage;
        private volatile int bgPct = -1;

        private void BgProgress(string stage, int pct) { bgStage = stage; bgPct = pct; }

        /// <summary>Runs pure computation on a worker thread, keeping CAW responsive and showing its progress.</summary>
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

        private void SetBusy(bool b)
        {
            busy = b;
            Injector.SetBusy(this, b);
            btnAnalyse.Enabled = btnApply.Enabled = btnRestore.Enabled = btnLearn.Enabled = btnSmartPick.Enabled = btnWorldTex.Enabled = !b;
            UseWaitCursor = b;
            Cursor.Current = b ? Cursors.WaitCursor : Cursors.Default;
            if (!b) progress.Value = 0;
        }
    }
}
