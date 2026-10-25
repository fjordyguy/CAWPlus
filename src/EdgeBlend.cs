using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace CawPlus
{
    /// <summary>
    /// Blends the world's edges into the distant terrain: a grass texture recoloured to the distant terrain's colour,
    /// painted in a band along the four world edges with a ragged inner boundary.
    /// </summary>
    public static class EdgeBlend
    {
        public const string LayerPrefix = "CAWPlus_edge_";

        /// <summary>0..1 paint for the edge layer: 1 at the world edge, fading out 'bandMeters' inwards.</summary>
        public static Grid Mask(Grid h, float cell, float bandMeters, int seed)
        {
            int W = h.W, H = h.H;
            float px = 1f / Math.Max(cell, 1e-3f);
            Grid n = Grid.Fbm(W, H, Math.Max(8f, bandMeters * 0.5f) * px, 4, new Random(seed ^ 0x5EED));
            // slope, so steep cliffs at the edge keep their rock
            Grid hs = h.Blur(1.5f * px);
            Grid m = new Grid(W, H);
            for (int y = 0; y < H; y++)
            {
                int ym = Grid.Clampi(y - 1, 0, H - 1), yp = Grid.Clampi(y + 1, 0, H - 1);
                for (int x = 0; x < W; x++)
                {
                    int xm = Grid.Clampi(x - 1, 0, W - 1), xp = Grid.Clampi(x + 1, 0, W - 1);
                    float gx = (hs[xp, y] - hs[xm, y]) / ((xp - xm) * cell), gy = (hs[x, yp] - hs[x, ym]) / ((yp - ym) * cell);
                    float slope = (float)(Math.Atan(Math.Sqrt(gx * gx + gy * gy)) * 180.0 / Math.PI);
                    float d = Math.Min(Math.Min(x + 0.5f, W - x - 0.5f), Math.Min(y + 0.5f, H - y - 0.5f)) * cell;
                    int i = y * W + x;
                    float wander = (n.D[i] - 0.5f) * 0.7f * bandMeters;
                    float v = 1 - Grid.Ramp(d + wander, 0.25f * bandMeters, bandMeters);
                    m.D[i] = v * (1 - 0.6f * Grid.Ramp(slope, 30, 45));
                }
            }
            Grid b = m.Blur(2f * px);
            b.Clamp01();
            return b;
        }

        /// <summary>
        /// Writes a copy of 'sourcePath' recoloured so it reads as 'distant'. If 'ground' (your own ground, picked from
        /// the same 3D view) is given, the change is distant/ground, which cancels CAW's lighting and fog; otherwise
        /// the texture's average colour is moved to 'distant'. Returns the new file path.
        /// </summary>
        public static string MakeTexture(string sourcePath, Color distant, Color ground, bool hasGround, string outDir)
        {
            int w, h;
            int[] px = LoadPixels(sourcePath, out w, out h);
            if (px == null) throw new InvalidDataException("Could not read " + Path.GetFileName(sourcePath));
            double mr = 0, mg = 0, mb = 0;
            for (int i = 0; i < px.Length; i++) { mr += (px[i] >> 16) & 255; mg += (px[i] >> 8) & 255; mb += px[i] & 255; }
            mr /= px.Length; mg /= px.Length; mb /= px.Length;
            double gr, gg, gb;
            if (hasGround)
            {
                gr = (distant.R + 1.0) / (ground.R + 1.0); gg = (distant.G + 1.0) / (ground.G + 1.0); gb = (distant.B + 1.0) / (ground.B + 1.0);
            }
            else
            {
                gr = (distant.R + 1.0) / (mr + 1); gg = (distant.G + 1.0) / (mg + 1); gb = (distant.B + 1.0) / (mb + 1);
            }
            gr = Clamp(gr, 0.25, 4); gg = Clamp(gg, 0.25, 4); gb = Clamp(gb, 0.25, 4);

            string hex = string.Format("{0:X2}{1:X2}{2:X2}", distant.R, distant.G, distant.B);
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, LayerPrefix + hex + "_" + Path.GetFileNameWithoutExtension(sourcePath) + ".png");
            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb))
            {
                BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                byte[] row = new byte[bd.Stride];
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int c = px[y * w + x];
                        row[x * 3 + 2] = B(((c >> 16) & 255) * gr);
                        row[x * 3 + 1] = B(((c >> 8) & 255) * gg);
                        row[x * 3] = B((c & 255) * gb);
                    }
                    Marshal.Copy(row, 0, new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), bd.Stride);
                }
                bmp.UnlockBits(bd);
                bmp.Save(outPath, ImageFormat.Png);
            }
            Log.Info(string.Format("Edge texture {0}: gains R {1:F2} G {2:F2} B {3:F2} from {4}", outPath, gr, gg, gb, sourcePath));
            return outPath;
        }

        /// <summary>Average colour of an image file (DDS, PNG, JPG, BMP).</summary>
        public static Color AverageColour(string path)
        {
            TextureFeatures f = TextureAnalyzer.Analyze(path);
            if (f == null) throw new InvalidDataException("Could not read " + Path.GetFileName(path));
            return Color.FromArgb(B(f.R * 255), B(f.G * 255), B(f.B * 255));
        }

        /// <summary>Average colour of a square around (x, y) in a bitmap.</summary>
        public static Color AverageColour(Bitmap bmp, int cx, int cy, int radius)
        {
            long r = 0, g = 0, b = 0; int n = 0;
            for (int y = Math.Max(0, cy - radius); y <= Math.Min(bmp.Height - 1, cy + radius); y++)
                for (int x = Math.Max(0, cx - radius); x <= Math.Min(bmp.Width - 1, cx + radius); x++)
                {
                    Color c = bmp.GetPixel(x, y); r += c.R; g += c.G; b += c.B; n++;
                }
            return n == 0 ? Color.Gray : Color.FromArgb((int)(r / n), (int)(g / n), (int)(b / n));
        }

        private static int[] LoadPixels(string path, out int w, out int h)
        {
            if (Path.GetExtension(path).ToLowerInvariant() == ".dds") return Dds.Decode(path, 1024, out w, out h);
            using (Bitmap src = new Bitmap(path))
            {
                w = Math.Min(src.Width, 1024); h = Math.Min(src.Height, 1024);
                using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp)) g.DrawImage(src, 0, 0, w, h);
                    BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    int[] px = new int[w * h];
                    for (int y = 0; y < h; y++)
                        Marshal.Copy(new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), px, y * w, w);
                    bmp.UnlockBits(bd);
                    return px;
                }
            }
        }

        private static double Clamp(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }
        private static byte B(double v) { return (byte)(v < 0 ? 0 : (v > 255 ? 255 : v + 0.5)); }
    }
}
