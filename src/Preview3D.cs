using System;
#if !NO_WINFORMS
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Windows.Forms;
#endif

namespace CawPlus
{
    /// <summary>Where the 3D preview looks from: a point on the map it orbits, the compass angle, the angle down and the distance.</summary>
    internal sealed class Camera3D
    {
        public float Tx, Tz;     // the point looked at (map metres)
        public float Yaw;        // direction of view across the map (radians; x = cos, z = sin)
        public float Pitch;      // angle down from level (radians)
        public float Dist;       // metres from the point looked at

        public const float MinPitch = 0.21f, MaxPitch = 1.48f; // 12 to 85 degrees

        /// <summary>The old fixed view: from above the north-west corner, ~35 degrees down, the whole map in sight.</summary>
        public void Reset(float world)
        {
            Tx = Tz = world / 2;
            Yaw = (float)(Math.PI / 4);
            Pitch = 0.62f;
            Dist = world * 1.35f;
        }

        public void Clamp(float world)
        {
            Pitch = Math.Max(MinPitch, Math.Min(MaxPitch, Pitch));
            Dist = Math.Max(Math.Max(40f, world * 0.02f), Math.Min(world * 4f, Dist));
            Tx = Math.Max(-0.25f * world, Math.Min(1.25f * world, Tx));
            Tz = Math.Max(-0.25f * world, Math.Min(1.25f * world, Tz));
        }
    }

    /// <summary>
    /// A 3D view of a map (Generate Terrain, Generate Roads, Distant Terrain): the 2D map (shaded land, water, roads, lots)
    /// draped over the heights and drawn in perspective from any camera (#142). The heights are a grid of triangles drawn
    /// with a depth buffer, the 2D map sampled per pixel (perspective-correct), the map's edge as a wall down to the sea
    /// floor. Pure computation on bitmaps: safe on a worker thread.
    /// </summary>
    internal static class Preview3D
    {
        /// <summary>Lens: focal length in pixels = width x this (about 55 degrees across).</summary>
        public const float Focal = 0.95f;

        /// <summary>
        /// 'colour' = the 2D map (any size, covering the whole grid), 'h' = heights at 'cell' metres, 'exaggerate' =
        /// vertical scale (1 = true heights), 'grid' = vertices along a side (fewer = faster, for dragging).
        /// </summary>
#if !NO_WINFORMS
        public static Bitmap Render(Bitmap colour, Grid h, float cell, float sea, Camera3D cam, float exaggerate, int width, int height, int grid)
        {
            int cw = colour.Width, ch = colour.Height;
            byte[] cols = new byte[cw * ch * 3];
            BitmapData cd = colour.LockBits(new Rectangle(0, 0, cw, ch), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            for (int y = 0; y < ch; y++)
                System.Runtime.InteropServices.Marshal.Copy(new IntPtr(cd.Scan0.ToInt64() + (long)y * cd.Stride), cols, y * cw * 3, cw * 3);
            colour.UnlockBits(cd);
            byte[] outp = RenderBytes(cols, cw, ch, h, cell, sea, cam, exaggerate, width, height, grid);
            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            for (int y = 0; y < height; y++)
                System.Runtime.InteropServices.Marshal.Copy(outp, y * width * 3, new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), width * 3);
            bmp.UnlockBits(bd);
            return bmp;
        }
#endif

        /// <summary>The height the camera orbits round: the land's middle height (exaggerated as drawn).</summary>
        public static float MidHeight(Grid h, float sea, float exaggerate)
        {
            double sum = 0; int n = 0;
            int step = Math.Max(1, h.W / 64);
            for (int y = 0; y < h.H; y += step)
                for (int x = 0; x < h.W; x += step) { sum += Math.Max(sea, h[x, y]); n++; }
            return n > 0 ? (float)(sum / n) * exaggerate : sea * exaggerate;
        }

        // camera frame: forward, right, up and the eye position
        private struct Frame
        {
            public float Fx, Fy, Fz, Rx, Rz, Ux, Uy, Uz, Cx, Cy, Cz;
            public Frame(Camera3D c, float ty)
            {
                float cp = (float)Math.Cos(c.Pitch), sp = (float)Math.Sin(c.Pitch), cy = (float)Math.Cos(c.Yaw), sy = (float)Math.Sin(c.Yaw);
                Fx = cp * cy; Fy = -sp; Fz = cp * sy;
                Rx = -sy; Rz = cy;                    // right is level (the old renderer's convention: right = (-dirZ, dirX))
                Ux = cy * sp; Uy = cp; Uz = sy * sp;  // right x forward
                Cx = c.Tx - Fx * c.Dist; Cy = ty - Fy * c.Dist; Cz = c.Tz - Fz * c.Dist;
            }
        }

