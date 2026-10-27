using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CawPlus
{
    /// <summary>
    /// Create World (Utilities toolbar, #17): the steps from blank land to a finished world, in order. Each step opens
    /// the CAW Plus tool that does it and ticks itself off when that tool reports success (Injector.Done); steps can be
    /// skipped or marked done by hand. Progress is kept per world (Documents\CAWPlus\Worlds\&lt;world&gt;\wizard.txt).
    /// </summary>
    public sealed class WizardForm : Form
    {
        private sealed class Step
        {
            public string Key, Title, Tool, ToolName, Text;
            public Step(string key, string title, string tool, string toolName, string text) { Key = key; Title = title; Tool = tool; ToolName = toolName; Text = text; }
        }

        private static readonly Step[] Steps =
        {
            new Step("terrain", "Terrain", "terrain", "Generate Terrain",
                "Choose a World type (or Any EA world) and a seed, look at the preview, then click Apply terrain.\n\n" +
                "Skip this if your world already has the land you want."),
            new Step("roads", "Roads", "roads", "Generate Roads",
                "Generate Roads plans roads for the world type over your land. Try New layout until you like it, choose a Road look, " +
                "then click Build roads."),
            new Step("lots", "Lots", "roads", "Generate Roads",
                "In Generate Roads, click Place lots. Each lot lines up with the road CAW built; hillside lots get level terraces."),
            new Step("paths", "Footpaths", "paths", "Footpaths",
                "Footpaths plans walking paths the way the world type has them (riverside walks, trails up the hills), then builds them."),
            new Step("paint", "Paint", "paint", "Auto-Paint",
                "Auto-Paint: click Read terrain, check the textures it picked for the world type's style, then Paint world."),
            new Step("steep", "Steep ground (optional)", "paint", "Auto-Paint",
                "In Auto-Paint, Where Sims can walk: Block steep ground marks cliffs and mountain sides so Sims don't walk up them, " +
                "as in EA's worlds. Optional."),
            new Step("objects", "Trees, rocks, signs, lights", "objects", "Auto-Place",
                "Auto-Place: click Read world, check the preview (trees, rocks, road signs, streetlights as the world type has them), " +
                "then Place."),
            new Step("spawners", "Spawners", "spawners", "Spawners",
                "Spawners: click Read world, then Place (fishing spots, seeds, gems, metals, insects...)."),
            new Step("distant-make", "Distant terrain", "distant", "Distant Terrain",
                "Distant Terrain: click Make distant terrain. Then save the world (File > Save), close CAW and start it again: " +
                "CAW loads the new object when it starts."),
            new Step("distant-place", "Place the distant terrain", "distant", "Distant Terrain",
                "After restarting CAW: open this world, open Create World again and click the button below, then Place in world."),
            new Step("save", "Save", null, null,
                "Save the world (File > Save) and export it when you're happy with it. For the game, also copy the distant terrain " +
                ".package into Documents\\Electronic Arts\\The Sims 3\\Mods\\Packages.\n\nThat's every step."),
        };

        private readonly Dictionary<string, string> state = new Dictionary<string, string>(); // key -> "done" | "skipped"
        private ListBox list;
        private Label title, text, progressLabel;
        private Button btnOpen, btnDone, btnSkip, btnBack, btnNext;
        private int current;
        private string worldKey;

        public WizardForm(Form owner)
        {
            SuspendLayout();
            Text = "Create World  (CAW Plus)";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(640, 360);
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 8.25f);

            list = new ListBox(); list.SetBounds(10, 10, 210, 300); list.IntegralHeight = false;
            list.SelectedIndexChanged += delegate { if (list.SelectedIndex >= 0 && list.SelectedIndex != current) { current = list.SelectedIndex; ShowStep(); } };
            Controls.Add(list);
            progressLabel = new Label(); progressLabel.SetBounds(10, 316, 210, 34); Controls.Add(progressLabel);

            title = new Label(); title.SetBounds(236, 10, 394, 26); title.Font = new Font("Segoe UI", 12f, FontStyle.Bold); Controls.Add(title);
            text = new Label(); text.SetBounds(236, 42, 394, 150); Controls.Add(text);

            btnOpen = new Button(); btnOpen.SetBounds(236, 200, 220, 30); btnOpen.Font = new Font(Font, FontStyle.Bold);
            btnOpen.Click += delegate { Step s = Steps[current]; if (s.Tool != null) Injector.Open(s.Tool, owner); else Mark("done"); };
            Controls.Add(btnOpen);
            btnDone = new Button(); btnDone.Text = "Mark done"; btnDone.SetBounds(236, 238, 106, 26);
            btnDone.Click += delegate { Mark("done"); };
            new ToolTip().SetToolTip(btnDone, "Tick this step off yourself (e.g. you did it with CAW's own tools).");
            btnSkip = new Button(); btnSkip.Text = "Skip"; btnSkip.SetBounds(350, 238, 106, 26);
            btnSkip.Click += delegate { Mark("skipped"); };
            Controls.Add(btnDone); Controls.Add(btnSkip);

            btnBack = new Button(); btnBack.Text = "< Back"; btnBack.SetBounds(450, 320, 86, 28);
            btnBack.Click += delegate { if (current > 0) { current--; ShowStep(); } };
            btnNext = new Button(); btnNext.Text = "Next >"; btnNext.SetBounds(544, 320, 86, 28);
            btnNext.Click += delegate { if (current < Steps.Length - 1) { current++; ShowStep(); } };
            Controls.Add(btnBack); Controls.Add(btnNext);

            Injector.StepHandler handler = OnStepDone;
            Injector.StepDone += handler;
            FormClosed += delegate { Injector.StepDone -= handler; };
            Activated += delegate { CheckWorld(); };

            LoadState();
            current = FirstOpen(0);
            ShowStep();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            Theme.Apply(this);
        }

        private void OnStepDone(string key)
        {
            if (IsDisposed) return;
            CheckWorld();
            state[key] = "done";
            Save();
            if (Steps[current].Key == key) current = FirstOpen(current);
            ShowStep();
        }

        private void Mark(string how)
        {
            state[Steps[current].Key] = how;
            Save();
            current = FirstOpen(current);
            ShowStep();
        }

        /// <summary>The first step from 'from' on (then from the start) that is neither done nor skipped; else the last.</summary>
        private int FirstOpen(int from)
        {
            for (int k = 0; k < Steps.Length; k++)
            {
                int i = (from + k) % Steps.Length;
                if (!state.ContainsKey(Steps[i].Key)) return i;
            }
            return Steps.Length - 1;
        }

        private void ShowStep()
        {
            Step s = Steps[current];
            title.Text = (current + 1) + ". " + s.Title;
            string st; state.TryGetValue(s.Key, out st);
            text.Text = s.Text + (st == "done" ? "\n\nDone." : st == "skipped" ? "\n\nSkipped." : "");
            btnOpen.Text = s.Tool != null ? "Open " + s.ToolName : "Finish";
            btnBack.Enabled = current > 0; btnNext.Enabled = current < Steps.Length - 1;
            list.BeginUpdate();
            list.Items.Clear();
            int done = 0;
            for (int i = 0; i < Steps.Length; i++)
            {
                string m; state.TryGetValue(Steps[i].Key, out m);
                if (m == "done") done++;
                list.Items.Add((m == "done" ? "✓ " : m == "skipped" ? "– " : "    ") + (i + 1) + ". " + Steps[i].Title);
            }
            list.SelectedIndex = current;
            list.EndUpdate();
            progressLabel.Text = done + " of " + Steps.Length + " steps done. A step ticks itself off when its tool finishes.";
        }

        // ---------------- progress per world ----------------

        private string StateFile()
        {
            object world = Caw.CurrentWorld;
            if (world == null) return null;
            return Path.Combine(Caw.WorldDir(Path.Combine(Log.Dir, "Worlds"), world), "wizard.txt");
        }

        private void CheckWorld()
        {
            object world = Caw.CurrentWorld;
            string key = world == null ? null : Caw.WorldKey(world);
            if (key == worldKey) return;
            LoadState();
            current = FirstOpen(0);
            ShowStep();
        }

        private void LoadState()
        {
            state.Clear();
            object world = Caw.CurrentWorld;
            worldKey = world == null ? null : Caw.WorldKey(world);
            try
            {
                string f = StateFile();
                if (f == null || !File.Exists(f)) return;
                foreach (string line in File.ReadAllLines(f))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) state[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch (Exception ex) { Log.Error("Create World: reading progress failed", ex); }
        }

        private void Save()
        {
            try
            {
                string f = StateFile();
                if (f == null) return;
                List<string> lines = new List<string>();
                foreach (KeyValuePair<string, string> kv in state) lines.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(f, lines.ToArray());
            }
            catch (Exception ex) { Log.Error("Create World: saving progress failed", ex); }
        }
    }
}
