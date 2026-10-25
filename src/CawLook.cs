using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace CawPlus
{
    /// <summary>
    /// CAW's own window: the "CAW Plus" tab in CAW's tool tab strip, and an optional dark look for CAW (#138).
    /// Everything by reflection on CAW's live controls; CAW's files are not changed. The 3D view is never touched.
    /// </summary>
    internal static class CawLook
    {
        // ---------- CAW Plus tab ----------

        /// <summary>Adds a "CAW Plus" tab after CAW's last tool tab (Terrain) holding an empty toolbar styled like CAW's;
        /// null when CAW's tab strip isn't as expected (the caller falls back to CAW's own toolbars).</summary>
        public static ToolStrip AddTab(Form form)
        {
            try
            {
                object tabs = Caw.GetField(form, "mToolsTabPanel");
                Control terrainTab = Caw.GetField(form, "mTerrainTab") as Control;
                ToolStrip refBar = Caw.GetField(form, "mTerrainToolBar") as ToolStrip;
                if (tabs == null || terrainTab == null || refBar == null) return null;
                object pages = tabs.GetType().GetProperty("TabPages").GetValue(tabs, null);
                Control page = Activator.CreateInstance(terrainTab.GetType(), new object[] { "CAW Plus" }) as Control;
                if (pages == null || page == null) return null;
                page.Name = "mCawPlusTab";

                ToolStrip bar = new ToolStrip();
                bar.Name = "mCawPlusToolBar";
                bar.Dock = refBar.Dock;
                bar.GripStyle = ToolStripGripStyle.Hidden;
                bar.BackColor = refBar.BackColor;
                bar.Font = refBar.Font;
                bar.ImageScalingSize = refBar.ImageScalingSize;
                bar.AutoSize = refBar.AutoSize;
                bar.Size = refBar.Size;
                bar.Padding = refBar.Padding;
                bar.Location = refBar.Location;
                page.Controls.Add(bar);

                MethodInfo add = pages.GetType().GetMethod("Add", new Type[] { terrainTab.GetType() });
                if (add == null) return null;
                add.Invoke(pages, new object[] { page });
                Log.Info("CAW Plus tab added after '" + Caw.GetProperty(terrainTab, "Title") + "'");
                return bar;
            }
            catch (Exception ex) { Log.Error("CAW Plus tab", ex); return null; }
        }

        /// <summary>A tall text button in the CAW Plus tab (the same height as CAW's picture buttons).</summary>
        public static ToolStripButton Button(ToolStrip bar, string text, string tip, bool main, EventHandler click)
        {
            ToolStripButton b = new ToolStripButton(text);
            b.DisplayStyle = ToolStripItemDisplayStyle.Text;
            if (main) b.Font = new Font(bar.Font, FontStyle.Bold);
            b.ToolTipText = tip;
            b.AutoSize = true;
            b.Padding = new Padding(8, 0, 8, 0);
            int h = bar.ImageScalingSize.Height + 8;
            b.Margin = new Padding(1, Math.Max(1, (bar.Height - h) / 2), 1, 1);
            if (click != null) b.Click += click;
            bar.Items.Add(b);
            return b;
        }

        // ---------- dark mode for CAW's window ----------

        private static readonly Color Back = Color.FromArgb(37, 38, 42);
        private static readonly Color Panel = Color.FromArgb(43, 45, 49);
        private static readonly Color Bar = Color.FromArgb(32, 33, 36);
        private static readonly Color Input = Color.FromArgb(28, 29, 32);
        private static readonly Color Line = Color.FromArgb(62, 65, 71);
        private static readonly Color Text = Color.FromArgb(225, 227, 230);
        private static readonly Color Muted = Color.FromArgb(160, 164, 171);
        private static readonly Color Select = Color.FromArgb(59, 130, 246);

        private static bool dark;
        private static bool hooked;
        // what was changed, so turning it off puts CAW's look back exactly
        private static readonly Dictionary<object, Dictionary<string, object>> saved = new Dictionary<object, Dictionary<string, object>>();
        private static Form main;

        public static bool IsDark { get { return dark; } }

        private static string SettingsFile { get { return Path.Combine(Log.Dir, "settings.txt"); } }

        /// <summary>The remembered choice (Documents\CAWPlus\settings.txt, "caw_dark=1").</summary>
        public static bool Remembered()
        {
            try
            {
                if (!File.Exists(SettingsFile)) return false;
                foreach (string l in File.ReadAllLines(SettingsFile)) if (l.Trim() == "caw_dark=1") return true;
            }
            catch { }
            return false;
        }

        private static void Remember(bool on)
        {
            try
            {
                List<string> lines = new List<string>();
                if (File.Exists(SettingsFile))
                    foreach (string l in File.ReadAllLines(SettingsFile)) if (!l.StartsWith("caw_dark=")) lines.Add(l);
                lines.Add("caw_dark=" + (on ? "1" : "0"));
                Directory.CreateDirectory(Log.Dir);
                File.WriteAllLines(SettingsFile, lines.ToArray());
            }
            catch { }
        }

        /// <summary>Turns CAW's dark look on or off (and remembers it).</summary>
        public static void SetDark(Form form, bool on, bool remember)
        {
            main = form;
            if (remember) Remember(on);
            if (on == dark) return;
            int changed = 0;
            try
            {
                form.SuspendLayout();
                if (on)
                {
                    dark = true;
                    Walk(form, ref changed);
                    DockContents(form, ref changed);
                    Magic(Caw.GetField(form, "mDockMgr"), ref changed);
                    Theme.DarkTitle(form, true);
                    if (!hooked) { hooked = true; Hook(form); }
                }
                else
                {
                    dark = false;
                    foreach (KeyValuePair<object, Dictionary<string, object>> kv in saved)
                        foreach (KeyValuePair<string, object> p in kv.Value) { Put(kv.Key, p.Key, p.Value); changed++; }
                    saved.Clear();
                    Theme.DarkTitle(form, false);
                }
            }
            catch (Exception ex) { Log.Error("CAW dark mode", ex); }
            finally { try { form.ResumeLayout(true); form.Invalidate(true); } catch { } }
            Log.Info("CAW dark mode " + (on ? "on" : "off") + " (" + changed + " settings)");
        }

        // controls added later (CAW's tool panels open and close, floating windows dock) get the look too
        private static void Hook(Control c)
        {
            c.ControlAdded += delegate (object s, ControlEventArgs e)
            {
                if (!dark || Skip(e.Control)) return;
                try { int n = 0; Walk(e.Control, ref n); } catch { }
            };
            foreach (Control k in c.Controls) if (!Skip(k)) Hook(k);
        }

        // the 3D view and its children keep CAW's own drawing
        private static bool Skip(Control c)
        {
            string n = c.GetType().Name;
            return n.IndexOf("RenderPanel", StringComparison.Ordinal) >= 0;
        }

        private static void DockContents(Form form, ref int changed)
        {
            // contents that are hidden right now aren't in the window's control tree
            object mgr = Caw.GetField(form, "mDockMgr");
            if (mgr == null) return;
            IEnumerable contents = Caw.GetProperty(mgr, "Contents") as IEnumerable;
            if (contents == null) return;
            foreach (object ct in contents)
            {
                Control c = Caw.GetProperty(ct, "Control") as Control;
                if (c != null && !Skip(c)) Walk(c, ref changed);
            }
        }

        private static void Walk(Control c, ref int changed)
        {
            if (Skip(c)) return;
            Style(c, ref changed);
            foreach (Control k in c.Controls) Walk(k, ref changed);
        }

        private static bool Light(Color c) { return c.A > 0 && c.GetBrightness() >= 0.55f; }
        private static bool DarkText(Color c) { return c.GetBrightness() < 0.5f; }

        private static void Style(Control c, ref int changed)
        {
            if (c is ToolStrip)
            {
                ToolStrip ts = (ToolStrip)c;
                Set(ts, "Renderer", new ToolStripProfessionalRenderer(new DarkTable()), ref changed);
                Set(ts, "BackColor", ts is MenuStrip || ts is StatusStrip ? Bar : Panel, ref changed);
                Set(ts, "ForeColor", Text, ref changed);
                Items(ts.Items, ref changed);
                return;
            }
            if (c is PropertyGrid)
            {
                PropertyGrid g = (PropertyGrid)c;
                Set(g, "BackColor", Back, ref changed);
                Set(g, "ViewBackColor", Input, ref changed);
                Set(g, "ViewForeColor", Text, ref changed);
                Set(g, "LineColor", Line, ref changed);
                Set(g, "CategoryForeColor", Muted, ref changed);
                Set(g, "HelpBackColor", Panel, ref changed);
                Set(g, "HelpForeColor", Text, ref changed);
                Set(g, "CommandsBackColor", Panel, ref changed);
                Set(g, "CommandsForeColor", Text, ref changed);
                return;
            }
            if (c.GetType().Namespace != null && c.GetType().Namespace.StartsWith("Crownwood")) Magic(c, ref changed);

            bool input = c is TextBoxBase || c is ListBox || c is TreeView || c is ListView || c is ComboBox
                || c is NumericUpDown || c.GetType().Name.IndexOf("Tree", StringComparison.Ordinal) >= 0;
            if (Light(c.BackColor)) Set(c, "BackColor", input ? Input : (c is Form || c is SplitContainer ? Back : Panel), ref changed);
            if (DarkText(c.ForeColor)) Set(c, "ForeColor", Text, ref changed);
            if (c is ComboBox && ((ComboBox)c).FlatStyle == FlatStyle.Standard) Set(c, "FlatStyle", FlatStyle.Flat, ref changed);
            if (c is Button && ((Button)c).FlatStyle == FlatStyle.Standard)
            {
                Set(c, "FlatStyle", FlatStyle.Flat, ref changed);
                ((Button)c).FlatAppearance.BorderColor = Line;
            }
        }

        private static void Items(ToolStripItemCollection items, ref int changed)
        {
            foreach (ToolStripItem it in items)
            {
                if (DarkText(it.ForeColor)) Set(it, "ForeColor", Text, ref changed);
                if (Light(it.BackColor) && it.BackColor != SystemColors.Control) Set(it, "BackColor", Panel, ref changed);
                ToolStripDropDownItem dd = it as ToolStripDropDownItem;
                if (dd != null && dd.HasDropDownItems)
                {
                    Set(dd.DropDown, "Renderer", new ToolStripProfessionalRenderer(new DarkTable()), ref changed);
                    Set(dd.DropDown, "BackColor", Bar, ref changed);
                    Items(dd.DropDownItems, ref changed);
                }
            }
        }

        // DotNetMagic tab strips, docking manager, status bar, tree control: their own colour properties
        private static void Magic(object o, ref int changed)
        {
            if (o == null) return;
            string[] backs = { "BackColor", "ButtonInactiveColor", "ResizeBarColor", "InactiveBackColor", "BackColor2" };
            string[] texts = { "ForeColor", "TextColor", "TextInactiveColor", "InactiveTextColor", "SelectTextColor" };
            foreach (string p in backs) { Color v; if (TryColor(o, p, out v) && (v.IsEmpty || Light(v))) Set(o, p, p == "ResizeBarColor" ? Line : Back, ref changed); }
            foreach (string p in texts) { Color v; if (TryColor(o, p, out v) && (v.IsEmpty || DarkText(v))) Set(o, p, p == "TextInactiveColor" ? Muted : Text, ref changed); }
            // (the docking panels' title text, InactiveTextColor, in the full text colour: muted grey was unreadable)
            Color a;
            if (TryColor(o, "ButtonActiveColor", out a)) Set(o, "ButtonActiveColor", Text, ref changed);
            if (TryColor(o, "HotTextColor", out a)) Set(o, "HotTextColor", Color.White, ref changed);
            if (TryColor(o, "ActiveColor", out a)) Set(o, "ActiveColor", Select, ref changed);
            if (TryColor(o, "ActiveTextColor", out a)) Set(o, "ActiveTextColor", Color.White, ref changed);
            if (TryColor(o, "SelectBackColor", out a)) Set(o, "SelectBackColor", Panel, ref changed);
            if (TryColor(o, "SelectTextBackColor", out a)) Set(o, "SelectTextBackColor", Panel, ref changed);
            if (TryColor(o, "InactiveTextBackColor", out a)) Set(o, "InactiveTextBackColor", Back, ref changed);
            // the tab pages of a tab strip
            IEnumerable pages = SafeProperty(o, "TabPages") as IEnumerable;
            if (pages != null) foreach (object p in pages) Magic(p, ref changed);
        }

        private static object SafeProperty(object o, string name)
        {
            try
            {
                PropertyInfo p = o.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                return p != null && p.GetIndexParameters().Length == 0 ? p.GetValue(o, null) : null;
            }
            catch { return null; }
        }

        private static bool TryColor(object o, string name, out Color c)
        {
            c = Color.Empty;
            object v = SafeProperty(o, name);
            if (!(v is Color)) return false;
            c = (Color)v;
            return true;
        }

        private static void Set(object o, string name, object value, ref int changed)
        {
            try
            {
                PropertyInfo p = o.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                if (p == null || !p.CanWrite) return;
                Dictionary<string, object> d;
                if (!saved.TryGetValue(o, out d)) { d = new Dictionary<string, object>(); saved[o] = d; }
                // a colour the control only inherits from its parent is remembered as "inherited", not as its value:
                // the parent may already be dark, and putting that value back left white text on CAW's light menu and
                // toolbars after switching off (the tester's v0.62 test)
                if (!d.ContainsKey(name)) d[name] = Inherited(o, name) ? (object)Ambient : p.GetValue(o, null);
                p.SetValue(o, value, null);
                changed++;
            }
            catch { }
        }

        private static void Put(object o, string name, object value)
        {
            try
            {
                if (value == Ambient)
                {
                    // back to inheriting from the parent (Control.ResetForeColor, ToolStripItem.ResetBackColor...)
                    MethodInfo reset = FindMethod(o.GetType(), "Reset" + name);
                    if (reset != null) reset.Invoke(o, null);
                    return;
                }
                PropertyInfo p = o.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                if (p != null && p.CanWrite) p.SetValue(o, value, null);
            }
            catch { }
        }

        private static readonly object Ambient = new object();

        /// <summary>True when the property isn't set on the object itself (WinForms' ShouldSerialize... says false) and
        /// it has a Reset... to go back to inheriting.</summary>
        private static bool Inherited(object o, string name)
        {
            try
            {
                MethodInfo should = FindMethod(o.GetType(), "ShouldSerialize" + name);
                if (should == null || should.ReturnType != typeof(bool) || FindMethod(o.GetType(), "Reset" + name) == null) return false;
                return !(bool)should.Invoke(o, null);
            }
            catch { return false; }
        }

        /// <summary>A parameterless instance method by name, private ones of base classes included.</summary>
        private static MethodInfo FindMethod(Type t, string name)
        {
            for (; t != null; t = t.BaseType)
            {
                MethodInfo m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (m != null) return m;
            }
            return null;
        }

        /// <summary>Colours for menus, toolbars and the status strip.</summary>
        private sealed class DarkTable : ProfessionalColorTable
        {
            public DarkTable() { UseSystemColors = false; }
            public override Color ToolStripGradientBegin { get { return Panel; } }
            public override Color ToolStripGradientMiddle { get { return Panel; } }
            public override Color ToolStripGradientEnd { get { return Panel; } }
            public override Color ToolStripBorder { get { return Bar; } }
            public override Color ToolStripDropDownBackground { get { return Bar; } }
            public override Color MenuStripGradientBegin { get { return Bar; } }
            public override Color MenuStripGradientEnd { get { return Bar; } }
            public override Color StatusStripGradientBegin { get { return Bar; } }
            public override Color StatusStripGradientEnd { get { return Bar; } }
            public override Color MenuBorder { get { return Line; } }
            public override Color MenuItemBorder { get { return Select; } }
            public override Color MenuItemSelected { get { return Color.FromArgb(55, 58, 64); } }
            public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(55, 58, 64); } }
            public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(55, 58, 64); } }
            public override Color MenuItemPressedGradientBegin { get { return Bar; } }
            public override Color MenuItemPressedGradientMiddle { get { return Bar; } }
            public override Color MenuItemPressedGradientEnd { get { return Bar; } }
            public override Color ImageMarginGradientBegin { get { return Bar; } }
            public override Color ImageMarginGradientMiddle { get { return Bar; } }
            public override Color ImageMarginGradientEnd { get { return Bar; } }
            public override Color SeparatorDark { get { return Line; } }
            public override Color SeparatorLight { get { return Bar; } }
            public override Color ButtonSelectedHighlight { get { return Color.FromArgb(62, 66, 73); } }
            public override Color ButtonSelectedGradientBegin { get { return Color.FromArgb(62, 66, 73); } }
            public override Color ButtonSelectedGradientMiddle { get { return Color.FromArgb(62, 66, 73); } }
            public override Color ButtonSelectedGradientEnd { get { return Color.FromArgb(62, 66, 73); } }
            public override Color ButtonSelectedBorder { get { return Select; } }
            public override Color ButtonPressedGradientBegin { get { return Color.FromArgb(37, 99, 235); } }
            public override Color ButtonPressedGradientMiddle { get { return Color.FromArgb(37, 99, 235); } }
            public override Color ButtonPressedGradientEnd { get { return Color.FromArgb(37, 99, 235); } }
            public override Color ButtonCheckedGradientBegin { get { return Color.FromArgb(40, 80, 150); } }
            public override Color ButtonCheckedGradientMiddle { get { return Color.FromArgb(40, 80, 150); } }
            public override Color ButtonCheckedGradientEnd { get { return Color.FromArgb(40, 80, 150); } }
            public override Color CheckBackground { get { return Color.FromArgb(40, 80, 150); } }
            public override Color GripDark { get { return Line; } }
            public override Color GripLight { get { return Bar; } }
            public override Color OverflowButtonGradientBegin { get { return Panel; } }
            public override Color OverflowButtonGradientMiddle { get { return Panel; } }
            public override Color OverflowButtonGradientEnd { get { return Panel; } }
        }
    }
}
