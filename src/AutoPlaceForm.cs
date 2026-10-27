using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CawPlus
{
    /// <summary>The Auto-Place panel: choose trees / bushes / rocks (or a biome), preview density, place them.</summary>
    public sealed class AutoPlaceForm : Form
    {
        private bool woodsOnlyInForest;
        private CheckBox chkDetails, chkTownSigns, chkTrees, chkBushes, chkRocks, chkMatch, chkSigns, chkLights, chkOwnLayers, chkZones, chkCluster, chkWaterPlants, chkFalls;
        private List<WaterfallSite> fallSites = new List<WaterfallSite>(); // pond-step waterfall sites found on Read world (#158)
        private TrackBar tbLightSpacing, tbMax, tbEdge;
        private readonly List<List<float[]>> roadLines = new List<List<float[]>>(); // road centre lines, map coords
        private List<float[]> junctions = new List<float[]>();
        private ComboBox cbRef, cbShow, cbSide;
        private TextBox txtFilter;
        private Button btnLearn, btnLayers;
        private float landHa;
        private string lastPreviewKey;
        private CheckedListBox list;
        private TrackBar tbCover, tbDensity, tbBushes, tbRocks, tbRoad;
        private NumericUpDown nudSeed;
        private Button btnRead, btnPlace, btnRemove;
        private bool placing, stopPlacing; // Place becomes Stop while placing (the tester: stop an accidental Place)
        private PictureBox picture;
        private ProgressBar progress;
        private Label status, counts, lblLayers;
        private Timer previewTimer;

        private readonly List<PlaceItem> all = new List<PlaceItem>();
        private readonly List<PlaceItem> shown = new List<PlaceItem>();
        private TerrainInfo terrain;
        private PlanMap painted;         // the plan painted in Generate Roads (#171), or null
        private Grid work; private int factor = 1; private float workCell;
        private readonly List<float[]> roadPts = new List<float[]>();  // map coords
        private readonly List<float[]> footPts = new List<float[]>();  // footpath centre lines, map coords
        private readonly List<List<float[]>> bridgeLines = new List<List<float[]>>(); // road pieces on bridges, map coords
        private List<float[]> lots = new List<float[]>();             // map x, y, radius (map px)
        private readonly List<float[]> occupied = new List<float[]>(); // existing objects, map coords + radius (map px)
        private List<string> existingNames = new List<string>();
        private List<Caw.WorldObject> existing = new List<Caw.WorldObject>();
        private List<string> layerNames = new List<string>();
        private readonly Dictionary<string, string> layerFor = new Dictionary<string, string>(); // kind key -> World Layer
        private PlacementEngine.Maps maps;
        private Grid clearMap; // roads, lots, footpaths and the map rim (Blocked()), without water: waterfall lips sit by ponds
        private List<Placement> plan;
        private readonly List<object> lastPlaced = new List<object>();
        private bool busy, filling;

        // what the list can show (the kinds Auto-Place places; spawners have their own window)
        private static readonly string[] ShowNames = { "All ticked kinds", "Trees", "Rocks", "Road signs", "Streetlights", "Bushes", "Water plants" };

        private delegate string ValueText(int v);

        public AutoPlaceForm()
        {
            SuspendLayout();
            Text = "Auto-Place  (CAW Plus)";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(960, 884);
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 8.25f);
            int x = 10, y = 10, colW = 420;

            GroupBox gbWhat = new GroupBox(); gbWhat.Text = "What to place"; gbWhat.SetBounds(x, y, colW, 396);
            Label lr = new Label(); lr.Text = "World type"; lr.AutoSize = true; lr.Location = new Point(10, 22); gbWhat.Controls.Add(lr);
            cbRef = new ComboBox(); cbRef.DropDownStyle = ComboBoxStyle.DropDownList; cbRef.SetBounds(80, 18, 196, 20);
            cbRef.SelectedIndexChanged += delegate { ApplyReference(); RefreshSliderLabels(); };
            new ToolTip().SetToolTip(cbRef, "Each kind is picked from what this Sims 3 world uses (its trees, rocks, signs, streetlights),\n" +
                "weighted by how often it uses them. If this catalog lacks them (pack not installed), safe generic ones fill in.\n" +
                "'Generic' picks by the world's look. You can tick or untick anything afterwards.");
            gbWhat.Controls.Add(cbRef);
            chkMatch = new CheckBox(); chkMatch.Text = "Match its density"; chkMatch.Checked = true; chkMatch.AutoSize = true; chkMatch.Location = new Point(284, 20);
            chkMatch.CheckedChanged += delegate { RefreshSliderLabels(); SchedulePreview(); };
            gbWhat.Controls.Add(chkMatch);
            // on by default: trees, rocks, road signs; the rest only when asked for
            // laid out left to right at their own text width, so nothing is cut off at any font size
            int cx = 10;
            chkTrees = Check(gbWhat, "Trees", ref cx, 46, true);
            chkRocks = Check(gbWhat, "Rocks", ref cx, 46, true);
            chkSigns = Check(gbWhat, "Road signs", ref cx, 46, true);
            chkLights = Check(gbWhat, "Streetlights", ref cx, 46, false);
            chkBushes = Check(gbWhat, "Bushes", ref cx, 46, false);
            Label lsh = new Label(); lsh.Text = "Show"; lsh.AutoSize = true; lsh.Location = new Point(10, 74); gbWhat.Controls.Add(lsh);
            cbShow = new ComboBox(); cbShow.DropDownStyle = ComboBoxStyle.DropDownList; cbShow.SetBounds(52, 70, 130, 20);
            cbShow.Items.AddRange(ShowNames); cbShow.SelectedIndex = 0;
            cbShow.SelectedIndexChanged += delegate { FillList(); };
            gbWhat.Controls.Add(cbShow);
            Label lse = new Label(); lse.Text = "Search"; lse.AutoSize = true; lse.Location = new Point(192, 74); gbWhat.Controls.Add(lse);
            txtFilter = new TextBox(); txtFilter.SetBounds(244, 70, 166, 20);
            txtFilter.TextChanged += delegate { FillList(); };
            new ToolTip().SetToolTip(txtFilter, "Type part of a name (e.g. oak, boulder) to find items to tick or untick");
            gbWhat.Controls.Add(txtFilter);
            list = new CheckedListBox(); list.SetBounds(10, 96, 400, 188); list.HorizontalScrollbar = true; list.CheckOnClick = true; list.IntegralHeight = false;
            list.ItemCheck += delegate (object o, ItemCheckEventArgs e)
            {
                if (filling) return;
                shown[e.Index].Selected = e.NewValue == CheckState.Checked;
                SchedulePreview();
            };
            gbWhat.Controls.Add(list);
            Button bAll = new Button(); bAll.Text = "Default picks"; bAll.SetBounds(10, 290, 96, 24); bAll.Click += delegate { ApplyReference(); };
            Button bTick = new Button(); bTick.Text = "Tick shown"; bTick.SetBounds(112, 290, 96, 24);
            bTick.Click += delegate { foreach (PlaceItem p in shown) p.Selected = true; FillList(); SchedulePreview(); };
            Button bUntick = new Button(); bUntick.Text = "Untick shown"; bUntick.SetBounds(214, 290, 96, 24);
            bUntick.Click += delegate { foreach (PlaceItem p in shown) p.Selected = false; FillList(); SchedulePreview(); };
            Button bNone = new Button(); bNone.Text = "Clear all"; bNone.SetBounds(316, 290, 94, 24);
            bNone.Click += delegate { foreach (PlaceItem p in all) p.Selected = false; FillList(); SchedulePreview(); };
            gbWhat.Controls.Add(bAll); gbWhat.Controls.Add(bTick); gbWhat.Controls.Add(bUntick); gbWhat.Controls.Add(bNone);
            chkZones = new CheckBox(); chkZones.Text = "Mountain tree zones"; chkZones.Checked = true; chkZones.AutoSize = true; chkZones.Location = new Point(10, 324);
            chkZones.CheckedChanged += delegate { SchedulePreview(); };
            new ToolTip().SetToolTip(chkZones, "Fewer trees higher up, thinning to a tree line near the tops,\n" +
                "and spruce, pine and fir taking over from leafy trees as the ground rises.");
            gbWhat.Controls.Add(chkZones);
            chkWaterPlants = new CheckBox(); chkWaterPlants.Text = "Water plants by rivers and ponds"; chkWaterPlants.Checked = true; chkWaterPlants.AutoSize = true; chkWaterPlants.Location = new Point(176, 324);
            chkWaterPlants.CheckedChanged += delegate { FillList(); SchedulePreview(); };
            new ToolTip().SetToolTip(chkWaterPlants, "Reeds, cattails and lilies (ticked under 'Water plants') in clumps along river and pond banks.\n" +
                "Not along the open sea.");
            gbWhat.Controls.Add(chkWaterPlants);
            chkDetails = new CheckBox(); chkDetails.Text = "Street details"; chkDetails.Checked = false; chkDetails.AutoSize = true; chkDetails.Location = new Point(240, 346);
            chkDetails.CheckedChanged += delegate { SchedulePreview(); };
            new ToolTip().SetToolTip(chkDetails, "Base-game road drains, manholes, fire hydrants and split-rail fences, placed as EA's worlds place them:\n" +
                "drains at each kerb where roads meet in town, manholes in the lane and hydrants on the verge along roads lined with lots,\n" +
                "fences in short runs beside country roads and out into the fields behind lots. They go in the 'Street Details' World Layer.");
            gbWhat.Controls.Add(chkDetails);
            chkTownSigns = new CheckBox(); chkTownSigns.Text = "Town signs and billboards"; chkTownSigns.Checked = false; chkTownSigns.AutoSize = true; chkTownSigns.Location = new Point(240, 368);
            chkTownSigns.CheckedChanged += delegate { SchedulePreview(); };
            new ToolTip().SetToolTip(chkTownSigns, "Base-game welcome signs, neighbourhood signs and billboards, placed as EA's worlds place them:\n" +
                "welcome signs on the roads into town just past the last lots, facing incoming traffic; neighbourhood signs ~38 m in from a junction\n" +
                "on the road into a group of houses; billboards 9-16 m off the roads round the town, never on a lot. They go in 'Street Details'.");
            gbWhat.Controls.Add(chkTownSigns);
            chkFalls = new CheckBox(); chkFalls.Text = "Waterfalls at pond steps (test)"; chkFalls.Checked = false; chkFalls.AutoSize = true; chkFalls.Location = new Point(10, 346);
            chkFalls.CheckedChanged += delegate { SchedulePreview(); };
            new ToolTip().SetToolTip(chkFalls, "EA's waterfall effects where a raised pond spills over a cliff (a fall at the lip and a splash below).\n" +
                "Only this kind: falls on sea cliffs or dry cliffs aren't found. At most as many as the World type has\n" +
                "(none for worlds without waterfalls). Checked offline on EA's own worlds: every site it found was a real\n" +
                "EA waterfall, but it finds only about 1 in 7 of them. Not yet tried in CAW: use a copy of the world.");
            gbWhat.Controls.Add(chkFalls);
            Controls.Add(gbWhat);
            y += 402;

            GroupBox gbHow = new GroupBox(); gbHow.Text = "Density"; gbHow.SetBounds(x, y, colW, 300);
            int ty = 18;
            tbCover = Slider(gbHow, "Woodland cover", 45, ref ty, delegate (int v) { return v + "% of the land" + Times(PlaceKind.Tree, v, -1); });
            tbDensity = Slider(gbHow, "Tree density", 40, ref ty, delegate (int v) { return Matching() ? Times(PlaceKind.Tree, -1, v).Trim(' ', '(', ')') : TreesPerHa(v); });
            tbBushes = Slider(gbHow, "Bushes", 40, ref ty, delegate (int v) { return Matching() ? Times(PlaceKind.Bush, -1, v).Trim(' ', '(', ')') : v + "%"; });
            tbRocks = Slider(gbHow, "Rocks", 30, ref ty, delegate (int v) { return Matching() ? Times(PlaceKind.Rock, -1, v).Trim(' ', '(', ')') : v + "%"; });
            tbLightSpacing = Slider(gbHow, "Streetlight spacing", 32, ref ty, delegate (int v) { return "every " + v + " m (" + (1000 / Math.Max(1, v)) + "/km)"; });
            tbLightSpacing.Minimum = 12; tbLightSpacing.Maximum = 80;
            tbRoad = Slider(gbHow, "Road clearance", 4, ref ty, delegate (int v) { return (6 + v) + " m from centre"; });
            tbRoad.Maximum = 20;
            tbMax = Slider(gbHow, "Max objects (world)", 100, ref ty, delegate (int v) { return (v * 100) + " in all"; });
            tbMax.Minimum = 10; tbMax.Maximum = 200;
            new ToolTip().SetToolTip(tbMax, "The most objects the whole world should have, those already placed included.\n" +
                "EA worlds hold 1,000-14,000 trees, kept as cluster trees (leave Cluster trees ticked). Placing takes about 12 ms per object.\n" +
                "Trees, bushes and rocks are thinned evenly to fit.");
            tbEdge = Slider(gbHow, "Edge border", 60, ref ty, delegate (int v) { return v == 0 ? "none" : v + " m kept clear"; });
            tbEdge.Maximum = 200;
            new ToolTip().SetToolTip(tbEdge, "Nothing is placed this close to the map edge (the distant terrain covers it).");
            Label ls = new Label(); ls.Text = "Seed"; ls.SetBounds(10, ty + 4, 34, 16); gbHow.Controls.Add(ls);
            nudSeed = new NumericUpDown(); nudSeed.Minimum = 0; nudSeed.Maximum = 999999; nudSeed.Value = 7; nudSeed.SetBounds(46, ty, 70, 20);
            nudSeed.ValueChanged += delegate { SchedulePreview(); };
            gbHow.Controls.Add(nudSeed);
            Button bNew = new Button(); bNew.Text = "New"; bNew.SetBounds(120, ty - 1, 44, 23);
            bNew.Click += delegate { nudSeed.Value = new Random().Next(0, 999999); };
            gbHow.Controls.Add(bNew);
            Label lsd = new Label(); lsd.Text = "Signs on the"; lsd.AutoSize = true; lsd.Location = new Point(184, ty + 4); gbHow.Controls.Add(lsd);
            cbSide = new ComboBox(); cbSide.DropDownStyle = ComboBoxStyle.DropDownList; cbSide.SetBounds(270, ty, 140, 20);
            cbSide.Items.AddRange(new object[] { "right of the road", "left of the road" }); cbSide.SelectedIndex = 0;
            cbSide.SelectedIndexChanged += delegate { SchedulePreview(); };
            new ToolTip().SetToolTip(cbSide, "Sims drive on the right, so signs stand on the right-hand verge facing the traffic.\n" +
                "If they come out on the left in CAW, choose 'left' (and please tell us).");
            gbHow.Controls.Add(cbSide);
            Controls.Add(gbHow);
            y += 306;

            chkOwnLayers = new CheckBox(); chkOwnLayers.Text = "Sort into World Layers"; chkOwnLayers.Checked = true; chkOwnLayers.AutoSize = true; chkOwnLayers.Location = new Point(x, y + 2);
            new ToolTip().SetToolTip(chkOwnLayers, "Each kind goes into a World Layer: your own layers are used when their name or contents fit\n" +
                "(e.g. Trees, Road Misc, River and Sea, World Misc), else one is made. 'Layers...' shows and changes the choice.");
            Controls.Add(chkOwnLayers);
            btnLayers = new Button(); btnLayers.Text = "Layers..."; btnLayers.SetBounds(x + 170, y - 1, 80, 24);
            btnLayers.Click += delegate { EditLayers(); };
            Controls.Add(btnLayers);
            chkCluster = new CheckBox(); chkCluster.Text = "Cluster trees after placing"; chkCluster.Checked = true; chkCluster.AutoSize = true; chkCluster.Location = new Point(x + 262, y + 2);
            new ToolTip().SetToolTip(chkCluster, "Recommended: runs CAW's own Cluster Trees after placing, which merges trees into clusters\n" +
                "that draw much faster (CAW also does this when saving).");
            Controls.Add(chkCluster);
            y += 26;
            lblLayers = new Label(); lblLayers.SetBounds(x, y, colW, 30); lblLayers.ForeColor = Color.DimGray; Controls.Add(lblLayers);
            y += 32;

            btnRead = new Button(); btnRead.Text = "Read world"; btnRead.SetBounds(x, y, 110, 28); btnRead.Click += delegate { ReadWorld(); };
            btnPlace = new Button(); btnPlace.Text = "Place"; btnPlace.SetBounds(x + 115, y, 110, 28); btnPlace.Font = new Font(Font, FontStyle.Bold);
            btnPlace.Click += delegate { if (placing) stopPlacing = true; else PlaceAll(); };
            btnRemove = new Button(); btnRemove.Text = "Remove last"; btnRemove.SetBounds(x + 230, y, 110, 28); btnRemove.Click += delegate { RemoveLast(); };
            Controls.Add(btnRead); Controls.Add(btnPlace); Controls.Add(btnRemove);
            y += 32;
            btnLearn = new Button(); btnLearn.Text = "Learn from this world..."; btnLearn.SetBounds(x, y, 340, 26);
            btnLearn.Click += delegate { LearnObjects(); };
            new ToolTip().SetToolTip(btnLearn, "Open a reference world (e.g. a copy of Sunset Valley), Read world, then click this.\n" +
                "It records which trees, bushes and rocks it uses, how many per hectare, and on what ground.");
            Controls.Add(btnLearn);
            y += 28;
            progress = new ThemedProgress(); progress.SetBounds(x, y, colW, 12); Controls.Add(progress);
            y += 14;
            status = new Label(); status.SetBounds(x, y, colW, 30); status.Text = "Click 'Read world' to start."; Controls.Add(status);

            picture = new PictureBox(); picture.SetBounds(x + colW + 18, 10, 512, 512);
            picture.SizeMode = PictureBoxSizeMode.Zoom; picture.BackColor = Color.FromArgb(40, 40, 40); picture.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(picture);
            counts = new Label(); counts.SetBounds(x + colW + 18, 528, 512, 160);
            counts.Text = "Preview: dark green = trees, light green = bushes, grey = rocks, white = streetlights, red = signs, red area = kept clear.";
            Controls.Add(counts);

            RefreshLibraries(WorldPreset.Inspiration ?? "Sunset Valley"); // the EA world Generate Terrain modelled this map on, else EA's standard set
            previewTimer = new Timer(); previewTimer.Interval = 250;
            previewTimer.Tick += delegate { previewTimer.Stop(); UpdatePreview(); };
            // scale with the screen's DPI (text was cut off on some displays)
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            Theme.Apply(this);
        }

        /// <summary>Whether the sliders scale the World type's own densities (Match its density, with a World type).</summary>
        private bool Matching() { return chkMatch != null && chkMatch.Checked && cbRef != null && cbRef.SelectedItem is ObjectLibrary; }

        /// <summary>" (x1.0 the World type's trees)": the density the sliders now ask for, against the World type's own.</summary>
        private string Times(PlaceKind kind, int cover, int value)
        {
            if (!Matching()) return "";
            PlaceSettings t = new PlaceSettings();
            t.ForestCover = cover >= 0 ? cover : (tbCover != null ? tbCover.Value : 45);
            t.TreeDensity = kind == PlaceKind.Tree && value >= 0 ? value : (tbDensity != null ? tbDensity.Value : 40);
            t.Bushes = kind == PlaceKind.Bush && value >= 0 ? value : (tbBushes != null ? tbBushes.Value : 40);
            t.Rocks = kind == PlaceKind.Rock && value >= 0 ? value : (tbRocks != null ? tbRocks.Value : 30);
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, " (x{0:0.0} the world type's {1})", PlacementEngine.SliderFactor(kind, t),
                kind == PlaceKind.Tree ? "trees" : kind == PlaceKind.Bush ? "bushes" : "rocks");
        }

        private readonly List<KeyValuePair<TrackBar, Label>> sliderLabels = new List<KeyValuePair<TrackBar, Label>>();
        private readonly List<ValueText> sliderTexts = new List<ValueText>();

        /// <summary>Re-reads every slider's value text (after the World type or Match its density changes).</summary>
        private void RefreshSliderLabels()
        {
            for (int i = 0; i < sliderLabels.Count; i++) sliderLabels[i].Value.Text = sliderTexts[i](sliderLabels[i].Key.Value);
        }

        /// <summary>Roughly how many trees per hectare of woodland a density value gives (9 m grid, fill 5-80%).</summary>
        private static string TreesPerHa(int v)
        {
            float fill = 0.05f + 0.75f * v / 100f;
            return string.Format("~{0:F0} trees/ha in woods", fill * 10000f / 81f * 0.55f);
        }

        private CheckBox Check(Control parent, string text, ref int x, int y, bool on)
        {
            CheckBox c = new CheckBox(); c.Text = text; c.Checked = on; c.AutoSize = true; c.Location = new Point(x, y);
            c.CheckedChanged += delegate { FillList(); SchedulePreview(); };
            parent.Controls.Add(c);
            x += c.PreferredSize.Width + 8;
            return c;
        }

        private TrackBar Slider(GroupBox gb, string label, int value, ref int ty, ValueText text)
        {
            Label l = new Label(); l.Text = label; l.SetBounds(10, ty + 6, 128, 18); gb.Controls.Add(l);
            TrackBar tb = new ThemedSlider(); tb.AutoSize = false; tb.Minimum = 0; tb.Maximum = 100; tb.Value = value; tb.TickFrequency = 10;
            tb.SetBounds(138, ty, 126, 28);
            Label val = new Label(); val.SetBounds(266, ty + 6, 150, 18); val.ForeColor = Color.DimGray; gb.Controls.Add(val);
            tb.ValueChanged += delegate { val.Text = text(tb.Value); if (tb == tbCover || tb == tbDensity) RefreshSliderLabels(); SchedulePreview(); };
            // letting go of a slider redraws straight away (the tester: the picture only changed after a new seed)
            tb.MouseUp += delegate { if (work != null && !busy) { previewTimer.Stop(); UpdatePreview(); } };
            val.Text = text(value);
            sliderLabels.Add(new KeyValuePair<TrackBar, Label>(tb, val)); sliderTexts.Add(text);
            gb.Controls.Add(tb);
            ty += 30;
            return tb;
        }

        private static string BiomeLabel(Biome b)
        {
            switch (b)
            {
                case Biome.MixedForest: return "Mixed forest";
                case Biome.Boreal: return "Boreal / alpine";
                default: return b.ToString();
            }
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
                for (int i = 0; i < work.D.Length; i++) if (work.D[i] >= terrain.WaterLevel) landCells++; // ponds are small; sea level is what matters here
                landHa = landCells * workCell * workCell / 10000f;
                maps = null; plan = null; clearMap = null; // the old world's maps don't fit the new work grid
                string wdir = Caw.WorldDir(Path.Combine(Log.Dir, "Worlds"), world);
                painted = PlanMap.Load(Path.Combine(wdir, "plan.png"));
                // a plan from the World Builder: its forest squares are where the woods go (#173), while the world still
                // has the land the World Builder made
                bool sketched = File.Exists(Path.Combine(wdir, "sketch.txt")), built = sketched && WorldSketch.AppliedTo(wdir, terrain.Heights);
                if (sketched && !built) painted = null;
                woodsOnlyInForest = painted != null && painted.Has(PlanArea.Forest) && built;
                if (painted != null) Log.Info("Auto-Place: using the painted plan (forest, parks, keep-clear)" + (woodsOnlyInForest ? ", woods only in the World Builder's forest" : ""));

                OnProgress("Reading roads and lots", 96);
                roadPts.Clear(); roadLines.Clear();
                List<float[]> pathSplines, bridgeSplines;
                List<float[]> splines = Caw.RoadSplines(world, out pathSplines, out bridgeSplines);
                foreach (float[] k in splines) SampleRoad(k);
                footPts.Clear(); bridgeLines.Clear();
                int nr = roadPts.Count, nl = roadLines.Count;
                foreach (float[] k in pathSplines) SampleRoad(k);
                footPts.AddRange(roadPts.GetRange(nr, roadPts.Count - nr)); roadPts.RemoveRange(nr, roadPts.Count - nr); roadLines.RemoveRange(nl, roadLines.Count - nl);
                foreach (float[] k in bridgeSplines) { SampleRoad(k); bridgeLines.Add(roadLines[roadLines.Count - 1]); roadLines.RemoveAt(roadLines.Count - 1); }
                roadPts.RemoveRange(nr, roadPts.Count - nr);
                junctions = FindJunctions(splines);
                lots = new List<float[]>();
                foreach (float[] c in Caw.LotCircles())
                {
                    float mx, my; TerrainSampler.WorldToMap(terrain, c[0], c[1], out mx, out my);
                    lots.Add(new[] { mx, my, c[2] / terrain.CellMeters });
                }
                occupied.Clear(); existingNames = new List<string>();
                existing = Caw.WorldObjects();
                foreach (Caw.WorldObject o in existing)
                {
                    existingNames.Add(o.Name);
                    float mx, my; TerrainSampler.WorldToMap(terrain, o.X, o.Z, out mx, out my);
                    // keep clear of what is there: its footprint (trees: the trunk area, canopies may touch)
                    float r = o.Tree ? 2.5f : Math.Max(1.5f, Math.Min(20f, o.Radius * 0.8f) + 1f);
                    occupied.Add(new[] { mx, my, r / terrain.CellMeters });
                }
                layerNames = Caw.LayerNames();
                fallSites = FindFalls(); // after 'existing': skips falls the world already has

                OnProgress("Reading the object catalog", 98);
                all.Clear();
                // EA's own objects by instance first (signs, streetlights, spawners, rocks, trees in the bundled worlds)
                PlacementEngine.LoadKnown();
                foreach (Caw.CatalogItem c in Caw.Catalog()) all.Add(PlacementEngine.Classify(c));
                GuessLayers();
                try
                {
                    string dump = System.IO.Path.Combine(Log.Dir, "catalog.csv");
                    PlacementEngine.DumpCatalog(all, dump);
                    Log.Info("Catalog with Auto-Place's kinds written to " + dump);
                }
                catch (Exception ex) { Log.Error("Writing catalog.csv failed", ex); }
                int trees = 0, bushes = 0, rocks = 0;
                foreach (PlaceItem p in all) { if (p.Kind == PlaceKind.Tree) trees++; else if (p.Kind == PlaceKind.Bush) bushes++; else if (p.Kind == PlaceKind.Rock) rocks++; }
                Log.Info(string.Format("Auto-Place: catalog {0} items ({1} trees, {2} bushes, {3} rocks), {4} road splines, {5} lots, {6} existing objects",
                    all.Count, trees, bushes, rocks, splines.Count, lots.Count, occupied.Count));
                SetBusy(false);
                ApplyReference();
                Status(string.Format("{0} trees, {1} bushes, {2} rocks in the catalog; {3} lots, {4} objects already placed.",
                    trees, bushes, rocks, lots.Count, occupied.Count));
            }
            catch (Exception ex)
            {
                Log.Error("Auto-Place read failed", ex);
                SetBusy(false);
                Status("Reading the world failed: " + ex.Message);
            }
        }

        /// <summary>Points along a road (both Bezier and Catmull-Rom readings of its 4 knots, to be safe), in map coords.</summary>
        private void SampleRoad(float[] k)
        {
            if (k.Length < 8) return;
            float len = 0;
            for (int i = 0; i < 3; i++) len += (float)Math.Sqrt(Sq(k[i * 2 + 2] - k[i * 2]) + Sq(k[i * 2 + 3] - k[i * 2 + 1]));
            int n = Math.Max(4, (int)(len / 2));
            List<float[]> line = new List<float[]>();
            for (int j = 0; j <= n; j++)
            {
                float t = (float)j / n, u = 1 - t;
                // cubic Bezier k0..k3
                float bx = u * u * u * k[0] + 3 * u * u * t * k[2] + 3 * u * t * t * k[4] + t * t * t * k[6];
                float bz = u * u * u * k[1] + 3 * u * u * t * k[3] + 3 * u * t * t * k[5] + t * t * t * k[7];
                Add(bx, bz);
                line.Add(roadPts[roadPts.Count - 1]);
                // Catmull-Rom segment k1..k2
                float t2 = t * t, t3 = t2 * t;
                float cx = 0.5f * (2 * k[2] + (-k[0] + k[4]) * t + (2 * k[0] - 5 * k[2] + 4 * k[4] - k[6]) * t2 + (-k[0] + 3 * k[2] - 3 * k[4] + k[6]) * t3);
                float cz = 0.5f * (2 * k[3] + (-k[1] + k[5]) * t + (2 * k[1] - 5 * k[3] + 4 * k[5] - k[7]) * t2 + (-k[1] + 3 * k[3] - 3 * k[5] + k[7]) * t3);
                Add(cx, cz);
            }
            roadLines.Add(line);
        }

        /// <summary>Where 3+ road ends meet within 15 m (around an intersection): sign spots, in map coords.</summary>
        private List<float[]> FindJunctions(List<float[]> splines)
        {
            List<float[]> ends = new List<float[]>();
            foreach (float[] k in splines)
            {
                if (k.Length < 8) continue;
                float mx, my;
                TerrainSampler.WorldToMap(terrain, k[0], k[1], out mx, out my); ends.Add(new[] { mx, my });
                TerrainSampler.WorldToMap(terrain, k[6], k[7], out mx, out my); ends.Add(new[] { mx, my });
            }
            float r = 15f / terrain.CellMeters;
            List<float[]> js = new List<float[]>();
            bool[] used = new bool[ends.Count];
            for (int i = 0; i < ends.Count; i++)
            {
                if (used[i]) continue;
                float sx = 0, sy = 0; int n = 0;
                for (int j = i; j < ends.Count; j++)
                    if (!used[j] && Sq(ends[j][0] - ends[i][0]) + Sq(ends[j][1] - ends[i][1]) < r * r) { used[j] = true; sx += ends[j][0]; sy += ends[j][1]; n++; }
                if (n >= 3) js.Add(new[] { sx / n, sy / n });
            }
            return js;
        }

        private void Add(float x, float z)
        {
            float mx, my; TerrainSampler.WorldToMap(terrain, x, z, out mx, out my);
            roadPts.Add(new[] { mx, my });
        }

        private static float Sq(float v) { return v * v; }

        /// <summary>Roads (6 m half-width + clearance) and lots (+3 m) rasterised on the working grid.</summary>
        private Grid Blocked()
        {
            Grid b = new Grid(work.W, work.H);
            float roadR = (6f + tbRoad.Value) / workCell;
            // the edge border: the distant terrain covers the map's rim, so nothing goes there
            int rim = (int)Math.Ceiling(tbEdge.Value / workCell);
            for (int yy = 0; yy < b.H; yy++)
                for (int xx = 0; xx < b.W; xx++)
                    if (xx < rim || yy < rim || xx >= b.W - rim || yy >= b.H - rim) b.D[yy * b.W + xx] = 1;
            foreach (float[] p in roadPts) Disc(b, p[0] / factor, p[1] / factor, roadR);
            foreach (float[] l in lots) Disc(b, l[0] / factor, l[1] / factor, l[2] / factor + 3f / workCell);
            // footpaths: EA keeps trees and rocks off them as off roads, 5 m from the centre line (5th percentile of
            // the distance, 12 worlds with footpaths: trees 2-10 m, median 5; rocks 4-7 m)
            float footR = 5f / workCell;
            foreach (float[] p in footPts) Disc(b, p[0] / factor, p[1] / factor, footR);
            return b;
        }

        private static void Disc(Grid g, float cx, float cy, float r)
        {
            int x0 = Math.Max(0, (int)(cx - r)), x1 = Math.Min(g.W - 1, (int)(cx + r)), y0 = Math.Max(0, (int)(cy - r)), y1 = Math.Min(g.H - 1, (int)(cy + r));
            float r2 = r * r;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (Sq(x + 0.5f - cx) + Sq(y + 0.5f - cy) <= r2) g.D[y * g.W + x] = 1;
        }

        // ---------------- reference worlds ----------------

        private void RefreshLibraries(string select)
        {
            cbRef.Items.Clear();
            cbRef.Items.Add("Generic (by tree names)");
            int sel = 0;
            foreach (ObjectLibrary l in ObjectLibrary.LoadAll())
            {
                cbRef.Items.Add(l);
                if (select != null && string.Equals(l.Name, select, StringComparison.OrdinalIgnoreCase)) sel = cbRef.Items.Count - 1;
            }
            cbRef.SelectedIndex = sel;
        }

        // the kinds Auto-Place places, and the fewest of each a pool should have before generic ones fill in
        private static readonly PlaceKind[] PlacedKinds = { PlaceKind.Tree, PlaceKind.Rock, PlaceKind.Sign, PlaceKind.Streetlight, PlaceKind.Bush };
        private static int MinPool(PlaceKind k) { return k == PlaceKind.Tree ? 3 : k == PlaceKind.Rock ? 2 : 1; }

        /// <summary>
        /// Picks each kind's pool from the 'Like world' (what that world uses, weighted by count). A kind it has too few
        /// of in this catalog is filled with safe generic items: SpeedTrees for the world's look, natural rocks,
        /// EA's own signs and lights. Buy-mode and garden plants are never picked; they can still be ticked by hand.
        /// </summary>
        private void ApplyReference()
        {
            try { ApplyReferenceCore(); }
            catch (Exception ex) { Log.Error("World type failed", ex); Status("World type failed: " + ex.Message); }
        }

        private void ApplyReferenceCore()
        {
            ObjectLibrary lib = cbRef.SelectedItem as ObjectLibrary;
            foreach (PlaceItem p in all)
            {
                PlaceItem fresh = PlacementEngine.Classify(p.Catalog);
                p.Ref = null; p.Weight = 1; p.Kind = fresh.Kind; p.Indoor = fresh.Indoor; p.BuyPlant = fresh.BuyPlant; p.WaterPlant = fresh.WaterPlant;
                p.Selected = false;
            }
            if (all.Count == 0) return;
            Biome biome = Biome.MixedForest; string why = "default";
            if (lib == null) biome = DetectBiomeHere(out why);
            int matched = 0;
            if (lib != null)
                foreach (PlaceItem p in all)
                {
                    p.Ref = lib.Find(p.Catalog.Name, p.Catalog.InstanceHex);
                    if (p.Ref == null) continue;
                    p.Kind = p.Ref.Kind; // the reference world says what it is (Other = not something Auto-Place puts down)
                    p.Weight = p.Ref.Count;
                    p.Selected = Array.IndexOf(PlacedKinds, p.Kind) >= 0 && !p.Indoor;
                    if (p.Selected) matched++;
                }
            List<string> filled = new List<string>();
            foreach (PlaceKind k in PlacedKinds)
            {
                int have = 0;
                foreach (PlaceItem p in all) if (p.Selected && p.Kind == k && !p.WaterPlant) have++;
                if (have >= MinPool(k)) continue;
                if (lib != null && !lib.Uses(k)) continue; // the world has none: don't add any (no random streetlights)
                int added = 0;
                foreach (PlaceItem p in all)
                    if (!p.Selected && p.Kind == k && GenericPick(p, biome)) { p.Selected = true; added++; }
                if (added > 0) filled.Add(KindLabel(k) + " +" + added);
            }
            // water plants: any natural ones (reeds, cattails, lilies)
            foreach (PlaceItem p in all) if (p.WaterPlant && !p.Indoor && !p.BuyPlant) p.Selected = true;
            if (lib != null)
            {
                float sp = lib.LightEvery(); // metres of lit road per light (the nearest-light distance is the road width where lights stand in pairs)
                if (float.IsNaN(sp) && !lib.Spacing.TryGetValue(PlaceKind.Streetlight, out sp)) sp = float.NaN;
                if (!float.IsNaN(sp)) tbLightSpacing.Value = Math.Max(tbLightSpacing.Minimum, Math.Min(tbLightSpacing.Maximum, (int)Math.Round(sp)));
            }
            // a kind the Like world never uses can't be ticked
            List<string> none = new List<string>();
            foreach (PlaceKind k in PlacedKinds)
            {
                CheckBox cb = KindBox(k);
                bool used = lib == null || lib.Uses(k);
                if (!used) { cb.Checked = false; none.Add(KindLabel(k).ToLowerInvariant()); }
                cb.Enabled = used;
            }
            FillList();
            SchedulePreview();
            if (none.Count > 0) filled.Add("none of: " + string.Join(", ", none.ToArray()) + " (this world has none)");
            string fill = filled.Count > 0 ? " Generic picks added where this catalog lacks the world's own: " + string.Join(", ", filled.ToArray()) + "." : "";
            if (lib != null)
                Status(string.Format("Like {0}: {1} of its objects found in this catalog.{2}", lib.Name, matched, fill));
            else
                Status("Generic: this world looks " + BiomeLabel(biome) + " (" + why + ")." + fill);
        }

        /// <summary>Safe default for a kind when the Like world doesn't supply one.</summary>
        private static bool GenericPick(PlaceItem p, Biome biome)
        {
            if (p.Indoor || p.BuyPlant || p.WaterPlant) return false;
            string inst = p.Catalog.InstanceHex;
            bool eaKnown = inst.Length > 0 && PlacementEngine.Known.ContainsKey(inst);
            switch (p.Kind)
            {
                case PlaceKind.Tree: return p.Catalog.IsTree && PlacementEngine.InBiome(p, biome);
                case PlaceKind.Bush: return p.Catalog.IsTree || eaKnown;          // SpeedTree bushes or EA scenery
                case PlaceKind.Rock: return true;                                  // the strict rock filter already applied
                case PlaceKind.Sign: case PlaceKind.Streetlight: return eaKnown;   // EA's own street furniture
                default: return false;
            }
        }

        private Biome DetectBiomeHere(out string why)
        {
            TextureFeatures baseF = null;
            try
            {
                object world = Caw.CurrentWorld;
                string folder = Caw.TerrainTexturesPath();
                string baseName = Path.GetFileNameWithoutExtension(Caw.LayerName(Caw.Terrain(world), 0));
                foreach (string t in TextureLibrary.Scan(folder))
                    if (string.Equals(Path.GetFileNameWithoutExtension(t), baseName, StringComparison.OrdinalIgnoreCase)) { baseF = TextureAnalyzer.Analyze(t); break; }
            }
            catch (Exception ex) { Log.Error("Reading base texture for biome failed", ex); }
            return PlacementEngine.DetectBiome(existingNames, baseF, out why);
        }

        private static string KindLabel(PlaceKind k)
        {
            switch (k)
            {
                case PlaceKind.Tree: return "trees";
                case PlaceKind.Rock: return "rocks";
                case PlaceKind.Sign: return "road signs";
                case PlaceKind.Streetlight: return "streetlights";
                case PlaceKind.Bush: return "bushes";
                default: return k.ToString().ToLowerInvariant();
            }
        }

        private void LearnObjects()
        {
            if (busy || Injector.BusyElsewhere(this)) return;
            object world = Caw.CurrentWorld;
            if (world == null || work == null) { Status("Open the reference world and click 'Read world' first."); return; }
            string name = Prompt("Learn from this world", "Name for this reference (e.g. Sunset Valley):", Caw.WorldName(world));
            if (string.IsNullOrEmpty(name)) return;
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            SetBusy(true);
            try
            {
                PlaceSettings s = Settings();
                PlacementEngine.Maps m = PlacementEngine.BuildMaps(work, workCell, s, null);
                List<object[]> objs = new List<object[]>();
                foreach (object[] o in Caw.ExistingObjects())
                {
                    float mx, my; TerrainSampler.WorldToMap(terrain, (float)o[1], (float)o[2], out mx, out my);
                    objs.Add(new object[] { o[0], mx, my });
                }
                ObjectLibrary lib = ObjectLibrary.Learn(name, m, workCell, factor, objs, s.WaterLevel, work, s.WaterOffset);
                int n = 0; foreach (RefObject r in lib.Items) n += r.Count;
                SetBusy(false);
                if (n < 20)
                {
                    MessageBox.Show(this, "Only " + n + " trees, bushes or rocks were found outside lots, which is too few to learn from.\n\n" +
                        "Use a world that has its scenery placed (e.g. a copy of an EA world, not an 'empty' one).", "Learn from this world");
                    Status("Not saved.");
                    return;
                }
                string path = Path.Combine(ObjectLibrary.Dir, name + ".objects");
                lib.Save(path);
                Log.Info(string.Format("Learned objects '{0}': {1} objects, {2} kinds, {3:F0} ha land; per ha: trees {4:F1}, bushes {5:F1}, rocks {6:F1}",
                    name, n, lib.Items.Count, lib.LandHectares, lib.PerHectare[0], lib.PerHectare[1], lib.PerHectare[2]));
                for (int i = 0; i < Math.Min(15, lib.Items.Count); i++) Log.Info("  " + lib.Items[i].Count + " x " + lib.Items[i].Name + " [" + lib.Items[i].Kind + "]");
                RefreshLibraries(name);
                Status("Learned '" + name + "': " + n + " objects of " + lib.Items.Count + " kinds. Saved to " + path);
            }
            catch (Exception ex)
            {
                Log.Error("Learning objects failed", ex);
                SetBusy(false);
                Status("Learning failed: " + ex.Message);
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

        // ---------------- choosing items ----------------

        private void FillList()
        {
            try { FillListCore(); }
            catch (Exception ex) { Log.Error("Item list failed", ex); Status("Item list failed: " + ex.Message); }
        }

        private void FillListCore()
        {
            if (list == null || cbShow == null) return;
            filling = true;
            list.BeginUpdate();
            list.Items.Clear(); shown.Clear();
            int show = cbShow.SelectedIndex;
            foreach (PlaceItem p in all)
            {
                if (p.Indoor || !Listed(p, show)) continue;
                if (txtFilter.Text.Length > 0 && p.Name.IndexOf(txtFilter.Text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                shown.Add(p);
                string tag = p.WaterPlant ? "water plant" : KindLabel(p.Kind);
                list.Items.Add(p.Name + "   [" + tag + (p.BuyPlant ? ", buy-mode plant?" : "") + "]", p.Selected);
            }
            list.EndUpdate();
            filling = false;
        }

        /// <summary>Whether the list shows this item for the 'Show' choice.</summary>
        private bool Listed(PlaceItem p, int show)
        {
            switch (show)
            {
                case 1: return p.Kind == PlaceKind.Tree;
                case 2: return p.Kind == PlaceKind.Rock;
                case 3: return p.Kind == PlaceKind.Sign;
                case 4: return p.Kind == PlaceKind.Streetlight;
                case 5: return p.Kind == PlaceKind.Bush && !p.WaterPlant;
                case 6: return p.WaterPlant;
                default: return p.WaterPlant ? chkWaterPlants.Checked : KindOn(p.Kind);
            }
        }

        private CheckBox KindBox(PlaceKind k)
        {
            switch (k)
            {
                case PlaceKind.Bush: return chkBushes;
                case PlaceKind.Rock: return chkRocks;
                case PlaceKind.Sign: return chkSigns;
                case PlaceKind.Streetlight: return chkLights;
                default: return chkTrees;
            }
        }

        private bool KindOn(PlaceKind k)
        {
            switch (k)
            {
                case PlaceKind.Tree: return chkTrees.Checked;
                case PlaceKind.Bush: return chkBushes.Checked;
                case PlaceKind.Rock: return chkRocks.Checked;
                case PlaceKind.Sign: return chkSigns.Checked;
                case PlaceKind.Streetlight: return chkLights.Checked;
                default: return false; // spawners: own window; fences and other props: not placed automatically
            }
        }

        private List<PlaceItem> details;

        /// <summary>The base-game street details (PlacementEngine.DetailNames), from the catalog by instance, else by key.</summary>
        private List<PlaceItem> DetailItems()
        {
            if (details != null) return details;
            details = new List<PlaceItem>();
            for (int k = 0; k < PlacementEngine.DetailIds.Length; k++)
            {
                string hex = PlacementEngine.DetailIds[k].ToString("x16");
                Caw.CatalogItem ci = null;
                foreach (PlaceItem p in all) if (p.Catalog != null && p.Catalog.InstanceHex == hex) { ci = p.Catalog; break; }
                try { if (ci == null) ci = Caw.ObjectByInstance(PlacementEngine.DetailNames[k], PlacementEngine.DetailIds[k]); }
                catch (Exception ex) { Log.Error("Street detail " + PlacementEngine.DetailNames[k] + " not found", ex); }
                if (ci == null) continue;
                PlaceItem it = new PlaceItem();
                it.Catalog = ci; it.Name = PlacementEngine.DetailNames[k]; it.Kind = PlaceKind.Detail; it.Detail = PlacementEngine.DetailRoles[k]; it.Selected = true;
                details.Add(it);
            }
            Log.Info("Street details: " + details.Count + " of " + PlacementEngine.DetailIds.Length + " objects found");
            return details;
        }

        private List<PlaceItem> ActiveItems()
        {
            List<PlaceItem> items = new List<PlaceItem>();
            foreach (PlaceItem p in all)
            {
                PlaceItem c = new PlaceItem();
                c.Catalog = p.Catalog; c.Name = p.Name; c.Kind = p.Kind;
                c.Conifer = p.Conifer; c.WaterLover = p.WaterLover; c.Lowland = p.Lowland; c.Palm = p.Palm; c.RockSize = p.RockSize;
                c.Ref = p.Ref; c.Weight = p.Weight; c.Indoor = p.Indoor; c.WaterPlant = p.WaterPlant; c.BuyPlant = p.BuyPlant;
                c.Selected = p.Selected && !p.Indoor && (p.WaterPlant ? chkWaterPlants.Checked : KindOn(p.Kind));
                items.Add(c);
            }
            foreach (PlaceItem d in DetailItems())
                if (PlacementEngine.IsTownSign(d.Detail) ? chkTownSigns.Checked : chkDetails.Checked) items.Add(d);
            return items;
        }

        // ---------------- preview ----------------

        private void SchedulePreview() { if (work != null) { previewTimer.Stop(); previewTimer.Start(); } }

        private PlaceSettings Settings()
        {
            PlaceSettings s = new PlaceSettings();
            s.Seed = (int)nudSeed.Value; s.ForestCover = tbCover.Value; s.TreeDensity = tbDensity.Value;
            s.Bushes = tbBushes.Value; s.Rocks = tbRocks.Value;
            s.WaterLevel = terrain.WaterLevel;
            s.WaterOffset = terrain.WaterOffset != null ? TerrainSampler.Downsample(terrain.WaterOffset, factor) : null;
            s.Library = cbRef.SelectedItem as ObjectLibrary;
            s.MatchLibraryDensity = chkMatch.Checked;
            s.StreetlightSpacing = tbLightSpacing.Value;
            s.WaterPlants = chkWaterPlants.Checked; s.MaxObjects = tbMax.Value * 100;
            s.EdgeMargin = tbEdge.Value; s.DriveRight = cbSide == null || cbSide.SelectedIndex == 0;
            s.Roads = roadLines; s.Junctions = junctions; s.Lots = lots;
            s.Bridges = bridgeLines; s.MapW = terrain.MapW; s.MapH = terrain.MapH;
            float[] rp = RockPaint(s.Library);
            if (rp != null) s.TreeSlopeStart = PlacementEngine.RockStart(rp);
            s.AltitudeZones = chkZones.Checked;
            if (painted != null) { s.PlanCodes = painted.Codes; s.PlanN = painted.N; s.WoodsOnlyInForest = woodsOnlyInForest; }
            return s;
        }

        /// <summary>The rock paint share by slope of the style named like the World type (EA styles are built in).</summary>
        private static readonly Dictionary<string, float[]> rockPaint = new Dictionary<string, float[]>();
        private static float[] RockPaint(ObjectLibrary lib)
        {
            if (lib == null) return null;
            float[] v;
            if (rockPaint.TryGetValue(lib.Name, out v)) return v;
            try
            {
                foreach (StyleProfile st in StyleProfile.LoadAll())
                    if (string.Equals(st.Name, lib.Name, StringComparison.OrdinalIgnoreCase) && st.Roles.ContainsKey(PaintRole.Rock))
                        v = st.Roles[PaintRole.Rock].Slope;
            }
            catch (Exception ex) { Log.Error("Reading the style for " + lib.Name + " failed", ex); }
            rockPaint[lib.Name] = v;
            return v;
        }

        private void UpdatePreview()
        {
            if (work == null || busy) return;
            try
            {
                PlaceSettings s = Settings();
                List<PlaceItem> items = ActiveItems();
                clearMap = Blocked();
                maps = PlacementEngine.BuildMaps(work, workCell, s, clearMap);
                string fitNote;
                plan = PlacementEngine.FitDensity(maps, workCell, factor, items, s, occupied, landHa, out fitNote);
                if (fitNote.Length > 0) Status("Density vs " + s.Library.Name + ", " + fitNote);
                int before = plan.Count;
                float kept = PlacementEngine.Budget(plan, items, existing.Count, s.MaxObjects, s.Seed);
                int[] n = new int[PlaceSettings.Kinds];
                Dictionary<string, int> per = new Dictionary<string, int>();
                foreach (Placement p in plan)
                {
                    n[(int)items[p.Item].Kind]++;
                    int c; per.TryGetValue(items[p.Item].Name, out c); per[items[p.Item].Name] = c + 1;
                }
                List<WaterfallSite> falls = ActiveFalls();
                chkFalls.Enabled = FallCap() > 0;
                picture.Image = Render(s, items);
                List<string> top = new List<string>();
                foreach (KeyValuePair<string, int> kv in per) top.Add(kv.Value + " " + kv.Key);
                top.Sort(delegate (string a, string b) { return int.Parse(b.Split(' ')[0]).CompareTo(int.Parse(a.Split(' ')[0])); });
                if (top.Count > 8) top.RemoveRange(8, top.Count - 8);
                int total = plan.Count;
                // one line per preview, so a log shows what each slider setting gave (the tester, v0.64: the tree sliders seemed to do nothing)
                string key = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}|{4}|{5}|{6}", s.Library != null ? s.Library.Name : "Generic",
                    s.MatchLibraryDensity, s.ForestCover, s.TreeDensity, s.Bushes, s.Rocks, s.MaxObjects);
                if (key != lastPreviewKey)
                {
                    lastPreviewKey = key;
                    Log.Info(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "Auto-Place preview: World type {0}, match {1}, cover {2}, tree density {3}, bushes {4}, rocks {5}: {6} trees ({7:F1}/ha of {8:F0} ha land), {9} bushes, {10} rocks; kept {11:P0} of {12} (max {13}, {14} already placed){15}",
                        s.Library != null ? s.Library.Name + (s.Library.TreeCountIncomplete ? " (tree count incomplete)" : "") : "Generic", s.MatchLibraryDensity ? "on" : "off",
                        s.ForestCover, s.TreeDensity, s.Bushes, s.Rocks, n[0], landHa > 0 ? n[0] / landHa : 0, landHa, n[1], n[2], kept, before, s.MaxObjects, existing.Count,
                        fitNote.Length > 0 ? "; fit " + fitNote : ""));
                }
                counts.Text = string.Format("{0} trees, {1} rocks, {2} signs, {3} bushes and water plants, {4} streetlights = {5} new objects.\n" +
                    "World total {6} of {7} ({8} already placed).{9}\n{10}\n" +
                    "Dark green = trees, light green = bushes, cyan = water plants, grey = rocks, white = streetlights, red = signs; red area = kept clear." +
                    (chkFalls.Checked ? "\nWaterfalls (blue triangles): " + falls.Count + " of " + fallSites.Count + " pond-step sites found, World type has " + FallCap() + "." : ""),
                    n[0], n[2], n[5], n[1], n[4], total, total + existing.Count, s.MaxObjects, existing.Count,
                    kept < 1 ? string.Format(" Thinned to {0:F0}% to stay within the limit ({1} planned).", kept * 100, before) : "",
                    string.Join(", ", top.ToArray()));
            }
            catch (Exception ex) { Log.Error("Auto-Place preview failed", ex); Status("Preview failed: " + ex.Message); }
        }

        private Bitmap Render(PlaceSettings s, List<PlaceItem> items)
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
                        int xm = Math.Max(0, wx - 1), xp = Math.Min(work.W - 1, wx + 1), ym = Math.Max(0, wy - 1), yp = Math.Min(work.H - 1, wy + 1);
                        float gx = (work[xp, wy] - work[xm, wy]) / ((xp - xm) * workCell), gy = (work[wx, yp] - work[wx, ym]) / ((yp - ym) * workCell);
                        float shade = Math.Max(0.5f, Math.Min(1.2f, (gx + gy) / 1.41421f * 0.6f + 0.85f));
                        float wl = s.WaterLevel + (s.WaterOffset != null ? s.WaterOffset.D[i] : 0);
                        Color c = work.D[i] < wl ? Color.FromArgb(55, 85, 110) : Color.FromArgb(120, 135, 80);
                        if (maps.Blocked.D[i] > 0 && work.D[i] >= wl) c = Color.FromArgb(150, 90, 80);
                        bmp.SetPixel(x, y, Color.FromArgb(Clamp(c.R * shade), Clamp(c.G * shade), Clamp(c.B * shade)));
                    }
                Color[] cols = { Color.FromArgb(25, 70, 25), Color.FromArgb(130, 180, 70), Color.FromArgb(170, 170, 170), Color.FromArgb(150, 60, 160),
                                 Color.White, Color.FromArgb(230, 20, 20), Color.FromArgb(0, 110, 220), Color.FromArgb(150, 100, 50), Color.FromArgb(255, 170, 40) };
                // roadside things last and larger, so they show on top
                for (int pass = 0; pass < 2; pass++)
                    foreach (Placement p in plan)
                    {
                        PlaceItem it = items[p.Item];
                        bool roadside = it.Kind == PlaceKind.Streetlight || it.Kind == PlaceKind.Sign;
                        if (roadside != (pass == 1)) continue;
                        float px = p.MapX / factor * k, py = p.MapY / factor * k, r = it.Kind == PlaceKind.Bush ? 1f : roadside ? 2.5f : 1.5f;
                        Color c = it.WaterPlant ? Color.FromArgb(60, 220, 220) : cols[(int)it.Kind];
                        using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, px - r, py - r, 2 * r, 2 * r);
                        if (roadside) using (Pen o = new Pen(Color.Black, 1f)) g.DrawEllipse(o, px - r, py - r, 2 * r, 2 * r);
                    }
                foreach (WaterfallSite w in ActiveFalls())
                {
                    float px = w.MapX / factor * k, py = w.MapY / factor * k;
                    PointF[] tri = { new PointF(px, py - 6), new PointF(px - 5, py + 4), new PointF(px + 5, py + 4) };
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(40, 140, 255))) g.FillPolygon(b, tri);
                    using (Pen o = new Pen(Color.White, 1f)) g.DrawPolygon(o, tri);
                }
            }
            return bmp;
        }

        private static int Clamp(float v) { return v < 0 ? 0 : (v > 255 ? 255 : (int)v); }

        // ---------------- waterfalls (#158) ----------------

        private List<WaterfallSite> FindFalls()
        {
            try
            {
                TerrainInfo ti = terrain;
                Grid levels = Waterfalls.PondLevels(ti.Heights.W, ti.Heights.H, ti.Ponds,
                    delegate (float px, float py, out float wx, out float wz) { TerrainSampler.PixelToWorld(ti, px, py, out wx, out wz); });
                List<WaterfallSite> found = Waterfalls.Find(ti.Heights, ti.CellMeters, ti.WaterLevel, levels);
                // not where the world already has a waterfall (placed by EA, by hand or by an earlier run)
                List<float[]> have = new List<float[]>();
                foreach (Caw.WorldObject o in existing)
                    if (o.Name != null && o.Name.IndexOf("aterfall", StringComparison.OrdinalIgnoreCase) >= 0) have.Add(new[] { o.X, o.Z });
                if (have.Count > 0)
                    found.RemoveAll(delegate (WaterfallSite w)
                    {
                        float x, z; TerrainSampler.PixelToWorld(ti, w.MapX, w.MapY, out x, out z);
                        foreach (float[] h in have) if ((h[0] - x) * (h[0] - x) + (h[1] - z) * (h[1] - z) < 40 * 40) return true;
                        return false;
                    });
                Log.Info("Waterfalls: " + found.Count + " pond-step sites (" + ti.Ponds.Count + " ponds)" +
                    (found.Count > 0 ? ": " + string.Join(", ", found.ConvertAll(delegate (WaterfallSite w) { return w.Size + " " + w.Drop.ToString("F0") + " m"; }).ToArray()) : ""));
                return found;
            }
            catch (Exception ex) { Log.Error("Finding waterfall sites failed", ex); return new List<WaterfallSite>(); }
        }

        /// <summary>The World type's EA waterfall count (0 for Generic or a world without falls).</summary>
        private int FallCap()
        {
            ObjectLibrary lib = cbRef.SelectedItem as ObjectLibrary;
            return lib != null && lib.FallSites > 0 ? lib.FallSites : 0;
        }

        /// <summary>The sites to place: the highest drops first, up to the World type's count, off roads and lots.</summary>
        private List<WaterfallSite> ActiveFalls()
        {
            List<WaterfallSite> o = new List<WaterfallSite>();
            int cap = FallCap();
            if (!chkFalls.Checked || cap == 0 || clearMap == null || clearMap.W != work.W || clearMap.H != work.H) return o;
            foreach (WaterfallSite w in fallSites)
            {
                if (o.Count >= cap) break;
                // roads, lots, paths and the rim only: maps.Blocked also counts the pond's shore margin as water, which every lip is in
                int wx = Math.Min(work.W - 1, (int)(w.MapX / factor)), wy = Math.Min(work.H - 1, (int)(w.MapY / factor));
                if (clearMap.D[wy * work.W + wx] > 0) continue;
                o.Add(w);
            }
            return o;
        }

        private int FallVariant() { ObjectLibrary lib = cbRef.SelectedItem as ObjectLibrary; return lib != null ? lib.FallVariant : 1; }

        /// <summary>Places a fall at each site's lip and a splash where it lands, in the 'Waterfalls' World Layer. Returns the number placed.</summary>
        private int PlaceFalls(object world, List<WaterfallSite> falls, ref int failed)
        {
            if (falls.Count == 0) return 0;
            int v = FallVariant(), n = 0;
            List<string> known = new List<string>();
            try { known = Caw.EffectNames(); } catch (Exception ex) { Log.Error("Reading CAW's effect names failed", ex); }
            if (chkOwnLayers.Checked)
                try { if (!Caw.UseLayer(world, "Waterfalls")) Log.Info("Could not switch to World Layer 'Waterfalls'; using the active one"); }
                catch (Exception ex) { Log.Error("Switching to World Layer 'Waterfalls' failed", ex); }
            foreach (WaterfallSite w in falls)
            {
                string fall = Waterfalls.FallEffect(v, w.Size), splash = Waterfalls.SplashEffect(v, w.Size);
                if (!known.Contains(fall) || !known.Contains(splash))
                {
                    // never another effect in its place (#158): report and skip
                    Log.Info("Waterfalls: CAW has no effect '" + (known.Contains(fall) ? splash : fall) + "' (" + known.Count + " effects listed); skipped");
                    failed++; continue;
                }
                float x, z, x2, z2, lx, lz;
                TerrainSampler.PixelToWorld(terrain, w.MapX, w.MapY, out x, out z);
                TerrainSampler.PixelToWorld(terrain, w.MapX + w.DirX * 10, w.MapY + w.DirY * 10, out x2, out z2);
                TerrainSampler.PixelToWorld(terrain, w.LandX, w.LandY, out lx, out lz);
                float yaw = (float)Math.Atan2(x2 - x, z2 - z); // facing (sin yaw, cos yaw) = downhill, as EA's falls face
                object a = null, b = null;
                try { a = Caw.PlaceEffect(fall, "Waterfall", x, z, yaw, lastPlaced.Count > 0); }
                catch (Exception ex) { Log.Error("Placing " + fall + " failed", ex); }
                if (a != null) lastPlaced.Add(a); else failed++;
                try { b = Caw.PlaceEffect(splash, "Waterfall splash", lx, lz, yaw, lastPlaced.Count > 0, w.WaterY); }
                catch (Exception ex) { Log.Error("Placing " + splash + " failed", ex); }
                if (b != null) lastPlaced.Add(b); else failed++;
                if (a != null) { n++; fallSites.Remove(w); } // placed: a second Place doesn't put it down again
                Log.Info(string.Format(System.Globalization.CultureInfo.InvariantCulture, "Waterfalls: {0} at {1:F0}, {2:F0} (drop {3:F0} m, yaw {4:F0} deg){5}; splash at {6:F0}, {7:F0}, y {8:F1}{9}",
                    fall, x, z, w.Drop, yaw * 180 / Math.PI, a != null ? "" : " FAILED", lx, lz, w.WaterY, b != null ? "" : " FAILED"));
            }
            return n;
        }

        // ---------------- placing ----------------

        private void PlaceAll()
        {
            if (busy || Injector.BusyElsewhere(this)) return;
            object world = Caw.CurrentWorld;
            if (world == null || terrain == null) { Status("Click 'Read world' first."); return; }
            List<WaterfallSite> falls = ActiveFalls();
            if ((plan == null || plan.Count == 0) && falls.Count == 0) { Status("Nothing to place; tick some items."); return; }
            if (plan == null) plan = new List<Placement>();
            if (!chkOwnLayers.Checked && !Caw.HasActiveLayer(world))
            {
                MessageBox.Show(this, "Select the World Layer to place into first (e.g. 'Trees' in CAW's World Layers panel).", "Auto-Place");
                return;
            }
            List<PlaceItem> items = ActiveItems();
            // which World Layer each placement goes into
            string[] target = new string[plan.Count];
            Dictionary<string, int> perLayer = new Dictionary<string, int>();
            for (int i = 0; i < plan.Count; i++)
            {
                target[i] = chkOwnLayers.Checked ? TargetLayer(items[plan[i].Item], plan[i]) : "";
                int c; perLayer.TryGetValue(target[i], out c); perLayer[target[i]] = c + 1;
            }
            string where = "into the selected World Layer";
            if (chkOwnLayers.Checked)
            {
                List<string> parts = new List<string>();
                foreach (KeyValuePair<string, int> kv in perLayer) parts.Add(kv.Value + " in '" + kv.Key + "'" + (layerNames.Contains(kv.Key) ? "" : " (new)"));
                where = "into World Layers:\n  " + string.Join("\n  ", parts.ToArray());
            }
            if (falls.Count > 0) where += "\nand " + falls.Count + " waterfalls (fall + splash each)" + (chkOwnLayers.Checked ? " in 'Waterfalls'" : "");
            if (MessageBox.Show(this, "Place " + plan.Count + " objects " + where + "\n\n" +
                    "This can take a few minutes. 'Remove last' (or CAW's Undo) takes them out again." +
                    (chkCluster.Checked ? "\nTrees are clustered afterwards (CAW's Cluster Trees)." : ""),
                    "Auto-Place", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            SetBusy(true);
            placing = true; stopPlacing = false;
            btnPlace.Text = "Stop"; btnPlace.Enabled = true;
            lastPlaced.Clear();
            int failed = 0, trees = 0, stoppedAt = -1;
            DateTime t0 = DateTime.Now;
            Caw.ResetPlaceTiming();
            try
            {
                // place layer by layer, so each lands in its World Layer
                int[] order = new int[plan.Count];
                for (int i = 0; i < order.Length; i++) order[i] = i;
                Array.Sort(order, delegate (int a, int b) { int c = string.CompareOrdinal(target[a], target[b]); return c != 0 ? c : a.CompareTo(b); });
                string current = null;
                for (int k = 0; k < order.Length; k++)
                {
                    Placement p = plan[order[k]];
                    if (chkOwnLayers.Checked && current != target[order[k]])
                    {
                        current = target[order[k]];
                        try { if (!Caw.UseLayer(world, current)) Log.Info("Could not switch to World Layer '" + current + "'; using the active one"); }
                        catch (Exception ex) { Log.Error("Switching to World Layer '" + current + "' failed", ex); }
                    }
                    float x, z; TerrainSampler.MapToWorld(terrain, p.MapX, p.MapY, out x, out z);
                    object o = null;
                    try { o = Caw.Place(items[p.Item].Catalog, x, z, p.Yaw, p.Variant, lastPlaced.Count > 0); }
                    catch (Exception ex) { if (failed < 5) Log.Error("Placing " + items[p.Item].Name + " failed", ex); }
                    if (o != null) { lastPlaced.Add(o); if (items[p.Item].Catalog.IsTree) trees++; } else failed++;
                    if (k == 9 && lastPlaced.Count == 0) { Status("The first 10 objects failed; stopped. See the log."); break; }
                    if (k % 10 == 0) { OnProgress("Placing " + (k + 1) + " of " + plan.Count + " (click Stop to stop)", 100 * k / plan.Count); Injector.Pump(); }
                    if (stopPlacing) { stoppedAt = k + 1; break; }
                }
                int fallsPlaced = stoppedAt < 0 ? PlaceFalls(world, falls, ref failed) : 0;
                if (stoppedAt >= 0) Log.Info("Auto-Place: stopped by the user after " + stoppedAt + " of " + plan.Count);
                Log.Info(string.Format("Auto-Place: placed {0}, failed {1}, {2:F0} s", lastPlaced.Count, failed, (DateTime.Now - t0).TotalSeconds));
                Log.Info("Auto-Place timing: " + Caw.PlaceTimingSummary());
                foreach (KeyValuePair<string, int> kv in perLayer) Log.Info("  " + kv.Value + " into '" + kv.Key + "'");
                string clustered = "";
                if (chkCluster.Checked && trees > 0)
                {
                    OnProgress("Clustering trees", 99);
                    try { Caw.ClusterTrees(); clustered = " Trees clustered."; Log.Info("Auto-Place: Cluster Trees done"); }
                    catch (Exception ex) { Log.Error("Cluster Trees failed", ex); clustered = " Clustering failed (see log); use CAW's Cluster Trees button."; }
                }
                if (lastPlaced.Count > 0) Injector.Done("objects");
                Status((stoppedAt >= 0 ? "Stopped: placed " : "Placed ") + lastPlaced.Count + " objects" + (fallsPlaced > 0 ? " incl. " + fallsPlaced + " waterfalls" : "") + (failed > 0 ? " (" + failed + " failed, see log)" : "") + "." + clustered +
                    (stoppedAt >= 0 ? " 'Remove last' takes these out again." : ""));
                // they now count as existing, so a second run fills around them and the budget counts them
                for (int k = 0; k < plan.Count; k++)
                {
                    if (stoppedAt >= 0) break; // (a stopped run: only some were placed; 'Read world' counts them again)
                    Placement p = plan[k];
                    occupied.Add(new[] { p.MapX, p.MapY, 2.5f / terrain.CellMeters });
                    Caw.WorldObject w = new Caw.WorldObject(); w.Name = items[p.Item].Name; existing.Add(w);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Auto-Place failed", ex); Status("Placing failed: " + ex.Message);
                if (ex is WorldChangedException) lastPlaced.Clear(); // they belong to the other world: 'Remove last' mustn't touch them
            }
            placing = false; stopPlacing = false; btnPlace.Text = "Place";
            SetBusy(false);
        }

        // ---------------- World Layers ----------------

        // the layer roles Auto-Place fills; "WaterRocks" = rocks at or in the water
        private static readonly string[] Roles = { "Tree", "Bush", "Rock", "WaterRocks", "Sign", "Streetlight" };
        private static readonly string[] RoleLabels = { "Trees", "Bushes and water plants", "Rocks", "Rocks in or by water", "Road signs", "Streetlights" };
        private static readonly string[] DefaultLayer = { "Trees", "Bushes", "Rocks", "Rocks", "Signs", "Streetlights" };
        private static readonly string[][] RoleWords = {
            new[] { "tree", "trees", "forest", "wood", "woods", "flora" },
            new[] { "bush", "bushes", "shrub", "shrubs", "plant", "plants", "flora", "flower", "flowers", "garden" },
            new[] { "rock", "rocks", "stone", "stones", "boulder", "boulders", "cliff" },
            new[] { "water", "river", "rivers", "sea", "ocean", "lake", "pond", "beach", "coast", "shore" },
            new[] { "sign", "signs", "road", "roads", "street", "traffic" },
            new[] { "light", "lights", "lamp", "lamps", "lighting", "street", "road", "roads" } };
        private static readonly string[] MiscWords = { "misc", "miscellaneous", "props", "objects", "ploppable", "ploppables", "world" };

        /// <summary>
        /// Picks a World Layer for each role from the world's own layers: by name (Trees, Road Misc, River and Sea...),
        /// then by what they already hold; a role nothing fits goes to a 'misc' layer if there is one (World Misc),
        /// else a new layer with EA's usual name.
        /// </summary>
        private void GuessLayers()
        {
            layerFor.Clear();
            // what each existing layer holds, by kind
            Dictionary<string, int[]> holds = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
            foreach (Caw.WorldObject o in existing)
            {
                if (string.IsNullOrEmpty(o.Layer)) continue;
                int[] h;
                if (!holds.TryGetValue(o.Layer, out h)) { h = new int[PlaceSettings.Kinds + 1]; holds[o.Layer] = h; }
                Caw.CatalogItem ci = new Caw.CatalogItem(); ci.Name = o.Name ?? ""; ci.IsTree = o.Tree;
                h[(int)PlacementEngine.Classify(ci).Kind]++; h[PlaceSettings.Kinds]++;
            }
            string misc = null;
            foreach (string l in layerNames)
                if (PlacementEngine.Words(l).Exists(delegate (string w) { return Array.IndexOf(MiscWords, w) >= 0; })) { misc = l; break; }
            for (int r = 0; r < Roles.Length; r++)
            {
                string best = null; float bv = 0.99f;
                foreach (string l in layerNames)
                {
                    List<string> words = PlacementEngine.Words(l);
                    float v = 0;
                    foreach (string w in words) if (Array.IndexOf(RoleWords[r], w) >= 0) v += 2;
                    if (r == 3 && v == 0) continue; // water rocks only go to a water-named layer
                    int[] h;
                    if (holds.TryGetValue(l, out h) && h[PlaceSettings.Kinds] >= 5)
                    {
                        int kind = r == 0 ? 0 : r == 1 ? 1 : r <= 3 ? 2 : r == 4 ? 5 : 4;
                        v += 3f * h[kind] / h[PlaceSettings.Kinds];
                    }
                    if (v > bv) { bv = v; best = l; }
                }
                if (best == null && r == 3) best = layerFor.ContainsKey("Rock") ? layerFor["Rock"] : null;
                if (best == null) best = misc ?? DefaultLayer[r];
                layerFor[Roles[r]] = best;
            }
            ShowLayers();
        }

        private void ShowLayers()
        {
            List<string> parts = new List<string>();
            for (int r = 0; r < Roles.Length; r++)
            {
                string l; if (!layerFor.TryGetValue(Roles[r], out l)) continue;
                parts.Add(RoleLabels[r].Split(' ')[0] + (r == 3 ? " in water" : "") + " > " + l + (layerNames.Contains(l) ? "" : " (new)"));
            }
            lblLayers.Text = string.Join(";  ", parts.ToArray());
        }

        private string TargetLayer(PlaceItem it, Placement p)
        {
            string role;
            switch (it.Kind)
            {
                case PlaceKind.Tree: role = "Tree"; break;
                case PlaceKind.Bush: role = "Bush"; break;
                case PlaceKind.Rock: role = NearWater(p) ? "WaterRocks" : "Rock"; break;
                case PlaceKind.Sign: role = "Sign"; break;
                case PlaceKind.Streetlight: role = "Streetlight"; break;
                case PlaceKind.Detail: return "Street Details";
                default: return "World Misc";
            }
            string l;
            return layerFor.TryGetValue(role, out l) ? l : DefaultLayer[Array.IndexOf(Roles, role)];
        }

        /// <summary>Within ~5 m of water (rivers, sea, ponds).</summary>
        private bool NearWater(Placement p)
        {
            if (maps == null) return false;
            int x = Math.Min(work.W - 1, Math.Max(0, (int)(p.MapX / factor))), y = Math.Min(work.H - 1, Math.Max(0, (int)(p.MapY / factor)));
            return maps.NearWater.D[y * work.W + x] > 0.15f;
        }

        /// <summary>Shows and changes which World Layer each kind goes into.</summary>
        private void EditLayers()
        {
            try { EditLayersCore(); }
            catch (Exception ex) { Log.Error("Layers failed", ex); Status("Layers failed: " + ex.Message); }
        }

        private void EditLayersCore()
        {
            if (terrain == null) { Status("Click 'Read world' first."); return; }
            using (Form f = new Form())
            {
                f.Text = "World Layers for Auto-Place"; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = f.MaximizeBox = false; f.ShowInTaskbar = false; f.Font = Font;
                f.ClientSize = new Size(380, 40 + Roles.Length * 28 + 40);
                Label head = new Label(); head.Text = "Pick one of your layers or type a new name:"; head.SetBounds(10, 10, 360, 18); f.Controls.Add(head);
                ComboBox[] boxes = new ComboBox[Roles.Length];
                for (int r = 0; r < Roles.Length; r++)
                {
                    Label l = new Label(); l.Text = RoleLabels[r]; l.SetBounds(10, 40 + r * 28, 150, 18); f.Controls.Add(l);
                    ComboBox cb = new ComboBox(); cb.DropDownStyle = ComboBoxStyle.DropDown; cb.SetBounds(164, 36 + r * 28, 206, 20);
                    foreach (string n in layerNames) cb.Items.Add(n);
                    string cur; layerFor.TryGetValue(Roles[r], out cur); cb.Text = cur ?? DefaultLayer[r];
                    f.Controls.Add(cb); boxes[r] = cb;
                }
                Button ok = new Button(); ok.Text = "OK"; ok.DialogResult = DialogResult.OK; ok.SetBounds(214, f.ClientSize.Height - 34, 75, 24);
                Button cancel = new Button(); cancel.Text = "Cancel"; cancel.DialogResult = DialogResult.Cancel; cancel.SetBounds(295, f.ClientSize.Height - 34, 75, 24);
                f.Controls.Add(ok); f.Controls.Add(cancel); f.AcceptButton = ok; f.CancelButton = cancel;
                if (Theme.Dialog(f, this) != DialogResult.OK) return;
                for (int r = 0; r < Roles.Length; r++) if (boxes[r].Text.Trim().Length > 0) layerFor[Roles[r]] = boxes[r].Text.Trim();
                ShowLayers();
            }
        }

        private void RemoveLast()
        {
            if (busy || lastPlaced.Count == 0) { Status("Nothing placed in this session."); return; }
            if (MessageBox.Show(this, "Remove the " + lastPlaced.Count + " objects from the last Auto-Place?", "Auto-Place",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            SetBusy(true);
            int removed = 0;
            for (int i = lastPlaced.Count - 1; i >= 0; i--)
            {
                try { Caw.Delete(lastPlaced[i], removed > 0); removed++; }
                catch (Exception ex) { if (removed < 3) Log.Error("Removing object failed", ex); }
                if (i % 25 == 0) { OnProgress("Removing", 100 * (lastPlaced.Count - i) / lastPlaced.Count); Injector.Pump(); }
            }
            lastPlaced.Clear();
            SetBusy(false);
            Status("Removed " + removed + " objects.");
        }

        // ---------------- helpers ----------------

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
            btnRead.Enabled = btnPlace.Enabled = btnRemove.Enabled = btnLearn.Enabled = !b;
            UseWaitCursor = b;
            if (!b) progress.Value = 0;
        }
    }
}