        /// <summary>
        /// The point on the level plane at the orbit height under a picture pixel (for zooming towards the cursor);
        /// false when the pixel looks above the horizon.
        /// </summary>
        public static bool GroundUnder(Camera3D c, float ty, float px, float py, int width, int height, out float gx, out float gz)
        {
            Frame f = new Frame(c, ty);
            float focal = width * Focal;
            float a = (px - width / 2f) / focal, b = (height / 2f - py) / focal;
            float dx = f.Fx + f.Rx * a + f.Ux * b, dy = f.Fy + f.Uy * b, dz = f.Fz + f.Rz * a + f.Uz * b;
            gx = gz = 0;
            if (dy > -0.02f) return false;
            float t = (ty - f.Cy) / dy;
            gx = f.Cx + dx * t; gz = f.Cz + dz * t;
            return true;
        }

        /// <summary>As Render, on raw BGR bytes (rows top to bottom, no padding).</summary>
        public static byte[] RenderBytes(byte[] cols, int cw, int ch, Grid h, float cell, float sea, Camera3D cam, float exaggerate, int width, int height, int grid)
        {
            float world = (h.W - 1) * cell;
            float lo, hi; h.MinMax(out lo, out hi);
            float ty = MidHeight(h, sea, exaggerate);
            Frame f = new Frame(cam, ty);
            float focal = width * Focal, cxs = width / 2f, cys = height / 2f;
            const float Near = 1f;

            byte[] outp = new byte[width * height * 3];
            // sky: a dark gradient (the windows' preview background towards a lighter horizon)
            for (int y = 0; y < height; y++)
            {
                float t = y / (float)Math.Max(1, height - 1);
                byte b = (byte)(22 + 22 * t), g = (byte)(20 + 18 * t), r = (byte)(19 + 14 * t);
                for (int x = 0, o = y * width * 3; x < width; x++, o += 3) { outp[o] = b; outp[o + 1] = g; outp[o + 2] = r; }
            }
            float[] zbuf = new float[width * height]; // 1 / depth; 0 = nothing drawn

            // vertices: n x n over the whole grid, plus the wall's foot along the edge
            int n = Math.Max(8, Math.Min(grid, h.W));
            float step = world / (n - 1), baseH = Math.Min(sea, lo) * exaggerate - Math.Max(4f, (hi - lo) * exaggerate * 0.03f);
            int nv = n * n;
            float[] sx = new float[nv], sy = new float[nv], iz = new float[nv], vu = new float[nv], vv = new float[nv];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float x = i * step, z = j * step;
                    float hv = Math.Max(TerrainGen.Sample(h, x / cell, z / cell), sea) * exaggerate;
                    Project(f, x, hv, z, focal, cxs, cys, Near, out sx[j * n + i], out sy[j * n + i], out iz[j * n + i]);
                    vu[j * n + i] = x / world * (cw - 1); vv[j * n + i] = z / world * (ch - 1);
                }
            float fogNear = cam.Dist * 0.6f, fogFar = cam.Dist * 3.5f + world;

            // which way round a front face winds on screen: a level triangle at the orbit point
            float ax, ay, az, bx, by, bz, qx, qy, qz;
            Project(f, cam.Tx, ty, cam.Tz, focal, cxs, cys, Near, out ax, out ay, out az);
            Project(f, cam.Tx + 10, ty, cam.Tz, focal, cxs, cys, Near, out bx, out by, out bz);
            Project(f, cam.Tx, ty, cam.Tz + 10, focal, cxs, cys, Near, out qx, out qy, out qz);
            float frontSign = Math.Sign((bx - ax) * (qy - ay) - (by - ay) * (qx - ax));
            if (frontSign == 0) frontSign = 1;

            for (int j = 0; j < n - 1; j++)
                for (int i = 0; i < n - 1; i++)
                {
                    int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                    Tri(outp, zbuf, width, height, cols, cw, ch, sx, sy, iz, vu, vv, a, b, c, frontSign, 1f, fogNear, fogFar);
                    Tri(outp, zbuf, width, height, cols, cw, ch, sx, sy, iz, vu, vv, b, d, c, frontSign, 1f, fogNear, fogFar);
                }

