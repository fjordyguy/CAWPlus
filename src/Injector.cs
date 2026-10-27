using System;
using System.Drawing;
using System.Windows.Forms;

namespace CawPlus
{
    /// <summary>Adds the CAW Plus tab (or, as a fallback, buttons on CAW's own toolbars) once the main window exists; opens the tool windows.</summary>
    internal static class Injector
    {
        // one CAW Plus action at a time: long actions pump messages (DoEvents), so another window's button could
        // otherwise start a second action whose undo steps would interleave with the first
        private static Form busyForm;
        // the world the running action works on: CAW's own menus stay usable during it (never locked), so another world
        // could be opened while the action still holds the first one (security review 2026-10-26)
        private static object busyWorld;
        private static string busyKey;

        public static void SetBusy(Form f, bool b)
        {
            if (b)
            {
                busyForm = f;
                try { busyWorld = Caw.CurrentWorld; busyKey = busyWorld != null ? Caw.WorldKey(busyWorld) : null; }
                catch { busyWorld = null; busyKey = null; }
            }
            else if (busyForm == f) { busyForm = null; busyWorld = null; busyKey = null; }
        }

        /// <summary>True when a CAW Plus action is running and CAW now has a different world open than when it began.</summary>
        public static bool WorldChanged
        {
            get
            {
                if (busyForm == null || busyWorld == null) return false;
                try
                {
                    object now = Caw.CurrentWorld;
                    if (ReferenceEquals(now, busyWorld)) return false;
                    return now == null || Caw.WorldKey(now) != busyKey; // a new wrapper of the same world is not a change
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// Lets CAW handle its messages during a long action (Application.DoEvents), then stops the action with
        /// WorldChangedException if another world was opened meanwhile, before anything more is written.
        /// </summary>
        public static void Pump()
        {
            Application.DoEvents();
            if (WorldChanged) throw new WorldChangedException();
        }

        /// <summary>True (with a beep) if another CAW Plus window is in the middle of an action.</summary>
        public static bool BusyElsewhere(Form f)
        {
            if (busyForm == null || busyForm == f || busyForm.IsDisposed) return false;
            System.Media.SystemSounds.Beep.Play();
            return true;
        }

        /// <summary>A tool finished one of the Create World steps (key, e.g. "terrain"); the wizard ticks it off.</summary>
        public delegate void StepHandler(string step);
        public static event StepHandler StepDone;
        public static void Done(string step)
        {
            Log.Info("Step done: " + step);
            StepHandler h = StepDone;
            if (h != null) try { h(step); } catch (Exception ex) { Log.Error("Create World step update failed", ex); }
        }

        /// <summary>Opens a tool window by name (the Create World wizard's buttons).</summary>
        public static void Open(string tool, Form owner)
        {
            switch (tool)
            {
                case "terrain": ShowTerrainGen(owner); break;
                case "builder": ShowWorldBuilder(owner); break;
                case "roads": ShowRoads(owner); break;
                case "paths": ShowFootpaths(owner); break;
                case "paint": ShowAutoPaint(owner); break;
                case "objects": ShowAutoPlace(owner); break;
                case "spawners": ShowSpawners(owner); break;
                case "distant": ShowDistant(owner); break;
            }
        }

        private static WizardForm wizardForm;
        private static void ShowWizard(Form owner)
        {
            try
            {
                if (Caw.CurrentWorld == null)
                {
                    MessageBox.Show(owner, "Open a world first (File > New for a blank one).", "Create World", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (wizardForm == null || wizardForm.IsDisposed) { wizardForm = new WizardForm(owner); wizardForm.Show(owner); }
                else wizardForm.Activate();
            }
            catch (Exception ex) { Log.Error("Opening Create World failed", ex); MessageBox.Show(owner, "Create World failed to open:\n" + ex.Message, "Create World"); }
        }

        private static bool done;
        private static int attempts;
        private static AutoPaintForm autoPaintForm;
        private static AutoPlaceForm autoPlaceForm;
        private static SpawnerForm spawnerForm;
        private static FootpathForm footpathForm;
        private static RoadsForm roadsForm;
        private static TerrainGenForm terrainGenForm;
        private static WorldBuilderForm worldBuilderForm;

        private static void ShowWorldBuilder(Form owner)
        {
            try
            {
                if (Caw.CurrentWorld == null)
                {
                    MessageBox.Show(owner, "Open a world first (File > New for a blank one).", "World Builder", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (worldBuilderForm == null || worldBuilderForm.IsDisposed) { worldBuilderForm = new WorldBuilderForm(); worldBuilderForm.Show(owner); }
                else worldBuilderForm.Activate();
            }
            catch (Exception ex)
            {
                Log.Error("Opening World Builder failed", ex);
                MessageBox.Show(owner, "World Builder failed to open:\n" + ex.Message, "World Builder", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private static DistantTerrainForm distantForm;

        /// <summary>
        /// Waits on a background thread for CAW's main window, then adds the button on CAW's UI thread.
        /// Nothing here may create a window before CAW's Main runs: Main calls SetCompatibleTextRenderingDefault,
        /// which throws if any window already exists (a WinForms Timer counts). CAW also never calls
        /// Application.Run (it loops on DoEvents), so Application.Idle never fires. BeginInvoke works with DoEvents.
        /// </summary>
        public static void Start()
        {
            System.Threading.Thread t = new System.Threading.Thread(WaitForMainForm);
            t.IsBackground = true;
            t.Name = "CAWPlus injector";
            t.Start();
            Log.Info("Waiting for CAW main window");
        }

        private static void WaitForMainForm()
        {
            while (!done)
            {
                System.Threading.Thread.Sleep(500);
                try
                {
                    Form form = Caw.MainForm as Form;
                    if (form == null || form.IsDisposed || !form.IsHandleCreated || !form.Visible) continue;
                    form.BeginInvoke(new EventHandler(OnTick), new object[] { null, EventArgs.Empty });
                }
                catch (Exception ex)
                {
                    // Visible/IsHandleCreated are safe off-thread; anything else failing just means "try again".
                    if (attempts++ > 20) { Log.Error("Waiting for CAW main window failed", ex); done = true; }
                }
            }
        }

        /// <summary>The CAW Plus tab: tools in the order a world is made, then the self-test and CAW's dark mode.</summary>
        private static void AddTabButtons(Form form, ToolStrip bar)
        {
            CawLook.Button(bar, "Create World", "Step by step from blank land to a finished world: opens each tool below in turn", true, delegate { ShowWizard(form); });
            bar.Items.Add(new ToolStripSeparator());
            CawLook.Button(bar, "1  Generate Terrain", "Step 1: hills, mountains, rivers, coasts and lakes like the chosen world type", true, delegate { ShowTerrainGen(form); });
            CawLook.Button(bar, "1  or World Builder", "Step 1, your own way: paint the world in squares (hills, mountains, lakes, sea, rivers, roads, town, forest) and Create", true, delegate { ShowWorldBuilder(form); });
            CawLook.Button(bar, "2  Generate Roads", "Step 2: plan and build roads and bridges, then place the lots", true, delegate { ShowRoads(form); });
            CawLook.Button(bar, "3  Footpaths", "Step 3: footpaths round the roads already built", false, delegate { ShowFootpaths(form); });
            CawLook.Button(bar, "4  Auto-Paint", "Step 4: paint the whole terrain (grass, rock, dirt, sand) in one click", true, delegate { ShowAutoPaint(form); });
            CawLook.Button(bar, "5  Auto-Place", "Step 5: trees, rocks, bushes, road signs and streetlights", true, delegate { ShowAutoPlace(form); });
            CawLook.Button(bar, "6  Spawners", "Step 6: fishing spots, seeds, gems, metals, insects", false, delegate { ShowSpawners(form); });
            CawLook.Button(bar, "7  Distant Terrain", "Step 7 (last): the landscape beyond the map edge, made from this world's own edge", false, delegate { ShowDistant(form); });
            bar.Items.Add(new ToolStripSeparator());
            CawLook.Button(bar, "Self-test", "Check every CAW call the tools use, in the open world (undone afterwards). Use a copy of a world.", false, delegate
            {
                if (BusyElsewhere(null)) return;
                try { SelfTest.Run(form); }
                catch (Exception ex) { Log.Error("Self-test failed", ex); MessageBox.Show(form, "Self-test failed: " + ex.Message, "CAW Plus"); }
            });
            ToolStripButton dark = CawLook.Button(bar, "Dark mode", "CAW's window in dark colours (the 3D view is unchanged). Remembered for next time.", false, null);
            dark.CheckOnClick = true;
            dark.Checked = CawLook.Remembered();
            dark.CheckedChanged += delegate { CawLook.SetDark(form, dark.Checked, true); };
            if (dark.Checked) CawLook.SetDark(form, true, false);
            Log.Info("CAW Plus tab: " + bar.Items.Count + " items");
        }

        /// <summary>Fallback when CAW's tab strip isn't as expected: buttons on CAW's own toolbars (as before v0.62).</summary>
        private static ToolStrip AddToCawToolbars(Form form)
        {
            ToolStrip bar = Caw.GetField(form, "mTerrainToolBar") as ToolStrip
                         ?? Caw.GetField(form, "mUtilitiesToolBar") as ToolStrip;

            if (bar != null)
            {
                bar.Items.Add(new ToolStripSeparator());
                ToolStripButton btn = new ToolStripButton("Auto-Paint");
                btn.DisplayStyle = ToolStripItemDisplayStyle.Text;
                btn.Font = new Font(bar.Font, FontStyle.Bold);
                btn.ToolTipText = "CAW Plus: automatically paint the whole terrain realistically";
                btn.Click += delegate { ShowAutoPaint(form); };
                bar.Items.Add(btn);
                ToolStripButton gen = new ToolStripButton("Generate Terrain");
                gen.DisplayStyle = ToolStripItemDisplayStyle.Text;
                gen.Font = new Font(bar.Font, FontStyle.Bold);
                gen.ToolTipText = "CAW Plus: random terrain (hills, rivers, coasts, lakes) from a seed";
                gen.Click += delegate { ShowTerrainGen(form); };
                bar.Items.Add(gen);
                ToolStripButton dt = new ToolStripButton("Distant Terrain");
                dt.DisplayStyle = ToolStripItemDisplayStyle.Text;
                dt.ToolTipText = "CAW Plus: make a custom distant terrain that carries on from this world's own edge (one of the last steps)";
                dt.Click += delegate { ShowDistant(form); };
                bar.Items.Add(dt);
                Log.Info("Auto-Paint, Generate Terrain and Distant Terrain buttons added to toolbar '" + bar.Name + "'");
            }

            // Self-test goes on the Utilities toolbar (always shown); falls back to the Terrain toolbar.
            ToolStrip utilBar = Caw.GetField(form, "mUtilitiesToolBar") as ToolStrip ?? bar;
            if (utilBar != null)
            {
                utilBar.Items.Add(new ToolStripSeparator());
                ToolStripButton st = new ToolStripButton("CAW Plus self-test");
                st.DisplayStyle = ToolStripItemDisplayStyle.Text;
                st.ToolTipText = "CAW Plus: check every CAW call the tools use, in the open world (undone afterwards)";
                st.Click += delegate
                {
                    if (BusyElsewhere(null)) return;
                    try { SelfTest.Run(form); }
                    catch (Exception ex) { Log.Error("Self-test failed", ex); MessageBox.Show(form, "Self-test failed: " + ex.Message, "CAW Plus"); }
                };
                utilBar.Items.Add(st);
                ToolStripButton wz = new ToolStripButton("Create World");
                wz.DisplayStyle = ToolStripItemDisplayStyle.Text; wz.Font = new Font(wz.Font, FontStyle.Bold);
                wz.ToolTipText = "CAW Plus: step by step from blank land to a finished world (terrain, roads, lots, paths, paint, trees, spawners, distant terrain)";
                wz.Click += delegate { ShowWizard(form); };
                utilBar.Items.Add(wz);
                Log.Info("Self-test button added to toolbar '" + utilBar.Name + "'");
            }

            // Auto-Place goes with CAW's tree tools; falls back to the Terrain toolbar.
            ToolStrip placeBar = Caw.GetField(form, "mRoadsAndTreesToolBar") as ToolStrip ?? bar;
            if (placeBar != null)
            {
                placeBar.Items.Add(new ToolStripSeparator());
                ToolStripButton pb = new ToolStripButton("Auto-Place");
                pb.DisplayStyle = ToolStripItemDisplayStyle.Text;
                pb.Font = new Font(placeBar.Font, FontStyle.Bold);
                pb.ToolTipText = "CAW Plus: place trees, rocks, bushes, road signs and streetlights across the world";
                pb.Click += delegate { ShowAutoPlace(form); };
                placeBar.Items.Add(pb);
                ToolStripButton sb = new ToolStripButton("Spawners");
                sb.DisplayStyle = ToolStripItemDisplayStyle.Text;
                sb.ToolTipText = "CAW Plus: place fishing spots, seeds, gems, metals, meteorites, butterflies and bugs the way EA's worlds do";
                sb.Click += delegate { ShowSpawners(form); };
                placeBar.Items.Add(sb);
                ToolStripButton rb = new ToolStripButton("Generate Roads");
                rb.DisplayStyle = ToolStripItemDisplayStyle.Text;
                rb.Font = new Font(placeBar.Font, FontStyle.Bold);
                rb.ToolTipText = "CAW Plus: plan roads and lots over the world's land the way the world type has them, then build them";
                rb.Click += delegate { ShowRoads(form); };
                placeBar.Items.Add(rb);
                ToolStripButton fb = new ToolStripButton("Footpaths");
                fb.DisplayStyle = ToolStripItemDisplayStyle.Text;
                fb.ToolTipText = "CAW Plus: footpaths round the roads already built, the way the world type has them";
                fb.Click += delegate { ShowFootpaths(form); };
                placeBar.Items.Add(fb);
                Log.Info("Auto-Place, Spawners, Generate Roads and Footpaths buttons added to toolbar '" + placeBar.Name + "'");
            }

            return bar;
        }

        private static void Stop()
        {
            done = true;
        }

        private static void OnTick(object sender, EventArgs e)
        {
            if (done) return;
            attempts++;
            try
            {
                Form form = Caw.MainForm as Form;
                if (form == null || form.IsDisposed) return;

                Stop();
                HookShutdown(form);

                // CAW Plus tab after Terrain, tools in the order a world is made (#138); else CAW's own toolbars
                ToolStrip bar = CawLook.AddTab(form);
                if (bar != null) AddTabButtons(form, bar);
                else bar = AddToCawToolbars(form);

                if (bar == null)
                {
                    // Fallback: small floating launcher window so the tool is still reachable.
                    Log.Info("Terrain toolbar not found; showing floating launcher");
                    Form launcher = new Form();
                    launcher.Text = "CAW Plus";
                    launcher.FormBorderStyle = FormBorderStyle.FixedToolWindow;
                    launcher.ClientSize = new Size(160, 40);
                    launcher.StartPosition = FormStartPosition.Manual;
                    launcher.Location = new Point(form.Left + 40, form.Top + 120);
                    Button b = new Button();
                    b.Text = "Auto-Paint";
                    b.Dock = DockStyle.Fill;
                    b.Click += delegate { ShowAutoPaint(form); };
                    launcher.Controls.Add(b);
                    Button b2 = new Button(); b2.Text = "Auto-Place"; b2.Dock = DockStyle.Bottom;
                    b2.Click += delegate { ShowAutoPlace(form); };
                    launcher.ClientSize = new Size(160, 70);
                    launcher.Controls.Add(b2);
                    launcher.Show(form);
                }
            }
            catch (Exception ex)
            {
                Log.Error("UI injection failed (attempt " + attempts + ")", ex);
                if (attempts > 20) Stop();
            }
        }

        /// <summary>
        /// Logs each step of CAW's shutdown, and lets go of everything CAW Plus holds before CAW's engine shuts down,
        /// so no wrapper of a native CAW object is finalised after the engine is gone.
        /// </summary>
        private static void HookShutdown(Form form)
        {
            form.FormClosing += delegate (object s, FormClosingEventArgs e)
            {
                Log.Info("Exit: main window closing (reason " + e.CloseReason + ", cancel " + e.Cancel + ")");
                if (e.Cancel) return;
                try
                {
                    if (autoPaintForm != null && !autoPaintForm.IsDisposed) { autoPaintForm.Close(); autoPaintForm.Dispose(); }
                    if (autoPlaceForm != null && !autoPlaceForm.IsDisposed) { autoPlaceForm.Close(); autoPlaceForm.Dispose(); }
                    if (spawnerForm != null && !spawnerForm.IsDisposed) { spawnerForm.Close(); spawnerForm.Dispose(); }
                    if (footpathForm != null && !footpathForm.IsDisposed) { footpathForm.Close(); footpathForm.Dispose(); }
                    if (roadsForm != null && !roadsForm.IsDisposed) { roadsForm.Close(); roadsForm.Dispose(); }
                    if (terrainGenForm != null && !terrainGenForm.IsDisposed) { terrainGenForm.Close(); terrainGenForm.Dispose(); }
                    if (distantForm != null && !distantForm.IsDisposed) { distantForm.Close(); distantForm.Dispose(); }
                    distantForm = null;
                    if (wizardForm != null && !wizardForm.IsDisposed) { wizardForm.Close(); wizardForm.Dispose(); }
                    wizardForm = null;
                    autoPaintForm = null; autoPlaceForm = null; spawnerForm = null; terrainGenForm = null; footpathForm = null; roadsForm = null;
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    Log.Info("Exit: CAW Plus windows closed and released");
                }
                catch (Exception ex) { Log.Error("Exit: releasing CAW Plus windows failed", ex); }
            };
            form.FormClosed += delegate { Log.Info("Exit: main window closed"); };
            form.Disposed += delegate { Log.Info("Exit: main window disposed (CAW shutdown finished)"); };
            Application.ApplicationExit += delegate { Log.Info("Exit: application exit"); };
        }

        private static void ShowTerrainGen(Form owner)
        {
            try
            {
                if (Caw.CurrentWorld == null)
                {
                    MessageBox.Show(owner, "Open a world first (File > New for a blank one).", "Generate Terrain", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (terrainGenForm == null || terrainGenForm.IsDisposed) { terrainGenForm = new TerrainGenForm(); terrainGenForm.Show(owner); }
                else terrainGenForm.Activate();
            }
            catch (Exception ex)
            {
                Log.Error("Opening Generate Terrain failed", ex);
                MessageBox.Show(owner, "Generate Terrain failed to open:\n" + ex.Message, "Generate Terrain", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void ShowAutoPlace(Form owner)
        {
            try
            {
                if (Caw.CurrentWorld == null)
                {
                    MessageBox.Show(owner, "Open or create a world first.", "Auto-Place", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (autoPlaceForm == null || autoPlaceForm.IsDisposed)
                {
                    autoPlaceForm = new AutoPlaceForm();
                    autoPlaceForm.Show(owner);
                }
                else autoPlaceForm.Activate();
            }
            catch (Exception ex)
            {
                Log.Error("Opening Auto-Place failed", ex);
                MessageBox.Show(owner, "Auto-Place failed to open:\n" + ex.Message, "Auto-Place", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void ShowDistant(Form owner)
        {
            try
            {
                if (Caw.CurrentWorld == null)
                {
                    MessageBox.Show(owner, "Open or create a world first.", "Distant Terrain", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (distantForm == null || distantForm.IsDisposed) { distantForm = new DistantTerrainForm(); distantForm.Show(owner); }
                else distantForm.Activate();
            }
            catch (Exception ex)
            {
                Log.Error("Opening Distant Terrain failed", ex);
                MessageBox.Show(owner, "Distant Terrain failed to open:\n" + ex.Message, "Distant Terrain", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void ShowRoads(Form owner)
        {
            try
            {
                if (Caw.CurrentWorld == null)
                {
                    MessageBox.Show(owner, "Open or create a world first.", "Generate Roads", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (roadsForm == null || roadsForm.IsDisposed) { roadsForm = new RoadsForm(); roadsForm.Show(owner); }
                else roadsForm.Activate();
            }
            catch (Exception ex)
            {
                Log.Error("Opening Generate Roads failed", ex);
                MessageBox.Show(owner, "Generate Roads failed to open:\n" + ex.Message, "Generate Roads", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void ShowFootpaths(Form owner)
        {
            try
            {
                if (Caw.CurrentWorld == null)
                {
                    MessageBox.Show(owner, "Open or create a world first.", "Footpaths", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (footpathForm == null || footpathForm.IsDisposed)
                {
                    footpathForm = new FootpathForm();
                    footpathForm.Show(owner);
                }
                else footpathForm.Activate();
            }
            catch (Exception ex)
            {
                Log.Error("Opening Footpaths failed", ex);
                MessageBox.Show(owner, "Footpaths failed to open:\n" + ex.Message, "Footpaths", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void ShowSpawners(Form owner)
        {
            try
            {
                if (Caw.CurrentWorld == null)
                {
                    MessageBox.Show(owner, "Open or create a world first.", "Spawners", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (spawnerForm == null || spawnerForm.IsDisposed)
                {
                    spawnerForm = new SpawnerForm();
                    spawnerForm.Show(owner);
                }
                else spawnerForm.Activate();
            }
            catch (Exception ex)
            {
                Log.Error("Opening Spawners failed", ex);
                MessageBox.Show(owner, "Spawners failed to open:\n" + ex.Message, "Spawners", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void ShowAutoPaint(Form owner)
        {
            try
            {
                if (Caw.CurrentWorld == null)
                {
                    MessageBox.Show(owner, "Open or create a world first.", "Auto-Paint", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (autoPaintForm == null || autoPaintForm.IsDisposed)
                {
                    autoPaintForm = new AutoPaintForm();
                    autoPaintForm.Show(owner);
                }
                else
                {
                    autoPaintForm.RefreshLayers();
                    autoPaintForm.Activate();
                }
            }
            catch (Exception ex)
            {
                Log.Error("Opening Auto-Paint failed", ex);
                MessageBox.Show(owner, "Auto-Paint failed to open:\n" + ex.Message, "Auto-Paint", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    /// <summary>Another world was opened in CAW while a CAW Plus action was running; the action stopped.</summary>
    internal sealed class WorldChangedException : Exception
    {
        public WorldChangedException()
            : base("Another world was opened while this was running, so CAW Plus stopped; nothing more was written to either world.") { }
    }
}
