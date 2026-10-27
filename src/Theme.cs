using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CawPlus
{
    /// <summary>
    /// CAW Plus's dark look: dark window and title bar, cards for group boxes, rounded flat buttons (bold = accent),
    /// rounded check boxes, slim sliders and progress bars. Theme.Apply(form) at the end of a window's constructor
    /// restyles every control in it (and any added later); nothing else in the window has to change. Each piece falls
    /// back to the standard look if Windows doesn't support it (dark title bar: Windows 10 1809+).
    /// </summary>
    internal static class Theme
    {
        public static readonly Color Back = Color.FromArgb(30, 31, 34);
        public static readonly Color Card = Color.FromArgb(39, 41, 45);
        public static readonly Color CardBorder = Color.FromArgb(52, 55, 60);
        public static readonly Color Input = Color.FromArgb(24, 25, 28);
        public static readonly Color Text = Color.FromArgb(230, 231, 233);
        public static readonly Color Muted = Color.FromArgb(160, 164, 171);
        public static readonly Color Accent = Color.FromArgb(59, 130, 246);
        public static readonly Color AccentHover = Color.FromArgb(96, 155, 250);
        public static readonly Color AccentDown = Color.FromArgb(37, 99, 235);
        public static readonly Color Button = Color.FromArgb(52, 55, 61);
        public static readonly Color ButtonHover = Color.FromArgb(66, 70, 77);
        public static readonly Color ButtonDown = Color.FromArgb(44, 46, 51);
        public static readonly Color Preview = Color.FromArgb(17, 18, 20);
        public static readonly Color Disabled = Color.FromArgb(110, 114, 120);

        /// <summary>Restyles the window and everything in it.</summary>
        public static void Apply(Form f)
        {
            try
            {
                f.BackColor = Back;
                f.ForeColor = Text;
                if (f.IsHandleCreated) DarkTitle(f);
                else f.HandleCreated += delegate { DarkTitle(f); };
                Children(f);
            }
            catch (Exception ex) { Log.Error("Theme", ex); }
        }

        /// <summary>Applies the theme to a small dialog and shows it.</summary>
        public static DialogResult Dialog(Form f, IWin32Window owner)
        {
            Apply(f);
            return f.ShowDialog(owner);
        }

        private static void Children(Control parent)
        {
            foreach (Control c in parent.Controls) Style(c);
            parent.ControlAdded += delegate (object s, ControlEventArgs e) { try { Style(e.Control); } catch { } };
        }

        private static void Style(Control c)
        {
            if (c is Button) StyleButton((Button)c);
            else if (c is CheckBox) StyleCheck((CheckBox)c);
            else if (c is GroupBox) StyleGroup((GroupBox)c);
            else if (c is TrackBar) c.BackColor = c.Parent != null ? c.Parent.BackColor : Back;
            else if (c is ComboBox)
            {
                ComboBox cb = (ComboBox)c;
                cb.FlatStyle = FlatStyle.Flat; cb.BackColor = Input; cb.ForeColor = Text;
            }
            else if (c is TextBoxBase || c is ListBox || c is NumericUpDown || c is TreeView || c is ListView)
            {
                c.BackColor = Input; c.ForeColor = Text;
                if (c is TextBox) ((TextBox)c).BorderStyle = BorderStyle.FixedSingle;
                else if (c is ListBox) ((ListBox)c).BorderStyle = BorderStyle.FixedSingle;
                else if (c is NumericUpDown) ((NumericUpDown)c).BorderStyle = BorderStyle.FixedSingle;
                DarkScrollBars(c);
            }
            else if (c is PictureBox)
            {
                PictureBox p = (PictureBox)c;
                if (p.Dock == DockStyle.None) { p.BackColor = Preview; p.BorderStyle = BorderStyle.None; }
            }
            else if (c is Label)
            {
                // labels that were set grey for the light look become the muted light grey
                if (c.ForeColor.GetBrightness() < 0.5f) c.ForeColor = Muted;
            }
            if (c.Controls.Count > 0 && !(c is NumericUpDown) && !(c is ComboBox)) Children(c);
        }

        // ---------- title bar and scroll bars (Windows 10 1809+; ignored elsewhere) ----------

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        private static void DarkTitle(Form f) { DarkTitle(f, true); }

        /// <summary>Dark title bar on or off (also used for CAW's own window).</summary>
        internal static void DarkTitle(Form f, bool dark)
        {
            try
            {
                int on = dark ? 1 : 0;
                if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, 4); // dark mode (19 before Windows 10 20H1)
                int caption = dark ? Back.R | (Back.G << 8) | (Back.B << 16) : unchecked((int)0xFFFFFFFF); // DWMWA_COLOR_DEFAULT
                DwmSetWindowAttribute(f.Handle, 35, ref caption, 4); // caption colour (Windows 11)
                if (!dark) return;
                int round = 2;
                DwmSetWindowAttribute(f.Handle, 33, ref round, 4);   // rounded corners (Windows 11)
            }
            catch { }
        }

        private static void DarkScrollBars(Control c)
        {
            EventHandler set = delegate { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } };
            if (c.IsHandleCreated) set(c, EventArgs.Empty); else c.HandleCreated += set;
        }

        // ---------- shapes ----------

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private static Color ParentBack(Control c) { return c.Parent != null ? c.Parent.BackColor : Back; }

        // ---------- buttons ----------

        private static void StyleButton(Button b)
        {
            bool hover = false, down = false;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.ForeColor = Text;
            b.Cursor = Cursors.Hand;
            b.MouseEnter += delegate { hover = true; b.Invalidate(); };
            b.MouseLeave += delegate { hover = false; down = false; b.Invalidate(); };
            b.MouseDown += delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; b.Invalidate(); } };
            b.MouseUp += delegate { down = false; b.Invalidate(); };
            b.EnabledChanged += delegate { b.Invalidate(); };
            b.Paint += delegate (object s, PaintEventArgs e)
            {
                try
                {
                    bool primary = b.Font.Bold;
                    Color fill = primary ? (down ? AccentDown : hover ? AccentHover : Accent) : (down ? ButtonDown : hover ? ButtonHover : Button);
                    Color fore = Text;
                    if (!b.Enabled) { fill = primary ? Color.FromArgb(44, 62, 92) : Color.FromArgb(42, 44, 48); fore = Disabled; }
                    Graphics g = e.Graphics;
                    g.Clear(ParentBack(b));
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (GraphicsPath p = Rounded(new RectangleF(0.5f, 0.5f, b.Width - 1.5f, b.Height - 1.5f), 6))
                    {
                        using (SolidBrush br = new SolidBrush(fill)) g.FillPath(br, p);
                    }
                    if (b.Image != null)
                        g.DrawImage(b.Image, (b.Width - b.Image.Width) / 2, (b.Height - b.Image.Height) / 2);
                    TextRenderer.DrawText(g, b.Text, b.Font, new Rectangle(4, 0, b.Width - 8, b.Height), fore,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
                }
                catch { }
            };
        }

        // ---------- check boxes ----------

        private static void StyleCheck(CheckBox c)
        {
            bool hover = false;
            c.ForeColor = Text;
            c.MouseEnter += delegate { hover = true; c.Invalidate(); };
            c.MouseLeave += delegate { hover = false; c.Invalidate(); };
            c.EnabledChanged += delegate { c.Invalidate(); };
            c.Paint += delegate (object s, PaintEventArgs e)
            {
                try
                {
                    Graphics g = e.Graphics;
                    g.Clear(c.BackColor.A == 255 ? c.BackColor : ParentBack(c));
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    int top = c.Height > 26 ? 3 : (c.Height - 14) / 2;
                    RectangleF box = new RectangleF(0.5f, top + 0.5f, 13, 13);
                    bool on = c.CheckState != CheckState.Unchecked;
                    using (GraphicsPath p = Rounded(box, 3.5f))
                    {
                        if (on)
                            using (SolidBrush br = new SolidBrush(c.Enabled ? (hover ? AccentHover : Accent) : Disabled)) g.FillPath(br, p);
                        else
                        {
                            using (SolidBrush br = new SolidBrush(Input)) g.FillPath(br, p);
                            using (Pen pen = new Pen(c.Enabled ? (hover ? Accent : Color.FromArgb(110, 115, 123)) : Color.FromArgb(70, 73, 78), 1.2f)) g.DrawPath(pen, p);
                        }
                    }
                    if (c.CheckState == CheckState.Checked)
                        using (Pen pen = new Pen(Color.White, 1.8f))
                        {
                            pen.StartCap = pen.EndCap = LineCap.Round;
                            g.DrawLines(pen, new PointF[] { new PointF(3.6f, top + 7.2f), new PointF(6f, top + 9.6f), new PointF(10.4f, top + 4.4f) });
                        }
                    else if (c.CheckState == CheckState.Indeterminate)
                        using (Pen pen = new Pen(Color.White, 1.8f)) g.DrawLine(pen, 3.5f, top + 7f, 10.5f, top + 7f);
                    TextFormatFlags tf = TextFormatFlags.Left | TextFormatFlags.EndEllipsis | (c.Height > 26 ? TextFormatFlags.WordBreak | TextFormatFlags.Top : TextFormatFlags.VerticalCenter);
                    Rectangle tr = c.Height > 26 ? new Rectangle(16, 1, c.Width - 16, c.Height - 1) : new Rectangle(16, 0, c.Width - 16, c.Height);
                    TextRenderer.DrawText(g, c.Text, c.Font, tr, c.Enabled ? c.ForeColor : Disabled, tf);
                }
                catch { }
            };
        }

        // ---------- group boxes as cards ----------

        private static void StyleGroup(GroupBox gb)
        {
            gb.BackColor = Card;   // children without their own colour take it
            gb.ForeColor = Text;
            gb.Paint += delegate (object s, PaintEventArgs e)
            {
                try
                {
                    Graphics g = e.Graphics;
                    g.Clear(ParentBack(gb));
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (GraphicsPath p = Rounded(new RectangleF(0.5f, 0.5f, gb.Width - 1.5f, gb.Height - 1.5f), 8))
                    {
                        using (SolidBrush br = new SolidBrush(Card)) g.FillPath(br, p);
                        using (Pen pen = new Pen(CardBorder)) g.DrawPath(pen, p);
                    }
                    using (Font f = new Font(gb.Font, FontStyle.Bold))
                        TextRenderer.DrawText(g, gb.Text.ToUpperInvariant(), f, new Point(8, 2), Muted);
                }
                catch { }
            };
        }
    }

    /// <summary>A slim progress bar drawn in the theme's accent colour.</summary>
    internal sealed class ThemedProgress : ProgressBar
    {
        public ThemedProgress()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float h = Math.Min(6f, Height - 1f), y = (Height - h) / 2f;
            using (GraphicsPath p = Theme.Rounded(new RectangleF(0.5f, y, Width - 1.5f, h), h / 2))
            using (SolidBrush br = new SolidBrush(Theme.Input)) g.FillPath(br, p);
            float frac = Maximum > Minimum ? (Value - Minimum) / (float)(Maximum - Minimum) : 0f;
            float w = (Width - 1.5f) * frac;
            if (w >= 1f)
                using (GraphicsPath p = Theme.Rounded(new RectangleF(0.5f, y, Math.Max(w, h), h), h / 2))
                using (SolidBrush br = new SolidBrush(Theme.Accent)) g.FillPath(br, p);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x402 || m.Msg == 0x403 || m.Msg == 0x406 || m.Msg == 0x401) Invalidate(); // PBM_SETPOS, DELTAPOS, SETRANGE32, SETRANGE
        }
    }

    /// <summary>
    /// A TrackBar with a slim rounded track (filled in the accent colour up to the value) and a round thumb, drawn
    /// through the native control's custom-draw notification; tick marks are left out. Mouse and keyboard handling is
    /// the standard control's.
    /// </summary>
    internal sealed class ThemedSlider : TrackBar
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NMCUSTOMDRAW
        {
            public IntPtr hwndFrom; public IntPtr idFrom; public int code;
            public int dwDrawStage; public IntPtr hdc; public RECT rc; public IntPtr dwItemSpec; public int uItemState; public IntPtr lItemlParam;
        }

        private RECT channel;
        private bool hover;

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x204E) // WM_REFLECT + WM_NOTIFY
            {
                try
                {
                    NMCUSTOMDRAW cd = (NMCUSTOMDRAW)Marshal.PtrToStructure(m.LParam, typeof(NMCUSTOMDRAW));
                    if (cd.code == -12) // NM_CUSTOMDRAW
                    {
                        if (cd.dwDrawStage == 1) { m.Result = (IntPtr)0x20; return; }   // prepaint: notify per item
                        if (cd.dwDrawStage == 0x10001)                                   // item prepaint
                        {
                            int item = cd.dwItemSpec.ToInt32();
                            if (item == 1) { m.Result = (IntPtr)4; return; }            // tick marks: skip
                            if (item == 3) { channel = cd.rc; Draw(cd.hdc, cd.rc, false); m.Result = (IntPtr)4; return; }
                            if (item == 2) { Draw(cd.hdc, cd.rc, true); m.Result = (IntPtr)4; return; }
                        }
                    }
                }
                catch { }
            }
            base.WndProc(ref m);
        }

        private void Draw(IntPtr hdc, RECT rc, bool thumb)
        {
            using (Graphics g = Graphics.FromHdc(hdc))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (!thumb)
                {
                    float cy = (rc.Top + rc.Bottom) / 2f, x0 = rc.Left, x1 = rc.Right;
                    using (GraphicsPath p = Theme.Rounded(new RectangleF(x0, cy - 2, x1 - x0, 4), 2))
                    using (SolidBrush br = new SolidBrush(Color.FromArgb(70, 74, 80))) g.FillPath(br, p);
                    float frac = Maximum > Minimum ? (Value - Minimum) / (float)(Maximum - Minimum) : 0f;
                    float xv = x0 + (x1 - x0) * frac;
                    if (xv - x0 >= 1)
                        using (GraphicsPath p = Theme.Rounded(new RectangleF(x0, cy - 2, xv - x0, 4), 2))
                        using (SolidBrush br = new SolidBrush(Enabled ? Theme.Accent : Theme.Disabled)) g.FillPath(br, p);
                }
                else
                {
                    float cx = (rc.Left + rc.Right) / 2f;
                    float cy = channel.Bottom > channel.Top ? (channel.Top + channel.Bottom) / 2f : (rc.Top + rc.Bottom) / 2f;
                    float r = hover || Focused ? 7.5f : 6.5f;
                    using (SolidBrush br = new SolidBrush(Enabled ? Color.White : Theme.Disabled)) g.FillEllipse(br, cx - r, cy - r, 2 * r, 2 * r);
                    using (Pen pen = new Pen(Enabled ? Theme.Accent : Theme.Disabled, 2.5f)) g.DrawEllipse(pen, cx - r + 1.25f, cy - r + 1.25f, 2 * r - 2.5f, 2 * r - 2.5f);
                }
            }
        }
    }
}