            // the map's edge: a wall down to below the sea floor, darker, both windings drawn
            float[] wsx = new float[4], wsy = new float[4], wiz = new float[4], wu = new float[4], wv = new float[4];
            for (int side = 0; side < 4; side++)
                for (int k = 0; k < n - 1; k++)
                {
                    int p0, p1;
                    if (side == 0) { p0 = k; p1 = k + 1; }                              // z = 0
                    else if (side == 1) { p0 = (n - 1) * n + k; p1 = p0 + 1; }        // z = world
                    else if (side == 2) { p0 = k * n; p1 = p0 + n; }                    // x = 0
                    else { p0 = k * n + n - 1; p1 = p0 + n; }                           // x = world
                    for (int q = 0; q < 2; q++)
                    {
                        int p = q == 0 ? p0 : p1;
                        int i = p % n, jj = p / n;
                        wsx[q] = sx[p]; wsy[q] = sy[p]; wiz[q] = iz[p]; wu[q] = vu[p]; wv[q] = vv[p];
                        Project(f, i * step, baseH, jj * step, focal, cxs, cys, Near, out wsx[q + 2], out wsy[q + 2], out wiz[q + 2]);
                        wu[q + 2] = vu[p]; wv[q + 2] = vv[p];
                    }
                    Tri(outp, zbuf, width, height, cols, cw, ch, wsx, wsy, wiz, wu, wv, 0, 1, 2, 0, 0.5f, fogNear, fogFar);
                    Tri(outp, zbuf, width, height, cols, cw, ch, wsx, wsy, wiz, wu, wv, 1, 3, 2, 0, 0.5f, fogNear, fogFar);
                }
            return outp;
        }

        private static void Project(Frame f, float x, float y, float z, float focal, float cx, float cy, float near, out float sx, out float sy, out float iz)
        {
            float px = x - f.Cx, py = y - f.Cy, pz = z - f.Cz;
            float zc = px * f.Fx + py * f.Fy + pz * f.Fz;
            if (zc < near) { sx = sy = 0; iz = 0; return; } // behind the camera: the triangle is skipped
            float xc = px * f.Rx + pz * f.Rz, yc = px * f.Ux + py * f.Uy + pz * f.Uz;
            iz = 1f / zc;
            sx = cx + focal * xc * iz;
            sy = cy - focal * yc * iz;
        }

        // one triangle into the depth buffer: map colour sampled per pixel, perspective-correct; 'front' = winding of front
        // faces (0 = draw both), 'dim' darkens (the edge wall)
        private static void Tri(byte[] outp, float[] zbuf, int W, int H, byte[] cols, int cw, int ch,
            float[] sx, float[] sy, float[] iz, float[] vu, float[] vv, int a, int b, int c, float front, float dim, float fogNear, float fogFar)
        {
            if (iz[a] <= 0 || iz[b] <= 0 || iz[c] <= 0) return;
            float x0 = sx[a], y0 = sy[a], x1 = sx[b], y1 = sy[b], x2 = sx[c], y2 = sy[c];
            float area = (x1 - x0) * (y2 - y0) - (y1 - y0) * (x2 - x0);
            if (Math.Abs(area) < 1e-6f) return;
            if (front != 0 && Math.Sign(area) != front) return; // a slope facing away: always hidden on a height field
            int minX = (int)Math.Max(0, Math.Floor(Math.Min(x0, Math.Min(x1, x2))));
            int maxX = (int)Math.Min(W - 1, Math.Ceiling(Math.Max(x0, Math.Max(x1, x2))));
            int minY = (int)Math.Max(0, Math.Floor(Math.Min(y0, Math.Min(y1, y2))));
            int maxY = (int)Math.Min(H - 1, Math.Ceiling(Math.Max(y0, Math.Max(y1, y2))));
            if (minX > maxX || minY > maxY) return;
            float inv = 1f / area;
            float z0 = iz[a], z1 = iz[b], z2 = iz[c];
            float u0 = vu[a] * z0, u1 = vu[b] * z1, u2 = vu[c] * z2, w0 = vv[a] * z0, w1 = vv[b] * z1, w2 = vv[c] * z2;
            const float Eps = -1e-3f;
            for (int y = minY; y <= maxY; y++)
            {
                float py = y + 0.5f;
                for (int x = minX; x <= maxX; x++)
                {
                    float px = x + 0.5f;
                    // barycentric weights (signed by the triangle's own winding, so either winding fills)
                    float l1 = ((px - x0) * (y2 - y0) - (py - y0) * (x2 - x0)) * inv;
                    float l2 = ((x1 - x0) * (py - y0) - (y1 - y0) * (px - x0)) * inv;
                    float l0 = 1 - l1 - l2;
                    if (l0 < Eps || l1 < Eps || l2 < Eps) continue;
                    float z = l0 * z0 + l1 * z1 + l2 * z2;
                    int zi = y * W + x;
                    if (z <= zbuf[zi]) continue;
                    zbuf[zi] = z;
                    float u = (l0 * u0 + l1 * u1 + l2 * u2) / z, v = (l0 * w0 + l1 * w1 + l2 * w2) / z;
                    int ui = (int)u, vi = (int)v;
                    if (ui < 0) ui = 0; else if (ui >= cw) ui = cw - 1;
                    if (vi < 0) vi = 0; else if (vi >= ch) vi = ch - 1;
                    int ci = (vi * cw + ui) * 3;
                    float depth = 1f / z;
                    float fog = depth <= fogNear ? 0 : Math.Min(0.45f, (depth - fogNear) / (fogFar - fogNear) * 0.45f);
                    float k = dim * (1 - fog);
                    int o = zi * 3;
                    outp[o] = (byte)(cols[ci] * k + 44 * fog);
                    outp[o + 1] = (byte)(cols[ci + 1] * k + 38 * fog);
                    outp[o + 2] = (byte)(cols[ci + 2] * k + 33 * fog);
                }
            }
        }
    }

#if !NO_WINFORMS
    /// <summary>
    /// The preview picture of a map window with its 2D / 3D view (#142): drag to orbit, right-drag (or Shift+drag) to pan,
    /// wheel to zoom towards the cursor, double-click to reset; a scale key in the corner. Quick low-detail frames while
    /// moving, a full one when the mouse rests. All on the UI thread with the map the window already holds.
    /// </summary>
    internal sealed class MapView
    {
        private readonly PictureBox pic;
        private readonly CheckBox chk3D, chkExaggerate;
        private readonly Camera3D cam = new Camera3D();
        private readonly Timer settle = new Timer();
        private Bitmap map;            // owned by the window
        private Grid heights;
        private float cell, sea, world;
        private string area;           // "World 2.0 x 2.0 km" or the window's own wording
        private Bitmap shown;          // our last 3D frame (disposed here)
        private bool camSet;
        private MouseButtons dragButton;
        private Point last;
        private float lastMpp;         // metres per picture pixel at the orbit point, for the scale key

        public const int FullSize = 512, DragSize = 256, FullGrid = 257, DragGrid = 97;

        public MapView(PictureBox pic, CheckBox chk3D, CheckBox chkExaggerate)
        {
            this.pic = pic; this.chk3D = chk3D; this.chkExaggerate = chkExaggerate;
            settle.Interval = 160;
            settle.Tick += delegate { settle.Stop(); Redraw(); };
            // the picture takes the keyboard focus under the mouse, so the wheel zooms it instead of turning a slider
            try
            {
                MethodInfo ss = typeof(Control).GetMethod("SetStyle", BindingFlags.Instance | BindingFlags.NonPublic);
                if (ss != null) ss.Invoke(pic, new object[] { ControlStyles.Selectable, true });
            }
            catch { }
            pic.TabStop = false;
            pic.MouseEnter += delegate { if (chk3D.Checked && pic.FindForm() == Form.ActiveForm) pic.Focus(); };
            pic.MouseDown += OnDown;
            pic.MouseMove += OnMove;
            pic.MouseUp += delegate
            {
                if (painting) { painting = false; pic.Capture = false; EventHandler h = PaintDone; if (h != null) h(this, EventArgs.Empty); return; }
                dragButton = MouseButtons.None; pic.Capture = false; settle.Stop(); settle.Start();
            };
            pic.MouseWheel += OnWheel;
            pic.DoubleClick += delegate { if (!chk3D.Checked || heights == null) return; cam.Reset(world); Redraw(); };
            pic.Paint += OnPaint;
            if (chkExaggerate != null) chkExaggerate.CheckedChanged += delegate { if (chk3D.Checked) Redraw(); };
            new ToolTip().SetToolTip(pic, "3D view: drag to orbit, right-drag (or Shift+drag) to pan, wheel to zoom, double-click to reset.");
        }

        private float Exaggeration { get { return chkExaggerate != null && chkExaggerate.Checked ? 2f : 1f; } }

        /// <summary>A new map (the window keeps and disposes the bitmap); the camera stays where it was if the map is the same size.</summary>
        public void SetMap(Bitmap map2D, Grid h, float cellM, float seaLevel, string areaLabel)
        {
            float w = h != null ? (h.W - 1) * cellM : 0;
            map = map2D; heights = h; cell = cellM; sea = seaLevel;
            area = areaLabel;
            if (h != null && (!camSet || Math.Abs(w - world) > 1)) { world = w; cam.Reset(world); camSet = true; }
            world = w > 0 ? w : world;
            Redraw();
        }

        /// <summary>The Turn button: a quarter turn round the point looked at.</summary>
        public void Turn()
        {
            cam.Yaw += (float)(Math.PI / 2);
            if (chk3D.Checked) Redraw();
        }

        /// <summary>Shows the 2D map or a full-detail 3D frame.</summary>
        public void Redraw() { Draw(false); }

        private void Draw(bool quick)
        {
            if (map == null) return;
            Bitmap prev = shown;
            try
            {
                if (chk3D.Checked && heights != null)
                {
                    int s = quick ? DragSize : FullSize;
                    shown = Preview3D.Render(map, heights, cell, sea, cam, Exaggeration, s, s, quick ? DragGrid : FullGrid);
                    pic.Image = shown;
                    lastMpp = cam.Dist / (Preview3D.Focal * DisplayRect().Width);
                }
                else { shown = null; pic.Image = map; }
            }
            catch (Exception ex) { Log.Error("3D preview failed", ex); shown = null; pic.Image = map; }
            // only our own frames are disposed here; the window owns its maps
            if (prev != null && !ReferenceEquals(prev, pic.Image)) prev.Dispose();
        }

        // where the image sits inside the picture box (Zoom: fitted and centred)
        private Rectangle DisplayRect()
        {
            Image im = pic.Image;
            Size cs = pic.ClientSize;
            if (im == null || im.Width <= 0 || im.Height <= 0) return new Rectangle(0, 0, Math.Max(1, cs.Width), Math.Max(1, cs.Height));
            float k = Math.Min(cs.Width / (float)im.Width, cs.Height / (float)im.Height);
            int w = Math.Max(1, (int)(im.Width * k)), hh = Math.Max(1, (int)(im.Height * k));
            return new Rectangle((cs.Width - w) / 2, (cs.Height - hh) / 2, w, hh);
        }

        /// <summary>Painting on the 2D map (the plan painter, #171): (u, v) = map position 0..1, 'first' on the mouse-down.</summary>
        public delegate void PaintFn(float u, float v, bool first);
        /// <summary>Called while the left button is held on the 2D map; null = no painting.</summary>
        public PaintFn Painter;
        /// <summary>Called when a painted stroke ends.</summary>
        public event EventHandler PaintDone;
        private bool painting;

        private bool PaintAt(MouseEventArgs e, bool first)
        {
            if (chk3D.Checked || Painter == null || map == null) return false;
            Rectangle r = DisplayRect();
            Painter((e.X - r.X) / (float)r.Width, (e.Y - r.Y) / (float)r.Height, first);
            return true;
        }

        private void OnDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && PaintAt(e, true)) { painting = true; pic.Capture = true; return; }
            if (!chk3D.Checked || heights == null) return;
            if (pic.FindForm() == Form.ActiveForm) pic.Focus();
            dragButton = e.Button; last = e.Location; pic.Capture = true;
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (painting) { PaintAt(e, false); return; }
            if (dragButton == MouseButtons.None || !chk3D.Checked || heights == null) return;
            int dx = e.X - last.X, dy = e.Y - last.Y;
            if (dx == 0 && dy == 0) return;
            last = e.Location;
            bool pan = dragButton == MouseButtons.Right || dragButton == MouseButtons.Middle || (Control.ModifierKeys & Keys.Shift) != 0;
            if (pan)
            {
                // the ground under the mouse follows it
                float mpp = cam.Dist / (Preview3D.Focal * DisplayRect().Width);
                float fx = (float)Math.Cos(cam.Yaw), fz = (float)Math.Sin(cam.Yaw), rx = -fz, rz = fx;
                float along = dy * mpp / Math.Max(0.35f, (float)Math.Sin(cam.Pitch));
                cam.Tx += -rx * dx * mpp + fx * along;
                cam.Tz += -rz * dx * mpp + fz * along;
            }
            else
            {
                cam.Yaw -= dx * 0.008f;
                cam.Pitch -= dy * 0.006f;
            }
            cam.Clamp(world);
            Draw(true);
            settle.Stop(); settle.Start();
        }

        private void OnWheel(object sender, MouseEventArgs e)
        {
            if (!chk3D.Checked || heights == null || e.Delta == 0) return;
            float k = (float)Math.Pow(0.8, e.Delta / 120.0);
            // towards the point under the cursor: it stays under the cursor
            Rectangle r = DisplayRect();
            float px = (e.X - r.X) / (float)r.Width * FullSize, py = (e.Y - r.Y) / (float)r.Height * FullSize;
            float gx, gz;
            bool hit = Preview3D.GroundUnder(cam, Preview3D.MidHeight(heights, sea, Exaggeration), px, py, FullSize, FullSize, out gx, out gz);
            float oldDist = cam.Dist;
            cam.Dist *= k; cam.Clamp(world);
            float real = cam.Dist / oldDist;
            if (hit)
            {
                cam.Tx = gx + (cam.Tx - gx) * real;
                cam.Tz = gz + (cam.Tz - gz) * real;
            }
            cam.Clamp(world);
            Draw(true);
            settle.Stop(); settle.Start();
        }

        // the scale key: a bar of a round length, the map's size and things to compare with
        private void OnPaint(object sender, PaintEventArgs e)
        {
            if (map == null || pic.Image == null || world <= 0) return;
            try
            {
                Rectangle r = DisplayRect();
                bool in3D = chk3D.Checked && heights != null && ReferenceEquals(pic.Image, shown);
                float mpp = in3D ? lastMpp : world / r.Width; // metres per screen pixel (3D: at the point looked at)
                if (mpp <= 0) return;
                float[] lens = { 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000 };
                float len = lens[0];
                foreach (float l in lens) if (l / mpp <= 130) len = l;
                int bar = Math.Max(4, (int)(len / mpp));
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Font font = new Font("Segoe UI", 7.5f))
                using (SolidBrush box = new SolidBrush(Color.FromArgb(170, 17, 18, 20)))
                using (SolidBrush text = new SolidBrush(Theme.Text))
                using (SolidBrush muted = new SolidBrush(Theme.Muted))
                using (Pen pen = new Pen(Theme.Text, 2))
                {
                    string sizeLine = area ?? string.Format(System.Globalization.CultureInfo.InvariantCulture, "World {0:0.0} x {0:0.0} km", world / 1000f);
                    string refLine = "Road ~12 m wide, lot 30 x 30 m, car 4.5 m, Sim 1.8 m";
                    string barText = (len >= 1000 ? (len / 1000f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " km" : len + " m")
                        + (in3D ? " (at the centre of view)" : "");
                    SizeF s1 = g.MeasureString(sizeLine, font), s2 = g.MeasureString(refLine, font), s3 = g.MeasureString(barText, font);
                    int w = (int)Math.Max(Math.Max(s1.Width, s2.Width), bar + 8 + s3.Width) + 14;
                    int hgt = (int)(s1.Height * 3) + 14;
                    int x0 = r.X + 6, y0 = r.Bottom - hgt - 6;
                    g.FillRectangle(box, x0, y0, w, hgt);
                    int by = y0 + 7 + (int)(s1.Height / 2);
                    g.DrawLine(pen, x0 + 7, by, x0 + 7 + bar, by);
                    g.DrawLine(pen, x0 + 7, by - 4, x0 + 7, by + 4);
                    g.DrawLine(pen, x0 + 7 + bar, by - 4, x0 + 7 + bar, by + 4);
                    g.DrawString(barText, font, text, x0 + 7 + bar + 6, y0 + 6);
                    g.DrawString(sizeLine, font, text, x0 + 7, y0 + 6 + s1.Height);
                    g.DrawString(refLine, font, muted, x0 + 7, y0 + 6 + s1.Height * 2);
                }
            }
            catch { }
        }
    }
#endif
}
